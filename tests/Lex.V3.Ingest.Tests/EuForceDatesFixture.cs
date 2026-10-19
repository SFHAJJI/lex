namespace Lex.V3.Ingest.Tests;

/// <summary>
/// Family A's rows for an act's dated statements (the plan's item 2.1): reified <c>owl:Axiom</c> statements on a CDM date
/// property of the act's work, in the shape EU run 16's custody holds them (the type-of-date carrier and the comment as
/// literals in the publisher's <c>{CODE|IRI}</c> form, the code escaped as one IRI path segment). The statement IRIs are
/// this fixture's own; the GDPR's dates, codes and notes are the ones run 16 holds for 32016R0679 (publisher metadata, no
/// text).
/// </summary>
internal static class EuForceDatesFixture
{
    internal const string Cdm = "http://publications.europa.eu/ontology/cdm#";
    internal const string EntryIntoForce = Cdm + "resource_legal_date_entry-into-force";
    internal const string Deadline = Cdm + "resource_legal_date_deadline";
    internal const string EndOfValidity = Cdm + "resource_legal_date_end-of-validity";
    internal const string Signature = Cdm + "resource_legal_date_signature";
    internal const string XsdDate = "http://www.w3.org/2001/XMLSchema#date";
    private const string Owl = "http://www.w3.org/2002/07/owl#";
    private const string RdfType = "http://www.w3.org/1999/02/22-rdf-syntax-ns#type";
    private const string Annotation = "http://publications.europa.eu/ontology/annotation#";
    private const string Fd335 = "http://publications.europa.eu/resource/authority/fd_335/";

    /// <summary>One dated statement: its own IRI, its date property, its value and datatype, its code and its note.</summary>
    internal sealed record Statement(string Axiom, string Predicate, string Value, string? Code, string? Note, string Datatype = XsdDate);

    /// <summary>The publisher's carrier of one code, <c>{CODE|IRI}</c>, the code escaped as one path segment.</summary>
    internal static string Carrier(string code) => "{" + code + "|" + Fd335 + Uri.EscapeDataString(code) + "}";

    /// <summary>A statement IRI of this fixture's, skolemised as the publisher's are.</summary>
    internal static string AxiomIri(string name) => "http://publications.europa.eu/.well-known/genid/lex-force-dates/" + name;

    /// <summary>The GDPR's three dated statements in run 16: its entry into force, its application and its deadline.</summary>
    internal static Statement[] Gdpr() =>
    [
        new(AxiomIri("gdpr-ev"), EntryIntoForce, "2016-05-24", "EV", $"{Carrier("DATPUB")} +20 {Carrier("V")} {Carrier("ART")} 99"),
        new(AxiomIri("gdpr-ma"), EntryIntoForce, "2018-05-25", "MA", $"{Carrier("V")} {Carrier("ART")} 99"),
        new(AxiomIri("gdpr-deadline"), Deadline, "2020-05-25", null, $"{Carrier("AU+TARD")} {Carrier("V")} {Carrier("ART")} 97"),
    ];

    /// <summary>Family A's rows for <paramref name="statements"/> on <paramref name="parentIri"/>, in the page's own cursor order.</summary>
    internal static string[] Rows(string parentIri, IEnumerable<Statement> statements) =>
    [
        .. statements
            .SelectMany(statement => Terms(parentIri, statement).Select(term => (statement.Axiom, Term: term)))
            .OrderBy(static row => row.Axiom, StringComparer.Ordinal)
            .ThenBy(static row => row.Term.Predicate, StringComparer.Ordinal)
            .Select(row => EuAcquisitionTestFixture.ReifiedAxiomFactsRow(
                parentIri, row.Axiom, row.Term.Predicate, row.Term.Value, row.Term.IsIri, row.Term.Datatype)),
    ];

    /// <summary>Family A's script for one work's statements, or its typed absence when there are none.</summary>
    internal static EuAcquisitionTestFixture.FamilyScript Script(string parentIri, IReadOnlyCollection<Statement> statements) =>
        statements.Count == 0
            ? EuAcquisitionTestFixture.AxiomAbsenceScriptFor(parentIri)
            : EuAcquisitionTestFixture.AxiomScriptFrom(Rows(parentIri, statements));

    private static IEnumerable<(string Predicate, string Value, bool IsIri, string Datatype)> Terms(string parentIri, Statement statement)
    {
        yield return (Owl + "annotatedSource", parentIri, true, "");
        yield return (Owl + "annotatedProperty", statement.Predicate, true, "");
        yield return (RdfType, Owl + "Axiom", true, "");
        yield return (Owl + "annotatedTarget", statement.Value, false, statement.Datatype);
        if (statement.Code is not null) yield return (Annotation + "type_of_date", Carrier(statement.Code), false, "");
        if (statement.Note is not null) yield return (Annotation + "comment_on_date", statement.Note, false, "");
    }
}
