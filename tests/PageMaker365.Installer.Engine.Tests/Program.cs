using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;
using PageMaker365.Installer.Engine.Models;
using PageMaker365.Installer.Engine.PowerShell;
using PageMaker365.Installer.Engine.Services;

namespace PageMaker365.Installer.Engine.Tests;

internal static class Program
{
    public static async Task<int> Main()
    {
        var tests = new (string Name, Func<Task> Run)[]
        {
            ("ConnectAsync sends expected session request and headers", ConnectAsyncSendsExpectedSessionRequestAndHeaders),
            ("SubmitDiscoveryAsync sends install-readiness discovery payload", SubmitDiscoveryAsyncSendsInstallReadinessDiscoveryPayload),
            ("GetOnboardingStatusAsync sends only sanitized package context", GetOnboardingStatusAsyncSendsOnlySanitizedPackageContext),
            ("GetOnboardingStatusAsync accepts unknown readiness without making it downloadable", GetOnboardingStatusAsyncAcceptsUnknownReadinessWithoutMakingItDownloadable),
            ("GetOnboardingStatusAsync carries missing field details", GetOnboardingStatusAsyncCarriesMissingFieldDetails),
            ("SubmitEvidenceAsync sends hardened callback contract", SubmitEvidenceAsyncSendsHardenedCallbackContract),
            ("SubmitEvidenceAsync retries transient failure with stable identity", SubmitEvidenceAsyncRetriesTransientFailureWithStableIdentity),
            ("SubmitEvidenceAsync does not retry unauthorized response", SubmitEvidenceAsyncDoesNotRetryUnauthorizedResponse),
            ("DownloadPackageAsync saves portal package to support bundle", DownloadPackageAsyncSavesPortalPackageToSupportBundle),
            ("DownloadPackageAsync verifies signed package with advertised JWKS", DownloadPackageAsyncVerifiesSignedPackageWithAdvertisedJwks),
            ("DownloadPackageAsync redownloads previously downloaded portal package", DownloadPackageAsyncRedownloadsPreviouslyDownloadedPortalPackage),
            ("PackageTrustKeyResolver rejects untrusted JWKS host", PackageTrustKeyResolverRejectsUntrustedJwksHost),
            ("DownloadPackageAsync sanitizes unsafe content disposition filename", DownloadPackageAsyncSanitizesUnsafeContentDispositionFilename),
            ("DownloadPackageAsync ignores external package download URL", DownloadPackageAsyncIgnoresExternalPackageDownloadUrl),
            ("OnboardingSessionService rejects bootstrap missing required runtime fields", OnboardingSessionServiceRejectsBootstrapMissingRequiredRuntimeFields),
            ("OnboardingSessionService rejects expired bootstrap", OnboardingSessionServiceRejectsExpiredBootstrap),
            ("OnboardingSessionService rejects untrusted bootstrap endpoints", OnboardingSessionServiceRejectsUntrustedBootstrapEndpoints),
            ("Graph device code requests only approved read scopes", GraphDeviceCodeRequestsOnlyApprovedReadScopes),
            ("OnboardingSessionService validates sample bootstrap contract", OnboardingSessionServiceValidatesSampleBootstrapContract),
            ("AuthenticationContextValidator accepts matching Azure context", AuthenticationContextValidatorAcceptsMatchingAzureContext),
            ("AuthenticationContextValidator rejects warning-only Azure sign-in", AuthenticationContextValidatorRejectsWarningOnlyAzureSignIn),
            ("AuthenticationContextValidator rejects wrong Azure tenant", AuthenticationContextValidatorRejectsWrongAzureTenant),
            ("AuthenticationContextValidator rejects wrong Azure subscription", AuthenticationContextValidatorRejectsWrongAzureSubscription),
            ("AuthenticationContextValidator accepts valid Graph context", AuthenticationContextValidatorAcceptsValidGraphContext),
            ("AuthenticationContextValidator rejects wrong Graph tenant", AuthenticationContextValidatorRejectsWrongGraphTenant),
            ("AuthenticationContextValidator rejects expired Graph token", AuthenticationContextValidatorRejectsExpiredGraphToken),
            ("AuthenticationContextValidator rejects missing Graph scope", AuthenticationContextValidatorRejectsMissingGraphScope),
            ("ConnectAsync rejects invalid portal response", ConnectAsyncRejectsInvalidPortalResponse),
            ("GetOnboardingStatusAsync rejects status missing readiness status", GetOnboardingStatusAsyncRejectsStatusMissingReadinessStatus),
            ("GetOnboardingStatusAsync rejects ready status without package URL", GetOnboardingStatusAsyncRejectsReadyStatusWithoutPackageUrl),
            ("GetOnboardingStatusAsync rejects status session mismatch", GetOnboardingStatusAsyncRejectsStatusSessionMismatch),
            ("GetOnboardingStatusAsync validates sample status contract", GetOnboardingStatusAsyncValidatesSampleStatusContract),
            ("ConnectAsync surfaces portal API error details", ConnectAsyncSurfacesPortalApiErrorDetails),
            ("DownloadPackageAsync rejects non-json package response", DownloadPackageAsyncRejectsNonJsonPackageResponse),
            ("DownloadPackageAsync rejects invalid generated package", DownloadPackageAsyncRejectsInvalidGeneratedPackage),
            ("DownloadPackageAsync rejects generated package missing required contract sections", DownloadPackageAsyncRejectsGeneratedPackageMissingRequiredContractSections),
            ("DownloadPackageAsync accepts package bound to active provenance", DownloadPackageAsyncAcceptsPackageBoundToActiveProvenance),
            ("DownloadPackageAsync rejects package with wrong onboarding session", DownloadPackageAsyncRejectsPackageWithWrongOnboardingSession),
            ("DownloadPackageAsync rejects package with wrong tenant", DownloadPackageAsyncRejectsPackageWithWrongTenant),
            ("DownloadPackageAsync rejects package with wrong discovery ID", DownloadPackageAsyncRejectsPackageWithWrongDiscoveryId),
            ("DownloadPackageAsync rejects package missing deployment export ID", DownloadPackageAsyncRejectsPackageMissingDeploymentExportId),
            ("Live portal install package download loads with CustomerConfigService", LivePortalInstallPackageDownloadLoadsWithCustomerConfigService),
            ("ConnectAsync falls back to mock when portal fails", ConnectAsyncFallsBackToMockWhenPortalFails),
            ("ConnectAsync does not fall back to mock for invalid portal response", ConnectAsyncDoesNotFallBackToMockForInvalidPortalResponse),
            ("CustomerConfigService verifies matching package hash", CustomerConfigServiceVerifiesMatchingPackageHash),
            ("CustomerConfigService rejects package hash mismatch", CustomerConfigServiceRejectsPackageHashMismatch),
            ("CustomerConfigService enforces signed-required trust mode", CustomerConfigServiceEnforcesSignedRequiredTrustMode),
            ("CustomerConfigService verifies Ed25519 signed package", CustomerConfigServiceVerifiesEd25519SignedPackage),
            ("CustomerConfigService rejects signed package without trusted key", CustomerConfigServiceRejectsSignedPackageWithoutTrustedKey),
            ("CustomerConfigService rejects invalid package signature", CustomerConfigServiceRejectsInvalidPackageSignature),
            ("CustomerConfigService rejects unsupported signature algorithm", CustomerConfigServiceRejectsUnsupportedSignatureAlgorithm),
            ("CustomerConfigService validates sample package contract", CustomerConfigServiceValidatesSamplePackageContract),
            ("CustomerConfigService rejects package missing required contract fields", CustomerConfigServiceRejectsPackageMissingRequiredContractFields),
            ("CustomerConfigService rejects raw secret containers", CustomerConfigServiceRejectsRawSecretContainers),
            ("OptionsService loads file and environment overrides", OptionsServiceLoadsFileAndEnvironmentOverrides),
            ("InstallerStateStore saves and loads active state", InstallerStateStoreSavesAndLoadsActiveState),
            ("InstallerStateStore ignores completed state for resume", InstallerStateStoreIgnoresCompletedStateForResume),
            ("DeploymentApprovalManifestService writes hash without raw confirmation", DeploymentApprovalManifestServiceWritesHashWithoutRawConfirmation),
            ("FinalEvidenceService copies approval and Azure artifacts", FinalEvidenceServiceCopiesApprovalAndAzureArtifacts),
            ("InstallerEngine parses JSON result from mixed PowerShell output", InstallerEngineParsesJsonResultFromMixedPowerShellOutput),
            ("InstallerEngine passes Graph token and deployment artifact to validation", InstallerEnginePassesGraphTokenToValidationProcess),
            ("PowerShellProcessRunner returns failed result on timeout", PowerShellProcessRunnerReturnsFailedResultOnTimeout),
            ("PowerShellProcessRunner returns failed result on cancellation", PowerShellProcessRunnerReturnsFailedResultOnCancellation),
            ("PowerShellProcessRunner reports live output lines", PowerShellProcessRunnerReportsLiveOutputLines),
            ("PowerShellProcessRunner passes scoped environment variables", PowerShellProcessRunnerPassesScopedEnvironmentVariables),
            ("AzureDiscoveryService returns package fallback when module is missing", AzureDiscoveryServiceReturnsPackageFallbackWhenModuleIsMissing),
            ("AzureDiscoveryService maps Azure result into tenant discovery", AzureDiscoveryServiceMapsAzureResultIntoTenantDiscovery),
            ("GraphDiscoveryService returns package fallback when module is missing", GraphDiscoveryServiceReturnsPackageFallbackWhenModuleIsMissing),
            ("GraphDiscoveryService maps Graph result into tenant discovery", GraphDiscoveryServiceMapsGraphResultIntoTenantDiscovery)
        };

        var failed = 0;
        foreach (var test in tests)
        {
            try
            {
                await test.Run();
                Console.WriteLine($"PASS {test.Name}");
            }
            catch (Exception exception)
            {
                failed++;
                Console.Error.WriteLine($"FAIL {test.Name}");
                Console.Error.WriteLine(exception);
            }
        }

        Console.WriteLine($"{tests.Length - failed}/{tests.Length} onboarding API contract tests passed.");
        return failed == 0 ? 0 : 1;
    }

    private static async Task ConnectAsyncSendsExpectedSessionRequestAndHeaders()
    {
        using var environment = new EnvironmentVariableScope("PM365_TEST_ONBOARDING_KEY", "test-token");
        var handler = new RecordingHttpMessageHandler(_ => JsonResponse("""
            {
              "status": "Connected",
              "sessionId": "onb_test_001",
              "correlationId": "corr-connect-001",
              "message": "Connected"
            }
            """));
        var client = CreatePortalClient(handler, apiKeyEnvironmentVariable: environment.Name);

        var response = await client.ConnectAsync(CreateSession());

        AssertEx.Equal("Connected", response.Status);
        AssertEx.Equal("corr-connect-001", response.CorrelationId);
        AssertEx.Equal(HttpMethod.Post, handler.Requests[0].Method);
        AssertEx.Equal("https://localhost:5443/api/onboarding/installer/connect", handler.Requests[0].RequestUri?.ToString());
        AssertEx.Equal("Bearer", handler.Requests[0].Authorization?.Scheme);
        AssertEx.Equal("test-token", handler.Requests[0].Authorization?.Parameter);
        AssertEx.Contains(handler.HeaderValues("X-PM365-Onboarding-Session"), "onb_test_001");
        AssertEx.Contains(handler.HeaderValues("X-PM365-Onboarding-Code"), "TEST-CODE-001");

        using var body = JsonDocument.Parse(handler.RequestBodies[0]);
        AssertJsonString(body, "sessionId", "onb_test_001");
        AssertJsonString(body, "oneTimeCode", "TEST-CODE-001");
        AssertJsonString(body, "requestedBy", "owner@example.test");
        AssertJsonString(body, "customerName", "Example Customer");
    }

    private static async Task SubmitEvidenceAsyncSendsHardenedCallbackContract()
    {
        var evidence = CreateInstallerEvidence();
        var handler = new RecordingHttpMessageHandler(_ => JsonResponse($$"""
            {
              "contractVersion": "0.2",
              "status": "Accepted",
              "sessionId": "onb_test_001",
              "eventId": "{{evidence.EventId}}",
              "eventType": "{{evidence.EventType}}",
              "installAttemptId": "{{evidence.InstallAttemptId}}",
              "sequence": {{evidence.Sequence}},
              "lifecycleStatus": "provisioning",
              "outcome": "passed",
              "installStatus": "provisioning",
              "correlationId": "corr-evidence-001",
              "receivedAt": "2026-07-11T18:00:00Z"
            }
            """));
        var client = CreatePortalClient(handler);
        var idempotencyKey = $"{evidence.InstallAttemptId}:{evidence.Sequence}:{evidence.EventId}";

        var receipt = await client.SubmitEvidenceAsync(CreateSession(), evidence, idempotencyKey);

        AssertEx.Equal("Accepted", receipt.Status);
        AssertEx.Equal("/api/onboarding/installer/evidence", handler.Requests[0].RequestUri!.AbsolutePath);
        AssertEx.Contains(handler.HeaderValues("Idempotency-Key"), idempotencyKey);
        AssertEx.Contains(handler.HeaderValues("X-PM365-Onboarding-Session"), "onb_test_001");
        AssertEx.Contains(handler.HeaderValues("X-PM365-Onboarding-Code"), "TEST-CODE-001");
        using var body = JsonDocument.Parse(handler.RequestBodies[0]);
        AssertEx.Equal(evidence.EventId, body.RootElement.GetProperty("eventId").GetString());
        AssertEx.Equal(evidence.InstallAttemptId, body.RootElement.GetProperty("installAttemptId").GetString());
        AssertEx.Equal(1, body.RootElement.GetProperty("sequence").GetInt32());
        AssertEx.False(handler.RequestBodies[0].Contains("TEST-CODE-001", StringComparison.Ordinal));
    }

    private static async Task SubmitEvidenceAsyncRetriesTransientFailureWithStableIdentity()
    {
        var evidence = CreateInstallerEvidence();
        var calls = 0;
        var handler = new RecordingHttpMessageHandler(_ =>
        {
            calls++;
            return calls == 1
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                {
                    Content = new StringContent("{\"error\":{\"code\":\"temporarily_unavailable\"}}")
                }
                : JsonResponse($$"""
                    {
                      "status":"Accepted",
                      "sessionId":"onb_test_001",
                      "eventId":"{{evidence.EventId}}",
                      "eventType":"{{evidence.EventType}}",
                      "installAttemptId":"{{evidence.InstallAttemptId}}",
                      "sequence":1,
                      "correlationId":"corr-retry-001"
                    }
                    """);
        });
        var client = CreatePortalClient(handler);
        var idempotencyKey = $"{evidence.InstallAttemptId}:1:{evidence.EventId}";

        await client.SubmitEvidenceAsync(CreateSession(), evidence, idempotencyKey);

        AssertEx.Equal(2, handler.Requests.Count);
        AssertEx.Equal(handler.RequestBodies[0], handler.RequestBodies[1]);
        AssertEx.Contains(handler.Requests[1].Headers["Idempotency-Key"], idempotencyKey);
    }

    private static async Task SubmitEvidenceAsyncDoesNotRetryUnauthorizedResponse()
    {
        var evidence = CreateInstallerEvidence();
        var handler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("{\"error\":{\"code\":\"unauthorized\"}}")
        });
        var client = CreatePortalClient(handler);

        var exception = await AssertEx.ThrowsAsync<OnboardingApiException>(() =>
            client.SubmitEvidenceAsync(CreateSession(), evidence, "attempt-evidence-001:1:event-evidence-001"));

        AssertEx.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
        AssertEx.Equal(1, handler.Requests.Count);
    }

    private static async Task SubmitDiscoveryAsyncSendsInstallReadinessDiscoveryPayload()
    {
        var handler = new RecordingHttpMessageHandler(_ => JsonResponse("""
            {
              "status": "Accepted",
              "sessionId": "onb_test_001",
              "discoveryId": "disc_test_001",
              "correlationId": "corr-discovery-001",
              "portalRecordUrl": "https://localhost:5444/admin/onboarding/onb_test_001",
              "message": "Accepted"
            }
            """));
        var client = CreatePortalClient(handler);

        var response = await client.SubmitDiscoveryAsync(CreateSession(), CreateDiscovery());

        AssertEx.Equal("Accepted", response.Status);
        AssertEx.Equal("disc_test_001", response.DiscoveryId);
        AssertEx.Equal("https://localhost:5443/api/onboarding/installer/discovery", handler.Requests[0].RequestUri?.ToString());

        using var body = JsonDocument.Parse(handler.RequestBodies[0]);
        AssertJsonString(body, "sessionId", "onb_test_001");
        AssertJsonString(body.RootElement.GetProperty("discovery"), "discoveryId", "disc_test_001");
        AssertJsonString(body.RootElement.GetProperty("discovery"), "onboardingSessionId", "onb_test_001");
        AssertJsonString(body.RootElement.GetProperty("discovery"), "dataPolicy", "InstallReadinessOnly");
        AssertJsonString(body.RootElement.GetProperty("discovery").GetProperty("customer"), "tenantId", "tenant-001");
    }

    private static async Task GetOnboardingStatusAsyncSendsOnlySanitizedPackageContext()
    {
        var handler = new RecordingHttpMessageHandler(_ => JsonResponse("""
            {
              "contractVersion": "0.1",
              "sessionId": "onb_test_001",
              "customerName": "Example Customer",
              "status": "Ready",
              "portalRecordUrl": "https://localhost:5444/admin/onboarding/onb_test_001",
              "correlationId": "corr-status-001",
              "message": "Ready",
              "missingFields": [],
              "packageReadiness": {
                "status": "Ready",
                "packageVersion": "0.2-test",
                "packageDownloadUrl": "https://localhost:5443/api/onboarding/installer/onb_test_001/install-package",
                "message": "Ready for download"
              }
            }
            """));
        var client = CreatePortalClient(handler);

        var status = await client.GetOnboardingStatusAsync(CreateSession(), CreateDiscovery(), CreateConfig());

        AssertEx.Equal("Ready", status.Status);
        AssertEx.Equal("Ready", status.PackageReadiness.Status);
        AssertEx.Equal("https://localhost:5443/api/onboarding/installer/status", handler.Requests[0].RequestUri?.ToString());

        using var body = JsonDocument.Parse(handler.RequestBodies[0]);
        var loadedPackage = body.RootElement.GetProperty("loadedPackage");
        AssertJsonString(loadedPackage, "tenantId", "tenant-001");
        AssertJsonString(loadedPackage, "tenantName", "Example Customer");
        AssertJsonString(loadedPackage, "azureSubscriptionId", "sub-001");
        AssertJsonString(loadedPackage, "resourceGroupName", "rg-pm365-example");
        AssertJsonString(loadedPackage, "sharePointSiteUrl", "https://example.sharepoint.com/sites/intranet");
        AssertJsonString(loadedPackage, "sharePointTenantHostname", "example.sharepoint.com");
        AssertJsonString(loadedPackage, "deploymentExportId", "export-001");
        AssertJsonString(loadedPackage, "packageHash", "");

        var requestJson = handler.RequestBodies[0];
        AssertEx.False(requestJson.Contains("secrets", StringComparison.OrdinalIgnoreCase), requestJson);
        AssertEx.False(requestJson.Contains("requiredSecretNames", StringComparison.OrdinalIgnoreCase), requestJson);
        AssertEx.False(requestJson.Contains("promptForSecrets", StringComparison.OrdinalIgnoreCase), requestJson);
        AssertEx.False(requestJson.Contains("Runtime API secret", StringComparison.OrdinalIgnoreCase), requestJson);
    }

    private static async Task DownloadPackageAsyncSavesPortalPackageToSupportBundle()
    {
        var packageJson = CustomerConfigService.ToJson(CreateConfig());
        var handler = new RecordingHttpMessageHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(packageJson, Encoding.UTF8, "application/json")
            };
            response.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
            {
                FileName = "example.customer.install.json"
            };
            response.Headers.Add("X-Correlation-ID", "corr-package-001");
            return response;
        });
        var client = CreatePortalClient(handler);
        var workspaceRoot = CreateTempDirectory();

        try
        {
            var result = await client.DownloadPackageAsync(
                CreateSession(),
                CreateReadyReadiness(),
                workspaceRoot);

            AssertEx.Equal("Downloaded", result.Status);
            AssertEx.Equal("0.2-test", result.PackageVersion);
            AssertEx.Equal("corr-package-001", result.CorrelationId);
            AssertEx.Equal(HttpMethod.Get, handler.Requests[0].Method);
            AssertEx.Equal("https://localhost:5443/custom/download", handler.Requests[0].RequestUri?.ToString());
            AssertEx.Contains(handler.HeaderValues("X-PM365-Onboarding-Session"), "onb_test_001");
            AssertEx.Contains(handler.HeaderValues("X-PM365-Onboarding-Code"), "TEST-CODE-001");
            AssertEx.True(File.Exists(result.PackagePath), result.PackagePath);
            AssertEx.Equal(packageJson, await File.ReadAllTextAsync(result.PackagePath));
            AssertEx.StringContains(result.PackagePath, Path.Combine("support-bundle", "onboarding", "onb_test_001", "generated-package"));
        }
        finally
        {
            Directory.Delete(workspaceRoot, recursive: true);
        }
    }

    private static async Task DownloadPackageAsyncVerifiesSignedPackageWithAdvertisedJwks()
    {
        using var trustedKeysJson = new EnvironmentVariableScope("PAGEMAKER365_INSTALLER_TRUSTED_PUBLIC_KEYS_JSON", null);
        using var trustedKeyId = new EnvironmentVariableScope("PAGEMAKER365_INSTALL_PACKAGE_PUBLIC_KEY_ID", null);
        using var trustedKeyPem = new EnvironmentVariableScope("PAGEMAKER365_INSTALL_PACKAGE_PUBLIC_KEY_PEM", null);
        var signedPackage = CreateSignedPackage(config =>
        {
            config.ControlPlane.JwksUrl = "https://api.pagemaker365.com/.well-known/pagemaker365-license-jwks.json";
        });
        var handler = new RecordingHttpMessageHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/.well-known/pagemaker365-license-jwks.json")
            {
                return JsonResponse(CreateJwksJson(signedPackage.PublicKeyId, signedPackage.PublicKeyJwkX));
            }

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(signedPackage.Json, Encoding.UTF8, "application/json")
            };
            response.Headers.Add("X-Correlation-ID", "corr-package-jwks");
            return response;
        });
        var client = CreatePortalClient(handler);
        var workspaceRoot = CreateTempDirectory();

        try
        {
            var result = await client.DownloadPackageAsync(
                CreateSession(),
                CreateReadyReadiness(),
                workspaceRoot,
                CreateDiscovery());

            AssertEx.Equal("Downloaded", result.Status);
            AssertEx.Equal("corr-package-jwks", result.CorrelationId);
            AssertEx.Equal(2, handler.Requests.Count);
            AssertEx.Equal("https://localhost:5443/custom/download", handler.Requests[0].RequestUri?.ToString());
            AssertEx.Equal(
                "https://api.pagemaker365.com/.well-known/pagemaker365-license-jwks.json",
                handler.Requests[1].RequestUri?.ToString());
            AssertEx.True(File.Exists(result.PackagePath), result.PackagePath);
        }
        finally
        {
            Directory.Delete(workspaceRoot, recursive: true);
        }
    }

    private static async Task DownloadPackageAsyncRedownloadsPreviouslyDownloadedPortalPackage()
    {
        var packageJson = CustomerConfigService.ToJson(CreateConfig());
        var handler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(packageJson, Encoding.UTF8, "application/json")
        });
        var client = CreatePortalClient(handler);
        var workspaceRoot = CreateTempDirectory();

        try
        {
            var readiness = CreateReadyReadiness();
            readiness.Status = "Downloaded";
            var result = await client.DownloadPackageAsync(
                CreateSession(),
                readiness,
                workspaceRoot);

            AssertEx.Equal("Downloaded", result.Status);
            AssertEx.True(File.Exists(result.PackagePath), result.PackagePath);
            AssertEx.Equal(packageJson, await File.ReadAllTextAsync(result.PackagePath));
        }
        finally
        {
            Directory.Delete(workspaceRoot, recursive: true);
        }
    }

    private static async Task PackageTrustKeyResolverRejectsUntrustedJwksHost()
    {
        var signedPackage = CreateSignedPackage(config =>
        {
            config.ControlPlane.JwksUrl = "https://evil.example.com/.well-known/pagemaker365-license-jwks.json";
        });
        var handler = new RecordingHttpMessageHandler(_ => throw new InvalidOperationException("Untrusted JWKS hosts must not be fetched."));
        var resolver = new PackageTrustKeyResolver(new HttpClient(handler));

        var exception = await AssertEx.ThrowsAsync<InvalidDataException>(() =>
            resolver.ResolveAsync(signedPackage.Config, PackageTrustOptions.Empty));

        AssertEx.StringContains(exception.Message, "not a trusted PageMaker365 endpoint");
        AssertEx.Equal(0, handler.Requests.Count);
    }

    private static async Task GetOnboardingStatusAsyncAcceptsUnknownReadinessWithoutMakingItDownloadable()
    {
        var statusHandler = new RecordingHttpMessageHandler(_ => JsonResponse("""
            {
              "contractVersion": "0.1",
              "sessionId": "onb_test_001",
              "customerName": "Example Customer",
              "status": "Pending",
              "portalRecordUrl": "https://localhost:5444/admin/onboarding/onb_test_001",
              "correlationId": "corr-status-unknown",
              "message": "Waiting on package generation",
              "missingFields": [],
              "packageReadiness": {
                "status": "QueuedForSignature",
                "packageVersion": "0.2-test",
                "packageDownloadUrl": "https://localhost:5443/api/onboarding/installer/onb_test_001/install-package",
                "message": "Package is not ready for installer download yet"
              }
            }
            """));
        var statusClient = CreatePortalClient(statusHandler);

        var status = await statusClient.GetOnboardingStatusAsync(CreateSession(), CreateDiscovery(), CreateConfig());

        AssertEx.Equal("Pending", status.Status);
        AssertEx.Equal("QueuedForSignature", status.PackageReadiness.Status);
        AssertEx.Equal("https://localhost:5443/api/onboarding/installer/onb_test_001/install-package", status.PackageReadiness.PackageDownloadUrl);

        var downloadHandler = new RecordingHttpMessageHandler(_ => throw new InvalidOperationException("Unknown readiness status must not trigger package download."));
        var downloadClient = CreatePortalClient(downloadHandler);
        var workspaceRoot = CreateTempDirectory();

        try
        {
            var result = await downloadClient.DownloadPackageAsync(CreateSession(), status.PackageReadiness, workspaceRoot);

            AssertEx.Equal("NotReady", result.Status);
            AssertEx.Equal("0.2-test", result.PackageVersion);
            AssertEx.Equal(0, downloadHandler.Requests.Count);
        }
        finally
        {
            Directory.Delete(workspaceRoot, recursive: true);
        }
    }

    private static async Task GetOnboardingStatusAsyncCarriesMissingFieldDetails()
    {
        var handler = new RecordingHttpMessageHandler(_ => JsonResponse("""
            {
              "contractVersion": "0.1",
              "sessionId": "onb_test_001",
              "customerName": "Example Customer",
              "status": "NeedsInput",
              "portalRecordUrl": "https://localhost:5444/admin/onboarding/onb_test_001",
              "correlationId": "corr-status-missing-fields",
              "message": "Additional package intake fields are required",
              "missingFields": [
                {
                  "fieldKey": "azure.subscriptionId",
                  "label": "Azure subscription",
                  "required": true,
                  "source": "Discovery",
                  "notes": "Select the subscription for PageMaker365 resources."
                },
                {
                  "fieldKey": "sharePoint.siteUrl",
                  "label": "SharePoint site URL",
                  "required": false,
                  "source": "LoadedPackage",
                  "notes": "Use the existing intranet site if available."
                }
              ],
              "packageReadiness": {
                "status": "Blocked",
                "packageVersion": "0.2-test",
                "message": "Required intake fields are missing"
              }
            }
            """));
        var client = CreatePortalClient(handler);

        var status = await client.GetOnboardingStatusAsync(CreateSession(), CreateDiscovery(), CreateConfig());

        AssertEx.Equal("NeedsInput", status.Status);
        AssertEx.Equal(2, status.MissingFields.Count);
        AssertEx.Equal("azure.subscriptionId", status.MissingFields[0].FieldKey);
        AssertEx.Equal("Azure subscription", status.MissingFields[0].Label);
        AssertEx.True(status.MissingFields[0].Required);
        AssertEx.Equal("Discovery", status.MissingFields[0].Source);
        AssertEx.Equal("Select the subscription for PageMaker365 resources.", status.MissingFields[0].Notes);
        AssertEx.Equal("sharePoint.siteUrl", status.MissingFields[1].FieldKey);
        AssertEx.Equal("SharePoint site URL", status.MissingFields[1].Label);
        AssertEx.False(status.MissingFields[1].Required);
        AssertEx.Equal("LoadedPackage", status.MissingFields[1].Source);
        AssertEx.Equal("Use the existing intranet site if available.", status.MissingFields[1].Notes);
    }

    private static async Task DownloadPackageAsyncSanitizesUnsafeContentDispositionFilename()
    {
        var packageJson = CustomerConfigService.ToJson(CreateConfig());
        var handler = new RecordingHttpMessageHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(packageJson, Encoding.UTF8, "application/json")
            };
            response.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
            {
                FileName = "customer:unsafe?package*.install.json"
            };
            return response;
        });
        var client = CreatePortalClient(handler);
        var workspaceRoot = CreateTempDirectory();

        try
        {
            var result = await client.DownloadPackageAsync(CreateSession(), CreateReadyReadiness(), workspaceRoot);

            AssertEx.Equal("Downloaded", result.Status);
            AssertEx.True(File.Exists(result.PackagePath), result.PackagePath);
            AssertEx.Equal("customer_unsafe_package_.install.json", Path.GetFileName(result.PackagePath));
            AssertEx.Equal(packageJson, await File.ReadAllTextAsync(result.PackagePath));
        }
        finally
        {
            Directory.Delete(workspaceRoot, recursive: true);
        }
    }

    private static async Task DownloadPackageAsyncIgnoresExternalPackageDownloadUrl()
    {
        var packageJson = CustomerConfigService.ToJson(CreateConfig());
        var handler = new RecordingHttpMessageHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(packageJson, Encoding.UTF8, "application/json")
            };
            return response;
        });
        var client = CreatePortalClient(handler);
        var readiness = CreateReadyReadiness();
        readiness.PackageDownloadUrl = "https://files.example.invalid/package.json";
        var workspaceRoot = CreateTempDirectory();

        try
        {
            var result = await client.DownloadPackageAsync(CreateSession(), readiness, workspaceRoot);

            AssertEx.Equal("Downloaded", result.Status);
            AssertEx.Equal("https://localhost:5443/api/onboarding/installer/onb_test_001/install-package", handler.Requests[0].RequestUri?.ToString());
            AssertEx.Contains(handler.HeaderValues("X-PM365-Onboarding-Session"), "onb_test_001");
            AssertEx.Contains(handler.HeaderValues("X-PM365-Onboarding-Code"), "TEST-CODE-001");
        }
        finally
        {
            Directory.Delete(workspaceRoot, recursive: true);
        }
    }

    private static Task OnboardingSessionServiceRejectsBootstrapMissingRequiredRuntimeFields()
    {
        var session = new OnboardingBootstrapSession
        {
            SessionId = "",
            CustomerName = "Example Customer",
            PortalBaseUrl = "not-a-url",
            ApiBaseUrl = "",
            OneTimeCode = "",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
        };

        var result = new OnboardingSessionService().Validate(session);
        var errors = string.Join(" ", result.Errors);

        AssertEx.False(result.IsValid);
        AssertEx.StringContains(errors, "Onboarding session ID is required");
        AssertEx.StringContains(errors, "API base URL is required");
        AssertEx.StringContains(errors, "One-time onboarding code is required");
        AssertEx.StringContains(errors, "Portal base URL must be an absolute URL");
        return Task.CompletedTask;
    }

    private static Task OnboardingSessionServiceRejectsExpiredBootstrap()
    {
        var session = CreateSession();
        session.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);

        var result = new OnboardingSessionService().Validate(session);

        AssertEx.False(result.IsValid);
        AssertEx.StringContains(string.Join(" ", result.Errors), "onboarding bootstrap session is expired");
        return Task.CompletedTask;
    }

    private static Task OnboardingSessionServiceRejectsUntrustedBootstrapEndpoints()
    {
        var session = CreateSession();
        session.PortalBaseUrl = "http://pagemaker365.com";
        session.ApiBaseUrl = "https://attacker.example.test";

        var result = new OnboardingSessionService().Validate(session);
        var errors = string.Join(" ", result.Errors);

        AssertEx.False(result.IsValid);
        AssertEx.StringContains(errors, "Portal base URL must use HTTPS");
        AssertEx.StringContains(errors, "API base URL host 'attacker.example.test' is not a trusted PageMaker365 endpoint");
        return Task.CompletedTask;
    }

    private static Task GraphDeviceCodeRequestsOnlyApprovedReadScopes()
    {
        var expected = new[]
        {
            "User.Read",
            "Domain.Read.All",
            "RoleManagement.Read.Directory",
            "Sites.Read.All"
        };

        AssertEx.Equal(string.Join("|", expected), string.Join("|", GraphDeviceCodeAuthenticator.RequiredScopes));
        AssertEx.False(GraphDeviceCodeAuthenticator.RequiredScopes.Any(scope => scope.Contains("ReadWrite", StringComparison.OrdinalIgnoreCase)));
        return Task.CompletedTask;
    }

    private static Task AuthenticationContextValidatorAcceptsMatchingAzureContext()
    {
        var result = AzureSignInResult("tenant-001", "sub-001");

        var failure = AuthenticationContextValidator.ValidateAzureSignIn(CreateConfig(), [result]);

        AssertEx.True(failure is null, failure?.Details);
        return Task.CompletedTask;
    }

    private static Task AuthenticationContextValidatorRejectsWarningOnlyAzureSignIn()
    {
        var warning = InstallerStepResult.Warning(
            "Azure Sign In",
            "AzureSignInWarning",
            "Azure context could not be verified.");

        var failure = AuthenticationContextValidator.ValidateAzureSignIn(CreateConfig(), [warning]);

        AssertEx.Equal("AzureSignInContextMissing", failure?.Code);
        return Task.CompletedTask;
    }

    private static Task AuthenticationContextValidatorRejectsWrongAzureTenant()
    {
        var result = AzureSignInResult("tenant-other", "sub-001");

        var failure = AuthenticationContextValidator.ValidateAzureSignIn(CreateConfig(), [result]);

        AssertEx.Equal("AzureTenantMismatch", failure?.Code);
        return Task.CompletedTask;
    }

    private static Task AuthenticationContextValidatorRejectsWrongAzureSubscription()
    {
        var result = AzureSignInResult("tenant-001", "sub-other");

        var failure = AuthenticationContextValidator.ValidateAzureSignIn(CreateConfig(), [result]);

        AssertEx.Equal("AzureSubscriptionMismatch", failure?.Code);
        return Task.CompletedTask;
    }

    private static Task AuthenticationContextValidatorAcceptsValidGraphContext()
    {
        var now = DateTimeOffset.UtcNow;
        var result = GraphSignInResult("tenant-001", now.AddHours(1));

        var failure = AuthenticationContextValidator.ValidateGraphSignIn(CreateConfig(), result, now);

        AssertEx.True(failure is null, failure?.Details);
        return Task.CompletedTask;
    }

    private static Task AuthenticationContextValidatorRejectsWrongGraphTenant()
    {
        var now = DateTimeOffset.UtcNow;
        var result = GraphSignInResult("tenant-other", now.AddHours(1));

        var failure = AuthenticationContextValidator.ValidateGraphSignIn(CreateConfig(), result, now);

        AssertEx.Equal("GraphTenantMismatch", failure?.Code);
        return Task.CompletedTask;
    }

    private static Task AuthenticationContextValidatorRejectsExpiredGraphToken()
    {
        var now = DateTimeOffset.UtcNow;
        var result = GraphSignInResult("tenant-001", now.AddSeconds(-1));

        var failure = AuthenticationContextValidator.ValidateGraphSignIn(CreateConfig(), result, now);

        AssertEx.Equal("GraphAccessTokenExpired", failure?.Code);
        return Task.CompletedTask;
    }

    private static Task AuthenticationContextValidatorRejectsMissingGraphScope()
    {
        var now = DateTimeOffset.UtcNow;
        var result = GraphSignInResult("tenant-001", now.AddHours(1));
        result.Scopes.Remove("Sites.Read.All");

        var failure = AuthenticationContextValidator.ValidateGraphSignIn(CreateConfig(), result, now);

        AssertEx.Equal("GraphConsentScopesMissing", failure?.Code);
        AssertEx.StringContains(failure?.Details ?? "", "Sites.Read.All");
        return Task.CompletedTask;
    }

    private static InstallerStepResult AzureSignInResult(string tenantId, string subscriptionId)
    {
        var result = InstallerStepResult.Passed(
            "Azure Sign In",
            "AzureSignInCompleted",
            "Azure sign-in completed.");
        result.Data["tenantId"] = tenantId;
        result.Data["subscriptionId"] = subscriptionId;
        return result;
    }

    private static GraphSignInResult GraphSignInResult(string tenantId, DateTimeOffset expiresOn)
    {
        return new GraphSignInResult
        {
            StepResult = InstallerStepResult.Passed(
                "Microsoft Graph Sign In",
                "GraphSignInCompleted",
                "Microsoft Graph sign-in completed."),
            AccessToken = "test-access-token",
            ExpiresOn = expiresOn,
            TenantId = tenantId,
            Account = "operator@example.test",
            Scopes = GraphDeviceCodeAuthenticator.RequiredScopes.ToList()
        };
    }

    private static async Task OnboardingSessionServiceValidatesSampleBootstrapContract()
    {
        var path = Path.Combine(FindRepositoryRoot(), "samples", "contoso.onboarding.bootstrap.json");
        var session = await new OnboardingSessionService().LoadBootstrapAsync(path);

        var result = new OnboardingSessionService().Validate(session);

        AssertEx.True(result.IsValid, string.Join(" ", result.Errors));
        AssertEx.Equal("onb_contoso_sandbox_001", session.SessionId);
        AssertEx.True(session.AllowsOperation(OnboardingOperation.InstallStatusSync));
    }

    private static async Task ConnectAsyncRejectsInvalidPortalResponse()
    {
        var handler = new RecordingHttpMessageHandler(_ => JsonResponse("""{"status":"Connected"}"""));
        var client = CreatePortalClient(handler);

        var exception = await AssertEx.ThrowsAsync<OnboardingApiException>(() => client.ConnectAsync(CreateSession()));

        AssertEx.StringContains(exception.Message, "missing required field");
        AssertEx.StringContains(exception.Message, "sessionId");
        AssertEx.StringContains(exception.Message, "correlationId");
    }

    private static async Task GetOnboardingStatusAsyncRejectsStatusMissingReadinessStatus()
    {
        var handler = new RecordingHttpMessageHandler(_ => JsonResponse("""
            {
              "contractVersion": "0.1",
              "sessionId": "onb_test_001",
              "customerName": "Example Customer",
              "status": "Pending",
              "portalRecordUrl": "https://localhost:5444/admin/onboarding/onb_test_001",
              "correlationId": "corr-status-missing-readiness",
              "message": "Package generation is queued.",
              "missingFields": [],
              "packageReadiness": {
                "packageVersion": "0.2-test",
                "message": "Queued"
              }
            }
            """));
        var client = CreatePortalClient(handler);

        var exception = await AssertEx.ThrowsAsync<OnboardingApiException>(() =>
            client.GetOnboardingStatusAsync(CreateSession(), CreateDiscovery(), CreateConfig()));

        AssertEx.StringContains(exception.Message, "missing required field");
        AssertEx.StringContains(exception.Message, "packageReadiness.status");
    }

    private static async Task GetOnboardingStatusAsyncRejectsReadyStatusWithoutPackageUrl()
    {
        var handler = new RecordingHttpMessageHandler(_ => JsonResponse("""
            {
              "contractVersion": "0.1",
              "sessionId": "onb_test_001",
              "customerName": "Example Customer",
              "status": "Ready",
              "portalRecordUrl": "https://localhost:5444/admin/onboarding/onb_test_001",
              "correlationId": "corr-status-002",
              "message": "Ready",
              "missingFields": [],
              "packageReadiness": {
                "status": "Ready",
                "packageVersion": "0.2-test",
                "message": "Ready for download"
              }
            }
            """));
        var client = CreatePortalClient(handler);

        var exception = await AssertEx.ThrowsAsync<OnboardingApiException>(() =>
            client.GetOnboardingStatusAsync(CreateSession(), CreateDiscovery(), CreateConfig()));

        AssertEx.StringContains(exception.Message, "packageReadiness.packageDownloadUrl");
    }

    private static async Task GetOnboardingStatusAsyncRejectsStatusSessionMismatch()
    {
        var handler = new RecordingHttpMessageHandler(_ => JsonResponse("""
            {
              "contractVersion": "0.1",
              "sessionId": "onb_test_other",
              "customerName": "Example Customer",
              "status": "Ready",
              "portalRecordUrl": "https://localhost:5444/admin/onboarding/onb_test_other",
              "correlationId": "corr-status-mismatch",
              "message": "Ready",
              "missingFields": [],
              "packageReadiness": {
                "status": "Ready",
                "packageVersion": "0.2-test",
                "packageDownloadUrl": "https://localhost:5443/api/onboarding/installer/onb_test_other/install-package",
                "message": "Ready for download"
              }
            }
            """));
        var client = CreatePortalClient(handler);

        var exception = await AssertEx.ThrowsAsync<OnboardingApiException>(() =>
            client.GetOnboardingStatusAsync(CreateSession(), CreateDiscovery(), CreateConfig()));

        AssertEx.StringContains(exception.Message, "returned session 'onb_test_other'");
        AssertEx.StringContains(exception.Message, "expected session 'onb_test_001'");
    }

    private static async Task GetOnboardingStatusAsyncValidatesSampleStatusContract()
    {
        var root = FindRepositoryRoot();
        var session = await new OnboardingSessionService().LoadBootstrapAsync(
            Path.Combine(root, "samples", "contoso.onboarding.bootstrap.json"));
        var statusJson = await File.ReadAllTextAsync(Path.Combine(root, "samples", "contoso.onboarding.status.json"));
        var handler = new RecordingHttpMessageHandler(_ => JsonResponse(statusJson));
        var client = CreatePortalClient(handler);

        var status = await client.GetOnboardingStatusAsync(session, discovery: null, config: null);

        AssertEx.Equal("Ready", status.Status);
        AssertEx.Equal(session.SessionId, status.SessionId);
        AssertEx.Equal("Ready", status.PackageReadiness.Status);
        AssertEx.Equal("0.2-mock", status.PackageReadiness.PackageVersion);
    }

    private static async Task ConnectAsyncSurfacesPortalApiErrorDetails()
    {
        var handler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent(
                """{"message":"Expired onboarding code","correlationId":"corr-401"}""",
                Encoding.UTF8,
                "application/json")
        });
        var client = CreatePortalClient(handler);

        var exception = await AssertEx.ThrowsAsync<OnboardingApiException>(() => client.ConnectAsync(CreateSession()));

        AssertEx.Equal(HttpStatusCode.Unauthorized, exception.StatusCode.GetValueOrDefault());
        AssertEx.Equal("corr-401", exception.CorrelationId);
        AssertEx.StringContains(exception.Message, "Expired onboarding code");
    }

    private static async Task DownloadPackageAsyncRejectsNonJsonPackageResponse()
    {
        var handler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html>not a package</html>", Encoding.UTF8, "text/html")
        });
        var client = CreatePortalClient(handler);
        var workspaceRoot = CreateTempDirectory();

        try
        {
            var exception = await AssertEx.ThrowsAsync<OnboardingApiException>(() =>
                client.DownloadPackageAsync(CreateSession(), CreateReadyReadiness(), workspaceRoot));

            AssertEx.StringContains(exception.Message, "unsupported content type");
        }
        finally
        {
            Directory.Delete(workspaceRoot, recursive: true);
        }
    }

    private static async Task DownloadPackageAsyncRejectsInvalidGeneratedPackage()
    {
        var handler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"contractVersion":"0.2","customer":{"tenantName":"Example Customer"}}""",
                Encoding.UTF8,
                "application/json")
        });
        var client = CreatePortalClient(handler);
        var workspaceRoot = CreateTempDirectory();

        try
        {
            var exception = await AssertEx.ThrowsAsync<OnboardingApiException>(() =>
                client.DownloadPackageAsync(CreateSession(), CreateReadyReadiness(), workspaceRoot));

            AssertEx.StringContains(exception.Message, "failed validation");
            AssertEx.StringContains(exception.Message, "Azure subscription ID is required");
        }
        finally
        {
            Directory.Delete(workspaceRoot, recursive: true);
        }
    }

    private static async Task DownloadPackageAsyncRejectsGeneratedPackageMissingRequiredContractSections()
    {
        var json = """
            {
              "contractVersion": "0.2",
              "customer": {
                "tenantName": "Example Customer",
                "tenantId": "tenant-001",
                "primaryContact": "owner@example.test"
              },
              "azure": {
                "tenantId": "tenant-001",
                "subscriptionId": "sub-001",
                "location": "eastus",
                "resourceGroupName": "rg-pm365-example",
                "environment": "test"
              },
              "sharePoint": {
                "siteUrl": "https://example.sharepoint.com/sites/intranet",
                "defaultDocumentLibrary": "Documents",
                "permissionMode": "SitesSelected"
              },
              "app": {
                "appName": "PageMaker365",
                "supportEmail": "support@example.test"
              },
              "controlPlane": {
                "deploymentExportId": "export-001",
                "trustMode": "UnsignedAllowed"
              }
            }
            """;
        var handler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });
        var client = CreatePortalClient(handler);
        var workspaceRoot = CreateTempDirectory();

        try
        {
            var exception = await AssertEx.ThrowsAsync<OnboardingApiException>(() =>
                client.DownloadPackageAsync(CreateSession(), CreateReadyReadiness(), workspaceRoot));

            AssertEx.StringContains(exception.Message, "failed validation");
            AssertEx.StringContains(exception.Message, "features is required");
            AssertEx.False(Directory.Exists(Path.Combine(workspaceRoot, "support-bundle", "onboarding", "onb_test_001", "generated-package")));
        }
        finally
        {
            Directory.Delete(workspaceRoot, recursive: true);
        }
    }

    private static async Task DownloadPackageAsyncAcceptsPackageBoundToActiveProvenance()
    {
        var packageJson = CreateProvenancePackageJson(_ => { });
        var workspaceRoot = CreateTempDirectory();

        try
        {
            var result = await DownloadPackageAsync(packageJson, workspaceRoot);

            AssertEx.Equal("Downloaded", result.Status);
            AssertEx.True(File.Exists(result.PackagePath), result.PackagePath);
            AssertEx.Equal(packageJson, await File.ReadAllTextAsync(result.PackagePath));
        }
        finally
        {
            Directory.Delete(workspaceRoot, recursive: true);
        }
    }

    private static async Task DownloadPackageAsyncRejectsPackageWithWrongOnboardingSession()
    {
        var packageJson = CreateProvenancePackageJson(config =>
        {
            config.ControlPlane.OnboardingSessionId = "onb_other_001";
        });
        var workspaceRoot = CreateTempDirectory();

        try
        {
            var exception = await AssertEx.ThrowsAsync<OnboardingApiException>(() =>
                DownloadPackageAsync(packageJson, workspaceRoot));

            AssertEx.StringContains(exception.Message, "onboarding session");
            AssertEx.False(GeneratedPackageDirectoryExists(workspaceRoot), "Mismatched generated package must not be written to support bundle.");
        }
        finally
        {
            Directory.Delete(workspaceRoot, recursive: true);
        }
    }

    private static async Task DownloadPackageAsyncRejectsPackageWithWrongTenant()
    {
        var packageJson = CreateProvenancePackageJson(config =>
        {
            config.Customer.TenantId = "tenant-other";
            config.Azure.TenantId = "tenant-other";
        });
        var workspaceRoot = CreateTempDirectory();

        try
        {
            var exception = await AssertEx.ThrowsAsync<OnboardingApiException>(() =>
                DownloadPackageAsync(packageJson, workspaceRoot));

            AssertEx.StringContains(exception.Message, "tenant");
            AssertEx.False(GeneratedPackageDirectoryExists(workspaceRoot), "Wrong-tenant generated package must not be written to support bundle.");
        }
        finally
        {
            Directory.Delete(workspaceRoot, recursive: true);
        }
    }

    private static async Task DownloadPackageAsyncRejectsPackageWithWrongDiscoveryId()
    {
        var packageJson = CreateProvenancePackageJson(config =>
        {
            config.ControlPlane.DiscoveryId = "disc_other_001";
        });
        var workspaceRoot = CreateTempDirectory();

        try
        {
            var exception = await AssertEx.ThrowsAsync<OnboardingApiException>(() =>
                DownloadPackageAsync(packageJson, workspaceRoot));

            AssertEx.StringContains(exception.Message, "discovery");
            AssertEx.False(GeneratedPackageDirectoryExists(workspaceRoot), "Wrong-discovery generated package must not be written to support bundle.");
        }
        finally
        {
            Directory.Delete(workspaceRoot, recursive: true);
        }
    }

    private static async Task DownloadPackageAsyncRejectsPackageMissingDeploymentExportId()
    {
        var packageJson = CreateProvenancePackageJson(config =>
        {
            config.ControlPlane.DeploymentExportId = "";
        });
        var workspaceRoot = CreateTempDirectory();

        try
        {
            var exception = await AssertEx.ThrowsAsync<OnboardingApiException>(() =>
                DownloadPackageAsync(packageJson, workspaceRoot));

            AssertEx.StringContains(exception.Message, "deploymentExportId");
            AssertEx.False(GeneratedPackageDirectoryExists(workspaceRoot), "Generated package missing export metadata must not be written to support bundle.");
        }
        finally
        {
            Directory.Delete(workspaceRoot, recursive: true);
        }
    }

    private static async Task LivePortalInstallPackageDownloadLoadsWithCustomerConfigService()
    {
        var bootstrapPath = Environment.GetEnvironmentVariable("PM365_LIVE_ONBOARDING_BOOTSTRAP_PATH");
        if (string.IsNullOrWhiteSpace(bootstrapPath))
        {
            Console.WriteLine("SKIP PM365_LIVE_ONBOARDING_BOOTSTRAP_PATH is not set.");
            return;
        }

        var sessionService = new OnboardingSessionService();
        var session = await sessionService.LoadBootstrapAsync(bootstrapPath);
        var sessionValidation = sessionService.Validate(session);
        AssertEx.True(sessionValidation.IsValid, string.Join(" ", sessionValidation.Errors));

        var baseUri = new Uri(session.ApiBaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        var packageEndpoint = new Uri(baseUri, $"api/onboarding/installer/{Uri.EscapeDataString(session.SessionId)}/install-package");
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        using var request = new HttpRequestMessage(HttpMethod.Get, packageEndpoint);
        request.Headers.Add("X-PM365-Onboarding-Session", session.SessionId);
        request.Headers.Add("X-PM365-Onboarding-Code", session.OneTimeCode);

        var apiKeyVariable = Environment.GetEnvironmentVariable("PM365_LIVE_ONBOARDING_API_KEY_ENV") ?? "PM365_ONBOARDING_API_KEY";
        var apiKey = Environment.GetEnvironmentVariable(apiKeyVariable);
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        using var response = await httpClient.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Live portal package download failed with HTTP {(int)response.StatusCode}.");
        }

        AssertEx.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(body);
        foreach (var section in new[]
        {
            "contractVersion",
            "customer",
            "azure",
            "sharePoint",
            "app",
            "entra",
            "controlPlane",
            "secrets",
            "features",
            "smokeTests"
        })
        {
            AssertEx.True(document.RootElement.TryGetProperty(section, out _), $"Downloaded package missing top-level section '{section}'.");
        }

        var workspaceRoot = CreateTempDirectory();
        try
        {
            var packagePath = Path.Combine(workspaceRoot, "downloaded.customer.install.json");
            await File.WriteAllTextAsync(packagePath, body);
            var config = await new CustomerConfigService().LoadAsync(packagePath);

            AssertEx.Equal(session.SessionId, config.ControlPlane.OnboardingSessionId);
            AssertEx.False(string.IsNullOrWhiteSpace(config.ControlPlane.DeploymentExportId), "Downloaded package missing deployment export ID.");
            AssertEx.False(string.IsNullOrWhiteSpace(config.Customer.TenantName), "Downloaded package missing customer tenant name.");
            AssertEx.False(string.IsNullOrWhiteSpace(config.Customer.TenantId), "Downloaded package missing customer tenant ID.");
            AssertEx.False(string.IsNullOrWhiteSpace(config.SharePoint.SiteUrl), "Downloaded package missing SharePoint site URL.");
            AssertEx.False(string.IsNullOrWhiteSpace(config.App.AppName), "Downloaded package missing app name.");
            AssertEx.True(config.Features.KnowledgeBase, "Downloaded package missing knowledge base feature flag.");
            AssertEx.True(config.Features.CustomerPortal, "Downloaded package missing customer portal feature flag.");
            AssertEx.True(config.Features.BillingIntegration, "Downloaded package missing billing integration feature flag.");
        }
        finally
        {
            Directory.Delete(workspaceRoot, recursive: true);
        }
    }

    private static async Task ConnectAsyncFallsBackToMockWhenPortalFails()
    {
        var handler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("failed", Encoding.UTF8, "text/plain")
        });
        var client = CreatePortalClient(handler, fallbackToMock: true);

        var response = await client.ConnectAsync(CreateSession());

        AssertEx.Equal("ConnectedMock", response.Status);
        AssertEx.StringContains(response.Message, "using local mock fallback");
        AssertEx.Equal(1, handler.Requests.Count);
    }

    private static async Task ConnectAsyncDoesNotFallBackToMockForInvalidPortalResponse()
    {
        var handler = new RecordingHttpMessageHandler(_ => JsonResponse("""{"status":"Connected"}"""));
        var client = CreatePortalClient(handler, fallbackToMock: true);

        var exception = await AssertEx.ThrowsAsync<OnboardingApiException>(() => client.ConnectAsync(CreateSession()));

        AssertEx.StringContains(exception.Message, "missing required field");
        AssertEx.StringContains(exception.Message, "sessionId");
        AssertEx.Equal(1, handler.Requests.Count);
    }

    private static Task CustomerConfigServiceVerifiesMatchingPackageHash()
    {
        var config = CreateConfig();
        var jsonWithoutHash = CustomerConfigService.ToJson(config);
        config.ControlPlane.PackageHash = CustomerConfigService.ComputePackageHash(jsonWithoutHash);
        var jsonWithHash = CustomerConfigService.ToJson(config);

        var result = new CustomerConfigService().Validate(config, jsonWithHash);

        AssertEx.True(result.IsValid, string.Join(" ", result.Errors));
        AssertEx.Equal("Hash verified", result.PackageTrustStatus);
        AssertEx.Equal(config.ControlPlane.PackageHash, result.ComputedPackageHash);
        return Task.CompletedTask;
    }

    private static Task CustomerConfigServiceRejectsPackageHashMismatch()
    {
        var config = CreateConfig();
        config.ControlPlane.PackageHash = "sha256:0000000000000000000000000000000000000000000000000000000000000000";
        var result = new CustomerConfigService().Validate(config, CustomerConfigService.ToJson(config));

        AssertEx.False(result.IsValid);
        AssertEx.Equal("Hash mismatch", result.PackageTrustStatus);
        AssertEx.StringContains(string.Join(" ", result.Errors), "hash mismatch");
        return Task.CompletedTask;
    }

    private static Task CustomerConfigServiceEnforcesSignedRequiredTrustMode()
    {
        var config = CreateConfig();
        config.ControlPlane.TrustMode = "SignedRequired";
        var result = new CustomerConfigService().Validate(config, CustomerConfigService.ToJson(config));

        AssertEx.False(result.IsValid);
        AssertEx.Equal("Missing signature", result.PackageTrustStatus);
        AssertEx.StringContains(string.Join(" ", result.Errors), "controlPlane.packageHash");
        AssertEx.StringContains(string.Join(" ", result.Errors), "controlPlane.signature");
        return Task.CompletedTask;
    }

    private static Task CustomerConfigServiceVerifiesEd25519SignedPackage()
    {
        var package = CreateSignedPackage();
        var result = new CustomerConfigService().Validate(
            package.Config,
            package.Json,
            trustOptions: CreatePackageTrustOptions(package));

        AssertEx.True(result.IsValid, string.Join(" ", result.Errors));
        AssertEx.Equal("Verified", result.PackageTrustStatus);
        AssertEx.Equal(package.Config.ControlPlane.PackageHash, result.ComputedPackageHash);
        return Task.CompletedTask;
    }

    private static Task CustomerConfigServiceRejectsSignedPackageWithoutTrustedKey()
    {
        var package = CreateSignedPackage();
        var result = new CustomerConfigService().Validate(
            package.Config,
            package.Json,
            trustOptions: PackageTrustOptions.Empty);

        AssertEx.False(result.IsValid);
        AssertEx.Equal("Missing trusted key", result.PackageTrustStatus);
        AssertEx.StringContains(string.Join(" ", result.Errors), package.PublicKeyId);
        return Task.CompletedTask;
    }

    private static Task CustomerConfigServiceRejectsInvalidPackageSignature()
    {
        var package = CreateSignedPackage(config => config.Customer.TenantName = "Signed Package Before Tamper");
        package.Config.Customer.TenantName = "Signed Package After Tamper";
        package.Config.ControlPlane.PackageHash = CustomerConfigService.ComputePackageHash(CustomerConfigService.ToJson(package.Config));
        var tamperedJson = CustomerConfigService.ToJson(package.Config);
        var result = new CustomerConfigService().Validate(
            package.Config,
            tamperedJson,
            trustOptions: CreatePackageTrustOptions(package));

        AssertEx.False(result.IsValid);
        AssertEx.Equal("Invalid signature", result.PackageTrustStatus);
        AssertEx.StringContains(string.Join(" ", result.Errors), "signature verification failed");
        return Task.CompletedTask;
    }

    private static Task CustomerConfigServiceRejectsUnsupportedSignatureAlgorithm()
    {
        var package = CreateSignedPackage(config => config.ControlPlane.SignatureAlgorithm = "RS256");
        var result = new CustomerConfigService().Validate(
            package.Config,
            package.Json,
            trustOptions: CreatePackageTrustOptions(package));

        AssertEx.False(result.IsValid);
        AssertEx.Equal("Unsupported signature", result.PackageTrustStatus);
        AssertEx.StringContains(string.Join(" ", result.Errors), "Only Ed25519");
        return Task.CompletedTask;
    }

    private static async Task CustomerConfigServiceValidatesSamplePackageContract()
    {
        var path = Path.Combine(FindRepositoryRoot(), "samples", "contoso.customer.install.json");
        var json = await File.ReadAllTextAsync(path);
        var config = JsonSerializer.Deserialize<CustomerInstallConfig>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        var result = new CustomerConfigService().Validate(config, json);

        AssertEx.True(result.IsValid, string.Join(" ", result.Errors));
    }

    private static Task CustomerConfigServiceRejectsPackageMissingRequiredContractFields()
    {
        var json = """
            {
              "contractVersion": "0.2",
              "customer": { "tenantName": "Example", "tenantId": "tenant-001" },
              "azure": { "subscriptionId": "sub-001", "location": "eastus", "resourceGroupName": "rg-test" },
              "sharePoint": { "siteUrl": "https://example.sharepoint.com/sites/intranet" },
              "app": { "appName": "PageMaker365" },
              "features": { "knowledgeBase": true }
            }
            """;
        var config = JsonSerializer.Deserialize<CustomerInstallConfig>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        var result = new CustomerConfigService().Validate(config, json);
        var errors = string.Join(" ", result.Errors);

        AssertEx.False(result.IsValid);
        AssertEx.StringContains(errors, "Customer primary contact is required");
        AssertEx.StringContains(errors, "Azure environment is required");
        AssertEx.StringContains(errors, "SharePoint default document library is required");
        AssertEx.StringContains(errors, "Support email is required");
        AssertEx.StringContains(errors, "features.customerPortal is required");
        AssertEx.StringContains(errors, "features.billingIntegration is required");
        return Task.CompletedTask;
    }

    private static Task CustomerConfigServiceRejectsRawSecretContainers()
    {
        var json = """
            {
              "customer": { "tenantName": "Example", "tenantId": "tenant-001" },
              "azure": { "subscriptionId": "sub-001", "location": "eastus", "resourceGroupName": "rg-test" },
              "sharePoint": { "siteUrl": "https://example.sharepoint.com/sites/intranet" },
              "app": { "appName": "PageMaker365" },
              "features": {},
              "secrets": {
                "tokens": {
                  "runtime": "do-not-store"
                }
              }
            }
            """;
        var config = JsonSerializer.Deserialize<CustomerInstallConfig>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var result = new CustomerConfigService().Validate(config, json);

        AssertEx.False(result.IsValid);
        AssertEx.StringContains(string.Join(" ", result.Errors), "secrets.tokens");
        return Task.CompletedTask;
    }

    private static Task OptionsServiceLoadsFileAndEnvironmentOverrides()
    {
        var workspaceRoot = CreateTempDirectory();
        using var mode = new EnvironmentVariableScope("PM365_ONBOARDING_MODE", "Portal");
        using var baseUrl = new EnvironmentVariableScope("PM365_ONBOARDING_API_BASE_URL", "https://localhost:5555");
        using var statusPath = new EnvironmentVariableScope("PM365_ONBOARDING_STATUS_ENDPOINT_PATH", "/custom/status");
        using var fallback = new EnvironmentVariableScope("PM365_ONBOARDING_FALLBACK_TO_MOCK", "false");
        File.WriteAllText(
            Path.Combine(workspaceRoot, "onboarding-api.json"),
            """
            {
              "mode": "Mock",
              "apiBaseUrl": "https://file.example.test",
              "connectEndpointPath": "/file/connect",
              "timeoutSeconds": 9,
              "fallbackToMockOnFailure": true
            }
            """);

        try
        {
            var options = new OnboardingApiOptionsService().Load(workspaceRoot);

            AssertEx.Equal("Portal", options.Mode);
            AssertEx.Equal("https://localhost:5555", options.ApiBaseUrl);
            AssertEx.Equal("/file/connect", options.ConnectEndpointPath);
            AssertEx.Equal("/custom/status", options.StatusEndpointPath);
            AssertEx.Equal(9, options.TimeoutSeconds);
            AssertEx.False(options.FallbackToMockOnFailure);
            AssertEx.Equal(
                "https://localhost:5555/file/connect",
                options.ConnectEndpoint(new OnboardingBootstrapSession()).ToString());
        }
        finally
        {
            Directory.Delete(workspaceRoot, recursive: true);
        }

        return Task.CompletedTask;
    }

    private static Task InstallerStateStoreSavesAndLoadsActiveState()
    {
        var workspaceRoot = CreateTempDirectory();
        try
        {
            var store = new InstallerStateStore(workspaceRoot);
            var path = store.Save(new PersistedInstallerState
            {
                StateId = "state-active-001",
                WorkflowMode = "Setup",
                CurrentStepNumber = 5,
                MaxAccessibleStepNumber = 6,
                PackagePath = "contoso.customer.install.json",
                LastPreviewStatus = InstallStatus.Passed,
                Steps =
                {
                    new PersistedInstallerStepState
                    {
                        Number = 5,
                        Name = "Preview",
                        StatusLabel = "Complete",
                        StatusBrush = "#42D8A0"
                    }
                },
                PreviewResults =
                {
                    InstallerStepResult.Passed("Azure What-If", "WhatIfSucceeded", "Preview succeeded.")
                },
                InstallerEvidenceOutbox = new InstallerEvidenceOutboxState
                {
                    InstallAttemptId = "attempt-persisted-001",
                    NextSequence = 3,
                    InstallStarted = true,
                    PendingEvents =
                    {
                        new PendingInstallerEvidenceEvent
                        {
                            IdempotencyKey = "attempt-persisted-001:2:event-persisted-002",
                            Payload = new InstallerEvidenceEvent
                            {
                                EventId = "event-persisted-002",
                                EventType = InstallerEvidenceEventType.InstallStarted,
                                InstallAttemptId = "attempt-persisted-001",
                                Sequence = 2
                            }
                        }
                    }
                }
            });

            var loaded = store.LoadMostRecentActive();

            AssertEx.True(File.Exists(path), path);
            AssertEx.True(loaded is not null, "Expected active state to load.");
            AssertEx.Equal("state-active-001", loaded!.StateId);
            AssertEx.Equal(5, loaded.CurrentStepNumber);
            AssertEx.Equal(InstallStatus.Passed, loaded.LastPreviewStatus);
            AssertEx.Equal("Azure What-If", loaded.PreviewResults[0].StepName);
            AssertEx.Equal("attempt-persisted-001", loaded.InstallerEvidenceOutbox.InstallAttemptId);
            AssertEx.Equal(3, loaded.InstallerEvidenceOutbox.NextSequence);
            AssertEx.Equal("event-persisted-002", loaded.InstallerEvidenceOutbox.PendingEvents[0].Payload.EventId);
        }
        finally
        {
            Directory.Delete(workspaceRoot, recursive: true);
        }

        return Task.CompletedTask;
    }

    private static Task InstallerStateStoreIgnoresCompletedStateForResume()
    {
        var workspaceRoot = CreateTempDirectory();
        try
        {
            var store = new InstallerStateStore(workspaceRoot);
            store.Save(new PersistedInstallerState
            {
                StateId = "state-active-001",
                WorkflowMode = "Setup",
                CurrentStepNumber = 7,
                MaxAccessibleStepNumber = 8
            });
            store.Save(new PersistedInstallerState
            {
                StateId = "state-complete-001",
                WorkflowMode = "Setup",
                CurrentStepNumber = 8,
                MaxAccessibleStepNumber = 8,
                IsCompleted = true
            });

            var loaded = store.LoadMostRecentActive();

            AssertEx.True(loaded is not null, "Expected active state to load.");
            AssertEx.Equal("state-active-001", loaded!.StateId);
        }
        finally
        {
            Directory.Delete(workspaceRoot, recursive: true);
        }

        return Task.CompletedTask;
    }

    private static async Task DeploymentApprovalManifestServiceWritesHashWithoutRawConfirmation()
    {
        var workspaceRoot = CreateTempDirectory();
        try
        {
            var previewPath = Path.Combine(workspaceRoot, "azure-whatif.json");
            await File.WriteAllTextAsync(previewPath, """{"artifactType":"PageMaker365.AzureWhatIf","status":"Passed"}""");
            var previewHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(previewPath))).ToLowerInvariant();
            var config = CreateConfig();

            var result = await new DeploymentApprovalManifestService().CreateAsync(
                config,
                new DeploymentApprovalManifestRequest
                {
                    OutputRoot = workspaceRoot,
                    InstallerVersion = "test-version",
                    WorkflowMode = "Setup",
                    PackagePath = "sample-package.json",
                    PackageExportId = "export-001",
                    PackageTrustStatus = "Hash verified",
                    PackageTrustSummary = "Package hash matched.",
                    PreviewStatus = "Passed",
                    PreviewSummary = "Preview reviewed.",
                    PreviewEvidencePath = previewPath,
                    PreviewResultCount = 1,
                    ApprovalConfirmed = true,
                    ConfirmationTarget = config.Azure.ResourceGroupName,
                    ConfirmationMatched = true
                });

            var manifestJson = await File.ReadAllTextAsync(result.ManifestPath);
            AssertEx.True(File.Exists(result.ManifestPath), result.ManifestPath);
            AssertEx.Equal("PageMaker365.DeploymentApproval", result.Manifest.ManifestType);
            AssertEx.Equal(previewPath, result.Manifest.PreviewEvidence.Path);
            AssertEx.Equal(previewHash, result.Manifest.PreviewEvidence.Hash);
            AssertEx.True(result.Manifest.PreviewEvidence.EvidenceFileFound);
            AssertEx.True(result.Manifest.ConfirmationSummary.Approved);
            AssertEx.False(result.Manifest.ConfirmationSummary.RawConfirmationTextPersisted);
            AssertEx.False(manifestJson.Contains("do-not-store-typed-confirmation", StringComparison.OrdinalIgnoreCase), manifestJson);
        }
        finally
        {
            Directory.Delete(workspaceRoot, recursive: true);
        }
    }

    private static async Task FinalEvidenceServiceCopiesApprovalAndAzureArtifacts()
    {
        var workspaceRoot = CreateTempDirectory();
        try
        {
            var previewReceiptPath = Path.Combine(workspaceRoot, "deployment-preview.json");
            var previewArtifactPath = Path.Combine(workspaceRoot, "azure-whatif.json");
            var approvalManifestPath = Path.Combine(workspaceRoot, "deployment-approval-manifest.json");
            var deploymentReceiptPath = Path.Combine(workspaceRoot, "deployment-install.json");
            var deploymentArtifactPath = Path.Combine(workspaceRoot, "azure-deployment.json");
            var validationReceiptPath = Path.Combine(workspaceRoot, "deployment-validation.json");

            await File.WriteAllTextAsync(previewReceiptPath, """{"previewStatus":"Passed"}""");
            await File.WriteAllTextAsync(previewArtifactPath, """{"artifactType":"PageMaker365.AzureWhatIf"}""");
            await File.WriteAllTextAsync(approvalManifestPath, """{"manifestType":"PageMaker365.DeploymentApproval"}""");
            await File.WriteAllTextAsync(deploymentReceiptPath, """{"deploymentStatus":"Passed"}""");
            await File.WriteAllTextAsync(deploymentArtifactPath, """{"artifactType":"PageMaker365.AzureDeployment"}""");
            await File.WriteAllTextAsync(validationReceiptPath, """{"validationStatus":"Passed"}""");

            var result = await new FinalEvidenceService().CreateAsync(
                CreateConfig(),
                new FinalEvidenceRequest
                {
                    OutputRoot = workspaceRoot,
                    InstallerVersion = "test-version",
                    PackagePath = "sample-package.json",
                    PreviewStatus = "Passed",
                    PreviewEvidencePath = previewReceiptPath,
                    PreviewArtifactPath = previewArtifactPath,
                    ApprovalManifestPath = approvalManifestPath,
                    DeploymentStatus = "Passed",
                    DeploymentEvidencePath = deploymentReceiptPath,
                    DeploymentArtifactPath = deploymentArtifactPath,
                    ValidationStatus = "Passed",
                    ValidationEvidencePath = validationReceiptPath,
                    FinalStatus = "Complete"
                });

            AssertEx.True(File.Exists(Path.Combine(result.EvidenceDirectory, "deployment-preview.json")));
            AssertEx.True(File.Exists(Path.Combine(result.EvidenceDirectory, "azure-whatif.json")));
            AssertEx.True(File.Exists(Path.Combine(result.EvidenceDirectory, "deployment-approval-manifest.json")));
            AssertEx.True(File.Exists(Path.Combine(result.EvidenceDirectory, "deployment-install.json")));
            AssertEx.True(File.Exists(Path.Combine(result.EvidenceDirectory, "azure-deployment.json")));
            AssertEx.True(File.Exists(Path.Combine(result.EvidenceDirectory, "deployment-validation.json")));
            AssertEx.True(File.Exists(result.BundlePath), result.BundlePath);

            var finalManifest = await File.ReadAllTextAsync(result.ManifestPath);
            AssertEx.StringContains(finalManifest, "artifactCopiedPath");
            AssertEx.StringContains(finalManifest, "azure-whatif.json");
            AssertEx.StringContains(finalManifest, "azure-deployment.json");
        }
        finally
        {
            Directory.Delete(workspaceRoot, recursive: true);
        }
    }

    private static Task InstallerEngineParsesJsonResultFromMixedPowerShellOutput()
    {
        var output = "\u001b[93mPlease select the account you want to login with.\u001b[0m\r\n" +
            "\r\nRetrieving subscriptions for the selection...\r\n" +
            "[Announcements]\r\n" +
            "With the new Azure PowerShell login experience, you can select the subscription you want to use more easily.\r\n\r\n" +
            """
            {
              "status": "Passed",
              "code": "AzureSignInCompleted",
              "summary": "Azure sign-in completed.",
              "details": "Current Azure context is tenant tenant-001, subscription sub-001.",
              "retrySafe": true,
              "data": {
                "tenantId": "tenant-001",
                "subscriptionId": "sub-001",
                "account": "admin@example.test"
              },
              "timestamp": "2026-07-11T03:06:04.5718873Z"
            }
            """;
        var method = typeof(InstallerEngine).GetMethod(
            "ParsePowerShellResults",
            BindingFlags.NonPublic | BindingFlags.Static);

        AssertEx.True(method is not null, "Parser method should exist.");
        var results = (IReadOnlyList<InstallerStepResult>)method!.Invoke(null, [output])!;

        AssertEx.Equal(1, results.Count);
        AssertEx.Equal(InstallStatus.Passed, results[0].Status);
        AssertEx.Equal("AzureSignInCompleted", results[0].Code);
        AssertEx.Equal("Azure Sign In", results[0].StepName);
        AssertEx.Equal("sub-001", results[0].Data["subscriptionId"]);

        var nameMethod = typeof(InstallerEngine).GetMethod(
            "NameFromCode",
            BindingFlags.NonPublic | BindingFlags.Static);
        AssertEx.True(nameMethod is not null, "Result-name mapper should exist.");
        AssertEx.Equal("Microsoft Graph Sites Module", (string)nameMethod!.Invoke(null, ["GraphSitesModuleMissing"])!);
        AssertEx.Equal("SharePoint Library", (string)nameMethod.Invoke(null, ["SharePointLibraryAccessFailed"])!);
        return Task.CompletedTask;
    }

    private static async Task InstallerEnginePassesGraphTokenToValidationProcess()
    {
        var workspaceRoot = CreateTempDirectory();
        try
        {
            var moduleDirectory = Path.Combine(workspaceRoot, "modules", "PageMaker365.Install");
            Directory.CreateDirectory(moduleDirectory);
            await File.WriteAllTextAsync(
                Path.Combine(moduleDirectory, "PageMaker365.Install.psd1"),
                "@{ RootModule = 'PageMaker365.Install.psm1'; ModuleVersion = '0.1.0'; FunctionsToExport = @('Test-PM365SmokeTests') }");
            await File.WriteAllTextAsync(
                Path.Combine(moduleDirectory, "PageMaker365.Install.psm1"),
                "function Test-PM365SmokeTests { param([string] $ConfigPath, [string] $DeploymentArtifactPath) " +
                "$received = $env:PM365_GRAPH_ACCESS_TOKEN -eq 'validation-token' -and $DeploymentArtifactPath -eq 'deployment-artifact.json'; " +
                "[pscustomobject]@{ status = if ($received) { 'Passed' } else { 'Failed' }; " +
                "code = 'ValidationGraphTokenReady'; summary = 'Graph token propagation checked.'; " +
                "details = if ($received) { 'Token received.' } else { 'Token missing.' }; retrySafe = $true; requiresApproval = $false; data = @{} } }; " +
                "Export-ModuleMember -Function Test-PM365SmokeTests");
            var configPath = Path.Combine(workspaceRoot, "customer.install.json");
            await File.WriteAllTextAsync(configPath, "{}");

            var engine = new InstallerEngine(new StructuredLogger(new RedactionService()));
            var session = engine.CreateSession(CreateConfig(), workspaceRoot);
            var results = await engine.RunValidationAsync(
                session,
                workspaceRoot,
                configPath,
                "validation-token",
                deploymentArtifactPath: "deployment-artifact.json");

            AssertEx.Equal(1, results.Count);
            AssertEx.Equal("ValidationGraphTokenReady", results[0].Code);
            AssertEx.Equal(InstallStatus.Passed, results[0].Status);
        }
        finally
        {
            Directory.Delete(workspaceRoot, recursive: true);
        }
    }

    private static async Task PowerShellProcessRunnerReturnsFailedResultOnTimeout()
    {
        var workspaceRoot = CreateTempDirectory();
        try
        {
            var result = await new PowerShellProcessRunner().RunAsync(
                "-NoLogo -NoProfile -NonInteractive -Command \"Write-Output 'before-timeout'; Start-Sleep -Seconds 10\"",
                workspaceRoot,
                timeout: TimeSpan.FromSeconds(2));

            AssertEx.False(result.Succeeded);
            AssertEx.True(result.TimedOut);
            AssertEx.False(result.Canceled);
            AssertEx.Equal(-1, result.ExitCode);
            AssertEx.StringContains(result.StandardOutput, "before-timeout");
            AssertEx.StringContains(result.StandardError, "timed out");
        }
        finally
        {
            Directory.Delete(workspaceRoot, recursive: true);
        }
    }

    private static async Task PowerShellProcessRunnerReturnsFailedResultOnCancellation()
    {
        var workspaceRoot = CreateTempDirectory();
        try
        {
            using var cancellation = new CancellationTokenSource();
            var progress = new InlineProgress<string>(line =>
            {
                if (line.Contains("before-cancel", StringComparison.Ordinal))
                {
                    cancellation.Cancel();
                }
            });
            var result = await new PowerShellProcessRunner().RunAsync(
                "-NoLogo -NoProfile -NonInteractive -Command \"Write-Output 'before-cancel'; Start-Sleep -Seconds 10\"",
                workspaceRoot,
                cancellation.Token,
                timeout: TimeSpan.FromSeconds(20),
                outputProgress: progress);

            AssertEx.False(result.Succeeded);
            AssertEx.False(result.TimedOut);
            AssertEx.True(result.Canceled);
            AssertEx.Equal(-2, result.ExitCode);
            AssertEx.StringContains(result.StandardOutput, "before-cancel");
            AssertEx.StringContains(result.StandardError, "canceled");
        }
        finally
        {
            Directory.Delete(workspaceRoot, recursive: true);
        }
    }

    private static async Task PowerShellProcessRunnerReportsLiveOutputLines()
    {
        var workspaceRoot = CreateTempDirectory();
        var outputLines = new List<string>();
        try
        {
            var progress = new InlineProgress<string>(line => outputLines.Add(line));
            var result = await new PowerShellProcessRunner().RunAsync(
                "-NoLogo -NoProfile -NonInteractive -Command \"[Console]::Out.WriteLine('progress-out'); [Console]::Error.WriteLine('progress-err')\"",
                workspaceRoot,
                outputProgress: progress);

            AssertEx.True(result.Succeeded, result.StandardError);
            AssertEx.Contains(outputLines, "progress-out");
            AssertEx.Contains(outputLines, "progress-err");
        }
        finally
        {
            Directory.Delete(workspaceRoot, recursive: true);
        }
    }

    private static async Task PowerShellProcessRunnerPassesScopedEnvironmentVariables()
    {
        var workspaceRoot = CreateTempDirectory();
        try
        {
            var result = await new PowerShellProcessRunner().RunAsync(
                "-NoLogo -NoProfile -NonInteractive -Command \"Write-Output $env:PM365_TEST_SCOPED_ENV\"",
                workspaceRoot,
                environmentVariables: new Dictionary<string, string>
                {
                    ["PM365_TEST_SCOPED_ENV"] = "scoped-value"
                });

            AssertEx.True(result.Succeeded, result.StandardError);
            AssertEx.StringContains(result.StandardOutput, "scoped-value");
        }
        finally
        {
            Directory.Delete(workspaceRoot, recursive: true);
        }
    }

    private static async Task AzureDiscoveryServiceReturnsPackageFallbackWhenModuleIsMissing()
    {
        var workspaceRoot = CreateTempDirectory();
        try
        {
            var result = await new AzureDiscoveryService().DiscoverAsync(workspaceRoot, CreateConfig());

            AssertEx.Equal("AzureDiscoveryFallback", result.Source);
            AssertEx.Equal("tenant-001", result.TenantId);
            AssertEx.Equal("sub-001", result.SelectedSubscriptionId);
            AssertEx.Equal("eastus", result.RecommendedLocation);
            AssertEx.Equal("rg-pm365-example", result.TargetResourceGroupName);
            AssertEx.Equal(1, result.AccessibleSubscriptions.Count);
            AssertEx.Equal("AzureDiscoveryModuleMissing", result.Findings[0].Code);
        }
        finally
        {
            Directory.Delete(workspaceRoot, recursive: true);
        }
    }

    private static Task AzureDiscoveryServiceMapsAzureResultIntoTenantDiscovery()
    {
        var discovery = CreateDiscovery();
        discovery.Azure.SelectedSubscriptionId = "";
        discovery.Azure.AccessibleSubscriptions.Clear();
        var azure = new AzureDiscoveryResult
        {
            AccountId = "azure-admin@example.test",
            TenantId = "tenant-live",
            SelectedSubscriptionId = "sub-live",
            SelectedSubscriptionName = "Live Subscription",
            SelectedSubscriptionState = "Enabled",
            RecommendedLocation = "westus3",
            TargetResourceGroupName = "rg-live",
            ResourceGroupExists = true,
            AccessibleSubscriptions =
            {
                new AzureSubscriptionDiscovery
                {
                    SubscriptionId = "sub-live",
                    DisplayName = "Live Subscription",
                    State = "Enabled"
                }
            },
            Findings =
            {
                new DiscoveryFinding
                {
                    Severity = "Info",
                    Code = "AzureDiscoveryReady",
                    Summary = "Azure discovery completed.",
                    Details = "Read-only metadata collected."
                }
            }
        };

        AzureDiscoveryService.ApplyToDiscovery(discovery, azure);

        AssertEx.Equal("tenant-live", discovery.Azure.TenantId);
        AssertEx.Equal("azure-admin@example.test", discovery.Azure.AccountId);
        AssertEx.Equal("sub-live", discovery.Azure.SelectedSubscriptionId);
        AssertEx.Equal("Live Subscription", discovery.Azure.SelectedSubscriptionName);
        AssertEx.Equal("Enabled", discovery.Azure.SelectedSubscriptionState);
        AssertEx.Equal("westus3", discovery.Azure.RecommendedLocation);
        AssertEx.Equal("rg-live", discovery.Azure.TargetResourceGroupName);
        AssertEx.True(discovery.Azure.ResourceGroupExists);
        AssertEx.Equal(1, discovery.Azure.AccessibleSubscriptions.Count);
        AssertEx.Equal("AzureDiscoveryReady", discovery.Findings.Last().Code);
        AssertEx.StringContains(discovery.Source, "AzurePowerShell");
        return Task.CompletedTask;
    }

    private static async Task GraphDiscoveryServiceReturnsPackageFallbackWhenModuleIsMissing()
    {
        var workspaceRoot = CreateTempDirectory();
        try
        {
            var result = await new GraphDiscoveryService().DiscoverAsync(
                workspaceRoot,
                CreateConfig(),
                allowSharePointDiscovery: true);

            AssertEx.Equal("GraphDiscoveryFallback", result.Source);
            AssertEx.Equal("tenant-001", result.TenantId);
            AssertEx.Equal("https://example.sharepoint.com/sites/intranet", result.SiteUrl);
            AssertEx.Equal("example.sharepoint.com", result.TenantHostname);
            AssertEx.Equal("Documents", result.DefaultDocumentLibrary);
            AssertEx.Equal("SitesSelected", result.PermissionMode);
            AssertEx.Equal("GraphDiscoveryModuleMissing", result.Findings[0].Code);
        }
        finally
        {
            Directory.Delete(workspaceRoot, recursive: true);
        }
    }

    private static Task GraphDiscoveryServiceMapsGraphResultIntoTenantDiscovery()
    {
        var discovery = CreateDiscovery();
        discovery.Customer.VerifiedDomains.Clear();
        discovery.SharePoint.SiteResolved = false;
        var graph = new GraphDiscoveryResult
        {
            AccountId = "graph-admin@example.test",
            TenantId = "tenant-live",
            Scopes = ["User.Read", "Domain.Read.All", "RoleManagement.Read.Directory", "Sites.Read.All"],
            DefaultDomain = "example.com",
            VerifiedDomains = ["example.com", "example.sharepoint.com"],
            TenantHostname = "example.sharepoint.com",
            SiteUrl = "https://example.sharepoint.com/sites/intranet",
            SiteId = "example.sharepoint.com,site-collection,site-id",
            SiteDisplayName = "Intranet",
            SiteResolved = true,
            DefaultDocumentLibrary = "Documents",
            DefaultDocumentLibraryId = "drive-live",
            PermissionMode = "SitesSelected",
            AvailableDocumentLibraries =
            {
                new SharePointDocumentLibraryDiscovery
                {
                    DriveId = "drive-live",
                    Name = "Documents",
                    WebUrl = "https://example.sharepoint.com/sites/intranet/Shared%20Documents",
                    DriveType = "documentLibrary"
                }
            },
            AppRegistrationMode = "Create",
            ConsentStatus = "AdminRoleReady",
            EntraPermissionMode = "SitesSelected",
            RequiredApplicationPermissions = ["Sites.Selected"],
            RequiredDelegatedScopes = ["openid", "profile", "email"],
            Findings =
            {
                new DiscoveryFinding
                {
                    Severity = "Info",
                    Code = "GraphDiscoveryReady",
                    Summary = "Graph discovery completed.",
                    Details = "Read-only metadata collected."
                }
            }
        };

        GraphDiscoveryService.ApplyToDiscovery(discovery, graph);

        AssertEx.Equal("example.com", discovery.Customer.DefaultDomain);
        AssertEx.Contains(discovery.Customer.VerifiedDomains, "example.com");
        AssertEx.Contains(discovery.Customer.VerifiedDomains, "example.sharepoint.com");
        AssertEx.Equal("example.sharepoint.com", discovery.SharePoint.TenantHostname);
        AssertEx.Equal("example.sharepoint.com,site-collection,site-id", discovery.SharePoint.SiteId);
        AssertEx.Equal("Intranet", discovery.SharePoint.SiteDisplayName);
        AssertEx.Equal("Documents", discovery.SharePoint.DefaultDocumentLibrary);
        AssertEx.Equal("drive-live", discovery.SharePoint.DefaultDocumentLibraryId);
        AssertEx.True(discovery.SharePoint.SiteResolved);
        AssertEx.Equal(1, discovery.SharePoint.AvailableDocumentLibraries.Count);
        AssertEx.Equal("graph-admin@example.test", discovery.Entra.AccountId);
        AssertEx.Contains(discovery.Entra.Scopes, "RoleManagement.Read.Directory");
        AssertEx.Equal("AdminRoleReady", discovery.Entra.ConsentStatus);
        AssertEx.Equal("Sites.Selected", discovery.Entra.RequiredApplicationPermissions[0]);
        AssertEx.Equal("GraphDiscoveryReady", discovery.Findings.Last().Code);
        AssertEx.StringContains(discovery.Source, "GraphPowerShell");
        return Task.CompletedTask;
    }

    private static OnboardingApiClient CreatePortalClient(
        RecordingHttpMessageHandler handler,
        bool fallbackToMock = false,
        string apiKeyEnvironmentVariable = "PM365_ONBOARDING_API_KEY")
    {
        return new OnboardingApiClient(
            new OnboardingApiOptions
            {
                Mode = "Portal",
                ApiBaseUrl = "https://localhost:5443",
                ApiKeyEnvironmentVariable = apiKeyEnvironmentVariable,
                FallbackToMockOnFailure = fallbackToMock
            },
            new HttpClient(handler));
    }

    private static SignedPackageFixture CreateSignedPackage(Action<CustomerInstallConfig>? mutate = null)
    {
        var generator = new Ed25519KeyPairGenerator();
        generator.Init(new Ed25519KeyGenerationParameters(new SecureRandom()));
        var keyPair = generator.GenerateKeyPair();
        var privateKey = (Ed25519PrivateKeyParameters)keyPair.Private;
        var publicKey = (Ed25519PublicKeyParameters)keyPair.Public;
        var publicKeyId = $"test-key-{Guid.NewGuid():N}";
        var publicKeyJwkX = Base64UrlEncode(publicKey.GetEncoded());
        var publicKeyPem = ToPem("PUBLIC KEY", SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(publicKey).GetDerEncoded());

        var config = CreateConfig();
        config.ControlPlane.TrustMode = "SignedRequired";
        config.ControlPlane.PublicKeyId = publicKeyId;
        config.ControlPlane.PackageHashAlgorithm = "SHA-256";
        config.ControlPlane.Canonicalization = "json-c14n-v1";
        config.ControlPlane.SignatureAlgorithm = "Ed25519";
        config.ControlPlane.PackageHash = "";
        config.ControlPlane.Signature = "";
        mutate?.Invoke(config);

        config.ControlPlane.PackageHash = CustomerConfigService.ComputePackageHash(CustomerConfigService.ToJson(config));
        config.ControlPlane.Signature = SignPackage(CustomerConfigService.ToJson(config), privateKey);
        return new SignedPackageFixture(config, CustomerConfigService.ToJson(config), publicKeyPem, publicKeyId, publicKeyJwkX);
    }

    private static PackageTrustOptions CreatePackageTrustOptions(SignedPackageFixture package)
    {
        return new PackageTrustOptions
        {
            TrustedPublicKeysById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [package.PublicKeyId] = package.PublicKeyPem
            }
        };
    }

    private static string SignPackage(string packageJson, Ed25519PrivateKeyParameters privateKey)
    {
        var payload = CustomerConfigService.GetPackageTrustPayloadBytes(packageJson);
        var signer = new Ed25519Signer();
        signer.Init(true, privateKey);
        signer.BlockUpdate(payload, 0, payload.Length);
        return Base64UrlEncode(signer.GenerateSignature());
    }

    private static string ToPem(string label, byte[] der)
    {
        var body = Convert.ToBase64String(der);
        var builder = new StringBuilder();
        builder.AppendLine($"-----BEGIN {label}-----");
        for (var index = 0; index < body.Length; index += 64)
        {
            builder.AppendLine(body.Substring(index, Math.Min(64, body.Length - index)));
        }

        builder.AppendLine($"-----END {label}-----");
        return builder.ToString();
    }

    private static string Base64UrlEncode(byte[] value)
    {
        return Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string CreateJwksJson(string publicKeyId, string publicKeyJwkX)
    {
        return $$"""
            {
              "keys": [
                {
                  "kty": "OKP",
                  "crv": "Ed25519",
                  "x": "{{publicKeyJwkX}}",
                  "kid": "{{publicKeyId}}",
                  "use": "sig",
                  "alg": "EdDSA"
                }
              ]
            }
            """;
    }

    private static OnboardingBootstrapSession CreateSession()
    {
        return new OnboardingBootstrapSession
        {
            SessionId = "onb_test_001",
            CustomerName = "Example Customer",
            ExpectedTenantId = "tenant-001",
            PortalBaseUrl = "https://localhost:5444",
            ApiBaseUrl = "https://localhost:5443",
            OneTimeCode = "TEST-CODE-001",
            RequestedBy = "owner@example.test",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
        };
    }

    private static InstallerEvidenceEvent CreateInstallerEvidence()
    {
        return new InstallerEvidenceEvent
        {
            EventId = "event-evidence-001",
            EventType = InstallerEvidenceEventType.InstallStarted,
            InstallAttemptId = "attempt-evidence-001",
            Sequence = 1,
            OccurredAt = new DateTimeOffset(2026, 7, 11, 18, 0, 0, TimeSpan.Zero),
            OnboardingSessionId = "onb_test_001",
            DeploymentExportId = "7f2d7c2e-4f1f-4c7d-8e7d-111111111111",
            LifecycleStatus = "provisioning",
            Outcome = "passed",
            InstallerVersion = "test-version",
            PackageHash = "sha256:test-hash",
            Message = "Installer started provisioning."
        };
    }

    private static TenantDiscoveryResult CreateDiscovery()
    {
        return new TenantDiscoveryResult
        {
            DiscoveryId = "disc_test_001",
            OnboardingSessionId = "onb_test_001",
            Source = "UnitTest",
            DataPolicy = "InstallReadinessOnly",
            Customer =
            {
                TenantId = "tenant-001",
                TenantName = "Example Customer",
                PrimaryContact = "owner@example.test",
                VerifiedDomains = ["example.test"]
            },
            Azure =
            {
                AccountId = "azure-admin@example.test",
                TenantId = "tenant-001",
                SelectedSubscriptionId = "sub-001",
                SelectedSubscriptionName = "Example Subscription",
                SelectedSubscriptionState = "Enabled",
                RecommendedLocation = "eastus",
                TargetResourceGroupName = "rg-pm365-example",
                ResourceGroupExists = true
            },
            SharePoint =
            {
                TenantHostname = "example.sharepoint.com",
                SiteUrl = "https://example.sharepoint.com/sites/intranet",
                SiteId = "site-001",
                DefaultDocumentLibrary = "Documents",
                SiteResolved = true
            }
        };
    }

    private static CustomerInstallConfig CreateConfig()
    {
        return new CustomerInstallConfig
        {
            Customer =
            {
                TenantName = "Example Customer",
                TenantId = "tenant-001",
                PrimaryContact = "owner@example.test"
            },
            Azure =
            {
                TenantId = "tenant-001",
                SubscriptionId = "sub-001",
                Location = "eastus",
                ResourceGroupName = "rg-pm365-example",
                Environment = "test"
            },
            SharePoint =
            {
                SiteUrl = "https://example.sharepoint.com/sites/intranet",
                DefaultDocumentLibrary = "Documents",
                PermissionMode = "SitesSelected"
            },
            App =
            {
                AppName = "PageMaker365",
                SupportEmail = "support@example.test"
            },
            Entra =
            {
                AppRegistrationMode = "Create",
                PermissionMode = "SitesSelected",
                RequiredApplicationPermissions = ["Sites.Selected"],
                RequiredDelegatedScopes = ["openid", "profile", "email"]
            },
            ControlPlane =
            {
                DeploymentExportId = "export-001",
                ExportedAt = "2026-07-07T00:00:00Z",
                Issuer = "PageMaker365 Control Plane",
                IssuerEnvironment = "test",
                OnboardingSessionId = "onb_test_001",
                DiscoveryId = "disc_test_001",
                SchemaId = "https://pagemaker365.com/schemas/customer-install.schema.json",
                EnvironmentId = "env-001",
                LicenseActivationId = "lic-001",
                EntitlementSyncUrl = "https://localhost:5443/api/runtime/entitlements/sync",
                PackageHashAlgorithm = "SHA-256",
                Canonicalization = "json-c14n-v1",
                TrustMode = "UnsignedAllowed"
            },
            Secrets =
            {
                RequiredSecretNames = ["runtime-api-secret"],
                PromptForSecrets =
                [
                    new SecretPromptInfo
                    {
                        Name = "runtime-api-secret",
                        Label = "Runtime API secret",
                        Required = true
                    }
                ]
            }
        };
    }

    private static OnboardingPackageReadiness CreateReadyReadiness()
    {
        return new OnboardingPackageReadiness
        {
            Status = "Ready",
            PackageVersion = "0.2-test",
            PackageDownloadUrl = "https://localhost:5443/custom/download"
        };
    }

    private static string CreateProvenancePackageJson(Action<CustomerInstallConfig> mutate)
    {
        var session = CreateSession();
        var discovery = CreateDiscovery();
        var config = CreateConfig();
        config.Customer.TenantId = session.ExpectedTenantId;
        config.Azure.TenantId = session.ExpectedTenantId;
        config.ControlPlane.OnboardingSessionId = session.SessionId;
        config.ControlPlane.DiscoveryId = discovery.DiscoveryId;
        mutate(config);
        return CustomerConfigService.ToJson(config);
    }

    private static async Task<OnboardingPackageDownloadResult> DownloadPackageAsync(
        string packageJson,
        string workspaceRoot)
    {
        var handler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(packageJson, Encoding.UTF8, "application/json")
        });
        var client = CreatePortalClient(handler);
        return await client.DownloadPackageAsync(CreateSession(), CreateReadyReadiness(), workspaceRoot, CreateDiscovery());
    }

    private static bool GeneratedPackageDirectoryExists(string workspaceRoot)
    {
        return Directory.Exists(Path.Combine(
            workspaceRoot,
            "support-bundle",
            "onboarding",
            "onb_test_001",
            "generated-package"));
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private static void AssertJsonString(JsonDocument document, string propertyName, string expected)
    {
        AssertJsonString(document.RootElement, propertyName, expected);
    }

    private static void AssertJsonString(JsonElement element, string propertyName, string expected)
    {
        AssertEx.True(element.TryGetProperty(propertyName, out var property), $"Missing JSON property: {propertyName}");
        AssertEx.Equal(expected, property.GetString());
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "pm365-installer-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PageMaker365.Installer.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate PageMaker365.Installer.sln from the test output directory.");
    }

    private sealed class RecordingHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];
        public List<string> RequestBodies { get; } = [];

        public IReadOnlyCollection<string> HeaderValues(string name)
        {
            return Requests[0].Headers.TryGetValue(name, out var values) ? values : [];
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var headers = request.Headers.ToDictionary(
                header => header.Key,
                header => (IReadOnlyCollection<string>)header.Value.ToList(),
                StringComparer.OrdinalIgnoreCase);
            Requests.Add(new CapturedRequest(
                request.Method,
                request.RequestUri,
                request.Headers.Authorization,
                headers));
            RequestBodies.Add(request.Content is null
                ? ""
                : await request.Content.ReadAsStringAsync(cancellationToken));
            return responseFactory(request);
        }
    }

    private sealed record CapturedRequest(
        HttpMethod Method,
        Uri? RequestUri,
        AuthenticationHeaderValue? Authorization,
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> Headers);

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly string? _originalValue;

        public EnvironmentVariableScope(string name, string? value)
        {
            Name = name;
            _originalValue = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public string Name { get; }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(Name, _originalValue);
        }
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value)
        {
            report(value);
        }
    }

    private sealed record SignedPackageFixture(
        CustomerInstallConfig Config,
        string Json,
        string PublicKeyPem,
        string PublicKeyId,
        string PublicKeyJwkX);
}

internal static class AssertEx
{
    public static void Equal<T>(T expected, T actual, string? message = null)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(message ?? $"Expected '{expected}', actual '{actual}'.");
        }
    }

    public static void True(bool condition, string? message = null)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message ?? "Expected condition to be true.");
        }
    }

    public static void False(bool condition, string? message = null)
    {
        if (condition)
        {
            throw new InvalidOperationException(message ?? "Expected condition to be false.");
        }
    }

    public static void Contains(IEnumerable<string> values, string expected)
    {
        if (!values.Contains(expected))
        {
            throw new InvalidOperationException($"Expected collection to contain '{expected}'.");
        }
    }

    public static void StringContains(string value, string expected)
    {
        if (!value.Contains(expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Expected '{value}' to contain '{expected}'.");
        }
    }

    public static async Task<TException> ThrowsAsync<TException>(Func<Task> action)
        where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException exception)
        {
            return exception;
        }

        throw new InvalidOperationException($"Expected exception of type {typeof(TException).Name}.");
    }
}
