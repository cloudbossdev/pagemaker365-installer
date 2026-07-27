using System.Security.Cryptography;
using PageMaker365.Installer.Engine.Models;

namespace PageMaker365.Installer.Engine.Services;

public sealed class DeploymentExecutionBindingService
{
    public DeploymentExecutionBindingResult Validate(DeploymentExecutionBindingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = new DeploymentExecutionBindingResult();

        result.PackageHash = HashPackage(request.PackagePath, result);
        result.PreviewEvidenceHash = ComputeFileHash(
            request.PreviewEvidencePath,
            "Deployment preview evidence",
            result);
        result.PreviewArtifactHash = ComputeFileHash(
            request.PreviewArtifactPath,
            "Azure What-If artifact",
            result);

        RequireExpectedHash(request.ExpectedPackageHash, "validated package", result);
        RequireExpectedHash(request.ExpectedPreviewEvidenceHash, "deployment preview evidence", result);
        RequireExpectedHash(request.ExpectedPreviewArtifactHash, "Azure What-If artifact", result);
        CompareHash(request.ExpectedPackageHash, result.PackageHash, "customer package", result);
        CompareHash(request.ExpectedPreviewEvidenceHash, result.PreviewEvidenceHash, "deployment preview evidence", result);
        CompareHash(request.ExpectedPreviewArtifactHash, result.PreviewArtifactHash, "Azure What-If artifact", result);
        return result;
    }

    public static string HashFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return "";
        }

        using var stream = File.OpenRead(path);
        return "sha256:" + Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    public static string HashPackage(string path)
    {
        var result = new DeploymentExecutionBindingResult();
        return HashPackage(path, result);
    }

    private static string HashPackage(string path, DeploymentExecutionBindingResult result)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            result.Errors.Add("The validated customer package file is missing.");
            return "";
        }

        try
        {
            return CustomerConfigService.ComputePackageHash(File.ReadAllText(path));
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or System.Text.Json.JsonException)
        {
            result.Errors.Add($"The customer package could not be revalidated: {exception.Message}");
            return "";
        }
    }

    private static string ComputeFileHash(
        string path,
        string label,
        DeploymentExecutionBindingResult result)
    {
        var hash = HashFile(path);
        if (string.IsNullOrWhiteSpace(hash))
        {
            result.Errors.Add($"{label} is missing and must be regenerated before deployment.");
        }

        return hash;
    }

    private static void RequireExpectedHash(
        string expected,
        string label,
        DeploymentExecutionBindingResult result)
    {
        if (string.IsNullOrWhiteSpace(expected))
        {
            result.Errors.Add($"The saved {label} identity is missing. Rerun deployment preview.");
        }
    }

    private static void CompareHash(
        string expected,
        string actual,
        string label,
        DeploymentExecutionBindingResult result)
    {
        if (!string.IsNullOrWhiteSpace(expected) &&
            !string.IsNullOrWhiteSpace(actual) &&
            !expected.Equals(actual, StringComparison.OrdinalIgnoreCase))
        {
            result.Errors.Add($"The {label} changed after validation. Reload the package and rerun deployment preview.");
        }
    }
}
