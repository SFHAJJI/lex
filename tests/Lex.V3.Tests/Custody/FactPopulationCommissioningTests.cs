using System.Text.Json;
using Lex.V3.Custody.Probe;

namespace Lex.V3.Tests.Custody;

[TestClass]
public sealed class FactPopulationCommissioningTests
{
    private const string ProposalSha256 =
        "2e134d982964af2592aac5002e0b2672b775e87288a80faa054ece0c4388f835";
    private static readonly Guid NightlyPolicyKey =
        Guid.Parse("ff52fe20-4b11-4ca2-9542-22249d5c4c06");

    [TestMethod]
    public async Task ExactProposalRetainsOneReplayableFiveFactPopulation()
    {
        var store = new AzureCustodyProbeContractTests.ProbeStore();
        var output = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);

        await CustodyProbeApplication.RunAsync(
            ["commission-replay", ProposalSha256],
            TextReader.Null,
            output,
            Environment(),
            _ => store,
            CancellationToken.None);

        using var receipt = JsonDocument.Parse(output.ToString());
        var root = receipt.RootElement;
        Assert.AreEqual("lex-v3-custody-fact-population-commissioning/1",
            root.GetProperty("schema").GetString());
        CollectionAssert.AreEqual(
            new[]
            {
                "schema",
                "proposal_sha256",
                "source_body_sha256",
                "source_route_sha256",
                "source_observation_id",
                "body_write_receipt_sha256",
                "commissioned_route_sha256",
                "fact_sha256s",
                "replay_input_sha256",
            },
            root.EnumerateObject().Select(static property => property.Name).ToArray(),
            "The evidence record must contain only claims established by this execution.");
        Assert.AreEqual(ProposalSha256, root.GetProperty("proposal_sha256").GetString());
        Assert.AreEqual(5, root.GetProperty("fact_sha256s").GetArrayLength());
        Assert.AreEqual(9, store.CreateCalls);

        var replayInputSha256 = root.GetProperty("replay_input_sha256").GetString()!;
        using var replay = JsonDocument.Parse(
            await FactCustodyReplay.RunAsync(store, replayInputSha256, CancellationToken.None));
        Assert.AreEqual(5, replay.RootElement.GetProperty("facts").GetArrayLength());
        Assert.AreEqual(1, replay.RootElement.GetProperty("observations").GetArrayLength());
        Assert.IsFalse(replay.RootElement.GetProperty("acceptance_established").GetBoolean());
    }

    [TestMethod]
    public async Task AnyOtherProposalIsRefusedBeforeStoreCreation()
    {
        var storeCreated = false;
        await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            CustodyProbeApplication.RunAsync(
                ["commission-replay", new string('a', 64)],
                TextReader.Null,
                TextWriter.Null,
                Environment(),
                _ =>
                {
                    storeCreated = true;
                    return new AzureCustodyProbeContractTests.ProbeStore();
                },
                CancellationToken.None));
        Assert.IsFalse(storeCreated);
    }

    private static Dictionary<string, string?> Environment() => new(StringComparer.Ordinal)
    {
        ["LEX_V3_CUSTODY_SERVICE_URI"] = "https://stlexv3custody.blob.core.windows.net/",
        ["LEX_V3_CUSTODY_STAGING_CONTAINER"] = "staging",
        ["LEX_V3_CUSTODY_NIGHTLY_CONTAINER"] = "nightly",
        ["LEX_V3_CUSTODY_LEGAL_HOLD_CONTAINER"] = "legal-hold",
        ["LEX_V3_CUSTODY_MANAGED_IDENTITY_CLIENT_ID"] = "66ba38aa-74fa-42c4-ab19-e4e41b9ae01b",
        ["LEX_V3_CUSTODY_NIGHTLY_POLICY_KEY"] = NightlyPolicyKey.ToString("D"),
        ["LEX_V3_CUSTODY_LEGAL_HOLD_POLICY_KEY"] = "b3eb07d3-9159-4673-a4b2-0f4b3ca86293",
        ["LEX_V3_CUSTODY_SUBSCRIPTION_ID"] = "37000e0e-4444-4f9a-95f9-3a786b4ddd30",
        ["LEX_V3_CUSTODY_RESOURCE_GROUP"] = "resource-group",
        ["IDENTITY_ENDPOINT"] = "http://127.0.0.1:42356/msi/token",
        ["IDENTITY_HEADER"] = "platform-rotated-header",
    };

}
