using System.Text;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The discovery instrument, checked before its answer is believed.
/// </summary>
/// <remarks>
/// <para>
/// THE RULE THIS EXISTS TO SATISFY IS ALREADY WRITTEN IN THIS REPOSITORY. The OpinionRequest count
/// canary states it: an answer from an instrument nobody checked is not evidence, because a zero
/// from a query that cannot count is indistinguishable from a zero from an empty class. The same
/// shape applies to a reader: a parser that silently returns nothing would make the live run report
/// "the publisher returned no dossiers" when the truth is that this code failed to read them.
/// </para>
/// <para>
/// WHY IT MATTERS MORE HERE THAN USUALLY. The owner decision authorizes the discovery-plus-acceptance
/// operation to run ONCE. A defect in the reader would spend that single attempt proving a bug in
/// test code rather than establishing anything about the publisher. The structural guards beside this
/// file prove the reader is WIRED to the retained payload; only these tests prove it READS it, and
/// treating the first as standing in for the second was my own gap, reported on #417 before it was
/// fixed.
/// </para>
/// <para>
/// Payloads below are SPARQL 1.1 Query Results JSON shaped as the endpoint returns them. They are
/// fixtures for the reader, and nothing here claims to know which dossiers the publisher holds.
/// </para>
/// </remarks>
[TestClass]
public sealed class EuProcedureEventDiscoveryInstrumentTests
{
    private const string FirstDossier =
        "http://publications.europa.eu/resource/cellar/11111111-2222-3333-4444-555555555555";

    private const string SecondDossier =
        "http://publications.europa.eu/resource/cellar/66666666-7777-8888-9999-aaaaaaaaaaaa";

    private const string ThirdDossier =
        "http://publications.europa.eu/resource/cellar/bbbbbbbb-cccc-dddd-eeee-ffffffffffff";

    /// <summary>Two bindings are read as two IRIs, in the order the publisher returned them.</summary>
    /// <remarks>
    /// Order is asserted rather than membership, because the two-window comparison is prefix
    /// equality: a reader that returned the right set in the wrong order would make a stably ordered
    /// result set look unstable and refuse a run that should have succeeded.
    /// </remarks>
    [TestMethod]
    public void TwoBindingsAreReadAsTwoIrisInDeliveryOrder()
    {
        var rows = EuProcedureEventLiveAcceptance.ParseDossiers(
            Payload(FirstDossier, SecondDossier));

        Assert.HasCount(2, rows);
        Assert.AreEqual(FirstDossier, rows[0], "the first delivered row stays first.");
        Assert.AreEqual(SecondDossier, rows[1], "and the second stays second.");
    }

    /// <summary>A wider window is read whole, so the comparison has something to compare.</summary>
    [TestMethod]
    public void AWiderWindowIsReadWhole()
    {
        var rows = EuProcedureEventLiveAcceptance.ParseDossiers(
            Payload(FirstDossier, SecondDossier, ThirdDossier));

        Assert.HasCount(3, rows);
        Assert.AreEqual(ThirdDossier, rows[2]);
    }

    /// <summary>A payload with no results section is read as empty rather than throwing.</summary>
    /// <remarks>
    /// Empty is the honest reading of a shape this reader does not recognise, and the run then
    /// refuses for want of two dossiers with its evidence already retained. A throw here would
    /// escape before the terminal index was written, which is the one outcome the retain-before-judge
    /// ordering exists to prevent.
    /// </remarks>
    [TestMethod]
    public void APayloadWithNoResultsSectionIsEmptyAndDoesNotThrow()
    {
        Assert.IsEmpty(EuProcedureEventLiveAcceptance.ParseDossiers(
            Encoding.UTF8.GetBytes("{\"head\":{\"vars\":[\"dossier\"]}}")));
        Assert.IsEmpty(EuProcedureEventLiveAcceptance.ParseDossiers(
            Encoding.UTF8.GetBytes("{\"head\":{\"vars\":[\"dossier\"]},\"results\":{}}")));
    }

    /// <summary>An empty binding set is read as empty.</summary>
    [TestMethod]
    public void NoBindingsIsEmpty() =>
        Assert.IsEmpty(EuProcedureEventLiveAcceptance.ParseDossiers(Payload()));

    /// <summary>
    /// Bindings carrying a different variable name yield nothing, never a wrong subject.
    /// </summary>
    /// <remarks>
    /// The variable name is part of the question this run asked. A reader that took the first value
    /// of whatever variable arrived would hand the producer an event IRI as though it were a dossier,
    /// and the producer would then honestly report that it found no events for it.
    /// </remarks>
    [TestMethod]
    public void ADifferentVariableNameYieldsNoSubjects()
    {
        var payload = Encoding.UTF8.GetBytes(
            "{\"head\":{\"vars\":[\"event\"]},\"results\":{\"bindings\":["
            + "{\"event\":{\"type\":\"uri\",\"value\":\"" + FirstDossier + "\"}}]}}");

        Assert.IsEmpty(
            EuProcedureEventLiveAcceptance.ParseDossiers(payload),
            "only the variable this run asked for may become a subject.");
    }

    /// <summary>An empty value is not a subject.</summary>
    [TestMethod]
    public void AnEmptyValueIsNotASubject()
    {
        var payload = Encoding.UTF8.GetBytes(
            "{\"head\":{\"vars\":[\"dossier\"]},\"results\":{\"bindings\":["
            + "{\"dossier\":{\"type\":\"uri\",\"value\":\"\"}},"
            + "{\"dossier\":{\"type\":\"uri\",\"value\":\"" + FirstDossier + "\"}}]}}");

        var rows = EuProcedureEventLiveAcceptance.ParseDossiers(payload);

        Assert.HasCount(1, rows, "the empty value is dropped, the real one kept.");
        Assert.AreEqual(FirstDossier, rows[0]);
    }

    /// <summary>The wider window agreeing on its shared prefix is agreement.</summary>
    [TestMethod]
    public void APrefixMatchIsAgreement() =>
        Assert.IsTrue(EuProcedureEventLiveAcceptance.WindowsAgree(
            [FirstDossier, SecondDossier],
            [FirstDossier, SecondDossier, ThirdDossier]));

    /// <summary>A different first row is disagreement, which is the defect this check exists for.</summary>
    /// <remarks>
    /// An unordered result set is the real risk: the publisher may answer a second identical
    /// question with different rows, and nothing about either answer would look wrong on its own.
    /// </remarks>
    [TestMethod]
    public void ADifferentRowIsDisagreement()
    {
        Assert.IsFalse(EuProcedureEventLiveAcceptance.WindowsAgree(
            [FirstDossier, SecondDossier],
            [FirstDossier, ThirdDossier, SecondDossier]));
        Assert.IsFalse(EuProcedureEventLiveAcceptance.WindowsAgree(
            [FirstDossier, SecondDossier],
            [ThirdDossier, FirstDossier, SecondDossier]),
            "the same set in a different order is not agreement.");
    }

    /// <summary>A wide window shorter than the narrow one is disagreement, not a vacuous pass.</summary>
    [TestMethod]
    public void AShorterWideWindowIsDisagreement() =>
        Assert.IsFalse(EuProcedureEventLiveAcceptance.WindowsAgree(
            [FirstDossier, SecondDossier],
            [FirstDossier]));

    /// <summary>Two empty windows do not agree, because neither discovered anything.</summary>
    /// <remarks>
    /// The one case most likely to slip through as a pass. Prefix equality over empty sequences is
    /// trivially true, and a run that discovered nothing twice would then satisfy its own equality
    /// check and fail later for a less informative reason.
    /// </remarks>
    [TestMethod]
    public void TwoEmptyWindowsDoNotAgree() =>
        Assert.IsFalse(
            EuProcedureEventLiveAcceptance.WindowsAgree([], []),
            "discovering nothing twice is not agreement.");

    /// <summary>A well-formed distinct pair is proved, and its canonical form is carried.</summary>
    [TestMethod]
    public void ATwoDistinctCanonicalPairIsProved()
    {
        var proof = EuProcedureEventLiveAcceptance.ProveTwoDistinctDossiers(
            [FirstDossier, SecondDossier],
            [FirstDossier, SecondDossier, ThirdDossier]);

        Assert.AreEqual(EuProcedureEventLiveAcceptance.DeliveredVerdict, proof.Verdict);
        Assert.IsNotNull(proof.Raw);
        Assert.IsNotNull(proof.Canonical);
        Assert.HasCount(2, proof.Canonical);
        Assert.AreNotEqual(proof.Canonical[0], proof.Canonical[1]);
    }

    /// <summary>
    /// A value the publisher labelled a URI but which is not one is refused.
    /// </summary>
    /// <remarks>
    /// THE REVIEWER'S OWN PROBE. He sent <c>{"type":"uri","value":"not an iri"}</c> through the
    /// parser and it came back as a row, because the SPARQL term label is the publisher's claim and
    /// not proof. The producer would have opened its robots bootstrap and only then refused, so this
    /// has to be settled before the bootstrap is counted.
    /// </remarks>
    [TestMethod]
    public void AValueLabelledUriThatIsNotAnIriIsRefused()
    {
        var proof = EuProcedureEventLiveAcceptance.ProveTwoDistinctDossiers(
            [FirstDossier, "not an iri"],
            [FirstDossier, "not an iri"]);

        Assert.AreEqual("DiscoveryValueIsNotCanonical", proof.Verdict);
        Assert.IsNull(proof.Raw, "nothing may be handed on when a value does not reduce.");
        Assert.IsNull(proof.Canonical);
    }

    /// <summary>
    /// A raw-distinct pair that reduces to one dossier is refused before the producer sees it.
    /// </summary>
    /// <remarks>
    /// The canonical form normalises <c>https</c> to <c>http</c>, so these two strings are distinct
    /// as text and are ONE member to the producer. Raw distinctness is therefore weaker than the
    /// producer's boundary, which is why distinctness is proved in canonical form.
    /// </remarks>
    [TestMethod]
    public void ARawDistinctPairThatCanonicalizesToOneIsRefused()
    {
        var httpsTwin = "https" + FirstDossier["http".Length..];
        var proof = EuProcedureEventLiveAcceptance.ProveTwoDistinctDossiers(
            [FirstDossier, httpsTwin],
            [FirstDossier, httpsTwin]);

        Assert.AreEqual("DiscoveryReturnedACanonicalDuplicate", proof.Verdict);
        Assert.IsNull(proof.Canonical);
    }

    /// <summary>
    /// A valid noncanonical member is admitted, and its canonical form is what the producer answers
    /// under.
    /// </summary>
    /// <remarks>
    /// THE CASE THAT WOULD HAVE FAULTED A CORRECT DISCOVERY. The integrated contract
    /// <c>ADossierRequestedNonCanonicallyIsAnsweredUnderItsCanonicalForm</c> proves the producer
    /// publishes and answers under the canonical HTTP spelling. So an <c>https</c> or
    /// trailing-slash dossier is a perfectly good discovery that this proof must admit - and
    /// interpreting the delivered result with the raw spelling would then throw, recording
    /// <c>Faulted</c> for a run that had done nothing wrong. Raw and canonical are therefore both
    /// carried, and they are deliberately different here.
    /// </remarks>
    [TestMethod]
    public void AValidNoncanonicalMemberIsAdmittedAndCarriesItsCanonicalForm()
    {
        var noncanonical = "https" + FirstDossier["http".Length..] + "/";
        var proof = EuProcedureEventLiveAcceptance.ProveTwoDistinctDossiers(
            [noncanonical, SecondDossier],
            [noncanonical, SecondDossier]);

        Assert.AreEqual(EuProcedureEventLiveAcceptance.DeliveredVerdict, proof.Verdict);
        Assert.IsNotNull(proof.Raw);
        Assert.IsNotNull(proof.Canonical);
        Assert.AreEqual(
            noncanonical, proof.Raw[0],
            "the request keeps the exact spelling the publisher returned.");
        Assert.AreEqual(
            FirstDossier, proof.Canonical[0],
            "and the canonical form drops the scheme difference and the trailing slash.");
        Assert.AreNotEqual(
            proof.Raw[0], proof.Canonical[0],
            "these must differ here, or this test is not exercising the mismatch at all.");
    }

    /// <summary>Fewer or more than two URI terms is refused.</summary>
    [TestMethod]
    public void AWindowThatIsNotExactlyTwoIsRefused()
    {
        Assert.AreEqual(
            "DiscoveryDidNotYieldTwoDossiers",
            EuProcedureEventLiveAcceptance.ProveTwoDistinctDossiers(
                [FirstDossier], [FirstDossier]).Verdict);
        Assert.AreEqual(
            "DiscoveryDidNotYieldTwoDossiers",
            EuProcedureEventLiveAcceptance.ProveTwoDistinctDossiers([], []).Verdict);
    }

    /// <summary>A textually identical pair is refused before canonicalization is even reached.</summary>
    [TestMethod]
    public void ATextuallyDuplicatePairIsRefused() =>
        Assert.AreEqual(
            "DiscoveryReturnedADuplicatePair",
            EuProcedureEventLiveAcceptance.ProveTwoDistinctDossiers(
                [FirstDossier, FirstDossier], [FirstDossier, FirstDossier]).Verdict);

    /// <summary>Disagreeing windows are refused, and nothing is handed on.</summary>
    [TestMethod]
    public void DisagreeingWindowsAreRefused()
    {
        var proof = EuProcedureEventLiveAcceptance.ProveTwoDistinctDossiers(
            [FirstDossier, SecondDossier],
            [ThirdDossier, FirstDossier, SecondDossier]);

        Assert.AreEqual("DiscoveryWindowsDisagree", proof.Verdict);
        Assert.IsNull(proof.Raw);
    }

    private static byte[] Payload(params string[] dossiers)
    {
        var builder = new StringBuilder("{\"head\":{\"vars\":[\"dossier\"]},\"results\":{\"bindings\":[");
        for (var index = 0; index < dossiers.Length; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            builder.Append("{\"dossier\":{\"type\":\"uri\",\"value\":\"")
                .Append(dossiers[index])
                .Append("\"}}");
        }

        return Encoding.UTF8.GetBytes(builder.Append("]}}").ToString());
    }
}
