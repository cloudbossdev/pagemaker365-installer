using Microsoft.Identity.Client;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PageMaker365.Installer.Engine.Models;
using PageMaker365.Installer.Engine.PowerShell;
using PageMaker365.Installer.Engine.Steps;

namespace PageMaker365.Installer.Engine.Services;

public sealed class InstallerEngine
{
    private readonly StructuredLogger _logger;
    private readonly PowerShellProcessRunner _powerShellRunner;
    private readonly GraphDeviceCodeAuthenticator _graphAuthenticator = new();

    public InstallerEngine(StructuredLogger logger)
    {
        _logger = logger;
        _powerShellRunner = new PowerShellProcessRunner();
    }

    public InstallerSession CreateSession(CustomerInstallConfig config, string workspaceRoot)
    {
        var session = InstallerSession.Create(config, workspaceRoot);
        Directory.CreateDirectory(session.LogDirectory);
        return session;
    }

    public async Task<IReadOnlyList<InstallerStepResult>> RunPreflightAsync(
        InstallerSession session,
        IProgress<InstallerStepResult>? progress = null,
        CancellationToken cancellationToken = default)
    {
        session.CurrentPhase = "Preflight & Permissions";
        session.Status = InstallStatus.Running;
        await _logger.WriteAsync(session, "phase.started", new { session.CurrentPhase }, cancellationToken);

        var steps = CreateMockPreflightSteps();
        foreach (var step in steps)
        {
            await _logger.WriteAsync(session, "step.started", new { step.Name, step.Code }, cancellationToken);
            var result = await step.RunAsync(session, cancellationToken);
            session.Results.Add(result);
            progress?.Report(result);
            await _logger.WriteAsync(session, "step.completed", result, cancellationToken);
            await PersistSessionAsync(session, cancellationToken);
        }

        session.Status = session.Results.Any(result => result.Status == InstallStatus.Failed)
            ? InstallStatus.Failed
            : session.Results.Any(result => result.Status == InstallStatus.Warning)
                ? InstallStatus.Warning
                : session.Results.Count > 0 && session.Results.All(result => result.Status == InstallStatus.Skipped)
                    ? InstallStatus.Skipped
                    : InstallStatus.Passed;

        await _logger.WriteAsync(session, "phase.completed", new { session.CurrentPhase, session.Status }, cancellationToken);
        await PersistSessionAsync(session, cancellationToken);
        return session.Results;
    }

    public async Task<IReadOnlyList<InstallerStepResult>> RunPowerShellPreflightAsync(
        InstallerSession session,
        string workspaceRoot,
        string configPath,
        string graphAccessToken = "",
        IProgress<InstallerStepResult>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var environmentVariables = string.IsNullOrWhiteSpace(graphAccessToken)
            ? null
            : new Dictionary<string, string> { ["PM365_GRAPH_ACCESS_TOKEN"] = graphAccessToken };

        return await RunPowerShellModuleCommandAsync(
            session,
            workspaceRoot,
            configPath,
            "Preflight & Permissions",
            "Start-PM365Preflight",
            progress,
            cancellationToken,
            environmentVariables: environmentVariables);
    }

    public async Task<IReadOnlyList<InstallerStepResult>> RunAzureSignInAsync(
        InstallerSession session,
        string workspaceRoot,
        string configPath,
        IProgress<InstallerStepResult>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return await RunPowerShellModuleCommandAsync(
            session,
            workspaceRoot,
            configPath,
            "Azure Sign In",
            "Connect-PM365Azure",
            progress,
            cancellationToken);
    }

    public async Task<IReadOnlyList<InstallerStepResult>> RunGraphSignInAsync(
        InstallerSession session,
        string workspaceRoot,
        string configPath,
        IProgress<InstallerStepResult>? progress = null,
        IProgress<string>? outputProgress = null,
        CancellationToken cancellationToken = default)
    {
        return await RunPowerShellModuleCommandAsync(
            session,
            workspaceRoot,
            configPath,
            "Microsoft Graph Sign In",
            "Connect-PM365Graph",
            progress,
            cancellationToken,
            commandArguments: "-UseDeviceCode -ContextScope 'CurrentUser'",
            outputProgress: outputProgress,
            useInteractiveWindow: true);
    }

    public async Task<GraphSignInResult> RunGraphDeviceCodeSignInAsync(
        InstallerSession session,
        IProgress<InstallerStepResult>? progress = null,
        IProgress<GraphDeviceCodePrompt>? promptProgress = null,
        CancellationToken cancellationToken = default)
    {
        session.CurrentPhase = "Microsoft Graph Sign In";
        session.Status = InstallStatus.Running;
        await _logger.WriteAsync(session, "phase.started", new { session.CurrentPhase, mode = "MSALDeviceCode" }, cancellationToken);

        GraphSignInResult signInResult;
        try
        {
            signInResult = await _graphAuthenticator.SignInAsync(
                ResolveTenantId(session.Config),
                ResolveInstallerGraphClientId(session.Config),
                promptProgress,
                cancellationToken);
        }
        catch (MsalException exception)
        {
            var failed = InstallerStepResult.Failed(
                "Microsoft Graph Sign In",
                "GraphSignInFailed",
                "Microsoft Graph sign-in did not complete.",
                exception.Message,
                retrySafe: true);
            await RecordResultAsync(session, failed, progress, cancellationToken);
            await CompletePhaseAsync(session, cancellationToken);
            return new GraphSignInResult { StepResult = failed };
        }

        var passed = InstallerStepResult.Passed(
            "Microsoft Graph Sign In",
            "GraphSignInCompleted",
            "Microsoft Graph sign-in completed.",
            $"Signed in as {ValueOrNotAvailable(signInResult.Account)} for tenant {ValueOrNotAvailable(signInResult.TenantId)}.");
        passed.Data["tenantId"] = signInResult.TenantId;
        passed.Data["account"] = signInResult.Account;
        passed.Data["scopes"] = string.Join(", ", signInResult.Scopes);
        passed.Data["authMode"] = "DeviceCode";

        signInResult.StepResult = passed;
        await RecordResultAsync(session, passed, progress, cancellationToken);
        await CompletePhaseAsync(session, cancellationToken);
        return signInResult;
    }

    public async Task<IReadOnlyList<InstallerStepResult>> RunWhatIfAsync(
        InstallerSession session,
        string workspaceRoot,
        string configPath,
        string outputPath = "",
        IProgress<InstallerStepResult>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var commandArguments = string.IsNullOrWhiteSpace(outputPath)
            ? ""
            : $"-OutputPath '{EscapePowerShellSingleQuotedValue(outputPath)}'";

        return await RunPowerShellModuleCommandAsync(
            session,
            workspaceRoot,
            configPath,
            "Deployment Preview",
            "Invoke-PM365WhatIf",
            progress,
            cancellationToken,
            commandArguments);
    }

    public async Task<IReadOnlyList<InstallerStepResult>> RunDeploymentAsync(
        InstallerSession session,
        string workspaceRoot,
        string configPath,
        string outputPath = "",
        IProgress<InstallerStepResult>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var commandArguments = string.IsNullOrWhiteSpace(outputPath)
            ? "-Confirm:$false"
            : $"-OutputPath '{EscapePowerShellSingleQuotedValue(outputPath)}' -Confirm:$false";

        return await RunPowerShellModuleCommandAsync(
            session,
            workspaceRoot,
            configPath,
            "Install",
            "Invoke-PM365Deployment",
            progress,
            cancellationToken,
            commandArguments);
    }

    public async Task<IReadOnlyList<InstallerStepResult>> RunRuntimeConfigurationAsync(
        InstallerSession session,
        string workspaceRoot,
        string configPath,
        IReadOnlyCollection<RuntimeSecretMaterial> secretMaterials,
        string outputPath = "",
        IProgress<InstallerStepResult>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ValidateRuntimeSecretMaterials(session.Config, secretMaterials);
        var commandArguments = string.IsNullOrWhiteSpace(outputPath)
            ? ""
            : $"-OutputPath '{EscapePowerShellSingleQuotedValue(outputPath)}'";
        var metadata = JsonSerializer.Serialize(new
        {
            contractVersion = "0.1",
            secrets = secretMaterials.Select(material => new
            {
                material.Definition.KeyVaultSecretName,
                material.Definition.AppSettingName,
                material.Definition.MinimumLength
            })
        });

        return await RunPowerShellModuleCommandAsync(
            session,
            workspaceRoot,
            configPath,
            "Runtime Configuration",
            "Set-PM365RuntimeConfiguration",
            progress,
            cancellationToken,
            commandArguments,
            standardInputWriter: (stream, _) =>
            {
                WriteUtf8Line(stream, metadata);
                foreach (var material in secretMaterials)
                {
                    material.WriteUtf8Value(stream);
                    stream.WriteByte((byte)'\n');
                }

                return Task.CompletedTask;
            });
    }

    public async Task<IReadOnlyList<InstallerStepResult>> RunValidationAsync(
        InstallerSession session,
        string workspaceRoot,
        string configPath,
        string graphAccessToken = "",
        IProgress<InstallerStepResult>? progress = null,
        string deploymentArtifactPath = "",
        CancellationToken cancellationToken = default)
    {
        var environmentVariables = string.IsNullOrWhiteSpace(graphAccessToken)
            ? null
            : new Dictionary<string, string> { ["PM365_GRAPH_ACCESS_TOKEN"] = graphAccessToken };
        var commandArguments = string.IsNullOrWhiteSpace(deploymentArtifactPath)
            ? ""
            : $"-DeploymentArtifactPath '{EscapePowerShellSingleQuotedValue(deploymentArtifactPath)}'";

        return await RunPowerShellModuleCommandAsync(
            session,
            workspaceRoot,
            configPath,
            "Validate",
            "Test-PM365SmokeTests",
            progress,
            cancellationToken,
            commandArguments,
            environmentVariables: environmentVariables);
    }

    public async Task<IReadOnlyList<InstallerStepResult>> RunRemovalInventoryAsync(
        InstallerSession session,
        string workspaceRoot,
        string configPath,
        string outputPath,
        IProgress<InstallerStepResult>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var commandArguments = $"-OutputPath '{EscapePowerShellSingleQuotedValue(outputPath)}'";
        return await RunPowerShellModuleCommandAsync(
            session,
            workspaceRoot,
            configPath,
            "Removal Inventory",
            "Get-PM365PartialInstallInventory",
            progress,
            cancellationToken,
            commandArguments);
    }

    public async Task<IReadOnlyList<InstallerStepResult>> RunRemovalAsync(
        InstallerSession session,
        string workspaceRoot,
        string configPath,
        string confirmationText,
        string outputPath,
        IProgress<InstallerStepResult>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var commandArguments =
            $"-ConfirmationText '{EscapePowerShellSingleQuotedValue(confirmationText)}' " +
            $"-OutputPath '{EscapePowerShellSingleQuotedValue(outputPath)}' " +
            "-RetainSoftDeletedKeyVault:$true -Confirm:$false";
        return await RunPowerShellModuleCommandAsync(
            session,
            workspaceRoot,
            configPath,
            "Remove",
            "Remove-PM365PartialInstall",
            progress,
            cancellationToken,
            commandArguments);
    }

    public async Task<IReadOnlyList<InstallerStepResult>> RunRemovalValidationAsync(
        InstallerSession session,
        string workspaceRoot,
        string configPath,
        string outputPath,
        IProgress<InstallerStepResult>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var commandArguments = $"-OutputPath '{EscapePowerShellSingleQuotedValue(outputPath)}'";
        return await RunPowerShellModuleCommandAsync(
            session,
            workspaceRoot,
            configPath,
            "Validate Cleanup",
            "Get-PM365PartialInstallInventory",
            progress,
            cancellationToken,
            commandArguments);
    }

    private async Task<IReadOnlyList<InstallerStepResult>> RunPowerShellModuleCommandAsync(
        InstallerSession session,
        string workspaceRoot,
        string configPath,
        string phase,
        string commandName,
        IProgress<InstallerStepResult>? progress = null,
        CancellationToken cancellationToken = default,
        string commandArguments = "",
        IProgress<string>? outputProgress = null,
        bool useInteractiveWindow = false,
        IReadOnlyDictionary<string, string>? environmentVariables = null,
        Func<Stream, CancellationToken, Task>? standardInputWriter = null)
    {
        session.CurrentPhase = phase;
        session.Status = InstallStatus.Running;
        await _logger.WriteAsync(session, "phase.started", new { session.CurrentPhase, mode = "PowerShell", commandName }, cancellationToken);

        var modulePath = Path.Combine(workspaceRoot, "modules", "PageMaker365.Install", "PageMaker365.Install.psd1");
        if (!File.Exists(modulePath))
        {
            var missing = InstallerStepResult.Failed(
                "Installer Module",
                "InstallerModuleMissing",
                "The PageMaker365.Install PowerShell module was not found.",
                modulePath,
                retrySafe: false);
            await RecordResultAsync(session, missing, progress, cancellationToken);
            await CompletePhaseAsync(session, cancellationToken);
            return session.Results;
        }

        await _logger.WriteAsync(session, "powershell.started", new { modulePath, command = commandName, configPath, useInteractiveWindow }, cancellationToken);
        var resultPath = useInteractiveWindow
            ? Path.Combine(session.LogDirectory, $"{SanitizeFileName(commandName)}-result.json")
            : "";
        var command = useInteractiveWindow
            ? BuildModuleResultFileCommand(modulePath, commandName, configPath, resultPath, commandArguments)
            : BuildModuleCommand(modulePath, commandName, configPath, commandArguments);
        PowerShellExecutionResult execution;
        try
        {
            execution = useInteractiveWindow
                ? await _powerShellRunner.RunInteractiveFileResultAsync(command, workspaceRoot, resultPath, cancellationToken)
                : await _powerShellRunner.RunAsync(
                    command,
                    workspaceRoot,
                    cancellationToken,
                    outputProgress: outputProgress,
                    environmentVariables: environmentVariables,
                    standardInputWriter: standardInputWriter);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            var failed = InstallerStepResult.Failed(
                "PowerShell Command",
                "PowerShellLaunchFailed",
                "The installer could not start the PowerShell command.",
                exception.Message,
                retrySafe: true);
            await RecordResultAsync(session, failed, progress, cancellationToken);
            await CompletePhaseAsync(session, cancellationToken);
            return session.Results;
        }

        await _logger.WriteAsync(
            session,
            "powershell.completed",
            new
            {
                execution.ExitCode,
                execution.StandardOutput,
                execution.StandardError
            },
            cancellationToken);

        if (!execution.Succeeded)
        {
            var failed = InstallerStepResult.Failed(
                "PowerShell Prerequisites",
                "PowerShellPreflightFailed",
                "The prerequisite check command failed.",
                string.IsNullOrWhiteSpace(execution.StandardError) ? execution.StandardOutput : execution.StandardError,
                retrySafe: true);
            await RecordResultAsync(session, failed, progress, cancellationToken);
            await CompletePhaseAsync(session, cancellationToken);
            return session.Results;
        }

        foreach (var result in ParsePowerShellResults(execution.StandardOutput))
        {
            await RecordResultAsync(session, result, progress, cancellationToken);
        }

        await CompletePhaseAsync(session, cancellationToken);
        return session.Results;
    }

    public InstallerDiagnosticPayload CreateDiagnosticPayload(InstallerSession session)
    {
        var failed = session.Results.FirstOrDefault(result => result.Status == InstallStatus.Failed);
        return new InstallerDiagnosticPayload
        {
            SessionId = session.SessionId,
            Phase = session.CurrentPhase,
            FailedStep = failed?.StepName ?? "",
            ErrorCode = failed?.Code ?? "",
            RedactedLog = ReadRedactedLog(session),
            Facts =
            {
                ["tenantName"] = session.Config.Customer.TenantName,
                ["environment"] = session.Config.Azure.Environment,
                ["resourceGroup"] = session.Config.Azure.ResourceGroupName,
                ["sharePointSite"] = session.Config.SharePoint.SiteUrl,
                ["hasBlockingFailure"] = (failed is not null).ToString()
            }
        };
    }

    private static IReadOnlyList<IInstallerStep> CreateMockPreflightSteps()
    {
        return
        [
            new MockInstallerStep(
                "PowerShell 7",
                "PowerShellReady",
                InstallerStepResult.Passed("PowerShell 7", "PowerShellReady", "PowerShell 7 is available.", "The installer engine can use PowerShell 7 for deployment commands.")),
            new MockInstallerStep(
                "Azure Subscription",
                "AzureSubscriptionReady",
                InstallerStepResult.Passed("Azure Subscription", "AzureSubscriptionReady", "Azure subscription context is ready.", "Subscription and resource group access will be verified by the real implementation.")),
            new MockInstallerStep(
                "SharePoint Site",
                "SharePointSiteReady",
                InstallerStepResult.Passed("SharePoint Site", "SharePointSiteReady", "SharePoint site URL format is valid.", "The real implementation will resolve site and library IDs through Microsoft Graph.")),
            new MockInstallerStep(
                "Bicep What-If Ready",
                "BicepNotVerified",
                InstallerStepResult.Warning("Bicep What-If Ready", "BicepNotVerified", "Bicep is not verified yet.", "The scaffold is using mocked preflight data until the deployment module is wired.")),
            new MockInstallerStep(
                "Entra Permissions",
                "MissingApplicationAdministrator",
                InstallerStepResult.Failed("Entra Permissions", "MissingApplicationAdministrator", "The signed-in user may not be able to approve app permissions.", "Ask a Global Administrator, Cloud Application Administrator, or Application Administrator to complete consent.", retrySafe: false))
        ];
    }

    private static string BuildPreflightCommand(string modulePath, string configPath)
    {
        return BuildModuleCommand(modulePath, "Start-PM365Preflight", configPath);
    }

    private static void ValidateRuntimeSecretMaterials(
        CustomerInstallConfig config,
        IReadOnlyCollection<RuntimeSecretMaterial> secretMaterials)
    {
        ArgumentNullException.ThrowIfNull(secretMaterials);
        var materialsBySetting = secretMaterials.ToDictionary(
            material => material.Definition.AppSettingName,
            StringComparer.Ordinal);
        foreach (var definition in config.Secrets.RuntimeSecrets)
        {
            if (!materialsBySetting.TryGetValue(definition.AppSettingName, out var material))
            {
                throw new InvalidOperationException($"Runtime secret value is missing for {definition.AppSettingName}.");
            }

            if (!material.Definition.KeyVaultSecretName.Equals(definition.KeyVaultSecretName, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Runtime secret metadata does not match the package for {definition.AppSettingName}.");
            }

            if (material.Length < definition.MinimumLength)
            {
                throw new InvalidOperationException($"Runtime secret value for {definition.AppSettingName} is shorter than the package minimum.");
            }

            if (material.Length > RuntimeSecretMaterial.MaximumLength)
            {
                throw new InvalidOperationException(
                    $"Runtime secret value for {definition.AppSettingName} exceeds the supported maximum length.");
            }
        }

        if (materialsBySetting.Count != config.Secrets.RuntimeSecrets.Count)
        {
            throw new InvalidOperationException("Runtime secret values contain entries that are not declared by the signed customer package.");
        }
    }

    private static void WriteUtf8Line(Stream stream, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        try
        {
            stream.Write(bytes);
            stream.WriteByte((byte)'\n');
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static string ResolveTenantId(CustomerInstallConfig config)
    {
        return !string.IsNullOrWhiteSpace(config.Customer.TenantId)
            ? config.Customer.TenantId
            : config.Azure.TenantId;
    }

    private static string ResolveInstallerGraphClientId(CustomerInstallConfig config)
    {
        var fromEnvironment = Environment.GetEnvironmentVariable("PM365_INSTALLER_GRAPH_CLIENT_ID");
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return fromEnvironment;
        }

        return GraphDeviceCodeAuthenticator.DefaultClientId;
    }

    private static string ValueOrNotAvailable(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "not available" : value;
    }

    private static string BuildModuleCommand(string modulePath, string commandName, string configPath, string commandArguments = "")
    {
        var escapedPath = EscapePowerShellSingleQuotedValue(modulePath);
        var escapedConfigPath = EscapePowerShellSingleQuotedValue(configPath);
        var arguments = string.IsNullOrWhiteSpace(commandArguments) ? "" : $" {commandArguments}";
        var script = "$ErrorActionPreference = 'Stop'; " +
                     $"Import-Module '{escapedPath}' -Force; " +
                     $"{commandName} -ConfigPath '{escapedConfigPath}'{arguments} | ConvertTo-Json -Depth 12";
        return $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"";
    }

    private static string BuildModuleResultFileCommand(
        string modulePath,
        string commandName,
        string configPath,
        string resultPath,
        string commandArguments = "")
    {
        var escapedPath = EscapePowerShellSingleQuotedValue(modulePath);
        var escapedConfigPath = EscapePowerShellSingleQuotedValue(configPath);
        var escapedResultPath = EscapePowerShellSingleQuotedValue(resultPath);
        var arguments = string.IsNullOrWhiteSpace(commandArguments) ? "" : $" {commandArguments}";
        var script = "$ErrorActionPreference = 'Stop'; " +
                     $"Import-Module '{escapedPath}' -Force; " +
                     $"$result = {commandName} -ConfigPath '{escapedConfigPath}'{arguments}; " +
                     $"$result | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath '{escapedResultPath}' -Encoding UTF8";
        return $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"";
    }

    private static string EscapePowerShellSingleQuotedValue(string value)
    {
        return value.Replace("'", "''");
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(character => invalid.Contains(character) ? '-' : character).ToArray());
    }

    private static IReadOnlyList<InstallerStepResult> ParsePowerShellResults(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return
            [
                InstallerStepResult.Failed(
                    "PowerShell Prerequisites",
                    "PowerShellPreflightNoOutput",
                    "The prerequisite check command returned no output.",
                    retrySafe: true)
            ];
        }

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var trimmed = TryExtractJsonPayload(json);
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return
            [
                InstallerStepResult.Failed(
                    "PowerShell Result",
                    "PowerShellResultParseFailed",
                    "The PowerShell command completed, but the installer could not find a structured result in its output.",
                    TrimForDetails(json),
                    retrySafe: true)
            ];
        }

        List<PowerShellModuleResult> moduleResults;
        try
        {
            moduleResults = trimmed.StartsWith("[", StringComparison.Ordinal)
                ? JsonSerializer.Deserialize<List<PowerShellModuleResult>>(trimmed, options) ?? []
                : [JsonSerializer.Deserialize<PowerShellModuleResult>(trimmed, options) ?? new PowerShellModuleResult()];
        }
        catch (JsonException exception)
        {
            return
            [
                InstallerStepResult.Failed(
                    "PowerShell Result",
                    "PowerShellResultParseFailed",
                    "The PowerShell command completed, but its structured result could not be parsed.",
                    $"{exception.Message}{Environment.NewLine}{TrimForDetails(json)}",
                    retrySafe: true)
            ];
        }

        return moduleResults.Select(ToInstallerStepResult).ToArray();
    }

    private static string TryExtractJsonPayload(string output)
    {
        var trimmed = output.Trim();
        for (var index = 0; index < trimmed.Length; index++)
        {
            var current = trimmed[index];
            if (current is not ('{' or '['))
            {
                continue;
            }

            var candidate = TryReadJsonValue(trimmed, index);
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            try
            {
                using var _ = JsonDocument.Parse(candidate);
                return candidate;
            }
            catch (JsonException)
            {
                // Some Azure modules emit text such as "[Announcements]" before JSON.
            }
        }

        return "";
    }

    private static string TryReadJsonValue(string value, int startIndex)
    {
        var opening = value[startIndex];
        var closing = opening == '{' ? '}' : ']';
        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var index = startIndex; index < value.Length; index++)
        {
            var current = value[index];
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (inString)
            {
                if (current == '\\')
                {
                    escaped = true;
                }
                else if (current == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (current == '"')
            {
                inString = true;
                continue;
            }

            if (current == opening)
            {
                depth++;
            }
            else if (current == closing)
            {
                depth--;
                if (depth == 0)
                {
                    return value.Substring(startIndex, index - startIndex + 1);
                }
            }
        }

        return "";
    }

    private static string TrimForDetails(string value)
    {
        var compact = value.Trim();
        return compact.Length <= 2000 ? compact : compact.Substring(0, 2000) + "...";
    }

    private static InstallerStepResult ToInstallerStepResult(PowerShellModuleResult result)
    {
        var status = Enum.TryParse<InstallStatus>(result.Status, ignoreCase: true, out var parsed)
            ? parsed
            : InstallStatus.Failed;
        var stepName = NameFromCode(result.Code);
        var now = DateTimeOffset.UtcNow;

        return new InstallerStepResult
        {
            StepName = stepName,
            Code = string.IsNullOrWhiteSpace(result.Code) ? "PowerShellResult" : result.Code,
            Status = status,
            Summary = result.Summary,
            Details = result.Details,
            RetrySafe = result.RetrySafe,
            Data = result.Data.ToDictionary(item => item.Key, item => item.Value.ToString()),
            StartedAt = now,
            CompletedAt = now
        };
    }

    private static string NameFromCode(string code)
    {
        return code switch
        {
            "PowerShellReady" => "PowerShell 7",
            "DeploymentContractReadable" or "DeploymentContractReady" or "DeploymentContractIncomplete" => "Deployment Contract",
            "DeploymentPackageSecretSafe" or "DeploymentPackageContainsRawSecrets" or "DeploymentSecretsContractMissing" => "Deployment Package Secrets",
            "DeploymentPackageTrustVerified" or "DeploymentPackageHashVerified" or "DeploymentPackageLegacyTrust" or "DeploymentPackageHashMismatch" or "DeploymentPackageSignatureMissing" or "DeploymentPackageTrustMetadataReady" or "DeploymentPackageTrustMetadataIncomplete" or "DeploymentPackageTrustMetadataInvalid" => "Package Trust",
            "AzAccountsReady" or "AzAccountsMissing" => "Az.Accounts Module",
            "BicepReady" or "BicepMissing" => "Bicep",
            "AzureSignInCompleted" or "AzureSignInFailed" => "Azure Sign In",
            "GraphSignInCompleted" or "GraphSignInFailed" => "Microsoft Graph Sign In",
            "AzureTenantReady" or "AzureTenantMismatch" => "Azure Tenant",
            "AzureSubscriptionReady" or "AzureSubscriptionMismatch" or "AzureSubscriptionUnavailable" => "Azure Subscription",
            "AzureResourceGroupReady" or "AzureResourceGroupWillBeCreated" or "AzureResourceGroupMissing" or "AzureResourceGroupOwnershipMismatch" => "Azure Resource Group",
            "KeyVaultNameReady" or "KeyVaultRecoveryRequired" or "KeyVaultRecoveryCheckSkipped" or "KeyVaultRecoveryCheckUnavailable" or "KeyVaultRecoveryContractMissing" => "Key Vault Recovery",
            "AzureRbacReady" or "AzureRbacInsufficient" or "AzureRbacNotFound" or "AzureRbacCheckUnavailable" => "Azure RBAC",
            "GraphTenantReady" or "GraphTenantMismatch" => "Microsoft Graph Tenant",
            "GraphConsentScopesReady" or "GraphConsentScopesMissing" => "Microsoft Graph Consent",
            "EntraAdminRoleReady" or "EntraAdminRoleMissing" or "EntraAdminRoleCheckUnavailable" => "Entra Admin Role",
            "SharePointSiteUrlReady" or "SharePointSiteUrlInvalid" => "SharePoint Site URL",
            "SharePointSiteResolved" or "SharePointSiteResolveFailed" => "SharePoint Site",
            "SharePointLibraryReady" or "SharePointLibraryNotFound" or "SharePointLibraryNotConfigured" => "SharePoint Library",
            "AzureWhatIfReady" or "AzureWhatIfFailed" => "Azure What-If",
            "AzureDeploymentReady" or "AzureDeploymentFailed" or "AppServiceCapacityUnavailable" => "Azure Deployment",
            "RuntimeConfigurationReady" or "RuntimeConfigurationFailed" or "RuntimeConfigurationInputInvalid" or "RuntimeKeyVaultReferenceFailed" => "Runtime Configuration",
            "DeploymentSkipped" => "Deployment Approval",
            "AppUrlMissing" => "Application URL",
            "AppHealthReady" or "AppHealthFailed" => "Application Health",
            "PartialInstallCleanupReady" or "PartialInstallCleanupBlocked" => "Removal Inventory",
            "PartialInstallCleanupCompleted" or "PartialInstallCleanupSkipped" or "PartialInstallCleanupConfirmationMismatch" or "PartialInstallCleanupIncomplete" => "Azure Removal",
            "PartialInstallAbsent" => "Cleanup Validation",
            _ when string.IsNullOrWhiteSpace(code) => "PowerShell Check",
            _ => code
        };
    }

    private async Task RecordResultAsync(
        InstallerSession session,
        InstallerStepResult result,
        IProgress<InstallerStepResult>? progress,
        CancellationToken cancellationToken)
    {
        session.Results.Add(result);
        progress?.Report(result);
        await _logger.WriteAsync(session, "step.completed", result, cancellationToken);
        await PersistSessionAsync(session, cancellationToken);
    }

    private async Task CompletePhaseAsync(InstallerSession session, CancellationToken cancellationToken)
    {
        session.Status = session.Results.Any(result => result.Status == InstallStatus.Failed)
            ? InstallStatus.Failed
            : session.Results.Any(result => result.Status == InstallStatus.Warning)
                ? InstallStatus.Warning
                : InstallStatus.Passed;

        await _logger.WriteAsync(session, "phase.completed", new { session.CurrentPhase, session.Status }, cancellationToken);
        await PersistSessionAsync(session, cancellationToken);
    }

    private async Task PersistSessionAsync(InstallerSession session, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(session.LogDirectory);
        var path = Path.Combine(session.LogDirectory, "install-session.json");
        var json = JsonSerializer.Serialize(session, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json, cancellationToken);
    }

    private static string ReadRedactedLog(InstallerSession session)
    {
        var path = Path.Combine(session.LogDirectory, "redacted-install.log");
        return File.Exists(path) ? File.ReadAllText(path) : "";
    }
}
