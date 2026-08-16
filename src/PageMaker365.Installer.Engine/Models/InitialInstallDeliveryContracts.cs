using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace PageMaker365.Installer.Engine.Models;

/// <summary>
/// The narrow, Sandbox-only handoff issued after portal approval. This is not a
/// customer-install 0.4 package and it carries no deployment authority.
/// </summary>
public sealed class InitialInstallDeliveryEnvelope
{
    public const string ContractVersionValue = "pagemaker365.initial-install-delivery.v1";

    [JsonPropertyName("contractVersion")]
    public string ContractVersion { get; init; } = ContractVersionValue;
    [JsonPropertyName("deliverySessionId")]
    public string DeliverySessionId { get; init; } = "";
    [JsonPropertyName("correlationId")]
    public string CorrelationId { get; init; } = "";
    public InitialInstallSignedPackage Package { get; init; } = new();
}

public sealed class InitialInstallSignedPackage
{
    public string PayloadJson { get; init; } = "";
    public string PayloadSha256 { get; init; } = "";
    public string SignatureAlgorithm { get; init; } = "";
    public string SigningKeyId { get; init; } = "";
    public string Signature { get; init; } = "";
}

/// <summary>
/// Explicitly supplied installer trust roots. The v1 consumer intentionally
/// has no environment, JWKS, or network key-resolution fallback.
/// </summary>
public sealed class InitialInstallTrustOptions
{
    public IReadOnlyDictionary<string, string> TrustedPublicKeysById { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public string GetTrustedPublicKey(string signingKeyId) =>
        TrustedPublicKeysById.TryGetValue(signingKeyId, out var publicKey) ? publicKey : "";
}

public sealed class InitialInstallDeliveryValidationResult
{
    public InitialInstallDeliveryEnvelope Delivery { get; init; } = new();
    public string ArtifactId { get; init; } = "";
    public string CustomerId { get; init; } = "";
    public string EnvironmentKey { get; init; } = "";
    public string AllowedOperation { get; init; } = "";
    public DateTimeOffset IssuedAt { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public byte[] CanonicalPayloadUtf8 { get; init; } = [];
}

public sealed class InitialInstallValidationReceipt
{
    public const string ContractVersionValue = "pagemaker365.initial-install-receipt.v1";

    [JsonPropertyName("contractVersion")]
    public string ContractVersion { get; init; } = ContractVersionValue;
    [JsonPropertyName("deliverySessionId")]
    public string DeliverySessionId { get; init; } = "";
    [JsonPropertyName("artifactId")]
    public string ArtifactId { get; init; } = "";
    [JsonPropertyName("payloadSha256")]
    public string PayloadSha256 { get; init; } = "";
    [JsonPropertyName("eventId")]
    public string EventId { get; init; } = "";
    [JsonPropertyName("idempotencyKey")]
    public string IdempotencyKey { get; init; } = "";
    [JsonPropertyName("eventType")]
    public string EventType { get; init; } = "";
    [JsonPropertyName("outcome")]
    public string Outcome { get; init; } = "";
    [JsonPropertyName("occurredAt")]
    public DateTimeOffset OccurredAt { get; init; }
    [JsonPropertyName("installerVersion")]
    public string InstallerVersion { get; init; } = "";
    [JsonPropertyName("safeError")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public InitialInstallSafeError? SafeError { get; init; }
}

public sealed class InitialInstallSafeError
{
    [JsonPropertyName("code")]
    public string Code { get; init; } = "";
    [JsonPropertyName("message")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Message { get; init; }
}

public interface IInitialInstallReceiptClient
{
    Task SubmitAsync(InitialInstallValidationReceipt receipt, CancellationToken cancellationToken = default);
}

/// <summary>
/// Local-only receipt seam used by contract tests and the future portal client.
/// It performs no HTTP, artifact download, deployment, or legacy evidence work.
/// </summary>
public sealed class LocalInitialInstallReceiptClient : IInitialInstallReceiptClient
{
    private readonly List<InitialInstallValidationReceipt> _receipts = [];

    public IReadOnlyList<InitialInstallValidationReceipt> Receipts => _receipts;

    public Task SubmitAsync(InitialInstallValidationReceipt receipt, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        InitialInstallValidationReceiptFactory.Validate(receipt);
        _receipts.Add(receipt);
        return Task.CompletedTask;
    }
}

public static class InitialInstallValidationReceiptFactory
{
    public static InitialInstallValidationReceipt CreateValidated(
        InitialInstallDeliveryValidationResult validation,
        string installerVersion,
        DateTimeOffset? occurredAt = null) =>
        Create(validation, installerVersion, "package_validated", "passed", null, occurredAt);

    public static InitialInstallValidationReceipt CreateValidationFailure(
        InitialInstallDeliveryEnvelope delivery,
        string artifactId,
        string payloadSha256,
        string installerVersion,
        InitialInstallSafeError safeError,
        DateTimeOffset? occurredAt = null) =>
        Create(
            delivery,
            artifactId,
            payloadSha256,
            installerVersion,
            "package_validation_failed",
            "failed",
            safeError,
            occurredAt);

    public static void Validate(InitialInstallValidationReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        if (!receipt.ContractVersion.Equals(InitialInstallValidationReceipt.ContractVersionValue, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Initial-install receipt contractVersion is invalid.");
        }
        RequireToken(receipt.DeliverySessionId, "deliverySessionId");
        RequireUuid(receipt.ArtifactId, "artifactId");
        RequireDigest(receipt.PayloadSha256, "payloadSha256");
        RequireToken(receipt.EventId, "eventId");
        RequireToken(receipt.IdempotencyKey, "idempotencyKey");
        RequireToken(receipt.InstallerVersion, "installerVersion");
        if (receipt.EventType is not ("package_validated" or "package_validation_failed"))
        {
            throw new InvalidDataException("Initial-install receipt eventType is invalid.");
        }

        if (receipt.Outcome is not ("passed" or "failed" or "blocked"))
        {
            throw new InvalidDataException("Initial-install receipt outcome is invalid.");
        }

        if (receipt.EventType == "package_validated" && receipt.Outcome != "passed")
        {
            throw new InvalidDataException("A validated package receipt must have a passed outcome.");
        }

        if (receipt.EventType == "package_validation_failed" && receipt.Outcome == "passed")
        {
            throw new InvalidDataException("A failed package validation receipt cannot have a passed outcome.");
        }

        if (receipt.OccurredAt == default || receipt.OccurredAt.Offset != TimeSpan.Zero)
        {
            throw new InvalidDataException("Initial-install receipt occurredAt must be a UTC timestamp.");
        }

        if (receipt.SafeError is not null)
        {
            RequireSafeError(receipt.SafeError);
        }
        else if (receipt.EventType == "package_validation_failed")
        {
            throw new InvalidDataException("A failed package validation receipt requires a safeError.");
        }
    }

    private static InitialInstallValidationReceipt Create(
        InitialInstallDeliveryValidationResult validation,
        string installerVersion,
        string eventType,
        string outcome,
        InitialInstallSafeError? safeError,
        DateTimeOffset? occurredAt) =>
        Create(
            validation.Delivery,
            validation.ArtifactId,
            validation.Delivery.Package.PayloadSha256,
            installerVersion,
            eventType,
            outcome,
            safeError,
            occurredAt);

    private static InitialInstallValidationReceipt Create(
        InitialInstallDeliveryEnvelope delivery,
        string artifactId,
        string payloadSha256,
        string installerVersion,
        string eventType,
        string outcome,
        InitialInstallSafeError? safeError,
        DateTimeOffset? occurredAt)
    {
        var receipt = new InitialInstallValidationReceipt
        {
            DeliverySessionId = delivery.DeliverySessionId,
            ArtifactId = artifactId,
            PayloadSha256 = payloadSha256,
            EventId = Guid.NewGuid().ToString("D"),
            IdempotencyKey = $"initial-install-validation:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{delivery.DeliverySessionId}:{artifactId}:{eventType}"))).ToLowerInvariant()}",
            EventType = eventType,
            Outcome = outcome,
            OccurredAt = (occurredAt ?? DateTimeOffset.UtcNow).ToUniversalTime(),
            InstallerVersion = installerVersion,
            SafeError = safeError
        };
        Validate(receipt);
        return receipt;
    }

    private static void RequireToken(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 220 || value != value.Trim() ||
            value.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '.' and not '_' and not ':' and not '-'))
        {
            throw new InvalidDataException($"Initial-install receipt {field} is invalid.");
        }
    }

    private static void RequireUuid(string value, string field)
    {
        if (!Guid.TryParseExact(value, "D", out var parsed) || parsed == Guid.Empty || !value.Equals(parsed.ToString("D"), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Initial-install receipt {field} is invalid.");
        }
    }

    private static void RequireDigest(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value) || !System.Text.RegularExpressions.Regex.IsMatch(value, "^[0-9a-f]{64}$"))
        {
            throw new InvalidDataException($"Initial-install receipt {field} is invalid.");
        }
    }

    private static void RequireSafeError(InitialInstallSafeError value)
    {
        if (string.IsNullOrWhiteSpace(value.Code) ||
            !System.Text.RegularExpressions.Regex.IsMatch(value.Code, "^[a-z0-9_]{1,64}$") ||
            (value.Message is not null &&
             (value.Message.Length > 240 || value.Message != value.Message.Trim() ||
              System.Text.RegularExpressions.Regex.IsMatch(value.Message, "(?:secret|token|password|private.?key|connection.?string|authorization|credential|endpoint|tenant|resource|raw.?body|stack|file.?path|https?://|[A-Za-z]:[\\\\/]|(?:^|\\s)/[^\\s]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase) ||
              value.Message.Any(character => char.IsControl(character) || character is '\u2028' or '\u2029'))))
        {
            throw new InvalidDataException("Initial-install receipt safeError is invalid.");
        }
    }
}
