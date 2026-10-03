using System.Text.Json;
using Lex.V3.Api;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Platform;
using static Lex.V3.Ingest.Tests.V3CorpusClassificationMountTests;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The launch contract's line "Operations served for Luxembourg and EU from the mounted corpus: all 27 registered names, each
/// either served or refusing with a typed reason its capability manifest states", held for EU identifiers on a mount whose EU
/// index carries the time view: each operation the EU capability manifest states refused answers that refusal for an EU
/// identifier, each operation the time view serves for an EU act answers, and every registered operation is one or the other,
/// served by no route, or takes no work identifier.
/// </summary>
public sealed partial class V3FirstMountBuildTests
{
    /// <summary>A request for each operation the EU manifest states refused, shaped as the operation takes it, with an EU identifier.</summary>
    private static readonly IReadOnlyDictionary<string, object> EuropeRefusedRequests = new Dictionary<string, object>(StringComparer.Ordinal)
    {
        ["answer_drift"] = new { identifier = GdprSeed },
        ["article_history"] = new { identifier = GdprSeed, anchor = "art_1", language = "eng" },
        ["as_observed"] = new { identifier = GdprSeed, date = "2025-03-01", snapshot = new string('0', 64) },
        ["changes_in_period"] = new { identifier = GdprSeed, date_from = "2018-05-25", date_to = "2024-01-01", language = "eng" },
        ["citation"] = new { identifier = GdprSeed, date = "2025-03-01", language = "eng" },
        ["cited_by"] = new { identifier = GdprSeed },
        ["classification"] = new { identifier = GdprSeed, language = "eng" },
        ["diff"] = new { identifier = GdprSeed, date_from = "2018-05-25", date_to = "2025-03-01", language = "eng" },
        ["in_force_on"] = new { identifier = GdprSeed, date = "2025-03-01", language = "eng" },
        ["manifestation"] = new { identifier = GdprSeed, language = "eng" },
        ["provenance"] = new { identifier = GdprSeed, date = "2025-03-01", language = "eng" },
        ["relations"] = new { identifier = GdprSeed },
        ["status_on"] = new { identifier = GdprSeed, date = "2025-03-01", language = "eng" },
    };

    [TestMethod]
    public async Task EveryOperationRefusedForAnEuIdentifierIsOneTheEuCapabilityManifestStatesWithItsTypedReason()
    {
        var (root, directory) = await ConsolidatedMountAsync();
        try
        {
            using var mount = await V3CorpusMount.OpenAsync(directory, CancellationToken.None);
            Assert.IsNotNull(mount);

            // The mounted EU manifest's statement, as coverage reports it.
            var operations = (await EnvelopeAsync(mount, "/api/v3/coverage", "coverage", new { })).Result!.Value.GetProperty("operations");
            var stated = operations.GetProperty("refused_for_eu").EnumerateArray().ToArray();
            CollectionAssert.AreEqual(
                V3EuropeRefusedOperations.Rows.Select(static row => row.Operation).ToArray(),
                stated.Select(static row => row.GetProperty("operation").GetString()!).ToArray(),
                "coverage reports the mounted EU manifest's refusals, in operation order");
            StringAssert.Contains(operations.GetProperty("refused_for_eu_note").GetString(), "retrieval_mode_unavailable");
            CollectionAssert.AreEquivalent(
                EuropeRefusedRequests.Keys.ToArray(),
                stated.Select(static row => row.GetProperty("operation").GetString()!).ToArray(),
                "this test asks every stated operation");

            // Each stated refusal is the one an EU identifier receives: the stated code, with EU context and the registry's fields.
            foreach (var row in stated)
            {
                var operation = row.GetProperty("operation").GetString()!;
                Assert.IsFalse(string.IsNullOrWhiteSpace(row.GetProperty("data_needed").GetString()), operation);
                var envelope = await EnvelopeAsync(mount, "/api/v3/" + operation, operation, EuropeRefusedRequests[operation]);
                Assert.AreEqual(row.GetProperty("reason").GetString(), envelope.Refusal?.Code, $"{operation}: the refusal the manifest states");
                Assert.AreEqual(PublisherId.EuEurLex, envelope.Context.Publisher, operation);
                var payload = envelope.Refusal!.HelpfulPayload;
                Assert.IsTrue(payload.GetProperty("requested_mode").GetString()!.EndsWith(operation, StringComparison.Ordinal), operation);
                Assert.AreEqual(JsonValueKind.Array, payload.GetProperty("available_modes").ValueKind, operation);
            }

            // The operations the time view serves for an EU act answer it.
            var asOf = await EnvelopeAsync(mount, "/api/v3/as_of", "as_of", new { identifier = GdprSeed, date = "2025-03-01", language = "eng" });
            Assert.IsNull(asOf.Refusal, asOf.Refusal?.Code);
            var permalink = asOf.Result!.Value.GetProperty("states")[0].GetProperty("wording").GetProperty("permalink").GetString()!;
            var served = new (string Operation, object Request)[]
            {
                ("as_of", new { identifier = GdprSeed, date = "2025-03-01", language = "eng" }),
                ("timeline", new { identifier = GdprSeed, language = "eng" }),
                ("evidence_bundle", new { identifier = GdprSeed, date = "2025-03-01", language = "eng" }),
                ("verify", new { identifier = permalink }),
                ("dossier", new { identifier = GdprSeed }),
                ("search", new { query = "personal data", language = "eng", identifier = GdprSeed }),
                ("resolve", new { identifier = GdprSeed }),
            };
            foreach (var (operation, request) in served)
            {
                var envelope = await EnvelopeAsync(mount, "/api/v3/" + operation, operation, request);
                Assert.IsNull(envelope.Refusal, $"{operation}: {envelope.Refusal?.Code}");
            }

            // Every registered operation is accounted for, once: served for an EU act, stated refused for an EU identifier, served by
            // no route (stated too), or taking no work identifier.
            var withoutWorkIdentifier = new[] { "ask", "browse", "coverage", "events" };
            CollectionAssert.AreEqual(
                V3OperationRegistry.Reviewed.Operations.Select(static operation => operation.OperationId).Order(StringComparer.Ordinal).ToArray(),
                served.Select(static entry => entry.Operation)
                    .Concat(EuropeRefusedRequests.Keys)
                    .Concat(V3UnservedOperations.Rows.Select(static row => row.Operation))
                    .Concat(withoutWorkIdentifier)
                    .Order(StringComparer.Ordinal)
                    .ToArray(),
                "the 27 registered operations, each once");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
