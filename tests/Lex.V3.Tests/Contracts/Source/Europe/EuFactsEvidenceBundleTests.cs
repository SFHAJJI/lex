using Lex.V3.Contracts;
using Lex.V3.Contracts.Facts;
using Lex.V3.Contracts.Source.Europe;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Europe;

/// <summary>
/// Stage 2 item E7's remaining half: the bundle that makes a Summary of EU Legislation's exclusion
/// structural.
/// </summary>
/// <remarks>
/// <para>
/// REL-005 types a LegisSum record as explanatory rather than law, and E7 is done when the type and
/// the exclusion are proven "by a bundle that cannot carry one" — a compile-time or door-level
/// exclusion with a test that tries and is refused, not a documented convention.
/// </para>
/// <para>
/// Before this, the exclusion was proven by asserting that
/// <see cref="IEuFactsEvidenceCarrier"/> is not assignable from
/// <see cref="EuLegislationSummary"/>. That assertion is true and it is not the ruling's criterion:
/// it states a property of a type, while the criterion asks for a container that refuses.
/// <see cref="IEuFactsEvidenceCarrier"/>'s own remarks said "every future evidence bundle demands
/// this marker" in the future tense, because there was no bundle. A bundle that does not exist
/// refuses nothing.
/// </para>
/// </remarks>
[TestClass]
public sealed class EuFactsEvidenceBundleTests
{
    private static OfficialIdentitySet Work(string uuid) =>
        new(PublisherId.EuEurLex,
            [new OfficialIdentifier(
                FactsIdentifierFamily.CellarWorkUri,
                "http://publications.europa.eu/resource/cellar/" + uuid)]);

    private static OfficialIdentitySet Gdpr() =>
        new(PublisherId.EuEurLex, [new OfficialIdentifier(FactsIdentifierFamily.Celex, "32016R0679")]);

    /// <summary>A real E4 carrier, built through its own door rather than stubbed.</summary>
    private static EuRelationEdgeBinding Carrier(string observationId = "obs:bundle-carrier") =>
        EuRelationEdgeBinding.Create(
            Work("00034b8a-6af2-4207-bc76-d24a10b5125c"),
            Work("62212f0d-011f-471e-a033-bf56990d4329"),
            EuAmendmentRelationVocabulary.AmendsPredicateUri,
            TargetBodyScope.BodyInScopeNotHeld,
            [],
            observationId);

    /// <summary>review/23 section 7 line 88's worked instance, as E7's own tests build it.</summary>
    private static EuLegislationSummary Summary() => EuLegislationSummary.Create(
        workIdDocument: "legissum:310401_2",
        summarizedAct: Gdpr(),
        predicateUri: EuLegislationSummaryPredicateVocabulary.SummarizesResourceLegalPredicateUri,
        summarizedActBodyScope: TargetBodyScope.BodyInScopeHeld,
        draftedInLanguage: "ENG",
        version: "2.0.0",
        obsolete: false,
        validatedByInstitution: "JUST",
        sourceObservationId: "obs:legissum-310401-2-summarizes-gdpr");

    /// <summary>
    /// THE GUARD E7 ASKS FOR: a summary is offered to the bundle and the bundle refuses it.
    /// </summary>
    /// <remarks>
    /// This is the "tries and is refused" the ruling requires, and it is why
    /// <see cref="EuFactsEvidenceBundle.TryAdmit"/> exists at all. The typed door's exclusion is
    /// stronger — a summary cannot be passed to it without failing to compile — but code that does
    /// not compile cannot be executed, so a compile-time exclusion alone leaves the ruling's test
    /// with nothing to try.
    /// </remarks>
    [TestMethod]
    public void ASummaryRecordOfferedToTheBundleIsRefusedAndNamed()
    {
        var admitted = EuFactsEvidenceBundle.TryAdmit(
            [Summary()], out var refusal, out var offendingTypeName);

        Assert.IsNull(admitted, "a bundle that admitted a summary would make REL-005 a convention.");
        Assert.AreEqual(EuFactsEvidenceAdmissionRefusal.CandidateIsNotAFactsEvidenceCarrier, refusal);
        Assert.AreEqual(typeof(EuLegislationSummary).FullName, offendingTypeName);
    }

    /// <summary>
    /// The refusal is of the whole call. Carriers are not admitted while the summary is dropped.
    /// </summary>
    /// <remarks>
    /// The failure mode this exists for is the one this seat shipped on the E8 procedure-event
    /// contract and had found against it: keeping the well-formed members and discarding the
    /// unrepresentable one returns something that looks complete and is not, and the dropped record
    /// leaves nothing behind to notice. An evidence bundle is the worst possible place for that,
    /// because its whole purpose is to say what the evidence was.
    /// </remarks>
    [TestMethod]
    public void AMixOfCarriersAndASummaryIsRefusedWholeRatherThanFiltered()
    {
        var admitted = EuFactsEvidenceBundle.TryAdmit(
            [Carrier("obs:first"), Summary(), Carrier("obs:third")],
            out var refusal,
            out var offendingTypeName);

        Assert.IsNull(admitted, "two carriers must not be delivered as though they were all the evidence.");
        Assert.AreEqual(EuFactsEvidenceAdmissionRefusal.CandidateIsNotAFactsEvidenceCarrier, refusal);
        Assert.AreEqual(typeof(EuLegislationSummary).FullName, offendingTypeName);
    }

    /// <summary>
    /// The typed door's element type is the marker itself, which is what makes the compile-time
    /// exclusion real rather than incidental.
    /// </summary>
    /// <remarks>
    /// Read off the signature rather than asserted in prose, so widening the door to <c>object</c>
    /// or to a concrete binding fails here. The second assertion is the exclusion that widening
    /// would silently undo.
    /// </remarks>
    [TestMethod]
    public void TheTypedDoorAdmitsOnlyTheMarkerSoASummaryCannotBeOfferedToItAtAll()
    {
        var parameter = typeof(EuFactsEvidenceBundle)
            .GetMethod(nameof(EuFactsEvidenceBundle.Create))!
            .GetParameters()[0];

        Assert.AreEqual(
            typeof(IReadOnlyList<IEuFactsEvidenceCarrier>), parameter.ParameterType,
            "the typed door must take the marker, never a wider or a concrete element type.");
        Assert.IsFalse(
            typeof(IEuFactsEvidenceCarrier).IsAssignableFrom(typeof(EuLegislationSummary)),
            "and the summary must remain outside the marker, or the door above admits it.");
    }

    /// <summary>Real Facts-layer evidence is admitted, in the order supplied.</summary>
    /// <remarks>
    /// Without this the refusals above would be satisfied by a bundle that refuses everything, which
    /// proves the exclusion and nothing about the bundle being usable.
    /// </remarks>
    [TestMethod]
    public void RealFactsCarriersAreAdmittedThroughBothDoorsInOrder()
    {
        var first = Carrier("obs:first");
        var second = Carrier("obs:second");

        var typed = EuFactsEvidenceBundle.Create([first, second]);
        Assert.HasCount(2, typed.Members);
        Assert.AreSame(first, typed.Members[0]);
        Assert.AreSame(second, typed.Members[1]);

        var admitted = EuFactsEvidenceBundle.TryAdmit(
            [first, second], out var refusal, out var offendingTypeName);
        Assert.AreEqual(EuFactsEvidenceAdmissionRefusal.None, refusal);
        Assert.IsNull(offendingTypeName);
        Assert.IsNotNull(admitted);
        Assert.AreSame(first, admitted!.Members[0]);
        Assert.AreSame(second, admitted.Members[1]);
    }

    /// <summary>A null candidate is refused, never skipped.</summary>
    /// <remarks>
    /// Skipping would shrink the evidence count silently, which is the same false absence as
    /// filtering a summary out, reached by a cheaper mistake.
    /// </remarks>
    [TestMethod]
    public void ANullCandidateIsRefusedRatherThanSkipped()
    {
        var admitted = EuFactsEvidenceBundle.TryAdmit(
            [Carrier(), null], out var refusal, out var offendingTypeName);

        Assert.IsNull(admitted);
        Assert.AreEqual(EuFactsEvidenceAdmissionRefusal.CandidateWasNull, refusal);
        Assert.IsNull(offendingTypeName, "there is no offending type when the candidate is absent.");
    }

    /// <summary>Each declared refusal is reachable from a delivered shape, so none is decoration.</summary>
    [TestMethod]
    public void EveryDeclaredRefusalIsReachableFromSomeOfferedShape()
    {
        var reached = new List<EuFactsEvidenceAdmissionRefusal>();

        Assert.IsNull(EuFactsEvidenceBundle.TryAdmit([Summary()], out var notACarrier, out _));
        reached.Add(notACarrier);

        Assert.IsNull(EuFactsEvidenceBundle.TryAdmit([null], out var wasNull, out _));
        reached.Add(wasNull);

        CollectionAssert.AreEqual(
            Enum.GetValues<EuFactsEvidenceAdmissionRefusal>()
                .Where(static member => member != EuFactsEvidenceAdmissionRefusal.None)
                .ToArray(),
            reached.Distinct().Order().ToArray(),
            "every declared refusal must be driven by a case above, each reaching a distinct one.");
    }

    /// <summary>
    /// An empty bundle is admitted and asserts nothing about completeness.
    /// </summary>
    /// <remarks>
    /// Recorded as a decision rather than left to be inferred. Whether an evidence set may be empty,
    /// and what retained proof that would need, is Decision 64's question about an acquisition, not
    /// this container's. Refusing empty here would mint a completeness rule out of a slice asked
    /// only to make an exclusion structural.
    /// </remarks>
    [TestMethod]
    public void AnEmptyBundleIsAdmittedAndClaimsNothingAboutCompleteness()
    {
        Assert.IsEmpty(EuFactsEvidenceBundle.Create([]).Members);

        var admitted = EuFactsEvidenceBundle.TryAdmit([], out var refusal, out _);
        Assert.AreEqual(EuFactsEvidenceAdmissionRefusal.None, refusal);
        Assert.IsNotNull(admitted);
        Assert.IsEmpty(admitted!.Members);
    }
}
