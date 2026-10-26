using Lex.V3.Contracts;
using Lex.V3.Contracts.Facts;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Ingest.Europe;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The EU index's force dates (the plan's item 2.1; R79's E1, R21): each reified date statement the run decoded becomes one
/// row, with its role read from the date property and the publisher's code alone, the code and note kept word for word, and
/// nothing chosen between two dates of one role.
/// </summary>
[TestClass]
public sealed class EuropeIndexForceDatesTests
{
    private static readonly string Work = EuAxiomWiringHarness.SeedRoot("32016R0679");

    private static EuDateAxiomBinding Binding(string axiom, string predicate, string value, string? code, string? label, string? note,
        string work = "", string observation = "urn:uuid:00000000-0000-0000-0000-00000000000a")
    {
        work = work.Length == 0 ? Work : work;
        var qualifiers = new List<AxiomQualifier>
        {
            new("http://www.w3.org/2002/07/owl#annotatedProperty", predicate),
            new("http://www.w3.org/2002/07/owl#annotatedSource", work),
            new("http://www.w3.org/2002/07/owl#annotatedTarget", value),
            new("http://www.w3.org/1999/02/22-rdf-syntax-ns#type", "http://www.w3.org/2002/07/owl#Axiom"),
        };
        if (code is not null) qualifiers.Add(new("http://publications.europa.eu/ontology/annotation#type_of_date", EuForceDatesFixture.Carrier(code)));
        if (note is not null) qualifiers.Add(new("http://publications.europa.eu/ontology/annotation#comment_on_date", note));
        return EuDateAxiomBinding.Create(
            new OfficialIdentitySet(PublisherId.EuEurLex, [new OfficialIdentifier(FactsIdentifierFamily.CellarWorkUri, work)]),
            value, PublisherDate.Date, DatePrecision.YearMonthDay, predicate, new QualifiedAxiom(EuForceDatesFixture.AxiomIri(axiom), qualifiers),
            code, label, note, EuReifiedAxiomDecode.ParsedByAuthority, observation);
    }

    private static EuropeIndexState[] Census(params string[] works) =>
        [.. works.Select(work => new EuropeIndexState(EuropeIndexBuilder.StateIdentity("32016R0679", Work, work), "32016R0679", Work, work,
            null, work == Work ? EuropeIndexStateDateStatus.OriginalWording : EuropeIndexStateDateStatus.ObservationMissing, null, "{}"))];

    [TestMethod]
    public void EachRoleIsReadFromTheDatePropertyAndThePublishersCodeAlone()
    {
        var rows = EuropeIndexBuilder.ProjectForceDates(
        [
            Binding("ev", EuForceDatesFixture.EntryIntoForce, "2016-05-24", "EV", "Entry into force", null),
            Binding("ma", EuForceDatesFixture.EntryIntoForce, "2018-05-25", "MA", "Application", null),
            Binding("part", EuForceDatesFixture.EntryIntoForce, "2008-07-11", "MA/PART", null, "x"),
            Binding("none", EuForceDatesFixture.EntryIntoForce, "2009-12-01", null, null, null),
            Binding("deadline", EuForceDatesFixture.Deadline, "2020-05-25", null, null, "{AU+TARD|x}"),
            Binding("latest", EuForceDatesFixture.Deadline, "2021-05-25", "AU+TARD", "At the latest", null),
            Binding("end", EuForceDatesFixture.EndOfValidity, "9999-12-31", null, null, null),
            Binding("signed", EuForceDatesFixture.Signature, "2016-04-27", null, null, "Strasbourg"),
        ], Census(Work));
        var byAxiom = rows.ToDictionary(static row => row.AxiomIri[(row.AxiomIri.LastIndexOf('/') + 1)..], StringComparer.Ordinal);
        Assert.AreEqual(EuropeForceDateRoles.EntryIntoForce, byAxiom["ev"].Role);
        Assert.AreEqual(EuropeForceDateRoles.Application, byAxiom["ma"].Role);
        Assert.AreEqual(EuropeForceDateRoles.NotPinned, byAxiom["part"].Role, "a code the table does not pin is given no role");
        Assert.AreEqual("MA/PART", byAxiom["part"].QualifierCode, "and keeps its code");
        Assert.AreEqual(EuropeForceDateRoles.NotPinned, byAxiom["none"].Role);
        Assert.AreEqual(EuropeForceDateRoles.Deadline, byAxiom["deadline"].Role, "the deadline property is the publisher's deadline");
        Assert.AreEqual("role_not_stated_by_publisher", byAxiom["deadline"].FactRole, "while the contract's own reading stays its own");
        Assert.AreEqual("{AU+TARD|x}", byAxiom["deadline"].PublisherComment);
        Assert.AreEqual(EuropeForceDateRoles.Deadline, byAxiom["latest"].Role);
        Assert.AreEqual("publisher_deadline", byAxiom["latest"].FactRole);
        Assert.AreEqual(EuropeForceDateRoles.EndOfValidity, byAxiom["end"].Role);
        Assert.IsTrue(byAxiom["end"].OpenEnded, "9999-12-31 is the publisher's open end, recorded as one");
        Assert.AreEqual(EuropeForceDateRoles.Signature, byAxiom["signed"].Role);
        CollectionAssert.AreEqual(rows.Select(static row => row.DateIdentitySha256).Order(StringComparer.Ordinal).ToArray(),
            rows.Select(static row => row.DateIdentitySha256).ToArray(), "rows are in identity order");
        EuropeIndexBuilder.ValidateForceDateEvidence(rows, Census(Work));
    }

    [TestMethod]
    public void OneStatementDeliveredTwiceIsOneRowAndTwoStatementsOfOneDateAreTwo()
    {
        var once = Binding("ev", EuForceDatesFixture.EntryIntoForce, "2009-12-01", "EV", "Entry into force", null);
        var twice = Binding("ev", EuForceDatesFixture.EntryIntoForce, "2009-12-01", "EV", "Entry into force", null);
        var other = Binding("ev-2", EuForceDatesFixture.EntryIntoForce, "2009-12-01", "EV", "Entry into force", null);
        Assert.HasCount(1, EuropeIndexBuilder.ProjectForceDates([once, twice], Census(Work)), "a statement two seeds' closures delivered is one row");
        Assert.HasCount(2, EuropeIndexBuilder.ProjectForceDates([once, other], Census(Work)),
            "two statements of one date are two rows, as 12016P/TXT's two entry-into-force statements are in run 16");
    }

    [TestMethod]
    public void AStatementOnAWorkTheCensusDoesNotHoldOrDeliveredFromAnotherBatchWithOtherValuesIsRefused()
    {
        var stranger = Binding("ev", EuForceDatesFixture.EntryIntoForce, "2016-05-24", "EV", "Entry into force", null,
            work: "http://publications.europa.eu/resource/cellar/00000000-0000-0000-0000-000000000000");
        Assert.ThrowsExactly<InvalidDataException>(() => EuropeIndexBuilder.ProjectForceDates([stranger], Census(Work)));
        var first = Binding("ev", EuForceDatesFixture.EntryIntoForce, "2016-05-24", "EV", "Entry into force", null);
        var relabelled = Binding("ev", EuForceDatesFixture.EntryIntoForce, "2016-05-24", "EV", "Entry into force", null,
            observation: "urn:uuid:00000000-0000-0000-0000-00000000000b");
        Assert.ThrowsExactly<InvalidDataException>(() => EuropeIndexBuilder.ProjectForceDates([first, relabelled], Census(Work)),
            "one statement read from two batches names one coordinate, or it is refused");
    }

    [TestMethod]
    public void AStoredRowThatItsOwnStatementDoesNotSayIsRefused()
    {
        var rows = EuropeIndexBuilder.ProjectForceDates(
            [Binding("ev", EuForceDatesFixture.EntryIntoForce, "2016-05-24", "EV", "Entry into force", "{V|x} 99")], Census(Work));
        var row = rows.Single();
        foreach (var changed in new[]
                 {
                     row with { Role = EuropeForceDateRoles.Application },
                     row with { DateValue = "2016-05-25" },
                     row with { QualifierCode = "MA" },
                     row with { PublisherComment = "{V|x} 98" },
                     row with { OpenEnded = true },
                     row with { FactRole = "application_date" },
                     row with { QualifierLabel = null },
                 })
        {
            Assert.ThrowsExactly<InvalidDataException>(() => EuropeIndexBuilder.ValidateForceDateEvidence([changed], Census(Work)), changed.ToString());
        }

        Assert.ThrowsExactly<InvalidDataException>(() => EuropeIndexBuilder.ValidateForceDateEvidence(rows, Census()),
            "a date of a work the census does not hold");
    }

    [TestMethod]
    [DataRow("http://publications.europa.eu/ontology/cdm#resource_legal_date_end-of-validity", "end_of_validity")]
    [DataRow("http://publications.europa.eu/ontology/cdm#resource_legal_date_signature", "signature")]
    public void AnUnpinnedCodeOnTheEndOfValidityOrSignaturePropertyKeepsThePropertysRoleAndItsCode(string predicate, string role)
    {
        // The review of #987 (EU 2.1): the contract admits a code its table does not pin on these properties and types it as no
        // role stated; the property gives the row its role, and the code is kept, as on the entry-into-force property.
        var coded = Binding("coded", predicate, "2020-01-01", "PE", null, null);
        Assert.AreEqual(DateSemanticRole.RoleNotStatedByPublisher, coded.Fact.SemanticRole, "the contract admits it");
        var rows = EuropeIndexBuilder.ProjectForceDates([coded], Census(Work));
        Assert.AreEqual(role, rows.Single().Role);
        Assert.AreEqual("PE", rows.Single().QualifierCode);
        Assert.AreEqual("role_not_stated_by_publisher", rows.Single().FactRole);
        EuropeIndexBuilder.ValidateForceDateEvidence(rows, Census(Work));
    }

    [TestMethod]
    public void TheReaderReopensARowWhoseCommentTheDecodeDidNotReadAsALiteral()
    {
        // The review of #987 (EU 2.1): a comment_on_date delivered as an IRI (or a blank node) is retained among the statement's
        // properties, and the decode reads no note from it (PublisherComment null); the build's own read-back reopens the row.
        const string place = "http://publications.europa.eu/resource/authority/place/FRA_SXB";
        var qualifiers = new List<AxiomQualifier>
        {
            new("http://www.w3.org/2002/07/owl#annotatedProperty", EuForceDatesFixture.Signature),
            new("http://www.w3.org/2002/07/owl#annotatedSource", Work),
            new("http://www.w3.org/2002/07/owl#annotatedTarget", "2016-04-27"),
            new("http://www.w3.org/1999/02/22-rdf-syntax-ns#type", "http://www.w3.org/2002/07/owl#Axiom"),
            new("http://publications.europa.eu/ontology/annotation#comment_on_date", place),
        };
        // Exactly what the decode builds for a non-literal comment: the term retained, PublisherComment null.
        var binding = EuDateAxiomBinding.Create(
            new OfficialIdentitySet(PublisherId.EuEurLex, [new OfficialIdentifier(FactsIdentifierFamily.CellarWorkUri, Work)]),
            "2016-04-27", PublisherDate.Date, DatePrecision.YearMonthDay, EuForceDatesFixture.Signature,
            new QualifiedAxiom(EuForceDatesFixture.AxiomIri("iri-note"), qualifiers), null, null, null,
            EuReifiedAxiomDecode.ParsedByAuthority, "urn:uuid:00000000-0000-0000-0000-00000000000a");
        var rows = EuropeIndexBuilder.ProjectForceDates([binding], Census(Work));
        Assert.IsNull(rows.Single().PublisherComment, "no note is read from a comment that is not a literal");
        StringAssert.Contains(rows.Single().QualifiersJson, place, "the statement's comment is retained as delivered");
        EuropeIndexBuilder.ValidateForceDateEvidence(rows, Census(Work));

        // A note is still never anything but the retained comment.
        Assert.ThrowsExactly<InvalidDataException>(() => EuropeIndexBuilder.ValidateForceDateEvidence([rows.Single() with { PublisherComment = "Strasbourg" }], Census(Work)));
    }
}
