using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Win32.SafeHandles;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;
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

internal enum RuntimeBridgeHttpOperation
{
    Session,
    ArtifactFull,
    ArtifactRange,
    Receipt,
    Protected
}

internal enum RuntimeBridgeHttpFault
{
    Method,
    Path,
    Query,
    Fragment,
    RequestHeaderMissing,
    RequestHeaderExtra,
    RequestHeaderReordered,
    RequestHeaderDuplicate,
    RequestHeaderWrongRole,
    RequestContentType,
    RequestBody,
    Status,
    ResponseHeaderMissing,
    ResponseHeaderExtra,
    ResponseHeaderReordered,
    ResponseHeaderDuplicate,
    ResponseHeaderWrongValue,
    ResponseContentType,
    Location,
    ResponseBody
}

internal sealed record RuntimeBridgeHttpMutation(RuntimeBridgeHttpOperation Operation, RuntimeBridgeHttpFault Fault);

internal enum RuntimeBridgeReceiptDigestFault { None, Missing, Uppercase, Stale, CrossPair }
internal enum RuntimeBridgeNativeStageAttack
{
    TrustedRootBeforeBind,
    TrustedRootAfterBind,
    ParentBeforeStageCreate,
    ParentAfterValidationBeforeStageCreate,
    StageBeforeBind,
    StageAfterBind,
    MarkerBeforeBind,
    DirectoryBeforeBind,
    DirectoryCaseAlias,
    FileBeforeBind,
    FileCaseAlias,
    FileHardlinkAlias,
    FileSymbolicAlias,
    TrueDirectoryJunction,
    EnumerationSecondWriter,
    CleanupIdentitySubstitution
}
internal enum RuntimeBridgeFileSymbolicLinkCapability { Established, OsDenied }
internal sealed record RuntimeBridgeFileSymbolicLinkCapabilityResult(
    RuntimeBridgeFileSymbolicLinkCapability Capability,
    int NativeErrorCode);
internal enum RuntimeBridgeReceiptNestedFault
{
    None,
    RequestArtifactsExtra,
    RequestArtifactExtra,
    RequestArtifactItemWrong,
    RequestSafeResultExtra,
    RequestSafeResultWrong,
    ResponseReceiptExtra,
    ResponseArtifactsExtra,
    ResponseArtifactExtra,
    ResponseSafeResultExtra,
    ResponseSafeResultWrong
}

internal sealed record RuntimeBridgeMeasuredVectorOutcome(
    RuntimeBridgeResult Result,
    int Status,
    string? ErrorCode,
    int ReadCount,
    int MutationCount,
    int ResponseBytes,
    int SessionCalls,
    int ReceiptCalls,
    int ProtectedWrites,
    int RandomGenerations,
    int Previews,
    int Approvals,
    int HandlerCalls,
    int Recoveries,
    int CleanupCalls,
    RuntimeBridgeTwoCallRaceObservation? RaceObservation,
    string? DurableWinnerIdentity,
    string? DurableState,
    int CompetingStatusCode,
    int CompetingBodyBytes);

internal sealed record RuntimeBridgeTwoCallRaceObservation(
    int CallCount,
    int WinnerCount,
    int ReplayCount,
    int ConflictCount,
    int LoserCount);

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
    internal TestNativeStageRaceProbe? NativeStageRaceProbe { get; }
    internal TestCancellationProbe? CancellationProbe { get; }
    internal CancellationTokenSource? BoundaryCancellation { get; }

    internal RuntimeBridgeTestHarness(
        RuntimeBridgeTestFailure failure = RuntimeBridgeTestFailure.None,
        string volatileIdentity = "A",
        PortableOwnedStageMutation? portableStageMutation = null,
        byte[]? cursorEntropy = null,
        RuntimeBridgeHttpMutation? httpMutation = null,
        bool probeNativeStageRaces = false,
        byte licenseVariant = 0,
        RuntimeBridgeReceiptDigestFault receiptDigestFault = RuntimeBridgeReceiptDigestFault.None,
        RuntimeBridgeReceiptNestedFault receiptNestedFault = RuntimeBridgeReceiptNestedFault.None,
        string? cancellationCheckpoint = null,
        RuntimeBridgeNativeStageAttack? nativeStageAttack = null,
        string? deliveryNegativeMutation = null,
        string? protectedNegativeMutation = null,
        string? failureCheckpoint = null)
    {
        Failure = failure;
        var root = FindRepositoryRoot();
        var fixture = Path.Combine(root, "tests", "PageMaker365.Installer.Engine.Tests", "Fixtures", "private-runtime-v07-cross-repository-rehearsal-v1");
        var packageJson = File.ReadAllText(Path.Combine(fixture, "customer-install-0.7.json"), new UTF8Encoding(false, true));
        Catalog = RuntimeConfigurationCatalogV1Authority.Create(
            File.ReadAllBytes(Path.Combine(fixture, "runtime-configuration.catalog.json")),
            File.ReadAllBytes(Path.Combine(fixture, "runtime-configuration.schema.json")));
        using var trustJson = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(fixture, "signing-trust.json")));
        var keyId = trustJson.RootElement.GetProperty("keyId").GetString()!;
        var packagePublicKey = File.ReadAllText(Path.Combine(fixture, "signing-public-key.pem"), new UTF8Encoding(false, true));
        var licensePublicKeyPem = string.Concat(File.ReadAllText(Path.Combine(fixture, "license-signing-public-key.pem"), new UTF8Encoding(false, true))
            .Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd(), "\n");
        var licenseAuthority = CreateLicenseAuthority(fixture, licensePublicKeyPem, failure);
        var trust = new PackageTrustOptions { TrustedPublicKeysById = new Dictionary<string, string>(StringComparer.Ordinal) { [keyId] = packagePublicKey } };
        byte[]? signedLicenseOverride = null;
        if (licenseVariant != 0)
        {
            var reissued = ReissuePackageAndLicense(fixture, packageJson, licenseVariant);
            packageJson = reissued.PackageJson;
            trust = reissued.Trust;
            licensePublicKeyPem = reissued.LicenseAuthority.PublicKeyPem;
            licenseAuthority = reissued.LicenseAuthority;
            signedLicenseOverride = reissued.SignedLicenseUtf8;
        }
        PackageJson = packageJson;
        Trust = trust;
        LicensePublicKeyPem = licensePublicKeyPem;
        LicenseAuthority = licenseAuthority;
        WorkspaceRoot = Path.Combine(Path.GetTempPath(), "pm365-inst003-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(WorkspaceRoot);
        ArtifactTransport = new TestArtifactTransport(Capability, fixture, WorkspaceRoot, Trace, failure, httpMutation, receiptNestedFault,
            deliveryNegativeMutation);
        LicenseTransport = new TestLicenseTransport(Capability, fixture, Trace, httpMutation, signedLicenseOverride, protectedNegativeMutation);
        CursorGenerator = new TestCursorGenerator(Capability, Trace, cursorEntropy);
        WriteSink = new TestWriteSink(Capability, Trace, failure, volatileIdentity, receiptDigestFault);
        WhatIf = new TestWhatIf(Capability, Trace, failure);
        Approval = new TestApproval(Capability, Trace, failure, volatileIdentity);
        Handler = new TestHandler(Capability, Trace, failure);
        Recovery = new TestRecovery(Capability, Trace, failure);
        PortableStageStore = portableStageMutation is null ? null : new TestOwnedStageStore(Capability, portableStageMutation.Value);
        NativeStageRaceProbe = probeNativeStageRaces || nativeStageAttack is not null
            ? new TestNativeStageRaceProbe(Capability, nativeStageAttack ?? RuntimeBridgeNativeStageAttack.TrustedRootAfterBind) : null;
        if (cancellationCheckpoint is not null || failureCheckpoint is not null)
        {
            BoundaryCancellation = new CancellationTokenSource();
            CancellationProbe = new TestCancellationProbe(Capability, BoundaryCancellation, cancellationCheckpoint, failureCheckpoint);
        }
        Bridge = new RuntimeDeploymentRecoveryBridge(Capability, Catalog, Trust, LicenseAuthority, Now, ArtifactTransport, LicenseTransport,
            CursorGenerator, WriteSink, WhatIf, Approval, Handler, Recovery, PortableStageStore, NativeStageRaceProbe, CancellationProbe);
    }

    internal sealed class TestCancellationProbe(
        RuntimeBridgeSyntheticTestCapability capability,
        CancellationTokenSource cancellation,
        string? cancelAtCheckpoint,
        string? failAtCheckpoint) : IRuntimeBridgeCancellationProbe
    {
        public RuntimeBridgeSyntheticTestCapability Capability { get; } = capability;
        internal int Count { get; private set; }
        internal List<string> Phases { get; } = [];
        public void Probe(string label, string phase)
        {
            Count++;
            var checkpoint = label + ":" + phase;
            Phases.Add(checkpoint);
            if (checkpoint == cancelAtCheckpoint) cancellation.Cancel();
            if (checkpoint == failAtCheckpoint) throw new InvalidDataException("runtime_bridge_synthetic_boundary_failure");
        }
    }

    internal static RuntimeBridgeMeasuredVectorOutcome ExecuteDeliveryNegative(string mutation)
    {
        var harness = new RuntimeBridgeTestHarness(deliveryNegativeMutation: mutation);
        try
        {
            var result = harness.Bridge.RunAsync(harness.Invocation()).GetAwaiter().GetResult();
            if (mutation == "concurrent-downloads") _ = harness.ArtifactTransport.CompetingDownloadTask!.GetAwaiter().GetResult();
            if (mutation is "receipt-event-mismatch" or "receipt-replay") _ = harness.ArtifactTransport.CompetingReceiptTask!.GetAwaiter().GetResult();
            var competingStatus = mutation == "concurrent-downloads" ? harness.ArtifactTransport.CompetingDownloadTask!.Result.StatusCode :
                mutation is "receipt-event-mismatch" or "receipt-replay" ? harness.ArtifactTransport.CompetingReceiptTask!.Result.StatusCode : 0;
            var competingBody = mutation == "concurrent-downloads" ? harness.ArtifactTransport.CompetingDownloadTask!.Result.Body.Length :
                mutation is "receipt-event-mismatch" or "receipt-replay" ? harness.ArtifactTransport.CompetingReceiptTask!.Result.ResponseBodyUtf8.Length : 0;
            return harness.Measure(result, harness.ArtifactTransport.LastServerStatus, harness.ArtifactTransport.LastServerErrorCode,
                harness.ArtifactTransport.ArtifactOpenCount, harness.ArtifactTransport.ReceiptMutationCount,
                harness.ArtifactTransport.LastResponseBodyBytes,
                harness.ArtifactTransport.ConcurrentDownloadRace ?? harness.ArtifactTransport.ConcurrentReceiptRace,
                harness.ArtifactTransport.ConcurrentDownloadWinner ?? harness.ArtifactTransport.ConcurrentReceiptWinner,
                mutation == "concurrent-downloads" ? harness.ArtifactTransport.ArtifactDurableState :
                    mutation is "receipt-event-mismatch" or "receipt-replay" ? harness.ArtifactTransport.ReceiptDurableState : null,
                competingStatus, competingBody);
        }
        finally { harness.Dispose(); }
    }

    internal static RuntimeBridgeMeasuredVectorOutcome ExecuteProtectedNegative(string mutation)
    {
        var harness = new RuntimeBridgeTestHarness(protectedNegativeMutation: mutation);
        try
        {
            var result = harness.Bridge.RunAsync(harness.Invocation()).GetAwaiter().GetResult();
            if (mutation == "concurrent-redemption") _ = harness.LicenseTransport.CompetingProtectedTask!.GetAwaiter().GetResult();
            return harness.Measure(result, harness.LicenseTransport.LastServerStatus, harness.LicenseTransport.LastServerErrorCode,
                harness.LicenseTransport.ProtectedReadCount, harness.LicenseTransport.RedemptionCount,
                harness.LicenseTransport.LastResponseBodyBytes, harness.LicenseTransport.ConcurrentRedemptionRace,
                harness.LicenseTransport.ConcurrentRedemptionWinner,
                mutation == "concurrent-redemption" ? harness.LicenseTransport.ProtectedDurableState : null,
                harness.LicenseTransport.CompetingProtectedStatusCode, harness.LicenseTransport.CompetingProtectedBodyBytes);
        }
        finally { harness.Dispose(); }
    }

    private RuntimeBridgeMeasuredVectorOutcome Measure(RuntimeBridgeResult result, int status, string? errorCode,
        int reads, int mutations, int responseBytes, RuntimeBridgeTwoCallRaceObservation? raceObservation,
        string? durableWinnerIdentity, string? durableState, int competingStatusCode, int competingBodyBytes) =>
        new(result, status, errorCode, reads, mutations, responseBytes, ArtifactTransport.SessionCount,
            ArtifactTransport.ReceiptCount, WriteSink.CallCount, CursorGenerator.CallCount, WhatIf.CallCount,
            Approval.CallCount, Handler.CallCount, Recovery.CallCount, PortableStageStore?.CleanupCount ?? (result.StageCleaned ? 1 : 0),
            raceObservation, durableWinnerIdentity, durableState, competingStatusCode, competingBodyBytes);

    internal sealed class TestNativeStageRaceProbe(
        RuntimeBridgeSyntheticTestCapability capability,
        RuntimeBridgeNativeStageAttack attack) : IRuntimeBridgeOwnedStageRaceProbe
    {
        public RuntimeBridgeSyntheticTestCapability Capability { get; } = capability;
        internal int ProbeCount { get; private set; }
        internal int DeniedCount { get; private set; }
        internal int UnavailableCount { get; private set; }
        internal int UnexpectedSuccessCount { get; private set; }
        internal List<string> UnexpectedOperations { get; } = [];
        internal bool AttackApplied { get; private set; }
        internal string? ForeignPath { get; private set; }
        internal string? ForeignTargetPath { get; private set; }
        internal string? ForeignTargetRoot { get; private set; }
        internal string? ForeignTargetSha256 { get; private set; }

        internal static RuntimeBridgeFileSymbolicLinkCapabilityResult ProbeFileSymbolicLinkCapability()
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("native_file_symbolic_link_probe_requires_windows");
            var root = NewOwnedTemporaryPath("pm365-file-symbolic-link-capability-");
            var target = Path.Combine(root, "owned-target.bin");
            var link = Path.Combine(root, "owned-link.bin");
            var content = Encoding.ASCII.GetBytes("pagemaker365-owned-symbolic-link-capability\n");
            try
            {
                if (Directory.Exists(root) || File.Exists(root)) throw new IOException("native_symbolic_link_probe_path_collision");
                Directory.CreateDirectory(root);
                using (var stream = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    stream.Write(content);
                try
                {
                    File.CreateSymbolicLink(link, target);
                }
                catch (Exception ex) when (IsExactSymbolicLinkAccessDenial(ex))
                {
                    var nativeCode = NativeErrorCode(ex);
                    CleanupCapabilityProbe(root, target, link);
                    return new(RuntimeBridgeFileSymbolicLinkCapability.OsDenied, nativeCode);
                }

                var linkInfo = new FileInfo(link);
                if (!File.Exists(link) || (File.GetAttributes(link) & FileAttributes.ReparsePoint) == 0 ||
                    string.IsNullOrWhiteSpace(linkInfo.LinkTarget))
                    throw new InvalidDataException("native_symbolic_link_probe_not_reparse_link");
                var resolved = linkInfo.ResolveLinkTarget(returnFinalTarget: true) ??
                    throw new InvalidDataException("native_symbolic_link_probe_target_unresolved");
                if (!string.Equals(Path.GetFullPath(resolved.FullName), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase) ||
                    !File.ReadAllBytes(resolved.FullName).SequenceEqual(content))
                    throw new InvalidDataException("native_symbolic_link_probe_target_identity_invalid");
                File.Delete(link);
                if (File.Exists(link) || !File.Exists(target) || !File.ReadAllBytes(target).SequenceEqual(content))
                    throw new InvalidDataException("native_symbolic_link_probe_link_delete_changed_target");
                File.Delete(target);
                Directory.Delete(root, recursive: false);
                AssertCapabilityProbeClean(root, target, link);
                return new(RuntimeBridgeFileSymbolicLinkCapability.Established, 0);
            }
            catch
            {
                CleanupCapabilityProbe(root, target, link);
                throw;
            }
        }

        public void Probe(string operation, string path)
        {
            if (AttackApplied || !Applies(operation, attack)) return;
            ProbeCount++;
            AttackApplied = true;
            var replacement = path + ".race-substitute";
            try
            {
                switch (attack)
                {
                    case RuntimeBridgeNativeStageAttack.TrustedRootBeforeBind:
                    case RuntimeBridgeNativeStageAttack.TrustedRootAfterBind:
                    case RuntimeBridgeNativeStageAttack.ParentBeforeStageCreate:
                    case RuntimeBridgeNativeStageAttack.ParentAfterValidationBeforeStageCreate:
                    case RuntimeBridgeNativeStageAttack.StageBeforeBind:
                    case RuntimeBridgeNativeStageAttack.StageAfterBind:
                    case RuntimeBridgeNativeStageAttack.DirectoryBeforeBind:
                    case RuntimeBridgeNativeStageAttack.FileBeforeBind:
                    case RuntimeBridgeNativeStageAttack.MarkerBeforeBind:
                        if (Directory.Exists(path))
                        {
                            Directory.Move(path, replacement);
                            Directory.CreateDirectory(path);
                        }
                        else
                        {
                            File.Move(path, replacement);
                            File.WriteAllBytes(path, [0xCC]);
                        }
                        ForeignPath = replacement;
                        break;
                    case RuntimeBridgeNativeStageAttack.DirectoryCaseAlias:
                        Directory.CreateDirectory(ToggleCase(path));
                        throw new IOException("native_case_alias_collision");
                    case RuntimeBridgeNativeStageAttack.FileCaseAlias:
                        using (File.Open(ToggleCase(path), FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
                        break;
                    case RuntimeBridgeNativeStageAttack.FileHardlinkAlias:
                        if (!CreateHardLinkNative(replacement, path, IntPtr.Zero)) throw new IOException("native_hardlink_denied");
                        ForeignPath = replacement; break;
                    case RuntimeBridgeNativeStageAttack.FileSymbolicAlias:
                        if (!TryCreateForeignSymbolicAlias(replacement, out var foreignRoot, out var foreignTarget, out var foreignSha256))
                        {
                            DeniedCount++;
                            return;
                        }
                        ForeignPath = replacement;
                        ForeignTargetRoot = foreignRoot;
                        ForeignTargetPath = foreignTarget;
                        ForeignTargetSha256 = foreignSha256;
                        break;
                    case RuntimeBridgeNativeStageAttack.TrueDirectoryJunction:
                        CreateJunction(replacement, path); ForeignPath = replacement; break;
                    case RuntimeBridgeNativeStageAttack.EnumerationSecondWriter:
                    case RuntimeBridgeNativeStageAttack.CleanupIdentitySubstitution:
                        replacement = Path.Combine(path, "foreign-native-directory"); Directory.CreateDirectory(replacement); ForeignPath = replacement; break;
                }
                UnexpectedSuccessCount++;
                UnexpectedOperations.Add(operation + ":" + (Directory.Exists(replacement) ? "directory" : "file"));
            }
            catch (IOException) { DeniedCount++; }
            catch (UnauthorizedAccessException) { DeniedCount++; }
            catch (PlatformNotSupportedException) { UnavailableCount++; }
        }

        internal void AssertForeignSymbolicAliasSurvives()
        {
            if (ForeignPath is null || ForeignTargetPath is null || ForeignTargetRoot is null || ForeignTargetSha256 is null)
                throw new InvalidDataException("native_foreign_symbolic_alias_identity_missing");
            var link = new FileInfo(ForeignPath);
            if (!File.Exists(ForeignPath) || (File.GetAttributes(ForeignPath) & FileAttributes.ReparsePoint) == 0 ||
                string.IsNullOrWhiteSpace(link.LinkTarget))
                throw new InvalidDataException("native_foreign_symbolic_alias_missing");
            var resolved = link.ResolveLinkTarget(returnFinalTarget: true) ??
                throw new InvalidDataException("native_foreign_symbolic_alias_unresolved");
            if (!string.Equals(Path.GetFullPath(resolved.FullName), Path.GetFullPath(ForeignTargetPath), StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(ForeignTargetPath) ||
                !string.Equals(Sha256(File.ReadAllBytes(ForeignTargetPath)), ForeignTargetSha256, StringComparison.Ordinal))
                throw new InvalidDataException("native_foreign_symbolic_alias_target_changed");
        }

        internal void CleanupForeignSymbolicAliasForTest()
        {
            if (ForeignPath is not null && File.Exists(ForeignPath)) File.Delete(ForeignPath);
            if (ForeignTargetPath is not null && File.Exists(ForeignTargetPath)) File.Delete(ForeignTargetPath);
            if (ForeignTargetRoot is not null && Directory.Exists(ForeignTargetRoot)) Directory.Delete(ForeignTargetRoot, recursive: false);
            if ((ForeignPath is not null && File.Exists(ForeignPath)) ||
                (ForeignTargetPath is not null && File.Exists(ForeignTargetPath)) ||
                (ForeignTargetRoot is not null && Directory.Exists(ForeignTargetRoot)))
                throw new InvalidDataException("native_foreign_symbolic_alias_cleanup_failed");
        }

        private static bool TryCreateForeignSymbolicAlias(string link, out string root, out string target, out string sha256)
        {
            root = NewOwnedTemporaryPath("pm365-file-symbolic-alias-target-");
            target = Path.Combine(root, "foreign-owned-target.bin");
            sha256 = string.Empty;
            var content = Encoding.ASCII.GetBytes("pagemaker365-foreign-owned-symbolic-target\n");
            try
            {
                if (Directory.Exists(root) || File.Exists(root)) throw new IOException("native_foreign_symbolic_target_collision");
                Directory.CreateDirectory(root);
                using (var stream = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    stream.Write(content);
                sha256 = Sha256(content);
                File.CreateSymbolicLink(link, target);
                return true;
            }
            catch (Exception ex) when (IsExactSymbolicLinkAccessDenial(ex))
            {
                File.Delete(target);
                Directory.Delete(root, recursive: false);
                root = string.Empty;
                target = string.Empty;
                sha256 = string.Empty;
                return false;
            }
            catch (Exception ex)
            {
                if (File.Exists(link)) File.Delete(link);
                if (File.Exists(target)) File.Delete(target);
                if (Directory.Exists(root)) Directory.Delete(root, recursive: false);
                throw new InvalidDataException("native_foreign_symbolic_alias_setup_failed", ex);
            }
        }

        private static string NewOwnedTemporaryPath(string prefix) =>
            Path.Combine(Path.GetTempPath(), prefix + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant());

        private static bool IsExactSymbolicLinkAccessDenial(Exception exception) =>
            exception is IOException or UnauthorizedAccessException && NativeErrorCode(exception) is 5 or 1314;

        private static int NativeErrorCode(Exception exception) => exception.HResult & 0xFFFF;

        private static void CleanupCapabilityProbe(string root, string target, string link)
        {
            if (File.Exists(link)) File.Delete(link);
            if (File.Exists(target)) File.Delete(target);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: false);
            AssertCapabilityProbeClean(root, target, link);
        }

        private static void AssertCapabilityProbeClean(string root, string target, string link)
        {
            if (File.Exists(link) || File.Exists(target) || Directory.Exists(root))
                throw new InvalidDataException("native_symbolic_link_probe_cleanup_failed");
        }

        private static string Sha256(byte[] bytes) =>
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        private static bool Applies(string operation, RuntimeBridgeNativeStageAttack value) => value switch
        {
            RuntimeBridgeNativeStageAttack.TrustedRootBeforeBind => operation == "trusted-root-before-handle-bind",
            RuntimeBridgeNativeStageAttack.TrustedRootAfterBind => operation == "trusted-root-after-handle-bind",
            RuntimeBridgeNativeStageAttack.ParentBeforeStageCreate => operation == "parent-before-stage-create",
            RuntimeBridgeNativeStageAttack.ParentAfterValidationBeforeStageCreate => operation == "parent-after-validation-before-stage-create",
            RuntimeBridgeNativeStageAttack.StageBeforeBind => operation == "stage-after-create-before-handle-bind",
            RuntimeBridgeNativeStageAttack.StageAfterBind => operation == "stage-after-handle-bind",
            RuntimeBridgeNativeStageAttack.MarkerBeforeBind => operation == "marker-after-create-before-handle-bind",
            RuntimeBridgeNativeStageAttack.DirectoryBeforeBind or RuntimeBridgeNativeStageAttack.TrueDirectoryJunction => operation == "directory-after-create-before-handle-bind",
            RuntimeBridgeNativeStageAttack.DirectoryCaseAlias => operation == "directory-after-handle-bind",
            RuntimeBridgeNativeStageAttack.FileBeforeBind => operation == "file-after-create-before-handle-bind",
            RuntimeBridgeNativeStageAttack.FileCaseAlias or RuntimeBridgeNativeStageAttack.FileHardlinkAlias or RuntimeBridgeNativeStageAttack.FileSymbolicAlias =>
                operation == "file-after-handle-bind-before-write",
            RuntimeBridgeNativeStageAttack.EnumerationSecondWriter => operation == "inventory-after-enumeration",
            RuntimeBridgeNativeStageAttack.CleanupIdentitySubstitution => operation == "cleanup-after-validation-before-delete",
            _ => false
        };

        private static string ToggleCase(string path)
        {
            var name = Path.GetFileName(path);
            var first = name[0];
            var toggled = char.IsUpper(first) ? char.ToLowerInvariant(first) : char.ToUpperInvariant(first);
            return Path.Combine(Path.GetDirectoryName(path)!, toggled + name[1..]);
        }

        private static void CreateJunction(string junction, string target)
        {
            Directory.CreateDirectory(junction);
            using var handle = CreateFileForJunction(junction, 0x40000000u, 0, IntPtr.Zero, 3, 0x02200000u, IntPtr.Zero);
            if (handle.IsInvalid) throw new IOException("native_junction_handle_denied");
            var substitute = Encoding.Unicode.GetBytes("\\??\\" + Path.GetFullPath(target));
            var print = Encoding.Unicode.GetBytes(Path.GetFullPath(target));
            var pathBytes = checked(substitute.Length + 2 + print.Length + 2);
            var buffer = new byte[16 + pathBytes];
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(0, 4), 0xA0000003u);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(4, 2), checked((ushort)(8 + pathBytes)));
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(8, 2), 0);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(10, 2), checked((ushort)substitute.Length));
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(12, 2), checked((ushort)(substitute.Length + 2)));
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(14, 2), checked((ushort)print.Length));
            substitute.CopyTo(buffer, 16);
            print.CopyTo(buffer, 18 + substitute.Length);
            if (!DeviceIoControl(handle, 0x000900A4u, buffer, buffer.Length, IntPtr.Zero, 0, out _, IntPtr.Zero))
                throw new IOException("native_junction_create_denied");
        }

        [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CreateHardLinkNative(string fileName, string existingFileName, IntPtr securityAttributes);
        [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFileForJunction(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
            uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeviceIoControl(SafeFileHandle device, uint controlCode, byte[] input, int inputSize,
            IntPtr output, int outputSize, out int bytesReturned, IntPtr overlapped);
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
    internal void Dispose()
    {
        if (!Directory.Exists(WorkspaceRoot)) return;
        try { Directory.Delete(WorkspaceRoot, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

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
        internal int CleanupCount { get; private set; }
        internal bool Exists => state is not null;
        internal int InventoryCount => state?.Inventory.Count ?? 0;
        internal int OwnedCount => state?.Owned.Count ?? 0;
        internal IReadOnlyCollection<string> UnownedPaths => state is null
            ? []
            : state.Inventory.Keys.Where(path => !state.Owned.ContainsKey(path)).OrderBy(path => path, StringComparer.Ordinal).ToArray();

        public RuntimeBridgeOwnedStageLease Create(
            string trustedRoot,
            string invocationId,
            IReadOnlyList<RuntimeBridgeOwnedStageEntry> inventory)
        {
            CreateCount++;
            if (mutation == PortableOwnedStageMutation.Unsupported)
                throw new PlatformNotSupportedException("runtime_bridge_stage_platform_unproved");
            if (state is not null || string.IsNullOrWhiteSpace(trustedRoot) || string.IsNullOrWhiteSpace(invocationId) || inventory.Count == 0)
                throw new InvalidDataException("portable_stage_create_invalid");
            var predeclared = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (var entry in inventory)
            {
                var path = RequireSafeRelative(entry.RelativePath);
                if (path == ".pm365-owned" || !predeclared.TryAdd(path, entry.IsDirectory))
                    throw new InvalidDataException("portable_stage_inventory_invalid");
            }
            var root = trustedRoot.TrimEnd('/', '\\');
            var stage = root + "/portable-owned-stage-0001";
            var marker = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
            var markerPath = ".pm365-owned";
            var markerNode = new PortableNode("file", "identity-marker-1", marker.ToArray());
            state = new StageState(invocationId, root, stage, marker.ToArray(), "identity-root-1", "identity-stage-1",
                new Dictionary<string, PortableNode>(StringComparer.Ordinal) { [markerPath] = markerNode.Clone() },
                new Dictionary<string, PortableNode>(StringComparer.Ordinal) { [markerPath] = markerNode.Clone() },
                predeclared);
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

        public void AssertComplete(RuntimeBridgeOwnedStageLease lease)
        {
            AssertOwned(lease);
            var current = state!;
            if (current.Owned.Count != current.Predeclared.Count + 1 ||
                current.Predeclared.Any(item => !current.Owned.TryGetValue(item.Key, out var node) ||
                    (node.Kind == "directory") != item.Value))
                throw new InvalidDataException("portable_stage_inventory_incomplete");
        }

        public void CreateDirectoryExclusive(RuntimeBridgeOwnedStageLease lease, string relativePath)
        {
            AssertOwned(lease);
            var path = RequireSafeRelative(relativePath);
            var current = state!;
            if (current.Inventory.ContainsKey(path)) throw new IOException("portable_stage_no_replace");
            RequirePredeclared(current, path, isDirectory: true);
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
            RequirePredeclared(current, path, isDirectory: false);
            EnsureParents(current, path);
            AddOwned(current, path, new PortableNode("file", "identity-file-" + current.NextIdentity++, bytes.ToArray()));
            ApplyDelayedMutation(current, path);
        }

        public bool Cleanup(RuntimeBridgeOwnedStageLease lease)
        {
            CleanupCount++;
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
                RequirePredeclared(current, parent, isDirectory: true);
                AddOwned(current, parent, new PortableNode("directory", "identity-directory-" + current.NextIdentity++, null));
            }
        }

        private static void RequirePredeclared(StageState current, string path, bool isDirectory)
        {
            if (!current.Predeclared.TryGetValue(path, out var declaredDirectory) || declaredDirectory != isDirectory)
                throw new InvalidDataException("portable_stage_path_not_predeclared");
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
            Dictionary<string, PortableNode> owned,
            Dictionary<string, bool> predeclared)
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
            internal Dictionary<string, bool> Predeclared { get; } = predeclared;
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

    private static ReissuedPackageAndLicense ReissuePackageAndLicense(string fixture, string packageJson, byte variant)
    {
        var packageKey = DeterministicPrivateKey($"package-{variant}");
        var licenseKey = DeterministicPrivateKey($"license-{variant}");
        var packageKeyId = $"test-only-inst003-package-{variant:D3}";
        var licenseKeyId = $"test-only-inst003-license-{variant:D3}";
        var packagePem = PublicKeyPem(packageKey.GeneratePublicKey());
        var licensePem = PublicKeyPem(licenseKey.GeneratePublicKey());

        var root = JsonNode.Parse(packageJson)!.AsObject();
        var settings = root["runtimeConfiguration"]!["publicSettings"]!.AsArray();
        settings.Single(node => node!["name"]!.GetValue<string>() == "API_LICENSE_PUBLIC_KEY_PEM")!["value"] = licensePem;
        var projection = root["runtimeConfiguration"]!.AsObject();
        projection.Remove("projectionSha256");
        using (var projectionDocument = JsonDocument.Parse(projection.ToJsonString()))
            projection["projectionSha256"] = PrivateRuntimeCanonicalJson.Sha256(PrivateRuntimeCanonicalJson.Canonicalize(projectionDocument.RootElement));
        root["controlPlane"]!["signingKeyId"] = packageKeyId;
        using (var unsigned = JsonDocument.Parse(root.ToJsonString()))
        {
            var payload = PrivateRuntimeCanonicalJson.Canonicalize(unsigned.RootElement, excludePackageIntegrity: true);
            root["controlPlane"]!["packageHash"] = "sha256:" + PrivateRuntimeCanonicalJson.Sha256(payload);
            root["controlPlane"]!["signature"] = EncodeBase64Url(Sign(packageKey, payload));
        }
        using var packageDocument = JsonDocument.Parse(root.ToJsonString());
        var canonicalPackage = PrivateRuntimeDeliveryV07PackageService.FormatCanonicalPackage(packageDocument.RootElement);

        using var acquisition = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(fixture, "protected-setting-acquisition-http-vectors.json")));
        var license = JsonNode.Parse(acquisition.RootElement.GetProperty("positive").GetProperty("response").GetProperty("value").GetRawText())!.AsObject();
        using (var payloadDocument = JsonDocument.Parse(license["payload"]!.ToJsonString()))
        {
            var payload = PrivateRuntimeCanonicalJson.Canonicalize(payloadDocument.RootElement);
            license["signature"]!["kid"] = licenseKeyId;
            license["signature"]!["value"] = EncodeBase64Url(Sign(licenseKey, payload));
        }
        using var licenseDocument = JsonDocument.Parse(license.ToJsonString());
        var signedLicense = PrivateRuntimeCanonicalJson.Canonicalize(licenseDocument.RootElement);
        var signature = license["signature"]!["value"]!.GetValue<string>();
        var digest = PrivateRuntimeCanonicalJson.Sha256(signedLicense);
        var subscriptionId = license["payload"]!["subscriptionId"]!.GetValue<string>();
        var authority = new RuntimeBridgeLicenseAuthority("Ed25519", licenseKeyId, licensePem,
            PrivateRuntimeCanonicalJson.Sha256(Encoding.UTF8.GetBytes(licensePem)), "json-c14n-v1", digest, digest,
            "json-c14n-v1:license-payload", signature, subscriptionId);
        var trust = new PackageTrustOptions
        {
            TrustedPublicKeysById = new Dictionary<string, string>(StringComparer.Ordinal) { [packageKeyId] = packagePem }
        };
        return new ReissuedPackageAndLicense(canonicalPackage, trust, authority, signedLicense);
    }

    private static Ed25519PrivateKeyParameters DeterministicPrivateKey(string label) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes($"PM365-INST003-R5::{label}::test-only")), 0);

    private static string PublicKeyPem(Ed25519PublicKeyParameters publicKey)
    {
        var base64 = Convert.ToBase64String(SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(publicKey).GetDerEncoded());
        var lines = Enumerable.Range(0, (base64.Length + 63) / 64)
            .Select(index => base64.Substring(index * 64, Math.Min(64, base64.Length - index * 64)));
        return "-----BEGIN PUBLIC KEY-----\n" + string.Join("\n", lines) + "\n-----END PUBLIC KEY-----\n";
    }

    private static byte[] Sign(Ed25519PrivateKeyParameters privateKey, byte[] payload)
    {
        var signer = new Ed25519Signer();
        signer.Init(true, privateKey);
        signer.BlockUpdate(payload, 0, payload.Length);
        return signer.GenerateSignature();
    }

    private static string EncodeBase64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed record ReissuedPackageAndLicense(string PackageJson, PackageTrustOptions Trust,
        RuntimeBridgeLicenseAuthority LicenseAuthority, byte[] SignedLicenseUtf8);

    internal sealed class TestArtifactTransport(
        RuntimeBridgeSyntheticTestCapability capability,
        string fixture,
        string workspaceRoot,
        List<string> trace,
        RuntimeBridgeTestFailure failure,
        RuntimeBridgeHttpMutation? httpMutation,
        RuntimeBridgeReceiptNestedFault receiptNestedFault,
        string? declaredMutation) : IRuntimeBridgeArtifactTransport
    {
        private bool httpMutationApplied;
        private int artifactOpenCount;
        private int receiptMutationCount;
        private int acquireCount;
        private int receiptCount;
        private readonly AsyncLocal<bool> competingCall = new();
        private readonly DurableArtifactRaceStore artifactRaceStore = new();
        private readonly DurableReceiptRaceStore receiptRaceStore = new();
        private readonly object competitorGate = new();
        private readonly Dictionary<string, int> rangeIndexes = new(StringComparer.Ordinal)
        {
            ["api"] = 0,
            ["portal"] = 0
        };

        public RuntimeBridgeSyntheticTestCapability Capability { get; } = capability;
        internal int SessionCount { get; private set; }
        internal int AcquireCount => Volatile.Read(ref acquireCount);
        internal int ReceiptCount => Volatile.Read(ref receiptCount);
        internal int SessionMutationCount { get; private set; }
        internal int SessionReplayProbeCount { get; private set; }
        internal int SessionReplayStatusCode { get; private set; }
        internal int ReceiptReplayProbeCount { get; private set; }
        internal int ReceiptReplayStatusCode { get; private set; }
        internal RuntimeBridgeArtifactSession? LastSession { get; private set; }
        internal RuntimeBridgeArtifactReceipt? LastReceipt { get; private set; }
        internal List<RuntimeBridgeArtifactRequest> ArtifactRequests { get; } = [];
        internal int ArtifactOpenCount => Volatile.Read(ref artifactOpenCount);
        internal int ReceiptMutationCount => Volatile.Read(ref receiptMutationCount);
        internal RuntimeBridgeTwoCallRaceObservation? ConcurrentDownloadRace { get; private set; }
        internal RuntimeBridgeTwoCallRaceObservation? ConcurrentReceiptRace { get; private set; }
        internal string? ConcurrentDownloadWinner => artifactRaceStore.WinnerIdentity;
        internal string ArtifactDurableState => artifactRaceStore.State;
        internal string? ConcurrentReceiptWinner => receiptRaceStore.WinnerIdentity;
        internal string ReceiptDurableState => receiptRaceStore.State;
        internal Task<RuntimeBridgeArtifactResponse>? CompetingDownloadTask { get; private set; }
        internal Task<RuntimeBridgeArtifactReceipt>? CompetingReceiptTask { get; private set; }
        internal int SessionResponseBodyBytes { get; private set; }
        internal int ArtifactResponseBodyBytes { get; private set; }
        internal int ReceiptResponseBodyBytes { get; private set; }
        internal int LastServerStatus { get; private set; } = 200;
        internal string? LastServerErrorCode { get; private set; }
        internal int LastResponseBodyBytes { get; private set; }
        public RuntimeBridgeArtifactSession CreateSession(PrivateRuntimeDeliveryPackageV07 package, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); SessionCount++; trace.Add("session");
            EvaluateSessionRequest();
            rangeIndexes["api"] = 0;
            rangeIndexes["portal"] = 0;
            var session = new RuntimeBridgeArtifactSession("rds_SYNTHETIC_W09_REHEARSAL_0001", DateTimeOffset.Parse("2099-08-30T12:00:00.000Z"))
            {
                Method = "POST",
                Path = "/api/onboarding/installer/runtime-delivery-sessions",
                Query = "",
                Fragment = "",
                RequestHeaders =
                [
                    new("Authorization", "ephemeral:authorization"),
                    new("X-PM365-Onboarding-Session", "ephemeral:onboarding-session"),
                    new("X-PM365-Onboarding-Code", "ephemeral:onboarding-code")
                ],
                RequestContentType = "application/json",
                RequestBodyUtf8 = Encoding.UTF8.GetBytes("{\"packageFile\":\"customer-install-0.7.json\"}"),
                StatusCode = 201,
                ResponseHeaders = JsonResponseHeaders("Authorization, X-PM365-Onboarding-Session"),
                ResponseContentType = "application/json",
                Location = null,
                ResponseBodyUtf8 = Encoding.UTF8.GetBytes("{\"ok\":true,\"created\":true,\"deliverySession\":{\"contractVersion\":\"pagemaker365.runtime-delivery-session.v1\",\"deliverySessionId\":\"rds_SYNTHETIC_W09_REHEARSAL_0001\",\"expiresAt\":\"2099-08-30T12:00:00.000Z\",\"artifactKinds\":[\"api\",\"portal\"],\"status\":\"active\"}}")
            };
            SessionMutationCount = 1;
            SessionReplayProbeCount = 1;
            SessionReplayStatusCode = 200;
            LastSession = ShouldMutate(RuntimeBridgeHttpOperation.Session) ? MutateSession(session, httpMutation!.Fault) : session;
            SessionResponseBodyBytes = LastSession.ResponseBodyUtf8.Length;
            return LastSession;
        }
        public RuntimeBridgeArtifactResponse Acquire(
            PrivateRuntimeDeliveryPackageV07 package,
            RuntimeBridgeArtifactSession session,
            RuntimeBridgeArtifactRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref acquireCount);
            lock (ArtifactRequests) ArtifactRequests.Add(request);
            EvaluateArtifactRequest(request);
            var range = request.RangeOffset is not null;
            trace.Add($"artifact-{request.ArtifactKind}-{(range ? "range" : "full")}");
            var expectedReference = request.ArtifactKind == "api" ? package.ApiDeliveryReference : package.PortalDeliveryReference;
            var pinnedOffset = request.ArtifactKind == "api" ? 17L : 29L;
            var pinnedLength = request.ArtifactKind == "api" ? 97 : 131;
            var bytes = File.ReadAllBytes(Path.Combine(fixture, "artifacts", request.ArtifactKind + ".zip"));
            if (!range && declaredMutation is "artifact-short" or "artifact-long" or "artifact-hash-mismatch" or "aborted")
                Interlocked.Exchange(ref artifactOpenCount, 1);
            var expectedEtag = $"\"sha256:{Sha256(bytes)}\"";
            var expectedRanges = GapFreeRanges(bytes.LongLength, pinnedOffset, pinnedLength);
            var rangeIndex = range ? rangeIndexes[request.ArtifactKind] : -1;
            var expectedRange = range && rangeIndex < expectedRanges.Count ? expectedRanges[rangeIndex] : default;
            var expectedVectorId = !range
                ? $"{request.ArtifactKind}-full"
                : expectedRange.Offset == pinnedOffset && expectedRange.Length == pinnedLength
                    ? $"{request.ArtifactKind}-range"
                    : $"{request.ArtifactKind}-range-derived-{rangeIndex:D2}";
            var expectedRequestHeaders = ArtifactRequestHeaders(
                session.SessionId,
                expectedReference,
                expectedEtag,
                range ? $"bytes={expectedRange.Offset}-{expectedRange.Offset + expectedRange.Length - 1}" : null);
            var operation = range ? RuntimeBridgeHttpOperation.ArtifactRange : RuntimeBridgeHttpOperation.ArtifactFull;
            var applyHttpMutation = ShouldMutate(operation);
            var observedRequest = applyHttpMutation && IsRequestFault(httpMutation!.Fault)
                ? MutateArtifactRequest(request, httpMutation.Fault)
                : request;
            if (observedRequest.VectorId != expectedVectorId ||
                observedRequest.ArtifactReference != expectedReference || observedRequest.PackageHash != package.PackageHash ||
                observedRequest.SessionId != session.SessionId || observedRequest.IfMatch != expectedEtag ||
                (!range && (observedRequest.RangeOffset is not null || observedRequest.RangeLength is not null)) ||
                (range && (rangeIndex >= expectedRanges.Count || observedRequest.RangeOffset != expectedRange.Offset || observedRequest.RangeLength != expectedRange.Length)) ||
                observedRequest.Method != "GET" || observedRequest.Path != $"/api/onboarding/installer/runtime-artifacts/{request.ArtifactKind}" ||
                observedRequest.Query.Length != 0 || observedRequest.Fragment.Length != 0 || observedRequest.ContentType is not null || observedRequest.BodyUtf8.Length != 0 ||
                !HeadersEqual(observedRequest.OrderedHeaders, expectedRequestHeaders))
                throw new InvalidDataException("synthetic_artifact_request_invalid");
            if (range) rangeIndexes[request.ArtifactKind] = rangeIndex + 1;

            var responseOffset = range ? expectedRange.Offset : 0;
            var responseLength = range ? expectedRange.Length : bytes.Length;

            var contentRange = range ? $"bytes {responseOffset}-{responseOffset + responseLength - 1}/{bytes.LongLength}" : null;
            var response = new RuntimeBridgeArtifactResponse(
                request.ArtifactKind, request.VectorId, expectedReference, package.PackageHash, session.SessionId,
                range ? 206 : 200, range, responseOffset, bytes.LongLength, Sha256(bytes), expectedEtag,
                "bytes", contentRange,
                responseLength, $"artifacts/{request.ArtifactKind}.zip", "private, no-store", "no-cache", "nosniff", true,
                range ? bytes.AsSpan((int)responseOffset, responseLength).ToArray() : bytes)
            {
                OrderedHeaders = ArtifactResponseHeaders(expectedEtag, responseLength, contentRange),
                ContentType = "application/zip",
                Location = null
            };

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
                    ContentRange = failure.HasFlag(RuntimeBridgeTestFailure.ArtifactRangeContentRange) ? $"bytes {responseOffset + 1}-{responseOffset + responseLength}/{bytes.Length}" : response.ContentRange,
                    ContentLength = failure.HasFlag(RuntimeBridgeTestFailure.ArtifactRangeContentLength) ? responseLength - 1 : response.ContentLength,
                    ArtifactKind = failure.HasFlag(RuntimeBridgeTestFailure.ArtifactRangeShape) ? "wrong" : response.ArtifactKind,
                    TotalLength = failure.HasFlag(RuntimeBridgeTestFailure.ArtifactRangeShape) ? bytes.Length - 1 : response.TotalLength,
                    Offset = failure.HasFlag(RuntimeBridgeTestFailure.ArtifactRangeShape) ? responseOffset + 1 : response.Offset,
                    Sha256 = failure.HasFlag(RuntimeBridgeTestFailure.ArtifactRangeShape) ? new string('7', 64) : response.Sha256,
                    BodyFile = failure.HasFlag(RuntimeBridgeTestFailure.ArtifactRangeBodyFile) ? "artifacts/wrong.zip" : response.BodyFile,
                    Pragma = failure.HasFlag(RuntimeBridgeTestFailure.ArtifactRangeHeaders) ? "cache" : response.Pragma,
                    NoRedirect = !failure.HasFlag(RuntimeBridgeTestFailure.ArtifactRangeRedirect) && response.NoRedirect,
                    Body = failure.HasFlag(RuntimeBridgeTestFailure.ArtifactRangeBody) ? Mutate(response.Body) : response.Body
                };
            }
            if (applyHttpMutation && !IsRequestFault(httpMutation!.Fault))
                response = MutateArtifactResponse(response, httpMutation.Fault);
            if (!range && declaredMutation is "artifact-short" or "artifact-long" or "artifact-hash-mismatch" or "aborted")
            {
                if (declaredMutation == "artifact-short") response = response with { Body = response.Body[..^1], ContentLength = response.ContentLength - 1 };
                else if (declaredMutation == "artifact-long") response = response with { Body = response.Body.Concat(new byte[] { 0 }).ToArray(), ContentLength = response.ContentLength + 1 };
                else if (declaredMutation == "artifact-hash-mismatch") response = response with { Body = Mutate(response.Body) };
                else response = response with { Body = [] };
                LastServerStatus = 200;
                LastServerErrorCode = null;
                LastResponseBodyBytes = declaredMutation is "artifact-long" or "aborted" ? 0 : response.Body.Length;
            }
            else if (!range && declaredMutation == "concurrent-downloads")
            {
                if (!competingCall.Value)
                {
                    lock (competitorGate)
                    {
                        CompetingDownloadTask ??= Task.Run(() =>
                        {
                            competingCall.Value = true;
                            return Acquire(package, session, request, CancellationToken.None);
                        });
                    }
                }
                var outcome = artifactRaceStore.Open(competingCall.Value ? "competitor" : "bridge", response.Body.Length);
                ConcurrentDownloadRace = artifactRaceStore.Observation;
                Interlocked.Exchange(ref artifactOpenCount, artifactRaceStore.OpenCount);
                if (!competingCall.Value)
                {
                    LastServerStatus = 200;
                    LastServerErrorCode = null;
                    LastResponseBodyBytes = artifactRaceStore.BodyBytes;
                }
                if (!outcome.Accepted)
                    throw new RuntimeBridgeSyntheticTransportException(200, null, LastResponseBodyBytes);
            }
            ArtifactResponseBodyBytes = checked(ArtifactResponseBodyBytes + response.Body.Length);
            return response;
        }

        private static IReadOnlyList<(long Offset, int Length)> GapFreeRanges(long total, long pinnedOffset, int pinnedLength)
        {
            var ranges = new List<(long, int)>();
            if (pinnedOffset > 0) ranges.Add((0, checked((int)pinnedOffset)));
            ranges.Add((pinnedOffset, pinnedLength));
            var cursor = pinnedOffset + pinnedLength;
            while (cursor < total)
            {
                var length = checked((int)Math.Min(pinnedLength, total - cursor));
                ranges.Add((cursor, length));
                cursor += length;
            }
            return ranges;
        }

        private static byte[] Mutate(byte[] original)
        {
            var mutated = original.ToArray();
            mutated[0] ^= 0x5A;
            return mutated;
        }
        public RuntimeBridgeArtifactReceipt SubmitReceipt(PrivateRuntimeDeliveryPackageV07 package, RuntimeBridgeArtifactSession session, IReadOnlyList<RuntimeBridgeVerifiedArtifact> artifacts, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); Interlocked.Increment(ref receiptCount); trace.Add("artifact-receipt");
            EvaluateReceiptRequest();
            if (artifacts.Count != 2) throw new InvalidDataException("synthetic_artifact_receipt");
            using var vector = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(fixture, "runtime-delivery-http-vectors.json")));
            var requestNode = JsonNode.Parse(vector.RootElement.GetProperty("receipt").GetProperty("request").GetRawText())!.AsObject();
            requestNode["packageHash"] = package.PackageHash;
            var eventId = competingCall.Value && declaredMutation == "receipt-event-mismatch"
                ? "synthetic-w09-conflicting" : "synthetic-w09-verified";
            requestNode["eventId"] = eventId;
            using var requestDocument = JsonDocument.Parse(requestNode.ToJsonString());
            var request = requestDocument.RootElement;
            var requestBody = PrivateRuntimeCanonicalJson.Canonicalize(request);
            var acceptedJson = JsonSerializer.SerializeToUtf8Bytes(new
            {
                ok = true,
                created = true,
                receipt = new
                {
                    deliverySessionId = session.SessionId,
                    packageHash = package.PackageHash,
                    releaseId = package.ReleaseId,
                    eventId,
                    occurredAt = "2026-08-30T12:00:00.000Z",
                    installerVersion = "0.0.0-synthetic",
                    outcome = "completed",
                    artifacts = request.GetProperty("artifacts"),
                    safeResult = request.GetProperty("safeResult"),
                    createdAt = "2026-08-30T12:00:00.000+00:00"
                }
            });
            using var acceptedDocument = JsonDocument.Parse(acceptedJson);
            var accepted = PrivateRuntimeCanonicalJson.Canonicalize(acceptedDocument.RootElement);
            var receipt = new RuntimeBridgeArtifactReceipt(session.SessionId, package.PackageHash, "completed", 1)
            {
                Method = "POST",
                Path = "/api/onboarding/installer/runtime-delivery-receipts",
                Query = "",
                Fragment = "",
                RequestHeaders =
                [
                    new("Authorization", "ephemeral:authorization"),
                    new("X-PM365-Onboarding-Session", "ephemeral:onboarding-session"),
                    new("X-PM365-Onboarding-Code", "ephemeral:onboarding-code"),
                    new("X-PM365-Runtime-Delivery-Session", session.SessionId),
                    new("Idempotency-Key", "synthetic-w09-receipt")
                ],
                RequestContentType = "application/json",
                RequestBodyUtf8 = requestBody,
                StatusCode = 201,
                ResponseHeaders = JsonResponseHeaders("Authorization, X-PM365-Onboarding-Session, X-PM365-Runtime-Delivery-Session"),
                ResponseContentType = "application/json",
                Location = null,
                ResponseBodyUtf8 = accepted
            };
            ReceiptReplayProbeCount = 1;
            ReceiptReplayStatusCode = 200;
            if (declaredMutation is "receipt-event-mismatch" or "receipt-replay")
            {
                if (!competingCall.Value)
                {
                    lock (competitorGate)
                    {
                        CompetingReceiptTask ??= Task.Run(() =>
                        {
                            competingCall.Value = true;
                            return SubmitReceipt(package, session, artifacts, CancellationToken.None);
                        });
                    }
                }
                var durable = receiptRaceStore.Submit(competingCall.Value ? "competitor" : "bridge",
                    "synthetic-w09-receipt", eventId, requestBody);
                ConcurrentReceiptRace = receiptRaceStore.Observation;
                Interlocked.Exchange(ref receiptMutationCount, receiptRaceStore.MutationCount);
                if (durable.StatusCode == 409)
                {
                    if (!competingCall.Value) Deny(409, "runtime_delivery_receipt_conflict", []);
                    throw new RuntimeBridgeSyntheticTransportException(409, "runtime_delivery_receipt_conflict", 0);
                }
                receipt = receipt with { StatusCode = durable.StatusCode };
                if (!competingCall.Value)
                {
                    LastServerStatus = durable.StatusCode;
                    LastServerErrorCode = null;
                    LastResponseBodyBytes = receipt.ResponseBodyUtf8.Length;
                }
            }
            else Interlocked.Exchange(ref receiptMutationCount, 1);
            if (receiptNestedFault != RuntimeBridgeReceiptNestedFault.None)
                receipt = MutateReceiptNested(receipt, receiptNestedFault);
            LastReceipt = ShouldMutate(RuntimeBridgeHttpOperation.Receipt) ? MutateReceipt(receipt, httpMutation!.Fault) : receipt;
            ReceiptResponseBodyBytes = LastReceipt.ResponseBodyUtf8.Length;
            return LastReceipt;
        }

        private void EvaluateSessionRequest()
        {
            if (declaredMutation is null) return;
            var state = new DeliveryServerState();
            state.Apply(declaredMutation);
            if (!state.FeaturePresent || !state.FeatureEnabled || !state.PackageCanonical || !state.PackageDurable || !state.SessionBinding)
                Deny(404, "runtime_delivery_unavailable", []);
            if (state.PackageExpired)
                Deny(410, "runtime_delivery_session_terminal", []);
            if (state.PackageRevoked)
                Deny(404, "runtime_delivery_unavailable", []);
        }

        private void EvaluateArtifactRequest(RuntimeBridgeArtifactRequest request)
        {
            if (declaredMutation is null) return;
            var state = new DeliveryServerState();
            state.Apply(declaredMutation);
            if (!state.AuthenticationPresent) Deny(401, "runtime_delivery_auth_required", []);
            if (!state.AuthenticationValid || !state.OnboardingSession || !state.OnboardingCode)
                Deny(404, "runtime_delivery_unavailable", []);
            if (!state.DeliverySessionPresent) Deny(401, "runtime_delivery_session_required", []);
            if (!state.DeliverySessionActive) Deny(410, "runtime_delivery_session_terminal", []);
            if (!state.ReferencePresent) Deny(401, "runtime_delivery_ref_required", []);
            if (!state.ReferenceCorrect || !state.ReferenceKind) Deny(404, "runtime_delivery_unavailable", []);
            if (!state.IfMatchValid) Deny(412, "runtime_delivery_etag_mismatch", []);
            if (!state.RangeValid) Deny(416, "runtime_delivery_range_invalid", []);
            if (!state.PackageCurrent) Deny(404, "runtime_delivery_unavailable", []);
            if (!state.SessionCurrent) Deny(410, "runtime_delivery_session_terminal", []);
            if (state.Redirect) Deny(503, "runtime_delivery_source_unavailable", []);
            if (state.RateLimited) Deny(429, "rate_limited", Encoding.UTF8.GetBytes("{\"error\":{\"code\":\"rate_limited\"}}"));
            _ = request;
        }

        private void EvaluateReceiptRequest()
        {
            if (declaredMutation == "receipt-binding-mismatch") Deny(400, "runtime_delivery_receipt_binding_invalid", []);
        }

        private void Deny(int status, string code, byte[] body)
        {
            LastServerStatus = status;
            LastServerErrorCode = code;
            LastResponseBodyBytes = body.Length;
            throw new RuntimeBridgeSyntheticTransportException(status, code, body.Length);
        }

        private sealed class DurableArtifactRaceStore
        {
            private readonly object sync = new();
            private readonly Barrier contention = new(2);
            private readonly List<string> callers = [];
            private string? winnerIdentity;
            private int bodyBytes;

            internal string? WinnerIdentity { get { lock (sync) return winnerIdentity; } }
            internal string State { get { lock (sync) return winnerIdentity is null ? "available" : "opened:" + winnerIdentity; } }
            internal int OpenCount { get { lock (sync) return callers.Count; } }
            internal int BodyBytes { get { lock (sync) return bodyBytes; } }
            internal RuntimeBridgeTwoCallRaceObservation Observation
            {
                get
                {
                    lock (sync)
                    {
                        var winners = winnerIdentity is null ? 0 : 1;
                        return new(callers.Count, winners, 0, callers.Count - winners, callers.Count - winners);
                    }
                }
            }

            internal DurableArtifactOpen Open(string callerIdentity, int returnedBodyBytes)
            {
                lock (sync)
                {
                    if (callers.Contains(callerIdentity, StringComparer.Ordinal))
                        throw new InvalidDataException("synthetic_artifact_race_duplicate_caller");
                    callers.Add(callerIdentity);
                    bodyBytes = checked(bodyBytes + returnedBodyBytes);
                }
                if (!contention.SignalAndWait(TimeSpan.FromSeconds(5)))
                    throw new TimeoutException("synthetic_artifact_store_barrier_timeout");
                lock (sync)
                {
                    if (callerIdentity == "competitor")
                    {
                        winnerIdentity = callerIdentity;
                        Monitor.PulseAll(sync);
                    }
                    else
                    {
                        var until = DateTime.UtcNow.AddSeconds(5);
                        while (winnerIdentity is null)
                        {
                            var remaining = until - DateTime.UtcNow;
                            if (remaining <= TimeSpan.Zero || !Monitor.Wait(sync, remaining))
                                throw new TimeoutException("synthetic_artifact_store_winner_timeout");
                        }
                    }
                    return new(callerIdentity == winnerIdentity);
                }
            }
        }

        private sealed record DurableArtifactOpen(bool Accepted);

        private sealed class DurableReceiptRaceStore
        {
            private readonly object sync = new();
            private readonly Barrier contention = new(2);
            private readonly List<string> callers = [];
            private readonly Dictionary<string, DurableReceiptRow> rows = new(StringComparer.Ordinal);
            private string? winnerIdentity;
            private int replayCount;
            private int conflictCount;

            internal string? WinnerIdentity { get { lock (sync) return winnerIdentity; } }
            internal string State
            {
                get
                {
                    lock (sync)
                        return rows.Count == 0 ? "empty" : $"persisted:{rows.Single().Key}:{rows.Single().Value.EventId}";
                }
            }
            internal int MutationCount { get { lock (sync) return rows.Count; } }
            internal RuntimeBridgeTwoCallRaceObservation Observation
            {
                get
                {
                    lock (sync)
                        return new(callers.Count, rows.Count, replayCount, conflictCount, callers.Count - rows.Count);
                }
            }

            internal DurableReceiptResult Submit(string callerIdentity, string idempotencyKey, string eventId, byte[] canonicalBody)
            {
                var digest = Sha256(canonicalBody);
                lock (sync)
                {
                    if (callers.Contains(callerIdentity, StringComparer.Ordinal))
                        throw new InvalidDataException("synthetic_receipt_race_duplicate_caller");
                    callers.Add(callerIdentity);
                }
                if (!contention.SignalAndWait(TimeSpan.FromSeconds(5)))
                    throw new TimeoutException("synthetic_receipt_store_barrier_timeout");
                lock (sync)
                {
                    if (callerIdentity == "competitor")
                    {
                        rows.Add(idempotencyKey, new(eventId, digest));
                        winnerIdentity = callerIdentity;
                        Monitor.PulseAll(sync);
                        return new(201);
                    }
                    var until = DateTime.UtcNow.AddSeconds(5);
                    while (winnerIdentity is null)
                    {
                        var remaining = until - DateTime.UtcNow;
                        if (remaining <= TimeSpan.Zero || !Monitor.Wait(sync, remaining))
                            throw new TimeoutException("synthetic_receipt_store_winner_timeout");
                    }
                    var current = rows[idempotencyKey];
                    if (current.EventId == eventId && current.BodySha256 == digest)
                    {
                        replayCount++;
                        return new(200);
                    }
                    conflictCount++;
                    return new(409);
                }
            }
        }

        private sealed record DurableReceiptRow(string EventId, string BodySha256);
        private sealed record DurableReceiptResult(int StatusCode);

        private sealed class DeliveryServerState
        {
            internal bool FeaturePresent = true, FeatureEnabled = true, PackageCanonical = true, PackageDurable = true,
                SessionBinding = true, AuthenticationPresent = true, AuthenticationValid = true, OnboardingSession = true,
                OnboardingCode = true, DeliverySessionPresent = true, DeliverySessionActive = true, ReferencePresent = true,
                ReferenceCorrect = true, ReferenceKind = true, IfMatchValid = true, RangeValid = true, PackageCurrent = true,
                SessionCurrent = true, PackageExpired, PackageRevoked, Redirect, RateLimited;

            internal void Apply(string mutation)
            {
                FeaturePresent = mutation != "feature-absent";
                FeatureEnabled = mutation != "feature-false";
                PackageCanonical = mutation != "package-noncanonical";
                PackageDurable = mutation != "package-durable-mismatch";
                SessionBinding = mutation != "session-mismatch";
                AuthenticationPresent = mutation != "authentication-missing";
                AuthenticationValid = mutation != "authentication-invalid";
                OnboardingSession = mutation != "onboarding-session-mismatch";
                OnboardingCode = mutation != "onboarding-code-mismatch";
                DeliverySessionPresent = mutation != "delivery-session-missing";
                DeliverySessionActive = mutation != "delivery-session-expired";
                ReferencePresent = mutation != "reference-missing";
                ReferenceCorrect = mutation != "reference-wrong";
                ReferenceKind = mutation != "reference-cross-kind";
                IfMatchValid = mutation != "if-match-invalid";
                RangeValid = mutation is not ("range-malformed" or "range-multiple" or "range-out-of-bounds");
                PackageExpired = mutation == "package-expired";
                PackageRevoked = mutation == "package-revoked";
                PackageCurrent = mutation != "package-race";
                SessionCurrent = mutation != "session-race";
                Redirect = mutation == "artifact-redirect";
                RateLimited = mutation == "rate-limited";
            }
        }

        private static RuntimeBridgeArtifactReceipt MutateReceiptNested(RuntimeBridgeArtifactReceipt receipt, RuntimeBridgeReceiptNestedFault fault)
        {
            var request = JsonNode.Parse(receipt.RequestBodyUtf8)!.AsObject();
            var response = JsonNode.Parse(receipt.ResponseBodyUtf8)!.AsObject();
            switch (fault)
            {
                case RuntimeBridgeReceiptNestedFault.RequestArtifactsExtra: request["artifacts"]!["extra"] = JsonValue.Create("forbidden"); break;
                case RuntimeBridgeReceiptNestedFault.RequestArtifactExtra: request["artifacts"]!["api"]!["extra"] = JsonValue.Create("forbidden"); break;
                case RuntimeBridgeReceiptNestedFault.RequestArtifactItemWrong: request["artifacts"]!["api"]!["bytesReceived"] = 1014; break;
                case RuntimeBridgeReceiptNestedFault.RequestSafeResultExtra: request["safeResult"]!["extra"] = JsonValue.Create("forbidden"); break;
                case RuntimeBridgeReceiptNestedFault.RequestSafeResultWrong: request["safeResult"]!["state"] = "failed"; break;
                case RuntimeBridgeReceiptNestedFault.ResponseReceiptExtra: response["receipt"]!["extra"] = JsonValue.Create("forbidden"); break;
                case RuntimeBridgeReceiptNestedFault.ResponseArtifactsExtra: response["receipt"]!["artifacts"]!["extra"] = JsonValue.Create("forbidden"); break;
                case RuntimeBridgeReceiptNestedFault.ResponseArtifactExtra: response["receipt"]!["artifacts"]!["portal"]!["extra"] = JsonValue.Create("forbidden"); break;
                case RuntimeBridgeReceiptNestedFault.ResponseSafeResultExtra: response["receipt"]!["safeResult"]!["extra"] = JsonValue.Create("forbidden"); break;
                case RuntimeBridgeReceiptNestedFault.ResponseSafeResultWrong: response["receipt"]!["safeResult"]!["code"] = "wrong"; break;
            }
            using var requestDocument = JsonDocument.Parse(request.ToJsonString());
            using var responseDocument = JsonDocument.Parse(response.ToJsonString());
            return receipt with
            {
                RequestBodyUtf8 = PrivateRuntimeCanonicalJson.Canonicalize(requestDocument.RootElement),
                ResponseBodyUtf8 = PrivateRuntimeCanonicalJson.Canonicalize(responseDocument.RootElement)
            };
        }

        private bool ShouldMutate(RuntimeBridgeHttpOperation operation)
        {
            if (httpMutationApplied || httpMutation?.Operation != operation) return false;
            httpMutationApplied = true;
            return true;
        }

        private static IReadOnlyList<RuntimeBridgeHttpHeader> ArtifactRequestHeaders(
            string sessionId,
            string reference,
            string etag,
            string? range) =>
            new[]
            {
                new RuntimeBridgeHttpHeader("Authorization", "ephemeral:authorization"),
                new RuntimeBridgeHttpHeader("X-PM365-Onboarding-Session", "ephemeral:onboarding-session"),
                new RuntimeBridgeHttpHeader("X-PM365-Onboarding-Code", "ephemeral:onboarding-code"),
                new RuntimeBridgeHttpHeader("X-PM365-Runtime-Delivery-Session", sessionId),
                new RuntimeBridgeHttpHeader("X-PM365-Runtime-Delivery-Ref", reference),
                new RuntimeBridgeHttpHeader("If-Match", etag)
            }.Concat(range is null ? [] : new[] { new RuntimeBridgeHttpHeader("Range", range) }).ToArray();

        private static IReadOnlyList<RuntimeBridgeHttpHeader> ArtifactResponseHeaders(string etag, long length, string? contentRange) =>
            new[]
            {
                new RuntimeBridgeHttpHeader("Cache-Control", "private, no-store"),
                new RuntimeBridgeHttpHeader("Pragma", "no-cache"),
                new RuntimeBridgeHttpHeader("X-Content-Type-Options", "nosniff"),
                new RuntimeBridgeHttpHeader("ETag", etag),
                new RuntimeBridgeHttpHeader("Accept-Ranges", "bytes"),
                new RuntimeBridgeHttpHeader("Content-Length", length.ToString(System.Globalization.CultureInfo.InvariantCulture))
            }.Concat(contentRange is null ? [] : new[] { new RuntimeBridgeHttpHeader("Content-Range", contentRange) }).ToArray();

        private static IReadOnlyList<RuntimeBridgeHttpHeader> JsonResponseHeaders(string vary) =>
        [
            new("Cache-Control", "private, no-store"),
            new("Pragma", "no-cache"),
            new("X-Content-Type-Options", "nosniff"),
            new("Vary", vary)
        ];

        private static bool HeadersEqual(IReadOnlyList<RuntimeBridgeHttpHeader> left, IReadOnlyList<RuntimeBridgeHttpHeader> right) =>
            left.Count == right.Count && left.Zip(right).All(pair => pair.First == pair.Second);
    }

    internal sealed class TestLicenseTransport : IRuntimeBridgeProtectedLicenseTransport
    {
        private readonly JsonElement positive;
        private readonly List<string> trace;
        private readonly RuntimeBridgeHttpMutation? httpMutation;
        private readonly byte[]? signedLicenseOverride;
        private readonly string? declaredMutation;
        private readonly AsyncLocal<bool> competingCall = new();
        private readonly DurableProtectedReferenceStore protectedStore = new();
        private readonly object competitorGate = new();
        private int callCount;
        public RuntimeBridgeSyntheticTestCapability Capability { get; }
        internal int CallCount => Volatile.Read(ref callCount);
        internal byte[]? ReturnedBuffer { get; private set; }
        internal int ReplayProbeCount { get; private set; }
        internal int ReplayStatusCode { get; private set; }
        internal RuntimeBridgeProtectedLicenseResponse? LastResponse { get; private set; }
        private int protectedReadCount;
        private int redemptionCount;
        internal int ProtectedReadCount => Volatile.Read(ref protectedReadCount);
        internal int RedemptionCount => Volatile.Read(ref redemptionCount);
        internal RuntimeBridgeTwoCallRaceObservation? ConcurrentRedemptionRace { get; private set; }
        internal string? ConcurrentRedemptionWinner => protectedStore.WinnerIdentity;
        internal string ProtectedDurableState => protectedStore.State;
        internal Task<RuntimeBridgeProtectedLicenseResponse>? CompetingProtectedTask { get; private set; }
        internal int CompetingProtectedStatusCode { get; private set; }
        internal int CompetingProtectedBodyBytes { get; private set; }
        internal int ResponseBodyBytes { get; private set; }
        internal int LastServerStatus { get; private set; } = 200;
        internal string? LastServerErrorCode { get; private set; }
        internal int LastResponseBodyBytes { get; private set; }
        internal TestLicenseTransport(RuntimeBridgeSyntheticTestCapability capability, string fixture, List<string> trace,
            RuntimeBridgeHttpMutation? httpMutation = null, byte[]? signedLicenseOverride = null, string? declaredMutation = null)
        {
            Capability = capability; this.trace = trace; this.httpMutation = httpMutation; this.signedLicenseOverride = signedLicenseOverride;
            this.declaredMutation = declaredMutation;
            using var doc = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(fixture, "protected-setting-acquisition-http-vectors.json")));
            positive = doc.RootElement.GetProperty("positive").Clone();
        }
        public RuntimeBridgeProtectedLicenseResponse AcquireOnce(PrivateRuntimeDeliveryPackageV07 package, RuntimeBridgeArtifactSession session, RuntimeConfigurationProtectedSettingV2 descriptor, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); Interlocked.Increment(ref callCount); trace.Add("license-acquire");
            EvaluateProtectedRequest();
            var response = positive.GetProperty("response");
            var returnedBuffer = signedLicenseOverride?.ToArray() ?? PrivateRuntimeCanonicalJson.Canonicalize(response.GetProperty("value"));
            ApplyProtectedPayloadMutation(ref returnedBuffer);
            var result = new RuntimeBridgeProtectedLicenseResponse(response.GetProperty("contractVersion").GetString()!, package.PackageHash,
                response.GetProperty("targetApp").GetString()!, response.GetProperty("name").GetString()!,
                positive.GetProperty("request").GetProperty("reference").GetString()!, "private, no-store", "no-cache", "nosniff",
                "Authorization, X-PM365-Onboarding-Session, X-PM365-Runtime-Delivery-Session", true, returnedBuffer)
            {
                Method = "POST",
                Path = "/api/onboarding/installer/runtime-protected-settings/acquire",
                Query = "",
                Fragment = "",
                RequestHeaders =
                [
                    new("Authorization", "ephemeral:authorization"),
                    new("X-PM365-Onboarding-Session", "ephemeral:onboarding-session"),
                    new("X-PM365-Onboarding-Code", "ephemeral:onboarding-code"),
                    new("X-PM365-Runtime-Delivery-Session", session.SessionId)
                ],
                RequestContentType = "application/json",
                RequestBodyUtf8 = CanonicalProtectedRequest(package, descriptor),
                StatusCode = 200,
                ResponseHeaders =
                [
                    new("Cache-Control", "private, no-store"),
                    new("Pragma", "no-cache"),
                    new("X-Content-Type-Options", "nosniff"),
                    new("Vary", "Authorization, X-PM365-Onboarding-Session, X-PM365-Runtime-Delivery-Session")
                ],
                ResponseContentType = "application/json",
                Location = null,
                ResponseBodyUtf8 = CanonicalProtectedResponse(response, package.PackageHash, returnedBuffer),
                ProtectedReadCount = 1,
                RedemptionCount = 1
            };
            if (declaredMutation == "concurrent-redemption")
            {
                if (!competingCall.Value)
                {
                    lock (competitorGate)
                    {
                        CompetingProtectedTask ??= Task.Run(() =>
                        {
                            competingCall.Value = true;
                            var competing = AcquireOnce(package, session, descriptor, CancellationToken.None);
                            CompetingProtectedStatusCode = competing.StatusCode;
                            CompetingProtectedBodyBytes = competing.ResponseBodyUtf8.Length;
                            CryptographicOperations.ZeroMemory(competing.SignedLicenseUtf8);
                            return competing;
                        });
                    }
                }
                var durable = protectedStore.Redeem(competingCall.Value ? "competitor" : "bridge",
                    descriptor.Reference.OpaqueReference ?? throw new InvalidDataException("synthetic_protected_reference_missing"));
                ConcurrentRedemptionRace = protectedStore.Observation;
                Interlocked.Exchange(ref protectedReadCount, protectedStore.ReadCount);
                Interlocked.Exchange(ref redemptionCount, protectedStore.RedemptionCount);
                if (!durable.Accepted)
                {
                    CryptographicOperations.ZeroMemory(returnedBuffer);
                    Deny(404, "private_runtime_protected_setting_unavailable", ProtectedUnavailableBody());
                }
            }
            else
            {
                Interlocked.Exchange(ref protectedReadCount, 1);
                Interlocked.Exchange(ref redemptionCount, 1);
            }
            ReplayProbeCount = 1;
            ReplayStatusCode = 404;
            LastResponse = httpMutation?.Operation == RuntimeBridgeHttpOperation.Protected
                ? MutateProtected(result, httpMutation.Fault)
                : result;
            if (!competingCall.Value) ReturnedBuffer = returnedBuffer;
            ResponseBodyBytes = LastResponse.ResponseBodyUtf8.Length;
            return LastResponse;
        }

        private void EvaluateProtectedRequest()
        {
            if (declaredMutation is null) return;
            var state = new ProtectedServerState();
            state.Apply(declaredMutation);
            if (!state.FeaturePresent || !state.FeatureEnabled || !state.ConfigPresent || !state.ConfigEnabled ||
                !state.AuthenticationValid || !state.OnboardingSession || !state.OnboardingCode || !state.DeliverySession ||
                !state.RequestShape || !state.PackageBinding || !state.ReferencePresent || !state.ReferenceCorrect ||
                !state.TargetCorrect || !state.NameCorrect || !state.ReferenceActive || !state.PackageActive ||
                !state.SessionActive || !state.ExportCurrent || !state.LicenseStatusValid)
                Deny(state.SessionGone ? 410 : 404, "private_runtime_protected_setting_unavailable", ProtectedUnavailableBody());
            if (state.RateLimited) Deny(429, "rate_limited", Encoding.UTF8.GetBytes("{\"error\":{\"code\":\"rate_limited\"}}"));
            if (state.Aborted) Deny(499, "private_runtime_protected_setting_aborted", Encoding.UTF8.GetBytes(
                "{\"error\":{\"code\":\"private_runtime_protected_setting_aborted\",\"message\":\"Protected runtime setting acquisition was canceled.\",\"status\":499}}"));
            if (!state.ActivationCurrent || !state.PayloadValid || !state.PackageCurrent || !state.SessionCurrent ||
                !state.ReferenceCurrent || !state.LicenseSignatureValid || !state.LicenseFingerprintValid ||
                 !state.LicenseKeyValid || !state.LicenseCurrent)
            {
                Interlocked.Exchange(ref protectedReadCount, 1);
                Interlocked.Exchange(ref redemptionCount, 0);
                Deny(state.SessionCurrent ? 404 : 410, "private_runtime_protected_setting_unavailable", ProtectedUnavailableBody());
            }
        }

        private void ApplyProtectedPayloadMutation(ref byte[] returnedBuffer)
        {
            if (declaredMutation is null) return;
            if (declaredMutation == "payload-corrupt") returnedBuffer = Encoding.UTF8.GetBytes("{not-json");
            else if (declaredMutation is "license-signature-invalid" or "license-fingerprint-invalid" or "license-wrong-key" or
                "license-status-invalid" or "license-expired")
            {
                using var document = JsonDocument.Parse(returnedBuffer);
                var node = JsonNode.Parse(document.RootElement.GetRawText())!.AsObject();
                if (declaredMutation == "license-signature-invalid") node["signature"]!["signature"] = new string('A', node["signature"]!["signature"]!.GetValue<string>().Length);
                else if (declaredMutation == "license-fingerprint-invalid") node["signedPayloadSha256"] = new string('0', 64);
                else if (declaredMutation == "license-wrong-key") node["signature"]!["kid"] = "wrong-test-key-0001";
                else if (declaredMutation == "license-status-invalid") node["payload"]!["status"] = "revoked";
                else node["payload"]!["expiresAt"] = "2026-01-01T00:00:00.000Z";
                using var mutated = JsonDocument.Parse(node.ToJsonString());
                returnedBuffer = PrivateRuntimeCanonicalJson.Canonicalize(mutated.RootElement);
            }
        }

        private void Deny(int status, string code, byte[] body)
        {
            LastServerStatus = status;
            LastServerErrorCode = code;
            LastResponseBodyBytes = body.Length;
            throw new RuntimeBridgeSyntheticTransportException(status, code, body.Length);
        }

        private static byte[] ProtectedUnavailableBody() => Encoding.UTF8.GetBytes(
            "{\"error\":{\"code\":\"private_runtime_protected_setting_unavailable\",\"message\":\"Protected runtime setting is currently unavailable.\",\"status\":404}}");

        private sealed class DurableProtectedReferenceStore
        {
            private readonly object sync = new();
            private readonly Barrier contention = new(2);
            private readonly List<string> callers = [];
            private string? winnerIdentity;
            private string? redeemedReference;

            internal string? WinnerIdentity { get { lock (sync) return winnerIdentity; } }
            internal string State { get { lock (sync) return redeemedReference is null ? "active" : "redeemed:" + redeemedReference; } }
            internal int ReadCount { get { lock (sync) return callers.Count; } }
            internal int RedemptionCount { get { lock (sync) return redeemedReference is null ? 0 : 1; } }
            internal RuntimeBridgeTwoCallRaceObservation Observation
            {
                get
                {
                    lock (sync)
                    {
                        var winners = redeemedReference is null ? 0 : 1;
                        return new(callers.Count, winners, 0, callers.Count - winners, callers.Count - winners);
                    }
                }
            }

            internal DurableProtectedResult Redeem(string callerIdentity, string opaqueReference)
            {
                lock (sync)
                {
                    if (callers.Contains(callerIdentity, StringComparer.Ordinal))
                        throw new InvalidDataException("synthetic_protected_race_duplicate_caller");
                    callers.Add(callerIdentity);
                }
                if (!contention.SignalAndWait(TimeSpan.FromSeconds(5)))
                    throw new TimeoutException("synthetic_protected_store_barrier_timeout");
                lock (sync)
                {
                    if (callerIdentity == "competitor")
                    {
                        redeemedReference = opaqueReference;
                        winnerIdentity = callerIdentity;
                        Monitor.PulseAll(sync);
                    }
                    else
                    {
                        var until = DateTime.UtcNow.AddSeconds(5);
                        while (winnerIdentity is null)
                        {
                            var remaining = until - DateTime.UtcNow;
                            if (remaining <= TimeSpan.Zero || !Monitor.Wait(sync, remaining))
                                throw new TimeoutException("synthetic_protected_store_winner_timeout");
                        }
                    }
                    return new(callerIdentity == winnerIdentity);
                }
            }
        }

        private sealed record DurableProtectedResult(bool Accepted);

        private sealed class ProtectedServerState
        {
            internal bool FeaturePresent = true, FeatureEnabled = true, ConfigPresent = true, ConfigEnabled = true,
                AuthenticationValid = true, OnboardingSession = true, OnboardingCode = true, DeliverySession = true,
                RequestShape = true, PackageBinding = true, ReferencePresent = true, ReferenceCorrect = true,
                TargetCorrect = true, NameCorrect = true, ReferenceActive = true, PackageActive = true, SessionActive = true,
                ExportCurrent = true, ActivationCurrent = true, PayloadValid = true, PackageCurrent = true,
                SessionCurrent = true, ReferenceCurrent = true, SessionGone, RateLimited, Aborted, ConcurrentRedemption;
            internal bool LicenseSignatureValid = true, LicenseFingerprintValid = true, LicenseKeyValid = true,
                LicenseStatusValid = true, LicenseCurrent = true;

            internal void Apply(string mutation)
            {
                FeaturePresent = mutation != "feature-absent";
                FeatureEnabled = mutation != "feature-false";
                ConfigPresent = mutation != "configuration-absent";
                ConfigEnabled = mutation != "configuration-false";
                AuthenticationValid = mutation is not ("authentication-missing" or "authentication-invalid");
                OnboardingSession = mutation != "onboarding-session-mismatch";
                OnboardingCode = mutation != "onboarding-code-mismatch";
                DeliverySession = mutation != "delivery-session-mismatch";
                RequestShape = mutation is not ("query-forbidden" or "range-forbidden" or "idempotency-forbidden" or
                    "retry-forbidden" or "reference-header-forbidden");
                PackageBinding = mutation != "package-mismatch";
                ReferencePresent = mutation != "reference-missing";
                ReferenceCorrect = mutation != "reference-wrong";
                TargetCorrect = mutation != "target-mismatch";
                NameCorrect = mutation != "name-mismatch";
                ReferenceActive = mutation is not ("reference-inactive" or "reference-expired" or "reference-redeemed");
                PackageActive = mutation is not ("package-stale" or "package-revoked");
                SessionActive = mutation != "session-revoked";
                SessionGone = mutation == "session-revoked";
                ExportCurrent = mutation != "export-drift";
                ActivationCurrent = mutation != "activation-drift";
                PayloadValid = mutation != "payload-corrupt";
                PackageCurrent = mutation != "package-race";
                SessionCurrent = mutation != "session-race";
                if (mutation == "session-race") SessionGone = true;
                ReferenceCurrent = mutation is not ("activation-race" or "reference-race");
                LicenseSignatureValid = mutation != "license-signature-invalid";
                LicenseFingerprintValid = mutation != "license-fingerprint-invalid";
                LicenseKeyValid = mutation != "license-wrong-key";
                LicenseStatusValid = mutation != "license-status-invalid";
                LicenseCurrent = mutation != "license-expired";
                RateLimited = mutation == "rate-limited";
                Aborted = mutation == "aborted";
                ConcurrentRedemption = mutation == "concurrent-redemption";
            }
        }

        private static byte[] CanonicalProtectedRequest(PrivateRuntimeDeliveryPackageV07 package, RuntimeConfigurationProtectedSettingV2 descriptor)
        {
            using var document = JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(new
            {
                contractVersion = descriptor.Reference.ContractVersion,
                packageHash = package.PackageHash,
                targetApp = "api",
                name = descriptor.Name,
                reference = descriptor.Reference.OpaqueReference
            }));
            return PrivateRuntimeCanonicalJson.Canonicalize(document.RootElement);
        }

        private static byte[] CanonicalProtectedResponse(JsonElement fixtureResponse, string packageHash, byte[] signedLicense)
        {
            using var value = JsonDocument.Parse(signedLicense);
            using var document = JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(new
            {
                contractVersion = fixtureResponse.GetProperty("contractVersion").GetString(),
                packageHash,
                targetApp = fixtureResponse.GetProperty("targetApp").GetString(),
                name = fixtureResponse.GetProperty("name").GetString(),
                value = value.RootElement
            }));
            return PrivateRuntimeCanonicalJson.Canonicalize(document.RootElement);
        }
    }

    internal sealed class TestCursorGenerator : IRuntimeBridgeCursorGenerator
    {
        private static readonly byte[] DefaultEntropy =
            SHA256.HashData(Encoding.UTF8.GetBytes("PM365-INST-003-R5-DETERMINISTIC-CURSOR"));
        private readonly List<string> trace;
        private readonly byte[] entropy;

        internal TestCursorGenerator(
            RuntimeBridgeSyntheticTestCapability capability,
            List<string> trace,
            byte[]? entropy = null)
        {
            Capability = capability;
            this.trace = trace;
            this.entropy = (entropy ?? DefaultEntropy).ToArray();
        }

        public RuntimeBridgeSyntheticTestCapability Capability { get; }
        internal int CallCount { get; private set; }
        internal byte[]? ReturnedBuffer { get; private set; }
        internal string SourceSha256 => Sha256(entropy);
        public byte[] Generate(int entropyBytes)
        {
            if (entropyBytes != entropy.Length) throw new InvalidDataException("synthetic_cursor_entropy_length");
            CallCount++;
            trace.Add("cursor-generate");
            ReturnedBuffer = entropy.ToArray();
            return ReturnedBuffer;
        }
    }

    internal sealed class TestWriteSink(
        RuntimeBridgeSyntheticTestCapability capability,
        List<string> trace,
        RuntimeBridgeTestFailure failure,
        string volatileIdentity,
        RuntimeBridgeReceiptDigestFault receiptDigestFault) : IRuntimeBridgeProtectedWriteSink
    {
        private string? licenseDigest;
        public RuntimeBridgeSyntheticTestCapability Capability { get; } = capability;
        internal List<ReadOnlyMemory<byte>> RetainedBuffers { get; } = [];
        internal List<string> ReceiptIds { get; } = [];
        internal List<RuntimeBridgeProtectedWriteReceipt> Receipts { get; } = [];
        internal int CallCount { get; private set; }
        public RuntimeBridgeProtectedWriteReceipt Write(RuntimeBridgeProtectedWriteRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); CallCount++; trace.Add("write-" + request.Name); RetainedBuffers.Add(request.ValueUtf8);
            if (failure.HasFlag(RuntimeBridgeTestFailure.LicenseWrite) && request.Name == "API_LICENSE_SIGNED_PAYLOAD") throw new InvalidOperationException("synthetic_write_denial");
            if (failure.HasFlag(RuntimeBridgeTestFailure.CursorWrite) && request.Name == "API_IMAGE_ASSET_CURSOR_SECRET") throw new InvalidOperationException("synthetic_write_denial");
            var actualDigest = Sha256(request.ValueUtf8.Span);
            if (request.Name == "API_LICENSE_SIGNED_PAYLOAD") licenseDigest = actualDigest;
            var digest = receiptDigestFault switch
            {
                RuntimeBridgeReceiptDigestFault.Missing => "",
                RuntimeBridgeReceiptDigestFault.Uppercase => actualDigest.ToUpperInvariant(),
                RuntimeBridgeReceiptDigestFault.Stale => new string('0', 64),
                RuntimeBridgeReceiptDigestFault.CrossPair when request.Name == "API_LICENSE_SIGNED_PAYLOAD" =>
                    Sha256(Encoding.UTF8.GetBytes("cross-paired-cursor-digest")),
                RuntimeBridgeReceiptDigestFault.CrossPair => licenseDigest ?? new string('0', 64),
                _ => actualDigest
            };
            var version = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"version:{request.Name}"))).ToLowerInvariant()[..32];
            var vault = "/subscriptions/61b7c2e9-8f34-45ad-b062-3ea19d75f48c/resourceGroups/pm365-fixture/providers/Microsoft.KeyVault/vaults/pm365fixture";
            if (request.VaultResourceId != vault) throw new InvalidDataException("synthetic_vault");
            var receiptId = "rwr_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"receipt:{volatileIdentity}:{CallCount}:{request.Name}"))).ToLowerInvariant()[..24];
            ReceiptIds.Add(receiptId);
            var receipt = new RuntimeBridgeProtectedWriteReceipt(receiptId, request.Name, request.Mode, request.VaultResourceId,
                request.SecretName, version, $"@Microsoft.KeyVault(SecretUri=https://pm365fixture.vault.azure.net/secrets/{request.SecretName}/{version})",
                digest, request.PackageHash, request.ApprovalDigest, "written", 1);
            Receipts.Add(receipt);
            return receipt;
        }
    }

    internal sealed class TestWhatIf(RuntimeBridgeSyntheticTestCapability capability, List<string> trace, RuntimeBridgeTestFailure failure) : IRuntimeBridgeWhatIf
    {
        public RuntimeBridgeSyntheticTestCapability Capability { get; } = capability;
        internal int CallCount { get; private set; }
        internal List<RuntimeBridgeWhatIfRequest> Requests { get; } = [];
        internal List<RuntimeBridgeWhatIfResult> Results { get; } = [];
        public RuntimeBridgeWhatIfResult Preview(RuntimeBridgeWhatIfRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); CallCount++; Requests.Add(request); trace.Add("whatif-" + request.Phase);
            if (failure.HasFlag(RuntimeBridgeTestFailure.SecondWhatIf) && request.Phase == "final") throw new InvalidDataException("synthetic_second_whatif");
            if (failure.HasFlag(RuntimeBridgeTestFailure.SecondWhatIfCancellation) && request.Phase == "final") throw new OperationCanceledException("synthetic_cancelled");
            var json = JsonSerializer.Serialize(new { request.Phase, request.PackageHash, request.InputSha256, request.ArtifactIdentitySha256, request.PhaseOneApprovalDigest, request.ReceiptIdentitySha256s }) + "\n";
            var requestSha = Sha256(Encoding.UTF8.GetBytes($"{request.Phase}\n{request.PackageHash}\n{request.InputSha256}\n{request.ArtifactIdentitySha256}\n{request.PhaseOneApprovalDigest}\n{string.Join(',', request.ReceiptIdentitySha256s)}\n"));
            var result = new RuntimeBridgeWhatIfResult(request.Phase, "previewed", requestSha, json,
                Sha256(Encoding.UTF8.GetBytes(json)), 0, 0);
            Results.Add(result);
            return result;
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
        internal List<RuntimeBridgeApprovalChallenge> Challenges { get; } = [];
        internal List<RuntimeBridgeApprovalReceipt> Receipts { get; } = [];
        public RuntimeBridgeApprovalReceipt Approve(RuntimeBridgeApprovalChallenge challenge, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); CallCount++; trace.Add("approval-" + challenge.Phase);
            Challenges.Add(challenge);
            if (failure.HasFlag(RuntimeBridgeTestFailure.ApprovalTwo) && challenge.Phase == "final") throw new InvalidDataException("synthetic_approval_denial");
            var id = $"approval-{volatileIdentity}-{CallCount}";
            ApprovalIds.Add(id);
            var receipt = new RuntimeBridgeApprovalReceipt(id, challenge.Phase, challenge.ChallengeSha256, challenge.Nonce,
                challenge.ExpiresAt, "approved", 1, Sha256(Encoding.UTF8.GetBytes(id + "\n" + challenge.ChallengeSha256 + "\n")));
            Receipts.Add(receipt);
            return receipt;
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
        internal List<string> RecoveredNames { get; } = [];
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
            RecoveredNames.Add(receipt.Name);
            return new(receipt.ReceiptId, "recovered", 1);
        }
    }

    private static bool IsRequestFault(RuntimeBridgeHttpFault fault) => fault <= RuntimeBridgeHttpFault.RequestBody;

    private static RuntimeBridgeArtifactSession MutateSession(RuntimeBridgeArtifactSession value, RuntimeBridgeHttpFault fault) => fault switch
    {
        RuntimeBridgeHttpFault.Method => value with { Method = "PATCH" },
        RuntimeBridgeHttpFault.Path => value with { Path = "/wrong" },
        RuntimeBridgeHttpFault.Query => value with { Query = "?forbidden=1" },
        RuntimeBridgeHttpFault.Fragment => value with { Fragment = "#forbidden" },
        RuntimeBridgeHttpFault.RequestHeaderMissing or RuntimeBridgeHttpFault.RequestHeaderExtra or
            RuntimeBridgeHttpFault.RequestHeaderReordered or RuntimeBridgeHttpFault.RequestHeaderDuplicate or
            RuntimeBridgeHttpFault.RequestHeaderWrongRole => value with { RequestHeaders = MutateHeaders(value.RequestHeaders, fault) },
        RuntimeBridgeHttpFault.RequestContentType => value with { RequestContentType = "text/plain" },
        RuntimeBridgeHttpFault.RequestBody => value with { RequestBodyUtf8 = Encoding.UTF8.GetBytes("{}") },
        RuntimeBridgeHttpFault.Status => value with { StatusCode = 200 },
        RuntimeBridgeHttpFault.ResponseHeaderMissing or RuntimeBridgeHttpFault.ResponseHeaderExtra or
            RuntimeBridgeHttpFault.ResponseHeaderReordered or RuntimeBridgeHttpFault.ResponseHeaderDuplicate or
            RuntimeBridgeHttpFault.ResponseHeaderWrongValue => value with { ResponseHeaders = MutateHeaders(value.ResponseHeaders, fault) },
        RuntimeBridgeHttpFault.ResponseContentType => value with { ResponseContentType = "text/plain" },
        RuntimeBridgeHttpFault.Location => value with { Location = "/redirect" },
        RuntimeBridgeHttpFault.ResponseBody => value with { ResponseBodyUtf8 = Encoding.UTF8.GetBytes("{}") },
        _ => value
    };

    private static RuntimeBridgeArtifactRequest MutateArtifactRequest(RuntimeBridgeArtifactRequest value, RuntimeBridgeHttpFault fault) => fault switch
    {
        RuntimeBridgeHttpFault.Method => value with { Method = "POST" },
        RuntimeBridgeHttpFault.Path => value with { Path = "/wrong" },
        RuntimeBridgeHttpFault.Query => value with { Query = "?forbidden=1" },
        RuntimeBridgeHttpFault.Fragment => value with { Fragment = "#forbidden" },
        RuntimeBridgeHttpFault.RequestHeaderMissing or RuntimeBridgeHttpFault.RequestHeaderExtra or
            RuntimeBridgeHttpFault.RequestHeaderReordered or RuntimeBridgeHttpFault.RequestHeaderDuplicate or
            RuntimeBridgeHttpFault.RequestHeaderWrongRole => value with { OrderedHeaders = MutateHeaders(value.OrderedHeaders, fault) },
        RuntimeBridgeHttpFault.RequestContentType => value with { ContentType = "application/json" },
        RuntimeBridgeHttpFault.RequestBody => value with { BodyUtf8 = Encoding.UTF8.GetBytes("{}") },
        _ => value
    };

    private static RuntimeBridgeArtifactResponse MutateArtifactResponse(RuntimeBridgeArtifactResponse value, RuntimeBridgeHttpFault fault) => fault switch
    {
        RuntimeBridgeHttpFault.Status => value with { StatusCode = value.StatusCode == 200 ? 206 : 200 },
        RuntimeBridgeHttpFault.ResponseHeaderMissing or RuntimeBridgeHttpFault.ResponseHeaderExtra or
            RuntimeBridgeHttpFault.ResponseHeaderReordered or RuntimeBridgeHttpFault.ResponseHeaderDuplicate or
            RuntimeBridgeHttpFault.ResponseHeaderWrongValue => value with { OrderedHeaders = MutateHeaders(value.OrderedHeaders, fault) },
        RuntimeBridgeHttpFault.ResponseContentType => value with { ContentType = "application/json" },
        RuntimeBridgeHttpFault.Location => value with { Location = "/redirect" },
        RuntimeBridgeHttpFault.ResponseBody => value with { Body = MutateBytes(value.Body) },
        _ => value
    };

    private static RuntimeBridgeArtifactReceipt MutateReceipt(RuntimeBridgeArtifactReceipt value, RuntimeBridgeHttpFault fault) => fault switch
    {
        RuntimeBridgeHttpFault.Method => value with { Method = "PATCH" },
        RuntimeBridgeHttpFault.Path => value with { Path = "/wrong" },
        RuntimeBridgeHttpFault.Query => value with { Query = "?forbidden=1" },
        RuntimeBridgeHttpFault.Fragment => value with { Fragment = "#forbidden" },
        RuntimeBridgeHttpFault.RequestHeaderMissing or RuntimeBridgeHttpFault.RequestHeaderExtra or
            RuntimeBridgeHttpFault.RequestHeaderReordered or RuntimeBridgeHttpFault.RequestHeaderDuplicate or
            RuntimeBridgeHttpFault.RequestHeaderWrongRole => value with { RequestHeaders = MutateHeaders(value.RequestHeaders, fault) },
        RuntimeBridgeHttpFault.RequestContentType => value with { RequestContentType = "text/plain" },
        RuntimeBridgeHttpFault.RequestBody => value with { RequestBodyUtf8 = Encoding.UTF8.GetBytes("{}") },
        RuntimeBridgeHttpFault.Status => value with { StatusCode = 200 },
        RuntimeBridgeHttpFault.ResponseHeaderMissing or RuntimeBridgeHttpFault.ResponseHeaderExtra or
            RuntimeBridgeHttpFault.ResponseHeaderReordered or RuntimeBridgeHttpFault.ResponseHeaderDuplicate or
            RuntimeBridgeHttpFault.ResponseHeaderWrongValue => value with { ResponseHeaders = MutateHeaders(value.ResponseHeaders, fault) },
        RuntimeBridgeHttpFault.ResponseContentType => value with { ResponseContentType = "text/plain" },
        RuntimeBridgeHttpFault.Location => value with { Location = "/redirect" },
        RuntimeBridgeHttpFault.ResponseBody => value with { ResponseBodyUtf8 = Encoding.UTF8.GetBytes("{}") },
        _ => value
    };

    private static RuntimeBridgeProtectedLicenseResponse MutateProtected(RuntimeBridgeProtectedLicenseResponse value, RuntimeBridgeHttpFault fault) => fault switch
    {
        RuntimeBridgeHttpFault.Method => value with { Method = "PATCH" },
        RuntimeBridgeHttpFault.Path => value with { Path = "/wrong" },
        RuntimeBridgeHttpFault.Query => value with { Query = "?forbidden=1" },
        RuntimeBridgeHttpFault.Fragment => value with { Fragment = "#forbidden" },
        RuntimeBridgeHttpFault.RequestHeaderMissing or RuntimeBridgeHttpFault.RequestHeaderExtra or
            RuntimeBridgeHttpFault.RequestHeaderReordered or RuntimeBridgeHttpFault.RequestHeaderDuplicate or
            RuntimeBridgeHttpFault.RequestHeaderWrongRole => value with { RequestHeaders = MutateHeaders(value.RequestHeaders, fault) },
        RuntimeBridgeHttpFault.RequestContentType => value with { RequestContentType = "text/plain" },
        RuntimeBridgeHttpFault.RequestBody => value with { RequestBodyUtf8 = Encoding.UTF8.GetBytes("{}") },
        RuntimeBridgeHttpFault.Status => value with { StatusCode = 404 },
        RuntimeBridgeHttpFault.ResponseHeaderMissing or RuntimeBridgeHttpFault.ResponseHeaderExtra or
            RuntimeBridgeHttpFault.ResponseHeaderReordered or RuntimeBridgeHttpFault.ResponseHeaderDuplicate or
            RuntimeBridgeHttpFault.ResponseHeaderWrongValue => value with { ResponseHeaders = MutateHeaders(value.ResponseHeaders, fault) },
        RuntimeBridgeHttpFault.ResponseContentType => value with { ResponseContentType = "text/plain" },
        RuntimeBridgeHttpFault.Location => value with { Location = "/redirect" },
        RuntimeBridgeHttpFault.ResponseBody => value with { ResponseBodyUtf8 = Encoding.UTF8.GetBytes("{}") },
        _ => value
    };

    private static IReadOnlyList<RuntimeBridgeHttpHeader> MutateHeaders(
        IReadOnlyList<RuntimeBridgeHttpHeader> source,
        RuntimeBridgeHttpFault fault)
    {
        var result = source.ToList();
        switch (fault)
        {
            case RuntimeBridgeHttpFault.RequestHeaderMissing:
            case RuntimeBridgeHttpFault.ResponseHeaderMissing:
                result.RemoveAt(0); break;
            case RuntimeBridgeHttpFault.RequestHeaderExtra:
            case RuntimeBridgeHttpFault.ResponseHeaderExtra:
                result.Add(new("X-PM365-Forbidden", "forbidden")); break;
            case RuntimeBridgeHttpFault.RequestHeaderReordered:
            case RuntimeBridgeHttpFault.ResponseHeaderReordered:
                (result[0], result[1]) = (result[1], result[0]); break;
            case RuntimeBridgeHttpFault.RequestHeaderDuplicate:
            case RuntimeBridgeHttpFault.ResponseHeaderDuplicate:
                result.Insert(1, result[0]); break;
            case RuntimeBridgeHttpFault.RequestHeaderWrongRole:
            case RuntimeBridgeHttpFault.ResponseHeaderWrongValue:
                result[0] = result[0] with { Value = "wrong" }; break;
        }
        return result;
    }

    private static byte[] MutateBytes(byte[] bytes)
    {
        var result = bytes.ToArray();
        result[0] ^= 0x5A;
        return result;
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
