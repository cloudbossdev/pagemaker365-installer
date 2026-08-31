using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PageMaker365.Installer.Engine.Models;
using PageMaker365.Installer.Engine.PowerShell;

namespace PageMaker365.Installer.Engine.Services;

/// <summary>
/// Default-denied test runner for the installer lifecycle. It exercises the
/// same public <see cref="InstallerEngine"/> phases used by the desktop app,
/// but its process boundary is an in-memory fixture and it never authenticates
/// to, discovers, or mutates Azure/Microsoft 365.
/// </summary>
public sealed class FixtureLifecycleRunner
{
    public const string RunnerContractVersion = "pagemaker365.installer-lifecycle-runner.fixture.v1";
    public const string EnableEnvironmentVariable = "PM365_ENABLE_FIXTURE_LIFECYCLE_RUNNER";
    private static readonly Regex RawDigest = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant);
    private static readonly Regex SafeToken = new("^[A-Za-z0-9._:-]{1,220}$", RegexOptions.CultureInvariant);

    private readonly StructuredLogger _logger;

    public FixtureLifecycleRunner(StructuredLogger logger)
    {
        _logger = logger;
    }

    public async Task<FixtureLifecycleRunnerResult> RunAsync(
        FixtureLifecycleRunnerRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateSafetyGuard(request);
        var bootstrap = await ValidateBootstrapAsync(request, cancellationToken);

        var result = new FixtureLifecycleRunnerResult
        {
            ContractVersion = RunnerContractVersion,
            FixtureOnly = true,
            CloudMutation = "denied",
            RunId = request.RunId,
            RuntimeDeliveryStatus = "blocked",
            RuntimeDeliveryBlockerCode = "RUNTIME_DELIVERY_CONTRACT_PENDING"
        };

        var outputRoot = RequireDirectory(request.OutputRoot, "fixture output root");
        var outbox = new FixtureLifecycleEvidenceState();
        var callback = new FixtureEvidenceCallback(request.InducePortalOutageOnce);
        var firstInstall = await ExecuteInstallAsync(
            request,
            bootstrap,
            outputRoot,
            outbox,
            callback,
            induceDeploymentFailure: request.Scenario.Equals(FixtureLifecycleScenario.FailureRecoveryReinstallUninstall, StringComparison.Ordinal),
            executionLabel: "initial",
            result,
            cancellationToken);

        if (!firstInstall && !request.Scenario.Equals(FixtureLifecycleScenario.FailureRecoveryReinstallUninstall, StringComparison.Ordinal))
        {
            CompleteResult(result, outbox, callback, "failed");
            return result;
        }

        if (!firstInstall)
        {
            result.RecoveryCode = "FIXTURE_DEPLOYMENT_FAILURE_RECOVERED";
            StartNewInstallAttempt(outbox);
            var recovered = await ExecuteInstallAsync(
                request,
                bootstrap,
                outputRoot,
                outbox,
                callback,
                induceDeploymentFailure: false,
                executionLabel: "reinstall",
                result,
                cancellationToken);
            if (!recovered)
            {
                CompleteResult(result, outbox, callback, "failed");
                return result;
            }
        }

        var removalSucceeded = await ExecuteRemovalAsync(request, bootstrap, outputRoot, outbox, callback, result, cancellationToken);
        await FlushEvidenceAsync(outbox, callback, outputRoot, cancellationToken);
        CompleteResult(result, outbox, callback, !removalSucceeded ? "failed" : outbox.PendingEvents.Count == 0 ? "passed" : "blocked");
        return result;
    }

    private async Task<RuntimeBootstrapEnvelopeValidationResult> ValidateBootstrapAsync(
        FixtureLifecycleRunnerRequest request,
        CancellationToken cancellationToken)
    {
        var bootstrapPath = RequireExistingFile(request.RuntimeBootstrapPath, "runtime bootstrap");
        var envelope = await File.ReadAllTextAsync(bootstrapPath, cancellationToken);
        return new FixtureRuntimeBootstrapEnvelopeValidator().ValidateJson(envelope, new RuntimeBootstrapEnvelopeBinding
        {
            PackagePayloadSha256 = request.VerifiedPackagePayloadSha256,
            CustomerId = request.CustomerId,
            TenantId = request.TenantId,
            InstallationId = request.InstallationId,
            EnvironmentId = request.EnvironmentId,
            DeploymentExportId = request.DeploymentExportId,
            RuntimeReleaseId = request.RuntimeReleaseId
        });
    }

    private async Task<bool> ExecuteInstallAsync(
        FixtureLifecycleRunnerRequest request,
        RuntimeBootstrapEnvelopeValidationResult bootstrap,
        string outputRoot,
        FixtureLifecycleEvidenceState outbox,
        FixtureEvidenceCallback callback,
        bool induceDeploymentFailure,
        string executionLabel,
        FixtureLifecycleRunnerResult result,
        CancellationToken cancellationToken)
    {
        var workspaceRoot = Path.Combine(outputRoot, "fixture-workspace", executionLabel);
        await PrepareFixtureWorkspaceAsync(workspaceRoot, cancellationToken);
        var config = CreateFixtureConfig(request);
        var configPath = Path.Combine(workspaceRoot, "fixture.customer.install.json");
        await File.WriteAllTextAsync(configPath, "{}", new UTF8Encoding(false), cancellationToken);

        var fixtureRunner = new FixturePowerShellProcessRunner(induceDeploymentFailure);
        var engine = new InstallerEngine(_logger, powerShellRunner: fixtureRunner);
        var session = engine.CreateSession(config, workspaceRoot, request.VerifiedPackagePayloadSha256);
        var outputPath = Path.Combine(session.LogDirectory, "fixture-stage.json");

        AddStage(result, "Acquire", "passed", "RUNTIME_BOOTSTRAP_BOUND", bootstrap.IdempotencyKey, "not_applicable", "", "");
        QueueInstallEvidence(outbox, config, InstallerEvidenceEventType.PackageValidated, "provisioning", "passed", "Package binding validated.");
        await FlushEvidenceAsync(outbox, callback, outputRoot, cancellationToken);

        if (!await RunStageAsync(result, "Preflight", "PREFLIGHT", () => engine.RunPowerShellPreflightAsync(session, workspaceRoot, configPath, cancellationToken: cancellationToken), outbox, callback, outputRoot, cancellationToken))
        {
            QueueInstallFailure(outbox, config, "PREFLIGHT_FAILED");
            await FlushEvidenceAsync(outbox, callback, outputRoot, cancellationToken);
            return false;
        }

        if (!await RunStageAsync(result, "Preview", "PREVIEW", () => engine.RunWhatIfAsync(session, workspaceRoot, configPath, outputPath, cancellationToken: cancellationToken), outbox, callback, outputRoot, cancellationToken))
        {
            QueueInstallFailure(outbox, config, "PREVIEW_FAILED");
            await FlushEvidenceAsync(outbox, callback, outputRoot, cancellationToken);
            return false;
        }

        QueueInstallEvidence(outbox, config, InstallerEvidenceEventType.InstallStarted, "provisioning", "passed", "Fixture provisioning started.");
        await FlushEvidenceAsync(outbox, callback, outputRoot, cancellationToken);
        if (!await RunStageAsync(result, "Deploy", "DEPLOY", () => engine.RunDeploymentAsync(session, workspaceRoot, configPath, outputPath, cancellationToken: cancellationToken), outbox, callback, outputRoot, cancellationToken))
        {
            QueueInstallFailure(outbox, config, "DEPLOYMENT_FAILED");
            await FlushEvidenceAsync(outbox, callback, outputRoot, cancellationToken);
            return false;
        }

        QueueInstallEvidence(outbox, config, InstallerEvidenceEventType.AzureDeploymentCompleted, "provisioning", "passed", "Fixture deployment completed.");
        await FlushEvidenceAsync(outbox, callback, outputRoot, cancellationToken);
        if (!await RunStageAsync(result, "Configure", "CONFIGURE", () => engine.RunRuntimeConfigurationAsync(session, workspaceRoot, configPath, [], outputPath, cancellationToken: cancellationToken), outbox, callback, outputRoot, cancellationToken))
        {
            QueueInstallFailure(outbox, config, "RUNTIME_CONFIGURATION_FAILED");
            await FlushEvidenceAsync(outbox, callback, outputRoot, cancellationToken);
            return false;
        }

        QueueInstallEvidence(outbox, config, InstallerEvidenceEventType.RuntimeConfigured, "provisioning", "passed", "Fixture runtime configuration completed.");
        await FlushEvidenceAsync(outbox, callback, outputRoot, cancellationToken);
        if (!await RunStageAsync(result, "Validate", "VALIDATE", () => engine.RunValidationAsync(session, workspaceRoot, configPath, cancellationToken: cancellationToken), outbox, callback, outputRoot, cancellationToken))
        {
            QueueInstallEvidence(outbox, config, InstallerEvidenceEventType.SmokeTestsCompleted, "failed", "failed", "Fixture smoke validation failed.", "SMOKE_TESTS_FAILED");
            QueueInstallFailure(outbox, config, "SMOKE_TESTS_FAILED");
            await FlushEvidenceAsync(outbox, callback, outputRoot, cancellationToken);
            return false;
        }

        QueueInstallEvidence(outbox, config, InstallerEvidenceEventType.SmokeTestsCompleted, "provisioning", "passed", "Fixture smoke validation completed.");
        QueueInstallEvidence(outbox, config, InstallerEvidenceEventType.InstallCompleted, "completed", "passed", "Fixture install completed.");
        await FlushEvidenceAsync(outbox, callback, outputRoot, cancellationToken);
        AddStage(result, "Evidence", outbox.PendingEvents.Count == 0 ? "passed" : "blocked", "INSTALL_EVIDENCE", "", callback.LastReceiptStatus, outbox.PendingEvents.Count == 0 ? "" : "EVIDENCE_CALLBACK_PENDING", "");
        return outbox.PendingEvents.Count == 0;
    }

    private async Task<bool> ExecuteRemovalAsync(
        FixtureLifecycleRunnerRequest request,
        RuntimeBootstrapEnvelopeValidationResult bootstrap,
        string outputRoot,
        FixtureLifecycleEvidenceState outbox,
        FixtureEvidenceCallback callback,
        FixtureLifecycleRunnerResult result,
        CancellationToken cancellationToken)
    {
        var workspaceRoot = Path.Combine(outputRoot, "fixture-workspace", "removal");
        await PrepareFixtureWorkspaceAsync(workspaceRoot, cancellationToken);
        var config = CreateFixtureConfig(request);
        var configPath = Path.Combine(workspaceRoot, "fixture.customer.install.json");
        await File.WriteAllTextAsync(configPath, "{}", new UTF8Encoding(false), cancellationToken);
        var engine = new InstallerEngine(_logger, powerShellRunner: new FixturePowerShellProcessRunner(false));
        var session = engine.CreateSession(config, workspaceRoot, request.VerifiedPackagePayloadSha256);
        var outputPath = Path.Combine(session.LogDirectory, "fixture-removal.json");
        var removal = new RemovalEvidenceLifecycleService();
        var removalState = new RemovalEvidenceOutboxState();
        removal.StartNewAttempt(removalState);

        QueueRemovalEvidence(removal, removalState, config, InstallerEvidenceEventType.RemovalStarted, "removing", "passed", "Fixture removal started.");
        if (!await RunRemovalStageAsync(result, "Remove", "REMOVAL_INVENTORY", () => engine.RunRemovalInventoryAsync(session, workspaceRoot, configPath, outputPath, cancellationToken: cancellationToken), bootstrap.IdempotencyKey, callback.LastReceiptStatus, cancellationToken))
        {
            return await CompleteFailedRemovalAsync(removal, removalState, config, outbox, callback, outputRoot, result, cancellationToken);
        }
        QueueRemovalEvidence(removal, removalState, config, InstallerEvidenceEventType.RemovalInventoryCompleted, "removing", "passed", "Fixture removal inventory completed.");
        if (!await RunRemovalStageAsync(result, "Remove", "REMOVAL_EXECUTION", () => engine.RunRemovalAsync(session, workspaceRoot, configPath, request.Confirmation, outputPath, cancellationToken: cancellationToken), bootstrap.IdempotencyKey, callback.LastReceiptStatus, cancellationToken))
        {
            return await CompleteFailedRemovalAsync(removal, removalState, config, outbox, callback, outputRoot, result, cancellationToken);
        }
        QueueRemovalEvidence(removal, removalState, config, InstallerEvidenceEventType.RemovalExecutionCompleted, "removing", "passed", "Fixture removal execution completed.");
        if (!await RunRemovalStageAsync(result, "Remove", "REMOVAL_VALIDATION", () => engine.RunRemovalValidationAsync(session, workspaceRoot, configPath, outputPath, cancellationToken: cancellationToken), bootstrap.IdempotencyKey, callback.LastReceiptStatus, cancellationToken))
        {
            return await CompleteFailedRemovalAsync(removal, removalState, config, outbox, callback, outputRoot, result, cancellationToken);
        }
        QueueRemovalEvidence(removal, removalState, config, InstallerEvidenceEventType.RemovalValidationCompleted, "removing", "passed", "Fixture removal validation completed.");
        QueueRemovalEvidence(removal, removalState, config, InstallerEvidenceEventType.RemovalCompleted, "removed", "passed", "Fixture removal completed.");

        CopyRemovalOutbox(removalState, outbox);
        await FlushEvidenceAsync(outbox, callback, outputRoot, cancellationToken);
        AddStage(result, "Evidence", outbox.PendingEvents.Count == 0 ? "passed" : "blocked", "REMOVAL_EVIDENCE", "", callback.LastReceiptStatus, outbox.PendingEvents.Count == 0 ? "" : "EVIDENCE_CALLBACK_PENDING", "");
        return outbox.PendingEvents.Count == 0;
    }

    private static async Task<bool> RunStageAsync(
        FixtureLifecycleRunnerResult result,
        string stage,
        string code,
        Func<Task<IReadOnlyList<InstallerStepResult>>> action,
        FixtureLifecycleEvidenceState outbox,
        FixtureEvidenceCallback callback,
        string outputRoot,
        CancellationToken cancellationToken)
    {
        var steps = await action();
        var last = steps.LastOrDefault();
        var passed = last is not null && last.Status is InstallStatus.Passed or InstallStatus.Warning;
        AddStage(result, stage, passed ? "passed" : "failed", code, "", callback.LastReceiptStatus, passed ? "" : last?.Code ?? "FIXTURE_ENGINE_STAGE_FAILED", "");
        await PersistEvidenceStateAsync(outbox, outputRoot, cancellationToken);
        return passed;
    }

    private static async Task<bool> RunRemovalStageAsync(
        FixtureLifecycleRunnerResult result,
        string stage,
        string code,
        Func<Task<IReadOnlyList<InstallerStepResult>>> action,
        string correlationReference,
        string receiptStatus,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var steps = await action();
        var last = steps.LastOrDefault();
        var passed = last?.Status is InstallStatus.Passed or InstallStatus.Warning;
        AddStage(result, stage, passed ? "passed" : "failed", code, correlationReference, receiptStatus, passed ? "" : last?.Code ?? "FIXTURE_REMOVAL_STAGE_FAILED", "");
        return passed;
    }

    private static void QueueInstallEvidence(
        FixtureLifecycleEvidenceState state,
        CustomerInstallConfig config,
        string eventType,
        string lifecycleStatus,
        string outcome,
        string message,
        string errorCode = "")
    {
        if (string.IsNullOrWhiteSpace(state.InstallAttemptId))
        {
            state.InstallAttemptId = $"ia_fixture_{Guid.NewGuid():N}";
            state.NextInstallSequence = 1;
        }

        if (state.InstallTerminal)
        {
            throw new InvalidOperationException("A terminal fixture install attempt cannot queue more evidence.");
        }

        ValidateInstallTransition(state, eventType);
        var payload = new InstallerEvidenceEvent
        {
            Lifecycle = InstallerEvidenceLifecycle.Install,
            AttemptId = state.InstallAttemptId,
            InstallAttemptId = state.InstallAttemptId,
            EventId = $"evt_{Guid.NewGuid():N}",
            EventType = eventType,
            Sequence = state.NextInstallSequence++,
            OnboardingSessionId = config.ControlPlane.OnboardingSessionId,
            DeploymentExportId = config.ControlPlane.DeploymentExportId,
            LifecycleStatus = lifecycleStatus,
            Outcome = outcome,
            InstallerVersion = "fixture-lifecycle-runner",
            PackageHash = "sha256:" + config.ControlPlane.PackageHash,
            AzureResourceGroup = config.Azure.ResourceGroupName,
            Message = message,
            Error = string.IsNullOrWhiteSpace(errorCode) ? null : new InstallerEvidenceError
            {
                Code = errorCode,
                Category = "fixture",
                Message = "Fixture lifecycle stage did not complete.",
                Retryable = true
            }
        };
        state.PendingEvents.Add(new PendingInstallerEvidenceEvent
        {
            IdempotencyKey = $"{state.InstallAttemptId}:{payload.Sequence}:{payload.EventId}",
            Payload = payload
        });
        state.LastInstallEventType = eventType;
        state.InstallTerminal = eventType is InstallerEvidenceEventType.PackageValidationFailed or InstallerEvidenceEventType.InstallFailed or InstallerEvidenceEventType.InstallCompleted;
    }

    private static void StartNewInstallAttempt(FixtureLifecycleEvidenceState state)
    {
        if (state.PendingEvents.Count != 0)
        {
            throw new InvalidOperationException("Fixture recovery cannot begin while prior evidence remains pending.");
        }

        state.InstallAttemptId = "";
        state.NextInstallSequence = 1;
        state.LastInstallEventType = "";
        state.InstallTerminal = false;
    }

    private static void QueueInstallFailure(FixtureLifecycleEvidenceState state, CustomerInstallConfig config, string errorCode) =>
        QueueInstallEvidence(state, config, InstallerEvidenceEventType.InstallFailed, "failed", "failed", "Fixture install failed.", errorCode);

    private static void ValidateInstallTransition(FixtureLifecycleEvidenceState state, string eventType)
    {
        var valid = eventType switch
        {
            InstallerEvidenceEventType.PackageValidated or InstallerEvidenceEventType.PackageValidationFailed => string.IsNullOrWhiteSpace(state.LastInstallEventType),
            InstallerEvidenceEventType.InstallStarted => state.LastInstallEventType == InstallerEvidenceEventType.PackageValidated,
            InstallerEvidenceEventType.AzureDeploymentCompleted => state.LastInstallEventType == InstallerEvidenceEventType.InstallStarted,
            InstallerEvidenceEventType.RuntimeConfigured => state.LastInstallEventType == InstallerEvidenceEventType.AzureDeploymentCompleted,
            InstallerEvidenceEventType.SmokeTestsCompleted => state.LastInstallEventType == InstallerEvidenceEventType.RuntimeConfigured,
            InstallerEvidenceEventType.InstallCompleted => state.LastInstallEventType == InstallerEvidenceEventType.SmokeTestsCompleted,
            InstallerEvidenceEventType.InstallFailed => !string.IsNullOrWhiteSpace(state.LastInstallEventType),
            _ => false
        };
        if (!valid)
        {
            throw new InvalidOperationException($"Fixture install evidence event '{eventType}' is not valid after '{state.LastInstallEventType}'.");
        }
    }

    private static void QueueRemovalEvidence(
        RemovalEvidenceLifecycleService service,
        RemovalEvidenceOutboxState state,
        CustomerInstallConfig config,
        string eventType,
        string lifecycleStatus,
        string outcome,
        string message)
    {
        service.Queue(state, new InstallerEvidenceEvent
        {
            EventType = eventType,
            OnboardingSessionId = config.ControlPlane.OnboardingSessionId,
            DeploymentExportId = config.ControlPlane.DeploymentExportId,
            LifecycleStatus = lifecycleStatus,
            Outcome = outcome,
            InstallerVersion = "fixture-lifecycle-runner",
            PackageHash = "sha256:" + config.ControlPlane.PackageHash,
            AzureResourceGroup = config.Azure.ResourceGroupName,
            Message = message,
            RemovalOutcomes = new RemovalEvidenceOutcomeSummary()
        });
    }

    private static async Task<bool> CompleteFailedRemovalAsync(
        RemovalEvidenceLifecycleService service,
        RemovalEvidenceOutboxState removalState,
        CustomerInstallConfig config,
        FixtureLifecycleEvidenceState outbox,
        FixtureEvidenceCallback callback,
        string outputRoot,
        FixtureLifecycleRunnerResult result,
        CancellationToken cancellationToken)
    {
        service.Queue(removalState, new InstallerEvidenceEvent
        {
            EventType = InstallerEvidenceEventType.RemovalFailed,
            OnboardingSessionId = config.ControlPlane.OnboardingSessionId,
            DeploymentExportId = config.ControlPlane.DeploymentExportId,
            LifecycleStatus = "failed",
            Outcome = "failed",
            InstallerVersion = "fixture-lifecycle-runner",
            PackageHash = "sha256:" + config.ControlPlane.PackageHash,
            AzureResourceGroup = config.Azure.ResourceGroupName,
            Message = "Fixture removal did not complete.",
            Error = new InstallerEvidenceError
            {
                Code = "FIXTURE_REMOVAL_STAGE_FAILED",
                Category = "fixture",
                Message = "Fixture removal stage did not complete.",
                Retryable = true
            },
            RemovalOutcomes = new RemovalEvidenceOutcomeSummary { Failed = 1 }
        });
        CopyRemovalOutbox(removalState, outbox);
        await FlushEvidenceAsync(outbox, callback, outputRoot, cancellationToken);
        AddStage(result, "Evidence", "failed", "REMOVAL_EVIDENCE", "", callback.LastReceiptStatus, "REMOVAL_STAGE_FAILED", "");
        return false;
    }

    private static void CopyRemovalOutbox(RemovalEvidenceOutboxState removalState, FixtureLifecycleEvidenceState outbox)
    {
        foreach (var pending in removalState.PendingEvents)
        {
            outbox.PendingEvents.Add(pending);
        }
    }

    private static async Task FlushEvidenceAsync(
        FixtureLifecycleEvidenceState state,
        FixtureEvidenceCallback callback,
        string outputRoot,
        CancellationToken cancellationToken)
    {
        while (state.PendingEvents.Count > 0)
        {
            var pending = state.PendingEvents[0];
            pending.DeliveryAttempts++;
            pending.LastAttemptAt = DateTimeOffset.UtcNow;
            if (!callback.TryAccept(pending, out var receiptStatus))
            {
                pending.LastDeliveryStatus = "RetryPending";
                callback.LastReceiptStatus = receiptStatus;
                await PersistEvidenceStateAsync(state, outputRoot, cancellationToken);
                return;
            }

            state.PendingEvents.RemoveAt(0);
            callback.LastReceiptStatus = receiptStatus;
            await PersistEvidenceStateAsync(state, outputRoot, cancellationToken);
        }
    }

    private static async Task PersistEvidenceStateAsync(FixtureLifecycleEvidenceState state, string outputRoot, CancellationToken cancellationToken)
    {
        var statePath = Path.Combine(outputRoot, "fixture-lifecycle-evidence-state.json");
        var safeState = new
        {
            contractVersion = RunnerContractVersion,
            fixtureOnly = true,
            cloudMutation = "denied",
            installAttemptReference = state.InstallAttemptId,
            pendingEvidenceCount = state.PendingEvents.Count,
            pending = state.PendingEvents.Select(item => new
            {
                eventType = item.Payload.EventType,
                sequence = item.Payload.Sequence,
                idempotencyKey = item.IdempotencyKey,
                deliveryAttempts = item.DeliveryAttempts,
                deliveryStatus = item.LastDeliveryStatus
            }).ToArray()
        };
        await File.WriteAllTextAsync(statePath, JsonSerializer.Serialize(safeState), new UTF8Encoding(false), cancellationToken);
    }

    private static CustomerInstallConfig CreateFixtureConfig(FixtureLifecycleRunnerRequest request) => new()
    {
        Customer = new CustomerInfo
        {
            CustomerId = request.CustomerId,
            InstallationId = request.InstallationId,
            TenantId = request.TenantId
        },
        Azure = new AzureInfo
        {
            TenantId = request.TenantId,
            SubscriptionId = request.SubscriptionId,
            ResourceGroupName = request.ResourceGroupName,
            Environment = request.Environment
        },
        ControlPlane = new ControlPlaneInfo
        {
            OnboardingSessionId = request.OnboardingSessionId,
            DeploymentExportId = request.DeploymentExportId,
            PackageHash = request.VerifiedPackagePayloadSha256
        },
        RuntimeArtifacts = new RuntimeArtifactContract { ReleaseId = request.RuntimeReleaseId }
    };

    private static async Task PrepareFixtureWorkspaceAsync(string workspaceRoot, CancellationToken cancellationToken)
    {
        var moduleDirectory = Path.Combine(workspaceRoot, "modules", "PageMaker365.Install");
        Directory.CreateDirectory(moduleDirectory);
        var modulePath = Path.Combine(moduleDirectory, "PageMaker365.Install.psd1");
        if (!File.Exists(modulePath))
        {
            await File.WriteAllTextAsync(modulePath, "@{ RootModule = 'fixture-only' }", new UTF8Encoding(false), cancellationToken);
        }
    }

    private static void AddStage(
        FixtureLifecycleRunnerResult result,
        string stage,
        string status,
        string code,
        string correlationReference,
        string receiptStatus,
        string blockerCode,
        string recoveryCode)
    {
        result.Stages.Add(new FixtureLifecycleRunnerStage
        {
            Stage = stage,
            Status = status,
            Code = code,
            SafeCorrelationReference = correlationReference,
            ReceiptStatus = receiptStatus,
            BlockerCode = blockerCode,
            RecoveryCode = recoveryCode
        });
    }

    private static void CompleteResult(
        FixtureLifecycleRunnerResult result,
        FixtureLifecycleEvidenceState outbox,
        FixtureEvidenceCallback callback,
        string status)
    {
        result.Status = status;
        result.PendingEvidenceCount = outbox.PendingEvents.Count;
        result.ReceiptStatus = callback.LastReceiptStatus;
        if (outbox.PendingEvents.Count > 0)
        {
            result.BlockerCode = "EVIDENCE_CALLBACK_PENDING";
        }
    }

    private static void ValidateSafetyGuard(FixtureLifecycleRunnerRequest request)
    {
        if (!request.ContractVersion.Equals(RunnerContractVersion, StringComparison.Ordinal) ||
            !request.FixtureOnly || !request.TestOnlyEnabled || request.AllowCloudMutation ||
            !string.Equals(Environment.GetEnvironmentVariable(EnableEnvironmentVariable), "1", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Fixture lifecycle runner is disabled. Explicit fixture-only enablement is required.");
        }
        if (!request.Environment.Equals("disposable-sandbox", StringComparison.Ordinal) ||
            request.Environment.Contains("production", StringComparison.OrdinalIgnoreCase) ||
            request.Environment.Contains("customer", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Fixture lifecycle runner accepts only the disposable-sandbox environment.");
        }
        if (!SafeToken.IsMatch(request.RunId) || !SafeToken.IsMatch(request.SubscriptionId) ||
            !SafeToken.IsMatch(request.CustomerId) || !SafeToken.IsMatch(request.TenantId) ||
            !SafeToken.IsMatch(request.InstallationId) || !SafeToken.IsMatch(request.EnvironmentId) ||
            !SafeToken.IsMatch(request.DeploymentExportId) || !SafeToken.IsMatch(request.RuntimeReleaseId) ||
            !SafeToken.IsMatch(request.OnboardingSessionId) || !RawDigest.IsMatch(request.VerifiedPackagePayloadSha256))
        {
            throw new InvalidDataException("Fixture lifecycle runner contains an invalid safe identifier or package digest.");
        }
        if (request.AllowedSubscriptionIds.Count == 0 || !request.AllowedSubscriptionIds.Contains(request.SubscriptionId, StringComparer.Ordinal))
        {
            throw new InvalidDataException("Fixture lifecycle runner subscription is not explicitly allowlisted.");
        }
        if (string.IsNullOrWhiteSpace(request.ResourceGroupName) ||
            !request.ResourceGroupName.StartsWith("rg-pm365-harness-", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Fixture lifecycle runner resource group is outside the harness boundary.");
        }
        if (!request.Confirmation.Equals($"RUN-FIXTURE-LIFECYCLE:{request.RunId}", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Fixture lifecycle runner confirmation does not match the requested run.");
        }
        if (!request.Scenario.Equals(FixtureLifecycleScenario.InstallUninstall, StringComparison.Ordinal) &&
            !request.Scenario.Equals(FixtureLifecycleScenario.FailureRecoveryReinstallUninstall, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Fixture lifecycle runner scenario is unsupported.");
        }
    }

    private static string RequireExistingFile(string value, string label)
    {
        var path = Path.GetFullPath(value);
        if (!File.Exists(path)) throw new FileNotFoundException($"Fixture lifecycle {label} was not found.");
        return path;
    }

    private static string RequireDirectory(string value, string label)
    {
        var path = Path.GetFullPath(value);
        if (!Directory.Exists(path)) throw new DirectoryNotFoundException($"Fixture lifecycle {label} does not exist.");
        return path;
    }

    private sealed class FixturePowerShellProcessRunner : IPowerShellProcessRunner
    {
        private readonly bool _induceDeploymentFailure;

        public FixturePowerShellProcessRunner(bool induceDeploymentFailure)
        {
            _induceDeploymentFailure = induceDeploymentFailure;
        }

        public Task<PowerShellExecutionResult> RunAsync(
            string arguments,
            string workingDirectory,
            CancellationToken cancellationToken = default,
            TimeSpan? timeout = null,
            IProgress<string>? outputProgress = null,
            IReadOnlyDictionary<string, string>? environmentVariables = null,
            Func<Stream, CancellationToken, Task>? standardInputWriter = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var failed = _induceDeploymentFailure && arguments.Contains("Invoke-PM365Deployment", StringComparison.Ordinal);
            var code = failed ? "FixtureDeploymentInducedFailure" : "FixtureEnginePathCompleted";
            var output = JsonSerializer.Serialize(new
            {
                status = failed ? "Failed" : "Passed",
                code,
                summary = failed ? "Fixture deployment failure induced." : "Fixture-only engine path completed.",
                details = failed ? "No cloud command was executed." : "No cloud command was executed.",
                retrySafe = true,
                data = new { fixtureOnly = "true", cloudMutation = "denied" }
            });
            outputProgress?.Report(output);
            return Task.FromResult(new PowerShellExecutionResult { ExitCode = 0, StandardOutput = output });
        }

        public Task<PowerShellExecutionResult> RunInteractiveFileResultAsync(
            string arguments,
            string workingDirectory,
            string resultPath,
            CancellationToken cancellationToken = default,
            TimeSpan? timeout = null) =>
            RunAsync(arguments, workingDirectory, cancellationToken, timeout);
    }

    private sealed class FixtureEvidenceCallback
    {
        private bool _outagePending;

        public FixtureEvidenceCallback(bool outagePending)
        {
            _outagePending = outagePending;
        }

        public string LastReceiptStatus { get; set; } = "not_attempted";

        public bool TryAccept(PendingInstallerEvidenceEvent pending, out string receiptStatus)
        {
            if (_outagePending)
            {
                _outagePending = false;
                receiptStatus = "retry_pending";
                return false;
            }

            receiptStatus = "accepted_fixture";
            return true;
        }
    }
}

public static class FixtureLifecycleScenario
{
    public const string InstallUninstall = "install-uninstall";
    public const string FailureRecoveryReinstallUninstall = "failure-recovery-reinstall-uninstall";
}

public sealed record class FixtureLifecycleRunnerRequest
{
    public string ContractVersion { get; init; } = "";
    public bool FixtureOnly { get; init; }
    public bool TestOnlyEnabled { get; init; }
    public bool AllowCloudMutation { get; init; }
    public string RunId { get; init; } = "";
    public string Environment { get; init; } = "";
    public string SubscriptionId { get; init; } = "";
    public List<string> AllowedSubscriptionIds { get; init; } = [];
    public string ResourceGroupName { get; init; } = "";
    public string Confirmation { get; init; } = "";
    public string OutputRoot { get; init; } = "";
    public string RuntimeBootstrapPath { get; init; } = "";
    public string VerifiedPackagePayloadSha256 { get; init; } = "";
    public string CustomerId { get; init; } = "";
    public string TenantId { get; init; } = "";
    public string InstallationId { get; init; } = "";
    public string EnvironmentId { get; init; } = "";
    public string DeploymentExportId { get; init; } = "";
    public string RuntimeReleaseId { get; init; } = "";
    public string OnboardingSessionId { get; init; } = "";
    public string Scenario { get; init; } = FixtureLifecycleScenario.InstallUninstall;
    public bool InducePortalOutageOnce { get; init; }
}

public sealed class FixtureLifecycleRunnerResult
{
    public string ContractVersion { get; set; } = FixtureLifecycleRunner.RunnerContractVersion;
    public bool FixtureOnly { get; set; }
    public string CloudMutation { get; set; } = "denied";
    public string RunId { get; set; } = "";
    public string Status { get; set; } = "not_started";
    public string ReceiptStatus { get; set; } = "not_attempted";
    public int PendingEvidenceCount { get; set; }
    public string BlockerCode { get; set; } = "";
    public string RecoveryCode { get; set; } = "";
    public string RuntimeDeliveryStatus { get; set; } = "blocked";
    public string RuntimeDeliveryBlockerCode { get; set; } = "";
    public List<FixtureLifecycleRunnerStage> Stages { get; set; } = [];
}

public sealed class FixtureLifecycleRunnerStage
{
    public string Stage { get; set; } = "";
    public string Status { get; set; } = "";
    public string Code { get; set; } = "";
    public string SafeCorrelationReference { get; set; } = "";
    public string ReceiptStatus { get; set; } = "";
    public string BlockerCode { get; set; } = "";
    public string RecoveryCode { get; set; } = "";
}

internal sealed class FixtureLifecycleEvidenceState
{
    public string InstallAttemptId { get; set; } = "";
    public int NextInstallSequence { get; set; } = 1;
    public string LastInstallEventType { get; set; } = "";
    public bool InstallTerminal { get; set; }
    public List<PendingInstallerEvidenceEvent> PendingEvents { get; } = [];
}
