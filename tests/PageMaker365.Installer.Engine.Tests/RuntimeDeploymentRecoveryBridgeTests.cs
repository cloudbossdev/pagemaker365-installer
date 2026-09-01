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
        foreach (var attack in Enum.GetValues<RuntimeBridgeNativeStageAttack>())
        {
            var harness = new RuntimeBridgeTestHarness(nativeStageAttack: attack);
            try
            {
                var result = harness.Bridge.RunAsync(harness.Invocation()).GetAwaiter().GetResult();
                var probe = harness.NativeStageRaceProbe!;
                AssertEx.True(probe.AttackApplied, attack.ToString());
                AssertEx.Equal(1, probe.ProbeCount, attack.ToString());
                AssertEx.Equal(1, probe.DeniedCount + probe.UnexpectedSuccessCount, attack.ToString());
                if (probe.UnexpectedSuccessCount == 0)
                {
                    AssertEx.Equal("simulated", result.Status, attack.ToString());
                    AssertEx.True(result.StageCleaned, attack.ToString());
                }
                else
                {
                    var beforeLeaseCommit = attack is RuntimeBridgeNativeStageAttack.TrustedRootBeforeBind or
                        RuntimeBridgeNativeStageAttack.ParentBeforeStageCreate or RuntimeBridgeNativeStageAttack.ParentAfterValidationBeforeStageCreate or
                        RuntimeBridgeNativeStageAttack.StageBeforeBind;
                    AssertEx.Equal(beforeLeaseCommit ? "failed" : "cleanup-required", result.Status, attack.ToString());
                    AssertEx.Equal(beforeLeaseCommit, result.StageCleaned, attack.ToString());
                    AssertEx.True(probe.ForeignPath is not null && (File.Exists(probe.ForeignPath) || Directory.Exists(probe.ForeignPath)), attack.ToString());
                }
                AssertEx.False(result.AuthorizesDeployment, attack.ToString());
            }
            finally
            {
                var foreign = harness.NativeStageRaceProbe?.ForeignPath;
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

    private static void NativeWindowsCancellationTableCoversEveryCreateBindAndCleanupBoundary()
    {
        if (!OperatingSystem.IsWindows()) return;
        var baseline = new RuntimeBridgeTestHarness(cancellationCheckpoint: "never");
        string[] checkpoints;
        try
        {
            var result = baseline.Bridge.RunAsync(baseline.Invocation(), baseline.BoundaryCancellation!.Token).GetAwaiter().GetResult();
            AssertEx.Equal("simulated", result.Status);
            checkpoints = baseline.CancellationProbe!.Phases.Where(item => item.StartsWith("native.", StringComparison.Ordinal)).ToArray();
            var expected = ExpectedNativeCancellationCheckpoints();
            var mismatch = Enumerable.Range(0, Math.Min(expected.Length, checkpoints.Length))
                .FirstOrDefault(index => expected[index] != checkpoints[index], -1);
            AssertEx.True(expected.SequenceEqual(checkpoints, StringComparer.Ordinal),
                $"Expected {expected.Length} native checkpoints; observed {checkpoints.Length}; mismatch {mismatch}: " +
                (mismatch >= 0 ? $"{expected[mismatch]} != {checkpoints[mismatch]}" : "length-only"));
            AssertEx.Equal(checkpoints.Length, checkpoints.Distinct(StringComparer.Ordinal).Count());
        }
        finally { baseline.Dispose(); baseline.BoundaryCancellation?.Dispose(); }

        foreach (var checkpoint in checkpoints)
        {
            var harness = new RuntimeBridgeTestHarness(cancellationCheckpoint: checkpoint);
            try
            {
                var result = harness.Bridge.RunAsync(harness.Invocation(), harness.BoundaryCancellation!.Token).GetAwaiter().GetResult();
                var cleanupBoundary = checkpoint.StartsWith("native.cleanup.", StringComparison.Ordinal);
                AssertEx.Equal(cleanupBoundary ? "cleanup-required" : "failed", result.Status, checkpoint);
                AssertEx.Equal(!cleanupBoundary, result.StageCleaned, checkpoint);
                AssertEx.False(result.AuthorizesDeployment, checkpoint);
                AssertEx.Equal(1, harness.ArtifactTransport.SessionCount, checkpoint);
                AssertEx.Equal(29, harness.ArtifactTransport.AcquireCount, checkpoint);
                AssertEx.Equal(1, harness.ArtifactTransport.ReceiptCount, checkpoint);
                AssertEx.Equal(cleanupBoundary ? 1 : 0, harness.LicenseTransport.CallCount, checkpoint);
                AssertEx.Equal(cleanupBoundary ? 2 : 0, harness.WriteSink.CallCount, checkpoint);
                AssertEx.Equal(cleanupBoundary ? 1 : 0, harness.CursorGenerator.CallCount, checkpoint);
                AssertEx.Equal(cleanupBoundary ? 2 : 0, harness.WhatIf.CallCount, checkpoint);
                AssertEx.Equal(cleanupBoundary ? 2 : 0, harness.Approval.CallCount, checkpoint);
                AssertEx.Equal(cleanupBoundary ? 1 : 0, harness.Handler.CallCount, checkpoint);
                AssertEx.Equal(0, harness.Recovery.CallCount, checkpoint);
            }
            finally { harness.Dispose(); harness.BoundaryCancellation?.Dispose(); }
        }
    }

    private static string[] ExpectedNativeCancellationCheckpoints()
    {
        var labels = new List<string> { "native.root.open", "native.parent.validate", "native.stage.create", "native.stage.bind",
            "native.marker.create", "native.marker.bind", "native.file.pagemaker365-api-1.4.3.zip.create", "native.file.pagemaker365-api-1.4.3.zip.bind",
            "native.directory.api.create", "native.directory.api.bind" };
        var root = FindRepositoryRoot();
        var fixture = Path.Combine(root, "tests", "PageMaker365.Installer.Engine.Tests", "Fixtures", "private-runtime-v07-cross-repository-rehearsal-v1");
        foreach (var kind in new[] { "api", "portal" })
        {
            if (kind == "portal")
            {
                labels.Add("native.file.pagemaker365-portal-1.4.3.zip.create"); labels.Add("native.file.pagemaker365-portal-1.4.3.zip.bind");
                labels.Add("native.directory.portal.create"); labels.Add("native.directory.portal.bind");
            }
            using var archive = ZipFile.OpenRead(Path.Combine(fixture, "artifacts", kind + ".zip"));
            var createdDirectories = new HashSet<string>(StringComparer.Ordinal) { kind };
            foreach (var entry in archive.Entries)
            {
                var parts = entry.FullName.Split('/');
                for (var index = 1; index < parts.Length; index++)
                {
                    var directory = kind + "." + string.Join('.', parts.Take(index));
                    if (!createdDirectories.Add(directory)) continue;
                    labels.Add($"native.directory.{directory}.create");
                    labels.Add($"native.directory.{directory}.bind");
                }
                var file = kind + "." + entry.FullName.Replace('/', '.');
                labels.Add($"native.file.{file}.create");
                labels.Add($"native.file.{file}.bind");
            }
        }
        labels.AddRange(["native.inventory.enumerate-complete", "native.cleanup.delete", "native.cleanup.commit"]);
        return labels.SelectMany(label => new[] { label + ":before", label + ":after" }).ToArray();
    }

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
        var baseline = new RuntimeBridgeTestHarness(portableStageMutation: PortableOwnedStageMutation.None,
            cancellationCheckpoint: "never");
        string[] checkpoints;
        try
        {
            var result = baseline.Bridge.RunAsync(baseline.Invocation(), baseline.BoundaryCancellation!.Token).GetAwaiter().GetResult();
            AssertEx.Equal("simulated", result.Status, result.EvidenceJson);
            checkpoints = baseline.CancellationProbe!.Phases.ToArray();
            var expected = ExpectedCancellationCheckpoints();
            AssertEx.True(expected.SequenceEqual(checkpoints, StringComparer.Ordinal),
                $"Expected {expected.Length} closed checkpoints; observed {checkpoints.Length}.");
            AssertEx.Equal(checkpoints.Length, checkpoints.Distinct(StringComparer.Ordinal).Count());
            AssertEx.True(checkpoints.Select((checkpoint, index) => (checkpoint, index)).All(item =>
                item.checkpoint.EndsWith(item.index % 2 == 0 ? ":before" : ":after", StringComparison.Ordinal)));
        }
        finally { baseline.Dispose(); baseline.BoundaryCancellation?.Dispose(); }

        foreach (var checkpoint in checkpoints)
        {
            var harness = new RuntimeBridgeTestHarness(portableStageMutation: PortableOwnedStageMutation.None,
                cancellationCheckpoint: checkpoint);
            try
            {
                var result = harness.Bridge.RunAsync(harness.Invocation(), harness.BoundaryCancellation!.Token).GetAwaiter().GetResult();
                var position = Array.IndexOf(checkpoints, checkpoint);
                bool Completed(string label) => Array.IndexOf(checkpoints, label + ":after") <= position &&
                    Array.IndexOf(checkpoints, label + ":after") >= 0;
                var expectedAcquires = checkpoints.Take(position + 1).Count(item => item.StartsWith("artifact.", StringComparison.Ordinal) && item.EndsWith(".exchange:after", StringComparison.Ordinal));
                var expectedWrites = (Completed("protected.license.write") ? 1 : 0) + (Completed("protected.cursor.generate-write") ? 1 : 0);
                var simulationAccepted = Array.IndexOf(checkpoints, "handler.result-digest.validate:before") <= position;
                var stageCreated = Completed("stage.create");
                var expectedCleanup = !stageCreated ? 0 : Completed("stage.cleanup") ? 2 : 1;
                var expectedRecoveries = simulationAccepted ? 0 : expectedWrites;
                AssertEx.False(result.Status == "simulated", checkpoint);
                AssertEx.False(result.AuthorizesDeployment, checkpoint);
                AssertEx.Equal(Completed("session.exchange") ? 1 : 0, harness.ArtifactTransport.SessionCount, checkpoint);
                AssertEx.Equal(expectedAcquires, harness.ArtifactTransport.AcquireCount, checkpoint);
                AssertEx.Equal(Completed("receipt.exchange") ? 1 : 0, harness.ArtifactTransport.ReceiptCount, checkpoint);
                AssertEx.Equal(Completed("protected.license.exchange") ? 1 : 0, harness.LicenseTransport.CallCount, checkpoint);
                AssertEx.Equal(expectedWrites, harness.WriteSink.CallCount, checkpoint);
                AssertEx.Equal(Completed("protected.cursor.generate-write") ? 1 : 0, harness.CursorGenerator.CallCount, checkpoint);
                AssertEx.Equal((Completed("whatif.provisional") ? 1 : 0) + (Completed("whatif.final") ? 1 : 0), harness.WhatIf.CallCount, checkpoint);
                AssertEx.Equal((Completed("approval.provisional") ? 1 : 0) + (Completed("approval.final") ? 1 : 0), harness.Approval.CallCount, checkpoint);
                AssertEx.Equal(Completed("handler.invoke") ? 1 : 0, harness.Handler.CallCount, checkpoint);
                AssertEx.Equal(expectedRecoveries, harness.Recovery.CallCount, checkpoint);
                AssertEx.Equal(expectedCleanup, harness.PortableStageStore!.CleanupCount, checkpoint);
                AssertEx.True(harness.WriteSink.RetainedBuffers.All(buffer => buffer.Span.ToArray().All(value => value == 0)), checkpoint);
            }
            finally { harness.Dispose(); harness.BoundaryCancellation?.Dispose(); }
        }
    }

    private static string[] ExpectedCancellationCheckpoints()
    {
        var labels = new List<string> { "package.parse", "package.trust.validate", "input.provisional", "session.exchange", "session.response.validate" };
        labels.Add("artifact.api.full.exchange");
        labels.Add("artifact.api.full.header-digest.validate");
        foreach (var index in Enumerable.Range(0, 12))
        {
            labels.Add($"artifact.api.range.{index:D2}.exchange");
            labels.Add($"artifact.api.range.{index:D2}.header-digest.validate");
        }
        labels.AddRange(["artifact.api.range.reconstruction.validate", "artifact.api.zip.validate", "artifact.api.provenance.validate"]);
        labels.Add("artifact.portal.full.exchange");
        labels.Add("artifact.portal.full.header-digest.validate");
        foreach (var index in Enumerable.Range(0, 15))
        {
            labels.Add($"artifact.portal.range.{index:D2}.exchange");
            labels.Add($"artifact.portal.range.{index:D2}.header-digest.validate");
        }
        labels.AddRange(["artifact.portal.range.reconstruction.validate", "artifact.portal.zip.validate", "artifact.portal.provenance.validate",
            "receipt.exchange", "receipt.envelope.validate", "receipt.request.validate", "receipt.response.validate", "stage.create"]);
        var root = FindRepositoryRoot();
        var fixture = Path.Combine(root, "tests", "PageMaker365.Installer.Engine.Tests", "Fixtures", "private-runtime-v07-cross-repository-rehearsal-v1");
        foreach (var kind in new[] { "api", "portal" })
        {
            labels.Add($"stage.{kind}.assert");
            labels.Add($"stage.{kind}.archive.write");
            labels.Add($"stage.{kind}.directory.create");
            using var archive = ZipFile.OpenRead(Path.Combine(fixture, "artifacts", kind + ".zip"));
            labels.AddRange(archive.Entries.Select(entry => $"stage.{kind}/{entry.FullName}.write"));
        }
        labels.AddRange(["stage.inventory.complete", "whatif.provisional", "whatif.provisional.validate", "approval.provisional",
            "approval.provisional.validate", "approval.provisional.consume", "protected.license.exchange", "protected.license.response.validate",
            "protected.license.signature-fingerprint.validate", "protected.license.write", "protected.license.receipt-content.validate",
            "protected.cursor.generate-write", "input.finalize", "input.final-shape.validate", "whatif.final", "whatif.final.validate",
            "approval.final", "approval.final.validate", "approval.final.consume", "handler.invoke", "handler.result-digest.validate",
            "stage.cleanup", "evidence.commit"]);
        return labels.SelectMany(label => new[] { label + ":before", label + ":after" }).ToArray();
    }

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
