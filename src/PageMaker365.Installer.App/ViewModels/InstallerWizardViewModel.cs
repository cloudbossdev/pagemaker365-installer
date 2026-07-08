using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using PageMaker365.Installer.App;
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
    private readonly FinalEvidenceService _finalEvidenceService = new();
    private readonly DeploymentApprovalManifestService _deploymentApprovalManifestService = new();
    private readonly TenantDiscoveryService _tenantDiscoveryService;
    private readonly InstallerStateStore _stateStore;
    private readonly IOnboardingApiClient _onboardingApiClient;
    private readonly string _workspaceRoot;
    private CustomerInstallConfig? _config;
    private InstallerSession? _session;
    private OnboardingBootstrapSession? _bootstrapSession;
    private TenantDiscoveryResult? _tenantDiscovery;
    private OnboardingPortalStatus? _onboardingPortalStatus;
    private OnboardingPackageReadiness? _packageReadiness;
    private AssistantWorkspaceWindow? _assistantWindow;
    private string _stateId = InstallerStateStore.CreateStateId();
    private DateTimeOffset _stateCreatedAt = DateTimeOffset.UtcNow;
    private string _bootstrapSourcePath = "";
    private bool _stateCompleted;
    private bool _isRestoringState;

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
    private string _portalMissingFieldsSummary = "Package readiness has not been checked.";
    private string _packageReadinessStatus = "Not checked";
    private string _packageReadinessVersion = "Not available";
    private string _packageDownloadPath = "Not downloaded";
    private string _portalStatusOutputPath = "Not saved";
    private string _discoveryReviewSummary = "Create discovery to populate Azure, Microsoft Graph, SharePoint, and Entra readiness.";
    private string _discoveredLibrariesSummary = "No SharePoint document libraries discovered yet.";
    private string _discoverySyncReadinessSummary = "Create discovery before syncing onboarding data to the portal.";
    private string _discoverySyncStatusBrush = "#8290AA";
    private string _packageReadinessStatusBrush = "#8290AA";
    private string _packageReadinessSummary = "Package readiness has not been checked.";
    private string _packageTrustStatus = "Not checked";
    private string _packageTrustStatusBrush = "#8290AA";
    private string _packageTrustSummary = "Load a package to inspect export metadata and integrity.";
    private string _packageExportId = "Not available";
    private string _packageDeclaredHash = "Not available";
    private string _packageComputedHash = "Not checked";
    private string _previewStatus = "Not run";
    private string _previewStatusBrush = "#8290AA";
    private string _previewSummary = "Run deployment preview to see Azure what-if results before install.";
    private string _previewOutputPath = "Not saved";
    private string _previewArtifactPath = "Not created";
    private string _deploymentStatus = "Waiting for preview";
    private string _deploymentStatusBrush = "#8290AA";
    private string _deploymentSummary = "Complete deployment preview before running install.";
    private string _deploymentOutputPath = "Not saved";
    private string _deploymentArtifactPath = "Not created";
    private string _deploymentApprovalManifestId = "";
    private string _deploymentApprovalManifestPath = "Not created";
    private string _deploymentApprovalSummary = "No deployment approval manifest created.";
    private string _validationStatus = "Waiting for install";
    private string _validationStatusBrush = "#8290AA";
    private string _validationSummary = "Complete install before running validation.";
    private string _validationOutputPath = "Not saved";
    private string _finishStatus = "Waiting for validation";
    private string _finishStatusBrush = "#8290AA";
    private string _finishSummary = "Complete validation before generating final evidence.";
    private string _finalReportPath = "Not created";
    private string _finalManifestPath = "Not created";
    private string _finalBundlePath = "Not created";
    private string _finalEvidenceDirectory = "Not created";
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
    private InstallStatus _lastPreviewStatus = InstallStatus.NotStarted;
    private InstallStatus _lastDeploymentStatus = InstallStatus.NotStarted;
    private InstallStatus _lastValidationStatus = InstallStatus.NotStarted;
    private bool _deploymentApprovalConfirmed;
    private bool _canContinue;
    private bool _canGoBack;
    private bool _canGoNext;
    private bool _workflowSelected;
    private bool _hasRestorableSession;
    private PersistedInstallerState? _pendingResumeState;
    private string _resumeSessionSummary = "No saved installer session found.";
    private string _resumeSessionDetails = "Start a new setup workflow to begin.";
    private string _removalAvailabilityText = RemovalWorkflowEnabled()
        ? "Removal workflow is enabled for this environment. Review inventory and preview output before approving cleanup."
        : "Removal workflow is a design preview in this build and is disabled until backend cleanup commands are implemented.";
    private string _removalWorkflowButtonText = RemovalWorkflowEnabled() ? "Use Removal Workflow" : "Removal Unavailable";
    private string _deploymentConfirmationText = "";

    public InstallerWizardViewModel()
        : this(null, null, null)
    {
    }

    public InstallerWizardViewModel(
        IOnboardingApiClient? onboardingApiClient,
        InstallerStateStore? stateStore,
        string? workspaceRoot)
    {
        _workspaceRoot = string.IsNullOrWhiteSpace(workspaceRoot)
            ? ResolveWorkspaceRoot()
            : workspaceRoot;
        _stateStore = stateStore ?? new InstallerStateStore();
        _engine = new InstallerEngine(new StructuredLogger(_redactionService));
        _supportBundleService = new SupportBundleService(_redactionService);
        _tenantDiscoveryService = new TenantDiscoveryService(_redactionService);
        _onboardingApiClient = onboardingApiClient ?? new OnboardingApiClient(new OnboardingApiOptionsService().Load(GetWorkspaceRoot()));

        Steps = [];

        SelectSetupModeCommand = new RelayCommand(SelectSetupModeAsync);
        SelectRemovalModeCommand = new RelayCommand(SelectRemovalModeAsync);
        LoadSampleBootstrapCommand = new RelayCommand(LoadSampleBootstrapAsync);
        BrowseBootstrapCommand = new RelayCommand(BrowseBootstrapAsync);
        ConnectOnboardingCommand = new RelayCommand(ConnectOnboardingAsync, () => _bootstrapSession is not null);
        RunDiscoveryCommand = new RelayCommand(RunDiscoveryAsync, CanRunTenantDiscovery);
        SyncDiscoveryCommand = new RelayCommand(SyncDiscoveryAsync, CanSyncDiscovery);
        SaveDiscoveryCommand = new RelayCommand(SaveDiscoveryAsync, () => _tenantDiscovery is not null);
        CheckPackageReadinessCommand = new RelayCommand(CheckPackageReadinessAsync, () => _bootstrapSession is not null);
        DownloadGeneratedPackageCommand = new RelayCommand(DownloadGeneratedPackageAsync, CanDownloadGeneratedPackage);
        LoadSamplePackageCommand = new RelayCommand(LoadSamplePackageAsync);
        BrowsePackageCommand = new RelayCommand(BrowsePackageAsync);
        ConnectAzureCommand = new RelayCommand(ConnectAzureAsync, () => _config is not null);
        ConnectGraphCommand = new RelayCommand(ConnectGraphAsync, () => _config is not null);
        RunPreflightCommand = new RelayCommand(RunPreflightAsync, () => _config is not null);
        RunPreviewCommand = new RelayCommand(RunPreviewAsync, CanRunPreview);
        RunInstallCommand = new RelayCommand(RunInstallAsync, CanRunInstall);
        RunValidationCommand = new RelayCommand(RunValidationAsync, CanRunValidation);
        CreateFinalEvidenceCommand = new RelayCommand(CreateFinalEvidenceAsync, CanCreateFinalEvidence);
        ExplainIssueCommand = new RelayCommand(ExplainIssueAsync, () => _session is not null);
        GenerateAdminMessageCommand = new RelayCommand(GenerateAdminMessageAsync, () => _session is not null);
        CreateSupportBundleCommand = new RelayCommand(CreateSupportBundleAsync, () => _session is not null);
        OpenAssistantCommand = new RelayCommand(OpenAssistantAsync);
        BackCommand = new RelayCommand(GoBackAsync, () => CanGoBack);
        NextCommand = new RelayCommand(GoNextAsync, () => CanGoNext);
        GoToStepCommand = new RelayCommand(GoToStepAsync, CanGoToStep);
        GoHomeCommand = new RelayCommand(GoHomeAsync);
        ResumeSessionCommand = new RelayCommand(ResumeSessionAsync, () => HasRestorableSession);
        StartNewSessionCommand = new RelayCommand(StartNewSessionAsync);
        ForgetResumeSessionCommand = new RelayCommand(ForgetResumeSessionAsync, () => HasRestorableSession);
        ConfigureSetupWorkflow();
        _workflowSelected = false;
        DetectRestorableState();
        RefreshStepNavigation();
    }

    public string InstallerVersion => "Alpha scaffold 0.1";
    public ObservableCollection<StepViewModel> Steps { get; }
    public ObservableCollection<CheckResultViewModel> CheckResults { get; } = [];
    public ObservableCollection<PreviewResultViewModel> PreviewResults { get; } = [];
    public ObservableCollection<DeploymentResultViewModel> DeploymentResults { get; } = [];
    public ObservableCollection<ValidationResultViewModel> ValidationResults { get; } = [];
    public ObservableCollection<DiscoveryReadinessCardViewModel> DiscoveryReadinessCards { get; } = [];
    public ObservableCollection<DiscoveryValueViewModel> DiscoveryValues { get; } = [];
    public ObservableCollection<DiscoveryFindingViewModel> DiscoveryFindings { get; } = [];
    public ObservableCollection<SharePointLibraryViewModel> DiscoveredLibraries { get; } = [];
    public ObservableCollection<MissingFieldViewModel> PortalMissingFields { get; } = [];
    public PortalSyncReceiptViewModel PortalSyncReceipt { get; } = new();
    public RelayCommand SelectSetupModeCommand { get; }
    public RelayCommand SelectRemovalModeCommand { get; }
    public RelayCommand LoadSampleBootstrapCommand { get; }
    public RelayCommand BrowseBootstrapCommand { get; }
    public RelayCommand ConnectOnboardingCommand { get; }
    public RelayCommand RunDiscoveryCommand { get; }
    public RelayCommand SyncDiscoveryCommand { get; }
    public RelayCommand SaveDiscoveryCommand { get; }
    public RelayCommand CheckPackageReadinessCommand { get; }
    public RelayCommand DownloadGeneratedPackageCommand { get; }
    public RelayCommand LoadSamplePackageCommand { get; }
    public RelayCommand BrowsePackageCommand { get; }
    public RelayCommand ConnectAzureCommand { get; }
    public RelayCommand ConnectGraphCommand { get; }
    public RelayCommand RunPreflightCommand { get; }
    public RelayCommand RunPreviewCommand { get; }
    public RelayCommand RunInstallCommand { get; }
    public RelayCommand RunValidationCommand { get; }
    public RelayCommand CreateFinalEvidenceCommand { get; }
    public RelayCommand ExplainIssueCommand { get; }
    public RelayCommand GenerateAdminMessageCommand { get; }
    public RelayCommand CreateSupportBundleCommand { get; }
    public RelayCommand OpenAssistantCommand { get; }
    public RelayCommand BackCommand { get; }
    public RelayCommand NextCommand { get; }
    public RelayCommand GoToStepCommand { get; }
    public RelayCommand GoHomeCommand { get; }
    public RelayCommand ResumeSessionCommand { get; }
    public RelayCommand StartNewSessionCommand { get; }
    public RelayCommand ForgetResumeSessionCommand { get; }

    public void SaveCurrentState()
    {
        SaveWizardState();
    }

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

    public string PortalMissingFieldsSummary
    {
        get => _portalMissingFieldsSummary;
        set => SetProperty(ref _portalMissingFieldsSummary, value);
    }

    public string PackageReadinessStatus
    {
        get => _packageReadinessStatus;
        set => SetProperty(ref _packageReadinessStatus, value);
    }

    public string PackageReadinessVersion
    {
        get => _packageReadinessVersion;
        set => SetProperty(ref _packageReadinessVersion, value);
    }

    public string PackageDownloadPath
    {
        get => _packageDownloadPath;
        set => SetProperty(ref _packageDownloadPath, value);
    }

    public string PortalStatusOutputPath
    {
        get => _portalStatusOutputPath;
        set => SetProperty(ref _portalStatusOutputPath, value);
    }

    public string DiscoveryReviewSummary
    {
        get => _discoveryReviewSummary;
        set => SetProperty(ref _discoveryReviewSummary, value);
    }

    public string DiscoveredLibrariesSummary
    {
        get => _discoveredLibrariesSummary;
        set => SetProperty(ref _discoveredLibrariesSummary, value);
    }

    public string DiscoverySyncReadinessSummary
    {
        get => _discoverySyncReadinessSummary;
        set => SetProperty(ref _discoverySyncReadinessSummary, value);
    }

    public string DiscoverySyncStatusBrush
    {
        get => _discoverySyncStatusBrush;
        set => SetProperty(ref _discoverySyncStatusBrush, value);
    }

    public string PackageReadinessStatusBrush
    {
        get => _packageReadinessStatusBrush;
        set => SetProperty(ref _packageReadinessStatusBrush, value);
    }

    public string PackageReadinessSummary
    {
        get => _packageReadinessSummary;
        set => SetProperty(ref _packageReadinessSummary, value);
    }

    public string PackageTrustStatus
    {
        get => _packageTrustStatus;
        set => SetProperty(ref _packageTrustStatus, value);
    }

    public string PackageTrustStatusBrush
    {
        get => _packageTrustStatusBrush;
        set => SetProperty(ref _packageTrustStatusBrush, value);
    }

    public string PackageTrustSummary
    {
        get => _packageTrustSummary;
        set => SetProperty(ref _packageTrustSummary, value);
    }

    public string PackageExportId
    {
        get => _packageExportId;
        set => SetProperty(ref _packageExportId, string.IsNullOrWhiteSpace(value) ? "Not available" : value);
    }

    public string PackageDeclaredHash
    {
        get => _packageDeclaredHash;
        set => SetProperty(ref _packageDeclaredHash, string.IsNullOrWhiteSpace(value) ? "Not available" : value);
    }

    public string PackageComputedHash
    {
        get => _packageComputedHash;
        set => SetProperty(ref _packageComputedHash, string.IsNullOrWhiteSpace(value) ? "Not checked" : value);
    }

    public string PreviewStatus
    {
        get => _previewStatus;
        set => SetProperty(ref _previewStatus, value);
    }

    public string PreviewStatusBrush
    {
        get => _previewStatusBrush;
        set => SetProperty(ref _previewStatusBrush, value);
    }

    public string PreviewSummary
    {
        get => _previewSummary;
        set => SetProperty(ref _previewSummary, value);
    }

    public string PreviewOutputPath
    {
        get => _previewOutputPath;
        set => SetProperty(ref _previewOutputPath, value);
    }

    public string PreviewArtifactPath
    {
        get => _previewArtifactPath;
        set => SetProperty(ref _previewArtifactPath, string.IsNullOrWhiteSpace(value) ? "Not created" : value);
    }

    public string DeploymentStatus
    {
        get => _deploymentStatus;
        set => SetProperty(ref _deploymentStatus, value);
    }

    public string DeploymentStatusBrush
    {
        get => _deploymentStatusBrush;
        set => SetProperty(ref _deploymentStatusBrush, value);
    }

    public string DeploymentSummary
    {
        get => _deploymentSummary;
        set => SetProperty(ref _deploymentSummary, value);
    }

    public string DeploymentOutputPath
    {
        get => _deploymentOutputPath;
        set => SetProperty(ref _deploymentOutputPath, value);
    }

    public string DeploymentArtifactPath
    {
        get => _deploymentArtifactPath;
        set => SetProperty(ref _deploymentArtifactPath, string.IsNullOrWhiteSpace(value) ? "Not created" : value);
    }

    public string DeploymentApprovalManifestId
    {
        get => _deploymentApprovalManifestId;
        set => SetProperty(ref _deploymentApprovalManifestId, value);
    }

    public string DeploymentApprovalManifestPath
    {
        get => _deploymentApprovalManifestPath;
        set => SetProperty(ref _deploymentApprovalManifestPath, string.IsNullOrWhiteSpace(value) ? "Not created" : value);
    }

    public string DeploymentApprovalSummary
    {
        get => _deploymentApprovalSummary;
        set => SetProperty(ref _deploymentApprovalSummary, string.IsNullOrWhiteSpace(value) ? "No deployment approval manifest created." : value);
    }

    public bool DeploymentApprovalConfirmed
    {
        get => _deploymentApprovalConfirmed;
        set
        {
            if (SetProperty(ref _deploymentApprovalConfirmed, value))
            {
                RunInstallCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string DeploymentConfirmationText
    {
        get => _deploymentConfirmationText;
        set
        {
            if (SetProperty(ref _deploymentConfirmationText, value))
            {
                RunInstallCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string DeploymentConfirmationTarget => _config?.Azure.ResourceGroupName ?? "target resource group";

    public string DeploymentConfirmationPrompt => $"Type {DeploymentConfirmationTarget} to enable install.";

    public string DeploymentTargetSummary => _config is null
        ? "No deployment target loaded."
        : $"{_config.Customer.TenantName} | {_config.Azure.SubscriptionId} | {_config.Azure.ResourceGroupName}";

    public string DeploymentTargetDetails => _config is null
        ? "Load the customer package and complete deployment preview before installing."
        : $"SharePoint site: {_config.SharePoint.SiteUrl}";

    public string ValidationStatus
    {
        get => _validationStatus;
        set => SetProperty(ref _validationStatus, value);
    }

    public string ValidationStatusBrush
    {
        get => _validationStatusBrush;
        set => SetProperty(ref _validationStatusBrush, value);
    }

    public string ValidationSummary
    {
        get => _validationSummary;
        set => SetProperty(ref _validationSummary, value);
    }

    public string ValidationOutputPath
    {
        get => _validationOutputPath;
        set => SetProperty(ref _validationOutputPath, value);
    }

    public string ValidationTargetSummary => _config is null
        ? "No validation target loaded."
        : $"{_config.Customer.TenantName} | {_config.SharePoint.SiteUrl}";

    public string ValidationTargetDetails => _config is null
        ? "Complete install before running validation."
        : $"Install evidence: {DeploymentOutputPath}";

    public string FinishStatus
    {
        get => _finishStatus;
        set => SetProperty(ref _finishStatus, value);
    }

    public string FinishStatusBrush
    {
        get => _finishStatusBrush;
        set => SetProperty(ref _finishStatusBrush, value);
    }

    public string FinishSummary
    {
        get => _finishSummary;
        set => SetProperty(ref _finishSummary, value);
    }

    public string FinalReportPath
    {
        get => _finalReportPath;
        set => SetProperty(ref _finalReportPath, value);
    }

    public string FinalManifestPath
    {
        get => _finalManifestPath;
        set => SetProperty(ref _finalManifestPath, value);
    }

    public string FinalBundlePath
    {
        get => _finalBundlePath;
        set => SetProperty(ref _finalBundlePath, value);
    }

    public string FinalEvidenceDirectory
    {
        get => _finalEvidenceDirectory;
        set => SetProperty(ref _finalEvidenceDirectory, value);
    }

    public string FinalEvidenceTargetSummary => _config is null
        ? "No customer package loaded."
        : $"{_config.Customer.TenantName} | {_config.Azure.ResourceGroupName}";

    public string FinalEvidenceTargetDetails => _config is null
        ? "Complete validation before generating final evidence."
        : $"Validation evidence: {ValidationOutputPath}";

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
    public bool IsWorkflowSelected => _workflowSelected;
    public bool HasRestorableSession
    {
        get => _hasRestorableSession;
        set => SetProperty(ref _hasRestorableSession, value);
    }

    public string ResumeSessionSummary
    {
        get => _resumeSessionSummary;
        set => SetProperty(ref _resumeSessionSummary, value);
    }

    public string ResumeSessionDetails
    {
        get => _resumeSessionDetails;
        set => SetProperty(ref _resumeSessionDetails, value);
    }

    public bool IsRemovalWorkflowAvailable => RemovalWorkflowEnabled();

    public string RemovalAvailabilityText
    {
        get => _removalAvailabilityText;
        set => SetProperty(ref _removalAvailabilityText, value);
    }

    public string RemovalWorkflowButtonText
    {
        get => _removalWorkflowButtonText;
        set => SetProperty(ref _removalWorkflowButtonText, value);
    }

    private Task SelectSetupModeAsync()
    {
        _stateId = InstallerStateStore.CreateStateId();
        _stateCreatedAt = DateTimeOffset.UtcNow;
        _stateCompleted = false;
        _pendingResumeState = null;
        HasRestorableSession = false;
        ConfigureSetupWorkflow();
        _workflowSelected = true;
        FooterStatus = "Setup workflow selected. Continue to load the customer install package.";
        OnPropertyChanged(nameof(IsWorkflowSelected));
        RefreshStepNavigation();
        SaveWizardState();
        return Task.CompletedTask;
    }

    private Task SelectRemovalModeAsync()
    {
        if (!IsRemovalWorkflowAvailable)
        {
            FooterStatus = "Removal workflow is disabled in this build. It will be enabled after inventory, preview, cleanup, and validation commands are implemented.";
            AiTitle = "Removal workflow unavailable";
            AiSummary = "This build can document the removal concept, but it should not be used as a customer uninstall tool yet.";
            return Task.CompletedTask;
        }

        _stateId = InstallerStateStore.CreateStateId();
        _stateCreatedAt = DateTimeOffset.UtcNow;
        _stateCompleted = false;
        _pendingResumeState = null;
        HasRestorableSession = false;
        ConfigureRemovalWorkflow();
        _workflowSelected = true;
        FooterStatus = "Removal workflow selected. Continue to discover or load the existing PageMaker365 deployment.";
        OnPropertyChanged(nameof(IsWorkflowSelected));
        RefreshStepNavigation();
        SaveWizardState();
        return Task.CompletedTask;
    }

    private async Task LoadSampleBootstrapAsync()
    {
        var path = FindSampleBootstrapPath();
        if (path is null)
        {
            _bootstrapSourcePath = "";
            LoadBootstrapSession(OnboardingSessionService.CreateFallbackSession(), "Built-in demo onboarding session");
            SaveWizardState();
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
        _bootstrapSourcePath = path;
        if (validation.Warnings.Count > 0)
        {
            FooterStatus = string.Join(" ", validation.Warnings);
        }

        SaveWizardState();
    }

    private void LoadBootstrapSession(OnboardingBootstrapSession session, string source)
    {
        _bootstrapSession = session;
        _tenantDiscovery = null;
        _onboardingPortalStatus = null;
        _packageReadiness = null;
        OnboardingSessionId = session.SessionId;
        OnboardingApiBaseUrl = session.ApiBaseUrl;
        OnboardingStatus = $"Bootstrap loaded from {source}.";
        PortalSyncStatus = "Not synced";
        DiscoverySummary = "No discovery snapshot created.";
        DiscoveryOutputPath = "Not saved";
        ClearDiscoveryReview();
        ClearPortalWorkflowReview();
        PortalMissingFieldsSummary = "Package readiness has not been checked.";
        PackageReadinessStatus = "Not checked";
        PackageReadinessVersion = "Not available";
        PackageDownloadPath = "Not downloaded";
        PortalStatusOutputPath = "Not saved";
        if (_config is null)
        {
            CustomerName = session.CustomerName;
        }

        FooterStatus = $"Onboarding bootstrap loaded. Client: {_onboardingApiClient.ConnectionLabel}.";
        ConnectOnboardingCommand.RaiseCanExecuteChanged();
        RunDiscoveryCommand.RaiseCanExecuteChanged();
        SyncDiscoveryCommand.RaiseCanExecuteChanged();
        SaveDiscoveryCommand.RaiseCanExecuteChanged();
        CheckPackageReadinessCommand.RaiseCanExecuteChanged();
        DownloadGeneratedPackageCommand.RaiseCanExecuteChanged();
    }

    private async Task ConnectOnboardingAsync()
    {
        if (_bootstrapSession is null)
        {
            FooterStatus = "Load an onboarding bootstrap session first.";
            return;
        }

        if (!await EnsureBootstrapOperationAllowedAsync(
                OnboardingOperation.InstallStatusSync,
                "Portal connect",
                "Blocked",
                requirePortalSync: true))
        {
            return;
        }

        try
        {
            var connection = await _onboardingApiClient.ConnectAsync(_bootstrapSession);
            OnboardingStatus = $"{connection.Status}: {connection.Message}";
            PortalSyncStatus = $"Connected with correlation {connection.CorrelationId}.";
            PortalSyncReceipt.SessionId = connection.SessionId;
            PortalSyncReceipt.SyncStatus = connection.Status;
            PortalSyncReceipt.CorrelationId = connection.CorrelationId;
            PortalSyncReceipt.ErrorMessage = "";
            PortalSyncReceipt.SyncedAt = DateTimeOffset.UtcNow.ToString("u");
            FooterStatus = $"{_onboardingApiClient.ConnectionLabel} session connected.";
            SaveWizardState();
        }
        catch (Exception exception)
        {
            await HandlePortalOperationFailureAsync("Portal connect", "Connection failed", exception, saveReceipt: false);
        }
    }

    private async Task RunDiscoveryAsync()
    {
        if (_bootstrapSession is null)
        {
            FooterStatus = "Load an onboarding bootstrap session first.";
            return;
        }

        if (!await EnsureBootstrapOperationAllowedAsync(
                OnboardingOperation.TenantDiscovery,
                "Tenant discovery",
                "Discovery blocked",
                requirePortalSync: false))
        {
            return;
        }

        FooterStatus = "Running read-only install-readiness discovery.";
        _tenantDiscovery = await _tenantDiscoveryService.CreateDiscoveryAsync(
            _bootstrapSession,
            _config,
            GetWorkspaceRoot());
        _onboardingPortalStatus = null;
        _packageReadiness = null;
        DiscoverySummary = $"{_tenantDiscovery.Customer.TenantName}; tenant {_tenantDiscovery.Customer.TenantId}; SharePoint {_tenantDiscovery.SharePoint.SiteUrl}; subscription {_tenantDiscovery.Azure.SelectedSubscriptionId}";
        RefreshDiscoveryReview(_tenantDiscovery);
        DiscoveryOutputPath = "Not saved";
        PortalSyncStatus = "Discovery created locally. Not synced.";
        RefreshDiscoverySyncReadiness();
        PackageReadinessStatus = "Discovery changed; check readiness";
        PackageReadinessStatusBrush = "#FFB84D";
        PackageReadinessSummary = "Discovery has changed. Sync discovery to the portal, then check package readiness.";
        PortalMissingFieldsSummary = "Discovery is ready to sync. Check package readiness after portal sync.";
        PortalMissingFields.Clear();
        FooterStatus = "Tenant discovery created. Review, save a redacted copy, or sync it to the configured onboarding API.";
        AiTitle = "Discovery snapshot ready";
        AiSummary = "The installer has created an install-readiness payload shaped for portal onboarding. This snapshot can be saved locally or synced to the configured onboarding API.";
        SyncDiscoveryCommand.RaiseCanExecuteChanged();
        SaveDiscoveryCommand.RaiseCanExecuteChanged();
        DownloadGeneratedPackageCommand.RaiseCanExecuteChanged();
        SaveWizardState();
    }

    private async Task SyncDiscoveryAsync()
    {
        if (_bootstrapSession is null || _tenantDiscovery is null)
        {
            FooterStatus = "Create a discovery snapshot before syncing.";
            return;
        }

        if (!await EnsureBootstrapOperationAllowedAsync(
                OnboardingOperation.InstallStatusSync,
                "Discovery sync",
                "Sync blocked",
                requirePortalSync: true))
        {
            return;
        }

        if (HasBlockingDiscoveryFindings(_tenantDiscovery))
        {
            RefreshDiscoverySyncReadiness();
            FooterStatus = "Discovery has blocking findings. Resolve blockers before syncing to the portal.";
            return;
        }

        try
        {
            var submission = await _onboardingApiClient.SubmitDiscoveryAsync(_bootstrapSession, _tenantDiscovery);
            PortalSyncStatus = $"{submission.Status}: {submission.PortalRecordUrl}";
            PortalSyncReceipt.SessionId = submission.SessionId;
            PortalSyncReceipt.DiscoveryId = submission.DiscoveryId;
            PortalSyncReceipt.SyncStatus = submission.Status;
            PortalSyncReceipt.CorrelationId = submission.CorrelationId;
            PortalSyncReceipt.PortalRecordUrl = submission.PortalRecordUrl;
            PortalSyncReceipt.ErrorMessage = "";
            PortalSyncReceipt.SyncedAt = DateTimeOffset.UtcNow.ToString("u");
            FooterStatus = $"Discovery synced through {_onboardingApiClient.ConnectionLabel}.";
            await RefreshOnboardingPortalStatusAsync("Discovery synced. Package readiness refreshed.");
            await SavePortalSyncReceiptAsync();
            SaveWizardState();
        }
        catch (Exception exception)
        {
            await HandlePortalOperationFailureAsync("Discovery sync", "Sync failed", exception);
        }
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
        SaveWizardState();
    }

    private async Task CheckPackageReadinessAsync()
    {
        if (_bootstrapSession is null)
        {
            FooterStatus = "Load an onboarding bootstrap session before checking package readiness.";
            return;
        }

        if (!await EnsureBootstrapOperationAllowedAsync(
                OnboardingOperation.InstallStatusSync,
                "Package readiness check",
                "Blocked",
                requirePortalSync: true))
        {
            return;
        }

        try
        {
            await RefreshOnboardingPortalStatusAsync("Package readiness checked.");
        }
        catch (Exception exception)
        {
            await HandlePortalOperationFailureAsync("Package readiness check", "Readiness failed", exception);
        }
    }

    private async Task RefreshOnboardingPortalStatusAsync(string footerStatus)
    {
        if (_bootstrapSession is null)
        {
            return;
        }

        if (!await EnsureBootstrapOperationAllowedAsync(
                OnboardingOperation.InstallStatusSync,
                "Package readiness check",
                "Blocked",
                requirePortalSync: true))
        {
            return;
        }

        _onboardingPortalStatus = await _onboardingApiClient.GetOnboardingStatusAsync(_bootstrapSession, _tenantDiscovery, _config);
        _packageReadiness = _onboardingPortalStatus.PackageReadiness;
        PackageReadinessStatus = _packageReadiness.Status;
        PackageReadinessVersion = string.IsNullOrWhiteSpace(_packageReadiness.PackageVersion)
            ? "Not available"
            : _packageReadiness.PackageVersion;
        PackageDownloadPath = string.IsNullOrWhiteSpace(_packageReadiness.LocalPackagePath)
            ? "Not downloaded"
            : _packageReadiness.LocalPackagePath;
        PortalMissingFieldsSummary = _onboardingPortalStatus.MissingFields.Count == 0
            ? "No required onboarding fields are missing."
            : string.Join("; ", _onboardingPortalStatus.MissingFields.Select(field => $"{field.Label} ({field.FieldKey})"));
        RefreshPortalReadinessReview(_onboardingPortalStatus);
        PortalSyncStatus = $"{_onboardingPortalStatus.Status}: {_onboardingPortalStatus.PortalRecordUrl}";
        OnboardingStatus = $"{_onboardingPortalStatus.Status}: {_onboardingPortalStatus.Message}";

        ApplyPackageGenerationPolicyReview();

        var outputRoot = Path.Combine(GetWorkspaceRoot(), "support-bundle");
        PortalStatusOutputPath = await _onboardingApiClient.SaveStatusAsync(_onboardingPortalStatus, outputRoot);
        FooterStatus = $"{footerStatus} {_packageReadiness.Message}";
        DownloadGeneratedPackageCommand.RaiseCanExecuteChanged();
        await SavePortalSyncReceiptAsync();
        SaveWizardState();
    }

    private async Task HandlePortalOperationFailureAsync(
        string operation,
        string readinessStatus,
        Exception exception,
        bool saveReceipt = true)
    {
        var message = FormatPortalOperationFailure(operation, exception);
        FooterStatus = message;
        OnboardingStatus = message;
        PortalSyncStatus = message;
        PackageReadinessStatus = readinessStatus;
        PackageReadinessStatusBrush = "#FF5C7A";
        PackageReadinessSummary = message;

        if (_bootstrapSession is not null)
        {
            PortalSyncReceipt.SessionId = _bootstrapSession.SessionId;
        }

        if (_tenantDiscovery is not null)
        {
            PortalSyncReceipt.DiscoveryId = _tenantDiscovery.DiscoveryId;
        }

        PortalSyncReceipt.SyncStatus = "Failed";
        PortalSyncReceipt.PackageReadinessStatus = readinessStatus;
        PortalSyncReceipt.ErrorMessage = message;
        PortalSyncReceipt.SyncedAt = DateTimeOffset.UtcNow.ToString("u");
        if (exception is OnboardingApiException apiException && !string.IsNullOrWhiteSpace(apiException.CorrelationId))
        {
            PortalSyncReceipt.CorrelationId = apiException.CorrelationId;
        }

        if (saveReceipt)
        {
            await SavePortalSyncReceiptAsync();
        }

        DownloadGeneratedPackageCommand.RaiseCanExecuteChanged();
        SaveWizardState();
    }

    private async Task<bool> EnsureBootstrapOperationAllowedAsync(
        string operation,
        string operationLabel,
        string blockedStatus,
        bool requirePortalSync)
    {
        if (IsBootstrapOperationAllowed(operation, requirePortalSync, out var reason))
        {
            return true;
        }

        await ApplyBootstrapPolicyBlockAsync(operationLabel, blockedStatus, reason);
        return false;
    }

    private bool IsBootstrapOperationAllowed(
        string operation,
        bool requirePortalSync,
        out string reason)
    {
        reason = "";

        if (_bootstrapSession is null)
        {
            reason = "Load an onboarding bootstrap session first.";
            return false;
        }

        if (requirePortalSync && !_bootstrapSession.DiscoveryPolicy.AllowPortalSync)
        {
            reason = "Portal sync is not allowed by this onboarding bootstrap policy.";
            return false;
        }

        if (!_bootstrapSession.AllowsOperation(operation))
        {
            reason = $"The {operation} operation is not allowed by this onboarding bootstrap session.";
            return false;
        }

        return true;
    }

    private async Task ApplyBootstrapPolicyBlockAsync(
        string operationLabel,
        string blockedStatus,
        string reason)
    {
        var message = $"{operationLabel} not allowed: {reason}";
        FooterStatus = message;
        OnboardingStatus = message;
        PortalSyncStatus = message;
        DiscoverySyncReadinessSummary = message;
        DiscoverySyncStatusBrush = "#FF5C7A";
        PackageReadinessStatus = blockedStatus;
        PackageReadinessStatusBrush = "#FF5C7A";
        PackageReadinessSummary = message;

        if (_bootstrapSession is not null)
        {
            PortalSyncReceipt.SessionId = _bootstrapSession.SessionId;
        }

        if (_tenantDiscovery is not null)
        {
            PortalSyncReceipt.DiscoveryId = _tenantDiscovery.DiscoveryId;
        }

        PortalSyncReceipt.SyncStatus = "PolicyDenied";
        PortalSyncReceipt.PackageReadinessStatus = blockedStatus;
        PortalSyncReceipt.ErrorMessage = message;
        PortalSyncReceipt.SyncedAt = DateTimeOffset.UtcNow.ToString("u");
        await SavePortalSyncReceiptAsync();

        ConnectOnboardingCommand.RaiseCanExecuteChanged();
        RunDiscoveryCommand.RaiseCanExecuteChanged();
        SyncDiscoveryCommand.RaiseCanExecuteChanged();
        CheckPackageReadinessCommand.RaiseCanExecuteChanged();
        DownloadGeneratedPackageCommand.RaiseCanExecuteChanged();
        SaveWizardState();
    }

    private void ApplyPackageGenerationPolicyReview()
    {
        if (_packageReadiness is null ||
            !_packageReadiness.Status.Equals("Ready", StringComparison.OrdinalIgnoreCase) ||
            IsBootstrapOperationAllowed(
                OnboardingOperation.InstallPackageGeneration,
                requirePortalSync: true,
                out var reason))
        {
            return;
        }

        var message = $"Package download not allowed: {reason}";
        PackageReadinessStatus = "Blocked";
        PackageReadinessStatusBrush = "#FF5C7A";
        PackageReadinessSummary = message;
        PortalSyncReceipt.PackageReadinessStatus = "Blocked";
        PortalSyncReceipt.ErrorMessage = message;
    }

    private static string FormatPortalOperationFailure(string operation, Exception exception)
    {
        if (exception is OnboardingApiException apiException &&
            !string.IsNullOrWhiteSpace(apiException.CorrelationId))
        {
            return $"{operation} failed: {apiException.Message} Correlation: {apiException.CorrelationId}";
        }

        return $"{operation} failed: {exception.Message}";
    }

    private bool CanRunTenantDiscovery()
    {
        return _bootstrapSession is not null &&
            IsBootstrapOperationAllowed(
                OnboardingOperation.TenantDiscovery,
                requirePortalSync: false,
                out _);
    }

    private bool CanDownloadGeneratedPackage()
    {
        return _bootstrapSession is not null
            && _packageReadiness is not null
            && _packageReadiness.Status.Equals("Ready", StringComparison.OrdinalIgnoreCase)
            && IsBootstrapOperationAllowed(
                OnboardingOperation.InstallPackageGeneration,
                requirePortalSync: true,
                out _);
    }

    private async Task DownloadGeneratedPackageAsync()
    {
        if (_bootstrapSession is null)
        {
            FooterStatus = "Load an onboarding bootstrap session before downloading a generated package.";
            return;
        }

        if (!await EnsureBootstrapOperationAllowedAsync(
                OnboardingOperation.InstallPackageGeneration,
                "Package download",
                "Blocked",
                requirePortalSync: true))
        {
            return;
        }

        if (_packageReadiness is null)
        {
            try
            {
                await RefreshOnboardingPortalStatusAsync("Package readiness checked before download.");
            }
            catch (Exception exception)
            {
                await HandlePortalOperationFailureAsync("Package readiness check", "Readiness failed", exception);
                return;
            }
        }

        if (!CanDownloadGeneratedPackage() || _packageReadiness is null)
        {
            FooterStatus = "The portal package is not ready yet. Complete missing onboarding fields or sync discovery first.";
            return;
        }

        try
        {
            var download = await _onboardingApiClient.DownloadPackageAsync(
                _bootstrapSession,
                _packageReadiness,
                GetWorkspaceRoot(),
                _tenantDiscovery);
            PackageDownloadPath = download.PackagePath;
            PackageReadinessStatus = download.Status;
            PackageReadinessVersion = download.PackageVersion;
            PortalSyncReceipt.PackageVersion = download.PackageVersion;
            if (!string.IsNullOrWhiteSpace(download.CorrelationId))
            {
                PortalSyncReceipt.CorrelationId = download.CorrelationId;
            }

            if (!download.Status.Equals("Downloaded", StringComparison.OrdinalIgnoreCase))
            {
                FooterStatus = download.Message;
                PortalSyncReceipt.PackageReadinessStatus = download.Status;
                PortalSyncReceipt.ErrorMessage = "";
                await SavePortalSyncReceiptAsync();
                SaveWizardState();
                return;
            }

            var packageLoaded = await LoadPackageAsync(
                download.PackagePath,
                PackageProvenanceContext.ForPortalDownload(_bootstrapSession, _tenantDiscovery));
            if (!packageLoaded)
            {
                var validationMessage = FooterStatus;
                PackageDownloadPath = download.PackagePath;
                PackageReadinessStatus = "PackageInvalid";
                PackageReadinessStatusBrush = "#FF5C7A";
                PackageReadinessSummary = $"Generated package downloaded but failed local validation. {validationMessage}";
                PortalSyncReceipt.PackageReadinessStatus = PackageReadinessStatus;
                PortalSyncReceipt.ErrorMessage = PackageReadinessSummary;
                await SavePortalSyncReceiptAsync();
                DownloadGeneratedPackageCommand.RaiseCanExecuteChanged();
                SaveWizardState();
                return;
            }

            PackageDownloadPath = download.PackagePath;
            PackageReadinessStatus = "Downloaded";
            PackageReadinessStatusBrush = "#42D8A0";
            PackageReadinessSummary = "Generated package has been downloaded and loaded into the installer.";
            PackageReadinessVersion = download.PackageVersion;
            PortalSyncReceipt.PackageReadinessStatus = PackageReadinessStatus;
            PortalSyncReceipt.PackageVersion = download.PackageVersion;
            PortalSyncReceipt.ErrorMessage = "";
            FooterStatus = $"Generated install package downloaded and loaded: {download.PackagePath}";
            await SavePortalSyncReceiptAsync();
            DownloadGeneratedPackageCommand.RaiseCanExecuteChanged();
            SaveWizardState();
        }
        catch (Exception exception)
        {
            await HandlePortalOperationFailureAsync("Package download", "Download failed", exception);
        }
    }

    private async Task LoadSamplePackageAsync()
    {
        var path = FindSamplePackagePath();
        if (path is null)
        {
            LoadConfig(CreateFallbackConfig(), "Built-in demo package");
            SaveWizardState();
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

    private async Task<bool> LoadPackageAsync(
        string path,
        PackageProvenanceContext? provenanceContext = null)
    {
        var packageJson = await File.ReadAllTextAsync(path);
        CustomerInstallConfig config;
        try
        {
            config = await _configService.LoadAsync(path);
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException)
        {
            var loadValidation = new ConfigValidationResult();
            loadValidation.Errors.Add(exception.Message);
            ApplyPackageTrustReview(loadValidation);
            FooterStatus = exception.Message;
            return false;
        }

        var validation = _configService.Validate(config, packageJson, provenanceContext);
        if (!validation.IsValid)
        {
            ApplyPackageTrustReview(validation);
            FooterStatus = string.Join(" ", validation.Errors);
            return false;
        }

        LoadConfig(config, path, validation);
        if (validation.Warnings.Count > 0)
        {
            FooterStatus = string.Join(" ", validation.Warnings);
        }

        SaveWizardState();
        return true;
    }

    private void LoadConfig(CustomerInstallConfig config, string path, ConfigValidationResult? validation = null)
    {
        _config = config;
        _onboardingPortalStatus = null;
        _packageReadiness = null;
        validation ??= _configService.Validate(config);
        ApplyPackageTrustReview(validation);
        PackagePath = path;
        CustomerName = config.Customer.TenantName;
        AzureSubscription = $"{config.Azure.SubscriptionId} / {config.Azure.ResourceGroupName}";
        SharePointSite = config.SharePoint.SiteUrl;
        if (_bootstrapSession is not null)
        {
            PackageReadinessStatus = "Package changed; check readiness";
            PackageReadinessStatusBrush = "#FFB84D";
            PackageReadinessSummary = "Loaded package changed. Check package readiness again before download.";
            PortalMissingFieldsSummary = "Loaded package can be used as readiness context. Check package readiness again.";
            PortalMissingFields.Clear();
        }
        FooterStatus = IsRemovalMode
            ? "Customer package loaded. Sign in so the app can inventory the existing deployment."
            : "Customer package loaded. Run preflight checks next.";
        SetStepStatus(2, "Complete", "#42D8A0");
        SetStepStatus(3, "Current", "#19D8E9");
        SetStepStatus(4, "Ready", "#19D8E9");
        UnlockThroughStep(4);
        SetCurrentStep(3);
        ClearPreviewReview();
        ClearDeploymentReview();
        OnPropertyChanged(nameof(DeploymentConfirmationTarget));
        OnPropertyChanged(nameof(DeploymentConfirmationPrompt));
        OnPropertyChanged(nameof(DeploymentTargetSummary));
        OnPropertyChanged(nameof(DeploymentTargetDetails));
        OnPropertyChanged(nameof(ValidationTargetSummary));
        OnPropertyChanged(nameof(ValidationTargetDetails));
        OnPropertyChanged(nameof(FinalEvidenceTargetSummary));
        OnPropertyChanged(nameof(FinalEvidenceTargetDetails));
        ConnectAzureCommand.RaiseCanExecuteChanged();
        ConnectGraphCommand.RaiseCanExecuteChanged();
        RunPreflightCommand.RaiseCanExecuteChanged();
        RunPreviewCommand.RaiseCanExecuteChanged();
        RunInstallCommand.RaiseCanExecuteChanged();
        RunValidationCommand.RaiseCanExecuteChanged();
        CreateFinalEvidenceCommand.RaiseCanExecuteChanged();
        DownloadGeneratedPackageCommand.RaiseCanExecuteChanged();
    }

    private void ApplyPackageTrustReview(ConfigValidationResult validation)
    {
        PackageTrustStatus = validation.PackageTrustStatus;
        PackageTrustStatusBrush = BrushForPackageTrust(validation.PackageTrustStatus);
        PackageTrustSummary = validation.PackageTrustSummary;
        PackageExportId = validation.DeploymentExportId;
        PackageDeclaredHash = validation.DeclaredPackageHash;
        PackageComputedHash = validation.ComputedPackageHash;
    }

    private void ClearPackageTrustReview()
    {
        PackageTrustStatus = "Not checked";
        PackageTrustStatusBrush = "#8290AA";
        PackageTrustSummary = "Load a package to inspect export metadata and integrity.";
        PackageExportId = "Not available";
        PackageDeclaredHash = "Not available";
        PackageComputedHash = "Not checked";
    }

    private static string BrushForPackageTrust(string status)
    {
        return status switch
        {
            "Verified" => "#42D8A0",
            "Hash verified" => "#42D8A0",
            "Legacy package" => "#FFB84D",
            "Missing signature" => "#FF5C7A",
            "Hash mismatch" => "#FF5C7A",
            _ => "#8290AA"
        };
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

        var packageTrustResult = CreatePackageTrustStepResult();
        _session.Results.Add(packageTrustResult);
        CheckResults.Add(new CheckResultViewModel(packageTrustResult));

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
        RunPreviewCommand.RaiseCanExecuteChanged();
        SaveWizardState();
    }

    private InstallerStepResult CreatePackageTrustStepResult()
    {
        var packageJson = File.Exists(PackagePath) ? File.ReadAllText(PackagePath) : "";
        var validation = _configService.Validate(_config!, packageJson);
        ApplyPackageTrustReview(validation);

        var status = validation.IsValid
            ? validation.PackageTrustStatus.Equals("Verified", StringComparison.OrdinalIgnoreCase) ||
                validation.PackageTrustStatus.Equals("Hash verified", StringComparison.OrdinalIgnoreCase)
                ? InstallStatus.Passed
                : InstallStatus.Warning
            : InstallStatus.Failed;

        return new InstallerStepResult
        {
            StepName = "Package Trust",
            Code = validation.PackageTrustStatus switch
            {
                "Verified" => "DeploymentPackageTrustVerified",
                "Hash verified" => "DeploymentPackageHashVerified",
                "Legacy package" => "DeploymentPackageLegacyTrust",
                "Hash mismatch" => "DeploymentPackageHashMismatch",
                "Missing signature" => "DeploymentPackageSignatureMissing",
                _ => "DeploymentPackageTrustUnchecked"
            },
            Status = status,
            Summary = validation.PackageTrustSummary,
            Details = BuildPackageTrustDetails(validation),
            RetrySafe = status is not InstallStatus.Failed,
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow
        };
    }

    private static string BuildPackageTrustDetails(ConfigValidationResult validation)
    {
        var details = new[]
        {
            $"Export ID: {ValueOrNotAvailable(validation.DeploymentExportId)}",
            $"Declared hash: {ValueOrNotAvailable(validation.DeclaredPackageHash)}",
            $"Computed hash: {ValueOrNotAvailable(validation.ComputedPackageHash)}",
            $"Trust mode: {ValueOrNotAvailable(validation.TrustMode)}"
        };

        return string.Join(Environment.NewLine, details);
    }

    private static string ValueOrNotAvailable(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "Not available" : value;
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
            SaveWizardState();
        }

        return Task.CompletedTask;
    }

    private Task GoNextAsync()
    {
        if (!CanAdvanceFromCurrentStep())
        {
            FooterStatus = "Choose a workflow before continuing.";
            return Task.CompletedTask;
        }

        if (_currentStepNumber < _maxAccessibleStepNumber)
        {
            SetCurrentStep(_currentStepNumber + 1);
            SaveWizardState();
        }

        return Task.CompletedTask;
    }

    private Task GoToStepAsync(object? parameter)
    {
        if (TryGetStepNumber(parameter, out var stepNumber) && CanNavigateToStep(stepNumber))
        {
            SetCurrentStep(stepNumber);
            SaveWizardState();
        }

        return Task.CompletedTask;
    }

    private Task GoHomeAsync()
    {
        SetCurrentStep(1);
        FooterStatus = _workflowSelected
            ? "Review the selected workflow or choose a new workflow to restart this session."
            : "Choose setup to continue through the guided workflow.";
        SaveWizardState();
        return Task.CompletedTask;
    }

    private bool CanGoToStep(object? parameter)
    {
        return TryGetStepNumber(parameter, out var stepNumber) && CanNavigateToStep(stepNumber);
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
            step.IsAccessible = step.Number <= _maxAccessibleStepNumber && (step.Number == 1 || _workflowSelected);
            step.IsCurrent = step.Number == _currentStepNumber;
            RefreshStepStatusForNavigation(step);
        }

        CanGoBack = _currentStepNumber > 1;
        CanGoNext = _currentStepNumber < _maxAccessibleStepNumber && CanAdvanceFromCurrentStep();
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
        RunPreviewCommand?.RaiseCanExecuteChanged();
        RunInstallCommand?.RaiseCanExecuteChanged();
        RunValidationCommand?.RaiseCanExecuteChanged();
        CreateFinalEvidenceCommand?.RaiseCanExecuteChanged();
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
        OnPropertyChanged(nameof(IsWorkflowSelected));
        OnPropertyChanged(nameof(IsRemovalWorkflowAvailable));
    }

    private bool CanNavigateToStep(int stepNumber)
    {
        if (stepNumber < 1 || stepNumber > _maxAccessibleStepNumber)
        {
            return false;
        }

        return stepNumber == 1 || _workflowSelected;
    }

    private bool CanAdvanceFromCurrentStep()
    {
        return _currentStepNumber != 1 || _workflowSelected;
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
        ClearPreviewReview();
        ClearDeploymentReview();
        ClearValidationReview();
        ClearFinalEvidenceReview();
        CanContinue = false;
        RefreshStepNavigation();
    }

    private void ResetSessionData()
    {
        _config = null;
        _session = null;
        _bootstrapSession = null;
        _tenantDiscovery = null;
        _onboardingPortalStatus = null;
        _packageReadiness = null;
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
        PortalMissingFieldsSummary = "Package readiness has not been checked.";
        PackageReadinessStatus = "Not checked";
        PackageReadinessVersion = "Not available";
        PackageDownloadPath = "Not downloaded";
        PortalStatusOutputPath = "Not saved";
        ClearPackageTrustReview();
        ClearDiscoveryReview();
        ClearPortalWorkflowReview();
        ClearPreviewReview();
        ClearDeploymentReview();
        ClearValidationReview();
        ClearFinalEvidenceReview();
        SessionId = "No active session";
        SessionStatus = "Not started";
        FooterStatus = "Review the workflow requirements, then continue to the next step.";
        ConnectOnboardingCommand.RaiseCanExecuteChanged();
        RunDiscoveryCommand.RaiseCanExecuteChanged();
        SyncDiscoveryCommand.RaiseCanExecuteChanged();
        SaveDiscoveryCommand.RaiseCanExecuteChanged();
        CheckPackageReadinessCommand.RaiseCanExecuteChanged();
        DownloadGeneratedPackageCommand.RaiseCanExecuteChanged();
        ConnectAzureCommand.RaiseCanExecuteChanged();
        ConnectGraphCommand.RaiseCanExecuteChanged();
        RunPreflightCommand.RaiseCanExecuteChanged();
        RunPreviewCommand.RaiseCanExecuteChanged();
        RunInstallCommand.RaiseCanExecuteChanged();
        RunValidationCommand.RaiseCanExecuteChanged();
        CreateFinalEvidenceCommand.RaiseCanExecuteChanged();
        ExplainIssueCommand.RaiseCanExecuteChanged();
        GenerateAdminMessageCommand.RaiseCanExecuteChanged();
        CreateSupportBundleCommand.RaiseCanExecuteChanged();
    }

    private void DetectRestorableState()
    {
        var state = _stateStore.LoadMostRecentActive();
        if (state is null)
        {
            HasRestorableSession = false;
            ResumeSessionSummary = "No saved installer session found.";
            ResumeSessionDetails = "Start a new setup workflow to begin.";
            ResumeSessionCommand.RaiseCanExecuteChanged();
            ForgetResumeSessionCommand.RaiseCanExecuteChanged();
            return;
        }

        _pendingResumeState = state;
        HasRestorableSession = true;
        ResumeSessionSummary = $"Saved {WorkflowLabel(state.WorkflowMode)} session for {SavedOrDefault(state.CustomerName, "unknown customer")}.";
        ResumeSessionDetails = $"Step {state.CurrentStepNumber}; last saved {state.SavedAt.LocalDateTime:g}; package {SavedOrDefault(Path.GetFileName(state.PackagePath), "not loaded")}.";
        FooterStatus = "A previous active session was found. Resume it, start a new session, or forget it before continuing.";
        ResumeSessionCommand.RaiseCanExecuteChanged();
        ForgetResumeSessionCommand.RaiseCanExecuteChanged();
    }

    private Task ResumeSessionAsync()
    {
        if (_pendingResumeState is null)
        {
            DetectRestorableState();
            return Task.CompletedTask;
        }

        if (_pendingResumeState.WorkflowMode.Equals("Removal", StringComparison.OrdinalIgnoreCase) && !IsRemovalWorkflowAvailable)
        {
            FooterStatus = "The saved session uses the disabled removal workflow. Start a new setup session or forget the saved removal session.";
            return Task.CompletedTask;
        }

        _isRestoringState = true;
        try
        {
            ApplyPersistedState(_pendingResumeState);
            _workflowSelected = true;
            HasRestorableSession = false;
            _pendingResumeState = null;
            OnPropertyChanged(nameof(IsWorkflowSelected));
            ResumeSessionCommand.RaiseCanExecuteChanged();
            ForgetResumeSessionCommand.RaiseCanExecuteChanged();
        }
        finally
        {
            _isRestoringState = false;
        }

        RefreshStepNavigation();
        return Task.CompletedTask;
    }

    private Task StartNewSessionAsync()
    {
        _pendingResumeState = null;
        HasRestorableSession = false;
        _stateId = InstallerStateStore.CreateStateId();
        _stateCreatedAt = DateTimeOffset.UtcNow;
        _stateCompleted = false;
        ConfigureSetupWorkflow();
        _workflowSelected = false;
        ResumeSessionSummary = "No saved installer session selected.";
        ResumeSessionDetails = "Choose setup to begin a new customer session.";
        FooterStatus = "Started a new installer session. Choose setup to continue.";
        OnPropertyChanged(nameof(IsWorkflowSelected));
        RefreshStepNavigation();
        ResumeSessionCommand.RaiseCanExecuteChanged();
        ForgetResumeSessionCommand.RaiseCanExecuteChanged();
        return Task.CompletedTask;
    }

    private Task ForgetResumeSessionAsync()
    {
        if (_pendingResumeState is not null)
        {
            _stateStore.Delete(_pendingResumeState.StateId);
        }

        _pendingResumeState = null;
        HasRestorableSession = false;
        ResumeSessionSummary = "Saved installer session forgotten.";
        ResumeSessionDetails = "Choose setup to begin a new customer session.";
        FooterStatus = "Saved session was forgotten locally. No customer tenant resources were changed.";
        ResumeSessionCommand.RaiseCanExecuteChanged();
        ForgetResumeSessionCommand.RaiseCanExecuteChanged();
        return Task.CompletedTask;
    }

    private void ApplyPersistedState(PersistedInstallerState state)
    {
        if (state.WorkflowMode.Equals("Removal", StringComparison.OrdinalIgnoreCase))
        {
            ConfigureRemovalWorkflow();
        }
        else
        {
            ConfigureSetupWorkflow();
        }

        _stateId = string.IsNullOrWhiteSpace(state.StateId) ? InstallerStateStore.CreateStateId() : state.StateId;
        _stateCreatedAt = state.CreatedAt == default ? state.SavedAt : state.CreatedAt;
        _stateCompleted = state.IsCompleted;
        _workflowSelected = state.WorkflowSelected || state.CurrentStepNumber > 1 || state.Config is not null || state.InstallerSession is not null || state.TenantDiscovery is not null;
        _bootstrapSourcePath = state.BootstrapSourcePath;
        _config = state.Config ?? TryLoadConfig(state.PackagePath);
        _session = state.InstallerSession;
        _bootstrapSession = TryLoadBootstrapSession(_bootstrapSourcePath);
        _tenantDiscovery = state.TenantDiscovery;
        _onboardingPortalStatus = state.OnboardingPortalStatus;
        _packageReadiness = state.PackageReadiness ?? state.OnboardingPortalStatus?.PackageReadiness;

        if (_tenantDiscovery is not null)
        {
            RefreshDiscoveryReview(_tenantDiscovery);
        }

        if (_onboardingPortalStatus is not null)
        {
            RefreshPortalReadinessReview(_onboardingPortalStatus);
        }

        PackagePath = SavedOrDefault(state.PackagePath, PackagePath);
        CustomerName = SavedOrDefault(state.CustomerName, CustomerName);
        AzureSubscription = SavedOrDefault(state.AzureSubscription, AzureSubscription);
        SharePointSite = SavedOrDefault(state.SharePointSite, SharePointSite);
        OnboardingSessionId = SavedOrDefault(state.OnboardingSessionId, OnboardingSessionId);
        OnboardingStatus = SavedOrDefault(state.OnboardingStatus, OnboardingStatus);
        OnboardingApiBaseUrl = SavedOrDefault(state.OnboardingApiBaseUrl, OnboardingApiBaseUrl);
        DiscoverySummary = SavedOrDefault(state.DiscoverySummary, DiscoverySummary);
        DiscoveryOutputPath = SavedOrDefault(state.DiscoveryOutputPath, DiscoveryOutputPath);
        PortalSyncStatus = SavedOrDefault(state.PortalSyncStatus, PortalSyncStatus);
        PortalMissingFieldsSummary = SavedOrDefault(state.PortalMissingFieldsSummary, PortalMissingFieldsSummary);
        PackageReadinessStatus = SavedOrDefault(state.PackageReadinessStatus, PackageReadinessStatus);
        PackageReadinessVersion = SavedOrDefault(state.PackageReadinessVersion, PackageReadinessVersion);
        PackageDownloadPath = SavedOrDefault(state.PackageDownloadPath, PackageDownloadPath);
        PortalStatusOutputPath = SavedOrDefault(state.PortalStatusOutputPath, PortalStatusOutputPath);
        DiscoveryReviewSummary = SavedOrDefault(state.DiscoveryReviewSummary, DiscoveryReviewSummary);
        DiscoveredLibrariesSummary = SavedOrDefault(state.DiscoveredLibrariesSummary, DiscoveredLibrariesSummary);
        DiscoverySyncReadinessSummary = SavedOrDefault(state.DiscoverySyncReadinessSummary, DiscoverySyncReadinessSummary);
        DiscoverySyncStatusBrush = SavedOrDefault(state.DiscoverySyncStatusBrush, DiscoverySyncStatusBrush);
        PackageReadinessStatusBrush = SavedOrDefault(state.PackageReadinessStatusBrush, PackageReadinessStatusBrush);
        PackageReadinessSummary = SavedOrDefault(state.PackageReadinessSummary, PackageReadinessSummary);
        PackageTrustStatus = SavedOrDefault(state.PackageTrustStatus, PackageTrustStatus);
        PackageTrustStatusBrush = SavedOrDefault(state.PackageTrustStatusBrush, PackageTrustStatusBrush);
        PackageTrustSummary = SavedOrDefault(state.PackageTrustSummary, PackageTrustSummary);
        PackageExportId = SavedOrDefault(state.PackageExportId, PackageExportId);
        PackageDeclaredHash = SavedOrDefault(state.PackageDeclaredHash, PackageDeclaredHash);
        PackageComputedHash = SavedOrDefault(state.PackageComputedHash, PackageComputedHash);

        ApplyPortalSyncReceipt(state.PortalSyncReceipt);
        RebuildResultCollections(state);

        _lastPreviewStatus = state.LastPreviewStatus;
        _lastDeploymentStatus = state.LastDeploymentStatus;
        _lastValidationStatus = state.LastValidationStatus;
        PreviewStatus = SavedOrDefault(state.PreviewStatus, PreviewStatus);
        PreviewStatusBrush = SavedOrDefault(state.PreviewStatusBrush, PreviewStatusBrush);
        PreviewSummary = SavedOrDefault(state.PreviewSummary, PreviewSummary);
        PreviewOutputPath = SavedOrDefault(state.PreviewOutputPath, PreviewOutputPath);
        PreviewArtifactPath = SavedOrDefault(state.PreviewArtifactPath, PreviewArtifactPath);
        DeploymentStatus = SavedOrDefault(state.DeploymentStatus, DeploymentStatus);
        DeploymentStatusBrush = SavedOrDefault(state.DeploymentStatusBrush, DeploymentStatusBrush);
        DeploymentSummary = SavedOrDefault(state.DeploymentSummary, DeploymentSummary);
        DeploymentOutputPath = SavedOrDefault(state.DeploymentOutputPath, DeploymentOutputPath);
        DeploymentArtifactPath = SavedOrDefault(state.DeploymentArtifactPath, DeploymentArtifactPath);
        DeploymentApprovalManifestId = SavedOrDefault(state.DeploymentApprovalManifestId, DeploymentApprovalManifestId);
        DeploymentApprovalManifestPath = SavedOrDefault(state.DeploymentApprovalManifestPath, DeploymentApprovalManifestPath);
        DeploymentApprovalSummary = SavedOrDefault(state.DeploymentApprovalSummary, DeploymentApprovalSummary);
        ValidationStatus = SavedOrDefault(state.ValidationStatus, ValidationStatus);
        ValidationStatusBrush = SavedOrDefault(state.ValidationStatusBrush, ValidationStatusBrush);
        ValidationSummary = SavedOrDefault(state.ValidationSummary, ValidationSummary);
        ValidationOutputPath = SavedOrDefault(state.ValidationOutputPath, ValidationOutputPath);
        FinishStatus = SavedOrDefault(state.FinishStatus, FinishStatus);
        FinishStatusBrush = SavedOrDefault(state.FinishStatusBrush, FinishStatusBrush);
        FinishSummary = SavedOrDefault(state.FinishSummary, FinishSummary);
        FinalReportPath = SavedOrDefault(state.FinalReportPath, FinalReportPath);
        FinalManifestPath = SavedOrDefault(state.FinalManifestPath, FinalManifestPath);
        FinalBundlePath = SavedOrDefault(state.FinalBundlePath, FinalBundlePath);
        FinalEvidenceDirectory = SavedOrDefault(state.FinalEvidenceDirectory, FinalEvidenceDirectory);
        AiTitle = SavedOrDefault(state.AiTitle, AiTitle);
        AiSummary = SavedOrDefault(state.AiSummary, AiSummary);
        SessionId = SavedOrDefault(state.SessionId, SessionId);
        SessionStatus = SavedOrDefault(state.SessionStatus, SessionStatus);

        DeploymentApprovalConfirmed = false;
        DeploymentConfirmationText = "";

        _maxAccessibleStepNumber = Math.Clamp(Math.Max(state.MaxAccessibleStepNumber, 2), 1, Steps.Count);
        _currentStepNumber = Math.Clamp(state.CurrentStepNumber, 1, _maxAccessibleStepNumber);
        RefreshStepNavigation();
        ApplyPersistedStepStates(state.Steps);
        RefreshStateDependentCommands();

        FooterStatus = $"Resumed previous {WorkflowModeLabel()} session saved {state.SavedAt.LocalDateTime:g}. {state.FooterStatus}";
    }

    private void SaveWizardState(bool markCompleted = false)
    {
        if (_stateCompleted && !markCompleted)
        {
            return;
        }

        if (_isRestoringState || !ShouldPersistState())
        {
            return;
        }

        _stateStore.Save(CreatePersistedState(markCompleted));
        if (markCompleted)
        {
            _stateCompleted = true;
        }
    }

    private bool ShouldPersistState()
    {
        return _config is not null ||
            _bootstrapSession is not null ||
            _tenantDiscovery is not null ||
            _session is not null ||
            _currentStepNumber > 1 ||
            _workflowSelected ||
            _workflowMode.Equals("Removal", StringComparison.OrdinalIgnoreCase) ||
            !PackagePath.Equals("No customer package loaded.", StringComparison.OrdinalIgnoreCase);
    }

    private PersistedInstallerState CreatePersistedState(bool markCompleted)
    {
        return new PersistedInstallerState
        {
            StateId = _stateId,
            CreatedAt = _stateCreatedAt,
            CompletedAt = markCompleted || _stateCompleted ? DateTimeOffset.UtcNow : null,
            IsCompleted = markCompleted || _stateCompleted,
            WorkflowMode = _workflowMode,
            WorkflowSelected = _workflowSelected,
            CurrentStepNumber = _currentStepNumber,
            MaxAccessibleStepNumber = _maxAccessibleStepNumber,
            Steps = Steps.Select(step => new PersistedInstallerStepState
            {
                Number = step.Number,
                Name = step.Name,
                StatusLabel = step.StatusLabel,
                StatusBrush = step.StatusBrush
            }).ToList(),
            PackagePath = PackagePath,
            Config = _config,
            InstallerSession = _session,
            BootstrapSourcePath = _bootstrapSourcePath,
            CustomerName = CustomerName,
            AzureSubscription = AzureSubscription,
            SharePointSite = SharePointSite,
            OnboardingSessionId = OnboardingSessionId,
            OnboardingStatus = OnboardingStatus,
            OnboardingApiBaseUrl = OnboardingApiBaseUrl,
            TenantDiscovery = _tenantDiscovery,
            OnboardingPortalStatus = _onboardingPortalStatus,
            PackageReadiness = _packageReadiness,
            DiscoverySummary = DiscoverySummary,
            DiscoveryOutputPath = DiscoveryOutputPath,
            PortalSyncStatus = PortalSyncStatus,
            PortalMissingFieldsSummary = PortalMissingFieldsSummary,
            PackageReadinessStatus = PackageReadinessStatus,
            PackageReadinessVersion = PackageReadinessVersion,
            PackageDownloadPath = PackageDownloadPath,
            PortalStatusOutputPath = PortalStatusOutputPath,
            DiscoveryReviewSummary = DiscoveryReviewSummary,
            DiscoveredLibrariesSummary = DiscoveredLibrariesSummary,
            DiscoverySyncReadinessSummary = DiscoverySyncReadinessSummary,
            DiscoverySyncStatusBrush = DiscoverySyncStatusBrush,
            PackageReadinessStatusBrush = PackageReadinessStatusBrush,
            PackageReadinessSummary = PackageReadinessSummary,
            PackageTrustStatus = PackageTrustStatus,
            PackageTrustStatusBrush = PackageTrustStatusBrush,
            PackageTrustSummary = PackageTrustSummary,
            PackageExportId = PackageExportId,
            PackageDeclaredHash = PackageDeclaredHash,
            PackageComputedHash = PackageComputedHash,
            PortalSyncReceipt = new PersistedPortalSyncReceipt
            {
                SessionId = PortalSyncReceipt.SessionId,
                DiscoveryId = PortalSyncReceipt.DiscoveryId,
                SyncStatus = PortalSyncReceipt.SyncStatus,
                CorrelationId = PortalSyncReceipt.CorrelationId,
                PackageReadinessStatus = PortalSyncReceipt.PackageReadinessStatus,
                PackageVersion = PortalSyncReceipt.PackageVersion,
                PortalRecordUrl = PortalSyncReceipt.PortalRecordUrl,
                ReceiptOutputPath = PortalSyncReceipt.ReceiptOutputPath,
                SyncedAt = PortalSyncReceipt.SyncedAt,
                ErrorMessage = PortalSyncReceipt.ErrorMessage
            },
            CheckResults = CheckResults.Select(ToStepResult).ToList(),
            PreviewResults = PreviewResults.Select(ToStepResult).ToList(),
            DeploymentResults = DeploymentResults.Select(ToStepResult).ToList(),
            ValidationResults = ValidationResults.Select(ToStepResult).ToList(),
            LastPreviewStatus = _lastPreviewStatus,
            LastDeploymentStatus = _lastDeploymentStatus,
            LastValidationStatus = _lastValidationStatus,
            PreviewStatus = PreviewStatus,
            PreviewStatusBrush = PreviewStatusBrush,
            PreviewSummary = PreviewSummary,
            PreviewOutputPath = PreviewOutputPath,
            PreviewArtifactPath = PreviewArtifactPath,
            DeploymentStatus = DeploymentStatus,
            DeploymentStatusBrush = DeploymentStatusBrush,
            DeploymentSummary = DeploymentSummary,
            DeploymentOutputPath = DeploymentOutputPath,
            DeploymentArtifactPath = DeploymentArtifactPath,
            DeploymentApprovalManifestId = DeploymentApprovalManifestId,
            DeploymentApprovalManifestPath = DeploymentApprovalManifestPath,
            DeploymentApprovalSummary = DeploymentApprovalSummary,
            ValidationStatus = ValidationStatus,
            ValidationStatusBrush = ValidationStatusBrush,
            ValidationSummary = ValidationSummary,
            ValidationOutputPath = ValidationOutputPath,
            FinishStatus = FinishStatus,
            FinishStatusBrush = FinishStatusBrush,
            FinishSummary = FinishSummary,
            FinalReportPath = FinalReportPath,
            FinalManifestPath = FinalManifestPath,
            FinalBundlePath = FinalBundlePath,
            FinalEvidenceDirectory = FinalEvidenceDirectory,
            AiTitle = AiTitle,
            AiSummary = AiSummary,
            SessionId = SessionId,
            SessionStatus = SessionStatus,
            FooterStatus = FooterStatus
        };
    }

    private void ApplyPortalSyncReceipt(PersistedPortalSyncReceipt receipt)
    {
        PortalSyncReceipt.SessionId = receipt.SessionId;
        PortalSyncReceipt.DiscoveryId = receipt.DiscoveryId;
        PortalSyncReceipt.SyncStatus = receipt.SyncStatus;
        PortalSyncReceipt.CorrelationId = receipt.CorrelationId;
        PortalSyncReceipt.PackageReadinessStatus = receipt.PackageReadinessStatus;
        PortalSyncReceipt.PackageVersion = receipt.PackageVersion;
        PortalSyncReceipt.PortalRecordUrl = receipt.PortalRecordUrl;
        PortalSyncReceipt.ReceiptOutputPath = receipt.ReceiptOutputPath;
        PortalSyncReceipt.SyncedAt = receipt.SyncedAt;
        PortalSyncReceipt.ErrorMessage = receipt.ErrorMessage;
    }

    private void RebuildResultCollections(PersistedInstallerState state)
    {
        CheckResults.Clear();
        foreach (var result in state.CheckResults)
        {
            CheckResults.Add(new CheckResultViewModel(result));
        }

        PreviewResults.Clear();
        foreach (var result in state.PreviewResults)
        {
            PreviewResults.Add(new PreviewResultViewModel(result));
        }

        DeploymentResults.Clear();
        foreach (var result in state.DeploymentResults)
        {
            DeploymentResults.Add(new DeploymentResultViewModel(result));
        }

        ValidationResults.Clear();
        foreach (var result in state.ValidationResults)
        {
            ValidationResults.Add(new ValidationResultViewModel(result));
        }
    }

    private void ApplyPersistedStepStates(IEnumerable<PersistedInstallerStepState> steps)
    {
        foreach (var persisted in steps)
        {
            var step = Steps.FirstOrDefault(item => item.Number == persisted.Number);
            if (step is null)
            {
                continue;
            }

            step.StatusLabel = SavedOrDefault(persisted.StatusLabel, step.StatusLabel);
            step.StatusBrush = SavedOrDefault(persisted.StatusBrush, step.StatusBrush);
        }
    }

    private void RefreshStateDependentCommands()
    {
        OnPropertyChanged(nameof(DeploymentConfirmationTarget));
        OnPropertyChanged(nameof(DeploymentConfirmationPrompt));
        OnPropertyChanged(nameof(DeploymentTargetSummary));
        OnPropertyChanged(nameof(DeploymentTargetDetails));
        OnPropertyChanged(nameof(ValidationTargetSummary));
        OnPropertyChanged(nameof(ValidationTargetDetails));
        OnPropertyChanged(nameof(FinalEvidenceTargetSummary));
        OnPropertyChanged(nameof(FinalEvidenceTargetDetails));
        ConnectOnboardingCommand.RaiseCanExecuteChanged();
        RunDiscoveryCommand.RaiseCanExecuteChanged();
        SyncDiscoveryCommand.RaiseCanExecuteChanged();
        SaveDiscoveryCommand.RaiseCanExecuteChanged();
        CheckPackageReadinessCommand.RaiseCanExecuteChanged();
        DownloadGeneratedPackageCommand.RaiseCanExecuteChanged();
        ConnectAzureCommand.RaiseCanExecuteChanged();
        ConnectGraphCommand.RaiseCanExecuteChanged();
        RunPreflightCommand.RaiseCanExecuteChanged();
        RunPreviewCommand.RaiseCanExecuteChanged();
        RunInstallCommand.RaiseCanExecuteChanged();
        RunValidationCommand.RaiseCanExecuteChanged();
        CreateFinalEvidenceCommand.RaiseCanExecuteChanged();
        ExplainIssueCommand.RaiseCanExecuteChanged();
        GenerateAdminMessageCommand.RaiseCanExecuteChanged();
        CreateSupportBundleCommand.RaiseCanExecuteChanged();
    }

    private CustomerInstallConfig? TryLoadConfig(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            return _configService.LoadAsync(path).GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private OnboardingBootstrapSession? TryLoadBootstrapSession(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            return _onboardingSessionService.LoadBootstrapAsync(path).GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private string WorkflowModeLabel()
    {
        return _workflowMode.Equals("Removal", StringComparison.OrdinalIgnoreCase) ? "removal" : "setup";
    }

    private static string WorkflowLabel(string workflowMode)
    {
        return workflowMode.Equals("Removal", StringComparison.OrdinalIgnoreCase) ? "removal" : "setup";
    }

    private static bool RemovalWorkflowEnabled()
    {
        return bool.TryParse(Environment.GetEnvironmentVariable("PM365_ENABLE_REMOVAL_WORKFLOW"), out var enabled) && enabled;
    }

    private static string SavedOrDefault(string value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private static InstallerStepResult ToStepResult(CheckResultViewModel result)
    {
        return new InstallerStepResult
        {
            StepName = result.Name,
            Status = ParseInstallStatus(result.StatusLabel),
            Summary = result.Summary,
            RetrySafe = true
        };
    }

    private static InstallerStepResult ToStepResult(PreviewResultViewModel result)
    {
        return new InstallerStepResult
        {
            StepName = result.Name,
            Code = result.Code,
            Status = ParseInstallStatus(result.StatusLabel),
            Summary = result.Summary,
            Details = result.Details,
            RetrySafe = result.RetrySafeLabel.Equals("Retry safe", StringComparison.OrdinalIgnoreCase)
        };
    }

    private static InstallerStepResult ToStepResult(DeploymentResultViewModel result)
    {
        return new InstallerStepResult
        {
            StepName = result.Name,
            Code = result.Code,
            Status = ParseInstallStatus(result.StatusLabel),
            Summary = result.Summary,
            Details = result.Details,
            RetrySafe = result.RetrySafeLabel.Equals("Retry safe", StringComparison.OrdinalIgnoreCase),
            RequiresApproval = result.ApprovalLabel.Equals("Approval required", StringComparison.OrdinalIgnoreCase)
        };
    }

    private static InstallerStepResult ToStepResult(ValidationResultViewModel result)
    {
        return new InstallerStepResult
        {
            StepName = result.Name,
            Code = result.Code,
            Status = ParseInstallStatus(result.StatusLabel),
            Summary = result.Summary,
            Details = result.Details,
            RetrySafe = result.RetrySafeLabel.Equals("Retry safe", StringComparison.OrdinalIgnoreCase)
        };
    }

    private static InstallStatus ParseInstallStatus(string value)
    {
        return Enum.TryParse<InstallStatus>(value, ignoreCase: true, out var status)
            ? status
            : InstallStatus.NotStarted;
    }

    private bool CanRunPreview()
    {
        return _config is not null &&
            File.Exists(PackagePath) &&
            _currentStepNumber >= 5;
    }

    private async Task RunPreviewAsync()
    {
        if (_config is null)
        {
            FooterStatus = "Load a customer package before running deployment preview.";
            return;
        }

        if (!File.Exists(PackagePath))
        {
            FooterStatus = "Deployment preview requires a customer package file. Load a package from disk first.";
            return;
        }

        PreviewResults.Clear();
        PreviewStatus = "Running";
        PreviewStatusBrush = "#19D8E9";
        PreviewSummary = "Running Azure what-if. No resources will be deployed.";
        PreviewOutputPath = "Not saved";
        var previewArtifactOutputPath = PrepareArtifactOutputPath("preview", "azure-whatif.json");
        PreviewArtifactPath = previewArtifactOutputPath;
        _lastPreviewStatus = InstallStatus.Running;
        ClearDeploymentReview();
        FooterStatus = "Running Azure what-if deployment preview.";
        _session = _engine.CreateSession(_config, GetWorkspaceRoot());
        SessionId = _session.SessionId;
        SessionStatus = "Deployment preview running";
        SetCurrentStep(5);
        SetStepStatus(5, "Running", "#19D8E9");

        var previewResults = new List<InstallerStepResult>();
        var progress = new Progress<InstallerStepResult>(result =>
        {
            previewResults.Add(result);
            PreviewResults.Add(new PreviewResultViewModel(result));
        });

        await _engine.RunWhatIfAsync(_session, GetWorkspaceRoot(), PackagePath, previewArtifactOutputPath, progress);
        PreviewArtifactPath = GetArtifactPathFromResults(previewResults, previewArtifactOutputPath);

        var previewStatus = GetPreviewStatus(previewResults);
        _lastPreviewStatus = previewStatus;
        RefreshDeploymentReadiness();
        PreviewStatus = previewStatus.ToString();
        PreviewStatusBrush = BrushForStatus(previewStatus);
        PreviewSummary = previewStatus switch
        {
            InstallStatus.Passed => "Deployment preview completed. Review the what-if output before continuing to install.",
            InstallStatus.Warning => "Deployment preview completed with warnings. Review the warnings before continuing.",
            InstallStatus.Failed => "Deployment preview found a blocker. Resolve it before continuing to install.",
            _ => "Deployment preview did not return a final status."
        };
        SessionStatus = _session.Status.ToString();
        SetStepStatus(5, previewStatus == InstallStatus.Failed ? "Blocked" : "Complete", PreviewStatusBrush);
        if (previewStatus is InstallStatus.Passed or InstallStatus.Warning)
        {
            UnlockThroughStep(6);
        }

        PreviewOutputPath = await SavePreviewEvidenceAsync(previewResults, previewStatus, PreviewArtifactPath);
        FooterStatus = previewStatus == InstallStatus.Failed
            ? "Deployment preview failed. Review the preview details and retry."
            : "Deployment preview completed. Continue to install after reviewing the results.";
        AiTitle = previewStatus == InstallStatus.Failed ? "Preview blocker detected" : "Preview ready";
        AiSummary = previewStatus == InstallStatus.Failed
            ? "Azure what-if reported a blocking issue. Review the preview details, fix the prerequisite, then rerun preview."
            : "Azure what-if completed without deploying resources. Review the preview evidence before continuing.";

        RunPreviewCommand.RaiseCanExecuteChanged();
        RunInstallCommand.RaiseCanExecuteChanged();
        ExplainIssueCommand.RaiseCanExecuteChanged();
        GenerateAdminMessageCommand.RaiseCanExecuteChanged();
        CreateSupportBundleCommand.RaiseCanExecuteChanged();
        SaveWizardState();
    }

    private async Task<string> SavePreviewEvidenceAsync(
        IReadOnlyList<InstallerStepResult> previewResults,
        InstallStatus previewStatus,
        string previewArtifactPath)
    {
        if (_session is null)
        {
            return "Not saved";
        }

        var directory = Path.Combine(GetWorkspaceRoot(), "support-bundle", "preview");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "deployment-preview.json");
        var receipt = new
        {
            contractVersion = "0.1",
            sessionId = _session.SessionId,
            packagePath = PackagePath,
            previewStatus = previewStatus.ToString(),
            previewedAt = DateTimeOffset.UtcNow,
            azureWhatIfArtifactPath = previewArtifactPath,
            results = previewResults.Select(result => new
            {
                result.StepName,
                result.Code,
                status = result.Status.ToString(),
                result.Summary,
                result.Details,
                result.RetrySafe,
                result.RequiresApproval
            }).ToList()
        };
        var json = JsonSerializer.Serialize(receipt, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        await File.WriteAllTextAsync(path, json);
        return path;
    }

    private static InstallStatus GetPreviewStatus(IReadOnlyList<InstallerStepResult> results)
    {
        if (results.Count == 0)
        {
            return InstallStatus.Failed;
        }

        if (results.Any(result => result.Status == InstallStatus.Failed))
        {
            return InstallStatus.Failed;
        }

        if (results.Any(result => result.Status == InstallStatus.Warning))
        {
            return InstallStatus.Warning;
        }

        return InstallStatus.Passed;
    }

    private static string BrushForStatus(InstallStatus status)
    {
        return status switch
        {
            InstallStatus.Passed => "#42D8A0",
            InstallStatus.Warning => "#FFB84D",
            InstallStatus.Failed => "#FF5C7A",
            InstallStatus.Running => "#19D8E9",
            InstallStatus.Skipped => "#8290AA",
            _ => "#8290AA"
        };
    }

    private void ClearPreviewReview()
    {
        _lastPreviewStatus = InstallStatus.NotStarted;
        PreviewStatus = "Not run";
        PreviewStatusBrush = "#8290AA";
        PreviewSummary = "Run deployment preview to see Azure what-if results before install.";
        PreviewOutputPath = "Not saved";
        PreviewArtifactPath = "Not created";
        PreviewResults.Clear();
        RunInstallCommand?.RaiseCanExecuteChanged();
    }

    private bool CanRunInstall()
    {
        return IsSetupMode &&
            _config is not null &&
            File.Exists(PackagePath) &&
            _currentStepNumber >= 6 &&
            _lastPreviewStatus is InstallStatus.Passed or InstallStatus.Warning &&
            DeploymentApprovalConfirmed &&
            IsDeploymentConfirmationValid();
    }

    private async Task RunInstallAsync()
    {
        if (_config is null)
        {
            FooterStatus = "Load a customer package before running install.";
            return;
        }

        if (_lastPreviewStatus is not (InstallStatus.Passed or InstallStatus.Warning))
        {
            FooterStatus = "Run deployment preview successfully before installing.";
            return;
        }

        if (!CanRunInstall())
        {
            FooterStatus = "Review the preview, approve the install, and type the target resource group before running install.";
            return;
        }

        DeploymentResults.Clear();
        ClearValidationReview();
        DeploymentStatus = "Running";
        DeploymentStatusBrush = "#19D8E9";
        DeploymentSummary = "Running approved Azure deployment.";
        DeploymentOutputPath = "Not saved";
        var deploymentArtifactOutputPath = PrepareArtifactOutputPath("install", "azure-deployment.json");
        DeploymentArtifactPath = deploymentArtifactOutputPath;
        FooterStatus = "Running approved PageMaker365 deployment.";
        _session = _engine.CreateSession(_config, GetWorkspaceRoot());
        SessionId = _session.SessionId;
        SessionStatus = "Install running";
        SetCurrentStep(6);
        SetStepStatus(6, "Running", "#19D8E9");

        var deploymentResults = new List<InstallerStepResult>();
        var progress = new Progress<InstallerStepResult>(result =>
        {
            deploymentResults.Add(result);
            DeploymentResults.Add(new DeploymentResultViewModel(result));
        });

        await WriteDeploymentApprovalManifestAsync();
        SaveWizardState();

        await _engine.RunDeploymentAsync(_session, GetWorkspaceRoot(), PackagePath, deploymentArtifactOutputPath, progress);
        DeploymentArtifactPath = GetArtifactPathFromResults(deploymentResults, deploymentArtifactOutputPath);

        var deploymentStatus = GetDeploymentStatus(deploymentResults);
        _lastDeploymentStatus = deploymentStatus;
        DeploymentStatus = deploymentStatus.ToString();
        DeploymentStatusBrush = BrushForStatus(deploymentStatus);
        DeploymentSummary = deploymentStatus switch
        {
            InstallStatus.Passed => "Install completed. Continue to validation and smoke tests.",
            InstallStatus.Warning => "Install completed with warnings. Review details before validation.",
            InstallStatus.Skipped => "Install was skipped. Review approval and rerun when ready.",
            InstallStatus.Failed => "Install failed. Resolve the blocker before continuing.",
            _ => "Install did not return a final status."
        };
        SessionStatus = _session.Status.ToString();
        var deploymentStepStatus = deploymentStatus switch
        {
            InstallStatus.Failed => "Blocked",
            InstallStatus.Passed or InstallStatus.Warning => "Complete",
            InstallStatus.Skipped => "Skipped",
            _ => deploymentStatus.ToString()
        };
        SetStepStatus(6, deploymentStepStatus, DeploymentStatusBrush);
        if (deploymentStatus is InstallStatus.Passed or InstallStatus.Warning)
        {
            UnlockThroughStep(7);
        }
        RefreshValidationReadiness();

        DeploymentOutputPath = await SaveDeploymentEvidenceAsync(deploymentResults, deploymentStatus, DeploymentArtifactPath);
        OnPropertyChanged(nameof(ValidationTargetDetails));
        FooterStatus = deploymentStatus switch
        {
            InstallStatus.Passed => "Install completed. Continue to validation.",
            InstallStatus.Warning => "Install completed with warnings. Review install evidence, then continue to validation.",
            InstallStatus.Skipped => "Install was skipped. Confirm approval and retry when ready.",
            _ => "Install failed. Review deployment results and support evidence."
        };
        AiTitle = deploymentStatus == InstallStatus.Failed ? "Install blocker detected" : "Install evidence ready";
        AiSummary = deploymentStatus == InstallStatus.Failed
            ? "Azure deployment reported a blocking issue. Review the install result details, fix the prerequisite, then rerun install."
            : "Deployment evidence was saved. Run validation next to confirm API health, SharePoint access, and telemetry.";

        RunInstallCommand.RaiseCanExecuteChanged();
        RunValidationCommand.RaiseCanExecuteChanged();
        ExplainIssueCommand.RaiseCanExecuteChanged();
        GenerateAdminMessageCommand.RaiseCanExecuteChanged();
        CreateSupportBundleCommand.RaiseCanExecuteChanged();
        SaveWizardState();
    }

    private async Task WriteDeploymentApprovalManifestAsync()
    {
        if (_config is null)
        {
            throw new InvalidOperationException("A customer package must be loaded before writing deployment approval.");
        }

        var outputRoot = Path.Combine(GetWorkspaceRoot(), "support-bundle");
        var result = await _deploymentApprovalManifestService.CreateAsync(
            _config,
            new DeploymentApprovalManifestRequest
            {
                OutputRoot = outputRoot,
                InstallerVersion = InstallerVersion,
                WorkflowMode = _workflowMode,
                PackagePath = PackagePath,
                PackageExportId = PackageExportId,
                PackageTrustStatus = PackageTrustStatus,
                PackageTrustSummary = PackageTrustSummary,
                PackageDeclaredHash = PackageDeclaredHash,
                PackageComputedHash = PackageComputedHash,
                PreviewStatus = _lastPreviewStatus.ToString(),
                PreviewSummary = PreviewSummary,
                PreviewEvidencePath = GetApprovalPreviewEvidencePath(),
                PreviewResultCount = PreviewResults.Count,
                PreviewWarningCount = PreviewResults.Count(result => ParseInstallStatus(result.StatusLabel) == InstallStatus.Warning),
                PreviewFailureCount = PreviewResults.Count(result => ParseInstallStatus(result.StatusLabel) == InstallStatus.Failed),
                ApprovalConfirmed = DeploymentApprovalConfirmed,
                ConfirmationTarget = DeploymentConfirmationTarget,
                ConfirmationMatched = IsDeploymentConfirmationValid(),
                Acknowledgements = CreateDeploymentApprovalAcknowledgements()
            });

        DeploymentApprovalManifestId = result.ApprovalId;
        DeploymentApprovalManifestPath = result.ManifestPath;
        DeploymentApprovalSummary = result.Summary;
    }

    private List<DeploymentApprovalAcknowledgement> CreateDeploymentApprovalAcknowledgements()
    {
        return
        [
            new()
            {
                Code = "PreviewReviewed",
                Summary = $"Deployment preview was reviewed with status {_lastPreviewStatus}.",
                Accepted = true
            },
            new()
            {
                Code = "PackageTrustReviewed",
                Summary = $"Package trust status was reviewed as {PackageTrustStatus}.",
                Accepted = true
            },
            new()
            {
                Code = "TargetConfirmed",
                Summary = $"Deployment target {DeploymentConfirmationTarget} was confirmed without storing raw typed text.",
                Accepted = true
            },
            new()
            {
                Code = "DeploymentApproved",
                Summary = "Approved deployment execution after preview review.",
                Accepted = true
            }
        ];
    }

    private async Task<string> SaveDeploymentEvidenceAsync(
        IReadOnlyList<InstallerStepResult> deploymentResults,
        InstallStatus deploymentStatus,
        string deploymentArtifactPath)
    {
        if (_session is null)
        {
            return "Not saved";
        }

        var directory = Path.Combine(GetWorkspaceRoot(), "support-bundle", "install");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "deployment-install.json");
        var receipt = new
        {
            contractVersion = "0.1",
            sessionId = _session.SessionId,
            packagePath = PackagePath,
            deploymentStatus = deploymentStatus.ToString(),
            deployedAt = DateTimeOffset.UtcNow,
            previewStatus = _lastPreviewStatus.ToString(),
            previewEvidencePath = PreviewOutputPath,
            azureWhatIfArtifactPath = PreviewArtifactPath,
            azureDeploymentArtifactPath = deploymentArtifactPath,
            approval = new
            {
                approved = DeploymentApprovalConfirmed,
                confirmationTarget = DeploymentConfirmationTarget,
                confirmationMatched = IsDeploymentConfirmationValid(),
                rawConfirmationTextPersisted = false,
                approvalManifestId = DeploymentApprovalManifestId,
                approvalManifestPath = DeploymentApprovalManifestPath,
                approvalSummary = DeploymentApprovalSummary,
                approvedAt = DateTimeOffset.UtcNow
            },
            target = new
            {
                customerName = _config?.Customer.TenantName,
                subscriptionId = _config?.Azure.SubscriptionId,
                resourceGroupName = _config?.Azure.ResourceGroupName,
                sharePointSiteUrl = _config?.SharePoint.SiteUrl
            },
            results = deploymentResults.Select(result => new
            {
                result.StepName,
                result.Code,
                status = result.Status.ToString(),
                result.Summary,
                result.Details,
                result.RetrySafe,
                result.RequiresApproval
            }).ToList()
        };
        var json = JsonSerializer.Serialize(receipt, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        await File.WriteAllTextAsync(path, json);
        return path;
    }

    private bool IsDeploymentConfirmationValid()
    {
        return _config is not null &&
            string.Equals(
                DeploymentConfirmationText.Trim(),
                _config.Azure.ResourceGroupName,
                StringComparison.OrdinalIgnoreCase);
    }

    private static InstallStatus GetDeploymentStatus(IReadOnlyList<InstallerStepResult> results)
    {
        if (results.Count == 0)
        {
            return InstallStatus.Failed;
        }

        if (results.Any(result => result.Status == InstallStatus.Failed))
        {
            return InstallStatus.Failed;
        }

        if (results.Any(result => result.Status == InstallStatus.Warning))
        {
            return InstallStatus.Warning;
        }

        if (results.All(result => result.Status == InstallStatus.Skipped))
        {
            return InstallStatus.Skipped;
        }

        return InstallStatus.Passed;
    }

    private void ClearDeploymentReview()
    {
        _lastDeploymentStatus = InstallStatus.NotStarted;
        DeploymentResults.Clear();
        DeploymentArtifactPath = "Not created";
        DeploymentApprovalConfirmed = false;
        DeploymentConfirmationText = "";
        ClearDeploymentApprovalManifestState();
        ClearValidationReview();
        RefreshDeploymentReadiness();
    }

    private void ClearDeploymentApprovalManifestState()
    {
        DeploymentApprovalManifestId = "";
        DeploymentApprovalManifestPath = "Not created";
        DeploymentApprovalSummary = "No deployment approval manifest created.";
    }

    private void RefreshDeploymentReadiness()
    {
        if (_lastPreviewStatus is InstallStatus.Passed or InstallStatus.Warning)
        {
            DeploymentStatus = "Ready for approval";
            DeploymentStatusBrush = "#FFB84D";
            DeploymentSummary = "Review the deployment preview evidence, approve the install, then type the target resource group.";
        }
        else
        {
            DeploymentStatus = "Waiting for preview";
            DeploymentStatusBrush = "#8290AA";
            DeploymentSummary = "Complete deployment preview before running install.";
        }

        DeploymentOutputPath = "Not saved";
        OnPropertyChanged(nameof(DeploymentConfirmationTarget));
        OnPropertyChanged(nameof(DeploymentConfirmationPrompt));
        OnPropertyChanged(nameof(DeploymentTargetSummary));
        OnPropertyChanged(nameof(DeploymentTargetDetails));
        OnPropertyChanged(nameof(ValidationTargetSummary));
        OnPropertyChanged(nameof(ValidationTargetDetails));
        RunInstallCommand?.RaiseCanExecuteChanged();
    }

    private bool CanRunValidation()
    {
        return IsSetupMode &&
            _config is not null &&
            File.Exists(PackagePath) &&
            _currentStepNumber >= 7 &&
            _lastDeploymentStatus is InstallStatus.Passed or InstallStatus.Warning;
    }

    private async Task RunValidationAsync()
    {
        if (_config is null)
        {
            FooterStatus = "Load a customer package before running validation.";
            return;
        }

        if (_lastDeploymentStatus is not (InstallStatus.Passed or InstallStatus.Warning))
        {
            FooterStatus = "Complete install before running validation.";
            return;
        }

        if (!File.Exists(PackagePath))
        {
            FooterStatus = "Validation requires a customer package file. Load a package from disk first.";
            return;
        }

        ValidationResults.Clear();
        ValidationStatus = "Running";
        ValidationStatusBrush = "#19D8E9";
        ValidationSummary = "Running smoke tests against the deployed environment.";
        ValidationOutputPath = "Not saved";
        FooterStatus = "Running deployment validation.";
        _session = _engine.CreateSession(_config, GetWorkspaceRoot());
        SessionId = _session.SessionId;
        SessionStatus = "Validation running";
        SetCurrentStep(7);
        SetStepStatus(7, "Running", "#19D8E9");

        var validationResults = new List<InstallerStepResult>();
        var progress = new Progress<InstallerStepResult>(result =>
        {
            validationResults.Add(result);
            ValidationResults.Add(new ValidationResultViewModel(result));
        });

        await _engine.RunValidationAsync(_session, GetWorkspaceRoot(), PackagePath, progress);

        var validationStatus = GetPhaseStatus(validationResults);
        _lastValidationStatus = validationStatus;
        ValidationStatus = validationStatus.ToString();
        ValidationStatusBrush = BrushForStatus(validationStatus);
        ValidationSummary = validationStatus switch
        {
            InstallStatus.Passed => "Validation completed. Continue to finish and generate final evidence.",
            InstallStatus.Warning => "Validation completed with warnings. Review the warnings before finishing.",
            InstallStatus.Skipped => "Validation was skipped. Review the smoke-test output and rerun when ready.",
            InstallStatus.Failed => "Validation failed. Resolve the blocker before finishing.",
            _ => "Validation did not return a final status."
        };
        SessionStatus = _session.Status.ToString();
        var validationStepStatus = validationStatus switch
        {
            InstallStatus.Failed => "Blocked",
            InstallStatus.Passed or InstallStatus.Warning => "Complete",
            InstallStatus.Skipped => "Skipped",
            _ => validationStatus.ToString()
        };
        SetStepStatus(7, validationStepStatus, ValidationStatusBrush);
        if (validationStatus is InstallStatus.Passed or InstallStatus.Warning)
        {
            UnlockThroughStep(8);
        }

        ValidationOutputPath = await SaveValidationEvidenceAsync(validationResults, validationStatus);
        RefreshFinalEvidenceReadiness();
        FooterStatus = validationStatus switch
        {
            InstallStatus.Passed => "Validation completed. Continue to finish.",
            InstallStatus.Warning => "Validation completed with warnings. Review evidence, then continue to finish.",
            InstallStatus.Skipped => "Validation was skipped. Rerun validation when ready.",
            _ => "Validation failed. Review validation results and support evidence."
        };
        AiTitle = validationStatus == InstallStatus.Failed ? "Validation blocker detected" : "Validation evidence ready";
        AiSummary = validationStatus == InstallStatus.Failed
            ? "Smoke tests reported a blocking issue. Review validation details, fix the environment, then rerun validation."
            : "Validation evidence was saved. Continue to Finish to generate final customer evidence.";

        RunValidationCommand.RaiseCanExecuteChanged();
        CreateFinalEvidenceCommand.RaiseCanExecuteChanged();
        ExplainIssueCommand.RaiseCanExecuteChanged();
        GenerateAdminMessageCommand.RaiseCanExecuteChanged();
        CreateSupportBundleCommand.RaiseCanExecuteChanged();
        SaveWizardState();
    }

    private async Task<string> SaveValidationEvidenceAsync(
        IReadOnlyList<InstallerStepResult> validationResults,
        InstallStatus validationStatus)
    {
        if (_session is null)
        {
            return "Not saved";
        }

        var directory = Path.Combine(GetWorkspaceRoot(), "support-bundle", "validate");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "deployment-validation.json");
        var receipt = new
        {
            contractVersion = "0.1",
            sessionId = _session.SessionId,
            packagePath = PackagePath,
            validationStatus = validationStatus.ToString(),
            validatedAt = DateTimeOffset.UtcNow,
            deploymentStatus = _lastDeploymentStatus.ToString(),
            deploymentEvidencePath = DeploymentOutputPath,
            target = new
            {
                customerName = _config?.Customer.TenantName,
                subscriptionId = _config?.Azure.SubscriptionId,
                resourceGroupName = _config?.Azure.ResourceGroupName,
                sharePointSiteUrl = _config?.SharePoint.SiteUrl,
                appDomain = _config?.App.CustomDomain
            },
            results = validationResults.Select(result => new
            {
                result.StepName,
                result.Code,
                status = result.Status.ToString(),
                result.Summary,
                result.Details,
                result.RetrySafe,
                result.RequiresApproval
            }).ToList()
        };
        var json = JsonSerializer.Serialize(receipt, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        await File.WriteAllTextAsync(path, json);
        return path;
    }

    private static InstallStatus GetPhaseStatus(IReadOnlyList<InstallerStepResult> results)
    {
        if (results.Count == 0)
        {
            return InstallStatus.Failed;
        }

        if (results.Any(result => result.Status == InstallStatus.Failed))
        {
            return InstallStatus.Failed;
        }

        if (results.Any(result => result.Status == InstallStatus.Warning))
        {
            return InstallStatus.Warning;
        }

        if (results.All(result => result.Status == InstallStatus.Skipped))
        {
            return InstallStatus.Skipped;
        }

        return InstallStatus.Passed;
    }

    private void ClearValidationReview()
    {
        _lastValidationStatus = InstallStatus.NotStarted;
        ValidationResults.Clear();
        ClearFinalEvidenceReview();
        RefreshValidationReadiness();
    }

    private void RefreshValidationReadiness()
    {
        if (_lastDeploymentStatus is InstallStatus.Passed or InstallStatus.Warning)
        {
            ValidationStatus = "Ready to validate";
            ValidationStatusBrush = "#FFB84D";
            ValidationSummary = "Run smoke tests to confirm the deployed app and SharePoint access are ready.";
        }
        else
        {
            ValidationStatus = "Waiting for install";
            ValidationStatusBrush = "#8290AA";
            ValidationSummary = "Complete install before running validation.";
        }

        ValidationOutputPath = "Not saved";
        OnPropertyChanged(nameof(ValidationTargetSummary));
        OnPropertyChanged(nameof(ValidationTargetDetails));
        OnPropertyChanged(nameof(FinalEvidenceTargetSummary));
        OnPropertyChanged(nameof(FinalEvidenceTargetDetails));
        RunValidationCommand?.RaiseCanExecuteChanged();
    }

    private bool CanCreateFinalEvidence()
    {
        return IsSetupMode &&
            _config is not null &&
            File.Exists(PackagePath) &&
            _currentStepNumber >= 8 &&
            _lastValidationStatus is InstallStatus.Passed or InstallStatus.Warning;
    }

    private async Task CreateFinalEvidenceAsync()
    {
        if (_config is null)
        {
            FooterStatus = "Load a customer package before generating final evidence.";
            return;
        }

        if (_lastValidationStatus is not (InstallStatus.Passed or InstallStatus.Warning))
        {
            FooterStatus = "Complete validation before generating final evidence.";
            return;
        }

        FinishStatus = "Generating";
        FinishStatusBrush = "#19D8E9";
        FinishSummary = "Creating final report, manifest, and evidence zip.";
        FooterStatus = "Generating final install evidence package.";
        SetCurrentStep(8);
        SetStepStatus(8, "Running", "#19D8E9");

        var outputRoot = Path.Combine(GetWorkspaceRoot(), "support-bundle");
        var result = await _finalEvidenceService.CreateAsync(
            _config,
            new FinalEvidenceRequest
            {
                OutputRoot = outputRoot,
                InstallerVersion = InstallerVersion,
                PackagePath = PackagePath,
                PreviewStatus = _lastPreviewStatus.ToString(),
                PreviewEvidencePath = PreviewOutputPath,
                PreviewArtifactPath = PreviewArtifactPath,
                ApprovalManifestPath = DeploymentApprovalManifestPath,
                DeploymentStatus = _lastDeploymentStatus.ToString(),
                DeploymentEvidencePath = DeploymentOutputPath,
                DeploymentArtifactPath = DeploymentArtifactPath,
                ValidationStatus = _lastValidationStatus.ToString(),
                ValidationEvidencePath = ValidationOutputPath,
                FinalStatus = GetFinalStatusLabel()
            });

        FinalEvidenceDirectory = result.EvidenceDirectory;
        FinalReportPath = result.ReportPath;
        FinalManifestPath = result.ManifestPath;
        FinalBundlePath = result.BundlePath;
        FinishStatus = "Complete";
        FinishStatusBrush = "#42D8A0";
        FinishSummary = "Final install evidence package is ready for PageMaker365 records and customer handoff.";
        SetStepStatus(8, "Complete", "#42D8A0");
        FooterStatus = $"Final evidence package created: {FinalBundlePath}";
        AiTitle = "Install workflow complete";
        AiSummary = "The final report, manifest, and evidence package are ready. Retain the package in customer records and complete the handoff steps.";

        CreateFinalEvidenceCommand.RaiseCanExecuteChanged();
        CreateSupportBundleCommand.RaiseCanExecuteChanged();
        SaveWizardState(markCompleted: true);
    }

    private string GetFinalStatusLabel()
    {
        return _lastValidationStatus == InstallStatus.Warning ||
            _lastDeploymentStatus == InstallStatus.Warning ||
            _lastPreviewStatus == InstallStatus.Warning
                ? "CompletedWithWarnings"
                : "Completed";
    }

    private void ClearFinalEvidenceReview()
    {
        FinishStatus = "Waiting for validation";
        FinishStatusBrush = "#8290AA";
        FinishSummary = "Complete validation before generating final evidence.";
        FinalReportPath = "Not created";
        FinalManifestPath = "Not created";
        FinalBundlePath = "Not created";
        FinalEvidenceDirectory = "Not created";
        RefreshFinalEvidenceReadiness();
    }

    private void RefreshFinalEvidenceReadiness()
    {
        if (_lastValidationStatus is InstallStatus.Passed or InstallStatus.Warning)
        {
            FinishStatus = "Ready to package";
            FinishStatusBrush = "#FFB84D";
            FinishSummary = "Generate the final report, manifest, and evidence zip for customer records.";
        }

        OnPropertyChanged(nameof(FinalEvidenceTargetSummary));
        OnPropertyChanged(nameof(FinalEvidenceTargetDetails));
        CreateFinalEvidenceCommand?.RaiseCanExecuteChanged();
    }

    private bool CanSyncDiscovery()
    {
        return _bootstrapSession is not null &&
            _tenantDiscovery is not null &&
            IsBootstrapOperationAllowed(
                OnboardingOperation.InstallStatusSync,
                requirePortalSync: true,
                out _) &&
            !HasBlockingDiscoveryFindings(_tenantDiscovery);
    }

    private void ClearPortalWorkflowReview()
    {
        DiscoverySyncReadinessSummary = "Create discovery before syncing onboarding data to the portal.";
        DiscoverySyncStatusBrush = "#8290AA";
        PackageReadinessStatusBrush = "#8290AA";
        PackageReadinessSummary = "Package readiness has not been checked.";
        PortalMissingFields.Clear();
        PortalSyncReceipt.Reset();
    }

    private void RefreshDiscoverySyncReadiness()
    {
        if (_tenantDiscovery is null)
        {
            DiscoverySyncReadinessSummary = "Create discovery before syncing onboarding data to the portal.";
            DiscoverySyncStatusBrush = "#8290AA";
            SyncDiscoveryCommand.RaiseCanExecuteChanged();
            return;
        }

        if (!IsBootstrapOperationAllowed(
                OnboardingOperation.InstallStatusSync,
                requirePortalSync: true,
                out var policyReason))
        {
            var message = $"Discovery sync not allowed: {policyReason}";
            DiscoverySyncReadinessSummary = message;
            DiscoverySyncStatusBrush = "#FF5C7A";
            PortalSyncStatus = message;
            SyncDiscoveryCommand.RaiseCanExecuteChanged();
            return;
        }

        var blockedFindings = _tenantDiscovery.Findings.Where(IsBlockedFinding).ToList();
        if (blockedFindings.Count > 0)
        {
            DiscoverySyncReadinessSummary = $"{blockedFindings.Count} blocking discovery finding{(blockedFindings.Count == 1 ? "" : "s")} must be resolved before portal sync.";
            DiscoverySyncStatusBrush = "#FF5C7A";
            SyncDiscoveryCommand.RaiseCanExecuteChanged();
            return;
        }

        var warningCount = _tenantDiscovery.Findings.Count(IsWarningFinding);
        if (warningCount > 0)
        {
            DiscoverySyncReadinessSummary = $"{warningCount} warning{(warningCount == 1 ? "" : "s")} found. Discovery can sync, but review warnings before package generation.";
            DiscoverySyncStatusBrush = "#FFB84D";
            SyncDiscoveryCommand.RaiseCanExecuteChanged();
            return;
        }

        DiscoverySyncReadinessSummary = "Discovery is ready to sync to the portal.";
        DiscoverySyncStatusBrush = "#42D8A0";
        SyncDiscoveryCommand.RaiseCanExecuteChanged();
    }

    private void RefreshPortalReadinessReview(OnboardingPortalStatus status)
    {
        PortalMissingFields.Clear();
        foreach (var field in status.MissingFields)
        {
            PortalMissingFields.Add(new MissingFieldViewModel(field));
        }

        if (PortalMissingFields.Count == 0)
        {
            PortalMissingFieldsSummary = "No required onboarding fields are missing.";
        }

        PackageReadinessStatusBrush = status.PackageReadiness.Status switch
        {
            "Ready" => "#42D8A0",
            "Downloaded" => "#42D8A0",
            "NeedsCustomerInput" => "#FFB84D",
            "NotReady" => "#FFB84D",
            "Failed" => "#FF5C7A",
            "Blocked" => "#FF5C7A",
            "Unauthorized" => "#FF5C7A",
            "InvalidResponse" => "#FF5C7A",
            "DownloadFailed" => "#FF5C7A",
            "PackageInvalid" => "#FF5C7A",
            _ => "#8290AA"
        };
        PackageReadinessSummary = string.IsNullOrWhiteSpace(status.PackageReadiness.Message)
            ? status.Message
            : status.PackageReadiness.Message;

        PortalSyncReceipt.SessionId = status.SessionId;
        PortalSyncReceipt.PackageReadinessStatus = status.PackageReadiness.Status;
        PortalSyncReceipt.PackageVersion = status.PackageReadiness.PackageVersion;
        PortalSyncReceipt.PortalRecordUrl = status.PortalRecordUrl;
        PortalSyncReceipt.ErrorMessage = "";
        if (!string.IsNullOrWhiteSpace(status.CorrelationId))
        {
            PortalSyncReceipt.CorrelationId = status.CorrelationId;
        }

        if (!string.IsNullOrWhiteSpace(_tenantDiscovery?.DiscoveryId))
        {
            PortalSyncReceipt.DiscoveryId = _tenantDiscovery.DiscoveryId;
        }
    }

    private async Task SavePortalSyncReceiptAsync()
    {
        if (_bootstrapSession is null)
        {
            return;
        }

        var directory = Path.Combine(GetWorkspaceRoot(), "support-bundle", "onboarding", _bootstrapSession.SessionId);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "portal-sync-receipt.json");
        var receipt = new
        {
            contractVersion = "0.1",
            sessionId = PortalSyncReceipt.SessionId,
            discoveryId = PortalSyncReceipt.DiscoveryId,
            syncStatus = PortalSyncReceipt.SyncStatus,
            portalCorrelationId = PortalSyncReceipt.CorrelationId,
            portalRecordUrl = PortalSyncReceipt.PortalRecordUrl,
            packageReadinessStatus = PortalSyncReceipt.PackageReadinessStatus,
            packageVersion = PortalSyncReceipt.PackageVersion,
            portalStatusOutputPath = PortalStatusOutputPath,
            packageDownloadPath = PackageDownloadPath,
            errorMessage = PortalSyncReceipt.ErrorMessage,
            savedAt = DateTimeOffset.UtcNow
        };
        var json = JsonSerializer.Serialize(receipt, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        await File.WriteAllTextAsync(path, json);
        PortalSyncReceipt.ReceiptOutputPath = path;
    }

    private static bool HasBlockingDiscoveryFindings(TenantDiscoveryResult discovery)
    {
        return discovery.Findings.Any(IsBlockedFinding);
    }

    private void ClearDiscoveryReview()
    {
        DiscoveryReviewSummary = "Create discovery to populate Azure, Microsoft Graph, SharePoint, and Entra readiness.";
        DiscoveredLibrariesSummary = "No SharePoint document libraries discovered yet.";

        DiscoveryReadinessCards.Clear();
        DiscoveryReadinessCards.Add(new DiscoveryReadinessCardViewModel("Azure", "Not checked", "Run discovery to inspect Azure tenant and subscription readiness.", "#8290AA"));
        DiscoveryReadinessCards.Add(new DiscoveryReadinessCardViewModel("Microsoft Graph", "Not checked", "Run discovery after Graph sign-in to inspect tenant and scope readiness.", "#8290AA"));
        DiscoveryReadinessCards.Add(new DiscoveryReadinessCardViewModel("SharePoint", "Not checked", "Run discovery to resolve the target site and document library.", "#8290AA"));
        DiscoveryReadinessCards.Add(new DiscoveryReadinessCardViewModel("Entra", "Not checked", "Run discovery to inspect consent and administrator readiness.", "#8290AA"));

        DiscoveryValues.Clear();
        DiscoveryValues.Add(new DiscoveryValueViewModel("Discovery", "Status", "Not checked"));

        DiscoveryFindings.Clear();
        DiscoveryFindings.Add(new DiscoveryFindingViewModel(new DiscoveryFinding
        {
            Severity = "Info",
            Code = "DiscoveryNotStarted",
            Summary = "No discovery snapshot has been created.",
            Details = "Load an onboarding bootstrap session, then create discovery to review install-readiness values."
        }));

        DiscoveredLibraries.Clear();
    }

    private void RefreshDiscoveryReview(TenantDiscoveryResult discovery)
    {
        DiscoveryReviewSummary = $"Discovery source: {discovery.Source}. Created {discovery.DiscoveredAt:u}.";

        DiscoveryReadinessCards.Clear();
        DiscoveryReadinessCards.Add(BuildReadinessCard(
            "Azure",
            discovery.Findings.Where(IsAzureFinding),
            discovery.Source.Contains("AzurePowerShell", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(discovery.Azure.SelectedSubscriptionId),
            $"Subscription {ValueOrPlaceholder(discovery.Azure.SelectedSubscriptionName, discovery.Azure.SelectedSubscriptionId)}; resource group {ValueOrPlaceholder(discovery.Azure.TargetResourceGroupName)}.",
            "No Azure subscription has been confirmed yet."));
        DiscoveryReadinessCards.Add(BuildReadinessCard(
            "Microsoft Graph",
            discovery.Findings.Where(IsGraphFinding),
            discovery.Source.Contains("Graph", StringComparison.OrdinalIgnoreCase),
            "Graph discovery ran and returned tenant metadata.",
            "Graph discovery has not run yet."));
        DiscoveryReadinessCards.Add(BuildReadinessCard(
            "SharePoint",
            discovery.Findings.Where(IsSharePointFinding),
            discovery.Source.Contains("Graph", StringComparison.OrdinalIgnoreCase) && discovery.SharePoint.SiteResolved,
            $"Resolved {ValueOrPlaceholder(discovery.SharePoint.SiteDisplayName, discovery.SharePoint.SiteUrl)}.",
            "The target SharePoint site has not been resolved yet."));
        DiscoveryReadinessCards.Add(BuildReadinessCard(
            "Entra",
            discovery.Findings.Where(IsEntraFinding),
            discovery.Entra.ConsentStatus.Equals("AdminRoleReady", StringComparison.OrdinalIgnoreCase),
            "An administrator-capable Graph account was detected.",
            $"Consent status is {ValueOrPlaceholder(discovery.Entra.ConsentStatus)}."));

        DiscoveryValues.Clear();
        AddDiscoveryValue("Discovery", "Source", discovery.Source);
        AddDiscoveryValue("Discovery", "Data Policy", discovery.DataPolicy);
        AddDiscoveryValue("Customer", "Tenant Name", discovery.Customer.TenantName);
        AddDiscoveryValue("Customer", "Tenant ID", discovery.Customer.TenantId);
        AddDiscoveryValue("Customer", "Default Domain", discovery.Customer.DefaultDomain);
        AddDiscoveryValue("Customer", "Verified Domains", JoinValues(discovery.Customer.VerifiedDomains));
        AddDiscoveryValue("Azure", "Signed-In Account", discovery.Azure.AccountId);
        AddDiscoveryValue("Azure", "Azure Tenant", discovery.Azure.TenantId);
        AddDiscoveryValue("Azure", "Subscription ID", discovery.Azure.SelectedSubscriptionId);
        AddDiscoveryValue("Azure", "Subscription Name", discovery.Azure.SelectedSubscriptionName);
        AddDiscoveryValue("Azure", "Subscription State", discovery.Azure.SelectedSubscriptionState);
        AddDiscoveryValue("Azure", "Resource Group", discovery.Azure.TargetResourceGroupName);
        AddDiscoveryValue("Azure", "Resource Group Exists", discovery.Azure.ResourceGroupExists ? "Yes" : "No");
        AddDiscoveryValue("Azure", "Location", discovery.Azure.RecommendedLocation);
        AddDiscoveryValue("SharePoint", "Tenant Hostname", discovery.SharePoint.TenantHostname);
        AddDiscoveryValue("SharePoint", "Site URL", discovery.SharePoint.SiteUrl);
        AddDiscoveryValue("SharePoint", "Site ID", discovery.SharePoint.SiteId);
        AddDiscoveryValue("SharePoint", "Site Name", discovery.SharePoint.SiteDisplayName);
        AddDiscoveryValue("SharePoint", "Default Library", discovery.SharePoint.DefaultDocumentLibrary);
        AddDiscoveryValue("SharePoint", "Library Drive ID", discovery.SharePoint.DefaultDocumentLibraryId);
        AddDiscoveryValue("Entra", "Signed-In Account", discovery.Entra.AccountId);
        AddDiscoveryValue("Entra", "Graph Scopes", JoinValues(discovery.Entra.Scopes));
        AddDiscoveryValue("Entra", "App Registration", discovery.Entra.AppRegistrationMode);
        AddDiscoveryValue("Entra", "Consent Status", discovery.Entra.ConsentStatus);
        AddDiscoveryValue("Entra", "Permission Mode", discovery.Entra.PermissionMode);
        AddDiscoveryValue("Entra", "Application Permissions", JoinValues(discovery.Entra.RequiredApplicationPermissions));
        AddDiscoveryValue("Entra", "Delegated Scopes", JoinValues(discovery.Entra.RequiredDelegatedScopes));

        DiscoveryFindings.Clear();
        foreach (var finding in discovery.Findings
            .OrderBy(GetFindingRank)
            .ThenBy(finding => finding.Code, StringComparer.OrdinalIgnoreCase))
        {
            DiscoveryFindings.Add(new DiscoveryFindingViewModel(finding));
        }

        if (DiscoveryFindings.Count == 0)
        {
            DiscoveryFindings.Add(new DiscoveryFindingViewModel(new DiscoveryFinding
            {
                Severity = "Info",
                Code = "DiscoveryNoFindings",
                Summary = "No discovery findings were reported.",
                Details = "The discovery provider did not return warnings or blockers."
            }));
        }

        DiscoveredLibraries.Clear();
        foreach (var library in discovery.SharePoint.AvailableDocumentLibraries)
        {
            DiscoveredLibraries.Add(new SharePointLibraryViewModel(library));
        }

        DiscoveredLibrariesSummary = DiscoveredLibraries.Count == 0
            ? "No SharePoint document libraries discovered yet."
            : $"{DiscoveredLibraries.Count} SharePoint document librar{(DiscoveredLibraries.Count == 1 ? "y" : "ies")} discovered for the target site.";
    }

    private void AddDiscoveryValue(string section, string label, string value)
    {
        DiscoveryValues.Add(new DiscoveryValueViewModel(section, label, value));
    }

    private static DiscoveryReadinessCardViewModel BuildReadinessCard(
        string name,
        IEnumerable<DiscoveryFinding> findings,
        bool ready,
        string readySummary,
        string notCheckedSummary)
    {
        var categoryFindings = findings.ToList();
        var blocked = categoryFindings.FirstOrDefault(IsBlockedFinding);
        if (blocked is not null)
        {
            return new DiscoveryReadinessCardViewModel(name, "Blocked", blocked.Summary, "#FF5C7A");
        }

        var warning = categoryFindings.FirstOrDefault(IsWarningFinding);
        if (warning is not null)
        {
            return new DiscoveryReadinessCardViewModel(name, "Warning", warning.Summary, "#FFB84D");
        }

        return ready
            ? new DiscoveryReadinessCardViewModel(name, "Ready", readySummary, "#42D8A0")
            : new DiscoveryReadinessCardViewModel(name, "Not checked", notCheckedSummary, "#8290AA");
    }

    private static bool IsAzureFinding(DiscoveryFinding finding)
    {
        return StartsWithAny(finding.Code, "Azure", "Az");
    }

    private static bool IsGraphFinding(DiscoveryFinding finding)
    {
        return StartsWithAny(finding.Code, "Graph");
    }

    private static bool IsSharePointFinding(DiscoveryFinding finding)
    {
        return StartsWithAny(finding.Code, "SharePoint");
    }

    private static bool IsEntraFinding(DiscoveryFinding finding)
    {
        return StartsWithAny(finding.Code, "Entra") ||
            finding.Code.Contains("Consent", StringComparison.OrdinalIgnoreCase) ||
            finding.Code.Contains("AdminRole", StringComparison.OrdinalIgnoreCase);
    }

    private static bool StartsWithAny(string value, params string[] prefixes)
    {
        return !string.IsNullOrWhiteSpace(value) &&
            prefixes.Any(prefix => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsBlockedFinding(DiscoveryFinding finding)
    {
        return finding.Severity.Equals("Failed", StringComparison.OrdinalIgnoreCase) ||
            finding.Severity.Equals("Blocked", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWarningFinding(DiscoveryFinding finding)
    {
        return finding.Severity.Equals("Warning", StringComparison.OrdinalIgnoreCase);
    }

    private static int GetFindingRank(DiscoveryFinding finding)
    {
        if (IsBlockedFinding(finding))
        {
            return 0;
        }

        if (IsWarningFinding(finding))
        {
            return 1;
        }

        if (finding.Severity.Equals("Skipped", StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        return 3;
    }

    private static string JoinValues(IEnumerable<string> values)
    {
        var cleanValues = values.Where(value => !string.IsNullOrWhiteSpace(value)).ToList();
        return cleanValues.Count == 0 ? "Not discovered" : string.Join(", ", cleanValues);
    }

    private static string ValueOrPlaceholder(params string[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "Not discovered";
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

    private Task OpenAssistantAsync()
    {
        if (_assistantWindow is { IsVisible: true })
        {
            _assistantWindow.Activate();
            return Task.CompletedTask;
        }

        var viewModel = new AssistantWorkspaceViewModel(
            CreateAssistantDiagnosticContext(),
            GetWorkspaceRoot(),
            _redactionService,
            new AssistantActionHandlers
            {
                CreateSupportBundleAsync = CreateSupportBundleForAssistantAsync,
                DraftAdminMessageAsync = DraftAdminMessageForAssistantAsync,
                RerunPreflightAsync = RerunPreflightForAssistantAsync
            });
        _assistantWindow = new AssistantWorkspaceWindow(viewModel)
        {
            Owner = Application.Current.MainWindow
        };
        _assistantWindow.Closed += (_, _) => _assistantWindow = null;
        _assistantWindow.Show();
        return Task.CompletedTask;
    }

    private async Task<string> CreateSupportBundleForAssistantAsync()
    {
        if (_session is null)
        {
            return "No active installer session exists yet. Run sign-in or preflight before creating a support bundle.";
        }

        await CreateSupportBundleAsync();
        return FooterStatus;
    }

    private async Task<string> DraftAdminMessageForAssistantAsync()
    {
        await GenerateAdminMessageAsync();
        return AiSummary;
    }

    private async Task<string> RerunPreflightForAssistantAsync()
    {
        if (_config is null)
        {
            return "Load a customer package before rerunning preflight.";
        }

        await RunPreflightAsync();
        return FooterStatus;
    }

    private AssistantDiagnosticContext CreateAssistantDiagnosticContext()
    {
        var currentStep = Steps.FirstOrDefault(step => step.IsCurrent);
        return new AssistantDiagnosticContext
        {
            WorkflowMode = _workflowMode,
            WorkflowTitle = WorkflowTitle,
            CurrentStep = currentStep is null ? $"Step {_currentStepNumber}" : $"{currentStep.Number}. {currentStep.Name}",
            CustomerName = CustomerName,
            PackagePath = PackagePath,
            AzureSubscription = AzureSubscription,
            SharePointSite = SharePointSite,
            OnboardingSessionId = OnboardingSessionId,
            OnboardingStatus = OnboardingStatus,
            OnboardingApiBaseUrl = OnboardingApiBaseUrl,
            PortalSyncStatus = PortalSyncStatus,
            DiscoverySummary = DiscoverySummary,
            DiscoveryOutputPath = DiscoveryOutputPath,
            InstallerSessionId = SessionId,
            InstallerSessionStatus = SessionStatus,
            FooterStatus = FooterStatus,
            Checks = _session?.Results.Select(ToAssistantCheckSummary).ToList() ?? []
        };
    }

    private static AssistantCheckSummary ToAssistantCheckSummary(InstallerStepResult result)
    {
        return new AssistantCheckSummary
        {
            Name = result.StepName,
            Code = result.Code,
            Status = result.Status.ToString(),
            Summary = result.Summary,
            RetrySafe = result.RetrySafe,
            RequiresApproval = result.RequiresApproval
        };
    }

    private string? FindSamplePackagePath()
    {
        foreach (var root in EnumerateWorkspaceCandidateRoots())
        {
            var candidate = Path.Combine(root, "samples", "contoso.customer.install.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private string? FindSampleBootstrapPath()
    {
        foreach (var root in EnumerateWorkspaceCandidateRoots())
        {
            var candidate = Path.Combine(root, "samples", "contoso.onboarding.bootstrap.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private IEnumerable<string> EnumerateWorkspaceCandidateRoots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(_workspaceRoot) && seen.Add(_workspaceRoot))
        {
            yield return _workspaceRoot;
        }

        foreach (var root in EnumerateCandidateRoots())
        {
            if (seen.Add(root))
            {
                yield return root;
            }
        }
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

    private string GetWorkspaceRoot()
    {
        return _workspaceRoot;
    }

    private static string ResolveWorkspaceRoot()
    {
        return EnumerateCandidateRoots().FirstOrDefault(root => Directory.Exists(Path.Combine(root, "samples")))
            ?? Environment.CurrentDirectory;
    }

    private static string GetArtifactPathFromResults(IEnumerable<InstallerStepResult> results, string fallbackPath)
    {
        var artifactPath = results
            .Select(result => result.Data.TryGetValue("artifactPath", out var value) ? value : "")
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        return NormalizeArtifactPath(artifactPath, fallbackPath);
    }

    private static string NormalizeArtifactPath(string? artifactPath, string fallbackPath)
    {
        var value = artifactPath?.Trim().Trim('"') ?? "";
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return File.Exists(fallbackPath) ? fallbackPath : "Not created";
    }

    private string PrepareArtifactOutputPath(string phaseDirectory, string fileName)
    {
        var directory = Path.Combine(GetWorkspaceRoot(), "support-bundle", phaseDirectory);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return path;
    }

    private string GetApprovalPreviewEvidencePath()
    {
        return File.Exists(PreviewArtifactPath) ? PreviewArtifactPath : PreviewOutputPath;
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
