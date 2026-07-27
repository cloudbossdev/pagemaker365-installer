using PageMaker365.Installer.Engine.Models;

namespace PageMaker365.Installer.Engine.Services;

public sealed class AssistantActionPolicy
{
    private static readonly IReadOnlyDictionary<string, AssistantRecommendedAction> LocalActions =
        new Dictionary<string, AssistantRecommendedAction>(StringComparer.Ordinal)
        {
            ["create-support-bundle"] = Action(
                "create-support-bundle",
                "Create support bundle",
                "Create a local sanitized installer support bundle.",
                "Support",
                requiresApproval: false),
            ["create-support-ticket-draft"] = Action(
                "create-support-ticket-draft",
                "Create support ticket draft",
                "Prepare a reviewable support ticket draft and the explicitly approved handoff.",
                "Support",
                requiresApproval: true),
            ["draft-admin-message"] = Action(
                "draft-admin-message",
                "Draft admin message",
                "Create an administrator-facing remediation message.",
                "Communication",
                requiresApproval: false),
            ["rerun-preflight"] = Action(
                "rerun-preflight",
                "Rerun preflight",
                "Run the installer preflight checks again.",
                "Installer",
                requiresApproval: true),
            ["open-portal-outbox"] = Action(
                "open-portal-outbox",
                "Open portal outbox",
                "Open the local support handoff outbox.",
                "Support",
                requiresApproval: false),
            ["copy-escalation-summary"] = Action(
                "copy-escalation-summary",
                "Copy escalation summary",
                "Copy a sanitized issue summary to the clipboard.",
                "Communication",
                requiresApproval: false)
        };

    public IReadOnlyList<AssistantRecommendedAction> Normalize(
        IEnumerable<AssistantRecommendedAction> recommendations)
    {
        ArgumentNullException.ThrowIfNull(recommendations);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var normalized = new List<AssistantRecommendedAction>();
        foreach (var recommendation in recommendations)
        {
            if (!recommendation.Enabled ||
                !seen.Add(recommendation.ActionId) ||
                !LocalActions.TryGetValue(recommendation.ActionId, out var local))
            {
                continue;
            }

            normalized.Add(Action(
                local.ActionId,
                local.Label,
                local.Description,
                local.Category,
                local.RequiresApproval));
        }

        return normalized;
    }

    private static AssistantRecommendedAction Action(
        string actionId,
        string label,
        string description,
        string category,
        bool requiresApproval)
    {
        return new AssistantRecommendedAction
        {
            ActionId = actionId,
            Label = label,
            Description = description,
            Category = category,
            RequiresApproval = requiresApproval,
            Enabled = true
        };
    }
}
