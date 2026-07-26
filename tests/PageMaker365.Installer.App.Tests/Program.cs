using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
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
            ("RelayCommand reports asynchronous operation state", RelayCommandReportsAsynchronousOperationState),
            ("Package step locks sign-in until a package is validated", PackageStepLocksSignInUntilPackageIsValidated),
            ("LoadSamplePackageCommand loads sample package and enables sign-in", LoadSamplePackageCommandLoadsSamplePackageAndEnablesSignIn),
            ("Canceled Graph sign-in clears stale code and remains retryable", CanceledGraphSignInClearsStaleCodeAndRemainsRetryable),
            ("Local downloaded package path remains supported", LocalDownloadedPackagePathRemainsSupported),
            ("Bootstrap loader rejects customer package without terminating session", BootstrapLoaderRejectsCustomerPackageWithoutTerminatingSession),
            ("Bootstrap loader rejects expired setup file", BootstrapLoaderRejectsExpiredSetupFile),
            ("Loaded bootstrap expiry blocks portal acquisition", LoadedBootstrapExpiryBlocksPortalAcquisition),
            ("Resume session restores saved bootstrap without blocking", ResumeSessionRestoresSavedBootstrapWithoutBlocking),
            ("Portal acquisition connects downloads and advances to sign-in", PortalAcquisitionConnectsDownloadsAndAdvancesToSignIn),
            ("Portal acquisition failure stays retryable on package step", PortalAcquisitionFailureStaysRetryableOnPackageStep),
            ("Portal authorization rejection requests a new setup file", PortalAuthorizationRejectionRequestsNewSetupFile),
            ("Portal acquisition redownloads a previously downloaded package", PortalAcquisitionRedownloadsPreviouslyDownloadedPackage),
            ("Portal acquisition polls pending package until ready", PortalAcquisitionPollsPendingPackageUntilReady),
            ("Portal acquisition exposes missing fields without requesting sign-in", PortalAcquisitionExposesMissingFieldsWithoutRequestingSignIn),
            ("CheckPackageReadinessCommand applies portal status and missing fields", CheckPackageReadinessCommandAppliesPortalStatusAndMissingFields),
            ("SyncDiscoveryCommand blocks portal sync when policy disallows portal sync", SyncDiscoveryCommandBlocksPortalSyncWhenPolicyDisallowsPortalSync),
            ("CheckPackageReadinessCommand blocks portal status when policy disallows portal sync", CheckPackageReadinessCommandBlocksPortalStatusWhenPolicyDisallowsPortalSync),
            ("DownloadGeneratedPackageCommand blocks package download when operation is not allowed", DownloadGeneratedPackageCommandBlocksPackageDownloadWhenOperationIsNotAllowed),
            ("SyncDiscoveryCommand calls portal client when policy allows portal sync", SyncDiscoveryCommandCallsPortalClientWhenPolicyAllowsPortalSync),
            ("DownloadGeneratedPackageCommand loads downloaded portal package", DownloadGeneratedPackageCommandLoadsDownloadedPortalPackage),
            ("Evidence sync failure keeps package event in persisted outbox", EvidenceSyncFailureKeepsPackageEventInPersistedOutbox),
            ("Removal workflow enables Azure inventory but keeps removal gated", RemovalWorkflowEnablesAzureInventoryButKeepsRemovalGated),
            ("DownloadGeneratedPackageCommand rejects provenance mismatch without loading package", DownloadGeneratedPackageCommandRejectsProvenanceMismatchWithoutLoadingPackage),
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

    private static async Task RelayCommandReportsAsynchronousOperationState()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var isRunning = false;
        var command = new RelayCommand(
            async () =>
            {
                started.SetResult();
                await release.Task;
            },
            runningChanged: value => isRunning = value);

        var execution = command.ExecuteAsync();
        await started.Task;
        AssertEx.True(isRunning, "The operation indicator should be active while the command is awaiting work.");
        AssertEx.False(command.CanExecute(null), "A running command should not execute twice.");

        release.SetResult();
        await execution;
        AssertEx.False(isRunning, "The operation indicator should stop when the command completes.");
    }

    private static async Task PackageStepLocksSignInUntilPackageIsValidated()
    {
        using var scope = TestScope.Create();
        var viewModel = scope.CreateViewModel();

        await viewModel.SelectSetupModeCommand.ExecuteAsync();
        await viewModel.NextCommand.ExecuteAsync();

        AssertEx.True(viewModel.IsPackageStep, "Setup should present package acquisition before sign-in.");
        AssertEx.False(viewModel.CanGoNext, "Sign-in must stay locked until a customer package passes local validation.");
        AssertEx.False(viewModel.NextCommand.CanExecute(null), "The Next command must not bypass package validation.");
        AssertEx.False(viewModel.GoToStepCommand.CanExecute(3), "Direct navigation must not bypass package validation.");
        AssertEx.False(viewModel.ConnectAzureCommand.CanExecute(null), "Azure sign-in must remain unavailable without a validated package.");
        AssertEx.False(viewModel.ConnectGraphCommand.CanExecute(null), "Graph sign-in must remain unavailable without a validated package.");
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
        AssertEx.True(viewModel.ConnectGraphCommand.CanExecute(null), "Graph sign-in should unlock after loading a valid package.");
        AssertEx.False(viewModel.RunPreflightCommand.CanExecute(null), "Preflight must remain locked until Azure and Graph sign-in both complete.");
        AssertEx.False(viewModel.GoToStepCommand.CanExecute(4), "The Preflight step must remain inaccessible until both sign-ins complete.");
        AssertEx.False(viewModel.CanGoNext, "Next must remain disabled while either required sign-in is incomplete.");
        AssertEx.NotEqual("Not checked", viewModel.PackageTrustStatus);
    }

    private static async Task CanceledGraphSignInClearsStaleCodeAndRemainsRetryable()
    {
        using var scope = TestScope.Create();
        var engine = new InstallerEngine(
            new StructuredLogger(new RedactionService()),
            new PromptThenCancelGraphAuthenticator());
        var viewModel = scope.CreateViewModel(engine: engine);

        await viewModel.SelectSetupModeCommand.ExecuteAsync();
        await viewModel.LoadSamplePackageCommand.ExecuteAsync();
        await viewModel.ConnectGraphCommand.ExecuteAsync();

        AssertEx.Equal("Canceled", viewModel.GraphSignInStatus);
        AssertEx.Equal("", viewModel.GraphDeviceCode);
        AssertEx.Equal("", viewModel.GraphDeviceCodeStatus);
        AssertEx.False(viewModel.HasGraphDeviceCode, "Canceled sign-in must not leave an expired device code visible.");
        AssertEx.False(viewModel.IsOperationRunning, "Canceled sign-in must return the app to an idle state.");
        AssertEx.True(viewModel.ConnectGraphCommand.CanExecute(null), "Canceled Graph sign-in must remain retryable.");
        AssertEx.False(viewModel.RunPreflightCommand.CanExecute(null), "Canceled Graph sign-in must not unlock Preflight.");
        AssertEx.StringContains(viewModel.FooterStatus, "canceled");
    }

    private static async Task LocalDownloadedPackagePathRemainsSupported()
    {
        using var scope = TestScope.Create();
        var packagePath = scope.WritePackage(CreateConfig("Local Package Customer"));
        var viewModel = scope.CreateViewModel();

        await viewModel.SelectSetupModeCommand.ExecuteAsync();
        await viewModel.LoadSamplePackageCommand.ExecuteAsync();

        AssertEx.Equal(Path.GetFullPath(packagePath), Path.GetFullPath(viewModel.PackagePath));
        AssertEx.Equal("Local Package Customer", viewModel.CustomerName);
        AssertEx.True(viewModel.IsSignInStep, "A valid local package should use the same sign-in flow as a portal package.");
        AssertEx.True(viewModel.ConnectAzureCommand.CanExecute(null), "A validated local package should enable Azure sign-in.");
        AssertEx.True(viewModel.ConnectGraphCommand.CanExecute(null), "A validated local package should enable Graph sign-in.");
        AssertEx.False(viewModel.IsOperationRunning, "Local package validation should return the activity indicator to idle.");
    }

    private static async Task BootstrapLoaderRejectsCustomerPackageWithoutTerminatingSession()
    {
        using var scope = TestScope.Create();
        var viewModel = scope.CreateViewModel();
        var packagePath = Path.Combine(scope.RootDirectory, "cloudboss.customer.install.json");
        await File.WriteAllTextAsync(packagePath, CustomerConfigService.ToJson(CreateConfig("CloudBoss")));

        var loaded = await viewModel.LoadBootstrapFileAsync(packagePath);

        AssertEx.False(loaded, "A customer install package must not load as an onboarding bootstrap.");
        AssertEx.Equal("Not connected", viewModel.OnboardingSessionId);
        AssertEx.StringContains(viewModel.FooterStatus, "not a valid PageMaker365 setup file");
        AssertEx.StringContains(viewModel.FooterStatus, "Alternative installation options");
    }

    private static async Task BootstrapLoaderRejectsExpiredSetupFile()
    {
        using var scope = TestScope.Create();
        var bootstrap = CreateBootstrap(allowPortalSync: true);
        bootstrap.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        var bootstrapPath = scope.WriteBootstrap(bootstrap);
        var viewModel = scope.CreateViewModel();

        await viewModel.SelectSetupModeCommand.ExecuteAsync();
        await viewModel.NextCommand.ExecuteAsync();
        var loaded = await viewModel.LoadBootstrapFileAsync(bootstrapPath);

        AssertEx.False(loaded, "An expired PageMaker365 setup file must not become the active onboarding session.");
        AssertEx.True(viewModel.IsPackageStep, "Rejected setup files must keep the operator on Package.");
        AssertEx.StringContains(viewModel.FooterStatus, "expired");
        AssertEx.False(viewModel.AcquirePortalPackageCommand.CanExecute(null), "Package acquisition must remain unavailable for an expired setup file.");
        AssertEx.False(viewModel.CanGoNext, "An expired setup file must not unlock sign-in.");
    }

    private static async Task LoadedBootstrapExpiryBlocksPortalAcquisition()
    {
        using var scope = TestScope.Create();
        var bootstrapPath = scope.WriteBootstrap(CreateBootstrap(allowPortalSync: true));
        var client = new FakeOnboardingApiClient();
        var viewModel = scope.CreateViewModel(client);

        await viewModel.SelectSetupModeCommand.ExecuteAsync();
        await viewModel.NextCommand.ExecuteAsync();
        AssertEx.True(await viewModel.LoadBootstrapFileAsync(bootstrapPath), "Valid bootstrap should load before the simulated expiry.");

        var bootstrapField = typeof(InstallerWizardViewModel).GetField(
            "_bootstrapSession",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var activeBootstrap = bootstrapField?.GetValue(viewModel) as OnboardingBootstrapSession;
        AssertEx.True(activeBootstrap is not null, "The test must locate the active bootstrap session.");
        activeBootstrap!.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1);

        await viewModel.AcquirePortalPackageAsync();

        AssertEx.Equal(0, client.ConnectCalls);
        AssertEx.True(viewModel.IsPackageStep, "An expired loaded setup file must remain on Package.");
        AssertEx.StringContains(viewModel.FooterStatus, "expired");
        AssertEx.StringContains(viewModel.FooterStatus, "new setup file");
        AssertEx.False(viewModel.CanGoNext, "Expiry after load must not unlock sign-in.");
    }

    private static async Task PortalAcquisitionConnectsDownloadsAndAdvancesToSignIn()
    {
        using var scope = TestScope.Create();
        var bootstrapPath = scope.WriteBootstrap(CreateBootstrap(allowPortalSync: true));
        var connectStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var connectRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new FakeOnboardingApiClient
        {
            PackageJson = CustomerConfigService.ToJson(CreateConfig("Portal Customer")),
            ConnectStarted = connectStarted,
            ConnectRelease = connectRelease
        };
        var viewModel = scope.CreateViewModel(client);

        await viewModel.SelectSetupModeCommand.ExecuteAsync();
        await viewModel.NextCommand.ExecuteAsync();
        AssertEx.True(await viewModel.LoadBootstrapFileAsync(bootstrapPath), "Valid bootstrap should load.");
        var acquisition = viewModel.AcquirePortalPackageCommand.ExecuteAsync();
        await connectStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        AssertEx.True(viewModel.IsOperationRunning, "Package acquisition should show global activity while portal work is in flight.");
        AssertEx.True(viewModel.IsPortalPackageAcquisitionRunning, "Package acquisition should expose its focused running state.");

        connectRelease.SetResult();
        await acquisition;

        AssertEx.Equal(1, client.ConnectCalls);
        AssertEx.Equal(1, client.StatusCalls);
        AssertEx.Equal(1, client.DownloadCalls);
        AssertEx.Equal("Portal Customer", viewModel.CustomerName);
        AssertEx.Equal("Downloaded", viewModel.PackageReadinessStatus);
        AssertEx.True(viewModel.IsSignInStep, "Successful portal acquisition should advance to the normal sign-in step.");
        AssertEx.False(viewModel.IsPortalPackageAcquisitionRunning, "Package acquisition should clear its running state after success.");
        AssertEx.False(viewModel.IsOperationRunning, "Package acquisition should return the global activity indicator to idle.");
    }

    private static async Task PortalAcquisitionFailureStaysRetryableOnPackageStep()
    {
        using var scope = TestScope.Create();
        var bootstrapPath = scope.WriteBootstrap(CreateBootstrap(allowPortalSync: true));
        var client = new FakeOnboardingApiClient
        {
            ConnectFailure = new HttpRequestException("Simulated portal outage"),
            PackageJson = CustomerConfigService.ToJson(CreateConfig("Recovered Portal Customer"))
        };
        var viewModel = scope.CreateViewModel(client);

        await viewModel.SelectSetupModeCommand.ExecuteAsync();
        await viewModel.NextCommand.ExecuteAsync();
        AssertEx.True(await viewModel.LoadBootstrapFileAsync(bootstrapPath), "Valid bootstrap should load.");
        await viewModel.AcquirePortalPackageCommand.ExecuteAsync();

        AssertEx.True(viewModel.IsPackageStep, "A portal failure must keep the user on package acquisition.");
        AssertEx.False(viewModel.CanGoNext, "A portal failure must not unlock sign-in.");
        AssertEx.True(viewModel.AcquirePortalPackageCommand.CanExecute(null), "The selected setup session should remain available for retry.");
        AssertEx.False(viewModel.IsPortalPackageAcquisitionRunning, "A failed package acquisition should clear its focused running state.");
        AssertEx.False(viewModel.IsOperationRunning, "A failed package acquisition should return global activity to idle.");

        client.ConnectFailure = null;
        await viewModel.AcquirePortalPackageCommand.ExecuteAsync();

        AssertEx.Equal(2, client.ConnectCalls);
        AssertEx.True(viewModel.IsSignInStep, "Retry should continue to sign-in after the package downloads and validates.");
        AssertEx.Equal("Recovered Portal Customer", viewModel.CustomerName);
        AssertEx.False(viewModel.IsOperationRunning, "A successful retry should return global activity to idle.");
    }

    private static async Task PortalAuthorizationRejectionRequestsNewSetupFile()
    {
        using var scope = TestScope.Create();
        var bootstrapPath = scope.WriteBootstrap(CreateBootstrap(allowPortalSync: true));
        var client = new FakeOnboardingApiClient
        {
            ConnectFailure = new OnboardingApiException(
                "Expired onboarding code",
                new Uri("https://api.example.test/api/onboarding/installer/connect"),
                HttpStatusCode.Unauthorized,
                "corr-expired-setup")
        };
        var viewModel = scope.CreateViewModel(client);

        await viewModel.SelectSetupModeCommand.ExecuteAsync();
        await viewModel.NextCommand.ExecuteAsync();
        AssertEx.True(await viewModel.LoadBootstrapFileAsync(bootstrapPath), "Valid bootstrap should load before portal authorization is checked.");
        await viewModel.AcquirePortalPackageCommand.ExecuteAsync();

        AssertEx.Equal(1, client.ConnectCalls);
        AssertEx.Equal(0, client.DownloadCalls);
        AssertEx.True(viewModel.IsPackageStep, "Rejected setup authorization must keep the operator on Package.");
        AssertEx.StringContains(viewModel.FooterStatus, "rejected or expired");
        AssertEx.StringContains(viewModel.FooterStatus, "new PageMaker365 setup file");
        AssertEx.StringContains(viewModel.FooterStatus, "corr-expired-setup");
        AssertEx.False(viewModel.CanGoNext, "Rejected setup authorization must not unlock sign-in.");
        AssertEx.False(viewModel.IsOperationRunning, "Rejected authorization must return the UI to an idle retryable state.");
    }

    private static async Task ResumeSessionRestoresSavedBootstrapWithoutBlocking()
    {
        using var scope = TestScope.Create();
        var bootstrap = CreateBootstrap(allowPortalSync: true);
        var bootstrapPath = scope.WriteBootstrap(bootstrap);
        var initialViewModel = scope.CreateViewModel();

        await initialViewModel.SelectSetupModeCommand.ExecuteAsync();
        await initialViewModel.NextCommand.ExecuteAsync();
        AssertEx.True(await initialViewModel.LoadBootstrapFileAsync(bootstrapPath), "Valid bootstrap should load and save the active session.");

        var resumedViewModel = scope.CreateViewModel();
        AssertEx.True(resumedViewModel.HasRestorableSession, "The saved bootstrap session should be offered for resume.");

        var resumeTask = resumedViewModel.ResumeSessionCommand.ExecuteAsync();
        var completedTask = await Task.WhenAny(resumeTask, Task.Delay(TimeSpan.FromSeconds(2)));

        AssertEx.True(ReferenceEquals(resumeTask, completedTask), "Resume should not block while loading the saved bootstrap.");
        await resumeTask;
        AssertEx.False(resumedViewModel.HasRestorableSession, "The saved-session prompt should close after resume.");
        AssertEx.Equal(bootstrap.SessionId, resumedViewModel.OnboardingSessionId);
        AssertEx.True(resumedViewModel.AcquirePortalPackageCommand.CanExecute(null), "Portal package acquisition should be available after resume.");
    }

    private static async Task PortalAcquisitionPollsPendingPackageUntilReady()
    {
        using var scope = TestScope.Create();
        var bootstrapPath = scope.WriteBootstrap(CreateBootstrap(allowPortalSync: true));
        var client = new FakeOnboardingApiClient
        {
            PackageJson = CustomerConfigService.ToJson(CreateConfig("Polling Customer"))
        };
        client.StatusSequence.Enqueue(CreatePortalStatus("NotReady"));
        client.StatusSequence.Enqueue(CreatePortalStatus("Ready"));
        var viewModel = scope.CreateViewModel(client);

        await viewModel.SelectSetupModeCommand.ExecuteAsync();
        await viewModel.NextCommand.ExecuteAsync();
        AssertEx.True(await viewModel.LoadBootstrapFileAsync(bootstrapPath), "Valid bootstrap should load.");
        await viewModel.AcquirePortalPackageCommand.ExecuteAsync();

        AssertEx.Equal(2, client.StatusCalls);
        AssertEx.Equal(1, client.DownloadCalls);
        AssertEx.True(viewModel.IsSignInStep, "Package acquisition should advance after polling reaches Ready.");
    }

    private static async Task PortalAcquisitionRedownloadsPreviouslyDownloadedPackage()
    {
        using var scope = TestScope.Create();
        var bootstrapPath = scope.WriteBootstrap(CreateBootstrap(allowPortalSync: true));
        var client = new FakeOnboardingApiClient
        {
            Status = CreatePortalStatus("Downloaded"),
            PackageJson = CustomerConfigService.ToJson(CreateConfig("Redownloaded Customer"))
        };
        var viewModel = scope.CreateViewModel(client);

        await viewModel.SelectSetupModeCommand.ExecuteAsync();
        await viewModel.NextCommand.ExecuteAsync();
        AssertEx.True(await viewModel.LoadBootstrapFileAsync(bootstrapPath), "Valid bootstrap should load.");
        await viewModel.AcquirePortalPackageAsync();

        AssertEx.Equal(1, client.StatusCalls);
        AssertEx.Equal(1, client.DownloadCalls);
        AssertEx.Equal("Redownloaded Customer", viewModel.CustomerName);
        AssertEx.Equal("Downloaded", viewModel.PackageReadinessStatus);
        AssertEx.True(viewModel.IsSignInStep, "A previously downloaded portal package should be downloaded again and advance to sign-in.");
    }

    private static async Task PortalAcquisitionExposesMissingFieldsWithoutRequestingSignIn()
    {
        using var scope = TestScope.Create();
        var bootstrapPath = scope.WriteBootstrap(CreateBootstrap(allowPortalSync: true));
        var client = new FakeOnboardingApiClient
        {
            Status = CreatePortalStatus(
                "NeedsCustomerInput",
                [
                    new OnboardingMissingField
                    {
                        FieldKey = "supportEmail",
                        Label = "Support email",
                        Required = true,
                        Source = "Portal",
                        Notes = "Required for package generation."
                    }
                ])
        };
        var viewModel = scope.CreateViewModel(client);

        await viewModel.SelectSetupModeCommand.ExecuteAsync();
        await viewModel.NextCommand.ExecuteAsync();
        AssertEx.True(await viewModel.LoadBootstrapFileAsync(bootstrapPath), "Valid bootstrap should load.");
        await viewModel.AcquirePortalPackageAsync();

        AssertEx.Equal(0, client.DownloadCalls);
        AssertEx.True(viewModel.IsPackageStep, "Missing portal fields should keep the user on the package step.");
        AssertEx.True(viewModel.IsPortalDiscoveryRequired, "Missing portal fields should reveal the recovery details.");
        AssertEx.StringContains(viewModel.FooterStatus, "customer portal");
        AssertEx.False(viewModel.ConnectAzureCommand.CanExecute(null), "Azure sign-in must remain unavailable until a valid package is loaded.");
        AssertEx.False(viewModel.CanGoNext, "Pending customer input must not unlock sign-in.");
        AssertEx.True(viewModel.AcquirePortalPackageCommand.CanExecute(null), "The setup session should remain available for retry after portal input is completed.");
        AssertEx.False(viewModel.IsPortalPackageAcquisitionRunning, "Pending package acquisition should clear its focused running state.");
        AssertEx.False(viewModel.IsOperationRunning, "Pending package acquisition should return global activity to idle.");
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

    private static async Task SyncDiscoveryCommandBlocksPortalSyncWhenPolicyDisallowsPortalSync()
    {
        using var scope = TestScope.Create();
        scope.WriteBootstrap(CreateBootstrap(allowPortalSync: false));
        var client = new FakeOnboardingApiClient();
        var viewModel = scope.CreateViewModel(client);

        await viewModel.SelectSetupModeCommand.ExecuteAsync();
        await viewModel.LoadSampleBootstrapCommand.ExecuteAsync();
        await viewModel.RunDiscoveryCommand.ExecuteAsync();
        await viewModel.SyncDiscoveryCommand.ExecuteAsync();

        AssertEx.Equal(0, client.SubmitDiscoveryCalls);
        AssertEx.Equal(0, client.StatusCalls);
        AssertEx.False(viewModel.SyncDiscoveryCommand.CanExecute(null), "Portal sync should be disabled when discoveryPolicy.allowPortalSync is false.");
        AssertEx.StringContains(viewModel.PortalSyncStatus, "not allowed");
    }

    private static async Task CheckPackageReadinessCommandBlocksPortalStatusWhenPolicyDisallowsPortalSync()
    {
        using var scope = TestScope.Create();
        scope.WriteBootstrap(CreateBootstrap(allowPortalSync: false));
        var client = new FakeOnboardingApiClient();
        var viewModel = scope.CreateViewModel(client);

        await viewModel.SelectSetupModeCommand.ExecuteAsync();
        await viewModel.LoadSampleBootstrapCommand.ExecuteAsync();
        await viewModel.CheckPackageReadinessCommand.ExecuteAsync();

        AssertEx.Equal(0, client.StatusCalls);
        AssertEx.Equal(0, client.SaveStatusCalls);
        AssertEx.StringContains(viewModel.OnboardingStatus, "not allowed");
        AssertEx.StringContains(viewModel.PackageReadinessStatus, "blocked");
    }

    private static async Task DownloadGeneratedPackageCommandBlocksPackageDownloadWhenOperationIsNotAllowed()
    {
        using var scope = TestScope.Create();
        scope.WriteBootstrap(CreateBootstrap(allowPortalSync: true, allowPackageGeneration: false));
        var client = new FakeOnboardingApiClient();
        var viewModel = scope.CreateViewModel(client);

        await viewModel.SelectSetupModeCommand.ExecuteAsync();
        await viewModel.LoadSampleBootstrapCommand.ExecuteAsync();
        await viewModel.CheckPackageReadinessCommand.ExecuteAsync();
        await viewModel.DownloadGeneratedPackageCommand.ExecuteAsync();

        AssertEx.Equal(1, client.StatusCalls);
        AssertEx.Equal(0, client.DownloadCalls);
        AssertEx.False(viewModel.DownloadGeneratedPackageCommand.CanExecute(null), "Package download should be disabled when InstallPackageGeneration is not allowed.");
        AssertEx.StringContains(viewModel.PackageReadinessStatus, "blocked");
    }

    private static async Task SyncDiscoveryCommandCallsPortalClientWhenPolicyAllowsPortalSync()
    {
        using var scope = TestScope.Create();
        scope.WriteBootstrap(CreateBootstrap(allowPortalSync: true));
        var client = new FakeOnboardingApiClient();
        var viewModel = scope.CreateViewModel(client);

        await viewModel.SelectSetupModeCommand.ExecuteAsync();
        await viewModel.LoadSampleBootstrapCommand.ExecuteAsync();
        await viewModel.RunDiscoveryCommand.ExecuteAsync();
        await viewModel.SyncDiscoveryCommand.ExecuteAsync();

        AssertEx.Equal(1, client.SubmitDiscoveryCalls);
        AssertEx.Equal(1, client.StatusCalls);
        AssertEx.Equal("Ready", viewModel.PackageReadinessStatus);
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
        AssertEx.Equal("https://download.pagemaker365.example", viewModel.DeployedSiteUrl);
        AssertEx.True(viewModel.HasDeployedSiteUrl, "A loaded package should expose its deployed runtime URL.");
        AssertEx.True(viewModel.ConnectAzureCommand.CanExecute(null), "Azure sign-in should unlock after loading the generated package.");
        AssertEx.True(File.Exists(viewModel.PackageDownloadPath), viewModel.PackageDownloadPath);
        AssertEx.True(File.Exists(viewModel.PortalSyncReceipt.ReceiptOutputPath), viewModel.PortalSyncReceipt.ReceiptOutputPath);
        AssertEx.Equal(1, client.EvidenceEvents.Count);
        AssertEx.Equal(InstallerEvidenceEventType.PackageValidated, client.EvidenceEvents[0].EventType);
        AssertEx.Equal(1, client.EvidenceEvents[0].Sequence);
    }

    private static async Task EvidenceSyncFailureKeepsPackageEventInPersistedOutbox()
    {
        using var scope = TestScope.Create();
        var client = new FakeOnboardingApiClient
        {
            PackageJson = CustomerConfigService.ToJson(CreateConfig("Outbox Customer")),
            EvidenceFailure = new HttpRequestException("Simulated portal outage")
        };
        var viewModel = scope.CreateViewModel(client);

        await viewModel.SelectSetupModeCommand.ExecuteAsync();
        await viewModel.LoadSampleBootstrapCommand.ExecuteAsync();
        await viewModel.CheckPackageReadinessCommand.ExecuteAsync();
        await viewModel.DownloadGeneratedPackageCommand.ExecuteAsync();

        var state = scope.LoadActiveState();
        AssertEx.Equal("Outbox Customer", viewModel.CustomerName);
        AssertEx.StringContains(viewModel.PortalSyncStatus, "sync pending");
        AssertEx.True(state is not null, "Expected installer state with a pending evidence event.");
        AssertEx.Equal(1, state!.InstallerEvidenceOutbox.PendingEvents.Count);
        AssertEx.Equal(InstallerEvidenceEventType.PackageValidated, state.InstallerEvidenceOutbox.PendingEvents[0].Payload.EventType);
        AssertEx.Equal(1, state.InstallerEvidenceOutbox.PendingEvents[0].Payload.Sequence);
    }

    private static async Task RemovalWorkflowEnablesAzureInventoryButKeepsRemovalGated()
    {
        using var scope = TestScope.Create();
        var client = new FakeOnboardingApiClient();
        var viewModel = scope.CreateViewModel(client);

        AssertEx.True(viewModel.IsRemovalWorkflowAvailable, "Azure-only removal should be available in this build.");
        await viewModel.SelectRemovalModeCommand.ExecuteAsync();
        await viewModel.LoadSampleBootstrapCommand.ExecuteAsync();
        await viewModel.LoadSamplePackageCommand.ExecuteAsync();
        await viewModel.GoToStepCommand.ExecuteAsync(4);

        AssertEx.True(viewModel.IsRemovalMode, "Removal workflow was not selected.");
        AssertEx.True(viewModel.IsSignInStep, "Removal should remain on Sign In until Azure authentication completes.");
        AssertEx.False(viewModel.RunRemovalInventoryCommand.CanExecute(null), "Removal inventory must remain unavailable until Azure sign-in completes.");
        AssertEx.False(viewModel.RunRemovalCommand.CanExecute(null), "Destructive removal must remain gated before inventory and approval.");
        AssertEx.Equal("Not run", viewModel.RemovalInventoryStatus);
        AssertEx.Equal(0, client.EvidenceEvents.Count);
    }

    private static async Task DownloadGeneratedPackageCommandRejectsProvenanceMismatchWithoutLoadingPackage()
    {
        using var scope = TestScope.Create();
        var config = CreateConfig("Wrong Tenant Package");
        config.Customer.TenantId = "tenant-wrong";
        config.Azure.TenantId = "tenant-wrong";
        config.ControlPlane.OnboardingSessionId = "onb_wrong_001";
        var client = new FakeOnboardingApiClient
        {
            PackageJson = CustomerConfigService.ToJson(config)
        };
        var viewModel = scope.CreateViewModel(client);

        await viewModel.SelectSetupModeCommand.ExecuteAsync();
        await viewModel.LoadSampleBootstrapCommand.ExecuteAsync();
        await viewModel.CheckPackageReadinessCommand.ExecuteAsync();
        await viewModel.DownloadGeneratedPackageCommand.ExecuteAsync();

        AssertEx.Equal(1, client.DownloadCalls);
        AssertEx.Equal("PackageInvalid", viewModel.PackageReadinessStatus);
        AssertEx.NotEqual("Wrong Tenant Package", viewModel.CustomerName);
        AssertEx.False(viewModel.ConnectAzureCommand.CanExecute(null), "Azure sign-in must not unlock after a provenance-mismatched generated package.");
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
            Status = readinessStatus.Equals("Ready", StringComparison.OrdinalIgnoreCase) ||
                readinessStatus.Equals("Downloaded", StringComparison.OrdinalIgnoreCase)
                    ? readinessStatus
                    : "Pending",
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

    private static OnboardingBootstrapSession CreateBootstrap(bool allowPortalSync, bool allowPackageGeneration = true)
    {
        var session = OnboardingSessionService.CreateFallbackSession();
        session.ExpiresAt = DateTimeOffset.UtcNow.AddDays(7);
        session.DiscoveryPolicy.AllowPortalSync = allowPortalSync;
        session.AllowedOperations.Clear();
        session.AllowedOperations.Add("TenantDiscovery");

        if (allowPortalSync)
        {
            session.AllowedOperations.Add("InstallStatusSync");
            if (allowPackageGeneration)
            {
                session.AllowedOperations.Add("InstallPackageGeneration");
            }
        }

        return session;
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
        public Queue<OnboardingPortalStatus> StatusSequence { get; } = new();

        public string PackageJson { get; set; } = CustomerConfigService.ToJson(CreateConfig("Downloaded Customer"));

        public int ConnectCalls { get; private set; }
        public int DownloadCalls { get; private set; }
        public int SubmitDiscoveryCalls { get; private set; }
        public int StatusCalls { get; private set; }
        public int SaveStatusCalls { get; private set; }
        public List<InstallerEvidenceEvent> EvidenceEvents { get; } = [];
        public Exception? EvidenceFailure { get; set; }
        public Exception? ConnectFailure { get; set; }
        public TaskCompletionSource? ConnectStarted { get; set; }
        public TaskCompletionSource? ConnectRelease { get; set; }

        public async Task<OnboardingSessionConnection> ConnectAsync(
            OnboardingBootstrapSession session,
            CancellationToken cancellationToken = default)
        {
            ConnectCalls++;
            ConnectStarted?.TrySetResult();
            if (ConnectRelease is not null)
            {
                await ConnectRelease.Task.WaitAsync(cancellationToken);
            }

            if (ConnectFailure is not null)
            {
                throw ConnectFailure;
            }

            return new OnboardingSessionConnection
            {
                Status = "Connected",
                SessionId = session.SessionId,
                CorrelationId = "corr-app-test-connect",
                Message = "Connected"
            };
        }

        public Task<OnboardingDiscoverySubmission> SubmitDiscoveryAsync(
            OnboardingBootstrapSession session,
            TenantDiscoveryResult discovery,
            CancellationToken cancellationToken = default)
        {
            SubmitDiscoveryCalls++;
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
            StatusCalls++;
            var status = StatusSequence.Count > 0 ? StatusSequence.Dequeue() : Status;
            status.SessionId = session.SessionId;
            return Task.FromResult(status);
        }

        public Task<string> SaveStatusAsync(
            OnboardingPortalStatus status,
            string outputRoot,
            CancellationToken cancellationToken = default)
        {
            SaveStatusCalls++;
            Directory.CreateDirectory(outputRoot);
            var path = Path.Combine(outputRoot, "fake-portal-status.json");
            File.WriteAllText(path, JsonSerializer.Serialize(status, JsonOptions));
            return Task.FromResult(path);
        }

        public Task<InstallerEvidenceReceipt> SubmitEvidenceAsync(
            OnboardingBootstrapSession session,
            InstallerEvidenceEvent evidence,
            string idempotencyKey,
            CancellationToken cancellationToken = default)
        {
            EvidenceEvents.Add(evidence);
            if (EvidenceFailure is not null)
            {
                return Task.FromException<InstallerEvidenceReceipt>(EvidenceFailure);
            }

            return Task.FromResult(new InstallerEvidenceReceipt
            {
                ContractVersion = "0.2",
                Status = "Accepted",
                SessionId = session.SessionId,
                EventId = evidence.EventId,
                EventType = evidence.EventType,
                InstallAttemptId = evidence.InstallAttemptId,
                Sequence = evidence.Sequence,
                LifecycleStatus = evidence.LifecycleStatus,
                Outcome = evidence.Outcome,
                InstallStatus = evidence.LifecycleStatus,
                CorrelationId = "corr-app-test-evidence",
                ReceivedAt = DateTimeOffset.UtcNow
            });
        }

        public Task<OnboardingPackageDownloadResult> DownloadPackageAsync(
            OnboardingBootstrapSession session,
            OnboardingPackageReadiness readiness,
            string workspaceRoot,
            TenantDiscoveryResult? discovery = null,
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

    private sealed class PromptThenCancelGraphAuthenticator : IGraphDeviceCodeAuthenticator
    {
        public async Task<GraphSignInResult> SignInAsync(
            string tenantId,
            string clientId,
            IProgress<GraphDeviceCodePrompt>? promptProgress = null,
            CancellationToken cancellationToken = default)
        {
            promptProgress?.Report(new GraphDeviceCodePrompt
            {
                Message = "Enter the code TEST-CODE to sign in.",
                UserCode = "TEST-CODE",
                VerificationUrl = "https://microsoft.com/devicelogin",
                ExpiresOn = DateTimeOffset.UtcNow.AddMinutes(10)
            });
            await Task.Delay(50, cancellationToken);
            throw new OperationCanceledException("Canceled by operator.");
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

        public InstallerWizardViewModel CreateViewModel(
            FakeOnboardingApiClient? client = null,
            InstallerEngine? engine = null)
        {
            var stateStore = new InstallerStateStore(Path.Combine(RootDirectory, "state"));
            return new InstallerWizardViewModel(
                client ?? new FakeOnboardingApiClient(),
                stateStore,
                RootDirectory,
                engine: engine);
        }

        public PersistedInstallerState? LoadActiveState()
        {
            return new InstallerStateStore(Path.Combine(RootDirectory, "state")).LoadMostRecentActive();
        }

        public string WriteBootstrap(OnboardingBootstrapSession session)
        {
            var samplesDirectory = Path.Combine(RootDirectory, "samples");
            Directory.CreateDirectory(samplesDirectory);
            var path = Path.Combine(samplesDirectory, "contoso.onboarding.bootstrap.json");
            File.WriteAllText(path, OnboardingSessionService.ToJson(session));
            return path;
        }

        public string WritePackage(CustomerInstallConfig config)
        {
            var samplesDirectory = Path.Combine(RootDirectory, "samples");
            Directory.CreateDirectory(samplesDirectory);
            var path = Path.Combine(samplesDirectory, "contoso.customer.install.json");
            File.WriteAllText(path, CustomerConfigService.ToJson(config));
            return path;
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

        public static void False(bool condition, string message)
        {
            if (condition)
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
