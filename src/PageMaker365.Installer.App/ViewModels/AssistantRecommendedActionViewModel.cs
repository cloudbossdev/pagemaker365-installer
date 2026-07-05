using PageMaker365.Installer.Engine.Models;

namespace PageMaker365.Installer.App.ViewModels;

public sealed class AssistantRecommendedActionViewModel : ViewModelBase
{
    private string _executionStatus = "Ready";

    public AssistantRecommendedActionViewModel(AssistantRecommendedAction model)
    {
        Model = model;
    }

    public AssistantRecommendedAction Model { get; }
    public string ActionId => Model.ActionId;
    public string Label => Model.Label;
    public string Description => Model.Description;
    public string Category => Model.Category;
    public bool RequiresApproval => Model.RequiresApproval;
    public bool Enabled => Model.Enabled;
    public string ApprovalLabel => RequiresApproval ? "Approval required" : "Ready";

    public string ExecutionStatus
    {
        get => _executionStatus;
        set => SetProperty(ref _executionStatus, value);
    }
}
