using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PageMaker365.Installer.Engine.Models;
using PageMaker365.Installer.Engine.Services;

namespace PageMaker365.Installer.Engine.Tests;

[Flags]
internal enum RuntimeBridgeTestFailure : long
{
    None = 0,
    SecondWhatIf = 1L << 0,
    HandlerAmbiguous = 1L << 1,
    LicenseWrite = 1L << 2,
    CursorWrite = 1L << 3,
    ApprovalTwo = 1L << 4,
    HandlerFailure = 1L << 5,
    SecondWhatIfCancellation = 1L << 6,
    CursorRecoveryFailure = 1L << 7,
    LicenseRecoveryFailure = 1L << 8,
    CursorRecoveryAmbiguous = 1L << 9,
    LicenseRecoveryAmbiguous = 1L << 10,
    ArtifactFullStatus = 1L << 11,
    ArtifactFullVector = 1L << 12,
    ArtifactFullReference = 1L << 13,
    ArtifactFullPackage = 1L << 14,
    ArtifactFullSession = 1L << 15,
    ArtifactFullEtag = 1L << 16,
    ArtifactFullAcceptRanges = 1L << 17,
    ArtifactFullContentRange = 1L << 18,
    ArtifactFullContentLength = 1L << 19,
    ArtifactFullBodyFile = 1L << 20,
    ArtifactFullHeaders = 1L << 21,
    ArtifactFullRedirect = 1L << 22,
    ArtifactFullBody = 1L << 23,
    ArtifactRangeStatus = 1L << 24,
    ArtifactRangeVector = 1L << 25,
    ArtifactRangeReference = 1L << 26,
    ArtifactRangePackage = 1L << 27,
    ArtifactRangeSession = 1L << 28,
    ArtifactRangeEtag = 1L << 29,
    ArtifactRangeAcceptRanges = 1L << 30,
    ArtifactRangeContentRange = 1L << 31,
    ArtifactRangeContentLength = 1L << 32,
    ArtifactRangeBodyFile = 1L << 33,
    ArtifactRangeHeaders = 1L << 34,
    ArtifactRangeRedirect = 1L << 35,
    ArtifactRangeBody = 1L << 36,
    StageMarkerMissing = 1L << 37,
    StageUnexpectedPath = 1L << 38,
    LicenseAlgorithm = 1L << 39,
    LicenseKeyId = 1L << 40,
    LicensePublicKeyDigest = 1L << 41,
    LicenseCanonicalization = 1L << 42,
    LicensePayloadDigest = 1L << 43,
    LicenseFingerprint = 1L << 44,
    LicenseFingerprintDomain = 1L << 45,
    LicenseSignature = 1L << 46,
    LicenseSubscription = 1L << 47,
    HandlerPackage = 1L << 48,
    HandlerInput = 1L << 49,
    HandlerPreview = 1L << 50,
    HandlerApproval = 1L << 51,
    HandlerArtifact = 1L << 52,
    HandlerAuthorizes = 1L << 53,
    HandlerResourceCount = 1L << 54,
    HandlerWriteCount = 1L << 55,
    HandlerDeploymentCount = 1L << 56,
    HandlerDigest = 1L << 57,
    ArtifactFullKind = 1L << 58,
    ArtifactFullTotalLength = 1L << 59,
    ArtifactFullOffset = 1L << 60,
    ArtifactFullSha = 1L << 61,
    ArtifactRangeShape = 1L << 62
}

internal enum PortableOwnedStageMutation
{
    None,
    Unsupported,
    RootIdentitySubstitution,
    StageIdentitySubstitution,
    MarkerRemoval,
    MarkerSubstitution,
    RootReparse,
    StageLink,
    ComponentReparse,
    ComponentLink,
    FileHardlink,
    ComponentIdentitySubstitution,
    UnexpectedInventory,
    ExistingTargetCollision,
    CleanupMarkerSubstitution,
    CleanupUnexpectedInventory,
    CleanupStageIdentitySubstitution
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
    internal RuntimeBridgeLicenseAuthority LicenseAuthority { get; }
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
    internal TestOwnedStageStore? PortableStageStore { get; }

    internal RuntimeBridgeTestHarness(
        RuntimeBridgeTestFailure failure = RuntimeBridgeTestFailure.None,
        string volatileIdentity = "A",
        PortableOwnedStageMutation? portableStageMutation = null)
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
        LicensePublicKeyPem = string.Concat(File.ReadAllText(Path.Combine(fixture, "license-signing-public-key.pem"), new UTF8Encoding(false, true))
            .Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd(), "\n");
        LicenseAuthority = CreateLicenseAuthority(fixture, LicensePublicKeyPem, failure);
        Trust = new PackageTrustOptions { TrustedPublicKeysById = new Dictionary<string, string>(StringComparer.Ordinal) { [keyId] = packagePublicKey } };
        WorkspaceRoot = Path.Combine(Path.GetTempPath(), "pm365-inst003-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(WorkspaceRoot);
        ArtifactTransport = new TestArtifactTransport(Capability, fixture, WorkspaceRoot, Trace, failure);
        LicenseTransport = new TestLicenseTransport(Capability, fixture, Trace);
        CursorGenerator = new TestCursorGenerator(Capability, Trace);
        WriteSink = new TestWriteSink(Capability, Trace, failure, volatileIdentity);
        WhatIf = new TestWhatIf(Capability, Trace, failure);
        Approval = new TestApproval(Capability, Trace, failure, volatileIdentity);
        Handler = new TestHandler(Capability, Trace, failure);
        Recovery = new TestRecovery(Capability, Trace, failure);
        PortableStageStore = portableStageMutation is null ? null : new TestOwnedStageStore(Capability, portableStageMutation.Value);
        Bridge = new RuntimeDeploymentRecoveryBridge(Capability, Catalog, Trust, LicenseAuthority, Now, ArtifactTransport, LicenseTransport,
            CursorGenerator, WriteSink, WhatIf, Approval, Handler, Recovery, PortableStageStore);
    }

    internal RuntimeBridgeInvocation Invocation(
        bool enabled = true,
        string invocationId = "inv_SYNTHETIC_RUNTIME_0001",
        string? packageJson = null,
        string? installerVersion = null) =>
        new(invocationId, packageJson ?? PackageJson, WorkspaceRoot, installerVersion ?? "0.0.0-synthetic", enabled);
    internal void AssertCapabilityMismatchDenied()
    {
        var other = RuntimeBridgeSyntheticTestCapability.CreateForTestSupport();
        AssertEx.Throws<InvalidDataException>(() => new RuntimeDeploymentRecoveryBridge(Capability, Catalog, Trust, LicenseAuthority, Now, ArtifactTransport,
            LicenseTransport, new TestCursorGenerator(other, Trace), WriteSink, WhatIf, Approval, Handler, Recovery));
    }
    internal RuntimeBridgeResult RunWithWrongLicenseTrust()
    {
        var bridge = new RuntimeDeploymentRecoveryBridge(Capability, Catalog, Trust, LicenseAuthority with
        {
            PublicKeyPem = "-----BEGIN PUBLIC KEY-----\nMCowBQYDK2VwAyEAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=\n-----END PUBLIC KEY-----\n"
        },
            Now, ArtifactTransport, LicenseTransport, CursorGenerator, WriteSink, WhatIf, Approval, Handler, Recovery);
        return bridge.RunAsync(Invocation()).GetAwaiter().GetResult();
    }
    internal void Dispose() { if (Directory.Exists(WorkspaceRoot)) Directory.Delete(WorkspaceRoot, recursive: true); }

    internal sealed class TestOwnedStageStore : IRuntimeBridgeOwnedStageStore
    {
        private readonly PortableOwnedStageMutation mutation;
        private StageState? state;
        private bool delayedMutationApplied;

        internal TestOwnedStageStore(RuntimeBridgeSyntheticTestCapability capability, PortableOwnedStageMutation mutation)
        {
            Capability = capability;
            this.mutation = mutation;
        }

        public RuntimeBridgeSyntheticTestCapability Capability { get; }
        internal int CreateCount { get; private set; }
        internal int AssertCount { get; private set; }
        internal int DeleteCount { get; private set; }
        internal IReadOnlyCollection<string> UnownedPaths => state is null
            ? []
            : state.Inventory.Keys.Where(path => !state.Owned.ContainsKey(path)).OrderBy(path => path, StringComparer.Ordinal).ToArray();

        public RuntimeBridgeOwnedStageLease Create(string trustedRoot, string invocationId)
        {
            CreateCount++;
            if (mutation == PortableOwnedStageMutation.Unsupported)
                throw new PlatformNotSupportedException("runtime_bridge_stage_platform_unproved");
            if (state is not null || string.IsNullOrWhiteSpace(trustedRoot) || string.IsNullOrWhiteSpace(invocationId))
                throw new InvalidDataException("portable_stage_create_invalid");
            var root = trustedRoot.TrimEnd('/', '\\');
            var stage = root + "/portable-owned-stage-0001";
            var marker = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
            var markerPath = ".pm365-owned";
            var markerNode = new PortableNode("file", "identity-marker-1", marker.ToArray());
            state = new StageState(invocationId, root, stage, marker.ToArray(), "identity-root-1", "identity-stage-1",
                new Dictionary<string, PortableNode>(StringComparer.Ordinal) { [markerPath] = markerNode.Clone() },
                new Dictionary<string, PortableNode>(StringComparer.Ordinal) { [markerPath] = markerNode.Clone() });
            ApplyImmediateMutation(state);
            return new RuntimeBridgeOwnedStageLease(invocationId, root, stage, marker);
        }

        public void AssertOwned(RuntimeBridgeOwnedStageLease lease)
        {
            AssertCount++;
            var current = state ?? throw new InvalidDataException("portable_stage_missing");
            if (lease.InvocationId != current.InvocationId || lease.TrustedRoot != current.TrustedRoot || lease.StageRoot != current.StageRoot ||
                lease.OwnershipMarker.Length != current.Marker.Length ||
                !CryptographicOperations.FixedTimeEquals(lease.OwnershipMarker, current.Marker))
                throw new InvalidDataException("portable_stage_lease_substitution");
            if (current.RootIdentity != "identity-root-1" || current.StageIdentity != "identity-stage-1" ||
                current.RootReparse || current.RootLink || current.StageReparse || current.StageLink)
                throw new InvalidDataException("portable_stage_identity_substitution");
            if (!current.Inventory.TryGetValue(".pm365-owned", out var marker) || marker.Content is null ||
                marker.Content.Length != current.Marker.Length || !CryptographicOperations.FixedTimeEquals(marker.Content, current.Marker))
                throw new InvalidDataException("portable_stage_marker_invalid");
            if (current.Inventory.Count != current.Owned.Count)
                throw new InvalidDataException("portable_stage_inventory_invalid");
            foreach (var owned in current.Owned)
            {
                if (!current.Inventory.TryGetValue(owned.Key, out var actual) || actual.Identity != owned.Value.Identity ||
                    actual.Kind != owned.Value.Kind || actual.IsReparse || actual.IsLink ||
                    (actual.Kind == "file" && actual.LinkCount != 1))
                    throw new InvalidDataException("portable_stage_component_substitution");
            }
        }

        public void CreateDirectoryExclusive(RuntimeBridgeOwnedStageLease lease, string relativePath)
        {
            AssertOwned(lease);
            var path = RequireSafeRelative(relativePath);
            var current = state!;
            if (current.Inventory.ContainsKey(path)) throw new IOException("portable_stage_no_replace");
            EnsureParents(current, path);
            AddOwned(current, path, new PortableNode("directory", "identity-directory-" + current.NextIdentity++, null));
            ApplyDelayedMutation(current, path);
        }

        public void WriteFileExclusive(RuntimeBridgeOwnedStageLease lease, string relativePath, ReadOnlySpan<byte> bytes)
        {
            AssertOwned(lease);
            var path = RequireSafeRelative(relativePath);
            var current = state!;
            if (mutation == PortableOwnedStageMutation.ExistingTargetCollision && !delayedMutationApplied)
            {
                delayedMutationApplied = true;
                current.Inventory[path] = new PortableNode("file", "identity-unowned-collision", [0xCC]);
            }
            if (current.Inventory.ContainsKey(path)) throw new IOException("portable_stage_no_replace");
            EnsureParents(current, path);
            AddOwned(current, path, new PortableNode("file", "identity-file-" + current.NextIdentity++, bytes.ToArray()));
            ApplyDelayedMutation(current, path);
        }

        public bool Cleanup(RuntimeBridgeOwnedStageLease lease)
        {
            var current = state;
            if (current is null) return false;
            ApplyCleanupMutation(current);
            try { AssertOwned(lease); }
            catch { return false; }
            DeleteCount += current.Owned.Count;
            foreach (var node in current.Inventory.Values.Where(node => node.Content is not null))
                CryptographicOperations.ZeroMemory(node.Content!);
            foreach (var node in current.Owned.Values.Where(node => node.Content is not null))
                CryptographicOperations.ZeroMemory(node.Content!);
            CryptographicOperations.ZeroMemory(current.Marker);
            CryptographicOperations.ZeroMemory(lease.OwnershipMarker);
            current.Inventory.Clear();
            current.Owned.Clear();
            state = null;
            return true;
        }

        private void ApplyImmediateMutation(StageState current)
        {
            switch (mutation)
            {
                case PortableOwnedStageMutation.RootIdentitySubstitution: current.RootIdentity = "identity-root-2"; break;
                case PortableOwnedStageMutation.StageIdentitySubstitution: current.StageIdentity = "identity-stage-2"; break;
                case PortableOwnedStageMutation.MarkerRemoval: current.Inventory.Remove(".pm365-owned"); break;
                case PortableOwnedStageMutation.MarkerSubstitution: current.Inventory[".pm365-owned"].Content![0] ^= 0xFF; break;
                case PortableOwnedStageMutation.RootReparse: current.RootReparse = true; break;
                case PortableOwnedStageMutation.StageLink: current.StageLink = true; break;
            }
        }

        private void ApplyDelayedMutation(StageState current, string path)
        {
            if (delayedMutationApplied) return;
            switch (mutation)
            {
                case PortableOwnedStageMutation.ComponentReparse:
                    current.Inventory[path].IsReparse = true; break;
                case PortableOwnedStageMutation.ComponentLink:
                    current.Inventory[path].IsLink = true; break;
                case PortableOwnedStageMutation.FileHardlink:
                    current.Inventory[path].LinkCount = 2; break;
                case PortableOwnedStageMutation.ComponentIdentitySubstitution:
                    current.Inventory[path].Identity = "identity-component-substitute"; break;
                case PortableOwnedStageMutation.UnexpectedInventory:
                    current.Inventory["foreign.txt"] = new PortableNode("file", "identity-foreign", [0xFA]); break;
                default:
                    return;
            }
            delayedMutationApplied = true;
        }

        private void ApplyCleanupMutation(StageState current)
        {
            if (delayedMutationApplied) return;
            switch (mutation)
            {
                case PortableOwnedStageMutation.CleanupMarkerSubstitution:
                    current.Inventory[".pm365-owned"].Content![0] ^= 0xFF; break;
                case PortableOwnedStageMutation.CleanupUnexpectedInventory:
                    current.Inventory["foreign-cleanup.txt"] = new PortableNode("file", "identity-foreign-cleanup", [0xFB]); break;
                case PortableOwnedStageMutation.CleanupStageIdentitySubstitution:
                    current.StageIdentity = "identity-stage-cleanup-substitute"; break;
                default:
                    return;
            }
            delayedMutationApplied = true;
        }

        private static string RequireSafeRelative(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath) || relativePath.StartsWith('/') || relativePath.Contains(':') ||
                relativePath.Contains('\\') || relativePath.Any(char.IsControl) ||
                relativePath.Split('/').Any(part => string.IsNullOrWhiteSpace(part) || part is "." or ".."))
                throw new InvalidDataException("portable_stage_path_invalid");
            return relativePath;
        }

        private static void EnsureParents(StageState current, string path)
        {
            var segments = path.Split('/');
            var parent = "";
            foreach (var segment in segments.Take(segments.Length - 1))
            {
                parent = parent.Length == 0 ? segment : parent + "/" + segment;
                if (current.Inventory.TryGetValue(parent, out var existing))
                {
                    if (existing.Kind != "directory" || existing.IsLink || existing.IsReparse)
                        throw new InvalidDataException("portable_stage_parent_invalid");
                    continue;
                }
                AddOwned(current, parent, new PortableNode("directory", "identity-directory-" + current.NextIdentity++, null));
            }
        }

        private static void AddOwned(StageState current, string path, PortableNode node)
        {
            if (!current.Inventory.TryAdd(path, node) || !current.Owned.TryAdd(path, node.Clone()))
                throw new IOException("portable_stage_no_replace");
        }

        private sealed class StageState(
            string invocationId,
            string trustedRoot,
            string stageRoot,
            byte[] marker,
            string rootIdentity,
            string stageIdentity,
            Dictionary<string, PortableNode> inventory,
            Dictionary<string, PortableNode> owned)
        {
            internal string InvocationId { get; } = invocationId;
            internal string TrustedRoot { get; } = trustedRoot;
            internal string StageRoot { get; } = stageRoot;
            internal byte[] Marker { get; } = marker;
            internal string RootIdentity { get; set; } = rootIdentity;
            internal string StageIdentity { get; set; } = stageIdentity;
            internal bool RootReparse { get; set; }
            internal bool RootLink { get; set; }
            internal bool StageReparse { get; set; }
            internal bool StageLink { get; set; }
            internal int NextIdentity { get; set; } = 1;
            internal Dictionary<string, PortableNode> Inventory { get; } = inventory;
            internal Dictionary<string, PortableNode> Owned { get; } = owned;
        }

        private sealed class PortableNode(string kind, string identity, byte[]? content)
        {
            internal string Kind { get; } = kind;
            internal string Identity { get; set; } = identity;
            internal byte[]? Content { get; } = content;
            internal bool IsReparse { get; set; }
            internal bool IsLink { get; set; }
            internal int LinkCount { get; set; } = 1;
            internal PortableNode Clone() => new(Kind, Identity, Content?.ToArray())
            {
                IsReparse = IsReparse,
                IsLink = IsLink,
                LinkCount = LinkCount
            };
        }
    }

    private static RuntimeBridgeLicenseAuthority CreateLicenseAuthority(string fixture, string publicKeyPem, RuntimeBridgeTestFailure failure)
    {
        using var vector = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(fixture, "license-signature-vector.json")));
        using var acquisition = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(fixture, "protected-setting-acquisition-http-vectors.json")));
        var root = vector.RootElement;
        var signedLicense = acquisition.RootElement.GetProperty("positive").GetProperty("response").GetProperty("value");
        var authority = new RuntimeBridgeLicenseAuthority(
            root.GetProperty("algorithm").GetString()!, root.GetProperty("keyId").GetString()!, publicKeyPem,
            root.GetProperty("publicKeySha256").GetString()!, root.GetProperty("canonicalization").GetString()!,
            root.GetProperty("signedPayloadSha256").GetString()!, root.GetProperty("signedPayloadFingerprint").GetString()!,
            "json-c14n-v1:license-payload", root.GetProperty("signature").GetString()!,
            signedLicense.GetProperty("payload").GetProperty("subscriptionId").GetString()!);
        return authority with
        {
            Algorithm = failure.HasFlag(RuntimeBridgeTestFailure.LicenseAlgorithm) ? "Ed448" : authority.Algorithm,
            KeyId = failure.HasFlag(RuntimeBridgeTestFailure.LicenseKeyId) ? "bad" : authority.KeyId,
            PublicKeySha256 = failure.HasFlag(RuntimeBridgeTestFailure.LicensePublicKeyDigest) ? new string('0', 64) : authority.PublicKeySha256,
            Canonicalization = failure.HasFlag(RuntimeBridgeTestFailure.LicenseCanonicalization) ? "json-unknown" : authority.Canonicalization,
            SignedPayloadSha256 = failure.HasFlag(RuntimeBridgeTestFailure.LicensePayloadDigest) ? new string('1', 64) : authority.SignedPayloadSha256,
            SignedPayloadFingerprint = failure.HasFlag(RuntimeBridgeTestFailure.LicenseFingerprint) ? new string('2', 64) : authority.SignedPayloadFingerprint,
            FingerprintDomain = failure.HasFlag(RuntimeBridgeTestFailure.LicenseFingerprintDomain) ? "wrong-domain" : authority.FingerprintDomain,
            Signature = failure.HasFlag(RuntimeBridgeTestFailure.LicenseSignature) ? new string('A', authority.Signature.Length) : authority.Signature,
            SubscriptionId = failure.HasFlag(RuntimeBridgeTestFailure.LicenseSubscription) ? Guid.Empty.ToString() : authority.SubscriptionId
        };
    }

    internal sealed class TestArtifactTransport(
        RuntimeBridgeSyntheticTestCapability capability,
        string fixture,
        string workspaceRoot,
        List<string> trace,
        RuntimeBridgeTestFailure failure) : IRuntimeBridgeArtifactTransport
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
        public RuntimeBridgeArtifactResponse Acquire(
            PrivateRuntimeDeliveryPackageV07 package,
            RuntimeBridgeArtifactSession session,
            RuntimeBridgeArtifactRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AcquireCount++;
            var range = request.RangeOffset is not null;
            trace.Add($"artifact-{request.ArtifactKind}-{(range ? "range" : "full")}");
            var expectedReference = request.ArtifactKind == "api" ? package.ApiDeliveryReference : package.PortalDeliveryReference;
            var expectedOffset = request.ArtifactKind == "api" ? 17L : 29L;
            var expectedLength = request.ArtifactKind == "api" ? 97 : 131;
            var bytes = File.ReadAllBytes(Path.Combine(fixture, "artifacts", request.ArtifactKind + ".zip"));
            var expectedEtag = $"\"sha256:{Sha256(bytes)}\"";
            if (request.VectorId != $"{request.ArtifactKind}-{(range ? "range" : "full")}" ||
                request.ArtifactReference != expectedReference || request.PackageHash != package.PackageHash ||
                request.SessionId != session.SessionId || request.IfMatch != expectedEtag ||
                (!range && (request.RangeOffset is not null || request.RangeLength is not null)) ||
                (range && (request.RangeOffset != expectedOffset || request.RangeLength != expectedLength)))
                throw new InvalidDataException("synthetic_artifact_request_invalid");

            var response = new RuntimeBridgeArtifactResponse(
                request.ArtifactKind, request.VectorId, expectedReference, package.PackageHash, session.SessionId,
                range ? 206 : 200, range, range ? expectedOffset : 0, bytes.LongLength, Sha256(bytes), expectedEtag,
                "bytes", range ? $"bytes {expectedOffset}-{expectedOffset + expectedLength - 1}/{bytes.LongLength}" : null,
                range ? expectedLength : bytes.LongLength, $"artifacts/{request.ArtifactKind}.zip", "private, no-store", "no-cache", "nosniff", true,
                range ? bytes.AsSpan((int)expectedOffset, expectedLength).ToArray() : bytes);

            if (!range)
            {
                response = response with
                {
                    StatusCode = failure.HasFlag(RuntimeBridgeTestFailure.ArtifactFullStatus) ? 206 : response.StatusCode,
                    ArtifactKind = failure.HasFlag(RuntimeBridgeTestFailure.ArtifactFullKind) ? "wrong" : response.ArtifactKind,
                    VectorId = failure.HasFlag(RuntimeBridgeTestFailure.ArtifactFullVector) ? "wrong-full" : response.VectorId,
                    ArtifactReference = failure.HasFlag(RuntimeBridgeTestFailure.ArtifactFullReference) ? "rda_wrong" : response.ArtifactReference,
                    PackageHash = failure.HasFlag(RuntimeBridgeTestFailure.ArtifactFullPackage) ? new string('0', 64) : response.PackageHash,
                    SessionId = failure.HasFlag(RuntimeBridgeTestFailure.ArtifactFullSession) ? "rds_wrong" : response.SessionId,
                    ETag = failure.HasFlag(RuntimeBridgeTestFailure.ArtifactFullEtag) ? "\"wrong\"" : response.ETag,
                    AcceptRanges = failure.HasFlag(RuntimeBridgeTestFailure.ArtifactFullAcceptRanges) ? null : response.AcceptRanges,
                    ContentRange = failure.HasFlag(RuntimeBridgeTestFailure.ArtifactFullContentRange) ? $"bytes 0-{bytes.Length - 1}/{bytes.Length}" : response.ContentRange,
                    ContentLength = failure.HasFlag(RuntimeBridgeTestFailure.ArtifactFullContentLength) ? bytes.Length - 1 : response.ContentLength,
                    TotalLength = failure.HasFlag(RuntimeBridgeTestFailure.ArtifactFullTotalLength) ? bytes.Length - 1 : response.TotalLength,
                    Offset = failure.HasFlag(RuntimeBridgeTestFailure.ArtifactFullOffset) ? 1 : response.Offset,
                    Sha256 = failure.HasFlag(RuntimeBridgeTestFailure.ArtifactFullSha) ? new string('6', 64) : response.Sha256,
                    BodyFile = failure.HasFlag(RuntimeBridgeTestFailure.ArtifactFullBodyFile) ? "artifacts/wrong.zip" : response.BodyFile,
                    CacheControl = failure.HasFlag(RuntimeBridgeTestFailure.ArtifactFullHeaders) ? "public" : response.CacheControl,
                    NoRedirect = !failure.HasFlag(RuntimeBridgeTestFailure.ArtifactFullRedirect) && response.NoRedirect,
                    Body = failure.HasFlag(RuntimeBridgeTestFailure.ArtifactFullBody) ? Mutate(response.Body) : response.Body
                };
                if (failure.HasFlag(RuntimeBridgeTestFailure.StageMarkerMissing) || failure.HasFlag(RuntimeBridgeTestFailure.StageUnexpectedPath))
                {
                    var stage = Directory.GetDirectories(workspaceRoot, "pm365-synthetic-runtime-*").Single();
                    if (failure.HasFlag(RuntimeBridgeTestFailure.StageMarkerMissing)) File.Delete(Path.Combine(stage, ".pm365-owned"));
                    if (failure.HasFlag(RuntimeBridgeTestFailure.StageUnexpectedPath)) File.WriteAllText(Path.Combine(stage, "foreign.txt"), "not owned");
                }
            }
            else
            {
                response = response with
                {
                    StatusCode = failure.HasFlag(RuntimeBridgeTestFailure.ArtifactRangeStatus) ? 200 : response.StatusCode,
                    VectorId = failure.HasFlag(RuntimeBridgeTestFailure.ArtifactRangeVector) ? "wrong-range" : response.VectorId,
                    ArtifactReference = failure.HasFlag(RuntimeBridgeTestFailure.ArtifactRangeReference) ? "rda_wrong" : response.ArtifactReference,
                    PackageHash = failure.HasFlag(RuntimeBridgeTestFailure.ArtifactRangePackage) ? new string('0', 64) : response.PackageHash,
                    SessionId = failure.HasFlag(RuntimeBridgeTestFailure.ArtifactRangeSession) ? "rds_wrong" : response.SessionId,
                    ETag = failure.HasFlag(RuntimeBridgeTestFailure.ArtifactRangeEtag) ? "\"wrong\"" : response.ETag,
                    AcceptRanges = failure.HasFlag(RuntimeBridgeTestFailure.ArtifactRangeAcceptRanges) ? null : response.AcceptRanges,
                    ContentRange = failure.HasFlag(RuntimeBridgeTestFailure.ArtifactRangeContentRange) ? $"bytes {expectedOffset + 1}-{expectedOffset + expectedLength}/{bytes.Length}" : response.ContentRange,
                    ContentLength = failure.HasFlag(RuntimeBridgeTestFailure.ArtifactRangeContentLength) ? expectedLength - 1 : response.ContentLength,
                    ArtifactKind = failure.HasFlag(RuntimeBridgeTestFailure.ArtifactRangeShape) ? "wrong" : response.ArtifactKind,
                    TotalLength = failure.HasFlag(RuntimeBridgeTestFailure.ArtifactRangeShape) ? bytes.Length - 1 : response.TotalLength,
                    Offset = failure.HasFlag(RuntimeBridgeTestFailure.ArtifactRangeShape) ? expectedOffset + 1 : response.Offset,
                    Sha256 = failure.HasFlag(RuntimeBridgeTestFailure.ArtifactRangeShape) ? new string('7', 64) : response.Sha256,
                    BodyFile = failure.HasFlag(RuntimeBridgeTestFailure.ArtifactRangeBodyFile) ? "artifacts/wrong.zip" : response.BodyFile,
                    Pragma = failure.HasFlag(RuntimeBridgeTestFailure.ArtifactRangeHeaders) ? "cache" : response.Pragma,
                    NoRedirect = !failure.HasFlag(RuntimeBridgeTestFailure.ArtifactRangeRedirect) && response.NoRedirect,
                    Body = failure.HasFlag(RuntimeBridgeTestFailure.ArtifactRangeBody) ? Mutate(response.Body) : response.Body
                };
            }
            return response;
        }

        private static byte[] Mutate(byte[] original)
        {
            var mutated = original.ToArray();
            mutated[0] ^= 0x5A;
            return mutated;
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

    internal sealed class TestWriteSink(
        RuntimeBridgeSyntheticTestCapability capability,
        List<string> trace,
        RuntimeBridgeTestFailure failure,
        string volatileIdentity) : IRuntimeBridgeProtectedWriteSink
    {
        public RuntimeBridgeSyntheticTestCapability Capability { get; } = capability;
        internal List<ReadOnlyMemory<byte>> RetainedBuffers { get; } = [];
        internal List<string> ReceiptIds { get; } = [];
        internal int CallCount { get; private set; }
        public RuntimeBridgeProtectedWriteReceipt Write(RuntimeBridgeProtectedWriteRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); CallCount++; trace.Add("write-" + request.Name); RetainedBuffers.Add(request.ValueUtf8);
            if (failure.HasFlag(RuntimeBridgeTestFailure.LicenseWrite) && request.Name == "API_LICENSE_SIGNED_PAYLOAD") throw new InvalidOperationException("synthetic_write_denial");
            if (failure.HasFlag(RuntimeBridgeTestFailure.CursorWrite) && request.Name == "API_IMAGE_ASSET_CURSOR_SECRET") throw new InvalidOperationException("synthetic_write_denial");
            var digest = Sha256(request.ValueUtf8.Span);
            var version = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"version:{request.Name}"))).ToLowerInvariant()[..32];
            var vault = "/subscriptions/61b7c2e9-8f34-45ad-b062-3ea19d75f48c/resourceGroups/pm365-fixture/providers/Microsoft.KeyVault/vaults/pm365fixture";
            if (request.VaultResourceId != vault) throw new InvalidDataException("synthetic_vault");
            var receiptId = "rwr_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"receipt:{volatileIdentity}:{CallCount}:{request.Name}"))).ToLowerInvariant()[..24];
            ReceiptIds.Add(receiptId);
            return new(receiptId, request.Name, request.Mode, request.VaultResourceId,
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

    internal sealed class TestApproval(
        RuntimeBridgeSyntheticTestCapability capability,
        List<string> trace,
        RuntimeBridgeTestFailure failure,
        string volatileIdentity) : IRuntimeBridgeApproval
    {
        public RuntimeBridgeSyntheticTestCapability Capability { get; } = capability;
        internal int CallCount { get; private set; }
        internal List<string> ApprovalIds { get; } = [];
        public RuntimeBridgeApprovalReceipt Approve(RuntimeBridgeApprovalChallenge challenge, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); CallCount++; trace.Add("approval-" + challenge.Phase);
            if (failure.HasFlag(RuntimeBridgeTestFailure.ApprovalTwo) && challenge.Phase == "final") throw new InvalidDataException("synthetic_approval_denial");
            var id = $"approval-{volatileIdentity}-{CallCount}";
            ApprovalIds.Add(id);
            return new(id, challenge.Phase, challenge.ChallengeSha256, challenge.Nonce, challenge.ExpiresAt, "approved", 1,
                Sha256(Encoding.UTF8.GetBytes(id + "\n" + challenge.ChallengeSha256 + "\n")));
        }
    }

    internal sealed class TestHandler(RuntimeBridgeSyntheticTestCapability capability, List<string> trace, RuntimeBridgeTestFailure failure) : IRuntimeBridgeSyntheticHandler
    {
        public RuntimeBridgeSyntheticTestCapability Capability { get; } = capability;
        internal int CallCount { get; private set; }
        internal RuntimeBridgeSimulationResult? LastResult { get; private set; }
        public RuntimeBridgeSimulationResult Simulate(RuntimeBridgeSimulationRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); CallCount++; trace.Add("handler");
            if (request.AuthorizesDeployment) throw new InvalidDataException("synthetic_authorization");
            if (failure.HasFlag(RuntimeBridgeTestFailure.HandlerAmbiguous)) throw new RuntimeBridgeTerminalAmbiguityException("synthetic_ambiguous");
            if (failure.HasFlag(RuntimeBridgeTestFailure.HandlerFailure)) throw new InvalidOperationException("synthetic_handler_failure");
            var artifactIdentity = ArtifactIdentity(request.Artifacts);
            var packageHash = failure.HasFlag(RuntimeBridgeTestFailure.HandlerPackage) ? new string('0', 64) : request.PackageHash;
            var inputSha = failure.HasFlag(RuntimeBridgeTestFailure.HandlerInput) ? new string('1', 64) : request.FinalInputSha256;
            var previewSha = failure.HasFlag(RuntimeBridgeTestFailure.HandlerPreview) ? new string('2', 64) : request.FinalPreviewSha256;
            var approvalSha = failure.HasFlag(RuntimeBridgeTestFailure.HandlerApproval) ? new string('3', 64) : request.PhaseTwoApprovalDigest;
            var artifactSha = failure.HasFlag(RuntimeBridgeTestFailure.HandlerArtifact) ? new string('4', 64) : artifactIdentity;
            var authorizes = failure.HasFlag(RuntimeBridgeTestFailure.HandlerAuthorizes);
            var resourceCount = failure.HasFlag(RuntimeBridgeTestFailure.HandlerResourceCount) ? 1 : 0;
            var writeCount = failure.HasFlag(RuntimeBridgeTestFailure.HandlerWriteCount) ? 1 : 0;
            var deploymentCount = failure.HasFlag(RuntimeBridgeTestFailure.HandlerDeploymentCount) ? 1 : 0;
            var digest = SimulationResultSha256(packageHash, inputSha, previewSha, approvalSha, artifactSha,
                "simulated", authorizes, resourceCount, writeCount, deploymentCount);
            if (failure.HasFlag(RuntimeBridgeTestFailure.HandlerDigest)) digest = new string('5', 64);
            LastResult = new(packageHash, inputSha, previewSha, approvalSha, artifactSha, "simulated", authorizes,
                resourceCount, writeCount, deploymentCount, digest);
            return LastResult;
        }
    }

    internal sealed class TestRecovery(RuntimeBridgeSyntheticTestCapability capability, List<string> trace, RuntimeBridgeTestFailure failure) : IRuntimeBridgeRecovery
    {
        public RuntimeBridgeSyntheticTestCapability Capability { get; } = capability;
        internal int CallCount { get; private set; }
        public RuntimeBridgeRecoveryResult Recover(RuntimeBridgeProtectedWriteReceipt receipt, CancellationToken cancellationToken)
        {
            CallCount++; trace.Add("recover-" + receipt.Name);
            var cursor = receipt.Name == "API_IMAGE_ASSET_CURSOR_SECRET";
            if ((cursor && failure.HasFlag(RuntimeBridgeTestFailure.CursorRecoveryFailure)) ||
                (!cursor && failure.HasFlag(RuntimeBridgeTestFailure.LicenseRecoveryFailure)))
                throw new InvalidOperationException("synthetic_recovery");
            if ((cursor && failure.HasFlag(RuntimeBridgeTestFailure.CursorRecoveryAmbiguous)) ||
                (!cursor && failure.HasFlag(RuntimeBridgeTestFailure.LicenseRecoveryAmbiguous)))
                return new(receipt.ReceiptId, "unknown", 0);
            return new(receipt.ReceiptId, "recovered", 1);
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null) { if (File.Exists(Path.Combine(directory.FullName, "PageMaker365.Installer.sln"))) return directory.FullName; directory = directory.Parent; }
        throw new DirectoryNotFoundException();
    }

    internal static string ArtifactIdentity(IEnumerable<RuntimeBridgeVerifiedArtifact> artifacts) =>
        Sha256(Encoding.UTF8.GetBytes(string.Join("\n", artifacts.Select(item => $"{item.ArtifactKind}:{item.Sha256}:{item.SizeBytes}:{item.ExtractedTreeSha256}")) + "\n"));

    internal static string SimulationResultSha256(
        string packageHash,
        string finalInputSha256,
        string finalPreviewSha256,
        string approvalBindingSha256,
        string artifactIdentitySha256,
        string status,
        bool authorizesDeployment,
        int resourceCount,
        int writeCount,
        int deploymentCount) => Sha256(Encoding.UTF8.GetBytes(
            $"{packageHash}\n{finalInputSha256}\n{finalPreviewSha256}\n{approvalBindingSha256}\n{artifactIdentitySha256}\n{status}\n{authorizesDeployment.ToString().ToLowerInvariant()}\n{resourceCount}\n{writeCount}\n{deploymentCount}\n"));

    internal static string Sha256(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
