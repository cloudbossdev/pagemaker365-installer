using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;
using PageMaker365.Installer.Engine.Models;

namespace PageMaker365.Installer.Engine.Services;

/// <summary>
/// Internal synthetic rehearsal only. It cannot select a real transport or deployment handler.
/// </summary>
internal sealed class RuntimeDeploymentRecoveryBridge
{
    private readonly RuntimeBridgeSyntheticTestCapability capability;
    private readonly PrivateRuntimeDeliveryV07PackageService packageService;
    private readonly PackageTrustOptions packageTrust;
    private readonly string licensePublicKeyPem;
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
    private readonly SemaphoreSlim gate = new(1, 1);
    private RuntimeBridgeResult? completed;

    internal RuntimeDeploymentRecoveryBridge(
        RuntimeBridgeSyntheticTestCapability capability,
        RuntimeConfigurationCatalogV1Authority catalog,
        PackageTrustOptions packageTrust,
        string licensePublicKeyPem,
        DateTimeOffset now,
        IRuntimeBridgeArtifactTransport artifacts,
        IRuntimeBridgeProtectedLicenseTransport licenses,
        IRuntimeBridgeCursorGenerator cursorGenerator,
        IRuntimeBridgeProtectedWriteSink writes,
        IRuntimeBridgeWhatIf whatIf,
        IRuntimeBridgeApproval approvals,
        IRuntimeBridgeSyntheticHandler handler,
        IRuntimeBridgeRecovery recovery)
    {
        this.capability = capability ?? throw new ArgumentNullException(nameof(capability));
        packageService = new PrivateRuntimeDeliveryV07PackageService(catalog ?? throw new ArgumentNullException(nameof(catalog)));
        this.packageTrust = new PackageTrustOptions
        {
            TrustedPublicKeysById = new Dictionary<string, string>(packageTrust?.TrustedPublicKeysById ?? throw new ArgumentNullException(nameof(packageTrust)), StringComparer.OrdinalIgnoreCase)
        };
        this.licensePublicKeyPem = string.IsNullOrWhiteSpace(licensePublicKeyPem)
            ? throw new ArgumentException("Pinned license trust is required.", nameof(licensePublicKeyPem))
            : string.Concat(licensePublicKeyPem.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd(), "\n");
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
    }

    internal async Task<RuntimeBridgeResult> RunAsync(RuntimeBridgeInvocation invocation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        if (!invocation.Enabled)
            return Result("denied", "runtime_deployment_recovery_bridge_disabled", [], [], null, false, 0, 0, 0, 0, 0, 0);

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (completed is not null) return completed;
            completed = Execute(invocation, cancellationToken);
            return completed;
        }
        finally
        {
            gate.Release();
        }
    }

    private RuntimeBridgeResult Execute(RuntimeBridgeInvocation invocation, CancellationToken cancellationToken)
    {
        var stageRoot = Path.GetFullPath(Path.Combine(invocation.WorkspaceRoot, "pm365-synthetic-runtime-" + Guid.NewGuid().ToString("N")));
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
            Directory.CreateDirectory(stageRoot);
            trace.Add("package-authorized");

            var session = artifacts.CreateSession(package, cancellationToken);
            if (session.ExpiresAt <= now || string.IsNullOrWhiteSpace(session.SessionId)) Fail("runtime_bridge_session_invalid");
            var verified = new[]
            {
                VerifyArtifact(package, session, "api", stageRoot, cancellationToken),
                VerifyArtifact(package, session, "portal", stageRoot, cancellationToken)
            };
            var artifactReceipt = artifacts.SubmitReceipt(package, session, verified, cancellationToken);
            if (artifactReceipt.SessionId != session.SessionId || artifactReceipt.PackageHash != package.PackageHash ||
                artifactReceipt.Status != "completed" || artifactReceipt.MutationCount != 1)
                Fail("runtime_bridge_artifact_receipt_invalid");
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
            evidenceState.ApprovalSha256s.Add(firstApproval.ApprovalDigest);
            trace.Add("approval-one");

            // D-017 authority: the at-most-once license acquisition/write precedes cursor generation/write.
            licenseAcquisitions++;
            var licenseDescriptor = pending.Single(item => item.Name == "API_LICENSE_SIGNED_PAYLOAD");
            var response = licenses.AcquireOnce(package, session, licenseDescriptor, cancellationToken);
            try
            {
                ValidateLicenseResponse(package, licenseDescriptor, response);
                ValidateSignedLicense(package, response.SignedLicenseUtf8);
                var licenseDigest = Sha256(response.SignedLicenseUtf8);
                licenseReceipt = ValidateReceipt(writes.Write(new RuntimeBridgeProtectedWriteRequest(
                    licenseDescriptor.Name, licenseDescriptor.Mode, licenseDescriptor.Reference.VaultResourceId,
                    licenseDescriptor.Reference.SecretName, package.PackageHash, firstApproval.ApprovalDigest,
                    response.SignedLicenseUtf8), cancellationToken), package, licenseDescriptor, firstApproval.ApprovalDigest, licenseDigest);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(response.SignedLicenseUtf8);
            }
            trace.Add("license-written-first");
            evidenceState.ReceiptIdentitySha256s.Add(ReceiptIdentity(licenseReceipt));

            var cursorDescriptor = pending.Single(item => item.Name == "API_IMAGE_ASSET_CURSOR_SECRET");
            var cursorCallback = new CursorWriteCallback(package, cursorDescriptor, firstApproval.ApprovalDigest, writes);
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
                "final", package.PackageHash, finalInput.InputSha256, ArtifactIdentity(verified), firstApproval.ApprovalDigest, receipts);
            var secondPreview = ValidatePreview(whatIf.Preview(secondRequest, cancellationToken), secondRequest);
            whatIfCount++;
            evidenceState.PreviewSha256s.Add(secondPreview.PreviewSha256);
            var secondChallenge = Challenge("final", package, finalInput.InputSha256, secondPreview, verified, provisional.RecoveryPlanSha256, firstApproval.ApprovalDigest, receipts);
            var secondApproval = ValidateApproval(approvals.Approve(secondChallenge, cancellationToken), secondChallenge);
            approvalCount++;
            evidenceState.ApprovalSha256s.Add(secondApproval.ApprovalDigest);
            if (secondApproval.ApprovalId == firstApproval.ApprovalId || secondApproval.ApprovalDigest == firstApproval.ApprovalDigest)
                Fail("runtime_bridge_approval_reuse");

            var simulation = handler.Simulate(new RuntimeBridgeSimulationRequest(
                package.PackageHash, finalInput.InputSha256, secondPreview.PreviewSha256,
                secondApproval.ApprovalDigest, verified, AuthorizesDeployment: false), cancellationToken);
            handlerCount++;
            if (simulation.Status != "simulated" || simulation.ResourceCount != 0 || simulation.WriteCount != 0 || simulation.DeploymentCount != 0)
                Fail("runtime_bridge_handler_outcome");
            simulationAccepted = true;
            trace.Add("handler-simulated");
            var cleaned = TryCleanupStage(stageRoot);
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
                var cleanedAfterSimulation = TryCleanupStage(stageRoot);
                return Result(cleanedAfterSimulation ? "failed" : "cleanup-required",
                    cleanedAfterSimulation ? "runtime_deployment_recovery_rehearsal_failed" : "runtime_deployment_recovery_stage_cleanup_required",
                    trace, [], null, cleanedAfterSimulation, licenseAcquisitions,
                    (licenseReceipt is null ? 0 : 1) + (cursorReceipt is null ? 0 : 1), whatIfCount, approvalCount, handlerCount, 0,
                    evidenceState: evidenceState);
            }
            var recoveryFailed = false;
            // Reverse of the authorized write order: cursor, then license.
            foreach (var receipt in new[] { cursorReceipt, licenseReceipt }.Where(item => item is not null).Cast<RuntimeBridgeProtectedWriteReceipt>())
            {
                try
                {
                    var result = recovery.Recover(receipt, CancellationToken.None);
                    if (result.ReceiptId != receipt.ReceiptId || result.Status != "recovered" || result.RecoveryCount != 1)
                        throw new InvalidDataException("runtime_bridge_recovery_invalid");
                    recoveries.Add(result);
                }
                catch
                {
                    recoveryFailed = true;
                }
            }
            var cleaned = TryCleanupStage(stageRoot);
            var ambiguity = error is RuntimeBridgeTerminalAmbiguityException;
            var ownedReceipts = recoveryFailed
                ? new[] { cursorReceipt, licenseReceipt }.Where(item => item is not null).Cast<RuntimeBridgeProtectedWriteReceipt>().ToArray()
                : [];
            return Result(recoveryFailed ? "recovery-required" : !cleaned ? "cleanup-required" : "failed",
                recoveryFailed ? "runtime_deployment_recovery_required" : !cleaned ? "runtime_deployment_recovery_stage_cleanup_required" :
                ambiguity ? "runtime_deployment_recovery_terminal_ambiguity" : "runtime_deployment_recovery_rehearsal_failed",
                trace, recoveries, null, cleaned, licenseAcquisitions,
                (licenseReceipt is null ? 0 : 1) + (cursorReceipt is null ? 0 : 1), whatIfCount, approvalCount, handlerCount, recoveries.Count,
                ownedReceipts, evidenceState);
        }
    }

    private RuntimeBridgeVerifiedArtifact VerifyArtifact(PrivateRuntimeDeliveryPackageV07 package, RuntimeBridgeArtifactSession session, string kind, string stageRoot, CancellationToken cancellationToken)
    {
        var expected = package.Artifact(kind);
        var full = artifacts.Acquire(package, session, kind, range: false, cancellationToken);
        var range = artifacts.Acquire(package, session, kind, range: true, cancellationToken);
        if (!full.NoRedirect || full.IsRange || !range.IsRange || full.CacheControl != "private, no-store" || range.CacheControl != "private, no-store" ||
            full.Pragma != "no-cache" || range.Pragma != "no-cache" || full.ContentTypeOptions != "nosniff" || range.ContentTypeOptions != "nosniff" ||
            full.ArtifactKind != kind || range.ArtifactKind != kind || full.TotalLength != expected.SizeBytes || full.Body.LongLength != expected.SizeBytes ||
            full.Sha256 != expected.Sha256 || Sha256(full.Body) != expected.Sha256 || range.TotalLength != expected.SizeBytes ||
            range.Offset < 0 || range.Offset + range.Body.Length > full.Body.Length ||
            !range.Body.SequenceEqual(full.Body.AsSpan((int)range.Offset, range.Body.Length).ToArray()))
            Fail("runtime_bridge_artifact_invalid");

        var zipPath = Path.Combine(stageRoot, expected.FileName);
        File.WriteAllBytes(zipPath, full.Body);
        var extractRoot = Path.Combine(stageRoot, kind);
        Directory.CreateDirectory(extractRoot);
        var entries = 0;
        using (var archive = ZipFile.OpenRead(zipPath))
        {
            long expandedBytes = 0;
            foreach (var entry in archive.Entries)
            {
                var unixMode = (entry.ExternalAttributes >> 16) & 0xF000;
                expandedBytes = checked(expandedBytes + entry.Length);
                if (entries++ >= 128 || entry.Length > 4 * 1024 * 1024 || expandedBytes > 8 * 1024 * 1024 || unixMode == 0xA000)
                    Fail("runtime_bridge_archive_invalid");
                var destination = Path.GetFullPath(Path.Combine(extractRoot, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
                if (!destination.StartsWith(extractRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(entry.Name))
                    Fail("runtime_bridge_archive_invalid");
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                entry.ExtractToFile(destination, overwrite: false);
            }
        }
        var tree = string.Join("\n", Directory.GetFiles(extractRoot, "*", SearchOption.AllDirectories)
            .Select(path => (Path.GetRelativePath(extractRoot, path).Replace('\\', '/'), Sha256(File.ReadAllBytes(path))))
            .OrderBy(item => item.Item1, StringComparer.Ordinal).Select(item => $"{item.Item1}:{item.Item2}")) + "\n";
        var provenancePath = Path.Combine(extractRoot, ".pm365", "provenance.json");
        if (!File.Exists(provenancePath)) Fail("runtime_bridge_artifact_provenance");
        using (var provenance = JsonDocument.Parse(File.ReadAllBytes(provenancePath)))
        {
            var value = provenance.RootElement;
            if (value.GetProperty("schemaVersion").GetString() != "pagemaker365.runtime-provenance.v1" ||
                value.GetProperty("product").GetString() != "PageMaker365" || value.GetProperty("artifactKind").GetString() != kind ||
                value.GetProperty("releaseId").GetString() != package.ReleaseId || value.GetProperty("runtimeVersion").GetString() != package.RuntimeVersion ||
                value.GetProperty("sourceRepository").GetString() != "cloudbossdev/spo-ui" || value.GetProperty("sourceCommit").GetString() != package.SourceCommit ||
                value.GetProperty("startupCommand").GetString() != expected.StartupCommand)
                Fail("runtime_bridge_artifact_provenance");
        }
        return new RuntimeBridgeVerifiedArtifact(kind, expected.FileName, expected.Sha256, expected.SizeBytes, extractRoot, Sha256(Encoding.UTF8.GetBytes(tree)), entries);
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
            response.Vary != "Authorization, X-PM365-Onboarding-Session, X-PM365-Runtime-Delivery-Session" || !response.NoRedirect || response.SignedLicenseUtf8.Length == 0)
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
            payload.GetProperty("customerId").GetString() != package.CustomerId || payload.GetProperty("installationId").GetString() != package.InstallationId ||
            payload.GetProperty("environmentId").GetString() != package.EnvironmentId || DateTimeOffset.Parse(payload.GetProperty("validFrom").GetString()!) > now ||
            DateTimeOffset.Parse(payload.GetProperty("validTo").GetString()!) <= now)
            Fail("runtime_bridge_license_binding");
        var signature = root.GetProperty("signature");
        if (!signature.EnumerateObject().Select(item => item.Name).SequenceEqual(new[] { "alg", "kid", "value" }, StringComparer.Ordinal) ||
            signature.GetProperty("alg").GetString() != "Ed25519" || string.IsNullOrWhiteSpace(signature.GetProperty("kid").GetString()))
            Fail("runtime_bridge_license_signature");
        var publicKey = (Ed25519PublicKeyParameters)PublicKeyFactory.CreateKey(Convert.FromBase64String(string.Concat(licensePublicKeyPem.Split('\n').Where(line => !line.StartsWith("---", StringComparison.Ordinal) && line.Length > 0))));
        var signatureText = signature.GetProperty("value").GetString()!;
        var signatureBytes = DecodeBase64Url(signatureText);
        if (EncodeBase64Url(signatureBytes) != signatureText) Fail("runtime_bridge_license_signature");
        var canonical = PrivateRuntimeCanonicalJson.Canonicalize(payload);
        var verifier = new Ed25519Signer(); verifier.Init(false, publicKey); verifier.BlockUpdate(canonical, 0, canonical.Length);
        if (!verifier.VerifySignature(signatureBytes)) Fail("runtime_bridge_license_signature");
    }

    private static RuntimeBridgeProtectedWriteReceipt ValidateReceipt(RuntimeBridgeProtectedWriteReceipt receipt, PrivateRuntimeDeliveryPackageV07 package,
        RuntimeConfigurationProtectedSettingV2 descriptor, string approvalDigest, string expectedContentSha256)
    {
        if (receipt.Name != descriptor.Name || receipt.Mode != descriptor.Mode || receipt.VaultResourceId != descriptor.Reference.VaultResourceId ||
            receipt.SecretName != descriptor.Reference.SecretName || receipt.PackageHash != package.PackageHash || receipt.ApprovalDigest != approvalDigest ||
            receipt.ContentSha256 != expectedContentSha256 || receipt.Outcome != "written" || receipt.WriteCount != 1 ||
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
        var pinned = Encoding.UTF8.GetBytes(licensePublicKeyPem);
        if (projected.Length != pinned.Length || !CryptographicOperations.FixedTimeEquals(projected, pinned))
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
            previewSha256s = evidenceState?.PreviewSha256s ?? [],
            approvalSha256s = evidenceState?.ApprovalSha256s ?? [],
            receiptIdentitySha256s = evidenceState?.ReceiptIdentitySha256s ?? [],
            finalInputSha256 = final?.InputSha256,
            recoveryReceiptIds = recoveries.Select(item => item.ReceiptId),
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
        $"{receipt.ReceiptId}\n{receipt.Name}\n{receipt.Mode}\n{receipt.VaultResourceId}\n{receipt.SecretName}\n{receipt.SecretVersion}\n{receipt.KeyVaultReference}\n{receipt.ContentSha256}\n{receipt.PackageHash}\n{receipt.ApprovalDigest}\n{receipt.Outcome}\n{receipt.WriteCount}\n"));
    private static bool TryCleanupStage(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); return !Directory.Exists(path); }
        catch { return false; }
    }
    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static byte[] DecodeBase64Url(string value) => Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/') + ((value.Length % 4) switch { 2 => "==", 3 => "=", 0 => "", _ => throw new InvalidDataException("runtime_bridge_license_signature") }));
    private static string EncodeBase64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static void Fail(string code) => throw new InvalidDataException(code);
    private static readonly JsonSerializerOptions SafeJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private sealed class CursorWriteCallback(PrivateRuntimeDeliveryPackageV07 package, RuntimeConfigurationProtectedSettingV2 descriptor,
        string approvalDigest, IRuntimeBridgeProtectedWriteSink sink) : IRuntimeConfigurationProtectedSecretCallback
    {
        internal RuntimeBridgeProtectedWriteReceipt? Receipt { get; private set; }
        public void Accept(string vaultResourceId, string secretName, ReadOnlyMemory<byte> base64UrlSecretUtf8, CancellationToken cancellationToken)
        {
            var request = new RuntimeBridgeProtectedWriteRequest(descriptor.Name, descriptor.Mode, vaultResourceId, secretName,
                package.PackageHash, approvalDigest, base64UrlSecretUtf8);
            Receipt = ValidateReceipt(sink.Write(request, cancellationToken), package, descriptor, approvalDigest, Sha256(base64UrlSecretUtf8.ToArray()));
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
        internal List<string> PreviewSha256s { get; } = [];
        internal List<string> ApprovalSha256s { get; } = [];
        internal List<string> ReceiptIdentitySha256s { get; } = [];
    }
}

internal sealed class RuntimeBridgeTerminalAmbiguityException(string code) : Exception(code);
