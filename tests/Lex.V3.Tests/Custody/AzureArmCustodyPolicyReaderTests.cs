using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Azure.Core;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Custody.Azure;

namespace Lex.V3.Tests.Custody;

[TestClass]
public sealed class AzureArmCustodyPolicyReaderTests
{
    private const string ConfigurationEtag = "\"0x8DB8A1B2C3D4E5F\"";
    private const string PolicyEtag = "\"0x8DB8A1B2C3D4E60\"";
    private const string RequestId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
    private static readonly DateTimeOffset ObservedAt =
        new(2026, 9, 1, 8, 30, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task NightlyReadUsesTheExactArmRequestAndReturnsOnlyVerifiedPolicyFacts()
    {
        using var fixture = Fixture.For(NightlyPayload());

        var observation = await fixture.Reader.ReadAsync(
            CustodyClass.NightlyFloor90d,
            CancellationToken.None);

        Assert.AreEqual(CustodyClass.NightlyFloor90d, observation.CustodyClass);
        Assert.AreEqual(ObservedAt, observation.ObservedAt);
        Assert.AreEqual(90, observation.LockedRetentionDays);
        Assert.IsFalse(observation.ActiveLegalHold);
        var receipt = observation.ConfigurationReceipt;
        Assert.AreEqual(AzureCustodySchemaIds.ConfigurationReceipt, receipt.Schema);
        Assert.AreEqual(Options().NightlyPolicyKey, receipt.PolicyKey);
        Assert.AreEqual(CustodyClass.NightlyFloor90d, receipt.CustodyClass);
        Assert.AreEqual(ObservedAt, receipt.ObservedAt);
        Assert.AreEqual(ResourceId(Options().NightlyContainer), receipt.ArmResourceId);
        Assert.AreEqual("2025-06-01", receipt.ArmApiVersion);
        Assert.AreEqual(ConfigurationEtag, receipt.ArmResourceEtag);
        Assert.AreEqual(RequestId, receipt.ArmRequestId);
        Assert.AreEqual(Options().ManagedIdentityClientId, receipt.ManagedIdentityClientId);
        Assert.AreEqual("None", receipt.PublicAccess);
        Assert.IsFalse(receipt.ImmutableStorageWithVersioningEnabled);
        Assert.IsNull(receipt.MigrationState);
        Assert.AreEqual(PolicyEtag, receipt.ImmutabilityPolicyEtag);
        Assert.AreEqual("Locked", receipt.ImmutabilityPolicyState);
        Assert.AreEqual(90, receipt.RetentionDays);
        Assert.IsFalse(receipt.ProtectedAppendWrites);
        Assert.IsFalse(receipt.ProtectedAppendWritesAll);
        Assert.IsFalse(receipt.ActiveLegalHold);
        Assert.IsFalse(receipt.ProtectedBlockBlobAppends);
        Assert.AreEqual(1, fixture.Credential.CallCount);
        CollectionAssert.AreEqual(
            new[] { "https://management.azure.com/.default" },
            fixture.Credential.Scopes);
        Assert.AreEqual(1, fixture.Handler.CallCount);
        Assert.AreEqual(HttpMethod.Get, fixture.Handler.Method);
        Assert.AreEqual(
            ExpectedUri(Options().NightlyContainer),
            fixture.Handler.RequestUri?.OriginalString);
        Assert.AreEqual("Bearer synthetic-arm-token", fixture.Handler.Authorization);
        CollectionAssert.AreEqual(new[] { "application/json" }, fixture.Handler.Accept);
    }

    [TestMethod]
    public async Task PrivateConfigurationReceiptHasAClosedVersionedWireShape()
    {
        using var fixture = Fixture.For(NightlyPayload());
        var observation = await fixture.Reader.ReadAsync(
            CustodyClass.NightlyFloor90d,
            CancellationToken.None);

        var json = ContractJson.Serialize(observation.ConfigurationReceipt);
        StringAssert.Contains(json, "\"schema\":\"lex-v3-azure-custody-configuration-receipt/1\"");
        StringAssert.Contains(json, "\"custody_class\":\"nightly_floor_90d\"");
        Assert.AreEqual(
            observation.ConfigurationReceipt,
            ContractJson.Deserialize<AzureCustodyConfigurationReceipt>(json));
    }

    [TestMethod]
    public async Task RedirectResponsesAreNotAcceptedAsPolicyEvidence()
    {
        using var fixture = Fixture.For(
            NightlyPayload(),
            statusCode: HttpStatusCode.TemporaryRedirect,
            location: new Uri("https://example.invalid/redirect"));

        await Assert.ThrowsExactlyAsync<CustodyRequiredException>(() => fixture.Reader.ReadAsync(
            CustodyClass.NightlyFloor90d,
            CancellationToken.None));

        Assert.AreEqual(1, fixture.Handler.CallCount);
        Assert.AreEqual(
            ExpectedUri(Options().NightlyContainer),
            fixture.Handler.RequestUri?.OriginalString);
    }

    [TestMethod]
    [DataRow("id")]
    [DataRow("name")]
    [DataRow("type")]
    public async Task ExactContainerResourceIdentityIsRequired(string member)
    {
        var root = NightlyRoot();
        root[member] = member switch
        {
            "id" => ResourceId("another-container"),
            "name" => "another-container",
            _ => "Microsoft.Storage/storageAccounts/blobServices",
        };
        using var fixture = Fixture.For(root.ToJsonString());

        await Assert.ThrowsExactlyAsync<CustodyPolicyException>(() => fixture.Reader.ReadAsync(
            CustodyClass.NightlyFloor90d,
            CancellationToken.None));
    }

    [TestMethod]
    public async Task AContainerWithPublicBlobAccessIsRejected()
    {
        var root = NightlyRoot();
        Properties(root)["publicAccess"] = "Blob";
        using var fixture = Fixture.For(root.ToJsonString());

        await Assert.ThrowsExactlyAsync<CustodyPolicyException>(() => fixture.Reader.ReadAsync(
            CustodyClass.NightlyFloor90d,
            CancellationToken.None));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task VersionLevelWormMustBeExplicitlyDisabled(bool enabled)
    {
        var root = NightlyRoot();
        Versioning(root)["enabled"] = enabled;
        using var fixture = Fixture.For(root.ToJsonString());

        if (enabled)
        {
            await Assert.ThrowsExactlyAsync<CustodyPolicyException>(() => fixture.Reader.ReadAsync(
                CustodyClass.NightlyFloor90d,
                CancellationToken.None));
            return;
        }

        var observation = await fixture.Reader.ReadAsync(
            CustodyClass.NightlyFloor90d,
            CancellationToken.None);
        Assert.AreEqual(90, observation.LockedRetentionDays);
    }

    [TestMethod]
    public async Task AResponseThatOmitsTheVersionLevelWormFlagIsRejected()
    {
        var root = NightlyRoot();
        Versioning(root).Remove("enabled");
        using var fixture = Fixture.For(root.ToJsonString());

        await Assert.ThrowsExactlyAsync<CustodyPolicyException>(() => fixture.Reader.ReadAsync(
            CustodyClass.NightlyFloor90d,
            CancellationToken.None));
    }

    [TestMethod]
    public async Task AnyObjectLevelMigrationStateIsRejectedWhileNullIsSafe()
    {
        var migrating = NightlyRoot();
        Versioning(migrating)["migrationState"] = "InProgress";
        using (var fixture = Fixture.For(migrating.ToJsonString()))
        {
            await Assert.ThrowsExactlyAsync<CustodyPolicyException>(() => fixture.Reader.ReadAsync(
                CustodyClass.NightlyFloor90d,
                CancellationToken.None));
        }

        var notMigrating = NightlyRoot();
        Versioning(notMigrating)["migrationState"] = null;
        using var safeFixture = Fixture.For(notMigrating.ToJsonString());
        var observation = await safeFixture.Reader.ReadAsync(
            CustodyClass.NightlyFloor90d,
            CancellationToken.None);
        Assert.AreEqual(90, observation.LockedRetentionDays);
    }

    [TestMethod]
    public async Task MissingOrFalseNightlyProtectedAppendFlagsAreSafe()
    {
        using (var missing = Fixture.For(NightlyPayload()))
        {
            var observation = await missing.Reader.ReadAsync(
                CustodyClass.NightlyFloor90d,
                CancellationToken.None);
            Assert.AreEqual(90, observation.LockedRetentionDays);
        }

        var root = NightlyRoot();
        NightlyPolicy(root)["allowProtectedAppendWrites"] = false;
        NightlyPolicy(root)["allowProtectedAppendWritesAll"] = false;
        using var explicitFalse = Fixture.For(root.ToJsonString());
        var explicitObservation = await explicitFalse.Reader.ReadAsync(
            CustodyClass.NightlyFloor90d,
            CancellationToken.None);
        Assert.AreEqual(90, explicitObservation.LockedRetentionDays);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task NightlyLaneRequiresAnExplicitlyFalseLegalHold(bool removeMember)
    {
        var root = NightlyRoot();
        if (removeMember)
        {
            Properties(root).Remove("hasLegalHold");
        }
        else
        {
            Properties(root)["hasLegalHold"] = true;
        }

        using var fixture = Fixture.For(root.ToJsonString());
        await Assert.ThrowsExactlyAsync<CustodyPolicyException>(() => fixture.Reader.ReadAsync(
            CustodyClass.NightlyFloor90d,
            CancellationToken.None));
    }

    [TestMethod]
    public async Task NightlyPolicyResourceMustCarryItsOwnEtag()
    {
        var root = NightlyRoot();
        NightlyPolicyResource(root).Remove("etag");
        using var fixture = Fixture.For(root.ToJsonString());

        await Assert.ThrowsExactlyAsync<CustodyPolicyException>(() => fixture.Reader.ReadAsync(
            CustodyClass.NightlyFloor90d,
            CancellationToken.None));
    }

    [TestMethod]
    [DataRow("allowProtectedAppendWrites")]
    [DataRow("allowProtectedAppendWritesAll")]
    public async Task AnyEnabledNightlyProtectedAppendModeIsRejected(string member)
    {
        var root = NightlyRoot();
        NightlyPolicy(root)[member] = true;
        using var fixture = Fixture.For(root.ToJsonString());

        await Assert.ThrowsExactlyAsync<CustodyPolicyException>(() => fixture.Reader.ReadAsync(
            CustodyClass.NightlyFloor90d,
            CancellationToken.None));
    }

    [TestMethod]
    [DataRow("Unlocked", 90)]
    [DataRow("Locked", 0)]
    [DataRow("Locked", 146001)]
    public async Task NightlyRequiresALockedPolicyAndAnAdmittedRetentionPeriod(
        string state,
        int days)
    {
        var root = NightlyRoot();
        NightlyPolicy(root)["state"] = state;
        NightlyPolicy(root)["immutabilityPeriodSinceCreationInDays"] = days;
        using var fixture = Fixture.For(root.ToJsonString());

        await Assert.ThrowsExactlyAsync<CustodyPolicyException>(() => fixture.Reader.ReadAsync(
            CustodyClass.NightlyFloor90d,
            CancellationToken.None));
    }

    [TestMethod]
    public async Task LegalHoldAcceptsMissingOrExplicitlyFalseProtectedAppendHistory()
    {
        using (var missing = Fixture.For(LegalHoldPayload()))
        {
            var observation = await missing.Reader.ReadAsync(
                CustodyClass.LegalHoldEvidence,
                CancellationToken.None);
            Assert.IsTrue(observation.ActiveLegalHold);
            Assert.IsNull(observation.LockedRetentionDays);
            var receipt = observation.ConfigurationReceipt;
            Assert.AreEqual(Options().LegalHoldPolicyKey, receipt.PolicyKey);
            Assert.AreEqual(CustodyClass.LegalHoldEvidence, receipt.CustodyClass);
            Assert.IsNull(receipt.ImmutabilityPolicyEtag);
            Assert.IsNull(receipt.ImmutabilityPolicyState);
            Assert.IsNull(receipt.RetentionDays);
            Assert.IsTrue(receipt.ActiveLegalHold);
            Assert.IsFalse(receipt.ProtectedBlockBlobAppends);
        }

        var root = LegalHoldRoot();
        LegalHold(root)["protectedAppendWritesHistory"] = new JsonObject
        {
            ["allowProtectedAppendWritesAll"] = false,
        };
        using var explicitFalse = Fixture.For(root.ToJsonString());
        var explicitObservation = await explicitFalse.Reader.ReadAsync(
            CustodyClass.LegalHoldEvidence,
            CancellationToken.None);
        Assert.IsTrue(explicitObservation.ActiveLegalHold);
    }

    [TestMethod]
    public async Task LegalHoldRejectsEnabledProtectedAppendHistory()
    {
        var root = LegalHoldRoot();
        LegalHold(root)["protectedAppendWritesHistory"] = new JsonObject
        {
            ["allowProtectedAppendWritesAll"] = true,
        };
        using var fixture = Fixture.For(root.ToJsonString());

        await Assert.ThrowsExactlyAsync<CustodyPolicyException>(() => fixture.Reader.ReadAsync(
            CustodyClass.LegalHoldEvidence,
            CancellationToken.None));
    }

    [TestMethod]
    [DataRow(true, false)]
    [DataRow(false, true)]
    public async Task LegalHoldMustBeActiveInBothSummaryAndDetail(
        bool summary,
        bool detail)
    {
        var root = LegalHoldRoot();
        Properties(root)["hasLegalHold"] = summary;
        LegalHold(root)["hasLegalHold"] = detail;
        using var fixture = Fixture.For(root.ToJsonString());

        await Assert.ThrowsExactlyAsync<CustodyPolicyException>(() => fixture.Reader.ReadAsync(
            CustodyClass.LegalHoldEvidence,
            CancellationToken.None));
    }

    [TestMethod]
    public async Task LegalHoldRejectsMissingImmutabilitySummary()
    {
        var root = LegalHoldRoot();
        Properties(root).Remove("hasImmutabilityPolicy");
        using var fixture = Fixture.For(root.ToJsonString());

        await Assert.ThrowsExactlyAsync<CustodyPolicyException>(() => fixture.Reader.ReadAsync(
            CustodyClass.LegalHoldEvidence,
            CancellationToken.None));
    }

    [TestMethod]
    public async Task LegalHoldRejectsActiveImmutabilitySummary()
    {
        var root = LegalHoldRoot();
        Properties(root)["hasImmutabilityPolicy"] = true;
        using var fixture = Fixture.For(root.ToJsonString());

        await Assert.ThrowsExactlyAsync<CustodyPolicyException>(() => fixture.Reader.ReadAsync(
            CustodyClass.LegalHoldEvidence,
            CancellationToken.None));
    }

    [TestMethod]
    public async Task LegalHoldRejectsNonBooleanImmutabilitySummary()
    {
        var root = LegalHoldRoot();
        Properties(root)["hasImmutabilityPolicy"] = "false";
        using var fixture = Fixture.For(root.ToJsonString());

        await Assert.ThrowsExactlyAsync<CustodyPolicyException>(() => fixture.Reader.ReadAsync(
            CustodyClass.LegalHoldEvidence,
            CancellationToken.None));
    }

    [TestMethod]
    public async Task LegalHoldRejectsDuplicateImmutabilitySummary()
    {
        var duplicate = LegalHoldPayload().Replace(
            "\"hasImmutabilityPolicy\":false",
            "\"hasImmutabilityPolicy\":false,\"hasImmutabilityPolicy\":false",
            StringComparison.Ordinal);
        using var fixture = Fixture.For(duplicate);

        await Assert.ThrowsExactlyAsync<CustodyPolicyException>(() => fixture.Reader.ReadAsync(
            CustodyClass.LegalHoldEvidence,
            CancellationToken.None));
    }

    [TestMethod]
    public async Task StagingVerificationAcceptsOnlyAPrivateMutableContainer()
    {
        using var fixture = Fixture.For(StagingPayload());

        await fixture.Reader.VerifyPrivateStagingAsync(CancellationToken.None);

        Assert.AreEqual(
            ExpectedUri(Options().StagingContainer),
            fixture.Handler.RequestUri?.OriginalString);
    }

    [TestMethod]
    [DataRow("public")]
    [DataRow("immutability")]
    [DataRow("legal-hold")]
    [DataRow("versioning")]
    [DataRow("migration")]
    public async Task StagingVerificationRejectsAProtectionOrExposureState(string mutation)
    {
        var root = StagingRoot();
        switch (mutation)
        {
            case "public":
                Properties(root)["publicAccess"] = "Container";
                break;
            case "immutability":
                Properties(root)["hasImmutabilityPolicy"] = true;
                break;
            case "legal-hold":
                Properties(root)["hasLegalHold"] = true;
                break;
            case "versioning":
                Versioning(root)["enabled"] = true;
                break;
            default:
                Versioning(root)["migrationState"] = "InProgress";
                break;
        }

        using var fixture = Fixture.For(root.ToJsonString());
        await Assert.ThrowsExactlyAsync<CustodyPolicyException>(() =>
            fixture.Reader.VerifyPrivateStagingAsync(CancellationToken.None));
    }

    [TestMethod]
    public async Task DuplicateRequiredMembersAreRejected()
    {
        var duplicateId = NightlyPayload().Replace(
            "\"id\":",
            $"\"id\":\"{ResourceId(Options().NightlyContainer)}\",\"id\":",
            StringComparison.Ordinal);
        using var fixture = Fixture.For(duplicateId);

        await Assert.ThrowsExactlyAsync<CustodyPolicyException>(() => fixture.Reader.ReadAsync(
            CustodyClass.NightlyFloor90d,
            CancellationToken.None));
    }

    [TestMethod]
    public async Task NonSuccessStatusIsCustodyUnavailability()
    {
        using var fixture = Fixture.For(
            NightlyPayload(),
            statusCode: HttpStatusCode.Forbidden);

        await Assert.ThrowsExactlyAsync<CustodyRequiredException>(() => fixture.Reader.ReadAsync(
            CustodyClass.NightlyFloor90d,
            CancellationToken.None));
    }

    [TestMethod]
    public async Task WrongContentTypeIsRejected()
    {
        using var fixture = Fixture.For(NightlyPayload(), mediaType: "text/html");

        await Assert.ThrowsExactlyAsync<CustodyPolicyException>(() => fixture.Reader.ReadAsync(
            CustodyClass.NightlyFloor90d,
            CancellationToken.None));
    }

    [TestMethod]
    public async Task MissingAuthoritativeResponseDateIsRejected()
    {
        using var fixture = Fixture.For(NightlyPayload(), includeDate: false);

        await Assert.ThrowsExactlyAsync<CustodyPolicyException>(() => fixture.Reader.ReadAsync(
            CustodyClass.NightlyFloor90d,
            CancellationToken.None));
    }

    [TestMethod]
    [DataRow("etag")]
    [DataRow("request-id")]
    public async Task MissingConfigurationReceiptHeadersAreRejected(string member)
    {
        using var fixture = Fixture.For(
            NightlyPayload(),
            includeEtag: member != "etag",
            includeRequestId: member != "request-id");

        await Assert.ThrowsExactlyAsync<CustodyPolicyException>(() => fixture.Reader.ReadAsync(
            CustodyClass.NightlyFloor90d,
            CancellationToken.None));
    }

    [TestMethod]
    public async Task BodyAndHeaderConfigurationEtagsMustMatch()
    {
        var root = NightlyRoot();
        root["etag"] = "\"0x000000000000000\"";
        using var fixture = Fixture.For(root.ToJsonString());

        await Assert.ThrowsExactlyAsync<CustodyPolicyException>(() => fixture.Reader.ReadAsync(
            CustodyClass.NightlyFloor90d,
            CancellationToken.None));
    }

    [TestMethod]
    public async Task BlankConfigurationRequestIdIsRejected()
    {
        using var fixture = Fixture.For(NightlyPayload(), requestId: "   ");

        await Assert.ThrowsExactlyAsync<CustodyPolicyException>(() => fixture.Reader.ReadAsync(
            CustodyClass.NightlyFloor90d,
            CancellationToken.None));
    }

    [TestMethod]
    public async Task NonGuidConfigurationRequestIdIsRejectedAsPolicyEvidence()
    {
        using var fixture = Fixture.For(NightlyPayload(), requestId: "not-a-guid");

        await Assert.ThrowsExactlyAsync<CustodyPolicyException>(() => fixture.Reader.ReadAsync(
            CustodyClass.NightlyFloor90d,
            CancellationToken.None));
    }

    [TestMethod]
    public async Task MultipleConfigurationRequestIdsAreRejected()
    {
        using var fixture = Fixture.For(
            NightlyPayload(),
            secondRequestId: "ffffffff-1111-2222-3333-444444444444");

        await Assert.ThrowsExactlyAsync<CustodyPolicyException>(() => fixture.Reader.ReadAsync(
            CustodyClass.NightlyFloor90d,
            CancellationToken.None));
    }

    [TestMethod]
    public async Task MissingOrDuplicateBodyConfigurationEtagIsRejected()
    {
        var missingRoot = NightlyRoot();
        missingRoot.Remove("etag");
        using (var missing = Fixture.For(missingRoot.ToJsonString()))
        {
            await Assert.ThrowsExactlyAsync<CustodyPolicyException>(() => missing.Reader.ReadAsync(
                CustodyClass.NightlyFloor90d,
                CancellationToken.None));
        }

        var duplicate = NightlyPayload().Replace(
            "{\"etag\":",
            $"{{\"etag\":\"{ConfigurationEtag.Replace("\"", "\\u0022", StringComparison.Ordinal)}\",\"etag\":",
            StringComparison.Ordinal);
        using var duplicated = Fixture.For(duplicate);
        await Assert.ThrowsExactlyAsync<CustodyPolicyException>(() => duplicated.Reader.ReadAsync(
            CustodyClass.NightlyFloor90d,
            CancellationToken.None));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task OversizeResponsesAreRejectedWithOrWithoutAContentLength(bool unknownLength)
    {
        var bytes = new byte[(256 * 1024) + 1];
        using var fixture = Fixture.For(
            bytes,
            unknownLength: unknownLength);

        await Assert.ThrowsExactlyAsync<CustodyPolicyException>(() => fixture.Reader.ReadAsync(
            CustodyClass.NightlyFloor90d,
            CancellationToken.None));
    }

    [TestMethod]
    public async Task TrailingMalformedJsonIsRejected()
    {
        using var fixture = Fixture.For(NightlyPayload() + " trailing");

        await Assert.ThrowsExactlyAsync<CustodyPolicyException>(() => fixture.Reader.ReadAsync(
            CustodyClass.NightlyFloor90d,
            CancellationToken.None));
    }

    private static AzureBlobCustodyOptions Options() => new(
        new Uri("https://stlexv3custody.blob.core.windows.net/"),
        "staging",
        "nightly",
        "legal-hold",
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        Guid.Parse("22222222-2222-2222-2222-222222222222"),
        Guid.Parse("33333333-3333-3333-3333-333333333333"),
        Guid.Parse("44444444-4444-4444-4444-444444444444"),
        "rg-lex_(v3)");

    private static string ExpectedUri(string container) =>
        "https://management.azure.com/subscriptions/"
        + $"{Options().SubscriptionId:D}"
        + "/resourceGroups/rg-lex_%28v3%29"
        + "/providers/Microsoft.Storage/storageAccounts/stlexv3custody"
        + "/blobServices/default/containers/"
        + container
        + "?api-version=2025-06-01";

    private static string ResourceId(string container)
    {
        var options = Options();
        return $"/subscriptions/{options.SubscriptionId:D}"
            + $"/resourceGroups/{options.ResourceGroup}"
            + "/providers/Microsoft.Storage/storageAccounts/"
            + options.StorageAccountName
            + "/blobServices/default/containers/"
            + container;
    }

    private static string NightlyPayload() => NightlyRoot().ToJsonString();

    private static JsonObject NightlyRoot()
    {
        var root = ContainerRoot(Options().NightlyContainer);
        var properties = Properties(root);
        properties["hasImmutabilityPolicy"] = true;
        properties["hasLegalHold"] = false;
        properties["immutabilityPolicy"] = new JsonObject
        {
            ["etag"] = PolicyEtag,
            ["properties"] = new JsonObject
            {
                ["state"] = "Locked",
                ["immutabilityPeriodSinceCreationInDays"] = 90,
            },
        };
        return root;
    }

    private static string LegalHoldPayload() => LegalHoldRoot().ToJsonString();

    private static JsonObject LegalHoldRoot()
    {
        var root = ContainerRoot(Options().LegalHoldContainer);
        var properties = Properties(root);
        properties["hasImmutabilityPolicy"] = false;
        properties["hasLegalHold"] = true;
        properties["legalHold"] = new JsonObject
        {
            ["hasLegalHold"] = true,
        };
        return root;
    }

    private static string StagingPayload() => StagingRoot().ToJsonString();

    private static JsonObject StagingRoot()
    {
        var root = ContainerRoot(Options().StagingContainer);
        var properties = Properties(root);
        properties["hasImmutabilityPolicy"] = false;
        properties["hasLegalHold"] = false;
        return root;
    }

    private static JsonObject ContainerRoot(string container) => new()
    {
        ["etag"] = ConfigurationEtag,
        ["id"] = ResourceId(container),
        ["name"] = container,
        ["type"] = "Microsoft.Storage/storageAccounts/blobServices/containers",
        ["properties"] = new JsonObject
        {
            ["publicAccess"] = "None",
            ["immutableStorageWithVersioning"] = new JsonObject
            {
                ["enabled"] = false,
            },
        },
    };

    private static JsonObject Properties(JsonObject root) =>
        (JsonObject)root["properties"]!;

    private static JsonObject Versioning(JsonObject root) =>
        (JsonObject)Properties(root)["immutableStorageWithVersioning"]!;

    private static JsonObject NightlyPolicy(JsonObject root) =>
        (JsonObject)NightlyPolicyResource(root)["properties"]!;

    private static JsonObject NightlyPolicyResource(JsonObject root) =>
        (JsonObject)Properties(root)["immutabilityPolicy"]!;

    private static JsonObject LegalHold(JsonObject root) =>
        (JsonObject)Properties(root)["legalHold"]!;

    private sealed class Fixture : IDisposable
    {
        private readonly HttpClient _client;

        private Fixture(
            CapturingCredential credential,
            CapturingHandler handler,
            HttpClient client)
        {
            Credential = credential;
            Handler = handler;
            _client = client;
            Reader = new AzureArmCustodyPolicyReader(Options(), credential, client);
        }

        public AzureArmCustodyPolicyReader Reader { get; }

        public CapturingCredential Credential { get; }

        public CapturingHandler Handler { get; }

        public static Fixture For(
            string payload,
            HttpStatusCode statusCode = HttpStatusCode.OK,
            string mediaType = "application/json",
            bool includeDate = true,
            bool includeEtag = true,
            bool includeRequestId = true,
            string requestId = RequestId,
            string? secondRequestId = null,
            Uri? location = null) => For(
                Encoding.UTF8.GetBytes(payload),
                statusCode,
                mediaType,
                includeDate,
                unknownLength: false,
                includeEtag,
                includeRequestId,
                requestId,
                secondRequestId,
                location);

        public static Fixture For(
            byte[] payload,
            HttpStatusCode statusCode = HttpStatusCode.OK,
            string mediaType = "application/json",
            bool includeDate = true,
            bool unknownLength = false,
            bool includeEtag = true,
            bool includeRequestId = true,
            string requestId = RequestId,
            string? secondRequestId = null,
            Uri? location = null)
        {
            var credential = new CapturingCredential();
            var handler = new CapturingHandler(() =>
            {
                HttpContent content = unknownLength
                    ? new UnknownLengthContent(payload)
                    : new ByteArrayContent(payload);
                content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
                var response = new HttpResponseMessage(statusCode)
                {
                    Content = content,
                };
                if (includeDate)
                {
                    response.Headers.Date = ObservedAt;
                }

                if (includeEtag)
                {
                    response.Headers.ETag = new EntityTagHeaderValue(ConfigurationEtag);
                }

                if (includeRequestId)
                {
                    response.Headers.TryAddWithoutValidation("x-ms-request-id", requestId);
                    if (secondRequestId is not null)
                    {
                        response.Headers.TryAddWithoutValidation(
                            "x-ms-request-id", secondRequestId);
                    }
                }

                response.Headers.Location = location;
                return response;
            });
            var client = new HttpClient(handler)
            {
                Timeout = Timeout.InfiniteTimeSpan,
            };
            return new Fixture(credential, handler, client);
        }

        public void Dispose() => _client.Dispose();
    }

    private sealed class CapturingCredential : TokenCredential
    {
        public int CallCount { get; private set; }

        public string[] Scopes { get; private set; } = [];

        public override AccessToken GetToken(
            TokenRequestContext requestContext,
            CancellationToken cancellationToken) => Capture(requestContext);

        public override ValueTask<AccessToken> GetTokenAsync(
            TokenRequestContext requestContext,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(Capture(requestContext));

        private AccessToken Capture(TokenRequestContext context)
        {
            CallCount++;
            Scopes = context.Scopes.ToArray();
            return new AccessToken("synthetic-arm-token", ObservedAt.AddHours(1));
        }
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly Func<HttpResponseMessage> _response;

        public CapturingHandler(Func<HttpResponseMessage> response) => _response = response;

        public int CallCount { get; private set; }

        public HttpMethod? Method { get; private set; }

        public Uri? RequestUri { get; private set; }

        public string? Authorization { get; private set; }

        public string[] Accept { get; private set; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            Method = request.Method;
            RequestUri = request.RequestUri;
            Authorization = request.Headers.Authorization?.ToString();
            Accept = request.Headers.Accept.Select(value => value.MediaType ?? string.Empty).ToArray();
            return Task.FromResult(_response());
        }
    }

    private sealed class UnknownLengthContent : HttpContent
    {
        private readonly byte[] _payload;

        public UnknownLengthContent(byte[] payload) => _payload = payload;

        protected override Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context) => stream.WriteAsync(_payload).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
