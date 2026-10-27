using System.Text.Json;
using Lex.V3.Ingest.Luxembourg;
using Microsoft.Data.Sqlite;
using static Lex.V3.Ingest.Tests.LuxembourgIndexBuilderTests;
using static Lex.V3.Ingest.Tests.LuxembourgIndexCatalogueStatesTests;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// Index 10's publisher relation records (the Release 3 scope's item 2; R29, R72, R21): the projection of the run's resolved
/// relations into typed rows (<see cref="LuxembourgIndexBuilder.ProjectPublisherRelations"/>), each relation type in the
/// publisher's own direction with its evidence and the acting act's dates from the facts, the impact records from their parts,
/// the families' states; the index written and read back through the strict reader, twice to the same bytes; a row whose dates
/// its facts do not state refused; and an index 9 (index 10 less its relation tables) still read as index 9, claiming none.
/// Every value is synthetic.
/// </summary>
[TestClass]
public sealed class LuxembourgPublisherRelationsTests
{
    private const string Fra = "http://publications.europa.eu/resource/authority/language/FRA";
    private static readonly string Amended = W("loi/2010/01/01/n1/jo");
    private static readonly string Amending = W("loi/2016/06/01/n1/jo");
    private static readonly string Temporary = W("loi/2020/05/12/a385/jo");
    private static readonly string Impact = W("loi/2016/06/01/n1/legal-analysis/LegalResourceImpact/1");

    /// <summary>The harness's held act, one act of each relation to it and from it, an impact record, and the families acquired.</summary>
    internal static Harness RelationsFixture()
    {
        var h = new Harness();
        h.State(Amended, entryIntoForce: "2010-01-05", publication: "2010-01-03", memberOf: W("loi/2010/01/01/n1"), expressions: ("fr", Fra))
            .Held(Amended, ["art_1", "art_2"], inFileDate: "2010-01-05");
        h.State(W("loi/2010/01/01/n1/consolide/20150101"), "2015-01-01", publication: "2015-01-10", memberOf: W("loi/2010/01/01/n1"), expressions: ("fr", Fra));
        h.Fact(Amending, "publicationDate", "2016-06-03").Fact(Amending, "dateEntryInForce", "2016-06-07");
        h.Fact(Amending + "/fr", "title", "Loi synthétique du 1er juin 2016 modifiant une loi");
        h.Fact(Temporary, "publicationDate", "2020-05-12");
        h.Relation(Amending, "modifies", Amended)
            .Relation(Amended, "modifies", W("loi/2000/01/01/n1/jo"))
            .Relation(W("rgd/2017/01/01/n1/jo"), "repeals", Amended)
            .Relation(Amended, "modifiedTempBy", Temporary)
            .Relation(Amended, "rectifies", W("loi/2009/12/01/n1/jo"))
            .Relation(Amended, "cites", W("loi/2000/01/01/n1/jo"))
            .Relation(Amended, "basedOn", W("loi/2000/01/01/n1/jo"))
            .Relation(Impact, "impactFromLegalResource", Amending)
            .Relation(Impact, "impactToLegalResource", Amended)
            .Relation(Impact, "impactToExpression", Amended + "/fr")
            .Relation(Impact, "legalResourceImpactHasType", "http://data.legilux.public.lu/resource/authority/impact-type/1")
            .Relation(Impact, "impactConsolidatedBy", W("loi/2010/01/01/n1/consolide/20170101"))
            .Relation(Impact, "impactConsolidatedBy", W("loi/2010/01/01/n1/consolide/20180101"))
            .RelationFamilies();
        return h;
    }

    [TestMethod]
    public void EachRelationTypeIsProjectedInThePublishersDirectionWithItsEvidenceAndTheActingActsDates()
    {
        var rows = RelationsFixture().PublisherRows;

        string Row(LuxembourgIndexBuilder.PublisherRelationRow row) =>
            $"{row.EdgeType} {row.SourcePredicate} {row.SubjectIri[Eli.Length..]} -> {row.ObjectIri[Eli.Length..]} acting {row.ActingIri[Eli.Length..]} " +
            $"published {row.ActingPublicationDate ?? "-"} entry {row.ActingEntryIntoForce ?? "-"}";
        CollectionAssert.AreEqual(
            new[]
            {
                "modified_temporarily_by jolux:modifiedTempBy loi/2010/01/01/n1/jo -> loi/2020/05/12/a385/jo acting loi/2020/05/12/a385/jo published 2020-05-12 entry -",
                "modifies jolux:modifies loi/2010/01/01/n1/jo -> loi/2000/01/01/n1/jo acting loi/2010/01/01/n1/jo published 2010-01-03 entry 2010-01-05",
                "modifies jolux:modifies loi/2016/06/01/n1/jo -> loi/2010/01/01/n1/jo acting loi/2016/06/01/n1/jo published 2016-06-03 entry 2016-06-07",
                "rectifies jolux:rectifies loi/2010/01/01/n1/jo -> loi/2009/12/01/n1/jo acting loi/2010/01/01/n1/jo published 2010-01-03 entry 2010-01-05",
                "repeals jolux:repeals rgd/2017/01/01/n1/jo -> loi/2010/01/01/n1/jo acting rgd/2017/01/01/n1/jo published - entry -",
            },
            rows.Relations.Select(Row).ToArray(),
            "the four act-level types, each in the publisher's direction (modifiedTempBy's acting act is its object); cites and basedOn are not projected");
        Assert.IsTrue(rows.Relations.All(static row => row.EvidenceSha256 == Harness.RelationEvidence), "each row keeps the digest of the observation it was read from");

        var impact = rows.Impacts.Single();
        Assert.AreEqual(Impact, impact.ImpactIri);
        Assert.AreEqual((Amending, Amended, Amended + "/fr"), (impact.FromLegalResourceIri, impact.ToLegalResourceIri, impact.ToExpressionIri));
        Assert.AreEqual("http://data.legilux.public.lu/resource/authority/impact-type/1", impact.ImpactTypeIri);
        Assert.IsNull(impact.ConsolidatedByIri, "a part the publisher gives twice is not chosen between");
        Assert.AreEqual("[\"jolux:impactConsolidatedBy\"]", impact.AmbiguousPartsJson, "and is named ambiguous");
        Assert.AreEqual(("2016-06-03", "2016-06-07"), (impact.ActingPublicationDate, impact.ActingEntryIntoForce));

        Assert.HasCount(10, rows.Families);
        Assert.IsTrue(rows.Families.All(static family => family.State == "acquired_complete" &&
            family.FamilyKeysJson == "[\"legislative-code-g\",\"legislative-loi-g\",\"legislative-rgd-g\"]" && family.Reason is null));
        CollectionAssert.Contains(rows.Families.Select(static family => family.Predicate).ToArray(), "jolux:modifiedTempBy");
    }

    [TestMethod]
    public void ARelationOrAnImpactReadFromTwoObservationsIsRefusedNotChosenBetween()
    {
        LuxembourgIndexBuilder.ProjectionRelation Relation(string subject, string predicate, string target, char evidence) =>
            new(subject, LuxembourgIndexBuilder.JoluxNamespace + predicate, target, new string(evidence, 64));
        var twice = Assert.ThrowsExactly<InvalidDataException>(() => LuxembourgIndexBuilder.ProjectPublisherRelations(
            [Relation(Amending, "modifies", Amended, '1'), Relation(Amending, "modifies", Amended, '2')], [], []));
        StringAssert.Contains(twice.Message, "was read from two observations");
        var parts = Assert.ThrowsExactly<InvalidDataException>(() => LuxembourgIndexBuilder.ProjectPublisherRelations(
            [Relation(Impact, "impactFromLegalResource", Amending, '1'), Relation(Impact, "impactToLegalResource", Amended, '2')], [], []));
        StringAssert.Contains(parts.Message, "more than one observation");

        // The same row twice, from one observation, is one row.
        var once = LuxembourgIndexBuilder.ProjectPublisherRelations(
            [Relation(Amending, "modifies", Amended, '1'), Relation(Amending, "modifies", Amended, '1')], [], []);
        Assert.HasCount(1, once.Relations);
        Assert.IsTrue(once.Relations.All(static row => row.ActingPublicationDate is null), "an act whose facts state no date has none");
    }

    [TestMethod]
    public void TheIndexHoldsTheRecordsTheReaderReadsThemAndASecondBuildIsTheSameBytes()
    {
        var harness = RelationsFixture();
        var built = harness.Build();
        var again = RelationsFixture().Build();
        Assert.AreEqual(built.IndexRef, again.IndexRef, "the second build of the same rows is the same bytes");
        CollectionAssert.AreEqual(built.IndexBytes.ToArray(), again.IndexBytes.ToArray());
        CollectionAssert.AreEqual(built.CapabilityManifestBytes.ToArray(), again.CapabilityManifestBytes.ToArray());

        using var reader = LuxembourgIndexReader.OpenAndVerify(built.IndexRef, built.IndexBytes.Span, Harness.CorpusRef, built.CapabilityManifest);
        Assert.AreEqual(LuxembourgIndexBuilder.Schema, reader.SchemaIdentity);
        Assert.AreEqual("lex-v3-luxembourg-index/10", reader.SchemaIdentity);
        Assert.IsTrue(reader.RecordsPublisherRelations);
        Assert.IsTrue(reader.RecordsCatalogueStates, "index 10 holds index 9's states");

        var relations = reader.ResolvePublisherRelations([Amended, W("loi/2010/01/01/n1")]);
        CollectionAssert.AreEqual(
            new[] { "modified_temporarily_by", "modifies", "modifies", "rectifies", "repeals" },
            relations.Select(static relation => relation.EdgeType).ToArray(),
            "every relation whose subject or object is the act, each once");
        var amending = relations.Single(relation => relation.SubjectIri == Amending);
        Assert.AreEqual((Amending, Amended, "2016-06-03", "2016-06-07"),
            (amending.ActingIri, amending.ActedOnIri, amending.ActingPublicationDate, amending.ActingEntryIntoForce));
        var temporary = relations.Single(static relation => relation.EdgeType == "modified_temporarily_by");
        Assert.AreEqual((Temporary, Amended), (temporary.ActingIri, temporary.ActedOnIri));
        Assert.HasCount(1, reader.ResolvePublisherImpacts([Amended]));
        Assert.HasCount(1, reader.ResolvePublisherImpacts([Amending]), "an impact record is found by its acting act too");
        Assert.IsEmpty(reader.ResolvePublisherRelations([W("loi/1999/01/01/n1/jo")]));
        Assert.HasCount(10, reader.ResolveRelationFamilies());
        Assert.IsTrue(reader.ResolveRelationFamilies().All(static family => family.FamilyKeys.Count == 3));
    }

    [TestMethod]
    public void ARowWhoseDatesItsFactsDoNotStateIsRefusedEvenWithItsStampResealed()
    {
        var built = RelationsFixture().Build();
        foreach (var tamper in new[]
                 {
                     "UPDATE publisher_relations SET acting_publication_date='2016-06-04' WHERE subject_iri LIKE '%/2016/06/01/n1/jo'",
                     "UPDATE publisher_relations SET acting_entry_into_force='2017-01-01' WHERE edge_type='repeals'",
                     "UPDATE publisher_impacts SET acting_publication_date=NULL",
                 })
        {
            var tampered = MutateDatabase(built.IndexBytes.Span, connection =>
            {
                Execute(connection, tamper);
                Execute(connection, "UPDATE stamp SET logical_rows_sha256=$p0 WHERE stamp_id=1", ("$p0", LogicalRowsOf(connection)));
            });
            var reference = IndexRefOf(tampered);
            var exception = Assert.ThrowsExactly<InvalidDataException>(() => LuxembourgIndexReader.OpenAndVerify(
                reference, tampered, Harness.CorpusRef, RebindManifest(built.CapabilityManifest, reference.Sha256)), tamper);
            StringAssert.Contains(exception.Message, "carries dates its facts do not state", tamper);
        }

        // An unsealed change is refused by the stamp, as every table's is.
        var unsealed = MutateDatabase(built.IndexBytes.Span, "DELETE FROM relation_families WHERE predicate='jolux:repeals'");
        var unsealedRef = IndexRefOf(unsealed);
        var stamped = Assert.ThrowsExactly<InvalidDataException>(() => LuxembourgIndexReader.OpenAndVerify(
            unsealedRef, unsealed, Harness.CorpusRef, RebindManifest(built.CapabilityManifest, unsealedRef.Sha256)));
        StringAssert.Contains(stamped.Message, "logical rows do not match their stamp");
    }

    [TestMethod]
    public void AnIndexNineIsStillReadAsIndexNineAndClaimsNoRelationRecord()
    {
        var built = RelationsFixture().Build();
        var nine = Schema9Of(built.IndexBytes.Span);
        var reference = IndexRefOf(nine);
        using var reader = LuxembourgIndexReader.OpenAndVerify(reference, nine, Harness.CorpusRef, RebindManifest(built.CapabilityManifest, reference.Sha256));
        Assert.AreEqual(LuxembourgIndexBuilder.LegacySchema9, reader.SchemaIdentity);
        Assert.IsTrue(reader.RecordsCatalogueStates);
        Assert.IsFalse(reader.RecordsPublisherRelations);
        Assert.IsEmpty(reader.ResolvePublisherRelations([Amended]));
        Assert.IsEmpty(reader.ResolvePublisherImpacts([Amended]));
        Assert.IsEmpty(reader.ResolveRelationFamilies());

        // Index 9's tables stamped as index 10, or index 10's tables under user_version 9, are refused.
        var stampedTen = MutateDatabase(nine, connection =>
            Execute(connection, "UPDATE stamp SET schema_identity=$p0 WHERE stamp_id=1", ("$p0", LuxembourgIndexBuilder.Schema)));
        var stampedRef = IndexRefOf(stampedTen);
        Assert.ThrowsExactly<InvalidDataException>(() => LuxembourgIndexReader.OpenAndVerify(
            stampedRef, stampedTen, Harness.CorpusRef, RebindManifest(built.CapabilityManifest, stampedRef.Sha256)));
        var tablesTen = MutateDatabase(built.IndexBytes.Span, "PRAGMA user_version=9");
        var tablesRef = IndexRefOf(tablesTen);
        var refused = Assert.ThrowsExactly<InvalidDataException>(() => LuxembourgIndexReader.OpenAndVerify(
            tablesRef, tablesTen, Harness.CorpusRef, RebindManifest(built.CapabilityManifest, tablesRef.Sha256)));
        StringAssert.Contains(refused.Message, "schema differs from the exact terminal schema");
    }

    /// <summary>
    /// The index's plans for the reader's relation queries: the two lookups walk the list and find each IRI by an index, never
    /// scanning a relation table; the open-time check finds each acting act's facts by the fact table's key, never scanning it.
    /// </summary>
    [TestMethod]
    public void TheRelationQueriesLookEachIriUpByAnIndexAndScanNoTable()
    {
        var built = RelationsFixture().Build();
        var path = Path.Combine(Path.GetTempPath(), $"lex-v3-lu-relations-plan-{Guid.NewGuid():N}.sqlite");
        try
        {
            File.WriteAllBytes(path, built.IndexBytes.ToArray());
            using var connection = LuxembourgIndexBuilder.Open(path, SqliteOpenMode.ReadOnly);
            string[] Plan(string sql)
            {
                using var command = connection.CreateCommand();
                command.CommandText = "EXPLAIN QUERY PLAN " + sql;
                command.Parameters.AddWithValue("$iris", JsonSerializer.Serialize(new[] { Amended }));
                using var reader = command.ExecuteReader();
                var plan = new List<string>();
                while (reader.Read()) plan.Add(reader.GetString(3));
                return plan.ToArray();
            }

            foreach (var (name, sql) in new[]
                     {
                         ("PublisherRelationsOf", LuxembourgIndexQueries.PublisherRelationsOf),
                         ("PublisherImpactsOf", LuxembourgIndexQueries.PublisherImpactsOf),
                     })
            {
                var plan = Plan(sql);
                Assert.IsFalse(plan.Any(static step => step.StartsWith("SCAN ", StringComparison.Ordinal) && !step.StartsWith("SCAN t", StringComparison.Ordinal)),
                    $"{name} scans a table: {string.Join(" | ", plan)}");
                Assert.AreEqual(2, plan.Count(static step => step.StartsWith("SEARCH ", StringComparison.Ordinal) && step.Contains("USING INDEX publisher_", StringComparison.Ordinal)),
                    $"{name} looks each IRI up by the two indexes: {string.Join(" | ", plan)}");
            }

            var facts = Plan(LuxembourgIndexQueries.ActingFacts);
            Assert.IsTrue(facts.Any(static step => step.StartsWith("SEARCH f USING INDEX", StringComparison.Ordinal)), string.Join(" | ", facts));
            Assert.IsFalse(facts.Any(static step => step.StartsWith("SCAN f", StringComparison.Ordinal)), string.Join(" | ", facts));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            LuxembourgIndexBuilder.DeleteDatabase(path);
        }
    }

    /// <summary>The reader runs the query text the plan test asks, once each, from its relation file.</summary>
    [TestMethod]
    public void TheReaderRunsTheRelationQueriesThisTestAsks()
    {
        var source = File.ReadAllText(Path.Combine(
            V3FirstMountBuildTests.CheckoutRoot(), "src", "Lex.V3.Ingest", "Luxembourg", "LuxembourgIndexReader.PublisherRelations.cs"));
        foreach (var name in new[] { "PublisherRelationsOf", "PublisherImpactsOf", "ActingFacts" })
        {
            Assert.AreEqual(1, source.Split($"command.CommandText = LuxembourgIndexQueries.{name};").Length - 1, name);
        }
    }
}
