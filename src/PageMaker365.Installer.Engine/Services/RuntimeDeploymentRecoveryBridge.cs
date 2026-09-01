using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;
using PageMaker365.Installer.Engine.Models;

namespace PageMaker365.Installer.Engine.Services;

/// <summary>
/// Internal synthetic rehearsal only. It cannot select a real transport or deployment handler.
/// </summary>
internal sealed class RuntimeDeploymentRecoveryBridge : IRuntimeBridgeOwnedStageStore
{
    private readonly RuntimeBridgeSyntheticTestCapability capability;
    private readonly PrivateRuntimeDeliveryV07PackageService packageService;
    private readonly PackageTrustOptions packageTrust;
    private readonly RuntimeBridgeLicenseAuthority licenseAuthority;
    private readonly RuntimeConfigurationApplicationV2Service application;
    private readonly DateTimeOffset now;
    private readonly IRuntimeBridgeArtifactTransport artifacts;
    private readonly IRuntimeBridgeProtectedLicenseTransport licenses;
    private readonly IRuntimeBridgeCursorGenerator cursorGenerator;
    private readonly IRuntimeBridgeProtectedWriteSink writes;
    private readonly IRuntimeBridgeWhatIf whatIf;
    private readonly IRuntimeBridgeApproval approvals;
    private readonly IRuntimeBridgeSyntheticHandler handler;
    private readonly IRuntimeBridgeRecovery recovery;
    private readonly IRuntimeBridgeOwnedStageStore stageStore;
    private readonly IRuntimeBridgeOwnedStageRaceProbe? stageRaceProbe;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<string, CachedInvocation> completed = new(StringComparer.Ordinal);
    private readonly Dictionary<string, OwnedStageState> ownedStages = new(StringComparer.OrdinalIgnoreCase);

    internal RuntimeDeploymentRecoveryBridge(
        RuntimeBridgeSyntheticTestCapability capability,
        RuntimeConfigurationCatalogV1Authority catalog,
        PackageTrustOptions packageTrust,
        RuntimeBridgeLicenseAuthority licenseAuthority,
        DateTimeOffset now,
        IRuntimeBridgeArtifactTransport artifacts,
        IRuntimeBridgeProtectedLicenseTransport licenses,
        IRuntimeBridgeCursorGenerator cursorGenerator,
        IRuntimeBridgeProtectedWriteSink writes,
        IRuntimeBridgeWhatIf whatIf,
        IRuntimeBridgeApproval approvals,
        IRuntimeBridgeSyntheticHandler handler,
        IRuntimeBridgeRecovery recovery,
        IRuntimeBridgeOwnedStageStore? stageStore = null,
        IRuntimeBridgeOwnedStageRaceProbe? stageRaceProbe = null)
    {
        this.capability = capability ?? throw new ArgumentNullException(nameof(capability));
        packageService = new PrivateRuntimeDeliveryV07PackageService(catalog ?? throw new ArgumentNullException(nameof(catalog)));
        this.packageTrust = new PackageTrustOptions
        {
            TrustedPublicKeysById = new Dictionary<string, string>(packageTrust?.TrustedPublicKeysById ?? throw new ArgumentNullException(nameof(packageTrust)), StringComparer.OrdinalIgnoreCase)
        };
        ArgumentNullException.ThrowIfNull(licenseAuthority);
        this.licenseAuthority = licenseAuthority with
        {
            PublicKeyPem = string.Concat(licenseAuthority.PublicKeyPem.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd(), "\n")
        };
        application = new RuntimeConfigurationApplicationV2Service(packageService, this.packageTrust, now);
        this.now = now;
        this.artifacts = RequireSeam(artifacts);
        this.licenses = RequireSeam(licenses);
        this.cursorGenerator = RequireSeam(cursorGenerator);
        this.writes = RequireSeam(writes);
        this.whatIf = RequireSeam(whatIf);
        this.approvals = RequireSeam(approvals);
        this.handler = RequireSeam(handler);
        this.recovery = RequireSeam(recovery);
        this.stageStore = stageStore is null ? this : RequireSeam(stageStore);
        this.stageRaceProbe = stageRaceProbe is null ? null : RequireSeam(stageRaceProbe);
    }

    RuntimeBridgeSyntheticTestCapability IRuntimeBridgeSyntheticTestSeam.Capability => capability;

    internal async Task<RuntimeBridgeResult> RunAsync(RuntimeBridgeInvocation invocation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        if (!invocation.Enabled)
            return Result("denied", "runtime_deployment_recovery_bridge_disabled", [], [], null, false, 0, 0, 0, 0, 0, 0);
        if (!Regex.IsMatch(invocation.InvocationId, "^inv_[A-Za-z0-9_-]{16,96}$", RegexOptions.CultureInvariant))
            return Result("denied", "runtime_deployment_recovery_invocation_invalid", [], [], null, false, 0, 0, 0, 0, 0, 0);
        var invocationIdentity = InvocationIdentity(invocation);

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (completed.TryGetValue(invocation.InvocationId, out var existing))
                return existing.InvocationIdentity == invocationIdentity
                    ? existing.Result
                    : Result("denied", "runtime_deployment_recovery_invocation_reuse", [], [], null, false, 0, 0, 0, 0, 0, 0);
            var result = Execute(invocation, cancellationToken);
            completed.Add(invocation.InvocationId, new CachedInvocation(invocationIdentity, result));
            return result;
        }
        finally
        {
            gate.Release();
        }
    }

    private RuntimeBridgeResult Execute(RuntimeBridgeInvocation invocation, CancellationToken cancellationToken)
    {
        RuntimeBridgeOwnedStageLease? stage = null;
        RuntimeBridgeProtectedWriteReceipt? licenseReceipt = null;
        RuntimeBridgeProtectedWriteReceipt? cursorReceipt = null;
        var trace = new List<string>();
        var recoveries = new List<RuntimeBridgeRecoveryResult>();
        var licenseAcquisitions = 0;
        var whatIfCount = 0;
        var approvalCount = 0;
        var handlerCount = 0;
        var simulationAccepted = false;
        var evidenceState = new EvidenceState();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var package = packageService.ValidateJson(invocation.CanonicalPackageJson, packageTrust, now);
            ValidateProjectedLicenseTrust(package);
            evidenceState.PackageHash = package.PackageHash;
            evidenceState.ProjectionSha256 = package.RuntimeConfiguration.ProjectionSha256;
            evidenceState.ManifestSha256 = package.ManifestSha256;
            evidenceState.DeploymentExportId = package.DeploymentExportId;
            evidenceState.ReleaseId = package.ReleaseId;
            evidenceState.RuntimeVersion = package.RuntimeVersion;
            var preliminary = application.CreateDeploymentInput(invocation.CanonicalPackageJson, enabled: true);
            trace.Add("package-authorized");

            var session = artifacts.CreateSession(package, cancellationToken);
            if (session.SessionId != "rds_SYNTHETIC_W09_REHEARSAL_0001" ||
                session.ExpiresAt != DateTimeOffset.Parse("2099-08-30T12:00:00.000Z") ||
                session.Method != "POST" || session.Path != "/api/onboarding/installer/runtime-delivery-sessions" ||
                session.Query.Length != 0 || session.Fragment.Length != 0 ||
                !HeadersEqual(session.RequestHeaders, SessionRequestHeaders) || session.RequestContentType != "application/json" ||
                !session.RequestBodyUtf8.SequenceEqual(SessionRequestBody) || session.StatusCode != 201 || session.Location is not null ||
                session.ResponseContentType != "application/json" || !HeadersEqual(session.ResponseHeaders, SessionResponseHeaders) ||
                !session.ResponseBodyUtf8.SequenceEqual(SessionResponseBody))
                Fail("runtime_bridge_session_invalid");
            trace.Add("session-validated");
            var prepared = new[]
            {
                AcquireArtifact(package, session, "api", cancellationToken),
                AcquireArtifact(package, session, "portal", cancellationToken)
            };
            trace.Add("artifact-protocol-validated");
            var verified = prepared.Select(item => item.Verified).ToArray();
            var artifactReceipt = artifacts.SubmitReceipt(package, session, verified, cancellationToken);
            if (artifactReceipt.SessionId != session.SessionId || artifactReceipt.PackageHash != package.PackageHash ||
                artifactReceipt.Status != "completed" || artifactReceipt.MutationCount != 1 ||
                artifactReceipt.Method != "POST" || artifactReceipt.Path != "/api/onboarding/installer/runtime-delivery-receipts" ||
                artifactReceipt.Query.Length != 0 || artifactReceipt.Fragment.Length != 0 ||
                !HeadersEqual(artifactReceipt.RequestHeaders, ReceiptRequestHeaders) || artifactReceipt.RequestContentType != "application/json" ||
                artifactReceipt.StatusCode != 201 || artifactReceipt.Location is not null ||
                artifactReceipt.ResponseContentType != "application/json" || !HeadersEqual(artifactReceipt.ResponseHeaders, ReceiptResponseHeaders) ||
                artifactReceipt.ResponseBodyUtf8.Length == 0)
                Fail("runtime_bridge_artifact_receipt_invalid");
            trace.Add("receipt-http-validated");
            if (!ValidateReceiptRequestBody(artifactReceipt.RequestBodyUtf8, package, session, verified)) Fail("runtime_bridge_artifact_receipt_request_invalid");
            trace.Add("receipt-body-validated");
            if (!ValidateReceiptResponseBody(artifactReceipt.ResponseBodyUtf8, session, package)) Fail("runtime_bridge_artifact_receipt_response_invalid");
            trace.Add("receipt-response-validated");
            var inventory = prepared.SelectMany(item => item.Inventory).ToArray();
            stage = stageStore.Create(invocation.WorkspaceRoot, invocation.InvocationId, inventory);
            foreach (var artifact in prepared) StageArtifact(stage, artifact);
            stageStore.AssertOwned(stage);
            trace.Add("artifacts-verified");
            evidenceState.ArtifactIdentitySha256 = ArtifactIdentity(verified);

            var pending = package.RuntimeConfiguration.ProtectedSettings.Skip(2).ToArray();
            var provisional = CreateProvisional(package, preliminary, pending, verified);
            var firstRequest = new RuntimeBridgeWhatIfRequest(
                "provisional", package.PackageHash, provisional.IntentSha256, ArtifactIdentity(verified), null, []);
            var firstPreview = ValidatePreview(whatIf.Preview(firstRequest, cancellationToken), firstRequest);
            whatIfCount++;
            evidenceState.PreviewSha256s.Add(firstPreview.PreviewSha256);
            var firstChallenge = Challenge("provisional", package, provisional.IntentSha256, firstPreview, verified, provisional.RecoveryPlanSha256, null, []);
            var firstApproval = ValidateApproval(approvals.Approve(firstChallenge, cancellationToken), firstChallenge);
            approvalCount++;
            var firstApprovalBinding = StableApprovalBinding(firstChallenge);
            evidenceState.ApprovalSha256s.Add(firstApprovalBinding);
            trace.Add("approval-one");

            // D-017 authority: the at-most-once license acquisition/write precedes cursor generation/write.
            licenseAcquisitions++;
            var licenseDescriptor = pending.Single(item => item.Name == "API_LICENSE_SIGNED_PAYLOAD");
            var response = licenses.AcquireOnce(package, session, licenseDescriptor, cancellationToken);
            try
            {
                ValidateLicenseResponse(package, licenseDescriptor, response);
                trace.Add("license-http-validated");
                ValidateSignedLicense(package, response.SignedLicenseUtf8);
                trace.Add("license-signed-validated");
                var licenseDigest = Sha256(response.SignedLicenseUtf8);
                licenseReceipt = ValidateReceipt(writes.Write(new RuntimeBridgeProtectedWriteRequest(
                    licenseDescriptor.Name, licenseDescriptor.Mode, licenseDescriptor.Reference.VaultResourceId,
                    licenseDescriptor.Reference.SecretName, package.PackageHash, firstApprovalBinding,
                    response.SignedLicenseUtf8), cancellationToken), package, licenseDescriptor, firstApprovalBinding, licenseDigest);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(response.SignedLicenseUtf8);
            }
            trace.Add("license-written-first");
            evidenceState.ReceiptIdentitySha256s.Add(ReceiptIdentity(licenseReceipt));

            var cursorDescriptor = pending.Single(item => item.Name == "API_IMAGE_ASSET_CURSOR_SECRET");
            var cursorCallback = new CursorWriteCallback(package, cursorDescriptor, firstApprovalBinding, writes);
            application.GenerateCursorSecret(invocation.CanonicalPackageJson, cursorGenerator, cursorCallback, enabled: true, cancellationToken);
            cursorReceipt = cursorCallback.Receipt ?? throw new InvalidDataException("runtime_bridge_cursor_receipt_missing");
            trace.Add("cursor-written-second");
            evidenceState.ReceiptIdentitySha256s.Add(ReceiptIdentity(cursorReceipt));

            var finalInput = application.FinalizeDeploymentInput(invocation.CanonicalPackageJson, licenseReceipt, cursorReceipt, enabled: true);
            if (finalInput.ApiPublicSettings.Count + finalInput.PortalPublicSettings.Count != 42 ||
                finalInput.ApiVersionedProtectedSettingReferences.Count != 4)
                Fail("runtime_bridge_final_input_shape");
            var receipts = new[] { ReceiptIdentity(licenseReceipt), ReceiptIdentity(cursorReceipt) };
            var secondRequest = new RuntimeBridgeWhatIfRequest(
                "final", package.PackageHash, finalInput.InputSha256, ArtifactIdentity(verified), firstApprovalBinding, receipts);
            var secondPreview = ValidatePreview(whatIf.Preview(secondRequest, cancellationToken), secondRequest);
            whatIfCount++;
            evidenceState.PreviewSha256s.Add(secondPreview.PreviewSha256);
            var secondChallenge = Challenge("final", package, finalInput.InputSha256, secondPreview, verified, provisional.RecoveryPlanSha256, firstApprovalBinding, receipts);
            var secondApproval = ValidateApproval(approvals.Approve(secondChallenge, cancellationToken), secondChallenge);
            approvalCount++;
            var secondApprovalBinding = StableApprovalBinding(secondChallenge);
            evidenceState.ApprovalSha256s.Add(secondApprovalBinding);
            if (secondApproval.ApprovalId == firstApproval.ApprovalId || secondApproval.ApprovalDigest == firstApproval.ApprovalDigest)
                Fail("runtime_bridge_approval_reuse");

            var simulation = handler.Simulate(new RuntimeBridgeSimulationRequest(
                package.PackageHash, finalInput.InputSha256, secondPreview.PreviewSha256,
                secondApprovalBinding, verified, AuthorizesDeployment: false), cancellationToken);
            handlerCount++;
            ValidateSimulationResult(simulation, package.PackageHash, finalInput.InputSha256, secondPreview.PreviewSha256,
                secondApprovalBinding, verified);
            evidenceState.HandlerResultSha256 = simulation.ResultSha256;
            simulationAccepted = true;
            trace.Add("handler-simulated");
            var cleaned = stageStore.Cleanup(stage);
            if (!cleaned)
                return Result("cleanup-required", "runtime_deployment_recovery_stage_cleanup_required", trace, [], null, false,
                    licenseAcquisitions, 2, whatIfCount, approvalCount, handlerCount, 0, evidenceState: evidenceState);
            return Result("simulated", "runtime_deployment_recovery_rehearsal_completed", trace, [], finalInput, true,
                licenseAcquisitions, 2, whatIfCount, approvalCount, handlerCount, 0, evidenceState: evidenceState);
        }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
        {
            if (simulationAccepted)
            {
                var cleanedAfterSimulation = stage is not null && stageStore.Cleanup(stage);
                return Result(cleanedAfterSimulation ? "failed" : "cleanup-required",
                    cleanedAfterSimulation ? "runtime_deployment_recovery_rehearsal_failed" : "runtime_deployment_recovery_stage_cleanup_required",
                    trace, [], null, cleanedAfterSimulation, licenseAcquisitions,
                    (licenseReceipt is null ? 0 : 1) + (cursorReceipt is null ? 0 : 1), whatIfCount, approvalCount, handlerCount, 0,
                    evidenceState: evidenceState);
            }
            var unrecovered = new List<RuntimeBridgeProtectedWriteReceipt>();
            // Reverse of the authorized write order: cursor, then license.
            foreach (var receipt in new[] { cursorReceipt, licenseReceipt }.Where(item => item is not null).Cast<RuntimeBridgeProtectedWriteReceipt>())
            {
                try
                {
                    var result = recovery.Recover(receipt, CancellationToken.None);
                    if (result.ReceiptId != receipt.ReceiptId || result.Status != "recovered" || result.RecoveryCount != 1)
                        throw new InvalidDataException("runtime_bridge_recovery_invalid");
                    recoveries.Add(result);
                    evidenceState.RecoverySemanticSha256s.Add(Sha256(Encoding.UTF8.GetBytes(
                        $"{ReceiptIdentity(receipt)}\nrecovered\n1\n")));
                }
                catch
                {
                    unrecovered.Add(receipt);
                }
            }
            var cleaned = stage is null || stageStore.Cleanup(stage);
            var ambiguity = error is RuntimeBridgeTerminalAmbiguityException;
            return Result(unrecovered.Count > 0 ? "recovery-required" : !cleaned ? "cleanup-required" : "failed",
                unrecovered.Count > 0 ? "runtime_deployment_recovery_required" : !cleaned ? "runtime_deployment_recovery_stage_cleanup_required" :
                ambiguity ? "runtime_deployment_recovery_terminal_ambiguity" : "runtime_deployment_recovery_rehearsal_failed",
                trace, recoveries, null, cleaned, licenseAcquisitions,
                (licenseReceipt is null ? 0 : 1) + (cursorReceipt is null ? 0 : 1), whatIfCount, approvalCount, handlerCount, recoveries.Count,
                unrecovered, evidenceState);
        }
    }

    private PreparedArtifact AcquireArtifact(PrivateRuntimeDeliveryPackageV07 package, RuntimeBridgeArtifactSession session, string kind,
        CancellationToken cancellationToken)
    {
        var expected = package.Artifact(kind);
        var reference = kind == "api" ? package.ApiDeliveryReference : package.PortalDeliveryReference;
        var etag = $"\"sha256:{expected.Sha256}\"";
        var (rangeOffset, rangeLength) = kind == "api" ? (17L, 97) : (29L, 131);
        var fullRequest = new RuntimeBridgeArtifactRequest($"{kind}-full", kind, reference, package.PackageHash, session.SessionId, etag, null, null)
        {
            Path = $"/api/onboarding/installer/runtime-artifacts/{kind}",
            OrderedHeaders = ArtifactRequestHeaders(session.SessionId, reference, etag, null)
        };
        var full = artifacts.Acquire(package, session, fullRequest, cancellationToken);
        var bodyFile = $"artifacts/{kind}.zip";
        if (!full.NoRedirect || full.IsRange || full.StatusCode != 200 || full.VectorId != fullRequest.VectorId ||
            full.ArtifactReference != reference || full.PackageHash != package.PackageHash || full.SessionId != session.SessionId ||
            full.ETag != etag || full.AcceptRanges != "bytes" || full.ContentRange is not null || full.ContentLength != expected.SizeBytes ||
            full.BodyFile != bodyFile || full.CacheControl != "private, no-store" || full.Pragma != "no-cache" ||
            full.ContentTypeOptions != "nosniff" || full.ArtifactKind != kind || full.TotalLength != expected.SizeBytes ||
            full.Offset != 0 || full.Body.LongLength != expected.SizeBytes || full.Sha256 != expected.Sha256 || Sha256(full.Body) != expected.Sha256 ||
            full.ContentType != "application/zip" || full.Location is not null || !HeadersEqual(full.OrderedHeaders, ArtifactResponseHeaders(etag, expected.SizeBytes, null)))
            Fail("runtime_bridge_artifact_protocol_invalid");
        var acceptedBody = full.Body.ToArray();
        using var reconstructed = new MemoryStream(acceptedBody.Length);
        var ranges = GapFreeRanges(expected.SizeBytes, rangeOffset, rangeLength);
        for (var index = 0; index < ranges.Count; index++)
        {
            var (offset, length) = ranges[index];
            var vectorId = offset == rangeOffset && length == rangeLength ? $"{kind}-range" : $"{kind}-range-derived-{index:D2}";
            var rangeValue = $"bytes={offset}-{offset + length - 1}";
            var request = new RuntimeBridgeArtifactRequest(vectorId, kind, reference, package.PackageHash, session.SessionId, etag, offset, length)
            {
                Path = $"/api/onboarding/installer/runtime-artifacts/{kind}",
                OrderedHeaders = ArtifactRequestHeaders(session.SessionId, reference, etag, rangeValue)
            };
            var range = artifacts.Acquire(package, session, request, cancellationToken);
            if (!range.NoRedirect || !range.IsRange || range.StatusCode != 206 || range.VectorId != request.VectorId ||
                range.ArtifactReference != reference || range.PackageHash != package.PackageHash || range.SessionId != session.SessionId ||
                range.ETag != etag || range.AcceptRanges != "bytes" ||
                range.ContentRange != $"bytes {offset}-{offset + length - 1}/{expected.SizeBytes}" ||
                range.ContentLength != length || range.BodyFile != bodyFile || range.CacheControl != "private, no-store" ||
                range.Pragma != "no-cache" || range.ContentTypeOptions != "nosniff" || range.ArtifactKind != kind ||
                range.TotalLength != expected.SizeBytes || range.Offset != offset || range.Sha256 != expected.Sha256 ||
                range.Body.Length != length || offset + range.Body.Length > acceptedBody.Length ||
                !range.Body.AsSpan().SequenceEqual(acceptedBody.AsSpan((int)offset, range.Body.Length)) ||
                range.ContentType != "application/zip" || range.Location is not null ||
                !HeadersEqual(range.OrderedHeaders, ArtifactResponseHeaders(etag, length, $"bytes {offset}-{offset + length - 1}/{expected.SizeBytes}")))
                Fail("runtime_bridge_artifact_protocol_invalid");
            reconstructed.Write(range.Body);
        }
        var reconstructedBytes = reconstructed.ToArray();
        if (reconstructedBytes.Length != acceptedBody.Length || !CryptographicOperations.FixedTimeEquals(reconstructedBytes, acceptedBody) ||
            !FixedHexEquals(Sha256(reconstructedBytes), expected.Sha256))
            Fail("runtime_bridge_artifact_range_reconstruction_invalid");

        var entries = 0;
        var treeRows = new List<(string Path, string Hash)>();
        var extracted = new List<PreparedEntry>();
        byte[]? provenanceBytes = null;
        using (var archive = new ZipArchive(new MemoryStream(acceptedBody, writable: false), ZipArchiveMode.Read, leaveOpen: false))
        {
            long expandedBytes = 0;
            foreach (var entry in archive.Entries)
            {
                var unixMode = (entry.ExternalAttributes >> 16) & 0xF000;
                expandedBytes = checked(expandedBytes + entry.Length);
                if (entries++ >= 128 || entry.Length > 4 * 1024 * 1024 || expandedBytes > 8 * 1024 * 1024 || unixMode == 0xA000)
                    Fail("runtime_bridge_archive_invalid");
                var relative = $"{kind}/{entry.FullName}";
                if (string.IsNullOrEmpty(entry.Name))
                    Fail("runtime_bridge_archive_invalid");
                if (entry.FullName.Contains(':', StringComparison.Ordinal) || entry.FullName.Contains('\\', StringComparison.Ordinal))
                    Fail("runtime_bridge_archive_invalid");
                using var input = entry.Open();
                using var output = new MemoryStream(); input.CopyTo(output);
                var entryBytes = output.ToArray();
                extracted.Add(new PreparedEntry(relative, entryBytes));
                treeRows.Add(($"{entry.FullName}", Sha256(entryBytes)));
                if (entry.FullName == ".pm365/provenance.json") provenanceBytes = entryBytes;
            }
        }
        var tree = string.Join("\n", treeRows.OrderBy(item => item.Path, StringComparer.Ordinal).Select(item => $"{item.Path}:{item.Hash}")) + "\n";
        if (provenanceBytes is null) Fail("runtime_bridge_artifact_provenance");
        using (var provenance = JsonDocument.Parse(provenanceBytes))
        {
            var value = provenance.RootElement;
            if (value.GetProperty("schemaVersion").GetString() != "pagemaker365.runtime-provenance.v1" ||
                value.GetProperty("product").GetString() != "PageMaker365" || value.GetProperty("artifactKind").GetString() != kind ||
                value.GetProperty("releaseId").GetString() != package.ReleaseId || value.GetProperty("runtimeVersion").GetString() != package.RuntimeVersion ||
                value.GetProperty("sourceRepository").GetString() != "cloudbossdev/spo-ui" || value.GetProperty("sourceCommit").GetString() != package.SourceCommit ||
                value.GetProperty("startupCommand").GetString() != expected.StartupCommand)
                Fail("runtime_bridge_artifact_provenance");
        }
        var verified = new RuntimeBridgeVerifiedArtifact(kind, expected.FileName, expected.Sha256, expected.SizeBytes, "owned-stage", Sha256(Encoding.UTF8.GetBytes(tree)), entries);
        var inventory = BuildInventory(expected.FileName, kind, extracted);
        return new PreparedArtifact(verified, acceptedBody, extracted, inventory);
    }

    private static IReadOnlyList<RuntimeBridgeOwnedStageEntry> BuildInventory(string archiveName, string kind, IReadOnlyList<PreparedEntry> entries)
    {
        var directories = new HashSet<string>(StringComparer.Ordinal) { kind };
        foreach (var entry in entries)
        {
            var parts = entry.RelativePath.Split('/');
            for (var index = 1; index < parts.Length; index++)
                directories.Add(string.Join('/', parts.Take(index)));
        }
        return directories.Order(StringComparer.Ordinal).Select(path => new RuntimeBridgeOwnedStageEntry(path, true))
            .Concat(new[] { new RuntimeBridgeOwnedStageEntry(archiveName, false) })
            .Concat(entries.Select(item => new RuntimeBridgeOwnedStageEntry(item.RelativePath, false)))
            .ToArray();
    }

    private static IReadOnlyList<(long Offset, int Length)> GapFreeRanges(long total, long pinnedOffset, int pinnedLength)
    {
        var result = new List<(long, int)>();
        if (pinnedOffset > 0) result.Add((0, checked((int)pinnedOffset)));
        result.Add((pinnedOffset, pinnedLength));
        var cursor = pinnedOffset + pinnedLength;
        while (cursor < total)
        {
            var length = checked((int)Math.Min(pinnedLength, total - cursor));
            result.Add((cursor, length));
            cursor += length;
        }
        return result;
    }

    private void StageArtifact(RuntimeBridgeOwnedStageLease stage, PreparedArtifact artifact)
    {
        stageStore.AssertOwned(stage);
        stageStore.WriteFileExclusive(stage, artifact.Verified.FileName, artifact.ArchiveBytes);
        stageStore.CreateDirectoryExclusive(stage, artifact.Verified.ArtifactKind);
        foreach (var entry in artifact.Entries)
            stageStore.WriteFileExclusive(stage, entry.RelativePath, entry.Bytes);
    }

    private static RuntimeBridgeProvisionalIntent CreateProvisional(PrivateRuntimeDeliveryPackageV07 package, RuntimeConfigurationApplicationV2DeploymentInput input,
        IReadOnlyList<RuntimeConfigurationProtectedSettingV2> pending, IReadOnlyList<RuntimeBridgeVerifiedArtifact> artifacts)
    {
        var destinations = pending.Select(item => new RuntimeBridgeProtectedDestination(item.Name, item.Mode, item.Reference.VaultResourceId,
            item.Reference.SecretName, Sha256(Encoding.UTF8.GetBytes($"{item.Name}\n{item.Mode}\n{item.Reference.VaultResourceId}\n{item.Reference.SecretName}\n")))).ToArray();
        var recoveryDigest = Sha256(Encoding.UTF8.GetBytes(string.Join("\n", destinations.Reverse().Select(item => item.DestinationSha256)) + "\n"));
        var canonical = JsonSerializer.Serialize(new
        {
            packageHash = package.PackageHash, projectionSha256 = package.RuntimeConfiguration.ProjectionSha256,
            deploymentInputSha256 = input.InputSha256,
            pendingDestinations = destinations.Select(item => new { item.Name, item.Mode, item.DestinationSha256 }),
            artifacts = artifacts.Select(item => new { item.ArtifactKind, item.Sha256, item.SizeBytes, item.ExtractedTreeSha256 }),
            recoveryPlanSha256 = recoveryDigest
        }, SafeJson) + "\n";
        return new RuntimeBridgeProvisionalIntent(package.PackageHash, package.RuntimeConfiguration.ProjectionSha256, package.ManifestSha256,
            input.InputSha256, input.ApiPublicSettings.Concat(input.PortalPublicSettings).ToArray(), input.ApiVersionedProtectedSettingReferences,
            destinations, artifacts, recoveryDigest, canonical, Sha256(Encoding.UTF8.GetBytes(canonical)));
    }

    private static RuntimeBridgeWhatIfResult ValidatePreview(RuntimeBridgeWhatIfResult result, RuntimeBridgeWhatIfRequest request)
    {
        if (result.Phase != request.Phase || result.Status != "previewed" || result.RequestSha256 != WhatIfRequestIdentity(request) ||
            result.ResourceWriteCount != 0 || result.DeploymentCount != 0 ||
            result.PreviewSha256 != Sha256(Encoding.UTF8.GetBytes(result.CanonicalJson))) Fail("runtime_bridge_whatif_invalid");
        return result;
    }

    private RuntimeBridgeApprovalChallenge Challenge(string phase, PrivateRuntimeDeliveryPackageV07 package, string inputSha, RuntimeBridgeWhatIfResult preview,
        IReadOnlyList<RuntimeBridgeVerifiedArtifact> artifacts, string recoverySha, string? firstApproval, IReadOnlyList<string> receiptIdentities)
    {
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var expires = now.AddMinutes(5);
        var canonical = $"{phase}\n{nonce}\n{package.PackageHash}\n{inputSha}\n{preview.PreviewSha256}\n{ArtifactIdentity(artifacts)}\n{recoverySha}\n{firstApproval}\n{string.Join(',', receiptIdentities)}\n{expires:O}\n";
        return new RuntimeBridgeApprovalChallenge(phase, nonce, package.PackageHash, inputSha, preview.PreviewSha256, ArtifactIdentity(artifacts), recoverySha,
            firstApproval, receiptIdentities, expires, Sha256(Encoding.UTF8.GetBytes(canonical)));
    }

    private RuntimeBridgeApprovalReceipt ValidateApproval(RuntimeBridgeApprovalReceipt receipt, RuntimeBridgeApprovalChallenge challenge)
    {
        if (receipt.Phase != challenge.Phase || receipt.ChallengeSha256 != challenge.ChallengeSha256 || receipt.Nonce != challenge.Nonce ||
            receipt.ExpiresAt != challenge.ExpiresAt || receipt.ExpiresAt <= now || receipt.Outcome != "approved" || receipt.UseCount != 1 ||
            string.IsNullOrWhiteSpace(receipt.ApprovalId) ||
            receipt.ApprovalDigest != Sha256(Encoding.UTF8.GetBytes(receipt.ApprovalId + "\n" + challenge.ChallengeSha256 + "\n")))
            Fail("runtime_bridge_approval_invalid");
        return receipt;
    }

    private static void ValidateLicenseResponse(PrivateRuntimeDeliveryPackageV07 package, RuntimeConfigurationProtectedSettingV2 descriptor, RuntimeBridgeProtectedLicenseResponse response)
    {
        if (response.ContractVersion != descriptor.Reference.ContractVersion || response.PackageHash != package.PackageHash || response.TargetApp != "api" ||
            response.Name != descriptor.Name || response.Reference != descriptor.Reference.OpaqueReference || response.CacheControl != "private, no-store" ||
            response.Pragma != "no-cache" || response.ContentTypeOptions != "nosniff" ||
            response.Vary != "Authorization, X-PM365-Onboarding-Session, X-PM365-Runtime-Delivery-Session" || !response.NoRedirect || response.SignedLicenseUtf8.Length == 0 ||
            response.Method != "POST" || response.Path != "/api/onboarding/installer/runtime-protected-settings/acquire" ||
            response.Query.Length != 0 || response.Fragment.Length != 0 || !HeadersEqual(response.RequestHeaders, ProtectedRequestHeaders) ||
            response.RequestContentType != "application/json" || !response.RequestBodyUtf8.SequenceEqual(ProtectedRequestBody(package, descriptor)) ||
            response.StatusCode != 200 || response.ResponseContentType != "application/json" || response.Location is not null ||
            !HeadersEqual(response.ResponseHeaders, ProtectedResponseHeaders) || response.ProtectedReadCount != 1 || response.RedemptionCount != 1 ||
            !response.ResponseBodyUtf8.SequenceEqual(ProtectedResponseBody(response)))
            Fail("runtime_bridge_license_response_invalid");
    }

    private void ValidateSignedLicense(PrivateRuntimeDeliveryPackageV07 package, byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        var names = root.EnumerateObject().Select(item => item.Name).OrderBy(item => item, StringComparer.Ordinal).ToArray();
        if (!names.SequenceEqual(new[] { "payload", "schemaVersion", "signature" }, StringComparer.Ordinal) ||
            !bytes.SequenceEqual(PrivateRuntimeCanonicalJson.Canonicalize(root)) ||
            root.GetProperty("schemaVersion").GetString() != "pagemaker365.license.v1") Fail("runtime_bridge_license_invalid");
        var payload = root.GetProperty("payload");
        var payloadNames = payload.EnumerateObject().Select(item => item.Name).OrderBy(item => item, StringComparer.Ordinal).ToArray();
        var expectedPayloadNames = new[]
        {
            "activationId", "customerDisplayName", "customerId", "customerKey", "environmentId", "environmentKey", "environmentLimit",
            "environmentType", "installationId", "installationKey", "issuedAt", "licenseId", "planKey", "product", "subscriptionId",
            "supportTier", "validFrom", "validTo", "workspaceLimit"
        }.OrderBy(item => item, StringComparer.Ordinal).ToArray();
        if (!payloadNames.SequenceEqual(expectedPayloadNames, StringComparer.Ordinal) || payload.GetProperty("product").GetString() != "PageMaker365" ||
            payload.GetProperty("licenseId").GetString() != "synthetic-w09-rehearsal-license" ||
            payload.GetProperty("activationId").GetString() != "f81e2937-c411-4fa0-b208-42d34464d6ae" ||
            payload.GetProperty("customerKey").GetString() != "synthetic-w09-rehearsal" ||
            payload.GetProperty("customerDisplayName").GetString() != "Synthetic W09 Rehearsal" ||
            payload.GetProperty("subscriptionId").GetString() != licenseAuthority.SubscriptionId ||
            payload.GetProperty("installationKey").GetString() != "primary" || payload.GetProperty("environmentKey").GetString() != "sandbox" ||
            payload.GetProperty("environmentType").GetString() != "sandbox" || payload.GetProperty("planKey").GetString() != "synthetic-test-only" ||
            payload.GetProperty("supportTier").GetString() != "none" || payload.GetProperty("workspaceLimit").GetInt32() != 1 ||
            payload.GetProperty("environmentLimit").GetInt32() != 1 ||
            payload.GetProperty("customerId").GetString() != package.CustomerId || payload.GetProperty("installationId").GetString() != package.InstallationId ||
            payload.GetProperty("environmentId").GetString() != package.EnvironmentId || package.TenantId != "d43a8f61-20e7-4c95-a13b-5792fd84c6e0" ||
            package.AzureSubscriptionId != "61b7c2e9-8f34-45ad-b062-3ea19d75f48c" ||
            package.DeploymentExportId != "934be217-c568-49f0-8d3a-62e15b74c09f" || package.ReleaseId != "pm365-runtime-1.4.3+c31427d" ||
            DateTimeOffset.Parse(payload.GetProperty("validFrom").GetString()!) != now ||
            DateTimeOffset.Parse(payload.GetProperty("validTo").GetString()!) != DateTimeOffset.Parse("2099-08-30T12:00:00.000Z") ||
            DateTimeOffset.Parse(payload.GetProperty("issuedAt").GetString()!) != now)
            Fail("runtime_bridge_license_binding");
        var signature = root.GetProperty("signature");
        if (!signature.EnumerateObject().Select(item => item.Name).SequenceEqual(new[] { "alg", "kid", "value" }, StringComparer.Ordinal) ||
            signature.GetProperty("alg").GetString() != licenseAuthority.Algorithm || signature.GetProperty("kid").GetString() != licenseAuthority.KeyId)
            Fail("runtime_bridge_license_signature");
        var publicKey = (Ed25519PublicKeyParameters)PublicKeyFactory.CreateKey(Convert.FromBase64String(string.Concat(licenseAuthority.PublicKeyPem.Split('\n').Where(line => !line.StartsWith("---", StringComparison.Ordinal) && line.Length > 0))));
        var signatureText = signature.GetProperty("value").GetString()!;
        var signatureBytes = DecodeBase64Url(signatureText);
        if (EncodeBase64Url(signatureBytes) != signatureText || signatureText != licenseAuthority.Signature) Fail("runtime_bridge_license_signature");
        var canonical = PrivateRuntimeCanonicalJson.Canonicalize(payload);
        var signedDocumentSha = Sha256(PrivateRuntimeCanonicalJson.Canonicalize(root));
        if (licenseAuthority.Canonicalization != "json-c14n-v1" || licenseAuthority.FingerprintDomain != "json-c14n-v1:license-payload" ||
            !FixedHexEquals(signedDocumentSha, licenseAuthority.SignedPayloadSha256) ||
            !FixedHexEquals(signedDocumentSha, licenseAuthority.SignedPayloadFingerprint))
            Fail("runtime_bridge_license_fingerprint");
        var verifier = new Ed25519Signer(); verifier.Init(false, publicKey); verifier.BlockUpdate(canonical, 0, canonical.Length);
        if (!verifier.VerifySignature(signatureBytes)) Fail("runtime_bridge_license_signature");
    }

    private static RuntimeBridgeProtectedWriteReceipt ValidateReceipt(RuntimeBridgeProtectedWriteReceipt receipt, PrivateRuntimeDeliveryPackageV07 package,
        RuntimeConfigurationProtectedSettingV2 descriptor, string approvalDigest, string expectedContentSha256)
    {
        if (receipt.Name != descriptor.Name || receipt.Mode != descriptor.Mode || receipt.VaultResourceId != descriptor.Reference.VaultResourceId ||
            receipt.SecretName != descriptor.Reference.SecretName || receipt.PackageHash != package.PackageHash || receipt.ApprovalDigest != approvalDigest ||
            !Regex.IsMatch(receipt.ContentSha256, "^[0-9a-f]{64}$", RegexOptions.CultureInvariant) ||
            !FixedHexEquals(receipt.ContentSha256, expectedContentSha256) || receipt.Outcome != "written" || receipt.WriteCount != 1 ||
            !Regex.IsMatch(receipt.ReceiptId, "^rwr_[A-Za-z0-9_-]{16,96}$", RegexOptions.CultureInvariant) ||
            !Regex.IsMatch(receipt.SecretVersion, "^[A-Za-z0-9]{16,64}$", RegexOptions.CultureInvariant))
            Fail("runtime_bridge_write_receipt_invalid");
        var vaultOrigin = package.RuntimeConfiguration.PublicSettings.Single(item => item.Name == "API_AZURE_KEY_VAULT_URL").Value.GetString()!.TrimEnd('/');
        var expectedReference = $"@Microsoft.KeyVault(SecretUri={vaultOrigin}/secrets/{receipt.SecretName}/{receipt.SecretVersion})";
        if (receipt.KeyVaultReference != expectedReference) Fail("runtime_bridge_write_receipt_invalid");
        return receipt;
    }

    private void ValidateProjectedLicenseTrust(PrivateRuntimeDeliveryPackageV07 package)
    {
        var projectedPem = package.RuntimeConfiguration.PublicSettings.Single(item => item.Name == "API_LICENSE_PUBLIC_KEY_PEM").Value.GetString()!;
        var projected = Encoding.UTF8.GetBytes(projectedPem);
        var pinned = Encoding.UTF8.GetBytes(licenseAuthority.PublicKeyPem);
        if (licenseAuthority.Algorithm != "Ed25519" || !Regex.IsMatch(licenseAuthority.KeyId, "^[A-Za-z0-9_-]{16,96}$", RegexOptions.CultureInvariant) ||
            !FixedHexEquals(Sha256(pinned), licenseAuthority.PublicKeySha256) || projected.Length != pinned.Length ||
            !CryptographicOperations.FixedTimeEquals(projected, pinned))
            Fail("runtime_bridge_license_trust_binding");
    }

    private RuntimeBridgeResult Result(string status, string code, IReadOnlyList<string> trace, IReadOnlyList<RuntimeBridgeRecoveryResult> recoveries,
        RuntimeConfigurationFinalizedDeploymentInputV2? final, bool cleaned, int acquisitions, int writesCount, int whatIfCount,
        int approvalCount, int handlerCount, int recoveryCount, IReadOnlyList<RuntimeBridgeProtectedWriteReceipt>? ownedReceipts = null,
        EvidenceState? evidenceState = null)
    {
        var evidence = JsonSerializer.Serialize(new
        {
            status, safeCode = code, authorizesDeployment = false, trace,
            packageHash = evidenceState?.PackageHash,
            projectionSha256 = evidenceState?.ProjectionSha256,
            manifestSha256 = evidenceState?.ManifestSha256,
            deploymentExportId = evidenceState?.DeploymentExportId,
            releaseId = evidenceState?.ReleaseId,
            runtimeVersion = evidenceState?.RuntimeVersion,
            artifactIdentitySha256 = evidenceState?.ArtifactIdentitySha256,
            handlerResultSha256 = evidenceState?.HandlerResultSha256,
            previewSha256s = evidenceState?.PreviewSha256s ?? [],
            approvalSha256s = evidenceState?.ApprovalSha256s ?? [],
            receiptIdentitySha256s = evidenceState?.ReceiptIdentitySha256s ?? [],
            finalInputSha256 = final?.InputSha256,
            recoverySemanticSha256s = evidenceState?.RecoverySemanticSha256s ?? [],
            counts = new { licenseAcquisitions = acquisitions, protectedWrites = writesCount, whatIf = whatIfCount, approvals = approvalCount, handler = handlerCount, recovery = recoveryCount },
            stageCleaned = cleaned
        }, SafeJson) + "\n";
        return new RuntimeBridgeResult(status, code, false, acquisitions, writesCount, whatIfCount, approvalCount, handlerCount, recoveryCount,
            cleaned, evidence, Sha256(Encoding.UTF8.GetBytes(evidence)), final, ownedReceipts ?? []);
    }

    private T RequireSeam<T>(T seam) where T : class, IRuntimeBridgeSyntheticTestSeam
    {
        ArgumentNullException.ThrowIfNull(seam);
        if (!ReferenceEquals(seam.Capability, capability)) throw new InvalidDataException("runtime_bridge_capability_mismatch");
        return seam;
    }

    private static string ArtifactIdentity(IEnumerable<RuntimeBridgeVerifiedArtifact> artifacts) =>
        Sha256(Encoding.UTF8.GetBytes(string.Join("\n", artifacts.Select(item => $"{item.ArtifactKind}:{item.Sha256}:{item.SizeBytes}:{item.ExtractedTreeSha256}")) + "\n"));
    private static string WhatIfRequestIdentity(RuntimeBridgeWhatIfRequest request) => Sha256(Encoding.UTF8.GetBytes(
        $"{request.Phase}\n{request.PackageHash}\n{request.InputSha256}\n{request.ArtifactIdentitySha256}\n{request.PhaseOneApprovalDigest}\n{string.Join(',', request.ReceiptIdentitySha256s)}\n"));
    private static string ReceiptIdentity(RuntimeBridgeProtectedWriteReceipt receipt) => Sha256(Encoding.UTF8.GetBytes(
        $"{receipt.Name}\n{receipt.Mode}\n{receipt.VaultResourceId}\n{receipt.SecretName}\n{receipt.SecretVersion}\n{receipt.KeyVaultReference}\n{receipt.ContentSha256}\n{receipt.PackageHash}\n{receipt.ApprovalDigest}\n{receipt.Outcome}\n{receipt.WriteCount}\n"));
    private static string StableApprovalBinding(RuntimeBridgeApprovalChallenge challenge) => Sha256(Encoding.UTF8.GetBytes(
        $"{challenge.Phase}\n{challenge.PackageHash}\n{challenge.InputSha256}\n{challenge.PreviewSha256}\n{challenge.ArtifactIdentitySha256}\n{challenge.RecoveryPlanSha256}\n{challenge.PhaseOneApprovalDigest}\n{string.Join(',', challenge.ReceiptIdentitySha256s)}\n"));
    private static string InvocationIdentity(RuntimeBridgeInvocation invocation) => Sha256(Encoding.UTF8.GetBytes(
        $"{Sha256(Encoding.UTF8.GetBytes(invocation.CanonicalPackageJson))}\n{invocation.WorkspaceRoot}\n{invocation.InstallerVersion}\n{invocation.Enabled.ToString().ToLowerInvariant()}\n"));
    private static void ValidateSimulationResult(RuntimeBridgeSimulationResult result, string packageHash, string finalInputSha256,
        string finalPreviewSha256, string approvalBindingSha256, IReadOnlyList<RuntimeBridgeVerifiedArtifact> artifacts)
    {
        var artifactIdentity = ArtifactIdentity(artifacts);
        var canonical = $"{packageHash}\n{finalInputSha256}\n{finalPreviewSha256}\n{approvalBindingSha256}\n{artifactIdentity}\nsimulated\nfalse\n0\n0\n0\n";
        if (result.PackageHash != packageHash || result.FinalInputSha256 != finalInputSha256 ||
            result.FinalPreviewSha256 != finalPreviewSha256 || result.ApprovalBindingSha256 != approvalBindingSha256 ||
            result.ArtifactIdentitySha256 != artifactIdentity || result.Status != "simulated" || result.AuthorizesDeployment ||
            result.ResourceCount != 0 || result.WriteCount != 0 || result.DeploymentCount != 0 ||
            !FixedHexEquals(result.ResultSha256, Sha256(Encoding.UTF8.GetBytes(canonical))))
            Fail("runtime_bridge_handler_outcome");
    }
    private static string ValidateTrustedRoot(string requestedRoot)
    {
        if (string.IsNullOrWhiteSpace(requestedRoot)) Fail("runtime_bridge_stage_root_invalid");
        var root = Path.GetFullPath(requestedRoot);
        if (!Path.IsPathFullyQualified(root) || !Directory.Exists(root) || File.Exists(root)) Fail("runtime_bridge_stage_root_invalid");
        AssertSafeComponents(root);
        return root.TrimEnd(Path.DirectorySeparatorChar);
    }

    RuntimeBridgeOwnedStageLease IRuntimeBridgeOwnedStageStore.Create(string trustedRoot, string invocationId, IReadOnlyList<RuntimeBridgeOwnedStageEntry> inventory)
    {
        var root = ValidateTrustedRoot(trustedRoot);
        var stageRoot = CreateOwnedStage(root, invocationId, inventory, out var marker);
        return new RuntimeBridgeOwnedStageLease(invocationId, root, stageRoot, marker);
    }

    void IRuntimeBridgeOwnedStageStore.AssertOwned(RuntimeBridgeOwnedStageLease lease) =>
        AssertOwnedStage(lease.TrustedRoot, lease.StageRoot, lease.OwnershipMarker);

    void IRuntimeBridgeOwnedStageStore.CreateDirectoryExclusive(RuntimeBridgeOwnedStageLease lease, string relativePath)
    {
        RequireSafeRelative(relativePath);
        CreateDirectoryExclusive(lease.TrustedRoot, lease.StageRoot, lease.OwnershipMarker, Path.Combine(lease.StageRoot, relativePath));
    }

    void IRuntimeBridgeOwnedStageStore.WriteFileExclusive(RuntimeBridgeOwnedStageLease lease, string relativePath, ReadOnlySpan<byte> bytes)
    {
        RequireSafeRelative(relativePath);
        var path = Path.Combine(lease.StageRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var parent = Path.GetDirectoryName(path)!;
        CreateSafeDirectories(lease.TrustedRoot, lease.StageRoot, lease.OwnershipMarker, lease.StageRoot, parent);
        WriteExclusive(lease.TrustedRoot, lease.StageRoot, lease.OwnershipMarker, path, bytes);
    }

    bool IRuntimeBridgeOwnedStageStore.Cleanup(RuntimeBridgeOwnedStageLease lease) =>
        TryCleanupStage(lease.TrustedRoot, lease.StageRoot, lease.OwnershipMarker);

    private static void RequireSafeRelative(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath) || relativePath.Contains(':') ||
            relativePath.Contains('\\') || relativePath.Split('/').Any(part => part is "" or "." or ".."))
            Fail("runtime_bridge_stage_path_invalid");
    }

    private string CreateOwnedStage(string trustedRoot, string invocationId, IReadOnlyList<RuntimeBridgeOwnedStageEntry> inventory, out byte[] marker)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("runtime_bridge_stage_platform_unproved");
        marker = RandomNumberGenerator.GetBytes(32);
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var stageRoot = Path.Combine(trustedRoot, $"pm365-synthetic-runtime-{nonce}");
        if (!CreateDirectoryNative(stageRoot, IntPtr.Zero))
            throw new IOException($"runtime_bridge_stage_create_{Marshal.GetLastWin32Error()}");
        var markerPath = Path.Combine(stageRoot, ".pm365-owned");
        try
        {
            using (var stream = new FileStream(markerPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(marker);
                stream.Flush(flushToDisk: true);
            }
            var identities = new Dictionary<string, FileIdentity>(StringComparer.OrdinalIgnoreCase)
            {
                [markerPath] = GetIdentity(markerPath, isDirectory: false)
            };
            var ownershipHandles = new List<SafeFileHandle>
            {
                OpenOwnershipHandle(stageRoot, isDirectory: true),
                OpenOwnershipHandle(markerPath, isDirectory: false)
            };
            var declared = inventory.Select(item =>
            {
                RequireSafeRelative(item.RelativePath);
                return (Path.GetFullPath(Path.Combine(stageRoot, item.RelativePath.Replace('/', Path.DirectorySeparatorChar))), item.IsDirectory);
            }).ToDictionary(item => item.Item1, item => item.IsDirectory, StringComparer.OrdinalIgnoreCase);
            if (declared.Count != inventory.Count) Fail("runtime_bridge_stage_inventory_invalid");
            ownedStages.Add(stageRoot, new OwnedStageState(invocationId, marker.ToArray(), GetIdentity(trustedRoot, isDirectory: true),
                GetIdentity(stageRoot, isDirectory: true), identities, declared, ownershipHandles));
            AssertOwnedStage(trustedRoot, stageRoot, marker);
            return stageRoot;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(marker);
            throw;
        }
    }

    private void AssertOwnedStage(string trustedRoot, string stageRoot, byte[] marker)
    {
        var fullRoot = Path.GetFullPath(trustedRoot).TrimEnd(Path.DirectorySeparatorChar);
        var fullStage = Path.GetFullPath(stageRoot).TrimEnd(Path.DirectorySeparatorChar);
        if (Path.GetDirectoryName(fullStage) != fullRoot || !ownedStages.TryGetValue(fullStage, out var state) || state is null)
            throw new InvalidDataException("runtime_bridge_stage_ownership_invalid");
        if (!CryptographicOperations.FixedTimeEquals(state.Marker, marker)) Fail("runtime_bridge_stage_ownership_invalid");
        AssertSafeComponents(fullRoot);
        AssertSafeComponents(fullStage);
        if (GetIdentity(fullRoot, isDirectory: true) != state.RootIdentity || GetIdentity(fullStage, isDirectory: true) != state.StageIdentity)
            Fail("runtime_bridge_stage_identity_changed");
        var markerPath = Path.Combine(fullStage, ".pm365-owned");
        if (!File.Exists(markerPath) || (File.GetAttributes(markerPath) & FileAttributes.ReparsePoint) != 0)
            Fail("runtime_bridge_stage_ownership_invalid");
        var actualMarker = File.ReadAllBytes(markerPath);
        try
        {
            if (actualMarker.Length != marker.Length || !CryptographicOperations.FixedTimeEquals(actualMarker, marker))
                Fail("runtime_bridge_stage_ownership_invalid");
        }
        finally { CryptographicOperations.ZeroMemory(actualMarker); }
        foreach (var path in Directory.EnumerateFileSystemEntries(fullStage, "*", SearchOption.AllDirectories))
        {
            AssertSafeComponents(path);
            if (!state.OwnedPaths.TryGetValue(path, out var expectedIdentity)) Fail("runtime_bridge_stage_unexpected_path");
            var isDirectory = Directory.Exists(path);
            if (GetIdentity(path, isDirectory) != expectedIdentity) Fail("runtime_bridge_stage_identity_changed");
        }
    }

    private static void AssertSafeComponents(string path)
    {
        var full = Path.GetFullPath(path);
        var driveRoot = Path.GetPathRoot(full)!;
        var relative = full[driveRoot.Length..];
        if (relative.Contains(':', StringComparison.Ordinal)) Fail("runtime_bridge_stage_path_invalid");
        var current = driveRoot;
        foreach (var part in relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            if (!File.Exists(current) && !Directory.Exists(current)) Fail("runtime_bridge_stage_component_missing");
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) Fail("runtime_bridge_stage_reparse_denied");
        }
    }

    private void WriteExclusive(string trustedRoot, string stageRoot, byte[] marker, string path, ReadOnlySpan<byte> bytes)
    {
        AssertOwnedStage(trustedRoot, stageRoot, marker);
        AssertPathUnderStage(stageRoot, path);
        AssertDeclared(stageRoot, path, isDirectory: false);
        var handle = CreateFileNative(path, 0x40000000u | 0x80u, 0, IntPtr.Zero, 1, 0x80u | 0x80000000u | 0x00200000u, IntPtr.Zero);
        if (handle.IsInvalid) Fail("runtime_bridge_stage_collision");
        try
        {
            RandomAccess.Write(handle, bytes, 0);
            RandomAccess.FlushToDisk(handle);
            stageRaceProbe?.Probe("file-created-before-registration", path);
            RegisterOwnedHandle(stageRoot, path, isDirectory: false, handle, retainHandle: true);
        }
        catch { handle.Dispose(); throw; }
        AssertOwnedStage(trustedRoot, stageRoot, marker);
    }

    private void CreateDirectoryExclusive(string trustedRoot, string stageRoot, byte[] marker, string path)
    {
        AssertOwnedStage(trustedRoot, stageRoot, marker);
        AssertPathUnderStage(stageRoot, path);
        AssertDeclared(stageRoot, path, isDirectory: true);
        if (!CreateDirectoryNative(path, IntPtr.Zero)) Fail("runtime_bridge_stage_collision");
        var handle = OpenOwnershipHandle(path, isDirectory: true);
        try
        {
            stageRaceProbe?.Probe("directory-created-before-registration", path);
            RegisterOwnedHandle(stageRoot, path, isDirectory: true, handle, retainHandle: true);
        }
        catch { handle.Dispose(); throw; }
        AssertSafeComponents(path);
        AssertOwnedStage(trustedRoot, stageRoot, marker);
    }

    private void CreateSafeDirectories(string trustedRoot, string stageRoot, byte[] marker, string extractionRoot, string destinationDirectory)
    {
        var current = extractionRoot;
        var relative = Path.GetRelativePath(extractionRoot, destinationDirectory);
        if (relative == ".") return;
        foreach (var part in relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            if (!Directory.Exists(current))
            {
                if (File.Exists(current)) Fail("runtime_bridge_stage_component_invalid");
                AssertOwnedStage(trustedRoot, stageRoot, marker);
                if (!CreateDirectoryNative(current, IntPtr.Zero)) Fail("runtime_bridge_stage_collision");
                var handle = OpenOwnershipHandle(current, isDirectory: true);
                try
                {
                    stageRaceProbe?.Probe("directory-created-before-registration", current);
                    RegisterOwnedHandle(stageRoot, current, isDirectory: true, handle, retainHandle: true);
                }
                catch { handle.Dispose(); throw; }
            }
            AssertSafeComponents(current);
        }
    }

    private static void AssertPathUnderStage(string stageRoot, string path)
    {
        var fullStage = Path.GetFullPath(stageRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(path);
        var relative = fullPath[Path.GetPathRoot(fullPath)!.Length..];
        if (!fullPath.StartsWith(fullStage, StringComparison.OrdinalIgnoreCase) || relative.Contains(':', StringComparison.Ordinal))
            Fail("runtime_bridge_stage_path_invalid");
    }

    private void RegisterOwnedHandle(string stageRoot, string path, bool isDirectory, SafeFileHandle handle, bool retainHandle)
    {
        if (!ownedStages.TryGetValue(stageRoot, out var state) || state is null) Fail("runtime_bridge_stage_ownership_invalid");
        var full = Path.GetFullPath(path);
        AssertDeclared(stageRoot, full, isDirectory);
        if (!GetFileInformationByHandle(handle, out var info) || (!isDirectory && info.NumberOfLinks != 1))
            Fail("runtime_bridge_stage_identity_unavailable");
        var identity = new FileIdentity(info.VolumeSerialNumber, ((ulong)info.FileIndexHigh << 32) | info.FileIndexLow);
        if (!state!.OwnedPaths.TryAdd(full, identity)) Fail("runtime_bridge_stage_collision");
        if (retainHandle) state.OwnershipHandles.Add(handle);
    }

    private void AssertDeclared(string stageRoot, string path, bool isDirectory)
    {
        if (!ownedStages.TryGetValue(stageRoot, out var state) || state is null ||
            !state.DeclaredPaths.TryGetValue(Path.GetFullPath(path), out var declaredDirectory) || declaredDirectory != isDirectory)
            Fail("runtime_bridge_stage_inventory_invalid");
    }

    private bool TryCleanupStage(string trustedRoot, string path, byte[] marker)
    {
        try
        {
            AssertOwnedStage(trustedRoot, path, marker);
            if (!ownedStages.TryGetValue(path, out var state)) return false;
            stageRaceProbe?.Probe("cleanup-after-validation-before-dispose", path);
            foreach (var handle in state.OwnershipHandles) handle.Dispose();
            state.OwnershipHandles.Clear();
            foreach (var owned in state.OwnedPaths.Keys.Where(File.Exists).OrderByDescending(item => item.Length))
            {
                if (GetIdentity(owned, isDirectory: false) != state.OwnedPaths[owned]) return false;
                File.Delete(owned);
            }
            foreach (var owned in state.OwnedPaths.Keys.Where(Directory.Exists).OrderByDescending(item => item.Length))
            {
                if (GetIdentity(owned, isDirectory: true) != state.OwnedPaths[owned]) return false;
                Directory.Delete(owned, recursive: false);
            }
            if (GetIdentity(path, isDirectory: true) != state.StageIdentity) return false;
            Directory.Delete(path, recursive: false);
            ownedStages.Remove(path);
            CryptographicOperations.ZeroMemory(state.Marker);
            CryptographicOperations.ZeroMemory(marker);
            return !Directory.Exists(path);
        }
        catch { return false; }
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateDirectoryW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateDirectoryNative(string path, IntPtr securityAttributes);
    private static FileIdentity GetIdentity(string path, bool isDirectory)
    {
        using var handle = CreateFileNative(path, 0x80, 0x1 | 0x2 | 0x4, IntPtr.Zero, 3,
            isDirectory ? 0x02000000u | 0x00200000u : 0x00200000u, IntPtr.Zero);
        var info = default(ByHandleFileInformation);
        if (handle.IsInvalid || !GetFileInformationByHandle(handle, out info)) Fail("runtime_bridge_stage_identity_unavailable");
        if (!isDirectory && info.NumberOfLinks != 1) Fail("runtime_bridge_stage_hardlink_denied");
        return new FileIdentity(info.VolumeSerialNumber, ((ulong)info.FileIndexHigh << 32) | info.FileIndexLow);
    }
    private static SafeFileHandle OpenOwnershipHandle(string path, bool isDirectory)
    {
        var handle = CreateFileNative(path, isDirectory ? 0x80u | 0x00010000u : 0x80u, 0x1 | 0x2, IntPtr.Zero, 3,
            isDirectory ? 0x02000000u | 0x00200000u : 0x00200000u, IntPtr.Zero);
        if (handle.IsInvalid) Fail("runtime_bridge_stage_identity_unavailable");
        return handle;
    }
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileNative(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
        uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle file, out ByHandleFileInformation information);
    private static string Sha256(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static bool FixedHexEquals(string left, string right)
    {
        var leftBytes = Encoding.ASCII.GetBytes(left);
        var rightBytes = Encoding.ASCII.GetBytes(right);
        try { return leftBytes.Length == rightBytes.Length && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes); }
        finally { CryptographicOperations.ZeroMemory(leftBytes); CryptographicOperations.ZeroMemory(rightBytes); }
    }
    private static byte[] DecodeBase64Url(string value) => Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/') + ((value.Length % 4) switch { 2 => "==", 3 => "=", 0 => "", _ => throw new InvalidDataException("runtime_bridge_license_signature") }));
    private static string EncodeBase64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static bool HeadersEqual(IReadOnlyList<RuntimeBridgeHttpHeader> actual, IReadOnlyList<RuntimeBridgeHttpHeader> expected) =>
        actual.Count == expected.Count && actual.Zip(expected).All(pair => pair.First.Name == pair.Second.Name && pair.First.Value == pair.Second.Value);
    private static IReadOnlyList<RuntimeBridgeHttpHeader> ArtifactRequestHeaders(string sessionId, string reference, string etag, string? range) =>
        new[]
        {
            new RuntimeBridgeHttpHeader("Authorization", "ephemeral:authorization"),
            new RuntimeBridgeHttpHeader("X-PM365-Onboarding-Session", "ephemeral:onboarding-session"),
            new RuntimeBridgeHttpHeader("X-PM365-Onboarding-Code", "ephemeral:onboarding-code"),
            new RuntimeBridgeHttpHeader("X-PM365-Runtime-Delivery-Session", sessionId),
            new RuntimeBridgeHttpHeader("X-PM365-Runtime-Delivery-Ref", reference),
            new RuntimeBridgeHttpHeader("If-Match", etag)
        }.Concat(range is null ? [] : new[] { new RuntimeBridgeHttpHeader("Range", range) }).ToArray();
    private static IReadOnlyList<RuntimeBridgeHttpHeader> ArtifactResponseHeaders(string etag, long contentLength, string? contentRange) =>
        new[]
        {
            new RuntimeBridgeHttpHeader("Cache-Control", "private, no-store"),
            new RuntimeBridgeHttpHeader("Pragma", "no-cache"),
            new RuntimeBridgeHttpHeader("X-Content-Type-Options", "nosniff"),
            new RuntimeBridgeHttpHeader("ETag", etag),
            new RuntimeBridgeHttpHeader("Accept-Ranges", "bytes"),
            new RuntimeBridgeHttpHeader("Content-Length", contentLength.ToString(System.Globalization.CultureInfo.InvariantCulture))
        }.Concat(contentRange is null ? [] : new[] { new RuntimeBridgeHttpHeader("Content-Range", contentRange) }).ToArray();
    private static byte[] ProtectedRequestBody(PrivateRuntimeDeliveryPackageV07 package, RuntimeConfigurationProtectedSettingV2 descriptor) =>
        CanonicalizeSerialized(new
        {
            contractVersion = descriptor.Reference.ContractVersion,
            packageHash = package.PackageHash,
            targetApp = "api",
            name = descriptor.Name,
            reference = descriptor.Reference.OpaqueReference
        });
    private static byte[] ProtectedResponseBody(RuntimeBridgeProtectedLicenseResponse response)
    {
        using var value = JsonDocument.Parse(response.SignedLicenseUtf8);
        return CanonicalizeSerialized(new
        {
            contractVersion = response.ContractVersion,
            packageHash = response.PackageHash,
            targetApp = response.TargetApp,
            name = response.Name,
            value = value.RootElement
        });
    }
    private static byte[] CanonicalizeSerialized<T>(T value)
    {
        using var document = JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(value, SafeJson));
        return PrivateRuntimeCanonicalJson.Canonicalize(document.RootElement);
    }
    private static bool ValidateReceiptRequestBody(byte[] bytes, PrivateRuntimeDeliveryPackageV07 package,
        RuntimeBridgeArtifactSession session, IReadOnlyList<RuntimeBridgeVerifiedArtifact> artifacts)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes);
            if (!bytes.SequenceEqual(PrivateRuntimeCanonicalJson.Canonicalize(document.RootElement))) return false;
            var root = document.RootElement;
            var names = root.EnumerateObject().Select(item => item.Name).ToArray();
            if (!names.SequenceEqual(new[] { "artifacts", "contractVersion", "deliverySessionId", "eventId", "idempotencyKey", "installerVersion", "manifestSha256", "occurredAt", "outcome", "packageHash", "releaseId", "safeResult" }, StringComparer.Ordinal)) return false;
            if (root.GetProperty("contractVersion").GetString() != "pagemaker365.runtime-delivery-receipt.v1" ||
                root.GetProperty("deliverySessionId").GetString() != session.SessionId || root.GetProperty("packageHash").GetString() != package.PackageHash ||
                root.GetProperty("releaseId").GetString() != package.ReleaseId || root.GetProperty("manifestSha256").GetString() != package.ManifestSha256 ||
                root.GetProperty("eventId").GetString() != "synthetic-w09-verified" || root.GetProperty("idempotencyKey").GetString() != "synthetic-w09-receipt" ||
                root.GetProperty("occurredAt").GetString() != "2026-08-30T12:00:00.000Z" || root.GetProperty("installerVersion").GetString() != "0.0.0-synthetic" ||
                root.GetProperty("outcome").GetString() != "completed") return false;
            foreach (var artifact in artifacts)
            {
                var item = root.GetProperty("artifacts").GetProperty(artifact.ArtifactKind);
                if (item.GetProperty("artifactKind").GetString() != artifact.ArtifactKind || item.GetProperty("sha256").GetString() != artifact.Sha256 ||
                    item.GetProperty("sizeBytes").GetInt64() != artifact.SizeBytes || item.GetProperty("verificationOutcome").GetString() != "verified" ||
                    item.GetProperty("fullStreamCount").GetInt32() != 1 || item.GetProperty("rangeRetryCount").GetInt32() != 0 ||
                    item.GetProperty("bytesReceived").GetInt64() != artifact.SizeBytes) return false;
            }
            var safe = root.GetProperty("safeResult");
            return safe.GetProperty("code").GetString() == "runtime_artifacts_verified" && safe.GetProperty("state").GetString() == "completed";
        }
        catch { return false; }
    }
    private static bool ValidateReceiptResponseBody(byte[] bytes, RuntimeBridgeArtifactSession session, PrivateRuntimeDeliveryPackageV07 package)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes);
            if (!bytes.SequenceEqual(PrivateRuntimeCanonicalJson.Canonicalize(document.RootElement))) return false;
            var root = document.RootElement;
            if (!root.EnumerateObject().Select(item => item.Name).SequenceEqual(new[] { "created", "ok", "receipt" }, StringComparer.Ordinal) ||
                !root.GetProperty("ok").GetBoolean() || !root.GetProperty("created").GetBoolean()) return false;
            var receipt = root.GetProperty("receipt");
            return receipt.GetProperty("deliverySessionId").GetString() == session.SessionId &&
                receipt.GetProperty("packageHash").GetString() == package.PackageHash && receipt.GetProperty("releaseId").GetString() == package.ReleaseId &&
                receipt.GetProperty("eventId").GetString() == "synthetic-w09-verified" && receipt.GetProperty("occurredAt").GetString() == "2026-08-30T12:00:00.000Z" &&
                receipt.GetProperty("installerVersion").GetString() == "0.0.0-synthetic" && receipt.GetProperty("outcome").GetString() == "completed" &&
                receipt.GetProperty("createdAt").GetString() == "2026-08-30T12:00:00.000+00:00";
        }
        catch { return false; }
    }
    private static readonly IReadOnlyList<RuntimeBridgeHttpHeader> SessionRequestHeaders =
    [
        new("Authorization", "ephemeral:authorization"),
        new("X-PM365-Onboarding-Session", "ephemeral:onboarding-session"),
        new("X-PM365-Onboarding-Code", "ephemeral:onboarding-code")
    ];
    private static readonly IReadOnlyList<RuntimeBridgeHttpHeader> SessionResponseHeaders =
    [
        new("Cache-Control", "private, no-store"), new("Pragma", "no-cache"), new("X-Content-Type-Options", "nosniff"),
        new("Vary", "Authorization, X-PM365-Onboarding-Session")
    ];
    private static readonly IReadOnlyList<RuntimeBridgeHttpHeader> ReceiptRequestHeaders =
    [
        new("Authorization", "ephemeral:authorization"), new("X-PM365-Onboarding-Session", "ephemeral:onboarding-session"),
        new("X-PM365-Onboarding-Code", "ephemeral:onboarding-code"),
        new("X-PM365-Runtime-Delivery-Session", "rds_SYNTHETIC_W09_REHEARSAL_0001"),
        new("Idempotency-Key", "synthetic-w09-receipt")
    ];
    private static readonly IReadOnlyList<RuntimeBridgeHttpHeader> ReceiptResponseHeaders =
    [
        new("Cache-Control", "private, no-store"), new("Pragma", "no-cache"), new("X-Content-Type-Options", "nosniff"),
        new("Vary", "Authorization, X-PM365-Onboarding-Session, X-PM365-Runtime-Delivery-Session")
    ];
    private static readonly IReadOnlyList<RuntimeBridgeHttpHeader> ProtectedRequestHeaders =
    [
        new("Authorization", "ephemeral:authorization"), new("X-PM365-Onboarding-Session", "ephemeral:onboarding-session"),
        new("X-PM365-Onboarding-Code", "ephemeral:onboarding-code"),
        new("X-PM365-Runtime-Delivery-Session", "rds_SYNTHETIC_W09_REHEARSAL_0001")
    ];
    private static readonly IReadOnlyList<RuntimeBridgeHttpHeader> ProtectedResponseHeaders =
    [
        new("Cache-Control", "private, no-store"), new("Pragma", "no-cache"), new("X-Content-Type-Options", "nosniff"),
        new("Vary", "Authorization, X-PM365-Onboarding-Session, X-PM365-Runtime-Delivery-Session")
    ];
    private static readonly byte[] SessionRequestBody = Encoding.UTF8.GetBytes("{\"packageFile\":\"customer-install-0.7.json\"}");
    private static readonly byte[] SessionResponseBody = Encoding.UTF8.GetBytes("{\"ok\":true,\"created\":true,\"deliverySession\":{\"contractVersion\":\"pagemaker365.runtime-delivery-session.v1\",\"deliverySessionId\":\"rds_SYNTHETIC_W09_REHEARSAL_0001\",\"expiresAt\":\"2099-08-30T12:00:00.000Z\",\"artifactKinds\":[\"api\",\"portal\"],\"status\":\"active\"}}");
    private static void Fail(string code) => throw new InvalidDataException(code);
    private static readonly JsonSerializerOptions SafeJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private sealed class CursorWriteCallback(PrivateRuntimeDeliveryPackageV07 package, RuntimeConfigurationProtectedSettingV2 descriptor,
        string approvalDigest, IRuntimeBridgeProtectedWriteSink sink) : IRuntimeConfigurationProtectedSecretCallback
    {
        internal RuntimeBridgeProtectedWriteReceipt? Receipt { get; private set; }
        public void Accept(string vaultResourceId, string secretName, ReadOnlyMemory<byte> base64UrlSecretUtf8, CancellationToken cancellationToken)
        {
            var expectedContentSha256 = Sha256(base64UrlSecretUtf8.Span);
            var request = new RuntimeBridgeProtectedWriteRequest(descriptor.Name, descriptor.Mode, vaultResourceId, secretName,
                package.PackageHash, approvalDigest, base64UrlSecretUtf8);
            Receipt = ValidateReceipt(sink.Write(request, cancellationToken), package, descriptor, approvalDigest, expectedContentSha256);
        }
    }

    private sealed class EvidenceState
    {
        internal string? PackageHash { get; set; }
        internal string? ProjectionSha256 { get; set; }
        internal string? ManifestSha256 { get; set; }
        internal string? DeploymentExportId { get; set; }
        internal string? ReleaseId { get; set; }
        internal string? RuntimeVersion { get; set; }
        internal string? ArtifactIdentitySha256 { get; set; }
        internal string? HandlerResultSha256 { get; set; }
        internal List<string> PreviewSha256s { get; } = [];
        internal List<string> ApprovalSha256s { get; } = [];
        internal List<string> ReceiptIdentitySha256s { get; } = [];
        internal List<string> RecoverySemanticSha256s { get; } = [];
    }

    private sealed record CachedInvocation(string InvocationIdentity, RuntimeBridgeResult Result);
    private sealed record PreparedEntry(string RelativePath, byte[] Bytes);
    private sealed record PreparedArtifact(RuntimeBridgeVerifiedArtifact Verified, byte[] ArchiveBytes,
        IReadOnlyList<PreparedEntry> Entries, IReadOnlyList<RuntimeBridgeOwnedStageEntry> Inventory);
    private sealed record FileIdentity(uint VolumeSerialNumber, ulong FileIndex);
    private sealed class OwnedStageState(string invocationId, byte[] marker, FileIdentity rootIdentity, FileIdentity stageIdentity,
        Dictionary<string, FileIdentity> ownedPaths, Dictionary<string, bool> declaredPaths, List<SafeFileHandle> ownershipHandles)
    {
        internal string InvocationId { get; } = invocationId;
        internal byte[] Marker { get; } = marker;
        internal FileIdentity RootIdentity { get; } = rootIdentity;
        internal FileIdentity StageIdentity { get; } = stageIdentity;
        internal Dictionary<string, FileIdentity> OwnedPaths { get; } = ownedPaths;
        internal Dictionary<string, bool> DeclaredPaths { get; } = declaredPaths;
        internal List<SafeFileHandle> OwnershipHandles { get; } = ownershipHandles;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        internal uint FileAttributes;
        internal System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        internal System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        internal System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        internal uint VolumeSerialNumber;
        internal uint FileSizeHigh;
        internal uint FileSizeLow;
        internal uint NumberOfLinks;
        internal uint FileIndexHigh;
        internal uint FileIndexLow;
    }
}

internal sealed class RuntimeBridgeTerminalAmbiguityException(string code) : Exception(code);
