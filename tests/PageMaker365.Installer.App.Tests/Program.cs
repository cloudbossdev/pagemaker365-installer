using System.IO;
using System.Text.Json;
using PageMaker365.Installer.App.ViewModels;
using PageMaker365.Installer.Engine.Models;
using PageMaker365.Installer.Engine.Services;

namespace PageMaker365.Installer.App.Tests;

internal static class Program
{
    public static async Task<int> Main()
    {
        var tests = new (string Name, Func<Task> Run)[]
        {
            ("LoadSamplePackageCommand loads sample package and enables sign-in", LoadSamplePackageCommandLoadsSamplePackageAndEnablesSignIn),
            ("CheckPackageReadinessCommand applies portal status and missing fields", CheckPackageReadinessCommandAppliesPortalStatusAndMissingFields),
            ("DownloadGeneratedPackageCommand loads downloaded portal package", DownloadGeneratedPackageCommandLoadsDownloadedPortalPackage),
            ("DownloadGeneratedPackageCommand rejects invalid downloaded package", DownloadGeneratedPackageCommandRejectsInvalidDownloadedPackage)
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

        Console.WriteLine($"{tests.Length - failed}/{tests.Length} installer app tests passed.");
        return failed == 0 ? 0 : 1;
    }

    private static async Task LoadSamplePackageCommandLoadsSamplePackageAndEnablesSignIn()
    {
        using var scope = TestScope.Create();
        var viewModel = scope.CreateViewModel();

        await viewModel.SelectSetupModeCommand.ExecuteAsync();
        await viewModel.LoadSamplePackageCommand.ExecuteAsync();

        AssertEx.Equal("Contoso Intranet", viewModel.CustomerName);
        AssertEx.StringContains(viewModel.AzureSubscription, "rg-pagemaker365-contoso-prod");
        AssertEx.Equal("https://contoso.sharepoint.com/sites/intranet", viewModel.SharePointSite);
        AssertEx.True(viewModel.ConnectAzureCommand.CanExecute(null), "Azure sign-in should unlock after loading a valid package.");
        AssertEx.NotEqual("Not checked", viewModel.PackageTrustStatus);
    }

    private static async Task CheckPackageReadinessCommandAppliesPortalStatusAndMissingFields()
    {
        using var scope = TestScope.Create();
        var client = new FakeOnboardingApiClient
        {
            Status = CreatePortalStatus(
                readinessStatus: "Ready",
                missingFields:
                [
                    new OnboardingMissingField
                    {
                        FieldKey = "sharePointSiteUrl",
                        Label = "SharePoint site URL",
                        Required = true,
                        Source = "Portal",
                        Notes = "Confirm the target workspace site."
                    }
                ])
        };
        var viewModel = scope.CreateViewModel(client);

        await viewModel.SelectSetupModeCommand.ExecuteAsync();
        await viewModel.LoadSampleBootstrapCommand.ExecuteAsync();
        await viewModel.CheckPackageReadinessCommand.ExecuteAsync();

        AssertEx.Equal("Ready", viewModel.PackageReadinessStatus);
        AssertEx.Equal("0.2-test", viewModel.PackageReadinessVersion);
        AssertEx.Equal(1, viewModel.PortalMissingFields.Count);
        AssertEx.Equal("sharePointSiteUrl", viewModel.PortalMissingFields[0].FieldKey);
        AssertEx.Equal("SharePoint site URL", viewModel.PortalMissingFields[0].Label);
        AssertEx.True(viewModel.DownloadGeneratedPackageCommand.CanExecute(null), "Ready portal package should enable download.");
        AssertEx.True(File.Exists(viewModel.PortalStatusOutputPath), viewModel.PortalStatusOutputPath);
    }

    private static async Task DownloadGeneratedPackageCommandLoadsDownloadedPortalPackage()
    {
        using var scope = TestScope.Create();
        var client = new FakeOnboardingApiClient
        {
            PackageJson = CustomerConfigService.ToJson(CreateConfig("Downloaded Customer"))
        };
        var viewModel = scope.CreateViewModel(client);

        await viewModel.SelectSetupModeCommand.ExecuteAsync();
        await viewModel.LoadSampleBootstrapCommand.ExecuteAsync();
        await viewModel.CheckPackageReadinessCommand.ExecuteAsync();
        await viewModel.DownloadGeneratedPackageCommand.ExecuteAsync();

        AssertEx.Equal(1, client.DownloadCalls);
        AssertEx.Equal("Downloaded Customer", viewModel.CustomerName);
        AssertEx.Equal("Downloaded", viewModel.PackageReadinessStatus);
        AssertEx.Equal("0.2-test", viewModel.PackageReadinessVersion);
        AssertEx.True(viewModel.ConnectAzureCommand.CanExecute(null), "Azure sign-in should unlock after loading the generated package.");
        AssertEx.True(File.Exists(viewModel.PackageDownloadPath), viewModel.PackageDownloadPath);
        AssertEx.True(File.Exists(viewModel.PortalSyncReceipt.ReceiptOutputPath), viewModel.PortalSyncReceipt.ReceiptOutputPath);
    }

    private static async Task DownloadGeneratedPackageCommandRejectsInvalidDownloadedPackage()
    {
        using var scope = TestScope.Create();
        var client = new FakeOnboardingApiClient
        {
            PackageJson = """
                {
                  "contractVersion": "0.2",
                  "customer": {
                    "tenantName": "Broken Customer"
                  },
                  "features": {
                    "knowledgeBase": true,
                    "customerPortal": true,
                    "billingIntegration": true
                  }
                }
                """
        };
        var viewModel = scope.CreateViewModel(client);

        await viewModel.SelectSetupModeCommand.ExecuteAsync();
        await viewModel.LoadSampleBootstrapCommand.ExecuteAsync();
        await viewModel.CheckPackageReadinessCommand.ExecuteAsync();
        await viewModel.DownloadGeneratedPackageCommand.ExecuteAsync();

        AssertEx.Equal(1, client.DownloadCalls);
        AssertEx.Equal("PackageInvalid", viewModel.PackageReadinessStatus);
        AssertEx.StringContains(viewModel.PackageReadinessSummary, "failed local validation");
        AssertEx.True(File.Exists(viewModel.PackageDownloadPath), viewModel.PackageDownloadPath);
        AssertEx.True(File.Exists(viewModel.PortalSyncReceipt.ReceiptOutputPath), viewModel.PortalSyncReceipt.ReceiptOutputPath);
    }

    private static OnboardingPortalStatus CreatePortalStatus(
        string readinessStatus = "Ready",
        IReadOnlyList<OnboardingMissingField>? missingFields = null)
    {
        return new OnboardingPortalStatus
        {
            ContractVersion = "0.1",
            SessionId = "onb_contoso_sandbox_001",
            CustomerName = "Contoso Intranet",
            Status = readinessStatus.Equals("Ready", StringComparison.OrdinalIgnoreCase) ? "Ready" : "Pending",
            PortalRecordUrl = "https://pagemaker365.com/admin/onboarding/onb_contoso_sandbox_001",
            CorrelationId = "corr-app-test-status",
            Message = "Package readiness returned by fake client.",
            MissingFields = missingFields?.ToList() ?? [],
            PackageReadiness = new OnboardingPackageReadiness
            {
                Status = readinessStatus,
                PackageVersion = "0.2-test",
                PackageDownloadUrl = "https://api.pagemaker365.com/api/onboarding/installer/onb_contoso_sandbox_001/install-package",
                Message = "Generated package is ready."
            }
        };
    }

    private static CustomerInstallConfig CreateConfig(string tenantName)
    {
        return new CustomerInstallConfig
        {
            ContractVersion = "0.2",
            Customer =
            {
                CustomerId = "cust-download",
                AccountKey = "download",
                InstallationId = "inst-download",
                TenantName = tenantName,
                TenantId = "tenant-download",
                PrimaryContact = "owner@example.test"
            },
            Azure =
            {
                TenantId = "tenant-download",
                SubscriptionId = "sub-download",
                Location = "eastus",
                ResourceGroupName = "rg-pm365-download",
                Environment = "test",
                ResourceNames =
                {
                    KeyVaultName = "kv-pm365-download",
                    StorageAccountName = "stpm365download",
                    LogAnalyticsName = "log-pm365-download",
                    ApplicationInsightsName = "ai-pm365-download",
                    AppServicePlanName = "asp-pm365-download",
                    ApiAppName = "app-pm365-download-api",
                    PortalAppName = "swa-pm365-download",
                    ManagedIdentityName = "id-pm365-download"
                }
            },
            SharePoint =
            {
                SiteUrl = "https://download.sharepoint.com/sites/intranet",
                DefaultDocumentLibrary = "Documents",
                PermissionMode = "SitesSelected"
            },
            App =
            {
                AppName = "pagemaker365-download",
                RuntimeBaseUrl = "https://download.pagemaker365.example",
                ApiBaseUrl = "https://download-api.pagemaker365.example",
                SupportEmail = "support@pagemaker365.com"
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
                BaseUrl = "https://pagemaker365.com",
                DeploymentExportId = "export-download",
                ExportedAt = "2026-07-07T00:00:00Z",
                ExpiresAt = "2026-08-06T00:00:00Z",
                Issuer = "PageMaker365 Control Plane",
                IssuerEnvironment = "test",
                OnboardingSessionId = "onb_contoso_sandbox_001",
                DiscoveryId = "disc-download",
                SchemaId = "https://pagemaker365.com/schemas/customer-install.schema.json",
                EnvironmentId = "env-download",
                LicenseActivationId = "lic-download",
                EntitlementSyncUrl = "https://api.pagemaker365.com/api/runtime/entitlements/sync",
                PackageHashAlgorithm = "SHA-256",
                Canonicalization = "json-c14n-v1",
                TrustMode = "UnsignedAllowed"
            },
            Secrets =
            {
                KeyVaultName = "kv-pm365-download",
                RequiredSecretNames = ["runtime-session-secret"],
                PromptForSecrets =
                [
                    new SecretPromptInfo
                    {
                        Name = "runtime-session-secret",
                        Label = "Runtime session secret",
                        Required = true,
                        GeneratedByInstaller = true
                    }
                ]
            },
            Features =
            {
                KnowledgeBase = true,
                CustomerPortal = true,
                BillingIntegration = true,
                Connectors = true
            },
            SmokeTests =
            {
                ApiHealthPath = "/health",
                PortalPath = "/",
                LicenseValidationPath = "/api/runtime/license/validate",
                EntitlementSyncPath = "/api/runtime/entitlements/sync"
            }
        };
    }

    private sealed class FakeOnboardingApiClient : IOnboardingApiClient
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };

        public string ConnectionLabel => "Fake portal";

        public OnboardingPortalStatus Status { get; set; } = CreatePortalStatus();

        public string PackageJson { get; set; } = CustomerConfigService.ToJson(CreateConfig("Downloaded Customer"));

        public int DownloadCalls { get; private set; }

        public Task<OnboardingSessionConnection> ConnectAsync(
            OnboardingBootstrapSession session,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new OnboardingSessionConnection
            {
                Status = "Connected",
                SessionId = session.SessionId,
                CorrelationId = "corr-app-test-connect",
                Message = "Connected"
            });
        }

        public Task<OnboardingDiscoverySubmission> SubmitDiscoveryAsync(
            OnboardingBootstrapSession session,
            TenantDiscoveryResult discovery,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new OnboardingDiscoverySubmission
            {
                Status = "Accepted",
                SessionId = session.SessionId,
                DiscoveryId = discovery.DiscoveryId,
                CorrelationId = "corr-app-test-discovery",
                PortalRecordUrl = "https://pagemaker365.com/admin/onboarding/" + session.SessionId,
                Message = "Accepted"
            });
        }

        public Task<OnboardingPortalStatus> GetOnboardingStatusAsync(
            OnboardingBootstrapSession session,
            TenantDiscoveryResult? discovery,
            CustomerInstallConfig? config,
            CancellationToken cancellationToken = default)
        {
            Status.SessionId = session.SessionId;
            return Task.FromResult(Status);
        }

        public Task<string> SaveStatusAsync(
            OnboardingPortalStatus status,
            string outputRoot,
            CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(outputRoot);
            var path = Path.Combine(outputRoot, "fake-portal-status.json");
            File.WriteAllText(path, JsonSerializer.Serialize(status, JsonOptions));
            return Task.FromResult(path);
        }

        public Task<OnboardingPackageDownloadResult> DownloadPackageAsync(
            OnboardingBootstrapSession session,
            OnboardingPackageReadiness readiness,
            string workspaceRoot,
            CancellationToken cancellationToken = default)
        {
            DownloadCalls++;
            var directory = Path.Combine(workspaceRoot, "support-bundle", "onboarding", session.SessionId, "generated-package");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "generated.customer.install.json");
            File.WriteAllText(path, PackageJson);
            return Task.FromResult(new OnboardingPackageDownloadResult
            {
                Status = "Downloaded",
                SessionId = session.SessionId,
                PackagePath = path,
                PackageVersion = readiness.PackageVersion,
                CorrelationId = "corr-app-test-download",
                Message = "Downloaded fake package."
            });
        }
    }

    private sealed class TestScope : IDisposable
    {
        private TestScope(string rootDirectory)
        {
            RootDirectory = rootDirectory;
        }

        public string RootDirectory { get; }

        public static TestScope Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "pm365-installer-app-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return new TestScope(root);
        }

        public InstallerWizardViewModel CreateViewModel(FakeOnboardingApiClient? client = null)
        {
            var stateStore = new InstallerStateStore(Path.Combine(RootDirectory, "state"));
            return new InstallerWizardViewModel(client ?? new FakeOnboardingApiClient(), stateStore, RootDirectory);
        }

        public void Dispose()
        {
            if (!Directory.Exists(RootDirectory))
            {
                return;
            }

            try
            {
                Directory.Delete(RootDirectory, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static class AssertEx
    {
        public static void Equal<T>(T expected, T actual)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
            }
        }

        public static void NotEqual<T>(T notExpected, T actual)
        {
            if (EqualityComparer<T>.Default.Equals(notExpected, actual))
            {
                throw new InvalidOperationException($"Did not expect '{actual}'.");
            }
        }

        public static void True(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        public static void StringContains(string value, string expected)
        {
            if (!value.Contains(expected, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Expected '{value}' to contain '{expected}'.");
            }
        }
    }
}
