using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PageMaker365.Installer.Engine.Services;

/// <summary>
/// Validates only the transport envelope for a runtime bootstrap. The decoded
/// payload is intentionally never parsed by the installer: workspace and
/// SharePoint semantics remain owned by the runtime contract owner.
///
/// This fixture adapter is default-disabled until the cross-repository
/// canonical schema and fixture set are accepted. It is not a production
/// runtime-bootstrap endpoint or signing implementation.
/// </summary>
public sealed class FixtureRuntimeBootstrapEnvelopeValidator
{
    public const string ContractVersion = "pagemaker365.runtime-bootstrap.v1";
    private const int MaximumPayloadBytes = 524_288;

    private static readonly string[] RequiredFields =
    [
        "contractVersion",
        "packagePayloadSha256",
        "payloadSha256",
        "customerId",
        "tenantId",
        "installationId",
        "environmentId",
        "deploymentExportId",
        "runtimeReleaseId",
        "idempotencyKey",
        "payloadBase64"
    ];

    private static readonly Regex Digest = new("^sha256:[0-9a-f]{64}$", RegexOptions.CultureInvariant);
    private static readonly Regex Token = new("^[A-Za-z0-9._:-]{1,220}$", RegexOptions.CultureInvariant);
    private static readonly Regex Base64 = new("^[A-Za-z0-9+/]*={0,2}$", RegexOptions.CultureInvariant);

    public RuntimeBootstrapEnvelopeValidationResult ValidateJson(
        string envelopeJson,
        RuntimeBootstrapEnvelopeBinding binding)
    {
        if (string.IsNullOrWhiteSpace(envelopeJson))
        {
            throw new InvalidDataException("Runtime bootstrap envelope is required.");
        }

        ArgumentNullException.ThrowIfNull(binding);
        ValidateBinding(binding);

        var utf8 = new UTF8Encoding(false, true).GetBytes(envelopeJson);
        using var document = JsonDocument.Parse(utf8, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 4
        });

        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Runtime bootstrap envelope must be a JSON object.");
        }

        RequireExactFields(root);
        RequireExactString(root, "contractVersion", ContractVersion);

        var packagePayloadSha256 = RequireDigest(root, "packagePayloadSha256");
        var payloadSha256 = RequireDigest(root, "payloadSha256");
        var customerId = RequireToken(root, "customerId");
        var tenantId = RequireToken(root, "tenantId");
        var installationId = RequireToken(root, "installationId");
        var environmentId = RequireToken(root, "environmentId");
        var deploymentExportId = RequireToken(root, "deploymentExportId");
        var runtimeReleaseId = RequireToken(root, "runtimeReleaseId");
        var idempotencyKey = RequireToken(root, "idempotencyKey");

        RequireDigestBinding(packagePayloadSha256, binding.PackagePayloadSha256, "signed package payload");
        RequireExactBinding(customerId, binding.CustomerId, "customer");
        RequireExactBinding(tenantId, binding.TenantId, "tenant");
        RequireExactBinding(installationId, binding.InstallationId, "installation");
        RequireExactBinding(environmentId, binding.EnvironmentId, "environment");
        RequireExactBinding(deploymentExportId, binding.DeploymentExportId, "deployment export");
        RequireExactBinding(runtimeReleaseId, binding.RuntimeReleaseId, "runtime release");

        var payloadBase64 = RequireString(root, "payloadBase64");
        if (!Base64.IsMatch(payloadBase64))
        {
            throw new InvalidDataException("Runtime bootstrap payload must be standard base64 without whitespace.");
        }

        byte[] payload;
        try
        {
            payload = Convert.FromBase64String(payloadBase64);
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException("Runtime bootstrap payload is not valid base64.", exception);
        }

        try
        {
            if (payload.Length is < 1 or > MaximumPayloadBytes)
            {
                throw new InvalidDataException("Runtime bootstrap payload has an unsupported size.");
            }

            var computedPayloadSha256 = "sha256:" + Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.ASCII.GetBytes(payloadSha256),
                    Encoding.ASCII.GetBytes(computedPayloadSha256)))
            {
                throw new InvalidDataException("Runtime bootstrap payloadSha256 does not match the opaque payload bytes.");
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }

        return new RuntimeBootstrapEnvelopeValidationResult
        {
            ContractVersion = ContractVersion,
            PackagePayloadSha256 = packagePayloadSha256,
            PayloadSha256 = payloadSha256,
            CustomerId = customerId,
            TenantId = tenantId,
            InstallationId = installationId,
            EnvironmentId = environmentId,
            DeploymentExportId = deploymentExportId,
            RuntimeReleaseId = runtimeReleaseId,
            IdempotencyKey = idempotencyKey
        };
    }

    private static void RequireExactFields(JsonElement root)
    {
        var properties = root.EnumerateObject().Select(property => property.Name).ToArray();
        if (properties.Length != RequiredFields.Length ||
            properties.Distinct(StringComparer.Ordinal).Count() != RequiredFields.Length ||
            properties.Any(property => !RequiredFields.Contains(property, StringComparer.Ordinal)))
        {
            throw new InvalidDataException("Runtime bootstrap envelope contains missing, duplicate, or unsupported fields.");
        }
    }

    private static void ValidateBinding(RuntimeBootstrapEnvelopeBinding binding)
    {
        foreach (var value in new[]
                 {
                     binding.PackagePayloadSha256,
                     binding.CustomerId,
                     binding.TenantId,
                     binding.InstallationId,
                     binding.EnvironmentId,
                     binding.DeploymentExportId,
                     binding.RuntimeReleaseId
                 })
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidDataException("Runtime bootstrap binding is incomplete.");
            }
        }
    }

    private static string RequireString(JsonElement parent, string property)
    {
        if (!parent.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new InvalidDataException($"Runtime bootstrap {property} is required.");
        }

        return value.GetString()!;
    }

    private static void RequireExactString(JsonElement parent, string property, string expected)
    {
        if (!RequireString(parent, property).Equals(expected, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Runtime bootstrap {property} is unsupported.");
        }
    }

    private static string RequireDigest(JsonElement parent, string property)
    {
        var value = RequireString(parent, property);
        if (!Digest.IsMatch(value))
        {
            throw new InvalidDataException($"Runtime bootstrap {property} must be a lowercase sha256 digest.");
        }

        return value;
    }

    private static string RequireToken(JsonElement parent, string property)
    {
        var value = RequireString(parent, property);
        if (!Token.IsMatch(value))
        {
            throw new InvalidDataException($"Runtime bootstrap {property} contains unsupported characters.");
        }

        return value;
    }

    private static void RequireDigestBinding(string supplied, string expected, string label)
    {
        var normalizedExpected = expected.StartsWith("sha256:", StringComparison.Ordinal)
            ? expected
            : "sha256:" + expected;
        if (!supplied.Equals(normalizedExpected, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Runtime bootstrap {label} binding does not match the verified installer state.");
        }
    }

    private static void RequireExactBinding(string supplied, string expected, string label)
    {
        if (!supplied.Equals(expected, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Runtime bootstrap {label} binding does not match the verified installer state.");
        }
    }
}

public sealed class RuntimeBootstrapEnvelopeBinding
{
    public string PackagePayloadSha256 { get; init; } = "";
    public string CustomerId { get; init; } = "";
    public string TenantId { get; init; } = "";
    public string InstallationId { get; init; } = "";
    public string EnvironmentId { get; init; } = "";
    public string DeploymentExportId { get; init; } = "";
    public string RuntimeReleaseId { get; init; } = "";
}

public sealed class RuntimeBootstrapEnvelopeValidationResult
{
    public string ContractVersion { get; init; } = "";
    public string PackagePayloadSha256 { get; init; } = "";
    public string PayloadSha256 { get; init; } = "";
    public string CustomerId { get; init; } = "";
    public string TenantId { get; init; } = "";
    public string InstallationId { get; init; } = "";
    public string EnvironmentId { get; init; } = "";
    public string DeploymentExportId { get; init; } = "";
    public string RuntimeReleaseId { get; init; } = "";
    public string IdempotencyKey { get; init; } = "";
}
