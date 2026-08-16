using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using PageMaker365.Installer.Engine.Models;
using PageMaker365.Installer.Engine.Services;

namespace PageMaker365.Installer.Engine.Tests;

internal static class InitialInstallDeliveryContractTests
{
    private const string NormativeFixtureKeyId = "fixture-ed25519-key";
    private const string NormativeFixturePayloadSha256 = "fa88f61089937cd0d8cc255acf731566f2e4ffb27551294b8a4be61e1dc0ed04";
    private const string NormativeFixtureSignature = "TITSeul8356Eq3wEjb5zsemv9bzK6gjCebZ9wpSddTPj03bWmTXpC1ZJpThNdc5WGaPlzcKvTeSXHzk6dqHSDA";
    private static readonly DateTimeOffset FixtureNow = new(2026, 12, 31, 0, 1, 0, TimeSpan.Zero);

    public static Task ValidatesFixtureAndMatchesNodeCanonicalBytes()
    {
        var fixture = LoadFixture();
        var result = new InitialInstallDeliveryService().ValidateJson(fixture.DeliveryJson, fixture.TrustOptions, FixtureNow);

        AssertEx.Equal("11111111-1111-4111-8111-111111111111", result.ArtifactId);
        AssertEx.Equal("55555555-5555-4555-8555-555555555555", result.CustomerId);
        AssertEx.Equal("sandbox", result.EnvironmentKey);
        AssertEx.Equal("initial_sandbox_install", result.AllowedOperation);
        AssertEx.Equal(fixture.ExpectedPayloadSha256, result.Delivery.Package.PayloadSha256);
        AssertEx.Equal(NormativeFixturePayloadSha256, result.Delivery.Package.PayloadSha256);
        AssertEx.Equal(NormativeFixtureKeyId, result.Delivery.Package.SigningKeyId);
        AssertEx.Equal(NormativeFixtureSignature, result.Delivery.Package.Signature);
        AssertEx.Equal(fixture.ExpectedCanonicalPayload, Encoding.UTF8.GetString(result.CanonicalPayloadUtf8));
        AssertEx.Equal(InitialInstallDeliveryEnvelope.ContractVersionValue, result.Delivery.ContractVersion);
        return Task.CompletedTask;
    }

    public static Task RejectsUnknownTrustedKey()
    {
        var fixture = LoadFixture();
        var exception = AssertEx.Throws<InvalidDataException>(() =>
            new InitialInstallDeliveryService().ValidateJson(fixture.DeliveryJson, new InitialInstallTrustOptions(), FixtureNow));
        AssertEx.StringContains(exception.Message, "trust map");
        return Task.CompletedTask;
    }

    public static Task RejectsPayloadHashAndSignatureTampering()
    {
        var fixture = LoadFixture();
        AssertEx.Throws<InvalidDataException>(() =>
            new InitialInstallDeliveryService().ValidateJson(
                fixture.DeliveryJson.Replace("safe-subscription", "tampered-subscription", StringComparison.Ordinal),
                fixture.TrustOptions,
                FixtureNow));
        AssertEx.Throws<InvalidDataException>(() =>
            new InitialInstallDeliveryService().ValidateJson(
                fixture.DeliveryJson.Replace("\"signature\":\"TIT", "\"signature\":\"UIT", StringComparison.Ordinal),
                fixture.TrustOptions,
                FixtureNow));
        return Task.CompletedTask;
    }

    public static Task RejectsDuplicateKeysAndUnexpectedPayloadAuthority()
    {
        var fixture = LoadFixture();
        var duplicateRoot = fixture.DeliveryJson.Replace(
            "\"contractVersion\":\"pagemaker365.initial-install-delivery.v1\",",
            "\"contractVersion\":\"pagemaker365.initial-install-delivery.v1\",\"contractVersion\":\"pagemaker365.initial-install-delivery.v1\",",
            StringComparison.Ordinal);
        AssertEx.Throws<InvalidDataException>(() =>
            new InitialInstallDeliveryService().ValidateJson(duplicateRoot, fixture.TrustOptions, FixtureNow));

        var payloadWithWorkspace = fixture.DeliveryJson.Replace(
            "\"technicalFacts\":{",
            "\"workspace\":{},\"technicalFacts\":{",
            StringComparison.Ordinal);
        AssertEx.Throws<InvalidDataException>(() =>
            new InitialInstallDeliveryService().ValidateJson(payloadWithWorkspace, fixture.TrustOptions, FixtureNow));
        return Task.CompletedTask;
    }

    public static Task RejectsNonSandboxExpiredAndNonCanonicalBindings()
    {
        var fixture = LoadFixture();
        AssertEx.Throws<InvalidDataException>(() =>
            new InitialInstallDeliveryService().ValidateJson(
                fixture.DeliveryJson.Replace("\"environmentKey\":\"sandbox\"", "\"environmentKey\":\"production\"", StringComparison.Ordinal),
                fixture.TrustOptions,
                FixtureNow));
        AssertEx.Throws<InvalidDataException>(() =>
            new InitialInstallDeliveryService().ValidateJson(
                fixture.DeliveryJson.Replace("2027-01-01T00:00:00.000Z", "2025-01-01T00:00:00.000Z", StringComparison.Ordinal),
                fixture.TrustOptions,
                FixtureNow));
        AssertEx.Throws<InvalidDataException>(() =>
            new InitialInstallDeliveryService().ValidateJson(
                fixture.DeliveryJson.Replace("\"version\":1", "\"version\":1e0", StringComparison.Ordinal),
                fixture.TrustOptions,
                FixtureNow));
        return Task.CompletedTask;
    }

    public static async Task LocalReceiptSeamUsesOnlyV1FieldsAndRejectsUnsafeError()
    {
        var fixture = LoadFixture();
        var validation = new InitialInstallDeliveryService().ValidateJson(fixture.DeliveryJson, fixture.TrustOptions, FixtureNow);
        var receipt = InitialInstallValidationReceiptFactory.CreateValidated(validation, "1.2.3", FixtureNow);
        var client = new LocalInitialInstallReceiptClient();
        await client.SubmitAsync(receipt);
        AssertEx.Equal(1, client.Receipts.Count);
        AssertEx.Equal(InitialInstallValidationReceipt.ContractVersionValue, receipt.ContractVersion);
        AssertEx.Equal("package_validated", receipt.EventType);
        AssertEx.Equal("passed", receipt.Outcome);
        var receiptJson = JsonSerializer.Serialize(receipt);
        AssertEx.False(receiptJson.Contains("deploymentExportId", StringComparison.OrdinalIgnoreCase));
        AssertEx.False(receiptJson.Contains("0.4", StringComparison.Ordinal));
        using (var serializedReceipt = JsonDocument.Parse(receiptJson))
        {
            var actualFields = serializedReceipt.RootElement.EnumerateObject().Select(property => property.Name).OrderBy(name => name).ToArray();
            var expectedFields = new[] { "artifactId", "contractVersion", "deliverySessionId", "eventId", "eventType", "idempotencyKey", "installerVersion", "occurredAt", "outcome", "payloadSha256" };
            AssertEx.True(actualFields.SequenceEqual(expectedFields, StringComparer.Ordinal), "Validated receipt must contain only v1 receipt fields.");
        }

        var unsafeFailure = new InitialInstallValidationReceipt
        {
            DeliverySessionId = validation.Delivery.DeliverySessionId,
            ArtifactId = validation.ArtifactId,
            PayloadSha256 = validation.Delivery.Package.PayloadSha256,
            EventId = "00000000-0000-4000-8000-000000000001",
            IdempotencyKey = "initial-install-validation:unsafe",
            EventType = "package_validation_failed",
            Outcome = "failed",
            OccurredAt = FixtureNow,
            InstallerVersion = "1.2.3",
            SafeError = new InitialInstallSafeError { Code = "validation_failed", Message = "Package token leaked" }
        };
        await AssertEx.ThrowsAsync<InvalidDataException>(() => client.SubmitAsync(unsafeFailure));

        var missingMessage = new InitialInstallValidationReceipt
        {
            DeliverySessionId = validation.Delivery.DeliverySessionId,
            ArtifactId = validation.ArtifactId,
            PayloadSha256 = validation.Delivery.Package.PayloadSha256,
            EventId = "00000000-0000-4000-8000-000000000002",
            IdempotencyKey = "initial-install-validation:missing-message",
            EventType = "package_validation_failed",
            Outcome = "blocked",
            OccurredAt = FixtureNow,
            InstallerVersion = "1.2.3",
            SafeError = new InitialInstallSafeError { Code = "validation_blocked" }
        };
        await AssertEx.ThrowsAsync<InvalidDataException>(() => client.SubmitAsync(missingMessage));

        var successfulReceiptWithError = new InitialInstallValidationReceipt
        {
            DeliverySessionId = validation.Delivery.DeliverySessionId,
            ArtifactId = validation.ArtifactId,
            PayloadSha256 = validation.Delivery.Package.PayloadSha256,
            EventId = "00000000-0000-4000-8000-000000000003",
            IdempotencyKey = "initial-install-validation:unexpected-error",
            EventType = "package_validated",
            Outcome = "passed",
            OccurredAt = FixtureNow,
            InstallerVersion = "1.2.3",
            SafeError = new InitialInstallSafeError { Code = "validation_failed", Message = "Validation failed." }
        };
        await AssertEx.ThrowsAsync<InvalidDataException>(() => client.SubmitAsync(successfulReceiptWithError));
    }

    private static Fixture LoadFixture()
    {
        var root = FindRepositoryRoot();
        var fixtureDirectory = Path.Combine(root, "tests", "PageMaker365.Installer.Engine.Tests", "Fixtures", "initial-sandbox-install-delivery");
        var deliveryPath = Path.Combine(fixtureDirectory, "positive-envelope.json");
        var canonicalPayloadPath = Path.Combine(fixtureDirectory, "canonical-payload.json");
        var publicKeyPath = Path.Combine(fixtureDirectory, "public-key.pem");
        var manifestPath = Path.Combine(fixtureDirectory, "manifest.json");
        var deliveryJson = File.ReadAllText(deliveryPath);
        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var manifestRoot = manifest.RootElement;
        foreach (var expectedFile in manifestRoot.GetProperty("files").EnumerateObject())
        {
            var actualPath = Path.Combine(fixtureDirectory, expectedFile.Name);
            var actualHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(actualPath))).ToLowerInvariant();
            AssertEx.Equal(expectedFile.Value.GetString(), actualHash, $"Portal fixture byte lock failed for {expectedFile.Name}.");
        }
        using var envelope = JsonDocument.Parse(deliveryJson);
        var package = envelope.RootElement.GetProperty("package");
        AssertEx.Equal(NormativeFixtureKeyId, package.GetProperty("signingKeyId").GetString());
        AssertEx.Equal(NormativeFixturePayloadSha256, package.GetProperty("payloadSha256").GetString());
        AssertEx.Equal(NormativeFixtureSignature, package.GetProperty("signature").GetString());
        return new Fixture(
            deliveryJson,
            new InitialInstallTrustOptions { TrustedPublicKeysById = new Dictionary<string, string>(StringComparer.Ordinal) { [NormativeFixtureKeyId] = File.ReadAllText(publicKeyPath) } },
            NormativeFixturePayloadSha256,
            File.ReadAllText(canonicalPayloadPath).TrimEnd('\n'));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PageMaker365.Installer.sln"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate repository root from the test output directory.");
    }

    private sealed record Fixture(
        string DeliveryJson,
        InitialInstallTrustOptions TrustOptions,
        string ExpectedPayloadSha256,
        string ExpectedCanonicalPayload);
}
