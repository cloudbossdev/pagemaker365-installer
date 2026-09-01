using System.Buffers.Binary;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using PageMaker365.Installer.Engine.Models;
using PageMaker365.Installer.Engine.Services;

namespace PageMaker365.Installer.Engine.Tests;

internal static class RuntimeDeploymentRecoveryBridgeTests
{
    internal static void RunAll()
    {
        RunsTheExactSyntheticSequenceAndReturnsOnlyRedactedEvidence();
        RemainsDefaultDisabledAndCapabilityClosed();
        StopsAtOwnedFailureAndCancellationBoundaries();
        RecoversInReverseOrderAndTreatsAmbiguityAsTerminal();
        SerializesConcurrentReplayWithoutRepeatingEffects();
        BindsReplayToTheExactInvocationIdentity();
        ProducesDeterministicEvidenceAcrossVolatileIdentitiesAndStages();
        ExecutesTheClosedProtectedContentEvidenceLedger();
        ExecutesTheClosedHttpProtocolMatrix();
        ExecutesAllFixtureDeclaredDeliveryAndProtectedNegativeVectors();
        RejectsExactZipLocalCentralAndEocdMutations();
        RecursivelyClosesReceiptRequestAndResponseBodies();
        PinsExplicitCancellationChecksAtEveryOwnedBoundary();
        PinsCompleteFailureRecoveryCancellationUnion();
        RejectsEveryOwnedArtifactProtocolMutationBeforeProtectedEffects();
        PinsTheCompleteLicenseAuthorityAndSignedIdentity();
        RejectsEveryHandlerResultMutationAndBindsItsDigestIntoEvidence();
        RejectsUnsafeOwnedStageMutationsWithoutDeletingForeignContent();
        NativeWindowsStageDeniesCreateRegistrationAndCleanupSubstitutionRaces();
        NativeWindowsStageExecutesTheClosedAliasAndWriterRaceMatrix();
        NativeWindowsCancellationTableCoversEveryCreateBindAndCleanupBoundary();
        PortableInjectedStageStoreProvesClosedOwnershipSemantics();
        ProvesCursorCopiesAreNotIntroducedAtTheCallbackBoundary();
        KeepsTheBoundaryInternalOfflineAndNonDeploying();
    }

    private static void NativeWindowsStageExecutesTheClosedAliasAndWriterRaceMatrix()
    {
        if (!OperatingSystem.IsWindows()) return;
        var symbolicLinkCapability = RuntimeBridgeTestHarness.TestNativeStageRaceProbe.ProbeFileSymbolicLinkCapability();
        Console.WriteLine($"NATIVE_FILE_SYMBOLIC_LINK_CAPABILITY={symbolicLinkCapability.Capability};NATIVE_ERROR={symbolicLinkCapability.NativeErrorCode}");
        var symbolicLinkOsDenied = NativeCase(RuntimeBridgeNativeStageAttack.FileSymbolicAlias, false, "simulated",
            "runtime_deployment_recovery_rehearsal_completed", true, true);
        var symbolicLinkEstablished = NativeCase(RuntimeBridgeNativeStageAttack.FileSymbolicAlias, true, "cleanup-required",
            "runtime_deployment_recovery_stage_cleanup_required", false, false);
        var symbolicLinkExpected = symbolicLinkCapability.Capability switch
        {
            RuntimeBridgeFileSymbolicLinkCapability.OsDenied => symbolicLinkOsDenied,
            RuntimeBridgeFileSymbolicLinkCapability.Established => symbolicLinkEstablished,
            _ => throw new InvalidDataException("native_file_symbolic_link_capability_invalid")
        };
        var ledger = new[]
        {
            NativeCase(RuntimeBridgeNativeStageAttack.TrustedRootBeforeBind, true, "failed", "runtime_deployment_recovery_rehearsal_failed", true, false),
            NativeCase(RuntimeBridgeNativeStageAttack.TrustedRootAfterBind, false, "simulated", "runtime_deployment_recovery_rehearsal_completed", true, true),
            NativeCase(RuntimeBridgeNativeStageAttack.ParentBeforeStageCreate, false, "simulated", "runtime_deployment_recovery_rehearsal_completed", true, true),
            NativeCase(RuntimeBridgeNativeStageAttack.ParentAfterValidationBeforeStageCreate, false, "simulated", "runtime_deployment_recovery_rehearsal_completed", true, true),
            NativeCase(RuntimeBridgeNativeStageAttack.StageBeforeBind, true, "failed", "runtime_deployment_recovery_rehearsal_failed", true, false),
            NativeCase(RuntimeBridgeNativeStageAttack.StageAfterBind, false, "simulated", "runtime_deployment_recovery_rehearsal_completed", true, true),
            NativeCase(RuntimeBridgeNativeStageAttack.MarkerBeforeBind, false, "simulated", "runtime_deployment_recovery_rehearsal_completed", true, true),
            NativeCase(RuntimeBridgeNativeStageAttack.DirectoryBeforeBind, true, "cleanup-required", "runtime_deployment_recovery_stage_cleanup_required", false, false),
            NativeCase(RuntimeBridgeNativeStageAttack.DirectoryCaseAlias, false, "simulated", "runtime_deployment_recovery_rehearsal_completed", true, true),
            NativeCase(RuntimeBridgeNativeStageAttack.FileBeforeBind, false, "simulated", "runtime_deployment_recovery_rehearsal_completed", true, true),
            NativeCase(RuntimeBridgeNativeStageAttack.FileCaseAlias, false, "simulated", "runtime_deployment_recovery_rehearsal_completed", true, true),
            NativeCase(RuntimeBridgeNativeStageAttack.FileHardlinkAlias, true, "cleanup-required", "runtime_deployment_recovery_stage_cleanup_required", false, false),
            symbolicLinkExpected,
            NativeCase(RuntimeBridgeNativeStageAttack.TrueDirectoryJunction, true, "cleanup-required", "runtime_deployment_recovery_stage_cleanup_required", false, false),
            NativeCase(RuntimeBridgeNativeStageAttack.EnumerationSecondWriter, true, "cleanup-required", "runtime_deployment_recovery_stage_cleanup_required", false, false),
            NativeCase(RuntimeBridgeNativeStageAttack.CleanupIdentitySubstitution, true, "cleanup-required",
                "runtime_deployment_recovery_stage_cleanup_required", false, true)
        };
        AssertEx.Equal(16, ledger.Length);
        AssertEx.True(Enum.GetValues<RuntimeBridgeNativeStageAttack>().SequenceEqual(ledger.Select(row => row.Attack)),
            "native16 ledger must independently enumerate every case in exact authority order");
        foreach (var expected in ledger)
        {
            var harness = new RuntimeBridgeTestHarness(nativeStageAttack: expected.Attack);
            try
            {
                var result = harness.Bridge.RunAsync(harness.Invocation()).GetAwaiter().GetResult();
                var probe = harness.NativeStageRaceProbe!;
                var id = expected.Attack.ToString();
                AssertEx.True(probe.AttackApplied, id);
                AssertEx.Equal(1, probe.ProbeCount, id);
                AssertEx.Equal(0, probe.UnavailableCount, id);
                AssertEx.Equal(expected.DeniedCount, probe.DeniedCount, id);
                AssertEx.Equal(expected.UnexpectedSuccessCount, probe.UnexpectedSuccessCount, id);
                AssertEx.Equal(expected.Status, result.Status, id);
                AssertEx.Equal(expected.SafeCode, result.SafeCode, id);
                AssertEx.Equal(expected.StageCleaned, result.StageCleaned, id);
                AssertEx.False(result.AuthorizesDeployment, id);
                AssertEx.Equal(expected.SessionCalls, harness.ArtifactTransport.SessionCount, id);
                AssertEx.Equal(expected.ArtifactCalls, harness.ArtifactTransport.AcquireCount, id);
                AssertEx.Equal(expected.ReceiptCalls, harness.ArtifactTransport.ReceiptCount, id);
                AssertEx.Equal(expected.LicenseCalls, harness.LicenseTransport.CallCount, id);
                AssertEx.Equal(expected.Writes, harness.WriteSink.CallCount, id);
                AssertEx.Equal(expected.RandomGenerations, harness.CursorGenerator.CallCount, id);
                AssertEx.Equal(expected.Previews, harness.WhatIf.CallCount, id);
                AssertEx.Equal(expected.Previews, result.WhatIfCount, id);
                AssertEx.Equal(expected.Approvals, harness.Approval.CallCount, id);
                AssertEx.Equal(expected.Approvals, result.ApprovalCount, id);
                AssertEx.Equal(expected.HandlerCalls, harness.Handler.CallCount, id);
                AssertEx.Equal(expected.Recoveries, harness.Recovery.CallCount, id);
                if (expected.Attack == RuntimeBridgeNativeStageAttack.FileSymbolicAlias && expected.AttackEstablished)
                    probe.AssertForeignSymbolicAliasSurvives();
                else if (expected.AttackEstablished)
                    AssertEx.True(probe.ForeignPath is not null && (File.Exists(probe.ForeignPath) || Directory.Exists(probe.ForeignPath)), id);
                else
                {
                    AssertEx.True(probe.ForeignPath is null, id);
                    AssertEx.True(probe.ForeignTargetPath is null, id);
                }
                if (expected.Attack == RuntimeBridgeNativeStageAttack.FileSymbolicAlias && expected.StageCleaned)
                    AssertEx.Equal(0, Directory.GetDirectories(harness.WorkspaceRoot, "pm365-synthetic-runtime-*", SearchOption.TopDirectoryOnly).Length, id);
            }
            finally
            {
                var probe = harness.NativeStageRaceProbe;
                var foreign = probe?.ForeignPath;
                if (probe?.ForeignTargetPath is not null) probe.CleanupForeignSymbolicAliasForTest();
                harness.Dispose();
                if (foreign is not null)
                {
                    try { if (Directory.Exists(foreign)) Directory.Delete(foreign, recursive: true); else if (File.Exists(foreign)) File.Delete(foreign); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
        }
    }

    private static NativeStageExpected NativeCase(RuntimeBridgeNativeStageAttack attack, bool established, string status,
        string safeCode, bool stageCleaned, bool completed) => new(attack, established, status, safeCode, stageCleaned,
            established ? 0 : 1, established ? 1 : 0, 1, 29, 1, completed ? 1 : 0, completed ? 2 : 0,
            completed ? 1 : 0, completed ? 2 : 0, completed ? 2 : 0, completed ? 1 : 0, 0);

    private sealed record NativeStageExpected(RuntimeBridgeNativeStageAttack Attack, bool AttackEstablished, string Status,
        string SafeCode, bool StageCleaned, int DeniedCount, int UnexpectedSuccessCount, int SessionCalls,
        int ArtifactCalls, int ReceiptCalls, int LicenseCalls, int Writes, int RandomGenerations, int Previews,
        int Approvals, int HandlerCalls, int Recoveries);

    private static void NativeWindowsCancellationTableCoversEveryCreateBindAndCleanupBoundary()
    {
        if (!OperatingSystem.IsWindows()) return;
        var ledger = ExpectedNativeCancellationLedger();
        AssertEx.Equal(78, ledger.Count);
        AssertEx.Equal(ledger.Count, ledger.Select(row => row.Checkpoint).Distinct(StringComparer.Ordinal).Count());
        var baseline = new RuntimeBridgeTestHarness(cancellationCheckpoint: "never");
        try
        {
            var result = baseline.Bridge.RunAsync(baseline.Invocation(), baseline.BoundaryCancellation!.Token).GetAwaiter().GetResult();
            AssertEx.Equal("simulated", result.Status);
            var observed = baseline.CancellationProbe!.Phases.Where(item => item.StartsWith("native.", StringComparison.Ordinal)).ToArray();
            var expectedLabels = ledger.Select(row => row.Checkpoint).ToArray();
            var mismatch = Enumerable.Range(0, Math.Min(expectedLabels.Length, observed.Length))
                .FirstOrDefault(index => expectedLabels[index] != observed[index], -1);
            AssertEx.True(expectedLabels.SequenceEqual(observed, StringComparer.Ordinal),
                $"Expected {expectedLabels.Length} native checkpoints; observed {observed.Length}; mismatch {mismatch}: " +
                (mismatch >= 0 ? $"{expectedLabels[mismatch]} != {observed[mismatch]}" : "length-only"));
            AssertEx.Equal(observed.Length, observed.Distinct(StringComparer.Ordinal).Count());
        }
        finally { baseline.Dispose(); baseline.BoundaryCancellation?.Dispose(); }

        foreach (var expected in ledger)
        {
            var harness = new RuntimeBridgeTestHarness(cancellationCheckpoint: expected.Checkpoint);
            try
            {
                var result = harness.Bridge.RunAsync(harness.Invocation(), harness.BoundaryCancellation!.Token).GetAwaiter().GetResult();
                var id = expected.Checkpoint;
                AssertEx.Equal(expected.Status, result.Status, id);
                AssertEx.Equal(expected.SafeCode, result.SafeCode, id);
                AssertEx.Equal(expected.StageCleaned, result.StageCleaned, id);
                AssertEx.False(result.AuthorizesDeployment, id);
                AssertEx.Equal(1, harness.ArtifactTransport.SessionCount, id);
                AssertEx.Equal(29, harness.ArtifactTransport.AcquireCount, id);
                AssertEx.Equal(1, harness.ArtifactTransport.ReceiptCount, id);
                AssertEx.Equal(expected.LicenseCalls, harness.LicenseTransport.CallCount, id);
                AssertEx.Equal(expected.LicenseCalls, harness.LicenseTransport.ProtectedReadCount, id);
                AssertEx.Equal(expected.LicenseCalls, harness.LicenseTransport.RedemptionCount, id);
                AssertEx.Equal(expected.Writes, harness.WriteSink.CallCount, id);
                AssertEx.Equal(expected.RandomGenerations, harness.CursorGenerator.CallCount, id);
                AssertEx.Equal(expected.Previews, harness.WhatIf.CallCount, id);
                AssertEx.Equal(expected.Approvals, harness.Approval.CallCount, id);
                AssertEx.Equal(expected.HandlerCalls, harness.Handler.CallCount, id);
                AssertEx.Equal(0, harness.Recovery.CallCount, id);
                AssertEx.Equal(252, harness.ArtifactTransport.SessionResponseBodyBytes, id);
                AssertEx.Equal(5648, harness.ArtifactTransport.ArtifactResponseBodyBytes, id);
                AssertEx.Equal(924, harness.ArtifactTransport.ReceiptResponseBodyBytes, id);
                AssertEx.Equal(expected.LicenseCalls == 1 ? 1179 : 0, harness.LicenseTransport.ResponseBodyBytes, id);
                AssertEx.Equal(1, harness.CancellationProbe!.Phases.Count(value => value == "native.cleanup.commit:after"), id);
                AssertEx.Equal(0, Directory.GetDirectories(harness.WorkspaceRoot).Length, id);
                AssertEx.Equal(0, result.OwnedReceipts.Count, id);
                AssertEx.True(result.FinalInput is null, id);
                AssertEx.True(result.EvidenceJson.Length > 0 && result.EvidenceSha256.Length == 64, id);
                AssertEx.True(harness.WriteSink.RetainedBuffers.All(buffer => buffer.Span.ToArray().All(value => value == 0)), id);
            }
            finally { harness.Dispose(); harness.BoundaryCancellation?.Dispose(); }
        }
    }

    private static IReadOnlyList<NativeCancellationExpected> ExpectedNativeCancellationLedger()
    {
        string[] labels =
        [
            "native.root.open", "native.parent.validate", "native.stage.create", "native.stage.bind", "native.marker.create", "native.marker.bind",
            "native.file.pagemaker365-api-1.4.3.zip.create", "native.file.pagemaker365-api-1.4.3.zip.bind",
            "native.directory.api.create", "native.directory.api.bind",
            "native.directory.api..pm365.create", "native.directory.api..pm365.bind",
            "native.file.api..pm365.provenance.json.create", "native.file.api..pm365.provenance.json.bind",
            "native.directory.api.dist.create", "native.directory.api.dist.bind",
            "native.file.api.dist.index.js.create", "native.file.api.dist.index.js.bind",
            "native.file.api.package.json.create", "native.file.api.package.json.bind",
            "native.file.pagemaker365-portal-1.4.3.zip.create", "native.file.pagemaker365-portal-1.4.3.zip.bind",
            "native.directory.portal.create", "native.directory.portal.bind",
            "native.directory.portal..pm365.create", "native.directory.portal..pm365.bind",
            "native.file.portal..pm365.generate-web-runtime-config.mjs.create", "native.file.portal..pm365.generate-web-runtime-config.mjs.bind",
            "native.file.portal..pm365.provenance.json.create", "native.file.portal..pm365.provenance.json.bind",
            "native.file.portal..pm365.start-portal-runtime.mjs.create", "native.file.portal..pm365.start-portal-runtime.mjs.bind",
            "native.file.portal.auth-redirect.html.create", "native.file.portal.auth-redirect.html.bind",
            "native.file.portal.index.html.create", "native.file.portal.index.html.bind",
            "native.inventory.enumerate-complete", "native.cleanup.delete", "native.cleanup.commit"
        ];
        return labels.SelectMany(label => new[] { label + ":before", label + ":after" })
            .Select(checkpoint => checkpoint.StartsWith("native.cleanup.", StringComparison.Ordinal)
                ? new NativeCancellationExpected(checkpoint, "cleanup-required", "runtime_deployment_recovery_stage_cleanup_required", false, 1, 2, 1, 2, 2, 1)
                : new NativeCancellationExpected(checkpoint, "failed", "runtime_deployment_recovery_rehearsal_failed", true, 0, 0, 0, 0, 0, 0))
            .ToArray();
    }

    private sealed record NativeCancellationExpected(string Checkpoint, string Status, string SafeCode, bool StageCleaned,
        int LicenseCalls, int Writes, int RandomGenerations, int Previews, int Approvals, int HandlerCalls);

    private static void ExecutesAllFixtureDeclaredDeliveryAndProtectedNegativeVectors()
    {
        var root = FindRepositoryRoot();
        var fixture = Path.Combine(root, "tests", "PageMaker365.Installer.Engine.Tests", "Fixtures", "private-runtime-v07-cross-repository-rehearsal-v1");
        using var delivery = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(fixture, "runtime-delivery-http-vectors.json")));
        using var protectedVectors = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(fixture, "protected-setting-acquisition-http-vectors.json")));
        var deliveryRows = delivery.RootElement.GetProperty("negativeVectors").EnumerateArray().ToArray();
        var protectedRows = protectedVectors.RootElement.GetProperty("negativeVectors").EnumerateArray().ToArray();
        AssertEx.Equal(32, deliveryRows.Length);
        AssertEx.Equal(40, protectedRows.Length);
        AssertEx.Equal(32, deliveryRows.Select(row => row.GetProperty("id").GetString()).Distinct(StringComparer.Ordinal).Count());
        AssertEx.Equal(40, protectedRows.Select(row => row.GetProperty("id").GetString()).Distinct(StringComparer.Ordinal).Count());
        foreach (var row in deliveryRows)
        {
            var id = row.GetProperty("id").GetString()!;
            var mutation = row.GetProperty("mutation").GetString()!;
            var actual = RuntimeBridgeTestHarness.ExecuteDeliveryNegative(mutation);
            AssertEx.Equal(row.GetProperty("expectedStatus").GetInt32(), actual.Status, row.GetProperty("id").GetString()!);
            AssertEx.Equal(row.GetProperty("expectedErrorCode").ValueKind == JsonValueKind.Null ? null : row.GetProperty("expectedErrorCode").GetString(), actual.ErrorCode);
            AssertEx.Equal(row.GetProperty("expectedArtifactOpenCount").GetInt32(), actual.ReadCount);
            AssertEx.Equal(row.GetProperty("expectedReceiptMutationCount").GetInt32(), actual.MutationCount);
            AssertEx.Equal(row.GetProperty("expectedResponseBodyBytes").GetInt32(), actual.ResponseBytes);
            AssertEx.False(actual.Result.AuthorizesDeployment, id);
            AssertEx.Equal(0, actual.ProtectedWrites, id);
            AssertEx.Equal(0, actual.RandomGenerations, id);
            AssertEx.Equal(0, actual.HandlerCalls, id);
            AssertEx.Equal(0, actual.Recoveries, id);
            if (id == "concurrent-downloads")
            {
                AssertEx.Equal(new RuntimeBridgeTwoCallRaceObservation(2, 1, 0, 1, 1), actual.RaceObservation, id);
                AssertEx.Equal("competitor", actual.DurableWinnerIdentity, id);
                AssertEx.Equal("opened:competitor", actual.DurableState, id);
                AssertEx.Equal(200, actual.CompetingStatusCode, id);
                AssertEx.Equal(1015, actual.CompetingBodyBytes, id);
                AssertEx.Equal(2, actual.RaceObservation!.CallCount, id);
                AssertEx.Equal(0, actual.ReceiptCalls, id);
            }
            else if (id == "receipt-event-mismatch")
            {
                AssertEx.Equal(new RuntimeBridgeTwoCallRaceObservation(2, 1, 0, 1, 1), actual.RaceObservation, id);
                AssertEx.Equal("competitor", actual.DurableWinnerIdentity, id);
                AssertEx.Equal("persisted:synthetic-w09-receipt:synthetic-w09-conflicting", actual.DurableState, id);
                AssertEx.Equal(201, actual.CompetingStatusCode, id);
                AssertEx.Equal(927, actual.CompetingBodyBytes, id);
                AssertEx.Equal(2, actual.ReceiptCalls, id);
            }
            else if (id == "receipt-replay")
            {
                AssertEx.Equal(new RuntimeBridgeTwoCallRaceObservation(2, 1, 1, 0, 1), actual.RaceObservation, id);
                AssertEx.Equal("competitor", actual.DurableWinnerIdentity, id);
                AssertEx.Equal("persisted:synthetic-w09-receipt:synthetic-w09-verified", actual.DurableState, id);
                AssertEx.Equal(201, actual.CompetingStatusCode, id);
                AssertEx.Equal(924, actual.CompetingBodyBytes, id);
                AssertEx.Equal(2, actual.ReceiptCalls, id);
            }
            else
            {
                AssertEx.True(actual.RaceObservation is null, id);
                AssertEx.True(actual.DurableWinnerIdentity is null && actual.DurableState is null, id);
            }
        }
        foreach (var row in protectedRows)
        {
            var id = row.GetProperty("id").GetString()!;
            var mutation = row.GetProperty("mutation").GetString()!;
            var actual = RuntimeBridgeTestHarness.ExecuteProtectedNegative(mutation);
            AssertEx.Equal(row.GetProperty("expectedStatus").GetInt32(), actual.Status, row.GetProperty("id").GetString()!);
            AssertEx.Equal(row.GetProperty("expectedErrorCode").GetString(), actual.ErrorCode);
            AssertEx.Equal(row.GetProperty("expectedProtectedReadCount").GetInt32(), actual.ReadCount);
            AssertEx.Equal(row.GetProperty("expectedRedemptionCount").GetInt32(), actual.MutationCount);
            AssertEx.Equal(row.GetProperty("expectedResponseBodyBytes").GetInt32(), actual.ResponseBytes);
            AssertEx.False(actual.Result.AuthorizesDeployment, id);
            AssertEx.Equal(0, actual.ProtectedWrites, id);
            AssertEx.Equal(0, actual.RandomGenerations, id);
            AssertEx.Equal(0, actual.HandlerCalls, id);
            AssertEx.Equal(0, actual.Recoveries, id);
            if (id == "concurrent-redemption")
            {
                AssertEx.Equal(new RuntimeBridgeTwoCallRaceObservation(2, 1, 0, 1, 1), actual.RaceObservation, id);
                AssertEx.Equal("competitor", actual.DurableWinnerIdentity, id);
                AssertEx.True(actual.DurableState!.StartsWith("redeemed:psr_", StringComparison.Ordinal), id);
                AssertEx.Equal(200, actual.CompetingStatusCode, id);
                AssertEx.Equal(1179, actual.CompetingBodyBytes, id);
            }
            else
            {
                AssertEx.True(actual.RaceObservation is null, id);
                AssertEx.True(actual.DurableWinnerIdentity is null && actual.DurableState is null, id);
            }
        }
        AssertEx.Equal(2, deliveryRows.Single(row => row.GetProperty("id").GetString() == "concurrent-downloads").GetProperty("expectedArtifactOpenCount").GetInt32());
        AssertEx.Equal(1, deliveryRows.Single(row => row.GetProperty("id").GetString() == "receipt-replay").GetProperty("expectedReceiptMutationCount").GetInt32());
        AssertEx.Equal(2, protectedRows.Single(row => row.GetProperty("id").GetString() == "concurrent-redemption").GetProperty("expectedProtectedReadCount").GetInt32());
        AssertEx.Equal(1, protectedRows.Single(row => row.GetProperty("id").GetString() == "concurrent-redemption").GetProperty("expectedRedemptionCount").GetInt32());
    }

    private static void RejectsExactZipLocalCentralAndEocdMutations()
    {
        var root = FindRepositoryRoot();
        var path = Path.Combine(root, "tests", "PageMaker365.Installer.Engine.Tests", "Fixtures", "private-runtime-v07-cross-repository-rehearsal-v1", "artifacts", "api.zip");
        var accepted = File.ReadAllBytes(path);
        RuntimeDeploymentRecoveryBridge.ValidateExactZipStructure(accepted);
        var eocd = accepted.Length - 22;
        var central = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(accepted.AsSpan(eocd + 16, 4)));
        var local = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(accepted.AsSpan(central + 42, 4)));
        void Deny(string name, Action<byte[]> mutate)
        {
            var value = accepted.ToArray(); mutate(value);
            _ = name;
            AssertEx.Throws<InvalidDataException>(() => RuntimeDeploymentRecoveryBridge.ValidateExactZipStructure(value));
        }
        Deny("local-signature", value => value[local] ^= 1);
        Deny("central-signature", value => value[central] ^= 1);
        Deny("eocd-signature", value => value[eocd] ^= 1);
        Deny("disk", value => BinaryPrimitives.WriteUInt16LittleEndian(value.AsSpan(eocd + 4, 2), 1));
        Deny("entry-count", value => BinaryPrimitives.WriteUInt16LittleEndian(value.AsSpan(eocd + 8, 2), 2));
        Deny("central-size", value => BinaryPrimitives.WriteUInt32LittleEndian(value.AsSpan(eocd + 12, 4), 184));
        Deny("central-offset", value => BinaryPrimitives.WriteUInt32LittleEndian(value.AsSpan(eocd + 16, 4), 807));
        Deny("comment", value => BinaryPrimitives.WriteUInt16LittleEndian(value.AsSpan(eocd + 20, 2), 1));
        Deny("made-by", value => BinaryPrimitives.WriteUInt16LittleEndian(value.AsSpan(central + 4, 2), 20));
        Deny("coherent-version", value => { BinaryPrimitives.WriteUInt16LittleEndian(value.AsSpan(local + 4, 2), 21); BinaryPrimitives.WriteUInt16LittleEndian(value.AsSpan(central + 6, 2), 21); });
        Deny("coherent-flags", value => { BinaryPrimitives.WriteUInt16LittleEndian(value.AsSpan(local + 6, 2), 0); BinaryPrimitives.WriteUInt16LittleEndian(value.AsSpan(central + 8, 2), 0); });
        Deny("coherent-compression", value => { BinaryPrimitives.WriteUInt16LittleEndian(value.AsSpan(local + 8, 2), 8); BinaryPrimitives.WriteUInt16LittleEndian(value.AsSpan(central + 10, 2), 8); });
        Deny("coherent-time", value => { BinaryPrimitives.WriteUInt16LittleEndian(value.AsSpan(local + 10, 2), 1); BinaryPrimitives.WriteUInt16LittleEndian(value.AsSpan(central + 12, 2), 1); });
        Deny("coherent-date", value => { BinaryPrimitives.WriteUInt16LittleEndian(value.AsSpan(local + 12, 2), 1); BinaryPrimitives.WriteUInt16LittleEndian(value.AsSpan(central + 14, 2), 1); });
        Deny("coherent-crc", value => { var crc = BinaryPrimitives.ReadUInt32LittleEndian(value.AsSpan(local + 14, 4)) ^ 1; BinaryPrimitives.WriteUInt32LittleEndian(value.AsSpan(local + 14, 4), crc); BinaryPrimitives.WriteUInt32LittleEndian(value.AsSpan(central + 16, 4), crc); });
        Deny("external-mode", value => BinaryPrimitives.WriteUInt32LittleEndian(value.AsSpan(central + 38, 4), 0xA1FF0000));
        Deny("local-offset", value => BinaryPrimitives.WriteUInt32LittleEndian(value.AsSpan(central + 42, 4), 1));
        Deny("local-central-name", value => value[central + 46] ^= 1);
        Deny("trailer", value => value[eocd + 20] = 1);
    }

    private static void RecursivelyClosesReceiptRequestAndResponseBodies()
    {
        foreach (var fault in Enum.GetValues<RuntimeBridgeReceiptNestedFault>().Where(value => value != RuntimeBridgeReceiptNestedFault.None))
        {
            var harness = new RuntimeBridgeTestHarness(receiptNestedFault: fault);
            try
            {
                var result = harness.Bridge.RunAsync(harness.Invocation()).GetAwaiter().GetResult();
                AssertEx.Equal("failed", result.Status, fault.ToString());
                AssertEx.Equal(1, harness.ArtifactTransport.ReceiptCount, fault.ToString());
                AssertEx.Equal(0, harness.WhatIf.CallCount, fault.ToString());
                AssertEx.Equal(0, harness.LicenseTransport.CallCount, fault.ToString());
                AssertEx.Equal(0, result.ProtectedWriteCount, fault.ToString());
            }
            finally { harness.Dispose(); }
        }
    }

    private static void PinsExplicitCancellationChecksAtEveryOwnedBoundary()
    {
        var ledger = ExpectedCancellationLedger();
        AssertEx.Equal(222, ledger.Count);
        AssertEx.Equal(ledger.Count, ledger.Select(row => row.Checkpoint).Distinct(StringComparer.Ordinal).Count());
        var baseline = new RuntimeBridgeTestHarness(portableStageMutation: PortableOwnedStageMutation.None,
            cancellationCheckpoint: "never");
        try
        {
            var result = baseline.Bridge.RunAsync(baseline.Invocation(), baseline.BoundaryCancellation!.Token).GetAwaiter().GetResult();
            AssertEx.Equal("simulated", result.Status, result.EvidenceJson);
            var observed = baseline.CancellationProbe!.Phases.ToArray();
            AssertEx.True(ledger.Select(row => row.Checkpoint).SequenceEqual(observed, StringComparer.Ordinal),
                $"Expected {ledger.Count} closed checkpoints; observed {observed.Length}.");
            AssertEx.Equal(observed.Length, observed.Distinct(StringComparer.Ordinal).Count());
            AssertEx.True(observed.Select((checkpoint, index) => (checkpoint, index)).All(item =>
                item.checkpoint.EndsWith(item.index % 2 == 0 ? ":before" : ":after", StringComparison.Ordinal)));
        }
        finally { baseline.Dispose(); baseline.BoundaryCancellation?.Dispose(); }

        foreach (var expected in ledger)
        {
            var harness = new RuntimeBridgeTestHarness(portableStageMutation: PortableOwnedStageMutation.None,
                cancellationCheckpoint: expected.Checkpoint);
            try
            {
                var result = harness.Bridge.RunAsync(harness.Invocation(), harness.BoundaryCancellation!.Token).GetAwaiter().GetResult();
                var id = expected.Checkpoint;
                AssertEx.Equal(expected.Status, result.Status, id);
                AssertEx.Equal(expected.SafeCode, result.SafeCode, id);
                AssertEx.Equal(expected.StageCleaned, result.StageCleaned, id);
                AssertEx.False(result.AuthorizesDeployment, id);
                AssertEx.Equal(expected.SessionCalls, harness.ArtifactTransport.SessionCount, id);
                AssertEx.Equal(expected.ArtifactCalls, harness.ArtifactTransport.AcquireCount, id);
                AssertEx.Equal(expected.ReceiptCalls, harness.ArtifactTransport.ReceiptCount, id);
                AssertEx.Equal(expected.ProtectedCalls, harness.LicenseTransport.CallCount, id);
                AssertEx.Equal(expected.ProtectedReads, harness.LicenseTransport.ProtectedReadCount, id);
                AssertEx.Equal(expected.Redemptions, harness.LicenseTransport.RedemptionCount, id);
                AssertEx.Equal(expected.Writes, harness.WriteSink.CallCount, id);
                AssertEx.Equal(expected.RandomGenerations, harness.CursorGenerator.CallCount, id);
                AssertEx.Equal(expected.Previews, harness.WhatIf.CallCount, id);
                AssertEx.Equal(expected.Approvals, harness.Approval.CallCount, id);
                AssertEx.Equal(expected.HandlerCalls, harness.Handler.CallCount, id);
                AssertEx.Equal(expected.Recoveries, harness.Recovery.CallCount, id);
                AssertEx.Equal(expected.CleanupCalls, harness.PortableStageStore!.CleanupCount, id);
                AssertEx.Equal(expected.OwnedDeleteCount, harness.PortableStageStore.DeleteCount, id);
                AssertEx.Equal(0, harness.PortableStageStore.UnownedPaths.Count, id);
                AssertEx.Equal(expected.SessionBodyBytes, harness.ArtifactTransport.SessionResponseBodyBytes, id);
                AssertEx.Equal(expected.ArtifactBodyBytes, harness.ArtifactTransport.ArtifactResponseBodyBytes, id);
                AssertEx.Equal(expected.ReceiptBodyBytes, harness.ArtifactTransport.ReceiptResponseBodyBytes, id);
                AssertEx.Equal(expected.ProtectedBodyBytes, harness.LicenseTransport.ResponseBodyBytes, id);
                AssertEx.Equal(0, result.OwnedReceipts.Count, id);
                AssertEx.True(result.FinalInput is null, id);
                AssertEx.True(result.EvidenceJson.Length > 0 && result.EvidenceSha256.Length == 64, id);
                AssertEx.True(harness.WriteSink.RetainedBuffers.All(buffer => buffer.Span.ToArray().All(value => value == 0)), id);
            }
            finally { harness.Dispose(); harness.BoundaryCancellation?.Dispose(); }
        }
    }

    private static IReadOnlyList<CancellationExpected> ExpectedCancellationLedger()
    {
        var rows = new List<CancellationExpected>();
        var state = new CancellationModel();
        void Boundary(string label, Action? committedEffect = null)
        {
            rows.Add(state.Snapshot(label + ":before"));
            committedEffect?.Invoke();
            rows.Add(state.Snapshot(label + ":after"));
        }
        Boundary("package.parse");
        Boundary("package.trust.validate");
        Boundary("input.provisional");
        Boundary("session.exchange", () => { state.SessionCalls = 1; state.SessionBodyBytes = 252; });
        Boundary("session.response.validate");
        AddArtifact("api", 1015, [17, 97, 97, 97, 97, 97, 97, 97, 97, 97, 97, 28]);
        AddArtifact("portal", 1809, [29, 131, 131, 131, 131, 131, 131, 131, 131, 131, 131, 131, 131, 131, 77]);
        Boundary("receipt.exchange", () => { state.ReceiptCalls = 1; state.ReceiptBodyBytes = 924; });
        Boundary("receipt.envelope.validate");
        Boundary("receipt.request.validate");
        Boundary("receipt.response.validate");
        Boundary("stage.create", () => { state.StageLease = true; state.StageOwned = true; state.OwnedCount = 1; });
        Stage("api",
        [
            ".pm365/provenance.json",
            "dist/index.js",
            "package.json"
        ]);
        Stage("portal",
        [
            ".pm365/generate-web-runtime-config.mjs",
            ".pm365/provenance.json",
            ".pm365/start-portal-runtime.mjs",
            "auth-redirect.html",
            "index.html"
        ]);
        Boundary("stage.inventory.complete");
        Boundary("whatif.provisional", () => state.Previews++);
        Boundary("whatif.provisional.validate");
        Boundary("approval.provisional", () => state.Approvals++);
        Boundary("approval.provisional.validate");
        Boundary("approval.provisional.consume");
        Boundary("protected.license.exchange", () =>
        {
            state.ProtectedCalls = 1; state.ProtectedReads = 1; state.Redemptions = 1; state.ProtectedBodyBytes = 1179;
        });
        Boundary("protected.license.response.validate");
        Boundary("protected.license.signature-fingerprint.validate");
        Boundary("protected.license.write", () => state.Writes++);
        Boundary("protected.license.receipt-content.validate");
        Boundary("protected.cursor.generate-write", () => { state.RandomGenerations++; state.Writes++; });
        Boundary("input.finalize");
        Boundary("input.final-shape.validate");
        Boundary("whatif.final", () => state.Previews++);
        Boundary("whatif.final.validate");
        Boundary("approval.final", () => state.Approvals++);
        Boundary("approval.final.validate");
        Boundary("approval.final.consume");
        Boundary("handler.invoke", () => state.HandlerCalls++);
        state.SimulationAccepted = true;
        Boundary("handler.result-digest.validate");
        Boundary("stage.cleanup", () =>
        {
            state.CleanupCalls++;
            state.OwnedDeleteCount += state.OwnedCount;
            state.OwnedCount = 0;
            state.StageOwned = false;
        });
        Boundary("evidence.commit");
        return rows;

        void AddArtifact(string kind, int fullLength, IReadOnlyList<int> ranges)
        {
            Boundary($"artifact.{kind}.full.exchange", () => { state.ArtifactCalls++; state.ArtifactBodyBytes += fullLength; });
            Boundary($"artifact.{kind}.full.header-digest.validate");
            for (var index = 0; index < ranges.Count; index++)
            {
                var length = ranges[index];
                Boundary($"artifact.{kind}.range.{index:D2}.exchange", () => { state.ArtifactCalls++; state.ArtifactBodyBytes += length; });
                Boundary($"artifact.{kind}.range.{index:D2}.header-digest.validate");
            }
            Boundary($"artifact.{kind}.range.reconstruction.validate");
            Boundary($"artifact.{kind}.zip.validate");
            Boundary($"artifact.{kind}.provenance.validate");
        }

        void Stage(string kind, IReadOnlyList<string> entries)
        {
            Boundary($"stage.{kind}.assert");
            Boundary($"stage.{kind}.archive.write", () => state.OwnedCount++);
            Boundary($"stage.{kind}.directory.create", () => state.OwnedCount++);
            var knownDirectories = new HashSet<string>(StringComparer.Ordinal) { kind };
            foreach (var entry in entries)
            {
                Boundary($"stage.{kind}/{entry}.write", () =>
                {
                    var parts = entry.Split('/');
                    for (var index = 1; index < parts.Length; index++)
                        if (knownDirectories.Add(kind + "/" + string.Join('/', parts.Take(index)))) state.OwnedCount++;
                    state.OwnedCount++;
                });
            }
        }
    }

    private sealed class CancellationModel
    {
        internal int SessionCalls, ArtifactCalls, ReceiptCalls, ProtectedCalls, ProtectedReads, Redemptions, Writes,
            RandomGenerations, Previews, Approvals, HandlerCalls, CleanupCalls, OwnedDeleteCount, OwnedCount,
            SessionBodyBytes, ArtifactBodyBytes, ReceiptBodyBytes, ProtectedBodyBytes;
        internal bool StageLease, StageOwned, SimulationAccepted;

        internal CancellationExpected Snapshot(string checkpoint)
        {
            var recoveryCount = SimulationAccepted ? 0 : Writes;
            var catchCleanupCalls = StageLease ? 1 : 0;
            var cleanupSucceeds = StageLease && StageOwned;
            var stageCleaned = !StageLease || cleanupSucceeds;
            var finalDeleteCount = OwnedDeleteCount + (cleanupSucceeds ? OwnedCount : 0);
            return new(checkpoint,
                stageCleaned ? "failed" : "cleanup-required",
                stageCleaned ? "runtime_deployment_recovery_rehearsal_failed" : "runtime_deployment_recovery_stage_cleanup_required",
                stageCleaned, SessionCalls, ArtifactCalls, ReceiptCalls, ProtectedCalls, ProtectedReads, Redemptions,
                Writes, RandomGenerations, Previews, Approvals, HandlerCalls, recoveryCount,
                CleanupCalls + catchCleanupCalls, finalDeleteCount, SessionBodyBytes, ArtifactBodyBytes,
                ReceiptBodyBytes, ProtectedBodyBytes);
        }
    }

    private sealed record CancellationExpected(string Checkpoint, string Status, string SafeCode, bool StageCleaned,
        int SessionCalls, int ArtifactCalls, int ReceiptCalls, int ProtectedCalls, int ProtectedReads, int Redemptions,
        int Writes, int RandomGenerations, int Previews, int Approvals, int HandlerCalls, int Recoveries,
        int CleanupCalls, int OwnedDeleteCount, int SessionBodyBytes, int ArtifactBodyBytes, int ReceiptBodyBytes,
        int ProtectedBodyBytes);

    private static void PinsCompleteFailureRecoveryCancellationUnion()
    {
        var success = ExpectedCancellationLedger().Select(row => row.Checkpoint).ToArray();
        var rows = new[]
        {
            RecoveryRows("cursor", RuntimeBridgeTestFailure.SecondWhatIf, "whatif.final:before",
                ["recovery.API_IMAGE_ASSET_CURSOR_SECRET:before", "recovery.API_IMAGE_ASSET_CURSOR_SECRET:after",
                    "recovery.API_LICENSE_SIGNED_PAYLOAD:before", "recovery.API_LICENSE_SIGNED_PAYLOAD:after",
                    "recovery.stage.cleanup:before", "recovery.stage.cleanup:after"],
                protectedWrites: 2, randomGenerations: 1, previews: 2, approvals: 1, recoveries: 2,
                targetLabels: ["recovery.API_IMAGE_ASSET_CURSOR_SECRET:before", "recovery.API_IMAGE_ASSET_CURSOR_SECRET:after"]),
            RecoveryRows("license", RuntimeBridgeTestFailure.CursorWrite, "protected.cursor.generate-write:before",
                ["recovery.API_LICENSE_SIGNED_PAYLOAD:before", "recovery.API_LICENSE_SIGNED_PAYLOAD:after",
                    "recovery.stage.cleanup:before", "recovery.stage.cleanup:after"],
                protectedWrites: 2, randomGenerations: 1, previews: 1, approvals: 1, recoveries: 1,
                targetLabels: ["recovery.API_LICENSE_SIGNED_PAYLOAD:before", "recovery.API_LICENSE_SIGNED_PAYLOAD:after",
                    "recovery.stage.cleanup:before", "recovery.stage.cleanup:after"]),
            RecoveryRows("simulation-cleanup", RuntimeBridgeTestFailure.None, "stage.cleanup:before",
                ["simulation-accepted.stage.cleanup:before", "simulation-accepted.stage.cleanup:after"],
                protectedWrites: 2, randomGenerations: 1, previews: 2, approvals: 2, recoveries: 0,
                handlerCalls: 1, failureCheckpoint: "stage.cleanup:before",
                targetLabels: ["simulation-accepted.stage.cleanup:before"]),
            RecoveryRows("evidence-commit", RuntimeBridgeTestFailure.None, "evidence.commit:before",
                ["simulation-accepted.stage.cleanup:before", "simulation-accepted.stage.cleanup:after"],
                protectedWrites: 2, randomGenerations: 1, previews: 2, approvals: 2, recoveries: 0,
                handlerCalls: 1, failureCheckpoint: "evidence.commit:before",
                targetLabels: ["simulation-accepted.stage.cleanup:after"], status: "cleanup-required",
                safeCode: "runtime_deployment_recovery_stage_cleanup_required", cleanupCalls: 2)
        }.SelectMany(group => group).ToArray();

        var expectedRecoveryLabels = new[]
        {
            "recovery.API_IMAGE_ASSET_CURSOR_SECRET:before", "recovery.API_IMAGE_ASSET_CURSOR_SECRET:after",
            "recovery.API_LICENSE_SIGNED_PAYLOAD:before", "recovery.API_LICENSE_SIGNED_PAYLOAD:after",
            "recovery.stage.cleanup:before", "recovery.stage.cleanup:after",
            "simulation-accepted.stage.cleanup:before", "simulation-accepted.stage.cleanup:after"
        };
        AssertEx.Equal(8, rows.Length);
        AssertEx.True(expectedRecoveryLabels.SequenceEqual(rows.Select(row => row.CancelCheckpoint), StringComparer.Ordinal),
            "failure/recovery cancellation rows must be an independent closed authority-ordered union");
        AssertEx.Equal(rows.Length, rows.Select(row => row.CancelCheckpoint).Distinct(StringComparer.Ordinal).Count());

        foreach (var expected in rows)
        {
            var harness = new RuntimeBridgeTestHarness(expected.Failure,
                portableStageMutation: PortableOwnedStageMutation.None,
                cancellationCheckpoint: expected.CancelCheckpoint,
                failureCheckpoint: expected.FailureCheckpoint);
            try
            {
                var result = harness.Bridge.RunAsync(harness.Invocation(), harness.BoundaryCancellation!.Token).GetAwaiter().GetResult();
                var id = expected.CancelCheckpoint;
                AssertEx.True(expected.ExpectedPhases.SequenceEqual(harness.CancellationProbe!.Phases, StringComparer.Ordinal),
                    $"{id}: expected {expected.ExpectedPhases.Count} checkpoints, observed {harness.CancellationProbe.Phases.Count}");
                AssertEx.True(harness.BoundaryCancellation.IsCancellationRequested, id);
                AssertEx.Equal(expected.Status, result.Status, id);
                AssertEx.Equal(expected.SafeCode, result.SafeCode, id);
                AssertEx.Equal(expected.Status == "failed", result.StageCleaned, id);
                AssertEx.False(result.AuthorizesDeployment, id);
                AssertEx.Equal(1, harness.ArtifactTransport.SessionCount, id);
                AssertEx.Equal(29, harness.ArtifactTransport.AcquireCount, id);
                AssertEx.Equal(1, harness.ArtifactTransport.ReceiptCount, id);
                AssertEx.Equal(1, harness.ArtifactTransport.ReceiptMutationCount, id);
                AssertEx.Equal(1, harness.LicenseTransport.CallCount, id);
                AssertEx.Equal(1, harness.LicenseTransport.ProtectedReadCount, id);
                AssertEx.Equal(1, harness.LicenseTransport.RedemptionCount, id);
                AssertEx.Equal(expected.ProtectedWrites, harness.WriteSink.CallCount, id);
                AssertEx.Equal(expected.CommittedWrites, result.ProtectedWriteCount, id);
                AssertEx.Equal(expected.RandomGenerations, harness.CursorGenerator.CallCount, id);
                AssertEx.Equal(expected.Previews, harness.WhatIf.CallCount, id);
                AssertEx.Equal(expected.Approvals, harness.Approval.CallCount, id);
                AssertEx.Equal(expected.HandlerCalls, harness.Handler.CallCount, id);
                AssertEx.Equal(expected.Recoveries, harness.Recovery.CallCount, id);
                AssertEx.Equal(expected.Recoveries, result.RecoveryCount, id);
                AssertEx.True(expected.RecoveredNames.SequenceEqual(harness.Recovery.RecoveredNames, StringComparer.Ordinal), id);
                AssertEx.Equal(expected.CleanupCalls, harness.PortableStageStore!.CleanupCount, id);
                AssertEx.Equal(16, harness.PortableStageStore.DeleteCount, id);
                AssertEx.False(harness.PortableStageStore.Exists, id);
                AssertEx.Equal(0, harness.PortableStageStore.InventoryCount, id);
                AssertEx.Equal(0, harness.PortableStageStore.OwnedCount, id);
                AssertEx.Equal(0, harness.PortableStageStore.UnownedPaths.Count, id);
                AssertEx.Equal(252, harness.ArtifactTransport.SessionResponseBodyBytes, id);
                AssertEx.Equal(5648, harness.ArtifactTransport.ArtifactResponseBodyBytes, id);
                AssertEx.Equal(924, harness.ArtifactTransport.ReceiptResponseBodyBytes, id);
                AssertEx.Equal(1179, harness.LicenseTransport.ResponseBodyBytes, id);
                AssertEx.Equal("empty", harness.ArtifactTransport.ReceiptDurableState, id);
                AssertEx.True(harness.ArtifactTransport.ConcurrentReceiptWinner is null, id);
                AssertEx.Equal("active", harness.LicenseTransport.ProtectedDurableState, id);
                AssertEx.True(harness.LicenseTransport.ConcurrentRedemptionWinner is null, id);
                AssertEx.Equal(0, result.OwnedReceipts.Count, id);
                AssertEx.True(result.FinalInput is null, id);
                AssertRecoveryEvidence(result, harness, expected, id);
                AssertEx.True(harness.LicenseTransport.ReturnedBuffer is not null &&
                    harness.LicenseTransport.ReturnedBuffer.All(value => value == 0), id);
                AssertEx.True(harness.CursorGenerator.ReturnedBuffer is not null &&
                    harness.CursorGenerator.ReturnedBuffer.All(value => value == 0), id);
                AssertEx.True(harness.WriteSink.RetainedBuffers.All(buffer => buffer.Span.ToArray().All(value => value == 0)), id);
            }
            finally { harness.Dispose(); harness.BoundaryCancellation?.Dispose(); }
        }

        IReadOnlyList<RecoveryCancellationExpected> RecoveryRows(string name, RuntimeBridgeTestFailure failure,
            string successTerminalCheckpoint, IReadOnlyList<string> tail, int protectedWrites, int randomGenerations,
            int previews, int approvals, int recoveries, int handlerCalls = 0, string? failureCheckpoint = null,
            IReadOnlyList<string>? targetLabels = null, string status = "failed",
            string safeCode = "runtime_deployment_recovery_rehearsal_failed", int cleanupCalls = 1)
        {
            var prefixLength = Array.IndexOf(success, successTerminalCheckpoint) + 1;
            AssertEx.True(prefixLength > 0, name);
            var phases = success.Take(prefixLength).Concat(tail).ToArray();
            var committedWrites = failure == RuntimeBridgeTestFailure.CursorWrite ? 1 : protectedWrites;
            var recoveredNames = failure == RuntimeBridgeTestFailure.SecondWhatIf
                ? new[] { "API_IMAGE_ASSET_CURSOR_SECRET", "API_LICENSE_SIGNED_PAYLOAD" }
                : failure == RuntimeBridgeTestFailure.CursorWrite ? new[] { "API_LICENSE_SIGNED_PAYLOAD" } : [];
            var targets = targetLabels ?? tail;
            return targets.Select(cancelCheckpoint => new RecoveryCancellationExpected(name, failure, failureCheckpoint,
                cancelCheckpoint, phases, status, safeCode, protectedWrites, committedWrites, randomGenerations, previews,
                approvals, handlerCalls, recoveries, cleanupCalls, recoveredNames)).ToArray();
        }

        static void AssertRecoveryEvidence(RuntimeBridgeResult result, RuntimeBridgeTestHarness harness,
            RecoveryCancellationExpected expected, string id)
        {
            AssertEx.Equal(RuntimeBridgeTestHarness.Sha256(System.Text.Encoding.UTF8.GetBytes(result.EvidenceJson)), result.EvidenceSha256, id);
            using var evidence = JsonDocument.Parse(result.EvidenceJson);
            var root = evidence.RootElement;
            AssertEx.Equal(expected.Status, root.GetProperty("status").GetString(), id);
            AssertEx.Equal(expected.SafeCode, root.GetProperty("safeCode").GetString(), id);
            AssertEx.Equal(expected.Status == "failed", root.GetProperty("stageCleaned").GetBoolean(), id);
            AssertEx.Equal(harness.WhatIf.Results.Count, root.GetProperty("previewSha256s").GetArrayLength(), id);
            AssertEx.Equal(harness.Approval.Receipts.Count, root.GetProperty("approvalSha256s").GetArrayLength(), id);
            AssertEx.Equal(expected.CommittedWrites, root.GetProperty("receiptIdentitySha256s").GetArrayLength(), id);
            AssertEx.Equal(expected.Recoveries, root.GetProperty("recoverySemanticSha256s").GetArrayLength(), id);
            var previewDigests = root.GetProperty("previewSha256s").EnumerateArray().Select(value => value.GetString()).ToArray();
            AssertEx.True(harness.WhatIf.Results.Select(value => value.PreviewSha256).SequenceEqual(previewDigests, StringComparer.Ordinal), id);
            var approvalDigests = root.GetProperty("approvalSha256s").EnumerateArray().Select(value => value.GetString()).ToArray();
            AssertEx.True(harness.Approval.Challenges.Take(harness.Approval.Receipts.Count).Select(StableApprovalBinding)
                .SequenceEqual(approvalDigests, StringComparer.Ordinal), id);
            var receiptDigests = root.GetProperty("receiptIdentitySha256s").EnumerateArray().Select(value => value.GetString()).ToArray();
            AssertEx.True(harness.WriteSink.Receipts.Select(ReceiptIdentity).SequenceEqual(receiptDigests, StringComparer.Ordinal), id);
            var recoveryDigests = root.GetProperty("recoverySemanticSha256s").EnumerateArray().Select(value => value.GetString()).ToArray();
            AssertEx.True(harness.WriteSink.Receipts.AsEnumerable().Reverse().Take(expected.Recoveries)
                .Select(receipt => RuntimeBridgeTestHarness.Sha256(System.Text.Encoding.UTF8.GetBytes($"{ReceiptIdentity(receipt)}\nrecovered\n1\n")))
                .SequenceEqual(recoveryDigests, StringComparer.Ordinal), id);
            var handlerDigest = root.GetProperty("handlerResultSha256");
            if (expected.HandlerCalls == 0) AssertEx.Equal(JsonValueKind.Null, handlerDigest.ValueKind, id);
            else AssertEx.Equal(harness.Handler.LastResult!.ResultSha256, handlerDigest.GetString(), id);
            var counts = root.GetProperty("counts");
            AssertEx.Equal(expected.CommittedWrites, counts.GetProperty("protectedWrites").GetInt32(), id);
            AssertEx.Equal(harness.WhatIf.Results.Count, counts.GetProperty("whatIf").GetInt32(), id);
            AssertEx.Equal(harness.Approval.Receipts.Count, counts.GetProperty("approvals").GetInt32(), id);
            AssertEx.Equal(expected.HandlerCalls, counts.GetProperty("handler").GetInt32(), id);
            AssertEx.Equal(expected.Recoveries, counts.GetProperty("recovery").GetInt32(), id);
        }

        static string StableApprovalBinding(RuntimeBridgeApprovalChallenge challenge) => RuntimeBridgeTestHarness.Sha256(
            System.Text.Encoding.UTF8.GetBytes($"{challenge.Phase}\n{challenge.PackageHash}\n{challenge.InputSha256}\n{challenge.PreviewSha256}\n{challenge.ArtifactIdentitySha256}\n{challenge.RecoveryPlanSha256}\n{challenge.PhaseOneApprovalDigest}\n{string.Join(',', challenge.ReceiptIdentitySha256s)}\n"));

        static string ReceiptIdentity(RuntimeBridgeProtectedWriteReceipt receipt) => RuntimeBridgeTestHarness.Sha256(
            System.Text.Encoding.UTF8.GetBytes($"{receipt.Name}\n{receipt.Mode}\n{receipt.VaultResourceId}\n{receipt.SecretName}\n{receipt.SecretVersion}\n{receipt.KeyVaultReference}\n{receipt.ContentSha256}\n{receipt.PackageHash}\n{receipt.ApprovalDigest}\n{receipt.Outcome}\n{receipt.WriteCount}\n"));
    }

    private sealed record RecoveryCancellationExpected(string Name, RuntimeBridgeTestFailure Failure, string? FailureCheckpoint,
        string CancelCheckpoint, IReadOnlyList<string> ExpectedPhases, string Status, string SafeCode, int ProtectedWrites,
        int CommittedWrites, int RandomGenerations, int Previews, int Approvals, int HandlerCalls, int Recoveries,
        int CleanupCalls, IReadOnlyList<string> RecoveredNames);

    private static void StopsAtOwnedFailureAndCancellationBoundaries()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var before = new RuntimeBridgeTestHarness();
        try
        {
            AssertEx.Throws<OperationCanceledException>(() => before.Bridge.RunAsync(before.Invocation(), cancellation.Token).GetAwaiter().GetResult());
            AssertEx.Equal(0, before.ArtifactTransport.SessionCount);
            AssertEx.Equal(0, before.LicenseTransport.CallCount);
        }
        finally { before.Dispose(); }

        AssertFailure(RuntimeBridgeTestFailure.LicenseWrite, expectedWrites: 1, expectedRecoveries: 0, expectedHandler: 0);
        AssertFailure(RuntimeBridgeTestFailure.CursorWrite, expectedWrites: 2, expectedRecoveries: 1, expectedHandler: 0);
        AssertFailure(RuntimeBridgeTestFailure.ApprovalTwo, expectedWrites: 2, expectedRecoveries: 2, expectedHandler: 0);
        AssertFailure(RuntimeBridgeTestFailure.HandlerFailure, expectedWrites: 2, expectedRecoveries: 2, expectedHandler: 1);
        AssertFailure(RuntimeBridgeTestFailure.SecondWhatIfCancellation, expectedWrites: 2, expectedRecoveries: 2, expectedHandler: 0);
    }

    private static void AssertFailure(RuntimeBridgeTestFailure failure, int expectedWrites, int expectedRecoveries, int expectedHandler)
    {
        var harness = new RuntimeBridgeTestHarness(failure);
        try
        {
            var result = harness.Bridge.RunAsync(harness.Invocation()).GetAwaiter().GetResult();
            AssertEx.Equal("failed", result.Status);
            AssertEx.Equal(1, harness.LicenseTransport.CallCount);
            AssertEx.Equal(expectedWrites, harness.WriteSink.CallCount);
            AssertEx.Equal(expectedRecoveries, harness.Recovery.CallCount);
            AssertEx.Equal(expectedHandler, harness.Handler.CallCount);
            AssertEx.True(result.StageCleaned);
            AssertEx.Equal(0, Directory.GetDirectories(harness.WorkspaceRoot).Length);
            AssertEx.True(harness.LicenseTransport.ReturnedBuffer!.All(value => value == 0));
            if (harness.CursorGenerator.ReturnedBuffer is not null) AssertEx.True(harness.CursorGenerator.ReturnedBuffer.All(value => value == 0));
        }
        finally { harness.Dispose(); }
    }

    private static void RunsTheExactSyntheticSequenceAndReturnsOnlyRedactedEvidence()
    {
        var harness = new RuntimeBridgeTestHarness();
        try
        {
            var result = harness.Bridge.RunAsync(harness.Invocation()).GetAwaiter().GetResult();
            AssertEx.Equal("simulated", result.Status, result.EvidenceJson);
            AssertEx.Equal("runtime_deployment_recovery_rehearsal_completed", result.SafeCode);
            AssertEx.False(result.AuthorizesDeployment);
            AssertEx.Equal(1, result.LicenseAcquisitionCount);
            AssertEx.Equal(2, result.ProtectedWriteCount);
            AssertEx.Equal(2, result.WhatIfCount);
            AssertEx.Equal(2, result.ApprovalCount);
            AssertEx.Equal(1, result.HandlerCount);
            AssertEx.Equal(0, result.RecoveryCount);
            AssertEx.Equal(0, result.OwnedReceipts.Count);
            AssertEx.True(result.StageCleaned);
            AssertEx.Equal(42, result.FinalInput!.ApiPublicSettings.Count + result.FinalInput.PortalPublicSettings.Count);
            AssertEx.Equal(4, result.FinalInput.ApiVersionedProtectedSettingReferences.Count);
            AssertEx.Equal(RuntimeConfigurationFinalizedDeploymentInputV2.ContractVersionValue, result.FinalInput.ContractVersion);
            AssertEx.Equal(RuntimeBridgeTestHarness.Sha256(System.Text.Encoding.UTF8.GetBytes(result.EvidenceJson)), result.EvidenceSha256);
            AssertEx.True(harness.Trace.IndexOf("write-API_LICENSE_SIGNED_PAYLOAD") < harness.Trace.IndexOf("cursor-generate"));
            AssertEx.True(harness.Trace.IndexOf("write-API_LICENSE_SIGNED_PAYLOAD") < harness.Trace.IndexOf("write-API_IMAGE_ASSET_CURSOR_SECRET"));
            AssertEx.True(harness.Trace.IndexOf("whatif-provisional") < harness.Trace.IndexOf("approval-provisional"));
            AssertEx.True(harness.Trace.IndexOf("whatif-final") < harness.Trace.IndexOf("approval-final"));
            AssertEx.True(harness.Trace.IndexOf("approval-final") < harness.Trace.IndexOf("handler"));
            AssertEx.Equal(29, harness.ArtifactTransport.AcquireCount);
            AssertEx.Equal(1, harness.ArtifactTransport.ReceiptCount);
            AssertEx.Equal(43, harness.ArtifactTransport.LastSession!.RequestBodyUtf8.Length);
            AssertEx.Equal(252, harness.ArtifactTransport.LastSession.ResponseBodyUtf8.Length);
            AssertEx.Equal(1, harness.ArtifactTransport.SessionMutationCount);
            AssertEx.Equal(1, harness.ArtifactTransport.SessionReplayProbeCount);
            AssertEx.Equal(200, harness.ArtifactTransport.SessionReplayStatusCode);
            AssertEx.Equal(1029, harness.ArtifactTransport.LastReceipt!.RequestBodyUtf8.Length);
            AssertEx.Equal(924, harness.ArtifactTransport.LastReceipt.ResponseBodyUtf8.Length);
            AssertEx.Equal(1, harness.ArtifactTransport.LastReceipt.MutationCount);
            AssertEx.Equal(1, harness.ArtifactTransport.ReceiptReplayProbeCount);
            AssertEx.Equal(200, harness.ArtifactTransport.ReceiptReplayStatusCode);
            AssertEx.Equal(252, harness.LicenseTransport.LastResponse!.RequestBodyUtf8.Length);
            AssertEx.Equal(1179, harness.LicenseTransport.LastResponse.ResponseBodyUtf8.Length);
            AssertEx.Equal(1, harness.LicenseTransport.LastResponse.ProtectedReadCount);
            AssertEx.Equal(1, harness.LicenseTransport.LastResponse.RedemptionCount);
            AssertEx.Equal(1, harness.LicenseTransport.ReplayProbeCount);
            AssertEx.Equal(404, harness.LicenseTransport.ReplayStatusCode);
            AssertGapFreeRanges(harness.ArtifactTransport.ArtifactRequests, "api", 1015, expectedRangeCount: 12, pinnedOffset: 17, pinnedLength: 97);
            AssertGapFreeRanges(harness.ArtifactTransport.ArtifactRequests, "portal", 1809, expectedRangeCount: 15, pinnedOffset: 29, pinnedLength: 131);
            AssertEx.Equal(0, harness.WhatIf.Requests[0].ReceiptIdentitySha256s.Count);
            AssertEx.Equal(2, harness.WhatIf.Requests[1].ReceiptIdentitySha256s.Count);
            AssertEx.True(harness.WhatIf.Requests[1].ReceiptIdentitySha256s.All(value => value.Length == 64));
            AssertEx.False(harness.WhatIf.Requests[0].InputSha256 == harness.WhatIf.Requests[1].InputSha256);
            AssertEx.True(harness.WhatIf.Requests[1].PhaseOneApprovalDigest is not null);
            AssertEx.True(harness.LicenseTransport.ReturnedBuffer!.All(value => value == 0), "Signed license bytes must be zeroed after the write.");
            AssertEx.True(harness.CursorGenerator.ReturnedBuffer!.All(value => value == 0), "Cursor entropy must be zeroed after the write.");
            AssertEx.True(harness.WriteSink.RetainedBuffers.All(buffer => buffer.ToArray().All(value => value == 0)), "All callback-retained protected buffers must be zeroed.");
            foreach (var forbidden in new[] { "pagemaker365.license.v1", "licenseId", "PRIVATE KEY", "psr_", "@Microsoft.KeyVault", "image-cursor-secret" })
                AssertEx.False(result.EvidenceJson.Contains(forbidden, StringComparison.OrdinalIgnoreCase), $"Evidence disclosed {forbidden}.");
            using var evidence = JsonDocument.Parse(result.EvidenceJson);
            AssertEx.False(evidence.RootElement.GetProperty("authorizesDeployment").GetBoolean());
            AssertEx.Equal(result.FinalInput.PackageHash, evidence.RootElement.GetProperty("packageHash").GetString());
            AssertEx.Equal(result.FinalInput.ProjectionSha256, evidence.RootElement.GetProperty("projectionSha256").GetString());
            AssertEx.Equal(2, evidence.RootElement.GetProperty("previewSha256s").GetArrayLength());
            AssertEx.Equal(2, evidence.RootElement.GetProperty("approvalSha256s").GetArrayLength());
            AssertEx.Equal(2, evidence.RootElement.GetProperty("receiptIdentitySha256s").GetArrayLength());
            AssertEx.Equal(harness.Handler.LastResult!.ResultSha256, evidence.RootElement.GetProperty("handlerResultSha256").GetString());
            AssertEx.Equal(0, evidence.RootElement.GetProperty("counts").GetProperty("recovery").GetInt32());
            AssertEx.Equal(0, Directory.GetDirectories(harness.WorkspaceRoot).Length);
        }
        finally { harness.Dispose(); }
    }

    private static void AssertGapFreeRanges(
        IEnumerable<RuntimeBridgeArtifactRequest> requests,
        string kind,
        long totalLength,
        int expectedRangeCount,
        long pinnedOffset,
        int pinnedLength)
    {
        var ranges = requests.Where(item => item.ArtifactKind == kind && item.RangeOffset is not null).ToArray();
        AssertEx.Equal(expectedRangeCount, ranges.Length, kind);
        var cursor = 0L;
        foreach (var range in ranges)
        {
            AssertEx.Equal(cursor, range.RangeOffset!.Value, kind);
            AssertEx.True(range.RangeLength is > 0, kind);
            AssertEx.Equal($"bytes={range.RangeOffset}-{range.RangeOffset + range.RangeLength - 1}",
                range.OrderedHeaders.Single(item => item.Name == "Range").Value, kind);
            cursor += range.RangeLength!.Value;
        }
        AssertEx.Equal(totalLength, cursor, kind);
        var pinned = ranges.Single(item => item.RangeOffset == pinnedOffset && item.RangeLength == pinnedLength);
        AssertEx.Equal($"{kind}-range", pinned.VectorId, kind);
        AssertEx.True(ranges.Where(item => !ReferenceEquals(item, pinned)).All(item => item.VectorId.StartsWith($"{kind}-range-derived-", StringComparison.Ordinal)), kind);
    }

    private static void RemainsDefaultDisabledAndCapabilityClosed()
    {
        var harness = new RuntimeBridgeTestHarness();
        try
        {
            var result = harness.Bridge.RunAsync(harness.Invocation(enabled: false)).GetAwaiter().GetResult();
            AssertEx.Equal("denied", result.Status);
            AssertEx.Equal("runtime_deployment_recovery_bridge_disabled", result.SafeCode);
            AssertEx.Equal(0, harness.ArtifactTransport.SessionCount);
            AssertEx.Equal(0, harness.LicenseTransport.CallCount);
            AssertEx.Equal(0, harness.WriteSink.CallCount);
            AssertEx.Equal(0, harness.WhatIf.CallCount);
            AssertEx.Equal(0, harness.Approval.CallCount);
            AssertEx.Equal(0, harness.Handler.CallCount);
            harness.AssertCapabilityMismatchDenied();
            var wrongLicenseTrust = harness.RunWithWrongLicenseTrust();
            AssertEx.Equal("failed", wrongLicenseTrust.Status);
            AssertEx.Equal(0, harness.ArtifactTransport.SessionCount);
            AssertEx.Equal(0, harness.LicenseTransport.CallCount);

            var capability = typeof(RuntimeBridgeSyntheticTestCapability);
            AssertEx.Equal(0, capability.GetConstructors(BindingFlags.Public | BindingFlags.Instance).Length);
            AssertEx.Equal(0, typeof(RuntimeDeploymentRecoveryBridge).GetConstructors(BindingFlags.Public | BindingFlags.Instance).Length);
            AssertEx.Equal(0, typeof(RuntimeDeploymentRecoveryBridge).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Length);
            AssertEx.False(typeof(RuntimeBridgeResult).IsPublic);
        }
        finally { harness.Dispose(); }
    }

    private static void RecoversInReverseOrderAndTreatsAmbiguityAsTerminal()
    {
        var failure = new RuntimeBridgeTestHarness(RuntimeBridgeTestFailure.SecondWhatIf);
        try
        {
            var result = failure.Bridge.RunAsync(failure.Invocation()).GetAwaiter().GetResult();
            AssertEx.Equal("failed", result.Status);
            AssertEx.Equal(2, result.RecoveryCount);
            var cursorRecovery = failure.Trace.IndexOf("recover-API_IMAGE_ASSET_CURSOR_SECRET");
            var licenseRecovery = failure.Trace.IndexOf("recover-API_LICENSE_SIGNED_PAYLOAD");
            AssertEx.True(cursorRecovery >= 0 && cursorRecovery < licenseRecovery, "Recovery must reverse cursor then license.");
            AssertEx.Equal(1, failure.LicenseTransport.CallCount);
            AssertEx.Equal(0, failure.Handler.CallCount);
        }
        finally { failure.Dispose(); }

        var ambiguity = new RuntimeBridgeTestHarness(RuntimeBridgeTestFailure.HandlerAmbiguous);
        try
        {
            var first = ambiguity.Bridge.RunAsync(ambiguity.Invocation()).GetAwaiter().GetResult();
            var replay = ambiguity.Bridge.RunAsync(ambiguity.Invocation()).GetAwaiter().GetResult();
            AssertEx.Equal("runtime_deployment_recovery_terminal_ambiguity", first.SafeCode);
            AssertEx.True(ReferenceEquals(first, replay));
            AssertEx.Equal(1, ambiguity.Handler.CallCount);
            AssertEx.Equal(1, ambiguity.LicenseTransport.CallCount);
            AssertEx.Equal(2, ambiguity.Recovery.CallCount);
        }
        finally { ambiguity.Dispose(); }

        AssertRecoveryOwnership(RuntimeBridgeTestFailure.CursorRecoveryFailure, "API_IMAGE_ASSET_CURSOR_SECRET", expectedRecoveries: 1);
        AssertRecoveryOwnership(RuntimeBridgeTestFailure.LicenseRecoveryFailure, "API_LICENSE_SIGNED_PAYLOAD", expectedRecoveries: 1);
        AssertRecoveryOwnership(RuntimeBridgeTestFailure.CursorRecoveryAmbiguous, "API_IMAGE_ASSET_CURSOR_SECRET", expectedRecoveries: 1);
        AssertRecoveryOwnership(RuntimeBridgeTestFailure.LicenseRecoveryAmbiguous, "API_LICENSE_SIGNED_PAYLOAD", expectedRecoveries: 1);
        AssertRecoveryOwnership(RuntimeBridgeTestFailure.CursorRecoveryFailure | RuntimeBridgeTestFailure.LicenseRecoveryFailure,
            expectedOwnedName: null, expectedRecoveries: 0, expectedOwnedCount: 2);
    }

    private static void AssertRecoveryOwnership(
        RuntimeBridgeTestFailure recoveryFailure,
        string? expectedOwnedName,
        int expectedRecoveries,
        int expectedOwnedCount = 1)
    {
        var harness = new RuntimeBridgeTestHarness(RuntimeBridgeTestFailure.SecondWhatIf | recoveryFailure);
        try
        {
            var result = harness.Bridge.RunAsync(harness.Invocation()).GetAwaiter().GetResult();
            AssertEx.Equal("recovery-required", result.Status);
            AssertEx.Equal("runtime_deployment_recovery_required", result.SafeCode);
            AssertEx.Equal(expectedRecoveries, result.RecoveryCount);
            AssertEx.Equal(2, harness.Recovery.CallCount);
            AssertEx.Equal(expectedOwnedCount, result.OwnedReceipts.Count);
            if (expectedOwnedName is not null) AssertEx.Equal(expectedOwnedName, result.OwnedReceipts.Single().Name);
            AssertEx.True(result.OwnedReceipts.All(receipt => receipt.Outcome == "written" && receipt.WriteCount == 1));
        }
        finally { harness.Dispose(); }
    }

    private static void SerializesConcurrentReplayWithoutRepeatingEffects()
    {
        var harness = new RuntimeBridgeTestHarness();
        try
        {
            var tasks = Enumerable.Range(0, 8).Select(_ => harness.Bridge.RunAsync(harness.Invocation())).ToArray();
            Task.WaitAll(tasks);
            AssertEx.True(tasks.All(task => ReferenceEquals(tasks[0].Result, task.Result)));
            AssertEx.Equal(1, harness.LicenseTransport.CallCount);
            AssertEx.Equal(2, harness.WriteSink.CallCount);
            AssertEx.Equal(2, harness.WhatIf.CallCount);
            AssertEx.Equal(2, harness.Approval.CallCount);
            AssertEx.Equal(1, harness.Handler.CallCount);
        }
        finally { harness.Dispose(); }
    }

    private static void BindsReplayToTheExactInvocationIdentity()
    {
        var harness = new RuntimeBridgeTestHarness();
        try
        {
            var original = harness.Bridge.RunAsync(harness.Invocation()).GetAwaiter().GetResult();
            var exactReplay = harness.Bridge.RunAsync(harness.Invocation()).GetAwaiter().GetResult();
            AssertEx.True(ReferenceEquals(original, exactReplay));
            var denied = harness.Bridge.RunAsync(harness.Invocation(installerVersion: "0.0.1-synthetic")).GetAwaiter().GetResult();
            AssertEx.Equal("denied", denied.Status);
            AssertEx.Equal("runtime_deployment_recovery_invocation_reuse", denied.SafeCode);
            AssertEx.Equal(1, harness.LicenseTransport.CallCount);
            AssertEx.Equal(2, harness.WriteSink.CallCount);
            AssertEx.Equal(1, harness.Handler.CallCount);
            var changedPackage = harness.Bridge.RunAsync(harness.Invocation(packageJson: harness.PackageJson + "\n")).GetAwaiter().GetResult();
            AssertEx.Equal("denied", changedPackage.Status);
            AssertEx.Equal("runtime_deployment_recovery_invocation_reuse", changedPackage.SafeCode);
            AssertEx.Equal(1, harness.LicenseTransport.CallCount);

            var independent = harness.Bridge.RunAsync(harness.Invocation(invocationId: "inv_SYNTHETIC_RUNTIME_0002")).GetAwaiter().GetResult();
            AssertEx.Equal("simulated", independent.Status, independent.EvidenceJson);
            AssertEx.Equal(2, harness.LicenseTransport.CallCount);
            AssertEx.Equal(4, harness.WriteSink.CallCount);
            AssertEx.Equal(2, harness.Handler.CallCount);
        }
        finally { harness.Dispose(); }
    }

    private static void ProducesDeterministicEvidenceAcrossVolatileIdentitiesAndStages()
    {
        var first = new RuntimeBridgeTestHarness(volatileIdentity: "FIRST");
        var second = new RuntimeBridgeTestHarness(volatileIdentity: "SECOND");
        try
        {
            var firstResult = first.Bridge.RunAsync(first.Invocation()).GetAwaiter().GetResult();
            var secondResult = second.Bridge.RunAsync(second.Invocation()).GetAwaiter().GetResult();
            AssertEx.Equal("simulated", firstResult.Status, firstResult.EvidenceJson);
            AssertEx.Equal("simulated", secondResult.Status, secondResult.EvidenceJson);
            AssertEx.Equal(firstResult.EvidenceJson, secondResult.EvidenceJson);
            AssertEx.Equal(firstResult.EvidenceSha256, secondResult.EvidenceSha256);
            AssertEx.False(first.Approval.ApprovalIds.SequenceEqual(second.Approval.ApprovalIds));
            AssertEx.False(first.WriteSink.ReceiptIds.SequenceEqual(second.WriteSink.ReceiptIds));
            AssertEx.False(first.WorkspaceRoot == second.WorkspaceRoot);
            AssertEx.Equal(first.CursorGenerator.SourceSha256, second.CursorGenerator.SourceSha256);
            AssertNoOpaqueRecoveryHandles(firstResult, first.WriteSink.ReceiptIds);
            AssertNoOpaqueRecoveryHandles(secondResult, second.WriteSink.ReceiptIds);
        }
        finally { first.Dispose(); second.Dispose(); }
    }

    private static void ExecutesTheClosedProtectedContentEvidenceLedger()
    {
        var executed = new List<string>();
        var expected = new[]
        {
            "evidence.same-content-different-volatile-identities",
            "evidence.different-cursor-content",
            "evidence.different-freshly-signed-license-content",
            "receipt.content-digest-denial-matrix",
            "evidence.recovered-failure-excludes-opaque-receipt-ids",
            "evidence.recovery-required-excludes-opaque-receipt-ids"
        };

        Run("evidence.same-content-different-volatile-identities", () =>
        {
            var first = new RuntimeBridgeTestHarness(volatileIdentity: "LEDGER-FIRST", cursorEntropy: CursorEntropy(0x31));
            var second = new RuntimeBridgeTestHarness(volatileIdentity: "LEDGER-SECOND", cursorEntropy: CursorEntropy(0x31));
            try
            {
                var firstResult = first.Bridge.RunAsync(first.Invocation()).GetAwaiter().GetResult();
                var secondResult = second.Bridge.RunAsync(second.Invocation()).GetAwaiter().GetResult();
                AssertEx.Equal("simulated", firstResult.Status, firstResult.EvidenceJson);
                AssertEx.Equal("simulated", secondResult.Status, secondResult.EvidenceJson);
                AssertEx.Equal(firstResult.EvidenceJson, secondResult.EvidenceJson);
                AssertEx.Equal(firstResult.EvidenceSha256, secondResult.EvidenceSha256);
                AssertEx.False(first.Approval.ApprovalIds.SequenceEqual(second.Approval.ApprovalIds));
                AssertEx.False(first.WriteSink.ReceiptIds.SequenceEqual(second.WriteSink.ReceiptIds));
                AssertEx.True(first.WriteSink.Receipts.Select(item => item.ContentSha256)
                    .SequenceEqual(second.WriteSink.Receipts.Select(item => item.ContentSha256), StringComparer.Ordinal));
                AssertNoOpaqueRecoveryHandles(firstResult, first.WriteSink.ReceiptIds);
                AssertNoOpaqueRecoveryHandles(secondResult, second.WriteSink.ReceiptIds);
            }
            finally { first.Dispose(); second.Dispose(); }
        });

        Run("evidence.different-cursor-content", () =>
        {
            var first = new RuntimeBridgeTestHarness(volatileIdentity: "CURSOR-A", cursorEntropy: CursorEntropy(0x41));
            var second = new RuntimeBridgeTestHarness(volatileIdentity: "CURSOR-B", cursorEntropy: CursorEntropy(0x42));
            try
            {
                var firstResult = first.Bridge.RunAsync(first.Invocation()).GetAwaiter().GetResult();
                var secondResult = second.Bridge.RunAsync(second.Invocation()).GetAwaiter().GetResult();
                AssertEx.Equal("simulated", firstResult.Status, firstResult.EvidenceJson);
                AssertEx.Equal("simulated", secondResult.Status, secondResult.EvidenceJson);
                var firstCursor = first.WriteSink.Receipts.Single(item => item.Name == "API_IMAGE_ASSET_CURSOR_SECRET");
                var secondCursor = second.WriteSink.Receipts.Single(item => item.Name == "API_IMAGE_ASSET_CURSOR_SECRET");
                AssertEx.False(firstCursor.ContentSha256 == secondCursor.ContentSha256);
                AssertEx.False(first.WhatIf.Requests[1].ReceiptIdentitySha256s[1] == second.WhatIf.Requests[1].ReceiptIdentitySha256s[1]);
                AssertEx.False(firstResult.FinalInput!.InputSha256 == secondResult.FinalInput!.InputSha256);
                AssertEx.False(first.WhatIf.Requests[1].InputSha256 == second.WhatIf.Requests[1].InputSha256);
                AssertEx.False(firstResult.EvidenceJson == secondResult.EvidenceJson);
                AssertEx.False(firstResult.EvidenceSha256 == secondResult.EvidenceSha256);
                AssertEx.True(first.CursorGenerator.ReturnedBuffer!.All(value => value == 0));
                AssertEx.True(second.CursorGenerator.ReturnedBuffer!.All(value => value == 0));
                AssertNoOpaqueRecoveryHandles(firstResult, first.WriteSink.ReceiptIds);
                AssertNoOpaqueRecoveryHandles(secondResult, second.WriteSink.ReceiptIds);
            }
            finally { first.Dispose(); second.Dispose(); }
        });

        Run("evidence.different-freshly-signed-license-content", () =>
        {
            var first = new RuntimeBridgeTestHarness(licenseVariant: 1, cursorEntropy: CursorEntropy(0x51));
            var second = new RuntimeBridgeTestHarness(licenseVariant: 2, cursorEntropy: CursorEntropy(0x51));
            try
            {
                var firstResult = first.Bridge.RunAsync(first.Invocation()).GetAwaiter().GetResult();
                var secondResult = second.Bridge.RunAsync(second.Invocation()).GetAwaiter().GetResult();
                AssertEx.Equal("simulated", firstResult.Status, firstResult.EvidenceJson);
                AssertEx.Equal("simulated", secondResult.Status, secondResult.EvidenceJson);
                var firstLicense = first.WriteSink.Receipts.Single(item => item.Name == "API_LICENSE_SIGNED_PAYLOAD");
                var secondLicense = second.WriteSink.Receipts.Single(item => item.Name == "API_LICENSE_SIGNED_PAYLOAD");
                AssertEx.False(firstLicense.ContentSha256 == secondLicense.ContentSha256);
                AssertEx.False(first.WhatIf.Requests[1].ReceiptIdentitySha256s[0] == second.WhatIf.Requests[1].ReceiptIdentitySha256s[0]);
                AssertEx.False(firstResult.FinalInput!.InputSha256 == secondResult.FinalInput!.InputSha256);
                AssertEx.False(firstResult.EvidenceSha256 == secondResult.EvidenceSha256);
                AssertEx.True(first.LicenseTransport.ReturnedBuffer!.All(value => value == 0));
                AssertEx.True(second.LicenseTransport.ReturnedBuffer!.All(value => value == 0));
            }
            finally { first.Dispose(); second.Dispose(); }
        });

        Run("receipt.content-digest-denial-matrix", () =>
        {
            foreach (var fault in new[]
            {
                RuntimeBridgeReceiptDigestFault.Missing,
                RuntimeBridgeReceiptDigestFault.Uppercase,
                RuntimeBridgeReceiptDigestFault.Stale,
                RuntimeBridgeReceiptDigestFault.CrossPair
            })
            {
                var harness = new RuntimeBridgeTestHarness(receiptDigestFault: fault);
                try
                {
                    var result = harness.Bridge.RunAsync(harness.Invocation()).GetAwaiter().GetResult();
                    AssertEx.Equal("failed", result.Status, fault + ":" + result.EvidenceJson);
                    AssertEx.Equal(1, harness.WriteSink.CallCount, fault.ToString());
                    AssertEx.Equal(0, harness.CursorGenerator.CallCount, fault.ToString());
                    AssertEx.Equal(1, harness.WhatIf.CallCount, fault.ToString());
                    AssertEx.Equal(1, harness.Approval.CallCount, fault.ToString());
                    AssertEx.Equal(0, harness.Handler.CallCount, fault.ToString());
                }
                finally { harness.Dispose(); }
            }
        });

        Run("evidence.recovered-failure-excludes-opaque-receipt-ids", () =>
        {
            var harness = new RuntimeBridgeTestHarness(RuntimeBridgeTestFailure.SecondWhatIf, volatileIdentity: "RECOVERED");
            try
            {
                var result = harness.Bridge.RunAsync(harness.Invocation()).GetAwaiter().GetResult();
                AssertEx.Equal("failed", result.Status, result.EvidenceJson);
                AssertEx.Equal(2, result.RecoveryCount);
                AssertEx.Equal(0, result.OwnedReceipts.Count);
                AssertNoOpaqueRecoveryHandles(result, harness.WriteSink.ReceiptIds);
            }
            finally { harness.Dispose(); }
        });

        Run("evidence.recovery-required-excludes-opaque-receipt-ids", () =>
        {
            var harness = new RuntimeBridgeTestHarness(
                RuntimeBridgeTestFailure.SecondWhatIf | RuntimeBridgeTestFailure.CursorRecoveryAmbiguous,
                volatileIdentity: "UNRECOVERED");
            try
            {
                var result = harness.Bridge.RunAsync(harness.Invocation()).GetAwaiter().GetResult();
                AssertEx.Equal("recovery-required", result.Status, result.EvidenceJson);
                AssertEx.Equal(1, result.OwnedReceipts.Count);
                AssertNoOpaqueRecoveryHandles(result, harness.WriteSink.ReceiptIds);
            }
            finally { harness.Dispose(); }
        });

        AssertEx.True(executed.SequenceEqual(expected, StringComparer.Ordinal),
            "The protected-content evidence case ledger must execute every named row exactly once and in authority order.");
        return;

        void Run(string name, Action action)
        {
            AssertEx.False(executed.Contains(name, StringComparer.Ordinal), $"Duplicate executed case: {name}");
            action();
            executed.Add(name);
        }
    }

    private static void ExecutesTheClosedHttpProtocolMatrix()
    {
        var executed = new List<string>();
        var expected = (
            from operation in Enum.GetValues<RuntimeBridgeHttpOperation>()
            from fault in Enum.GetValues<RuntimeBridgeHttpFault>()
            select $"http.{operation}.{fault}").ToArray();

        foreach (var operation in Enum.GetValues<RuntimeBridgeHttpOperation>())
        {
            foreach (var fault in Enum.GetValues<RuntimeBridgeHttpFault>())
            {
                var name = $"http.{operation}.{fault}";
                AssertEx.False(executed.Contains(name, StringComparer.Ordinal), $"Duplicate executed HTTP case: {name}");
                var harness = new RuntimeBridgeTestHarness(
                    portableStageMutation: PortableOwnedStageMutation.None,
                    httpMutation: new(operation, fault));
                try
                {
                    var result = harness.Bridge.RunAsync(harness.Invocation()).GetAwaiter().GetResult();
                    AssertEx.Equal("failed", result.Status, $"{name}: {result.EvidenceJson}");
                    AssertEx.Equal("runtime_deployment_recovery_rehearsal_failed", result.SafeCode, name);
                    AssertEx.False(result.AuthorizesDeployment, name);
                    AssertEx.Equal(1, harness.ArtifactTransport.SessionCount, name);
                    AssertEx.Equal(ExpectedArtifactAcquisitions(operation), harness.ArtifactTransport.AcquireCount, name);
                    AssertEx.Equal(operation >= RuntimeBridgeHttpOperation.Receipt ? 1 : 0, harness.ArtifactTransport.ReceiptCount, name);
                    AssertEx.Equal(operation == RuntimeBridgeHttpOperation.Protected ? 1 : 0, harness.LicenseTransport.CallCount, name);
                    AssertEx.Equal(0, harness.WriteSink.CallCount, name);
                    AssertEx.Equal(0, harness.Handler.CallCount, name);
                    AssertEx.Equal(0, result.OwnedReceipts.Count, name);
                    AssertEx.True(result.StageCleaned, name);
                    AssertNoOpaqueRecoveryHandles(result, harness.WriteSink.ReceiptIds);
                    executed.Add(name);
                }
                finally { harness.Dispose(); }
            }
        }

        AssertEx.True(executed.SequenceEqual(expected, StringComparer.Ordinal),
            "The closed HTTP protocol matrix must execute each named operation/fault row exactly once and in authority order.");

        static int ExpectedArtifactAcquisitions(RuntimeBridgeHttpOperation operation) => operation switch
        {
            RuntimeBridgeHttpOperation.Session => 0,
            RuntimeBridgeHttpOperation.ArtifactFull => 1,
            RuntimeBridgeHttpOperation.ArtifactRange => 2,
            RuntimeBridgeHttpOperation.Receipt or RuntimeBridgeHttpOperation.Protected => 29,
            _ => throw new InvalidOperationException("Unknown HTTP operation.")
        };
    }

    private static byte[] CursorEntropy(byte value) => Enumerable.Repeat(value, 32).Select(item => (byte)item).ToArray();

    private static void AssertNoOpaqueRecoveryHandles(RuntimeBridgeResult result, IEnumerable<string> receiptIds)
    {
        AssertEx.False(result.EvidenceJson.Contains("recoveryReceiptIds", StringComparison.Ordinal));
        AssertEx.False(result.EvidenceJson.Contains("\"receiptId\"", StringComparison.OrdinalIgnoreCase));
        foreach (var receiptId in receiptIds)
        {
            AssertEx.False(result.EvidenceJson.Contains(receiptId, StringComparison.Ordinal));
            AssertEx.False(result.EvidenceSha256.Contains(receiptId, StringComparison.Ordinal));
        }
    }

    private static void RejectsEveryOwnedArtifactProtocolMutationBeforeProtectedEffects()
    {
        var mutations = new[]
        {
            RuntimeBridgeTestFailure.ArtifactFullStatus,
            RuntimeBridgeTestFailure.ArtifactFullVector,
            RuntimeBridgeTestFailure.ArtifactFullReference,
            RuntimeBridgeTestFailure.ArtifactFullPackage,
            RuntimeBridgeTestFailure.ArtifactFullSession,
            RuntimeBridgeTestFailure.ArtifactFullEtag,
            RuntimeBridgeTestFailure.ArtifactFullAcceptRanges,
            RuntimeBridgeTestFailure.ArtifactFullContentRange,
            RuntimeBridgeTestFailure.ArtifactFullContentLength,
            RuntimeBridgeTestFailure.ArtifactFullBodyFile,
            RuntimeBridgeTestFailure.ArtifactFullHeaders,
            RuntimeBridgeTestFailure.ArtifactFullRedirect,
            RuntimeBridgeTestFailure.ArtifactFullBody,
            RuntimeBridgeTestFailure.ArtifactFullKind,
            RuntimeBridgeTestFailure.ArtifactFullTotalLength,
            RuntimeBridgeTestFailure.ArtifactFullOffset,
            RuntimeBridgeTestFailure.ArtifactFullSha,
            RuntimeBridgeTestFailure.ArtifactRangeStatus,
            RuntimeBridgeTestFailure.ArtifactRangeVector,
            RuntimeBridgeTestFailure.ArtifactRangeReference,
            RuntimeBridgeTestFailure.ArtifactRangePackage,
            RuntimeBridgeTestFailure.ArtifactRangeSession,
            RuntimeBridgeTestFailure.ArtifactRangeEtag,
            RuntimeBridgeTestFailure.ArtifactRangeAcceptRanges,
            RuntimeBridgeTestFailure.ArtifactRangeContentRange,
            RuntimeBridgeTestFailure.ArtifactRangeContentLength,
            RuntimeBridgeTestFailure.ArtifactRangeBodyFile,
            RuntimeBridgeTestFailure.ArtifactRangeHeaders,
            RuntimeBridgeTestFailure.ArtifactRangeRedirect,
            RuntimeBridgeTestFailure.ArtifactRangeBody,
            RuntimeBridgeTestFailure.ArtifactRangeShape
        };
        foreach (var mutation in mutations)
        {
            var harness = new RuntimeBridgeTestHarness(mutation);
            try
            {
                var result = harness.Bridge.RunAsync(harness.Invocation()).GetAwaiter().GetResult();
                AssertEx.Equal("failed", result.Status, $"{mutation}: {result.EvidenceJson}");
                AssertEx.Equal(0, harness.LicenseTransport.CallCount, mutation.ToString());
                AssertEx.Equal(0, harness.WriteSink.CallCount, mutation.ToString());
                AssertEx.Equal(0, harness.Handler.CallCount, mutation.ToString());
                AssertEx.Equal(mutation.ToString().StartsWith("ArtifactFull", StringComparison.Ordinal) ? 1 : 2,
                    harness.ArtifactTransport.AcquireCount, mutation.ToString());
                AssertEx.True(result.StageCleaned, mutation.ToString());
            }
            finally { harness.Dispose(); }
        }
    }

    private static void PinsTheCompleteLicenseAuthorityAndSignedIdentity()
    {
        var mutations = new[]
        {
            RuntimeBridgeTestFailure.LicenseAlgorithm,
            RuntimeBridgeTestFailure.LicenseKeyId,
            RuntimeBridgeTestFailure.LicensePublicKeyDigest,
            RuntimeBridgeTestFailure.LicenseCanonicalization,
            RuntimeBridgeTestFailure.LicensePayloadDigest,
            RuntimeBridgeTestFailure.LicenseFingerprint,
            RuntimeBridgeTestFailure.LicenseFingerprintDomain,
            RuntimeBridgeTestFailure.LicenseSignature,
            RuntimeBridgeTestFailure.LicenseSubscription
        };
        foreach (var mutation in mutations)
        {
            var harness = new RuntimeBridgeTestHarness(mutation);
            try
            {
                var result = harness.Bridge.RunAsync(harness.Invocation()).GetAwaiter().GetResult();
                AssertEx.Equal("failed", result.Status, $"{mutation}: {result.EvidenceJson}");
                var trustGateMutation = mutation is RuntimeBridgeTestFailure.LicenseAlgorithm or
                    RuntimeBridgeTestFailure.LicenseKeyId or RuntimeBridgeTestFailure.LicensePublicKeyDigest;
                AssertEx.Equal(trustGateMutation ? 0 : 1, harness.LicenseTransport.CallCount, mutation.ToString());
                AssertEx.Equal(trustGateMutation ? 0 : 1, harness.ArtifactTransport.SessionCount, mutation.ToString());
                AssertEx.Equal(0, harness.WriteSink.CallCount, mutation.ToString());
                AssertEx.Equal(0, harness.Handler.CallCount, mutation.ToString());
            }
            finally { harness.Dispose(); }
        }
    }

    private static void RejectsEveryHandlerResultMutationAndBindsItsDigestIntoEvidence()
    {
        var mutations = new[]
        {
            RuntimeBridgeTestFailure.HandlerPackage,
            RuntimeBridgeTestFailure.HandlerInput,
            RuntimeBridgeTestFailure.HandlerPreview,
            RuntimeBridgeTestFailure.HandlerApproval,
            RuntimeBridgeTestFailure.HandlerArtifact,
            RuntimeBridgeTestFailure.HandlerAuthorizes,
            RuntimeBridgeTestFailure.HandlerResourceCount,
            RuntimeBridgeTestFailure.HandlerWriteCount,
            RuntimeBridgeTestFailure.HandlerDeploymentCount,
            RuntimeBridgeTestFailure.HandlerDigest
        };
        foreach (var mutation in mutations)
        {
            var harness = new RuntimeBridgeTestHarness(mutation);
            try
            {
                var result = harness.Bridge.RunAsync(harness.Invocation()).GetAwaiter().GetResult();
                AssertEx.Equal("failed", result.Status, $"{mutation}: {result.EvidenceJson}");
                AssertEx.Equal(1, harness.Handler.CallCount, mutation.ToString());
                AssertEx.Equal(2, result.RecoveryCount, mutation.ToString());
                AssertEx.Equal(0, result.OwnedReceipts.Count, mutation.ToString());
            }
            finally { harness.Dispose(); }
        }
    }

    private static void RejectsUnsafeOwnedStageMutationsWithoutDeletingForeignContent()
    {
        foreach (var mutation in new[] { PortableOwnedStageMutation.MarkerRemoval, PortableOwnedStageMutation.UnexpectedInventory })
        {
            var harness = new RuntimeBridgeTestHarness(portableStageMutation: mutation);
            try
            {
                var result = harness.Bridge.RunAsync(harness.Invocation()).GetAwaiter().GetResult();
                AssertEx.Equal("cleanup-required", result.Status, $"{mutation}: {result.EvidenceJson}");
                AssertEx.False(result.StageCleaned);
                AssertEx.Equal(0, harness.LicenseTransport.CallCount);
                AssertEx.Equal(0, harness.WriteSink.CallCount);
                AssertEx.Equal(0, harness.PortableStageStore!.DeleteCount);
                if (mutation == PortableOwnedStageMutation.UnexpectedInventory)
                    AssertEx.True(harness.PortableStageStore.UnownedPaths.Contains("foreign.txt"));
            }
            finally { harness.Dispose(); }
        }
    }

    private static void PortableInjectedStageStoreProvesClosedOwnershipSemantics()
    {
        var expectedMutations = new[]
        {
            nameof(PortableOwnedStageMutation.None),
            nameof(PortableOwnedStageMutation.Unsupported),
            nameof(PortableOwnedStageMutation.RootIdentitySubstitution),
            nameof(PortableOwnedStageMutation.StageIdentitySubstitution),
            nameof(PortableOwnedStageMutation.MarkerRemoval),
            nameof(PortableOwnedStageMutation.MarkerSubstitution),
            nameof(PortableOwnedStageMutation.RootReparse),
            nameof(PortableOwnedStageMutation.StageLink),
            nameof(PortableOwnedStageMutation.ComponentReparse),
            nameof(PortableOwnedStageMutation.ComponentLink),
            nameof(PortableOwnedStageMutation.FileHardlink),
            nameof(PortableOwnedStageMutation.ComponentIdentitySubstitution),
            nameof(PortableOwnedStageMutation.UnexpectedInventory),
            nameof(PortableOwnedStageMutation.ExistingTargetCollision),
            nameof(PortableOwnedStageMutation.CleanupMarkerSubstitution),
            nameof(PortableOwnedStageMutation.CleanupUnexpectedInventory),
            nameof(PortableOwnedStageMutation.CleanupStageIdentitySubstitution)
        };
        AssertEx.True(Enum.GetNames<PortableOwnedStageMutation>().SequenceEqual(expectedMutations));

        var positive = new RuntimeBridgeTestHarness(portableStageMutation: PortableOwnedStageMutation.None);
        try
        {
            var result = positive.Bridge.RunAsync(positive.Invocation()).GetAwaiter().GetResult();
            AssertEx.Equal("simulated", result.Status, result.EvidenceJson);
            AssertEx.True(result.StageCleaned);
            AssertEx.True(positive.PortableStageStore!.DeleteCount > 0);
            AssertEx.Equal(0, Directory.GetDirectories(positive.WorkspaceRoot).Length);
        }
        finally { positive.Dispose(); }

        foreach (var mutation in Enum.GetValues<PortableOwnedStageMutation>().Where(value => value is not PortableOwnedStageMutation.None))
        {
            var harness = new RuntimeBridgeTestHarness(portableStageMutation: mutation);
            try
            {
                var result = harness.Bridge.RunAsync(harness.Invocation()).GetAwaiter().GetResult();
                AssertEx.Equal(mutation == PortableOwnedStageMutation.Unsupported ? "failed" : "cleanup-required", result.Status,
                    $"{mutation}: {result.EvidenceJson}");
                AssertEx.Equal(0, harness.PortableStageStore!.DeleteCount, mutation.ToString());
                AssertEx.Equal(1, harness.PortableStageStore.CreateCount, mutation.ToString());
                if (mutation is PortableOwnedStageMutation.CleanupMarkerSubstitution or
                    PortableOwnedStageMutation.CleanupUnexpectedInventory or PortableOwnedStageMutation.CleanupStageIdentitySubstitution)
                {
                    AssertEx.Equal(1, harness.Handler.CallCount, mutation.ToString());
                    AssertEx.Equal(1, harness.LicenseTransport.CallCount, mutation.ToString());
                }
                else
                {
                    AssertEx.Equal(0, harness.Handler.CallCount, mutation.ToString());
                    AssertEx.Equal(0, harness.LicenseTransport.CallCount, mutation.ToString());
                }
                if (mutation is PortableOwnedStageMutation.UnexpectedInventory or PortableOwnedStageMutation.ExistingTargetCollision or
                    PortableOwnedStageMutation.CleanupUnexpectedInventory)
                    AssertEx.Equal(1, harness.PortableStageStore.UnownedPaths.Count, mutation.ToString());
            }
            finally { harness.Dispose(); }
        }

        ProvesPortableStoreDirectLeasePathAndCleanupRules();
    }

    private static void NativeWindowsStageDeniesCreateRegistrationAndCleanupSubstitutionRaces()
    {
        if (!OperatingSystem.IsWindows()) return;
        var harness = new RuntimeBridgeTestHarness(probeNativeStageRaces: true);
        try
        {
            var result = harness.Bridge.RunAsync(harness.Invocation()).GetAwaiter().GetResult();
            AssertEx.Equal("simulated", result.Status, result.EvidenceJson);
            AssertEx.True(result.StageCleaned);
            AssertEx.Equal(1, harness.NativeStageRaceProbe!.ProbeCount);
            AssertEx.Equal(harness.NativeStageRaceProbe.ProbeCount, harness.NativeStageRaceProbe.DeniedCount,
                string.Join(',', harness.NativeStageRaceProbe.UnexpectedOperations));
            AssertEx.Equal(0, harness.NativeStageRaceProbe.UnexpectedSuccessCount, string.Join(',', harness.NativeStageRaceProbe.UnexpectedOperations));
            AssertEx.Equal(0, Directory.GetDirectories(harness.WorkspaceRoot).Length);
        }
        finally { harness.Dispose(); }
    }

    private static void ProvesPortableStoreDirectLeasePathAndCleanupRules()
    {
        var capability = RuntimeBridgeSyntheticTestCapability.CreateForTestSupport();
        var store = new RuntimeBridgeTestHarness.TestOwnedStageStore(capability, PortableOwnedStageMutation.None);
        var lease = store.Create("portable-root", "inv_PORTABLE_DIRECT_0001",
        [
            new("api.zip", false),
            new("api", true),
            new("portal", true),
            new("portal/.pm365", true),
            new("portal/.pm365/provenance.json", false)
        ]);
        store.AssertOwned(lease);
        foreach (var unsafePath in new[] { "", " ", "/rooted", "C:/rooted", "../escape", "a/../escape", "a/./file", "a//file", "a/ /file", "a\\file", "a:stream", "a/\0file" })
            AssertEx.Throws<InvalidDataException>(() => store.WriteFileExclusive(lease, unsafePath, [0x01]));

        store.WriteFileExclusive(lease, "api.zip", [0x01, 0x02]);
        AssertEx.Throws<IOException>(() => store.WriteFileExclusive(lease, "api.zip", [0x03]));
        store.CreateDirectoryExclusive(lease, "api");
        AssertEx.Throws<IOException>(() => store.CreateDirectoryExclusive(lease, "api"));
        AssertEx.Throws<IOException>(() => store.WriteFileExclusive(lease, "api", [0x04]));
        store.WriteFileExclusive(lease, "portal/.pm365/provenance.json", [0x05]);

        var wrongInvocation = lease with { InvocationId = "inv_PORTABLE_DIRECT_0002" };
        var wrongRoot = lease with { TrustedRoot = "different-root" };
        var wrongStage = lease with { StageRoot = "different-stage" };
        var wrongMarkerBytes = lease.OwnershipMarker.ToArray(); wrongMarkerBytes[0] ^= 0xFF;
        var wrongMarker = lease with { OwnershipMarker = wrongMarkerBytes };
        AssertEx.Throws<InvalidDataException>(() => store.AssertOwned(wrongInvocation));
        AssertEx.Throws<InvalidDataException>(() => store.AssertOwned(wrongRoot));
        AssertEx.Throws<InvalidDataException>(() => store.AssertOwned(wrongStage));
        AssertEx.Throws<InvalidDataException>(() => store.AssertOwned(wrongMarker));
        AssertEx.True(store.Cleanup(lease));
        AssertEx.True(lease.OwnershipMarker.All(value => value == 0));
        AssertEx.False(store.Cleanup(lease));

        var noDeleteStore = new RuntimeBridgeTestHarness.TestOwnedStageStore(capability, PortableOwnedStageMutation.CleanupUnexpectedInventory);
        var noDeleteLease = noDeleteStore.Create("portable-root", "inv_PORTABLE_DIRECT_0003", [new("owned.txt", false)]);
        noDeleteStore.WriteFileExclusive(noDeleteLease, "owned.txt", [0x06]);
        AssertEx.False(noDeleteStore.Cleanup(noDeleteLease));
        AssertEx.Equal(0, noDeleteStore.DeleteCount);
        AssertEx.True(noDeleteStore.UnownedPaths.Contains("foreign-cleanup.txt"));
        AssertEx.True(noDeleteLease.OwnershipMarker.Any(value => value != 0));

        var unsupported = new RuntimeBridgeTestHarness.TestOwnedStageStore(capability, PortableOwnedStageMutation.Unsupported);
        AssertEx.Throws<PlatformNotSupportedException>(() => unsupported.Create("portable-root", "inv_PORTABLE_DIRECT_0004", [new("owned.txt", false)]));
    }

    private static void ProvesCursorCopiesAreNotIntroducedAtTheCallbackBoundary()
    {
        var root = FindRepositoryRoot();
        var service = File.ReadAllText(Path.Combine(root, "src", "PageMaker365.Installer.Engine", "Services", "RuntimeDeploymentRecoveryBridge.cs"));
        var support = File.ReadAllText(Path.Combine(root, "tests", "PageMaker365.Installer.Engine.Tests", "RuntimeDeploymentRecoveryBridgeTestSupport.cs"));
        AssertEx.False(service.Contains("base64UrlSecretUtf8.ToArray()", StringComparison.Ordinal));
        AssertEx.False(support.Contains("request.ValueUtf8.ToArray()", StringComparison.Ordinal));
        AssertEx.True(service.Contains("Sha256(base64UrlSecretUtf8.Span)", StringComparison.Ordinal));
        AssertEx.True(support.Contains("Sha256(request.ValueUtf8.Span)", StringComparison.Ordinal));
    }

    private static void KeepsTheBoundaryInternalOfflineAndNonDeploying()
    {
        var root = FindRepositoryRoot();
        var sources = new[]
        {
            Path.Combine(root, "src", "PageMaker365.Installer.Engine", "Models", "RuntimeDeploymentRecoveryBridgeContracts.cs"),
            Path.Combine(root, "src", "PageMaker365.Installer.Engine", "Services", "RuntimeDeploymentRecoveryBridge.cs")
        };
        foreach (var source in sources.Select(File.ReadAllText))
        {
            foreach (var forbidden in new[] { "HttpClient", "Process.Start", "PowerShell", "Invoke-PM365", "New-Az", "Set-Az", "Get-Az", "InstallerEngine", "InstallerStateStore" })
                AssertEx.False(source.Contains(forbidden, StringComparison.Ordinal), $"Synthetic bridge contains forbidden production reach: {forbidden}");
        }
        AssertEx.Equal(0, typeof(InstallerEngine).GetMethods().Count(method => method.Name.Contains("RecoveryBridge", StringComparison.OrdinalIgnoreCase)));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null) { if (File.Exists(Path.Combine(directory.FullName, "PageMaker365.Installer.sln"))) return directory.FullName; directory = directory.Parent; }
        throw new DirectoryNotFoundException();
    }
}
