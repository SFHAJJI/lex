using System.Text;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// D1's second refusal (2026-10-26): the Release 1 re-derivation of Luxembourg run 15 got past the article dates and stopped
/// after 79 minutes in the index build, "UNIQUE constraint failed: work_titles…". A code state's title is asserted on its
/// expression (<c>…/code/civil/20180401/fr</c>), the catalogue states no <c>dateDocument</c> for a code state, and the title's
/// document date fell back to the applicability date of each article it was reached through: one title made a row per
/// article date, all under one key (run 15: 2,217 rows, every one a code's; <see cref="LuxembourgIndexNineBuildRun15Probe"/>).
/// An article's own date is never a title's document date now, so the title is one row with no document date.
/// </summary>
/// <remarks>
/// LEGAL CARE. The body is synthetic (<see cref="LuxembourgRederivationTests.SyntheticConsolidation"/>'s identification and
/// articles in the publisher's shape, with invented wording); the title is invented.
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class LuxembourgTitleDocumentDateTests
{
    private const string Title = "Loi synthétique du 1er janvier 2026 portant exemple";
    private const string TitleShort = "Loi synthétique";

    /// <summary>
    /// The derive's own path: the consolidation acquired with a title and a short title on its expression and two articles
    /// that state two dates of their own, re-derived under Release 1, built into the first mount, written and verified. With
    /// the article-date fallback the build refused (the work_titles key twice); now each title is one row, with no document
    /// date, and the articles keep their own dates.
    /// </summary>
    [TestMethod]
    public async Task ATitleReachedThroughArticlesOfTwoDatesIsOneRowWithNoDocumentDate()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var (europe, acquired) = await V3FirstMountBuildTests.AcquireAsync(
            store, V3FirstMountBuildTests.CheckoutRoot(), TwoDatedArticles());
        Assert.IsTrue(europe.Delivered, europe.Detail);
        Assert.IsTrue(acquired.Delivered, acquired.Detail);
        var titled = WithTitles(acquired);

        var rederived = await LuxembourgRederivation.ApplyAsync(store, titled, LuxembourgRederivationTests.Release1(), CancellationToken.None);
        CollectionAssert.AreEqual(
            new[] { "2026-01-15", "2026-02-01" },
            rederived.AknLegalContent!.Outcomes.Select(static outcome => outcome.Article!.Coordinate.PublisherApplicability).ToArray());

        var clock = new V3OfflineMount.BuildClock(new DateTimeOffset(2026, 10, 12, 18, 17, 5, TimeSpan.Zero));
        using var build = await new V3FirstMountBuild(store, clock).RunAsync(europe, rederived, CancellationToken.None);
        Assert.IsTrue(build.Delivered, $"{build.Refusal}: {build.Detail}");

        var directory = Path.Combine(Path.GetTempPath(), "lex-v3-title-dates-mount-" + Guid.NewGuid().ToString("N"));
        try
        {
            await V3CorpusMountWriter.WriteAsync(build, directory, null, CancellationToken.None, clock.GetUtcNow());
            var verification = await V3CorpusMountWriter.VerifyAsync(directory, CancellationToken.None);
            Assert.IsTrue(verification.Verified, verification.Detail);

            var titles = new List<string>();
            var articles = new List<string>();
            using (var connection = LuxembourgIndexBuilder.Open(
                       Path.Combine(directory, V3FirstMountBuildResult.LuxembourgIndexFileName), Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly))
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT expression_iri, title_kind, title, coalesce(document_date, 'null') FROM work_titles ORDER BY title_kind";
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        titles.Add($"{reader.GetString(0)} {reader.GetString(1)} {reader.GetString(2)} {reader.GetString(3)}");
                    }
                }

                command.CommandText = "SELECT publisher_id, applicability_date FROM articles ORDER BY document_position";
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        articles.Add($"{reader.GetString(0)}={reader.GetString(1)}");
                    }
                }
            }

            const string Expression = LuxembourgFirstMountAcquisitionTests.ConsolidationExpression;
            CollectionAssert.AreEqual(
                new[] { $"{Expression} title {Title} null", $"{Expression} title_short {TitleShort} null" },
                titles.ToArray(),
                "one row per title, whatever its articles' own dates");
            CollectionAssert.AreEqual(new[] { "art_1=2026-01-15", "art_2=2026-02-01" }, articles.ToArray());
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    /// <summary>The synthetic consolidation with two articles stating two dates of their own, as a code state's do.</summary>
    private static byte[] TwoDatedArticles()
    {
        var synthetic = Encoding.UTF8.GetString(LuxembourgRederivationTests.SyntheticConsolidation());
        var body = synthetic.IndexOf("<body>", StringComparison.Ordinal);
        var end = synthetic.IndexOf("</body>", StringComparison.Ordinal);
        Assert.IsTrue(body > 0 && end > body);
        const string articles =
            "<body>\n"
            + "<article id=\"art_1\"><scl:JOLUXWork><scl:JOLUXLegalResource>"
            + "<scl:jolux scl:name=\"dateApplicability\">2026-01-15</scl:jolux></scl:JOLUXLegalResource></scl:JOLUXWork>"
            + "<num>Art. 1er.</num><content><p>Texte synthétique du premier article.</p></content></article>\n"
            + "<article id=\"art_2\"><scl:JOLUXWork><scl:JOLUXLegalResource>"
            + "<scl:jolux scl:name=\"dateApplicability\">2026-02-01</scl:jolux></scl:JOLUXLegalResource></scl:JOLUXWork>"
            + "<num>Art. 2.</num><content><p>Texte synthétique du deuxième article.</p></content></article>\n";
        return Encoding.UTF8.GetBytes(synthetic[..body] + articles + synthetic[end..]);
    }

    /// <summary>
    /// The acquisition with the catalogue's title and short title of the consolidation's expression among its typed assertions,
    /// as run 15's catalogue states a code state's (on <c>…/fr</c>, a French literal, from the observation that read it).
    /// </summary>
    private static LuxembourgFirstMountAcquisitionResult WithTitles(LuxembourgFirstMountAcquisitionResult acquired)
    {
        var run = acquired.Run!;
        var evidence = run.TypedAssertions[0].FactDisposition.EvidenceRef;
        LuxembourgTypedAssertion Literal(LuxembourgAssertionPredicate predicate, string local, string value) => new(
            new LuxembourgObservedAssertion(
                LuxembourgFirstMountAcquisitionTests.ConsolidationExpression,
                "http://data.legilux.public.lu/resource/ontology/jolux#" + local,
                LuxembourgAssertionObjectKind.Literal,
                value,
                "http://www.w3.org/1999/02/22-rdf-syntax-ns#langString",
                "fr",
                evidence),
            new LuxembourgAssertionFactDisposition(predicate, LuxembourgAssertionFactKind.ExpressionLanguageOrTitle, evidence),
            null,
            null);
        var redelivered = LuxembourgQueryExecutionResult.Delivered(
            run.Topology, run.FamilyOutcomes, run.RelationFamilyAcquisitions, run.ResolvedRelations,
            run.LocalInboundRelations,
            [.. run.TypedAssertions, Literal(LuxembourgAssertionPredicate.Title, "title", Title),
                Literal(LuxembourgAssertionPredicate.TitleShort, "titleShort", TitleShort)],
            run.ResourceObservationSubjects, run.ResourceObservationExclusions, run.ScopeManifestReceipt!,
            run.ScopeManifestCanonicalSha256!, run.DocumentAcquisitionOutcomesByOrdinal!, run.CorpusRecordSetRef!,
            run.CorpusRecordSetReceipt!, run.CorpusRecordSet!, run.ObservedObjectIdentitySetRef!,
            run.ObservedObjectIdentitySetReceipt!, run.ObservedObjectIdentitySet!, run.HeldBodyDerivationPopulation!,
            run.GazetteBodySetsByOrdinal!, run.GazetteListingFetchRefusalsByOrdinal!,
            run.GazetteListingsWithContradictoryLegalValueByOrdinal!, run.PopulationLedger!);
        // The run's retained phase checkpoints, which the re-derivation names.
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
