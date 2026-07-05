using PageMaker365.Installer.Engine.Models;

namespace PageMaker365.Installer.Engine.Services;

public sealed class MockAssistantService
{
    public Task<AssistantMessage> CreateResponseAsync(
        AssistantConversation conversation,
        AssistantMessage userMessage,
        bool includeDiagnostics,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var context = conversation.DiagnosticContext;
        var failedCheck = context.Checks.FirstOrDefault(check => check.Status.Equals("Failed", StringComparison.OrdinalIgnoreCase));
        var warningCheck = context.Checks.FirstOrDefault(check => check.Status.Equals("Warning", StringComparison.OrdinalIgnoreCase));
        var imageCount = userMessage.Attachments.Count(attachment => attachment.IsImage);
        var fileCount = userMessage.Attachments.Count - imageCount;
        var lowerMessage = userMessage.Content.ToLowerInvariant();

        var response = new List<string>
        {
            "This scaffold is running in local advisory mode. It can organize the issue, screenshots, and session details, but it will not call an external AI service or run commands."
        };

        if (includeDiagnostics)
        {
            response.Add($"Current context: {context.WorkflowMode} workflow, {context.CurrentStep}, session status {context.InstallerSessionStatus}.");
        }

        if (imageCount > 0 || fileCount > 0)
        {
            response.Add($"I captured {imageCount} image attachment(s) and {fileCount} supporting file(s). These will be saved with the assistant transcript for the support bundle.");
        }

        if (failedCheck is not null)
        {
            response.Add($"Primary blocker: {failedCheck.Name} ({failedCheck.Code}). Start by reviewing this check summary: {failedCheck.Summary}");
        }
        else if (warningCheck is not null)
        {
            response.Add($"No blocking failure is recorded, but {warningCheck.Name} needs attention before the final deployment evidence is complete.");
        }
        else if (context.Checks.Count == 0)
        {
            response.Add("No preflight checks are attached yet. Load a customer package, sign in, and run preflight before treating this guidance as deployment-specific.");
        }
        else
        {
            response.Add("The available checks do not show a blocker. The next useful step is to continue the workflow and capture evidence if anything fails.");
        }

        if (lowerMessage.Contains("admin", StringComparison.OrdinalIgnoreCase)
            || lowerMessage.Contains("permission", StringComparison.OrdinalIgnoreCase)
            || lowerMessage.Contains("consent", StringComparison.OrdinalIgnoreCase))
        {
            response.Add("For permission issues, prepare an admin-facing message that names the tenant, the required role, the action being requested, and whether the action is retry-safe.");
        }
        else if (lowerMessage.Contains("screenshot", StringComparison.OrdinalIgnoreCase)
                 || lowerMessage.Contains("image", StringComparison.OrdinalIgnoreCase)
                 || imageCount > 0)
        {
            response.Add("For screenshots, make sure the captured image includes the full error text, current step, and any visible tenant or subscription selector. Secrets should be cropped or redacted before sharing outside the support bundle.");
        }
        else
        {
            response.Add("Recommended next action: keep the transcript with the session, then create a support bundle if this needs escalation to PageMaker365 support.");
        }

        return Task.FromResult(new AssistantMessage
        {
            Role = "Assistant",
            Content = string.Join(Environment.NewLine + Environment.NewLine, response),
            CreatedAt = DateTimeOffset.UtcNow
        });
    }
}
