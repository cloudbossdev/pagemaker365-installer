using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PageMaker365.Installer.Engine.Models;
using PageMaker365.Installer.Engine.Services;

namespace PageMaker365.Installer.Engine.Tests;

[Flags]
internal enum RuntimeBridgeTestFailure
{
    None = 0,
    SecondWhatIf = 1,
    HandlerAmbiguous = 2,
    Recovery = 4,
    LicenseWrite = 8,
    CursorWrite = 16,
    ApprovalTwo = 32,
    HandlerFailure = 64,
    SecondWhatIfCancellation = 128
}

internal sealed class RuntimeBridgeTestHarness
{
    internal static readonly DateTimeOffset Now = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);
    internal RuntimeBridgeSyntheticTestCapability Capability { get; } = RuntimeBridgeSyntheticTestCapability.CreateForTestSupport();
    internal List<string> Trace { get; } = [];
    internal RuntimeBridgeTestFailure Failure { get; }
    internal RuntimeDeploymentRecoveryBridge Bridge { get; }
    internal RuntimeConfigurationCatalogV1Authority Catalog { get; }
    internal PackageTrustOptions Trust { get; }
    internal string LicensePublicKeyPem { get; }
    internal string PackageJson { get; }
    internal string WorkspaceRoot { get; }
    internal TestArtifactTransport ArtifactTransport { get; }
    internal TestLicenseTransport LicenseTransport { get; }
    internal TestCursorGenerator CursorGenerator { get; }
    internal TestWriteSink WriteSink { get; }
    internal TestWhatIf WhatIf { get; }
    internal TestApproval Approval { get; }
    internal TestHandler Handler { get; }
    internal TestRecovery Recovery { get; }

    internal RuntimeBridgeTestHarness(RuntimeBridgeTestFailure failure = RuntimeBridgeTestFailure.None)
    {
        Failure = failure;
        var root = FindRepositoryRoot();
        var fixture = Path.Combine(root, "tests", "PageMaker365.Installer.Engine.Tests", "Fixtures", "private-runtime-v07-cross-repository-rehearsal-v1");
        PackageJson = File.ReadAllText(Path.Combine(fixture, "customer-install-0.7.json"), new UTF8Encoding(false, true));
        Catalog = RuntimeConfigurationCatalogV1Authority.Create(
            File.ReadAllBytes(Path.Combine(fixture, "runtime-configuration.catalog.json")),
            File.ReadAllBytes(Path.Combine(fixture, "runtime-configuration.schema.json")));
        using var trustJson = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(fixture, "signing-trust.json")));
        var keyId = trustJson.RootElement.GetProperty("keyId").GetString()!;
        var packagePublicKey = File.ReadAllText(Path.Combine(fixture, "signing-public-key.pem"), new UTF8Encoding(false, true));
        LicensePublicKeyPem = File.ReadAllText(Path.Combine(fixture, "license-signing-public-key.pem"), new UTF8Encoding(false, true));
        Trust = new PackageTrustOptions { TrustedPublicKeysById = new Dictionary<string, string>(StringComparer.Ordinal) { [keyId] = packagePublicKey } };
        WorkspaceRoot = Path.Combine(Path.GetTempPath(), "pm365-inst003-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(WorkspaceRoot);
        ArtifactTransport = new TestArtifactTransport(Capability, fixture, Trace);
        LicenseTransport = new TestLicenseTransport(Capability, fixture, Trace);
        CursorGenerator = new TestCursorGenerator(Capability, Trace);
        WriteSink = new TestWriteSink(Capability, Trace, failure);
        WhatIf = new TestWhatIf(Capability, Trace, failure);
        Approval = new TestApproval(Capability, Trace, failure);
        Handler = new TestHandler(Capability, Trace, failure);
        Recovery = new TestRecovery(Capability, Trace, failure);
        Bridge = new RuntimeDeploymentRecoveryBridge(Capability, Catalog, Trust, LicensePublicKeyPem, Now, ArtifactTransport, LicenseTransport,
            CursorGenerator, WriteSink, WhatIf, Approval, Handler, Recovery);
    }

    internal RuntimeBridgeInvocation Invocation(bool enabled = true) => new(PackageJson, WorkspaceRoot, "0.0.0-synthetic", enabled);
    internal void AssertCapabilityMismatchDenied()
    {
        var other = RuntimeBridgeSyntheticTestCapability.CreateForTestSupport();
        AssertEx.Throws<InvalidDataException>(() => new RuntimeDeploymentRecoveryBridge(Capability, Catalog, Trust, LicensePublicKeyPem, Now, ArtifactTransport,
            LicenseTransport, new TestCursorGenerator(other, Trace), WriteSink, WhatIf, Approval, Handler, Recovery));
    }
    internal RuntimeBridgeResult RunWithWrongLicenseTrust()
    {
        var bridge = new RuntimeDeploymentRecoveryBridge(Capability, Catalog, Trust,
            "-----BEGIN PUBLIC KEY-----\nMCowBQYDK2VwAyEAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=\n-----END PUBLIC KEY-----\n",
            Now, ArtifactTransport, LicenseTransport, CursorGenerator, WriteSink, WhatIf, Approval, Handler, Recovery);
        return bridge.RunAsync(Invocation()).GetAwaiter().GetResult();
    }
    internal void Dispose() { if (Directory.Exists(WorkspaceRoot)) Directory.Delete(WorkspaceRoot, recursive: true); }

    internal sealed class TestArtifactTransport(RuntimeBridgeSyntheticTestCapability capability, string fixture, List<string> trace) : IRuntimeBridgeArtifactTransport
    {
        public RuntimeBridgeSyntheticTestCapability Capability { get; } = capability;
        internal int SessionCount { get; private set; }
        internal int AcquireCount { get; private set; }
        internal int ReceiptCount { get; private set; }
        public RuntimeBridgeArtifactSession CreateSession(PrivateRuntimeDeliveryPackageV07 package, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); SessionCount++; trace.Add("session");
            return new("rds_SYNTHETIC_W09_REHEARSAL_0001", DateTimeOffset.Parse("2099-08-30T12:00:00.000Z"));
        }
        public RuntimeBridgeArtifactResponse Acquire(PrivateRuntimeDeliveryPackageV07 package, RuntimeBridgeArtifactSession session, string artifactKind, bool range, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); AcquireCount++; trace.Add($"artifact-{artifactKind}-{(range ? "range" : "full")}");
            var bytes = File.ReadAllBytes(Path.Combine(fixture, "artifacts", artifactKind + ".zip"));
            if (!range) return new(artifactKind, false, 0, bytes.Length, Sha256(bytes), "private, no-store", "no-cache", "nosniff", true, bytes);
            var offset = artifactKind == "api" ? 17 : 29;
            var count = artifactKind == "api" ? 97 : 131;
            return new(artifactKind, true, offset, bytes.Length, Sha256(bytes), "private, no-store", "no-cache", "nosniff", true, bytes.AsSpan(offset, count).ToArray());
        }
        public RuntimeBridgeArtifactReceipt SubmitReceipt(PrivateRuntimeDeliveryPackageV07 package, RuntimeBridgeArtifactSession session, IReadOnlyList<RuntimeBridgeVerifiedArtifact> artifacts, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); ReceiptCount++; trace.Add("artifact-receipt");
            if (artifacts.Count != 2) throw new InvalidDataException("synthetic_artifact_receipt");
            return new(session.SessionId, package.PackageHash, "completed", 1);
        }
    }

    internal sealed class TestLicenseTransport : IRuntimeBridgeProtectedLicenseTransport
    {
        private readonly JsonElement positive;
        private readonly List<string> trace;
        public RuntimeBridgeSyntheticTestCapability Capability { get; }
        internal int CallCount { get; private set; }
        internal byte[]? ReturnedBuffer { get; private set; }
        internal TestLicenseTransport(RuntimeBridgeSyntheticTestCapability capability, string fixture, List<string> trace)
        {
            Capability = capability; this.trace = trace;
            using var doc = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(fixture, "protected-setting-acquisition-http-vectors.json")));
            positive = doc.RootElement.GetProperty("positive").Clone();
        }
        public RuntimeBridgeProtectedLicenseResponse AcquireOnce(PrivateRuntimeDeliveryPackageV07 package, RuntimeBridgeArtifactSession session, RuntimeConfigurationProtectedSettingV2 descriptor, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); CallCount++; trace.Add("license-acquire");
            var response = positive.GetProperty("response");
            ReturnedBuffer = PrivateRuntimeCanonicalJson.Canonicalize(response.GetProperty("value"));
            return new(response.GetProperty("contractVersion").GetString()!, response.GetProperty("packageHash").GetString()!,
                response.GetProperty("targetApp").GetString()!, response.GetProperty("name").GetString()!,
                positive.GetProperty("request").GetProperty("reference").GetString()!, "private, no-store", "no-cache", "nosniff",
                "Authorization, X-PM365-Onboarding-Session, X-PM365-Runtime-Delivery-Session", true, ReturnedBuffer);
        }
    }

    internal sealed class TestCursorGenerator(RuntimeBridgeSyntheticTestCapability capability, List<string> trace) : IRuntimeBridgeCursorGenerator
    {
        public RuntimeBridgeSyntheticTestCapability Capability { get; } = capability;
        internal int CallCount { get; private set; }
        internal byte[]? ReturnedBuffer { get; private set; }
        public byte[] Generate(int entropyBytes)
        {
            CallCount++; trace.Add("cursor-generate"); ReturnedBuffer = RandomNumberGenerator.GetBytes(entropyBytes); return ReturnedBuffer;
        }
    }

    internal sealed class TestWriteSink(RuntimeBridgeSyntheticTestCapability capability, List<string> trace, RuntimeBridgeTestFailure failure) : IRuntimeBridgeProtectedWriteSink
    {
        public RuntimeBridgeSyntheticTestCapability Capability { get; } = capability;
        internal List<ReadOnlyMemory<byte>> RetainedBuffers { get; } = [];
        internal int CallCount { get; private set; }
        public RuntimeBridgeProtectedWriteReceipt Write(RuntimeBridgeProtectedWriteRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); CallCount++; trace.Add("write-" + request.Name); RetainedBuffers.Add(request.ValueUtf8);
            if (failure.HasFlag(RuntimeBridgeTestFailure.LicenseWrite) && request.Name == "API_LICENSE_SIGNED_PAYLOAD") throw new InvalidOperationException("synthetic_write_denial");
            if (failure.HasFlag(RuntimeBridgeTestFailure.CursorWrite) && request.Name == "API_IMAGE_ASSET_CURSOR_SECRET") throw new InvalidOperationException("synthetic_write_denial");
            var digest = Sha256(request.ValueUtf8.ToArray());
            var version = CallCount == 1 ? new string('a', 32) : new string('b', 32);
            var vault = "/subscriptions/61b7c2e9-8f34-45ad-b062-3ea19d75f48c/resourceGroups/pm365-fixture/providers/Microsoft.KeyVault/vaults/pm365fixture";
            if (request.VaultResourceId != vault) throw new InvalidDataException("synthetic_vault");
            return new("rwr_" + new string(CallCount == 1 ? 'A' : 'B', 24), request.Name, request.Mode, request.VaultResourceId,
                request.SecretName, version, $"@Microsoft.KeyVault(SecretUri=https://pm365fixture.vault.azure.net/secrets/{request.SecretName}/{version})",
                digest, request.PackageHash, request.ApprovalDigest, "written", 1);
        }
    }

    internal sealed class TestWhatIf(RuntimeBridgeSyntheticTestCapability capability, List<string> trace, RuntimeBridgeTestFailure failure) : IRuntimeBridgeWhatIf
    {
        public RuntimeBridgeSyntheticTestCapability Capability { get; } = capability;
        internal int CallCount { get; private set; }
        internal List<RuntimeBridgeWhatIfRequest> Requests { get; } = [];
        public RuntimeBridgeWhatIfResult Preview(RuntimeBridgeWhatIfRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); CallCount++; Requests.Add(request); trace.Add("whatif-" + request.Phase);
            if (failure.HasFlag(RuntimeBridgeTestFailure.SecondWhatIf) && request.Phase == "final") throw new InvalidDataException("synthetic_second_whatif");
            if (failure.HasFlag(RuntimeBridgeTestFailure.SecondWhatIfCancellation) && request.Phase == "final") throw new OperationCanceledException("synthetic_cancelled");
            var json = JsonSerializer.Serialize(new { request.Phase, request.PackageHash, request.InputSha256, request.ArtifactIdentitySha256, request.PhaseOneApprovalDigest, request.ReceiptIdentitySha256s }) + "\n";
            var requestSha = Sha256(Encoding.UTF8.GetBytes($"{request.Phase}\n{request.PackageHash}\n{request.InputSha256}\n{request.ArtifactIdentitySha256}\n{request.PhaseOneApprovalDigest}\n{string.Join(',', request.ReceiptIdentitySha256s)}\n"));
            return new(request.Phase, "previewed", requestSha, json, Sha256(Encoding.UTF8.GetBytes(json)), 0, 0);
        }
    }

    internal sealed class TestApproval(RuntimeBridgeSyntheticTestCapability capability, List<string> trace, RuntimeBridgeTestFailure failure) : IRuntimeBridgeApproval
    {
        public RuntimeBridgeSyntheticTestCapability Capability { get; } = capability;
        internal int CallCount { get; private set; }
        public RuntimeBridgeApprovalReceipt Approve(RuntimeBridgeApprovalChallenge challenge, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); CallCount++; trace.Add("approval-" + challenge.Phase);
            if (failure.HasFlag(RuntimeBridgeTestFailure.ApprovalTwo) && challenge.Phase == "final") throw new InvalidDataException("synthetic_approval_denial");
            var id = "approval-" + CallCount;
            return new(id, challenge.Phase, challenge.ChallengeSha256, challenge.Nonce, challenge.ExpiresAt, "approved", 1,
                Sha256(Encoding.UTF8.GetBytes(id + "\n" + challenge.ChallengeSha256 + "\n")));
        }
    }

    internal sealed class TestHandler(RuntimeBridgeSyntheticTestCapability capability, List<string> trace, RuntimeBridgeTestFailure failure) : IRuntimeBridgeSyntheticHandler
    {
        public RuntimeBridgeSyntheticTestCapability Capability { get; } = capability;
        internal int CallCount { get; private set; }
        public RuntimeBridgeSimulationResult Simulate(RuntimeBridgeSimulationRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); CallCount++; trace.Add("handler");
            if (request.AuthorizesDeployment) throw new InvalidDataException("synthetic_authorization");
            if (failure.HasFlag(RuntimeBridgeTestFailure.HandlerAmbiguous)) throw new RuntimeBridgeTerminalAmbiguityException("synthetic_ambiguous");
            if (failure.HasFlag(RuntimeBridgeTestFailure.HandlerFailure)) throw new InvalidOperationException("synthetic_handler_failure");
            return new("simulated", 0, 0, 0, Sha256(Encoding.UTF8.GetBytes(request.FinalInputSha256)));
        }
    }

    internal sealed class TestRecovery(RuntimeBridgeSyntheticTestCapability capability, List<string> trace, RuntimeBridgeTestFailure failure) : IRuntimeBridgeRecovery
    {
        public RuntimeBridgeSyntheticTestCapability Capability { get; } = capability;
        internal int CallCount { get; private set; }
        public RuntimeBridgeRecoveryResult Recover(RuntimeBridgeProtectedWriteReceipt receipt, CancellationToken cancellationToken)
        {
            CallCount++; trace.Add("recover-" + receipt.Name);
            if (failure.HasFlag(RuntimeBridgeTestFailure.Recovery)) throw new InvalidOperationException("synthetic_recovery");
            return new(receipt.ReceiptId, "recovered", 1);
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null) { if (File.Exists(Path.Combine(directory.FullName, "PageMaker365.Installer.sln"))) return directory.FullName; directory = directory.Parent; }
        throw new DirectoryNotFoundException();
    }

    internal static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
