using System.Security.Cryptography;
using System.Text.Json;
using Lex.V3.Api;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Index;
using Lex.V3.Contracts.Platform;
using Lex.V3.Contracts.Source.Core;
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

    [TestMethod]
    public async Task AnEuIdentifierIsAttributedToTheEuWhateverItsFormAndOnlyOneNoIndexHoldsIsUnknownToTheTimeView()
    {
        var (root, directory) = await ConsolidatedMountAsync();
        try
        {
            using var mount = await V3CorpusMount.OpenAsync(directory, CancellationToken.None);
            Assert.IsNotNull(mount);
            var asOf = await EnvelopeAsync(mount, "/api/v3/as_of", "as_of", new { identifier = GdprSeed, date = "2025-03-01", language = "eng" });
            Assert.IsNull(asOf.Refusal, asOf.Refusal?.Code);
            var wording = asOf.Result!.Value.GetProperty("states")[0].GetProperty("wording");
            var coordinate = wording.GetProperty("stable_coordinate").GetString()!;
            var permalink = wording.GetProperty("permalink").GetString()!;

            // as_observed by time refuses an EU identifier the mode with EU context, as by snapshot (review of #913: it answered
            // snapshot_unknown with Luxembourg context).
            var byTime = await EnvelopeAsync(mount, "/api/v3/as_observed", "as_observed", new { identifier = GdprSeed, date = "2025-03-01", at = "2026-10-01T08:30:00Z" });
            Assert.AreEqual("retrieval_mode_unavailable", byTime.Refusal?.Code);
            Assert.AreEqual(PublisherId.EuEurLex, byTime.Context.Publisher);

            // This service's own EU coordinates are EU-shaped: an operation refused for EU identifiers refuses them the mode with EU
            // context (review of #913: they were answered identifier_unknown, "no publisher shape", with Luxembourg context) ...
            foreach (var identifier in new[] { coordinate, permalink, "https://law.soufien.lu" + permalink })
            {
                var history = await EnvelopeAsync(mount, "/api/v3/article_history", "article_history", new { identifier, anchor = "art_1", language = "eng" });
                Assert.AreEqual("retrieval_mode_unavailable", history.Refusal?.Code, identifier);
                Assert.AreEqual(PublisherId.EuEurLex, history.Context.Publisher, identifier);
            }

            // ... and the time view answers the act they name, as it answers a provision coordinate of a held expression, the
            // consolidated wording's included.
            var bundle = await EnvelopeAsync(mount, "/api/v3/evidence_bundle", "evidence_bundle", new { identifier = GdprSeed, date = "2025-03-01", language = "eng" });
            Assert.IsNull(bundle.Refusal, bundle.Refusal?.Code);
            var provision = bundle.Result!.Value.GetProperty("wordings")[0].GetProperty("articles")[0].GetProperty("provision_coordinate").GetString()!;
            foreach (var identifier in new[] { coordinate, provision })
            {
                var answered = await EnvelopeAsync(mount, "/api/v3/as_of", "as_of", new { identifier, date = "2025-03-01", language = "eng" });
                Assert.IsNull(answered.Refusal, $"{identifier}: {answered.Refusal?.Code}");
                Assert.AreEqual(GdprSeed, answered.Result!.Value.GetProperty("seed_celex").GetString(), identifier);
                var listed = await EnvelopeAsync(mount, "/api/v3/timeline", "timeline", new { identifier, language = "eng" });
                Assert.IsNull(listed.Refusal, $"{identifier}: {listed.Refusal?.Code}");
            }

            // An EU identifier no index holds is unknown, with EU context, in as_of and timeline as in dossier: these operations
            // serve EU acts, so the mode refusal does not describe it (review of #913).
            foreach (var (operation, request) in new (string, object)[]
                     {
                         ("as_of", new { identifier = "32099R9999", date = "2025-03-01", language = "eng" }),
                         ("timeline", new { identifier = "32099R9999", language = "eng" }),
                         ("dossier", new { identifier = "32099R9999" }),
                     })
            {
                var envelope = await EnvelopeAsync(mount, "/api/v3/" + operation, operation, request);
                Assert.AreEqual("identifier_unknown", envelope.Refusal?.Code, operation);
                Assert.AreEqual(PublisherId.EuEurLex, envelope.Context.Publisher, operation);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task CoverageReportsTheMountedEuManifestSoAManifestWrittenBeforeTheRowsStatesNoEuRefusal()
    {
        var (root, directory) = await ConsolidatedMountAsync();
        try
        {
            // The EU manifest as a build before the EU rows wrote it: the same cells, and the three operations no route serves.
            var path = Path.Combine(directory, V3CorpusMount.EuropeCapabilityManifestFileName);
            var bytes = await File.ReadAllBytesAsync(path);
            var digest = V3IndexCapabilityManifestArtifact.ComputeSha256(bytes);
            var indexSha256 = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(directory, V3CorpusMount.EuropeIndexFileName))));
            var mounted = V3IndexCapabilityManifestArtifact.ParseAndVerify(
                new SourceArtifactRef(LexCorpus6Builder.ResourceIdOf(digest), digest), bytes, PublisherId.EuEurLex, indexSha256);
            Assert.HasCount(16, mounted.NotServed, "a current build states the 3 unserved operations and the 13 EU refusals");
            Assert.IsTrue(V3IndexCapabilityManifest.TryCreate(
                PublisherId.EuEurLex, indexSha256, mounted.Cells, V3UnservedOperations.Rows, out var earlier, out var refusal), refusal.ToString());
            using (var stream = File.Create(path))
            {
                _ = V3IndexCapabilityManifestArtifact.Write(stream, earlier!);
            }

            using var mount = await V3CorpusMount.OpenAsync(directory, CancellationToken.None);
            Assert.IsNotNull(mount, "an earlier manifest still mounts: the reader checks its cells");
            var operations = (await EnvelopeAsync(mount, "/api/v3/coverage", "coverage", new { })).Result!.Value.GetProperty("operations");
            Assert.AreEqual(0, operations.GetProperty("refused_for_eu").GetArrayLength(),
                "coverage reports what the mounted manifest states, not the platform's table");
            // The API refuses as it does on any mount; only the statement is missing.
            var refused = await EnvelopeAsync(mount, "/api/v3/diff", "diff", EuropeRefusedRequests["diff"]);
            Assert.AreEqual("retrieval_mode_unavailable", refused.Refusal?.Code);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
