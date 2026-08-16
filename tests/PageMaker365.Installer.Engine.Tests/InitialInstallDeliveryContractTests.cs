using System.Text;
using System.Text.Json;
using PageMaker365.Installer.Engine.Models;
using PageMaker365.Installer.Engine.Services;

namespace PageMaker365.Installer.Engine.Tests;

internal static class InitialInstallDeliveryContractTests
{
    private static readonly DateTimeOffset FixtureNow = new(2040, 8, 16, 0, 5, 0, TimeSpan.Zero);

    public static Task ValidatesFixtureAndMatchesNodeCanonicalBytes()
    {
        var fixture = LoadFixture();
        var result = new InitialInstallDeliveryService().ValidateJson(fixture.DeliveryJson, fixture.TrustOptions, FixtureNow);

        AssertEx.Equal("11111111-1111-4111-8111-111111111111", result.ArtifactId);
        AssertEx.Equal("22222222-2222-4222-8222-222222222222", result.CustomerId);
        AssertEx.Equal("sandbox", result.EnvironmentKey);
        AssertEx.Equal("initial_sandbox_install", result.AllowedOperation);
        AssertEx.Equal(fixture.ExpectedPayloadSha256, result.Delivery.Package.PayloadSha256);
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
                fixture.DeliveryJson.Replace("ttAA\"", "ttAB\"", StringComparison.Ordinal),
                fixture.TrustOptions,
                FixtureNow));
        return Task.CompletedTask;
    }

    public static Task RejectsDuplicateKeysAndUnexpectedPayloadAuthority()
    {
        var fixture = LoadFixture();
        var duplicateRoot = fixture.DeliveryJson.Replace(
            "\"contractVersion\": \"pagemaker365.initial-install-delivery.v1\",",
            "\"contractVersion\": \"pagemaker365.initial-install-delivery.v1\", \"contractVersion\": \"pagemaker365.initial-install-delivery.v1\",",
            StringComparison.Ordinal);
        AssertEx.Throws<InvalidDataException>(() =>
            new InitialInstallDeliveryService().ValidateJson(duplicateRoot, fixture.TrustOptions, FixtureNow));

        var payloadWithWorkspace = fixture.DeliveryJson.Replace(
            "\"technicalFacts\": {",
            "\"workspace\": {}, \"technicalFacts\": {",
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
                fixture.DeliveryJson.Replace("\"environmentKey\": \"sandbox\"", "\"environmentKey\": \"production\"", StringComparison.Ordinal),
                fixture.TrustOptions,
                FixtureNow));
        AssertEx.Throws<InvalidDataException>(() =>
            new InitialInstallDeliveryService().ValidateJson(
                fixture.DeliveryJson.Replace("2040-08-16T01:00:00.000Z", "2039-08-16T01:00:00.000Z", StringComparison.Ordinal),
                fixture.TrustOptions,
                FixtureNow));
        AssertEx.Throws<InvalidDataException>(() =>
            new InitialInstallDeliveryService().ValidateJson(
                fixture.DeliveryJson.Replace("\"version\": 1", "\"version\": 1e0", StringComparison.Ordinal),
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
        var fixtureDirectory = Path.Combine(root, "tests", "PageMaker365.Installer.Engine.Tests", "Fixtures");
        var deliveryJson = File.ReadAllText(Path.Combine(fixtureDirectory, "initial-install-delivery-v1.json"));
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixtureDirectory, "initial-install-delivery-v1.manifest.json")));
        var manifestRoot = manifest.RootElement;
        var trusted = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in manifestRoot.GetProperty("trustedPublicKeys").EnumerateObject())
        {
            trusted.Add(key.Name, key.Value.GetString() ?? "");
        }
        return new Fixture(
            deliveryJson,
            new InitialInstallTrustOptions { TrustedPublicKeysById = trusted },
            manifestRoot.GetProperty("expectedPayloadSha256").GetString() ?? "",
            manifestRoot.GetProperty("expectedCanonicalPayload").GetString() ?? "");
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
