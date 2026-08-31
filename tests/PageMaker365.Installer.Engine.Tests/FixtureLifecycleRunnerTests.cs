using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PageMaker365.Installer.Engine.Services;

namespace PageMaker365.Installer.Engine.Tests;

internal static class FixtureLifecycleRunnerTests
{
    public static async Task ExecutesRecoveryReinstallRemovalAndEvidenceReplay()
    {
        var root = CreateTempDirectory();
        try
        {
            var request = await CreateRequestAsync(root, FixtureLifecycleScenario.FailureRecoveryReinstallUninstall);
            using var environment = new EnvironmentVariableScope(FixtureLifecycleRunner.EnableEnvironmentVariable, "1");

            var result = await new FixtureLifecycleRunner(new StructuredLogger(new RedactionService())).RunAsync(request);

            AssertEx.Equal("passed", result.Status);
            AssertEx.True(result.FixtureOnly);
            AssertEx.Equal("denied", result.CloudMutation);
            AssertEx.Equal("RUNTIME_DELIVERY_CONTRACT_PENDING", result.RuntimeDeliveryBlockerCode);
            AssertEx.Equal("FIXTURE_DEPLOYMENT_FAILURE_RECOVERED", result.RecoveryCode);
            AssertEx.Equal(0, result.PendingEvidenceCount);
            AssertEx.True(result.Stages.Any(stage => stage.Code == "DEPLOY" && stage.Status == "failed"));
            AssertEx.True(result.Stages.Any(stage => stage.Code == "REMOVAL_VALIDATION" && stage.Status == "passed"));
            AssertEx.True(result.Stages.Any(stage => stage.Code == "REMOVAL_EVIDENCE" && stage.ReceiptStatus == "accepted_fixture"));

            var persisted = await File.ReadAllTextAsync(Path.Combine(root, "fixture-lifecycle-evidence-state.json"));
            AssertEx.StringContains(persisted, "\"pendingEvidenceCount\":0");
            AssertEx.False(persisted.Contains("customerId", StringComparison.OrdinalIgnoreCase));
            AssertEx.False(persisted.Contains("payloadBase64", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    public static async Task RefusesCloudMutationBeforeReadingBootstrap()
    {
        var root = CreateTempDirectory();
        try
        {
            var request = await CreateRequestAsync(root, FixtureLifecycleScenario.InstallUninstall);
            request = request with { AllowCloudMutation = true, RuntimeBootstrapPath = Path.Combine(root, "does-not-exist.json") };
            using var environment = new EnvironmentVariableScope(FixtureLifecycleRunner.EnableEnvironmentVariable, "1");

            await AssertEx.ThrowsAsync<InvalidDataException>(() =>
                new FixtureLifecycleRunner(new StructuredLogger(new RedactionService())).RunAsync(request));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    public static async Task CommandWritesSanitizedResult()
    {
        var root = CreateTempDirectory();
        try
        {
            var request = await CreateRequestAsync(root, FixtureLifecycleScenario.InstallUninstall);
            var requestPath = Path.Combine(root, "fixture-runner.json");
            var resultPath = Path.Combine(root, "fixture-result.json");
            await File.WriteAllTextAsync(requestPath, JsonSerializer.Serialize(request), new UTF8Encoding(false));
            using var environment = new EnvironmentVariableScope(FixtureLifecycleRunner.EnableEnvironmentVariable, "1");

            var exitCode = await FixtureLifecycleRunnerCommand.RunAsync(["--fixture-lifecycle-runner", requestPath, resultPath]);

            AssertEx.Equal(0, exitCode);
            var json = await File.ReadAllTextAsync(resultPath);
            AssertEx.StringContains(json, "\"cloudMutation\":\"denied\"");
            AssertEx.StringContains(json, "\"runtimeDeliveryBlockerCode\":\"RUNTIME_DELIVERY_CONTRACT_PENDING\"");
            AssertEx.False(json.Contains("payloadBase64", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    public static async Task RejectsBootstrapBindingMismatchAndPayloadTampering()
    {
        var root = CreateTempDirectory();
        try
        {
            var request = await CreateRequestAsync(root, FixtureLifecycleScenario.InstallUninstall);
            var valid = await File.ReadAllTextAsync(request.RuntimeBootstrapPath);
            var binding = new RuntimeBootstrapEnvelopeBinding
            {
                PackagePayloadSha256 = request.VerifiedPackagePayloadSha256,
                CustomerId = request.CustomerId,
                TenantId = request.TenantId,
                InstallationId = request.InstallationId,
                EnvironmentId = request.EnvironmentId,
                DeploymentExportId = request.DeploymentExportId,
                RuntimeReleaseId = request.RuntimeReleaseId
            };
            var validator = new FixtureRuntimeBootstrapEnvelopeValidator();

            AssertEx.Throws<InvalidDataException>(() =>
                validator.ValidateJson(valid.Replace("fixture-tenant-001", "wrong-tenant-001", StringComparison.Ordinal), binding));
            AssertEx.Throws<InvalidDataException>(() =>
                validator.ValidateJson(valid.Replace("Zml4dHVyZS1ib290c3RyYXA=", "dGFtcGVyZWQ=", StringComparison.Ordinal), binding));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<FixtureLifecycleRunnerRequest> CreateRequestAsync(string root, string scenario)
    {
        const string packageDigest = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var payload = Encoding.UTF8.GetBytes("fixture-bootstrap");
        var payloadDigest = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        var bootstrapPath = Path.Combine(root, "runtime-bootstrap.fixture.json");
        var bootstrap = new
        {
            contractVersion = FixtureRuntimeBootstrapEnvelopeValidator.ContractVersion,
            packagePayloadSha256 = "sha256:" + packageDigest,
            payloadSha256 = "sha256:" + payloadDigest,
            customerId = "fixture-customer-001",
            tenantId = "fixture-tenant-001",
            installationId = "fixture-install-001",
            environmentId = "fixture-environment-001",
            deploymentExportId = "fixture-export-001",
            runtimeReleaseId = "fixture-runtime-001",
            idempotencyKey = "fixture-bootstrap-apply-001",
            payloadBase64 = Convert.ToBase64String(payload)
        };
        await File.WriteAllTextAsync(bootstrapPath, JsonSerializer.Serialize(bootstrap), new UTF8Encoding(false));

        return new FixtureLifecycleRunnerRequest
        {
            ContractVersion = FixtureLifecycleRunner.RunnerContractVersion,
            FixtureOnly = true,
            TestOnlyEnabled = true,
            AllowCloudMutation = false,
            RunId = "fixture-run-001",
            Environment = "disposable-sandbox",
            SubscriptionId = "fixture-subscription-001",
            AllowedSubscriptionIds = ["fixture-subscription-001"],
            ResourceGroupName = "rg-pm365-harness-fixture-001",
            Confirmation = "RUN-FIXTURE-LIFECYCLE:fixture-run-001",
            OutputRoot = root,
            RuntimeBootstrapPath = bootstrapPath,
            VerifiedPackagePayloadSha256 = packageDigest,
            CustomerId = "fixture-customer-001",
            TenantId = "fixture-tenant-001",
            InstallationId = "fixture-install-001",
            EnvironmentId = "fixture-environment-001",
            DeploymentExportId = "fixture-export-001",
            RuntimeReleaseId = "fixture-runtime-001",
            OnboardingSessionId = "fixture-onboarding-001",
            Scenario = scenario,
            InducePortalOutageOnce = true
        };
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "pm365-fixture-lifecycle-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly string _name;
        private readonly string? _original;

        public EnvironmentVariableScope(string name, string value)
        {
            _name = name;
            _original = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose() => Environment.SetEnvironmentVariable(_name, _original);
    }
}
