using System.Text.Json;
using Lex.V3.Api;
using Lex.V3.Contracts.Platform;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The derive's own path for index 10's relation records: the fixture acquisition, its run re-delivered with resolved publisher
/// relations (an act that modifies the fixture's act, one it repeals) and the publication dates the catalogue states, re-derived
/// under Release 1, built into the first mount twice, written, verified and mounted. Both derives give the same files byte for
/// byte; the index holds the relations the run resolved, and the dossier serves them with R72's notice.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class LuxembourgPublisherRelationsDeriveTests
{
    private const string Jolux = "http://data.legilux.public.lu/resource/ontology/jolux#";
    private const string Work = LuxembourgFirstMountAcquisitionTests.ConsolidationParent;
    private const string Amending = "http://data.legilux.public.lu/eli/etat/leg/loi/2026/03/01/a9/jo";
    private const string Repealed = "http://data.legilux.public.lu/eli/etat/leg/rgd/2020/01/01/n1/jo";

    [TestMethod]
    public async Task TwoDerivesOfTheRelationsAreTheSameBytesAndTheDossierServesThemWithTheNotice()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var (europe, acquired) = await V3FirstMountBuildTests.AcquireAsync(
            store, V3FirstMountBuildTests.CheckoutRoot(), LuxembourgRederivationTests.SyntheticConsolidation());
        Assert.IsTrue(europe.Delivered, europe.Detail);
        Assert.IsTrue(acquired.Delivered, acquired.Detail);
        var related = WithRelations(acquired);
        var rederived = await LuxembourgRederivation.ApplyAsync(store, related, LuxembourgRederivationTests.Release1(), CancellationToken.None);
        Assert.AreEqual(acquired.Run!.ResolvedRelations.Count + 3, rederived.Run!.ResolvedRelations.Count, "the re-derivation keeps the run's resolved relations");

        var clock = new V3OfflineMount.BuildClock(new DateTimeOffset(2026, 10, 12, 18, 17, 5, TimeSpan.Zero));
        var directories = new List<string>();
        try
        {
            var digests = new List<string>();
            foreach (var derive in new[] { 1, 2 })
            {
                using var build = await new V3FirstMountBuild(store, clock).RunAsync(europe, rederived, CancellationToken.None);
                Assert.IsTrue(build.Delivered, $"derive {derive}: {build.Refusal}: {build.Detail}");
                var directory = Path.Combine(Path.GetTempPath(), "lex-v3-relations-derive-" + Guid.NewGuid().ToString("N"));
                directories.Add(directory);
                await V3CorpusMountWriter.WriteAsync(build, directory, null, CancellationToken.None, clock.GetUtcNow());
                var verification = await V3CorpusMountWriter.VerifyAsync(directory, CancellationToken.None);
                Assert.IsTrue(verification.Verified, verification.Detail);
                digests.Add(string.Join(" ", new[]
                {
                    V3FirstMountBuildResult.CorpusFileName, V3FirstMountBuildResult.LuxembourgIndexFileName,
                    V3FirstMountBuildResult.LuxembourgCapabilityManifestFileName, V3FirstMountBuildResult.EuropeIndexFileName,
                    V3FirstMountBuildResult.EuropeCapabilityManifestFileName,
                }.Select(name => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(directory, name)))))));
            }

            Assert.AreEqual(digests[0], digests[1], "the second derive writes the first's five files byte for byte");

            using (var connection = LuxembourgIndexBuilder.Open(
                       Path.Combine(directories[0], V3FirstMountBuildResult.LuxembourgIndexFileName), Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly))
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT schema_identity FROM stamp";
                Assert.AreEqual(LuxembourgIndexBuilder.Schema, (string)command.ExecuteScalar()!);
                command.CommandText = "SELECT edge_type || ' ' || subject_iri || ' ' || object_iri || ' ' || coalesce(acting_publication_date, '-') FROM publisher_relations ORDER BY 1";
                var rows = new List<string>();
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read()) rows.Add(reader.GetString(0));
                }

                CollectionAssert.AreEqual(
                    new[] { $"modifies {Amending} {Work}/jo 2026-03-05", $"repeals {Work}/jo {Repealed} -" },
                    rows.ToArray(),
                    "the index holds the relations the run resolved, each with the acting act's publication date from the run's facts; cites is not one");
            }

            using var mount = V3CorpusMount.Served(await V3CorpusMount.OpenAsync(directories[0], CancellationToken.None), directories[0]);
            Assert.IsNotNull(mount);
            var envelope = await V3CorpusIntervalMountTests.AskAsync(mount, "dossier", new { identifier = "/lu-legilux/loi-2026-01-01-a1" });
            Assert.AreEqual(V3Verdicts.Answer, envelope.Verdict, envelope.Refusal?.HelpfulPayload.GetRawText());
            var value = envelope.Result!.Value;
            var views = value.GetProperty("publisher_relations").GetProperty("edges").EnumerateArray()
                .Select(static edge => edge.GetProperty("view").GetString() + " " + edge.GetProperty("other").GetProperty("iri").GetString())
                .ToArray();
            CollectionAssert.AreEqual(new[] { $"amended_by {Amending}", $"repeals {Repealed}" }, views);
            var notice = value.GetProperty("amended_after_consolidation");
            Assert.AreEqual("notice", notice.GetProperty("status").GetString(), notice.GetRawText());
            Assert.AreEqual("2026-02-03", notice.GetProperty("latest_consolidation_publication_date").GetString());
            Assert.AreEqual(Amending, notice.GetProperty("acts")[0].GetProperty("acting_iri").GetString());
        }
        finally
        {
            foreach (var directory in directories.Where(Directory.Exists))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    /// <summary>
    /// The acquisition with three resolved publisher relations (one the projection keeps of each kind it reads, and a citation it
    /// does not) and the publication dates of the amending act and of the consolidation among its typed assertions, as run 15's
    /// set G and catalogue deliver them, from the run's own observation.
    /// </summary>
    private static LuxembourgFirstMountAcquisitionResult WithRelations(LuxembourgFirstMountAcquisitionResult acquired)
    {
        var run = acquired.Run!;
        var evidence = run.TypedAssertions[0].FactDisposition.EvidenceRef;
        LuxembourgResolvedRelation Relation(string subject, string predicate, string target) => new(
            subject, Jolux + predicate, target, evidence, LuxembourgRelationSemantic.AssertedRelation,
            LuxembourgRelationDisposition.Accepted, null, LuxembourgRelationAuthority.PublisherAsserted);
        LuxembourgTypedAssertion Published(string subject, string date) => new(
            new LuxembourgObservedAssertion(
                subject, Jolux + "publicationDate", LuxembourgAssertionObjectKind.Literal, date,
                "http://www.w3.org/2001/XMLSchema#date", "", evidence),
            new LuxembourgAssertionFactDisposition(LuxembourgAssertionPredicate.PublicationDate, LuxembourgAssertionFactKind.DescriptiveDate, evidence),
            null,
            null);
        var redelivered = LuxembourgQueryExecutionResult.Delivered(
            run.Topology, run.FamilyOutcomes, run.RelationFamilyAcquisitions,
            [.. run.ResolvedRelations, Relation(Amending, "modifies", Work + "/jo"), Relation(Work + "/jo", "repeals", Repealed),
                Relation(Work + "/jo", "cites", Repealed)],
            run.LocalInboundRelations,
            [.. run.TypedAssertions, Published(Amending, "2026-03-05"), Published(LuxembourgFirstMountAcquisitionTests.ConsolidationIri, "2026-02-03")],
            run.ResourceObservationSubjects, run.ResourceObservationExclusions, run.ScopeManifestReceipt!,
            run.ScopeManifestCanonicalSha256!, run.DocumentAcquisitionOutcomesByOrdinal!, run.CorpusRecordSetRef!,
            run.CorpusRecordSetReceipt!, run.CorpusRecordSet!, run.ObservedObjectIdentitySetRef!,
            run.ObservedObjectIdentitySetReceipt!, run.ObservedObjectIdentitySet!, run.HeldBodyDerivationPopulation!,
            run.GazetteBodySetsByOrdinal!, run.GazetteListingFetchRefusalsByOrdinal!,
            run.GazetteListingsWithContradictoryLegalValueByOrdinal!, run.PopulationLedger!);
        if (run.DocumentCheckpointRef is { } documents)
        {
            redelivered = redelivered.WithDocumentCheckpoint(documents);
        }
        if (run.GazetteCheckpointRef is { } gazette)
        {
            redelivered = redelivered.WithGazetteCheckpoint(gazette);
        }
        if (run.AcquisitionCheckpointRef is { } query)
        {
            redelivered = redelivered.WithAcquisitionCheckpoint(query);
        }
        var result = LuxembourgFirstMountAcquisitionResult.Success(
            redelivered, acquired.Profile!, acquired.VocabularyEvidenceRef!, acquired.AknInventory!, acquired.AknLegalContent!);
        if (acquired.VocabularyCheckpointRef is { } vocabulary)
        {
            result = result.WithVocabularyCheckpoint(vocabulary);
        }
        if (acquired.CheckpointRef is { } checkpoint)
        {
            result = result.WithCheckpoint(checkpoint, acquired.Resumption);
        }
        if (acquired.PopulationScopeManifestRef is { } scope)
        {
            result = result.WithPopulationScopeManifest(scope);
        }

        return result;
    }
}
