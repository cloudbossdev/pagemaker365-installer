namespace PageMaker365.Installer.App.ViewModels;

public sealed class PortalSyncReceiptViewModel : ViewModelBase
{
    private string _sessionId = "Not synced";
    private string _discoveryId = "Not synced";
    private string _syncStatus = "Not synced";
    private string _correlationId = "Not available";
    private string _packageReadinessStatus = "Not checked";
    private string _portalRecordUrl = "Not available";
    private string _receiptOutputPath = "Not saved";
    private string _syncedAt = "Not synced";

    public string SessionId
    {
        get => _sessionId;
        set => SetProperty(ref _sessionId, string.IsNullOrWhiteSpace(value) ? "Not synced" : value);
    }

    public string DiscoveryId
    {
        get => _discoveryId;
        set => SetProperty(ref _discoveryId, string.IsNullOrWhiteSpace(value) ? "Not synced" : value);
    }

    public string SyncStatus
    {
        get => _syncStatus;
        set => SetProperty(ref _syncStatus, string.IsNullOrWhiteSpace(value) ? "Not synced" : value);
    }

    public string CorrelationId
    {
        get => _correlationId;
        set => SetProperty(ref _correlationId, string.IsNullOrWhiteSpace(value) ? "Not available" : value);
    }

    public string PackageReadinessStatus
    {
        get => _packageReadinessStatus;
        set => SetProperty(ref _packageReadinessStatus, string.IsNullOrWhiteSpace(value) ? "Not checked" : value);
    }

    public string PortalRecordUrl
    {
        get => _portalRecordUrl;
        set => SetProperty(ref _portalRecordUrl, string.IsNullOrWhiteSpace(value) ? "Not available" : value);
    }

    public string ReceiptOutputPath
    {
        get => _receiptOutputPath;
        set => SetProperty(ref _receiptOutputPath, string.IsNullOrWhiteSpace(value) ? "Not saved" : value);
    }

    public string SyncedAt
    {
        get => _syncedAt;
        set => SetProperty(ref _syncedAt, string.IsNullOrWhiteSpace(value) ? "Not synced" : value);
    }

    public void Reset()
    {
        SessionId = "Not synced";
        DiscoveryId = "Not synced";
        SyncStatus = "Not synced";
        CorrelationId = "Not available";
        PackageReadinessStatus = "Not checked";
        PortalRecordUrl = "Not available";
        ReceiptOutputPath = "Not saved";
        SyncedAt = "Not synced";
    }
}
