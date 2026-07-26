using PageMaker365.Installer.Engine.Models;

namespace PageMaker365.Installer.Engine.Services;

public interface IGraphDeviceCodeAuthenticator
{
    Task<GraphSignInResult> SignInAsync(
        string tenantId,
        string clientId,
        IProgress<GraphDeviceCodePrompt>? promptProgress = null,
        CancellationToken cancellationToken = default);
}
