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
        RejectsEveryOwnedArtifactProtocolMutationBeforeProtectedEffects();
        PinsTheCompleteLicenseAuthorityAndSignedIdentity();
        RejectsEveryHandlerResultMutationAndBindsItsDigestIntoEvidence();
        RejectsUnsafeOwnedStageMutationsWithoutDeletingForeignContent();
        ProvesCursorCopiesAreNotIntroducedAtTheCallbackBoundary();
        KeepsTheBoundaryInternalOfflineAndNonDeploying();
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
            AssertEx.Equal(4, harness.ArtifactTransport.AcquireCount);
            AssertEx.Equal(1, harness.ArtifactTransport.ReceiptCount);
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
        }
        finally { first.Dispose(); second.Dispose(); }
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
        foreach (var mutation in new[] { RuntimeBridgeTestFailure.StageMarkerMissing, RuntimeBridgeTestFailure.StageUnexpectedPath })
        {
            var harness = new RuntimeBridgeTestHarness(mutation);
            try
            {
                var result = harness.Bridge.RunAsync(harness.Invocation()).GetAwaiter().GetResult();
                AssertEx.Equal("cleanup-required", result.Status, $"{mutation}: {result.EvidenceJson}");
                AssertEx.False(result.StageCleaned);
                AssertEx.Equal(0, harness.LicenseTransport.CallCount);
                AssertEx.Equal(0, harness.WriteSink.CallCount);
                AssertEx.Equal(1, Directory.GetDirectories(harness.WorkspaceRoot).Length);
                if (mutation == RuntimeBridgeTestFailure.StageUnexpectedPath)
                    AssertEx.True(File.Exists(Path.Combine(Directory.GetDirectories(harness.WorkspaceRoot).Single(), "foreign.txt")));
            }
            finally { harness.Dispose(); }
        }
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
