using System.Collections.ObjectModel;
using System.IO;
using Microsoft.Win32;
using PageMaker365.Installer.Engine.Models;
using PageMaker365.Installer.Engine.Services;

namespace PageMaker365.Installer.App.ViewModels;

public sealed class InstallerWizardViewModel : ViewModelBase
{
    private readonly CustomerConfigService _configService = new();
    private readonly OnboardingSessionService _onboardingSessionService = new();
    private readonly RedactionService _redactionService = new();
    private readonly InstallerEngine _engine;
    private readonly SupportBundleService _supportBundleService;
    private readonly TenantDiscoveryService _tenantDiscoveryService;
    private readonly MockOnboardingApiClient _onboardingApiClient = new();
    private CustomerInstallConfig? _config;
    private InstallerSession? _session;
    private OnboardingBootstrapSession? _bootstrapSession;
    private TenantDiscoveryResult? _tenantDiscovery;

    private string _packagePath = "No customer package loaded.";
    private string _customerName = "Load a customer package";
    private string _azureSubscription = "Not loaded";
    private string _sharePointSite = "Not loaded";
    private string _onboardingSessionId = "Not connected";
    private string _onboardingStatus = "No onboarding session connected.";
    private string _onboardingApiBaseUrl = "Not connected";
    private string _discoverySummary = "No discovery snapshot created.";
    private string _discoveryOutputPath = "Not saved";
    private string _portalSyncStatus = "Not synced";
    private string _aiTitle = "Ready to help";
    private string _aiSummary = "Run preflight checks to identify permission, Azure, or SharePoint issues before deployment.";
    private string _sessionId = "No active session";
    private string _sessionStatus = "Not started";
    private string _footerStatus = "Review the installer requirements, then continue to the package step.";
    private string _nextButtonText = "Next";
    private string _workflowTitle = "Deployment Flow";
    private string _workflowSubtitle = "Track each required step from package intake through validation.";
    private string _workflowMode = "Setup";
    private int _currentStepNumber = 1;
    private int _maxAccessibleStepNumber = 2;
    private bool _canContinue;
    private bool _canGoBack;
    private bool _canGoNext;

    public InstallerWizardViewModel()
    {
        _engine = new InstallerEngine(new StructuredLogger(_redactionService));
        _supportBundleService = new SupportBundleService(_redactionService);
        _tenantDiscoveryService = new TenantDiscoveryService(_redactionService);

        Steps = [];

        SelectSetupModeCommand = new RelayCommand(SelectSetupModeAsync);
        SelectRemovalModeCommand = new RelayCommand(SelectRemovalModeAsync);
        LoadSampleBootstrapCommand = new RelayCommand(LoadSampleBootstrapAsync);
        BrowseBootstrapCommand = new RelayCommand(BrowseBootstrapAsync);
        ConnectOnboardingCommand = new RelayCommand(ConnectOnboardingAsync, () => _bootstrapSession is not null);
        RunDiscoveryCommand = new RelayCommand(RunDiscoveryAsync, () => _bootstrapSession is not null);
        SyncDiscoveryCommand = new RelayCommand(SyncDiscoveryAsync, () => _bootstrapSession is not null && _tenantDiscovery is not null);
        SaveDiscoveryCommand = new RelayCommand(SaveDiscoveryAsync, () => _tenantDiscovery is not null);
        LoadSamplePackageCommand = new RelayCommand(LoadSamplePackageAsync);
        BrowsePackageCommand = new RelayCommand(BrowsePackageAsync);
        ConnectAzureCommand = new RelayCommand(ConnectAzureAsync, () => _config is not null);
        ConnectGraphCommand = new RelayCommand(ConnectGraphAsync, () => _config is not null);
        RunPreflightCommand = new RelayCommand(RunPreflightAsync, () => _config is not null);
        ExplainIssueCommand = new RelayCommand(ExplainIssueAsync, () => _session is not null);
        GenerateAdminMessageCommand = new RelayCommand(GenerateAdminMessageAsync, () => _session is not null);
        CreateSupportBundleCommand = new RelayCommand(CreateSupportBundleAsync, () => _session is not null);
        BackCommand = new RelayCommand(GoBackAsync, () => CanGoBack);
        NextCommand = new RelayCommand(GoNextAsync, () => CanGoNext);
        GoToStepCommand = new RelayCommand(GoToStepAsync, CanGoToStep);
        GoHomeCommand = new RelayCommand(GoHomeAsync);
        ConfigureSetupWorkflow();
    }

    public string InstallerVersion => "Alpha scaffold 0.1";
    public ObservableCollection<StepViewModel> Steps { get; }
    public ObservableCollection<CheckResultViewModel> CheckResults { get; } = [];
    public RelayCommand SelectSetupModeCommand { get; }
    public RelayCommand SelectRemovalModeCommand { get; }
    public RelayCommand LoadSampleBootstrapCommand { get; }
    public RelayCommand BrowseBootstrapCommand { get; }
    public RelayCommand ConnectOnboardingCommand { get; }
    public RelayCommand RunDiscoveryCommand { get; }
    public RelayCommand SyncDiscoveryCommand { get; }
    public RelayCommand SaveDiscoveryCommand { get; }
    public RelayCommand LoadSamplePackageCommand { get; }
    public RelayCommand BrowsePackageCommand { get; }
    public RelayCommand ConnectAzureCommand { get; }
    public RelayCommand ConnectGraphCommand { get; }
    public RelayCommand RunPreflightCommand { get; }
    public RelayCommand ExplainIssueCommand { get; }
    public RelayCommand GenerateAdminMessageCommand { get; }
    public RelayCommand CreateSupportBundleCommand { get; }
    public RelayCommand BackCommand { get; }
    public RelayCommand NextCommand { get; }
    public RelayCommand GoToStepCommand { get; }
    public RelayCommand GoHomeCommand { get; }

    public string PackagePath
    {
        get => _packagePath;
        set => SetProperty(ref _packagePath, value);
    }

    public string CustomerName
    {
        get => _customerName;
        set => SetProperty(ref _customerName, value);
    }

    public string AzureSubscription
    {
        get => _azureSubscription;
        set => SetProperty(ref _azureSubscription, value);
    }

    public string SharePointSite
    {
        get => _sharePointSite;
        set => SetProperty(ref _sharePointSite, value);
    }

    public string OnboardingSessionId
    {
        get => _onboardingSessionId;
        set => SetProperty(ref _onboardingSessionId, value);
    }

    public string OnboardingStatus
    {
        get => _onboardingStatus;
        set => SetProperty(ref _onboardingStatus, value);
    }

    public string OnboardingApiBaseUrl
    {
        get => _onboardingApiBaseUrl;
        set => SetProperty(ref _onboardingApiBaseUrl, value);
    }

    public string DiscoverySummary
    {
        get => _discoverySummary;
        set => SetProperty(ref _discoverySummary, value);
    }

    public string DiscoveryOutputPath
    {
        get => _discoveryOutputPath;
        set => SetProperty(ref _discoveryOutputPath, value);
    }

    public string PortalSyncStatus
    {
        get => _portalSyncStatus;
        set => SetProperty(ref _portalSyncStatus, value);
    }

    public string AiTitle
    {
        get => _aiTitle;
        set => SetProperty(ref _aiTitle, value);
    }

    public string AiSummary
    {
        get => _aiSummary;
        set => SetProperty(ref _aiSummary, value);
    }

    public string SessionId
    {
        get => _sessionId;
        set => SetProperty(ref _sessionId, value);
    }

    public string SessionStatus
    {
        get => _sessionStatus;
        set => SetProperty(ref _sessionStatus, value);
    }

    public string FooterStatus
    {
        get => _footerStatus;
        set => SetProperty(ref _footerStatus, value);
    }

    public bool CanContinue
    {
        get => _canContinue;
        set => SetProperty(ref _canContinue, value);
    }

    public string NextButtonText
    {
        get => _nextButtonText;
        set => SetProperty(ref _nextButtonText, value);
    }

    public string WorkflowTitle
    {
        get => _workflowTitle;
        set => SetProperty(ref _workflowTitle, value);
    }

    public string WorkflowSubtitle
    {
        get => _workflowSubtitle;
        set => SetProperty(ref _workflowSubtitle, value);
    }

    public bool CanGoBack
    {
        get => _canGoBack;
        set => SetProperty(ref _canGoBack, value);
    }

    public bool CanGoNext
    {
        get => _canGoNext;
        set => SetProperty(ref _canGoNext, value);
    }

    public bool IsWelcomeStep => _currentStepNumber == 1;
    public bool IsPackageStep => _currentStepNumber == 2;
    public bool IsSignInStep => _currentStepNumber == 3;
    public bool IsPreflightStep => _currentStepNumber == 4;
    public bool IsPreviewStep => _currentStepNumber == 5;
    public bool IsInstallStep => _currentStepNumber == 6;
    public bool IsValidateStep => _currentStepNumber == 7;
    public bool IsFinishStep => _currentStepNumber == 8;
    public bool IsSetupMode => _workflowMode == "Setup";
    public bool IsRemovalMode => _workflowMode == "Removal";

    private Task SelectSetupModeAsync()
    {
        ConfigureSetupWorkflow();
        FooterStatus = "Setup workflow selected. Continue to load the customer install package.";
        return Task.CompletedTask;
    }

    private Task SelectRemovalModeAsync()
    {
        ConfigureRemovalWorkflow();
        FooterStatus = "Removal workflow selected. Continue to discover or load the existing PageMaker365 deployment.";
        return Task.CompletedTask;
    }

    private async Task LoadSampleBootstrapAsync()
    {
        var path = FindSampleBootstrapPath();
        if (path is null)
        {
            LoadBootstrapSession(OnboardingSessionService.CreateFallbackSession(), "Built-in demo onboarding session");
            return;
        }

        await LoadBootstrapAsync(path);
    }

    private async Task BrowseBootstrapAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select PageMaker365 onboarding bootstrap session",
            Filter = "PageMaker365 bootstrap (*.pm365bootstrap;*.json)|*.pm365bootstrap;*.json|JSON files (*.json)|*.json|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            await LoadBootstrapAsync(dialog.FileName);
        }
    }

    private async Task LoadBootstrapAsync(string path)
    {
        var session = await _onboardingSessionService.LoadBootstrapAsync(path);
        var validation = _onboardingSessionService.Validate(session);
        if (!validation.IsValid)
        {
            FooterStatus = string.Join(" ", validation.Errors);
            return;
        }

        LoadBootstrapSession(session, path);
        if (validation.Warnings.Count > 0)
        {
            FooterStatus = string.Join(" ", validation.Warnings);
        }
    }

    private void LoadBootstrapSession(OnboardingBootstrapSession session, string source)
    {
        _bootstrapSession = session;
        _tenantDiscovery = null;
        OnboardingSessionId = session.SessionId;
        OnboardingApiBaseUrl = session.ApiBaseUrl;
        OnboardingStatus = $"Bootstrap loaded from {source}.";
        PortalSyncStatus = "Not synced";
        DiscoverySummary = "No discovery snapshot created.";
        DiscoveryOutputPath = "Not saved";
        if (_config is null)
        {
            CustomerName = session.CustomerName;
        }

        FooterStatus = "Onboarding bootstrap loaded. Connect the session or run mock discovery.";
        ConnectOnboardingCommand.RaiseCanExecuteChanged();
        RunDiscoveryCommand.RaiseCanExecuteChanged();
        SyncDiscoveryCommand.RaiseCanExecuteChanged();
        SaveDiscoveryCommand.RaiseCanExecuteChanged();
    }

    private async Task ConnectOnboardingAsync()
    {
        if (_bootstrapSession is null)
        {
            FooterStatus = "Load an onboarding bootstrap session first.";
            return;
        }

        var connection = await _onboardingApiClient.ConnectAsync(_bootstrapSession);
        OnboardingStatus = $"{connection.Status}: {connection.Message}";
        PortalSyncStatus = $"Connected with correlation {connection.CorrelationId}.";
        FooterStatus = "Mock onboarding API session connected. No network request was made.";
    }

    private Task RunDiscoveryAsync()
    {
        if (_bootstrapSession is null)
        {
            FooterStatus = "Load an onboarding bootstrap session first.";
            return Task.CompletedTask;
        }

        _tenantDiscovery = _tenantDiscoveryService.CreateDiscovery(_bootstrapSession, _config);
        DiscoverySummary = $"{_tenantDiscovery.Customer.TenantName}; tenant {_tenantDiscovery.Customer.TenantId}; SharePoint {_tenantDiscovery.SharePoint.SiteUrl}; subscription {_tenantDiscovery.Azure.SelectedSubscriptionId}";
        DiscoveryOutputPath = "Not saved";
        PortalSyncStatus = "Discovery created locally. Not synced.";
        FooterStatus = "Mock tenant discovery created. Review, save a redacted copy, or sync to the mock portal client.";
        AiTitle = "Discovery snapshot ready";
        AiSummary = "The installer has created an install-readiness payload shaped for portal onboarding. This build uses mock discovery derived from the bootstrap session and loaded package.";
        SyncDiscoveryCommand.RaiseCanExecuteChanged();
        SaveDiscoveryCommand.RaiseCanExecuteChanged();
        return Task.CompletedTask;
    }

    private async Task SyncDiscoveryAsync()
    {
        if (_bootstrapSession is null || _tenantDiscovery is null)
        {
            FooterStatus = "Create a discovery snapshot before syncing.";
            return;
        }

        var submission = await _onboardingApiClient.SubmitDiscoveryAsync(_bootstrapSession, _tenantDiscovery);
        PortalSyncStatus = $"{submission.Status}: {submission.PortalRecordUrl}";
        FooterStatus = "Discovery synced to the mock PageMaker365 API client. No network request was made.";
    }

    private async Task SaveDiscoveryAsync()
    {
        if (_tenantDiscovery is null)
        {
            FooterStatus = "Create a discovery snapshot before saving.";
            return;
        }

        var outputRoot = Path.Combine(GetWorkspaceRoot(), "support-bundle");
        var path = await _tenantDiscoveryService.SaveRedactedAsync(_tenantDiscovery, outputRoot);
        DiscoveryOutputPath = path;
        FooterStatus = $"Redacted tenant discovery saved: {path}";
    }

    private async Task LoadSamplePackageAsync()
    {
        var path = FindSamplePackagePath();
        if (path is null)
        {
            LoadConfig(CreateFallbackConfig(), "Built-in demo package");
            return;
        }

        await LoadPackageAsync(path);
    }

    private async Task BrowsePackageAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select PageMaker365 customer install package",
            Filter = "PageMaker365 package (*.pm365install;*.json)|*.pm365install;*.json|JSON files (*.json)|*.json|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            await LoadPackageAsync(dialog.FileName);
        }
    }

    private async Task LoadPackageAsync(string path)
    {
        var config = await _configService.LoadAsync(path);
        var validation = _configService.Validate(config);
        if (!validation.IsValid)
        {
            FooterStatus = string.Join(" ", validation.Errors);
            return;
        }

        LoadConfig(config, path);
        if (validation.Warnings.Count > 0)
        {
            FooterStatus = string.Join(" ", validation.Warnings);
        }
    }

    private void LoadConfig(CustomerInstallConfig config, string path)
    {
        _config = config;
        PackagePath = path;
        CustomerName = config.Customer.TenantName;
        AzureSubscription = $"{config.Azure.SubscriptionId} / {config.Azure.ResourceGroupName}";
        SharePointSite = config.SharePoint.SiteUrl;
        FooterStatus = IsRemovalMode
            ? "Customer package loaded. Sign in so the app can inventory the existing deployment."
            : "Customer package loaded. Run preflight checks next.";
        SetStepStatus(2, "Complete", "#42D8A0");
        SetStepStatus(3, "Current", "#19D8E9");
        SetStepStatus(4, "Ready", "#19D8E9");
        UnlockThroughStep(4);
        SetCurrentStep(3);
        ConnectAzureCommand.RaiseCanExecuteChanged();
        ConnectGraphCommand.RaiseCanExecuteChanged();
        RunPreflightCommand.RaiseCanExecuteChanged();
    }

    private async Task ConnectAzureAsync()
    {
        await RunPowerShellActionAsync(
            "Starting Azure sign-in. Complete the browser prompt, then return to the installer.",
            "Azure sign-in running",
            "Azure sign-in completed.",
            (session, progress) => _engine.RunAzureSignInAsync(session, GetWorkspaceRoot(), PackagePath, progress));
    }

    private async Task ConnectGraphAsync()
    {
        await RunPowerShellActionAsync(
            "Starting Microsoft Graph sign-in. Approve the requested scopes, then return to the installer.",
            "Graph sign-in running",
            "Microsoft Graph sign-in completed.",
            (session, progress) => _engine.RunGraphSignInAsync(session, GetWorkspaceRoot(), PackagePath, progress));
    }

    private async Task RunPreflightAsync()
    {
        if (_config is null)
        {
            return;
        }

        CheckResults.Clear();
        CanContinue = false;
        FooterStatus = "Running PowerShell-backed preflight checks...";
        SetCurrentStep(4);
        SetStepStatus(4, "Running", "#19D8E9");

        _session = _engine.CreateSession(_config, GetWorkspaceRoot());
        SessionId = _session.SessionId;
        SessionStatus = "Preflight running";

        var progress = new Progress<InstallerStepResult>(result =>
        {
            CheckResults.Add(new CheckResultViewModel(result));
        });

        await _engine.RunPowerShellPreflightAsync(_session, GetWorkspaceRoot(), PackagePath, progress);

        SessionStatus = _session.Status.ToString();
        CanContinue = _session.Status is InstallStatus.Passed or InstallStatus.Warning;
        SetStepStatus(4, _session.Status == InstallStatus.Failed ? "Blocked" : "Complete", _session.Status == InstallStatus.Failed ? "#FF5C7A" : "#42D8A0");
        if (CanContinue)
        {
            UnlockThroughStep(5);
        }
        FooterStatus = _session.Status == InstallStatus.Failed
            ? "Preflight found a blocking issue. Use the AI assistant or create a support bundle."
            : "Preflight completed. Continue to deployment preview.";

        await ExplainIssueAsync();
        ExplainIssueCommand.RaiseCanExecuteChanged();
        GenerateAdminMessageCommand.RaiseCanExecuteChanged();
        CreateSupportBundleCommand.RaiseCanExecuteChanged();
    }

    private async Task RunPowerShellActionAsync(
        string startingStatus,
        string runningStatus,
        string completedStatus,
        Func<InstallerSession, IProgress<InstallerStepResult>, Task<IReadOnlyList<InstallerStepResult>>> action)
    {
        if (_config is null)
        {
            return;
        }

        if (!File.Exists(PackagePath))
        {
            FooterStatus = "This action requires a customer package file. Load a package from disk first.";
            return;
        }

        FooterStatus = startingStatus;
        _session ??= _engine.CreateSession(_config, GetWorkspaceRoot());
        SessionId = _session.SessionId;
        SessionStatus = runningStatus;
        SetCurrentStep(3);
        SetStepStatus(3, "Running", "#19D8E9");

        var progress = new Progress<InstallerStepResult>(result =>
        {
            CheckResults.Add(new CheckResultViewModel(result));
        });

        await action(_session, progress);

        SessionStatus = _session.Status.ToString();
        SetStepStatus(3, _session.Status == InstallStatus.Failed ? "Blocked" : "Complete", _session.Status == InstallStatus.Failed ? "#FF5C7A" : "#42D8A0");
        if (_session.Status is not InstallStatus.Failed)
        {
            UnlockThroughStep(4);
            SetCurrentStep(4);
        }
        FooterStatus = _session.Status == InstallStatus.Failed
            ? "Sign-in did not complete. Review the result details and try again."
            : completedStatus;

        ExplainIssueCommand.RaiseCanExecuteChanged();
        GenerateAdminMessageCommand.RaiseCanExecuteChanged();
        CreateSupportBundleCommand.RaiseCanExecuteChanged();
    }

    private Task GoBackAsync()
    {
        if (_currentStepNumber > 1)
        {
            SetCurrentStep(_currentStepNumber - 1);
        }

        return Task.CompletedTask;
    }

    private Task GoNextAsync()
    {
        if (_currentStepNumber < _maxAccessibleStepNumber)
        {
            SetCurrentStep(_currentStepNumber + 1);
        }

        return Task.CompletedTask;
    }

    private Task GoToStepAsync(object? parameter)
    {
        if (TryGetStepNumber(parameter, out var stepNumber) && stepNumber <= _maxAccessibleStepNumber)
        {
            SetCurrentStep(stepNumber);
        }

        return Task.CompletedTask;
    }

    private Task GoHomeAsync()
    {
        SetCurrentStep(1);
        FooterStatus = "Choose setup or removal, then continue through the guided workflow.";
        return Task.CompletedTask;
    }

    private bool CanGoToStep(object? parameter)
    {
        return TryGetStepNumber(parameter, out var stepNumber) && stepNumber <= _maxAccessibleStepNumber;
    }

    private static bool TryGetStepNumber(object? parameter, out int stepNumber)
    {
        return parameter switch
        {
            int value => (stepNumber = value) > 0,
            string value when int.TryParse(value, out var parsed) => (stepNumber = parsed) > 0,
            _ => (stepNumber = 0) > 0
        };
    }

    private void UnlockThroughStep(int stepNumber)
    {
        _maxAccessibleStepNumber = Math.Max(_maxAccessibleStepNumber, stepNumber);
        RefreshStepNavigation();
    }

    private void SetCurrentStep(int stepNumber)
    {
        _currentStepNumber = Math.Clamp(stepNumber, 1, _maxAccessibleStepNumber);
        RefreshStepNavigation();
    }

    private void RefreshStepNavigation()
    {
        foreach (var step in Steps)
        {
            step.IsAccessible = step.Number <= _maxAccessibleStepNumber;
            step.IsCurrent = step.Number == _currentStepNumber;
            RefreshStepStatusForNavigation(step);
        }

        CanGoBack = _currentStepNumber > 1;
        CanGoNext = _currentStepNumber < _maxAccessibleStepNumber;
        NextButtonText = _currentStepNumber switch
        {
            4 when IsSetupMode => "Continue to Preview",
            4 when IsRemovalMode => "Continue to Removal Preview",
            5 when IsSetupMode => "Continue to Install",
            5 when IsRemovalMode => "Continue to Remove",
            6 when IsSetupMode => "Continue to Validate",
            6 when IsRemovalMode => "Continue to Cleanup Check",
            7 => "Continue to Finish",
            _ => "Next"
        };
        if (_currentStepNumber > 0 && _currentStepNumber < Steps.Count && NextButtonText == "Next")
        {
            NextButtonText = $"Next: {Steps[_currentStepNumber].Name}";
        }

        BackCommand?.RaiseCanExecuteChanged();
        NextCommand?.RaiseCanExecuteChanged();
        GoToStepCommand?.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(IsWelcomeStep));
        OnPropertyChanged(nameof(IsPackageStep));
        OnPropertyChanged(nameof(IsSignInStep));
        OnPropertyChanged(nameof(IsPreflightStep));
        OnPropertyChanged(nameof(IsPreviewStep));
        OnPropertyChanged(nameof(IsInstallStep));
        OnPropertyChanged(nameof(IsValidateStep));
        OnPropertyChanged(nameof(IsFinishStep));
        OnPropertyChanged(nameof(IsSetupMode));
        OnPropertyChanged(nameof(IsRemovalMode));
    }

    private void ConfigureSetupWorkflow()
    {
        _workflowMode = "Setup";
        WorkflowTitle = "Deployment Flow";
        WorkflowSubtitle = "Track each required setup step from package intake through validation.";
        ResetWorkflow(
        [
            "Welcome",
            "Package",
            "Sign In",
            "Preflight",
            "Preview",
            "Install",
            "Validate",
            "Finish"
        ]);
        AiTitle = "Ready to help";
        AiSummary = "Run preflight checks to identify permission, Azure, or SharePoint issues before deployment.";
    }

    private void ConfigureRemovalWorkflow()
    {
        _workflowMode = "Removal";
        WorkflowTitle = "Removal Flow";
        WorkflowSubtitle = "Track each cleanup step from deployment discovery through final evidence.";
        ResetWorkflow(
        [
            "Welcome",
            "Discovery",
            "Sign In",
            "Inventory",
            "Removal Preview",
            "Remove",
            "Validate Cleanup",
            "Finish"
        ]);
        AiTitle = "Ready to guide removal";
        AiSummary = "Inventory the existing deployment, preview what will be removed, and keep a cleanup report for the customer.";
    }

    private void ResetWorkflow(string[] stepNames)
    {
        ResetSessionData();
        Steps.Clear();
        for (var index = 0; index < stepNames.Length; index++)
        {
            Steps.Add(new StepViewModel(index + 1, stepNames[index]));
        }

        _currentStepNumber = 1;
        _maxAccessibleStepNumber = 2;
        CheckResults.Clear();
        CanContinue = false;
        RefreshStepNavigation();
    }

    private void ResetSessionData()
    {
        _config = null;
        _session = null;
        _bootstrapSession = null;
        _tenantDiscovery = null;
        PackagePath = "No customer package loaded.";
        CustomerName = "Load a customer package";
        AzureSubscription = "Not loaded";
        SharePointSite = "Not loaded";
        OnboardingSessionId = "Not connected";
        OnboardingStatus = "No onboarding session connected.";
        OnboardingApiBaseUrl = "Not connected";
        DiscoverySummary = "No discovery snapshot created.";
        DiscoveryOutputPath = "Not saved";
        PortalSyncStatus = "Not synced";
        SessionId = "No active session";
        SessionStatus = "Not started";
        FooterStatus = "Review the workflow requirements, then continue to the next step.";
        ConnectOnboardingCommand.RaiseCanExecuteChanged();
        RunDiscoveryCommand.RaiseCanExecuteChanged();
        SyncDiscoveryCommand.RaiseCanExecuteChanged();
        SaveDiscoveryCommand.RaiseCanExecuteChanged();
        ConnectAzureCommand.RaiseCanExecuteChanged();
        ConnectGraphCommand.RaiseCanExecuteChanged();
        RunPreflightCommand.RaiseCanExecuteChanged();
        ExplainIssueCommand.RaiseCanExecuteChanged();
        GenerateAdminMessageCommand.RaiseCanExecuteChanged();
        CreateSupportBundleCommand.RaiseCanExecuteChanged();
    }

    private void RefreshStepStatusForNavigation(StepViewModel step)
    {
        if (step.StatusLabel is "Complete" or "Blocked" or "Running")
        {
            return;
        }

        if (step.Number == 1 && _currentStepNumber > 1)
        {
            step.StatusLabel = "Complete";
            step.StatusBrush = "#42D8A0";
            return;
        }

        if (step.Number > _maxAccessibleStepNumber)
        {
            step.StatusLabel = "Pending";
            step.StatusBrush = "#2A355E";
            return;
        }

        if (step.Number == _currentStepNumber)
        {
            step.StatusLabel = "Current";
            step.StatusBrush = "#19D8E9";
            return;
        }

        step.StatusLabel = "Ready";
        step.StatusBrush = "#2A355E";
    }

    private void SetStepStatus(int stepNumber, string statusLabel, string statusBrush)
    {
        var step = Steps.FirstOrDefault(item => item.Number == stepNumber);
        if (step is null)
        {
            return;
        }

        step.StatusLabel = statusLabel;
        step.StatusBrush = statusBrush;
    }

    private Task ExplainIssueAsync()
    {
        if (_session is null)
        {
            return Task.CompletedTask;
        }

        var payload = _engine.CreateDiagnosticPayload(_session);
        if (string.IsNullOrWhiteSpace(payload.FailedStep))
        {
            AiTitle = "No blocking issue detected";
            AiSummary = "The current preflight result does not need remediation.";
            return Task.CompletedTask;
        }

        AiTitle = payload.FailedStep;
        AiSummary = payload.ErrorCode switch
        {
            "AzureSignInFailed" =>
                "Azure sign-in did not complete. Retry sign-in and make sure the account belongs to the customer tenant or has access to the target subscription.",
            "GraphSignInFailed" =>
                "Microsoft Graph sign-in did not complete. Retry sign-in and approve the requested scopes for tenant, app consent, and SharePoint validation.",
            "AzureTenantMismatch" =>
                "The signed-in Azure tenant does not match the loaded customer package. Sign in with the correct customer tenant or load a package generated for the current tenant, then rerun preflight.",
            "AzureSubscriptionMismatch" =>
                "The selected Azure subscription does not match the loaded customer package. Switch to the target subscription with Set-AzContext or load the correct package, then rerun preflight.",
            "GraphNotSignedIn" or "GraphNotSignedInForSharePoint" =>
                "Microsoft Graph sign-in is required before the installer can validate Entra consent or SharePoint access. Sign in with the required Graph scopes, then rerun preflight.",
            "MissingApplicationAdministrator" or "EntraAdminRoleMissing" or "EntraAdminRoleCheckUnavailable" =>
                "The installer may need an Entra administrator to approve app permissions. Ask a Global Administrator, Cloud Application Administrator, or Application Administrator to complete consent when the final app registration check is wired.",
            "SharePointSiteResolveFailed" =>
                "The SharePoint site could not be resolved through Microsoft Graph. Confirm the site URL, Graph sign-in tenant, and Sites.Read.All consent, then rerun preflight.",
            _ =>
                "The installer is running module-based local, Azure, Entra, and SharePoint preflight checks. Review the failed check details, resolve the issue, then rerun preflight."
        };
        return Task.CompletedTask;
    }

    private Task GenerateAdminMessageAsync()
    {
        if (IsRemovalMode)
        {
            AiTitle = "Removal admin message draft";
            AiSummary = "We are preparing to remove PageMaker365 from the customer tenant. Please have an authorized Azure and Microsoft 365 administrator available to review the cleanup inventory, approve the removal scope, and confirm any app permission or SharePoint access changes.";
            return Task.CompletedTask;
        }

        AiTitle = "Setup admin message draft";
        AiSummary = "We are ready to continue the PageMaker365 install, but need an authorized Entra administrator to approve the application permissions. Please have a Global Administrator, Cloud Application Administrator, or Application Administrator available for the consent step.";
        return Task.CompletedTask;
    }

    private async Task CreateSupportBundleAsync()
    {
        if (_session is null)
        {
            return;
        }

        var outputRoot = Path.Combine(GetWorkspaceRoot(), "support-bundle");
        var path = await _supportBundleService.CreateAsync(_session, outputRoot);
        FooterStatus = $"Support bundle created: {path}";
    }

    private static string? FindSamplePackagePath()
    {
        foreach (var root in EnumerateCandidateRoots())
        {
            var candidate = Path.Combine(root, "samples", "contoso.customer.install.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string? FindSampleBootstrapPath()
    {
        foreach (var root in EnumerateCandidateRoots())
        {
            var candidate = Path.Combine(root, "samples", "contoso.onboarding.bootstrap.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateCandidateRoots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null && seen.Add(directory.FullName))
            {
                yield return directory.FullName;
                directory = directory.Parent;
            }
        }
    }

    private static string GetWorkspaceRoot()
    {
        return EnumerateCandidateRoots().FirstOrDefault(root => Directory.Exists(Path.Combine(root, "samples")))
            ?? Environment.CurrentDirectory;
    }

    private static CustomerInstallConfig CreateFallbackConfig()
    {
        return new CustomerInstallConfig
        {
            Customer =
            {
                TenantName = "Contoso Intranet",
                TenantId = "00000000-0000-0000-0000-000000000000",
                PrimaryContact = "admin@contoso.com"
            },
            Azure =
            {
                SubscriptionId = "11111111-1111-1111-1111-111111111111",
                Location = "eastus",
                ResourceGroupName = "rg-pagemaker365-contoso-prod",
                Environment = "prod"
            },
            SharePoint =
            {
                SiteUrl = "https://contoso.sharepoint.com/sites/intranet",
                DefaultDocumentLibrary = "Documents"
            },
            App =
            {
                AppName = "pagemaker365-contoso",
                SupportEmail = "support@pagemaker365.com"
            },
            Features =
            {
                KnowledgeBase = true,
                CustomerPortal = true,
                BillingIntegration = true
            }
        };
    }
}
