using System.Text.Json;
using PageMaker365.Installer.Engine.Models;
using PageMaker365.Installer.Engine.PowerShell;
using PageMaker365.Installer.Engine.Steps;

namespace PageMaker365.Installer.Engine.Services;

public sealed class InstallerEngine
{
    private readonly StructuredLogger _logger;
    private readonly PowerShellProcessRunner _powerShellRunner;

    public InstallerEngine(StructuredLogger logger)
    {
        _logger = logger;
        _powerShellRunner = new PowerShellProcessRunner();
    }

    public InstallerSession CreateSession(CustomerInstallConfig config, string workspaceRoot)
    {
        var session = InstallerSession.Create(config, workspaceRoot);
        Directory.CreateDirectory(session.LogDirectory);
        return session;
    }

    public async Task<IReadOnlyList<InstallerStepResult>> RunPreflightAsync(
        InstallerSession session,
        IProgress<InstallerStepResult>? progress = null,
        CancellationToken cancellationToken = default)
    {
        session.CurrentPhase = "Preflight & Permissions";
        session.Status = InstallStatus.Running;
        await _logger.WriteAsync(session, "phase.started", new { session.CurrentPhase }, cancellationToken);

        var steps = CreateMockPreflightSteps();
        foreach (var step in steps)
        {
            await _logger.WriteAsync(session, "step.started", new { step.Name, step.Code }, cancellationToken);
            var result = await step.RunAsync(session, cancellationToken);
            session.Results.Add(result);
            progress?.Report(result);
            await _logger.WriteAsync(session, "step.completed", result, cancellationToken);
            await PersistSessionAsync(session, cancellationToken);
        }

        session.Status = session.Results.Any(result => result.Status == InstallStatus.Failed)
            ? InstallStatus.Failed
            : session.Results.Any(result => result.Status == InstallStatus.Warning)
                ? InstallStatus.Warning
                : InstallStatus.Passed;

        await _logger.WriteAsync(session, "phase.completed", new { session.CurrentPhase, session.Status }, cancellationToken);
        await PersistSessionAsync(session, cancellationToken);
        return session.Results;
    }

    public async Task<IReadOnlyList<InstallerStepResult>> RunPowerShellPreflightAsync(
        InstallerSession session,
        string workspaceRoot,
        string configPath,
        IProgress<InstallerStepResult>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return await RunPowerShellModuleCommandAsync(
            session,
            workspaceRoot,
            configPath,
            "Preflight & Permissions",
            "Start-PM365Preflight",
            progress,
            cancellationToken);
    }

    public async Task<IReadOnlyList<InstallerStepResult>> RunAzureSignInAsync(
        InstallerSession session,
        string workspaceRoot,
        string configPath,
        IProgress<InstallerStepResult>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return await RunPowerShellModuleCommandAsync(
            session,
            workspaceRoot,
            configPath,
            "Azure Sign In",
            "Connect-PM365Azure",
            progress,
            cancellationToken);
    }

    public async Task<IReadOnlyList<InstallerStepResult>> RunGraphSignInAsync(
        InstallerSession session,
        string workspaceRoot,
        string configPath,
        IProgress<InstallerStepResult>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return await RunPowerShellModuleCommandAsync(
            session,
            workspaceRoot,
            configPath,
            "Microsoft Graph Sign In",
            "Connect-PM365Graph",
            progress,
            cancellationToken);
    }

    public async Task<IReadOnlyList<InstallerStepResult>> RunWhatIfAsync(
        InstallerSession session,
        string workspaceRoot,
        string configPath,
        IProgress<InstallerStepResult>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return await RunPowerShellModuleCommandAsync(
            session,
            workspaceRoot,
            configPath,
            "Deployment Preview",
            "Invoke-PM365WhatIf",
            progress,
            cancellationToken);
    }

    private async Task<IReadOnlyList<InstallerStepResult>> RunPowerShellModuleCommandAsync(
        InstallerSession session,
        string workspaceRoot,
        string configPath,
        string phase,
        string commandName,
        IProgress<InstallerStepResult>? progress = null,
        CancellationToken cancellationToken = default)
    {
        session.CurrentPhase = phase;
        session.Status = InstallStatus.Running;
        await _logger.WriteAsync(session, "phase.started", new { session.CurrentPhase, mode = "PowerShell", commandName }, cancellationToken);

        var modulePath = Path.Combine(workspaceRoot, "modules", "PageMaker365.Install", "PageMaker365.Install.psd1");
        if (!File.Exists(modulePath))
        {
            var missing = InstallerStepResult.Failed(
                "Installer Module",
                "InstallerModuleMissing",
                "The PageMaker365.Install PowerShell module was not found.",
                modulePath,
                retrySafe: false);
            await RecordResultAsync(session, missing, progress, cancellationToken);
            await CompletePhaseAsync(session, cancellationToken);
            return session.Results;
        }

        await _logger.WriteAsync(session, "powershell.started", new { modulePath, command = commandName, configPath }, cancellationToken);
        var command = BuildModuleCommand(modulePath, commandName, configPath);
        var execution = await _powerShellRunner.RunAsync(command, workspaceRoot, cancellationToken);
        await _logger.WriteAsync(
            session,
            "powershell.completed",
            new
            {
                execution.ExitCode,
                execution.StandardOutput,
                execution.StandardError
            },
            cancellationToken);

        if (!execution.Succeeded)
        {
            var failed = InstallerStepResult.Failed(
                "PowerShell Prerequisites",
                "PowerShellPreflightFailed",
                "The prerequisite check command failed.",
                string.IsNullOrWhiteSpace(execution.StandardError) ? execution.StandardOutput : execution.StandardError,
                retrySafe: true);
            await RecordResultAsync(session, failed, progress, cancellationToken);
            await CompletePhaseAsync(session, cancellationToken);
            return session.Results;
        }

        foreach (var result in ParsePowerShellResults(execution.StandardOutput))
        {
            await RecordResultAsync(session, result, progress, cancellationToken);
        }

        await CompletePhaseAsync(session, cancellationToken);
        return session.Results;
    }

    public InstallerDiagnosticPayload CreateDiagnosticPayload(InstallerSession session)
    {
        var failed = session.Results.FirstOrDefault(result => result.Status == InstallStatus.Failed);
        return new InstallerDiagnosticPayload
        {
            SessionId = session.SessionId,
            Phase = session.CurrentPhase,
            FailedStep = failed?.StepName ?? "",
            ErrorCode = failed?.Code ?? "",
            RedactedLog = ReadRedactedLog(session),
            Facts =
            {
                ["tenantName"] = session.Config.Customer.TenantName,
                ["environment"] = session.Config.Azure.Environment,
                ["resourceGroup"] = session.Config.Azure.ResourceGroupName,
                ["sharePointSite"] = session.Config.SharePoint.SiteUrl,
                ["hasBlockingFailure"] = (failed is not null).ToString()
            }
        };
    }

    private static IReadOnlyList<IInstallerStep> CreateMockPreflightSteps()
    {
        return
        [
            new MockInstallerStep(
                "PowerShell 7",
                "PowerShellReady",
                InstallerStepResult.Passed("PowerShell 7", "PowerShellReady", "PowerShell 7 is available.", "The installer engine can use PowerShell 7 for deployment commands.")),
            new MockInstallerStep(
                "Azure Subscription",
                "AzureSubscriptionReady",
                InstallerStepResult.Passed("Azure Subscription", "AzureSubscriptionReady", "Azure subscription context is ready.", "Subscription and resource group access will be verified by the real implementation.")),
            new MockInstallerStep(
                "SharePoint Site",
                "SharePointSiteReady",
                InstallerStepResult.Passed("SharePoint Site", "SharePointSiteReady", "SharePoint site URL format is valid.", "The real implementation will resolve site and library IDs through Microsoft Graph.")),
            new MockInstallerStep(
                "Bicep What-If Ready",
                "BicepNotVerified",
                InstallerStepResult.Warning("Bicep What-If Ready", "BicepNotVerified", "Bicep is not verified yet.", "The scaffold is using mocked preflight data until the deployment module is wired.")),
            new MockInstallerStep(
                "Entra Permissions",
                "MissingApplicationAdministrator",
                InstallerStepResult.Failed("Entra Permissions", "MissingApplicationAdministrator", "The signed-in user may not be able to approve app permissions.", "Ask a Global Administrator, Cloud Application Administrator, or Application Administrator to complete consent.", retrySafe: false))
        ];
    }

    private static string BuildPreflightCommand(string modulePath, string configPath)
    {
        return BuildModuleCommand(modulePath, "Start-PM365Preflight", configPath);
    }

    private static string BuildModuleCommand(string modulePath, string commandName, string configPath)
    {
        var escapedPath = modulePath.Replace("'", "''");
        var escapedConfigPath = configPath.Replace("'", "''");
        var script = "$ErrorActionPreference = 'Stop'; " +
                     $"Import-Module '{escapedPath}' -Force; " +
                     $"{commandName} -ConfigPath '{escapedConfigPath}' | ConvertTo-Json -Depth 12";
        return $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"";
    }

    private static IReadOnlyList<InstallerStepResult> ParsePowerShellResults(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return
            [
                InstallerStepResult.Failed(
                    "PowerShell Prerequisites",
                    "PowerShellPreflightNoOutput",
                    "The prerequisite check command returned no output.",
                    retrySafe: true)
            ];
        }

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var trimmed = json.Trim();
        var moduleResults = trimmed.StartsWith("[", StringComparison.Ordinal)
            ? JsonSerializer.Deserialize<List<PowerShellModuleResult>>(trimmed, options) ?? []
            : [JsonSerializer.Deserialize<PowerShellModuleResult>(trimmed, options) ?? new PowerShellModuleResult()];

        return moduleResults.Select(ToInstallerStepResult).ToArray();
    }

    private static InstallerStepResult ToInstallerStepResult(PowerShellModuleResult result)
    {
        var status = Enum.TryParse<InstallStatus>(result.Status, ignoreCase: true, out var parsed)
            ? parsed
            : InstallStatus.Failed;
        var stepName = NameFromCode(result.Code);
        var now = DateTimeOffset.UtcNow;

        return new InstallerStepResult
        {
            StepName = stepName,
            Code = string.IsNullOrWhiteSpace(result.Code) ? "PowerShellResult" : result.Code,
            Status = status,
            Summary = result.Summary,
            Details = result.Details,
            RetrySafe = result.RetrySafe,
            Data = result.Data.ToDictionary(item => item.Key, item => item.Value.ToString()),
            StartedAt = now,
            CompletedAt = now
        };
    }

    private static string NameFromCode(string code)
    {
        return code switch
        {
            "PowerShellReady" => "PowerShell 7",
            "DeploymentContractReadable" or "DeploymentContractReady" or "DeploymentContractIncomplete" => "Deployment Contract",
            "DeploymentPackageSecretSafe" or "DeploymentPackageContainsRawSecrets" or "DeploymentSecretsContractMissing" => "Deployment Package Secrets",
            "AzAccountsReady" or "AzAccountsMissing" => "Az.Accounts Module",
            "BicepReady" or "BicepMissing" => "Bicep",
            "AzureSignInCompleted" or "AzureSignInFailed" => "Azure Sign In",
            "GraphSignInCompleted" or "GraphSignInFailed" => "Microsoft Graph Sign In",
            "AzureTenantReady" or "AzureTenantMismatch" => "Azure Tenant",
            "AzureSubscriptionReady" or "AzureSubscriptionMismatch" or "AzureSubscriptionUnavailable" => "Azure Subscription",
            "AzureResourceGroupReady" or "AzureResourceGroupMissing" => "Azure Resource Group",
            "AzureRbacReady" or "AzureRbacInsufficient" or "AzureRbacNotFound" or "AzureRbacCheckUnavailable" => "Azure RBAC",
            "GraphTenantReady" or "GraphTenantMismatch" => "Microsoft Graph Tenant",
            "GraphConsentScopesReady" or "GraphConsentScopesMissing" => "Microsoft Graph Consent",
            "EntraAdminRoleReady" or "EntraAdminRoleMissing" or "EntraAdminRoleCheckUnavailable" => "Entra Admin Role",
            "SharePointSiteUrlReady" or "SharePointSiteUrlInvalid" => "SharePoint Site URL",
            "SharePointSiteResolved" or "SharePointSiteResolveFailed" => "SharePoint Site",
            "SharePointLibraryReady" or "SharePointLibraryNotFound" or "SharePointLibraryNotConfigured" => "SharePoint Library",
            "AzureWhatIfReady" or "AzureWhatIfFailed" => "Azure What-If",
            _ when string.IsNullOrWhiteSpace(code) => "PowerShell Check",
            _ => code
        };
    }

    private async Task RecordResultAsync(
        InstallerSession session,
        InstallerStepResult result,
        IProgress<InstallerStepResult>? progress,
        CancellationToken cancellationToken)
    {
        session.Results.Add(result);
        progress?.Report(result);
        await _logger.WriteAsync(session, "step.completed", result, cancellationToken);
        await PersistSessionAsync(session, cancellationToken);
    }

    private async Task CompletePhaseAsync(InstallerSession session, CancellationToken cancellationToken)
    {
        session.Status = session.Results.Any(result => result.Status == InstallStatus.Failed)
            ? InstallStatus.Failed
            : session.Results.Any(result => result.Status == InstallStatus.Warning)
                ? InstallStatus.Warning
                : InstallStatus.Passed;

        await _logger.WriteAsync(session, "phase.completed", new { session.CurrentPhase, session.Status }, cancellationToken);
        await PersistSessionAsync(session, cancellationToken);
    }

    private async Task PersistSessionAsync(InstallerSession session, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(session.LogDirectory);
        var path = Path.Combine(session.LogDirectory, "install-session.json");
        var json = JsonSerializer.Serialize(session, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json, cancellationToken);
    }

    private static string ReadRedactedLog(InstallerSession session)
    {
        var path = Path.Combine(session.LogDirectory, "redacted-install.log");
        return File.Exists(path) ? File.ReadAllText(path) : "";
    }
}
