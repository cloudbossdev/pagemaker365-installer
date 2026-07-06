using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using PageMaker365.Installer.Engine.Models;
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
            ("DownloadPackageAsync saves portal package to support bundle", DownloadPackageAsyncSavesPortalPackageToSupportBundle),
            ("ConnectAsync falls back to mock when portal fails", ConnectAsyncFallsBackToMockWhenPortalFails),
            ("OptionsService loads file and environment overrides", OptionsServiceLoadsFileAndEnvironmentOverrides),
            ("InstallerStateStore saves and loads active state", InstallerStateStoreSavesAndLoadsActiveState),
            ("InstallerStateStore ignores completed state for resume", InstallerStateStoreIgnoresCompletedStateForResume),
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
        AssertEx.Equal("https://api.example.test/api/onboarding/installer/connect", handler.Requests[0].RequestUri?.ToString());
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

    private static async Task SubmitDiscoveryAsyncSendsInstallReadinessDiscoveryPayload()
    {
        var handler = new RecordingHttpMessageHandler(_ => JsonResponse("""
            {
              "status": "Accepted",
              "sessionId": "onb_test_001",
              "discoveryId": "disc_test_001",
              "correlationId": "corr-discovery-001",
              "portalRecordUrl": "https://portal.example.test/admin/onboarding/onb_test_001",
              "message": "Accepted"
            }
            """));
        var client = CreatePortalClient(handler);

        var response = await client.SubmitDiscoveryAsync(CreateSession(), CreateDiscovery());

        AssertEx.Equal("Accepted", response.Status);
        AssertEx.Equal("disc_test_001", response.DiscoveryId);
        AssertEx.Equal("https://api.example.test/api/onboarding/installer/discovery", handler.Requests[0].RequestUri?.ToString());

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
              "portalRecordUrl": "https://portal.example.test/admin/onboarding/onb_test_001",
              "correlationId": "corr-status-001",
              "message": "Ready",
              "missingFields": [],
              "packageReadiness": {
                "status": "Ready",
                "packageVersion": "0.2-test",
                "packageDownloadUrl": "https://api.example.test/api/onboarding/installer/onb_test_001/install-package",
                "message": "Ready for download"
              }
            }
            """));
        var client = CreatePortalClient(handler);

        var status = await client.GetOnboardingStatusAsync(CreateSession(), CreateDiscovery(), CreateConfig());

        AssertEx.Equal("Ready", status.Status);
        AssertEx.Equal("Ready", status.PackageReadiness.Status);
        AssertEx.Equal("https://api.example.test/api/onboarding/installer/status", handler.Requests[0].RequestUri?.ToString());

        using var body = JsonDocument.Parse(handler.RequestBodies[0]);
        var loadedPackage = body.RootElement.GetProperty("loadedPackage");
        AssertJsonString(loadedPackage, "tenantId", "tenant-001");
        AssertJsonString(loadedPackage, "tenantName", "Example Customer");
        AssertJsonString(loadedPackage, "azureSubscriptionId", "sub-001");
        AssertJsonString(loadedPackage, "resourceGroupName", "rg-pm365-example");
        AssertJsonString(loadedPackage, "sharePointSiteUrl", "https://example.sharepoint.com/sites/intranet");
        AssertJsonString(loadedPackage, "sharePointTenantHostname", "example.sharepoint.com");
        AssertJsonString(loadedPackage, "packageHash", "hash-001");

        var requestJson = handler.RequestBodies[0];
        AssertEx.False(requestJson.Contains("secrets", StringComparison.OrdinalIgnoreCase), requestJson);
        AssertEx.False(requestJson.Contains("requiredSecretNames", StringComparison.OrdinalIgnoreCase), requestJson);
        AssertEx.False(requestJson.Contains("promptForSecrets", StringComparison.OrdinalIgnoreCase), requestJson);
        AssertEx.False(requestJson.Contains("Runtime API secret", StringComparison.OrdinalIgnoreCase), requestJson);
    }

    private static async Task DownloadPackageAsyncSavesPortalPackageToSupportBundle()
    {
        var packageJson = """{"contractVersion":"0.2","customer":{"tenantName":"Example Customer"}}""";
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
                new OnboardingPackageReadiness
                {
                    Status = "Ready",
                    PackageVersion = "0.2-test",
                    PackageDownloadUrl = "https://api.example.test/custom/download"
                },
                workspaceRoot);

            AssertEx.Equal("Downloaded", result.Status);
            AssertEx.Equal("0.2-test", result.PackageVersion);
            AssertEx.Equal("corr-package-001", result.CorrelationId);
            AssertEx.Equal("https://api.example.test/custom/download", handler.Requests[0].RequestUri?.ToString());
            AssertEx.True(File.Exists(result.PackagePath), result.PackagePath);
            AssertEx.Equal(packageJson, await File.ReadAllTextAsync(result.PackagePath));
            AssertEx.StringContains(result.PackagePath, Path.Combine("support-bundle", "onboarding", "onb_test_001", "generated-package"));
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

    private static Task OptionsServiceLoadsFileAndEnvironmentOverrides()
    {
        var workspaceRoot = CreateTempDirectory();
        using var mode = new EnvironmentVariableScope("PM365_ONBOARDING_MODE", "Portal");
        using var baseUrl = new EnvironmentVariableScope("PM365_ONBOARDING_API_BASE_URL", "https://override.example.test");
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
            AssertEx.Equal("https://override.example.test", options.ApiBaseUrl);
            AssertEx.Equal("/file/connect", options.ConnectEndpointPath);
            AssertEx.Equal("/custom/status", options.StatusEndpointPath);
            AssertEx.Equal(9, options.TimeoutSeconds);
            AssertEx.False(options.FallbackToMockOnFailure);
            AssertEx.Equal(
                "https://override.example.test/file/connect",
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
                }
            });

            var loaded = store.LoadMostRecentActive();

            AssertEx.True(File.Exists(path), path);
            AssertEx.True(loaded is not null, "Expected active state to load.");
            AssertEx.Equal("state-active-001", loaded!.StateId);
            AssertEx.Equal(5, loaded.CurrentStepNumber);
            AssertEx.Equal(InstallStatus.Passed, loaded.LastPreviewStatus);
            AssertEx.Equal("Azure What-If", loaded.PreviewResults[0].StepName);
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
            TenantId = "tenant-live",
            SelectedSubscriptionId = "sub-live",
            SelectedSubscriptionName = "Live Subscription",
            RecommendedLocation = "westus3",
            TargetResourceGroupName = "rg-live",
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
        AssertEx.Equal("sub-live", discovery.Azure.SelectedSubscriptionId);
        AssertEx.Equal("Live Subscription", discovery.Azure.SelectedSubscriptionName);
        AssertEx.Equal("westus3", discovery.Azure.RecommendedLocation);
        AssertEx.Equal("rg-live", discovery.Azure.TargetResourceGroupName);
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
            TenantId = "tenant-live",
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

        AssertEx.Contains(discovery.Customer.VerifiedDomains, "example.com");
        AssertEx.Contains(discovery.Customer.VerifiedDomains, "example.sharepoint.com");
        AssertEx.Equal("example.sharepoint.com", discovery.SharePoint.TenantHostname);
        AssertEx.Equal("example.sharepoint.com,site-collection,site-id", discovery.SharePoint.SiteId);
        AssertEx.Equal("Intranet", discovery.SharePoint.SiteDisplayName);
        AssertEx.Equal("Documents", discovery.SharePoint.DefaultDocumentLibrary);
        AssertEx.Equal("drive-live", discovery.SharePoint.DefaultDocumentLibraryId);
        AssertEx.True(discovery.SharePoint.SiteResolved);
        AssertEx.Equal(1, discovery.SharePoint.AvailableDocumentLibraries.Count);
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
                ApiBaseUrl = "https://api.example.test",
                ApiKeyEnvironmentVariable = apiKeyEnvironmentVariable,
                FallbackToMockOnFailure = fallbackToMock
            },
            new HttpClient(handler));
    }

    private static OnboardingBootstrapSession CreateSession()
    {
        return new OnboardingBootstrapSession
        {
            SessionId = "onb_test_001",
            CustomerName = "Example Customer",
            ExpectedTenantId = "tenant-001",
            PortalBaseUrl = "https://portal.example.test",
            ApiBaseUrl = "https://api.example.test",
            OneTimeCode = "TEST-CODE-001",
            RequestedBy = "owner@example.test",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
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
                TenantId = "tenant-001",
                SelectedSubscriptionId = "sub-001",
                SelectedSubscriptionName = "Example Subscription",
                RecommendedLocation = "eastus",
                TargetResourceGroupName = "rg-pm365-example"
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
                ResourceGroupName = "rg-pm365-example"
            },
            SharePoint =
            {
                SiteUrl = "https://example.sharepoint.com/sites/intranet",
                DefaultDocumentLibrary = "Documents",
                PermissionMode = "SitesSelected"
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
                EnvironmentId = "env-001",
                PackageHash = "hash-001"
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

        public EnvironmentVariableScope(string name, string value)
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
}
