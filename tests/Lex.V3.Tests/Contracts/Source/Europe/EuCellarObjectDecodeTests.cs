using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Europe;

/// <summary>
/// D1-05b: the decode from a verified EU consolidation-family row set into
/// <see cref="EuCellarObjectSnapshot"/>. Fixtures mirror the real closure plan's own family
/// projection (<c>EuConsolidationDiscoveryPlan.CreateDeliveryProfile(EuConsolidationQuerySet.Family)</c>,
/// the same door <see cref="EuConsolidationDiscoveryTests"/> uses) and reuse GDPR's real CELEX and
/// Cellar coordinates from Appendix A and review/23's observed CDM shapes, per SCOPE_RULING
/// <c>lex-event-20260904T015609998Z-bb7cc08f556347f5a5455a58f810b9ee</c>: no live SPARQL call, no law
/// text.
/// </summary>
[TestClass]
public sealed class EuCellarObjectDecodeTests
{
    // GDPR: Appendix A seed 32016R0679, review/23's own worked CELEX/Cellar example.
    private const string GdprCelex = "32016R0679";
    private const string GdprRoot =
        "http://publications.europa.eu/resource/cellar/3e485e15-11bd-11e6-ba9a-01aa75ed71a1";

    // A dated consolidation of GDPR, in the shape review/23 records
    // (act_consolidated_number 2016R0679/20160504): a plausible distinct Cellar state.
    private const string StateA =
        "http://publications.europa.eu/resource/cellar/44444444-4444-4444-8444-444444444444";
    private const string StateB =
        "http://publications.europa.eu/resource/cellar/55555555-5555-4555-8555-555555555555";

    private const string XsdString = "http://www.w3.org/2001/XMLSchema#string";
    private const string XsdInteger = "http://www.w3.org/2001/XMLSchema#integer";

    private static readonly RepeatedEnumerationInterpretationProfile FamilyProfile =
        EuConsolidationDiscoveryPlan.Create().CreateDeliveryProfile(EuConsolidationQuerySet.Family);

    private static SourceArtifactRef Evidence(string label) =>
        new($"urn:uuid:{DeterministicGuid(label)}", Digest("evidence:" + label));

    private static string Digest(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static Guid DeterministicGuid(string label) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes("guid:" + label))[..16]);

    private static RepeatedEnumerationRow FamilyRow(
        string celex, string baseIri, string state, long multiplicity = 1)
    {
        var terms = new[]
        {
            RepeatedEnumerationRdfTerm.Literal(celex, XsdString, null),
            RepeatedEnumerationRdfTerm.Iri(baseIri),
            RepeatedEnumerationRdfTerm.Iri(state),
            RepeatedEnumerationRdfTerm.Literal(multiplicity.ToString(), XsdInteger, null),
            RepeatedEnumerationRdfTerm.Literal(state, null, null),
        };
        return new RepeatedEnumerationRow(
            Array.AsReadOnly(terms),
            Array.AsReadOnly(new[] { terms[0], terms[1], terms[2] }),
            Array.AsReadOnly(new[] { terms[4] }));
    }

    // ---- Happy path. ----

    [TestMethod]
    public void AMinimalSnapshotDecodesFromOneDiscoveredState()
    {
        var rows = new[] { FamilyRow(GdprCelex, GdprRoot, StateA) };

        var snapshot = EuCellarObjectDecode.TryDecode(
            GdprCelex, rows, FamilyProfile, EuActForm.Regulation, Evidence("gdpr"),
            out var refusal, out var snapshotRefusal);

        Assert.IsNotNull(snapshot);
        Assert.AreEqual(EuCellarObjectDecodeRefusal.None, refusal);
        Assert.AreEqual(EuCellarObjectSnapshotRefusal.None, snapshotRefusal);
        Assert.AreEqual(GdprRoot, snapshot!.CanonicalWorkRoot);
        Assert.AreEqual(GdprRoot, snapshot.ObjectRef.PublisherUri);

        var celexObservation = snapshot.Predicate(EuCdmPredicate.ResourceLegalIdCelex);
        Assert.AreEqual(EuPredicateObservationState.ObservedPresent, celexObservation.State);
        CollectionAssert.AreEqual(new[] { GdprCelex }, celexObservation.Values.ToArray());

        Assert.AreEqual(
            EuPredicateObservationState.NotObserved,
            snapshot.Predicate(EuCdmPredicate.ActConsolidatedDate).State);

        var consolidated = snapshot.Relation(EuRelationFamily.ConsolidatedBasedOn);
        Assert.AreEqual(EuRelationAcquisitionState.Complete, consolidated.Acquisition);
        Assert.AreEqual(1, consolidated.Edges.Count);
        Assert.AreEqual(StateA, consolidated.Edges[0].TargetWorkRoot);
        Assert.AreEqual(EuRelationAuthority.OntologyAuthorizedInverse, consolidated.Edges[0].Authority);

        Assert.AreEqual(
            EuRelationAcquisitionState.Unacquired,
            snapshot.Relation(EuRelationFamily.Amends).Acquisition);
        Assert.AreEqual(0, snapshot.Relation(EuRelationFamily.Amends).Edges.Count);

        Assert.AreEqual(EuChannel.CellarSparqlEndpoint, snapshot.Channel.Channel);
        Assert.IsNull(snapshot.Language);
        Assert.IsNull(snapshot.Format);
        Assert.IsNull(snapshot.Rights);
        Assert.IsNull(snapshot.Supporting);
    }

    // Fold-in for the D1-05b decode refreeze
    // (lex-event-20260904T025508487Z-0d433eb3f5254b6188c05ab22e962acd): BuildObjectRef's canonical
    // key prefix ("eu-consolidation-root:") feeds both ObjectRef.CanonicalKey and its SHA-256 digest,
    // and neither was ever asserted against a fixed expected string. The two expected literals below
    // are computed independently of this production code (sha256sum and, cross-checked, openssl dgst
    // -sha256, over the exact UTF-8 bytes of the canonical key string), the same "pin the GDPR root's
    // canonical key and digest as fixed literals" convention D1-05a used for the binding digest
    // (EuScopeProfileTests.ProfileAndSelectorTableDigestsArePinnedLiterally): a change to the prefix,
    // to the root the key is built from, or to how the digest is taken, is a real, catchable diff
    // rather than a tautology that recomputes the same literal through the same code it is meant to
    // guard.
    [TestMethod]
    public void TheObjectRefsCanonicalKeyAndDigestArePinnedLiterally()
    {
        var rows = new[] { FamilyRow(GdprCelex, GdprRoot, StateA) };

        var snapshot = EuCellarObjectDecode.TryDecode(
            GdprCelex, rows, FamilyProfile, EuActForm.Regulation, Evidence("gdpr-object-ref"),
            out var refusal, out _);

        Assert.AreEqual(EuCellarObjectDecodeRefusal.None, refusal);
        Assert.AreEqual(
            "eu-consolidation-root:"
                + "http://publications.europa.eu/resource/cellar/3e485e15-11bd-11e6-ba9a-01aa75ed71a1",
            snapshot!.ObjectRef.CanonicalKey);
        Assert.AreEqual(
            "7c5c4154a86ab3396956c0c1440e15710914741e9272273c1ca49eff5da51f68",
            snapshot.ObjectRef.CanonicalKeySha256);
    }

    [TestMethod]
    public void EveryOtherClosedPredicateIsHonestlyNotObserved()
    {
        var rows = new[] { FamilyRow(GdprCelex, GdprRoot, StateA) };

        var snapshot = EuCellarObjectDecode.TryDecode(
            GdprCelex, rows, FamilyProfile, EuActForm.Regulation, Evidence("gdpr"),
            out _, out _);

        Assert.IsNotNull(snapshot);
        foreach (var predicate in EuScopeVocabulary.CdmPredicates)
        {
            if (predicate == EuCdmPredicate.ResourceLegalIdCelex)
            {
                continue;
            }

            Assert.AreEqual(
                EuPredicateObservationState.NotObserved,
                snapshot!.Predicate(predicate).State,
                $"{predicate} should be honestly not-observed by this closure.");
        }
    }

    [TestMethod]
    public void ZeroFamilyRowsProducesACompleteZeroEdgeSnapshot()
    {
        var snapshot = EuCellarObjectDecode.TryDecode(
            GdprCelex, Array.Empty<RepeatedEnumerationRow>(), FamilyProfile, EuActForm.Regulation,
            Evidence("gdpr-empty"), out var refusal, out var snapshotRefusal);

        Assert.IsNotNull(snapshot);
        Assert.AreEqual(EuCellarObjectDecodeRefusal.None, refusal);
        Assert.AreEqual(EuCellarObjectSnapshotRefusal.None, snapshotRefusal);
        Assert.AreEqual(GdprRoot, snapshot!.CanonicalWorkRoot);

        var consolidated = snapshot.Relation(EuRelationFamily.ConsolidatedBasedOn);
        Assert.AreEqual(EuRelationAcquisitionState.Complete, consolidated.Acquisition);
        Assert.AreEqual(0, consolidated.Edges.Count);
    }

    [TestMethod]
    public void MultipleDistinctStatesAggregateIntoOneRelationFamilyObservationSortedOrdinally()
    {
        var rows = new[]
        {
            FamilyRow(GdprCelex, GdprRoot, StateB),
            FamilyRow(GdprCelex, GdprRoot, StateA),
        };

        var snapshot = EuCellarObjectDecode.TryDecode(
            GdprCelex, rows, FamilyProfile, EuActForm.Regulation, Evidence("gdpr-multi"),
            out var refusal, out _);

        Assert.AreEqual(EuCellarObjectDecodeRefusal.None, refusal);
        var edges = snapshot!.Relation(EuRelationFamily.ConsolidatedBasedOn).Edges;
        CollectionAssert.AreEqual(
            new[] { StateA, StateB }, edges.Select(edge => edge.TargetWorkRoot).ToArray());
    }

    [TestMethod]
    public void ARepeatedStateAcrossRowsDoesNotDuplicateTheEdge()
    {
        var rows = new[]
        {
            FamilyRow(GdprCelex, GdprRoot, StateA, multiplicity: 1),
            FamilyRow(GdprCelex, GdprRoot, StateA, multiplicity: 2),
        };

        var snapshot = EuCellarObjectDecode.TryDecode(
            GdprCelex, rows, FamilyProfile, EuActForm.Regulation, Evidence("gdpr-dup-state"),
            out var refusal, out _);

        Assert.AreEqual(EuCellarObjectDecodeRefusal.None, refusal);
        Assert.AreEqual(1, snapshot!.Relation(EuRelationFamily.ConsolidatedBasedOn).Edges.Count);
    }

    // ---- Refusals, each driven on its own branch. ----

    [TestMethod]
    public void ABaseCelexTermThatIsAnIriRefusesAsTermKindMismatch()
    {
        var terms = new RepeatedEnumerationRdfTerm[]
        {
            RepeatedEnumerationRdfTerm.Iri(GdprCelex), // wrong kind: an IRI, not a literal
            RepeatedEnumerationRdfTerm.Iri(GdprRoot),
            RepeatedEnumerationRdfTerm.Iri(StateA),
            RepeatedEnumerationRdfTerm.Literal("1", XsdInteger, null),
            RepeatedEnumerationRdfTerm.Literal(StateA, null, null),
        };
        var row = new RepeatedEnumerationRow(
            Array.AsReadOnly(terms),
            Array.AsReadOnly(new[] { terms[0], terms[1], terms[2] }),
            Array.AsReadOnly(new[] { terms[4] }));

        EuCellarObjectDecode.TryDecode(
            GdprCelex, [row], FamilyProfile, EuActForm.Regulation, Evidence("bad-celex-kind"),
            out var refusal, out _);

        Assert.AreEqual(EuCellarObjectDecodeRefusal.FamilyRowTermKindMismatch, refusal);
    }

    [TestMethod]
    public void ABaseTermThatIsALiteralRefusesAsTermKindMismatch()
    {
        var terms = new RepeatedEnumerationRdfTerm[]
        {
            RepeatedEnumerationRdfTerm.Literal(GdprCelex, XsdString, null),
            RepeatedEnumerationRdfTerm.Literal(GdprRoot, null, null), // wrong kind: expected IRI
            RepeatedEnumerationRdfTerm.Iri(StateA),
            RepeatedEnumerationRdfTerm.Literal("1", XsdInteger, null),
            RepeatedEnumerationRdfTerm.Literal(StateA, null, null),
        };
        var row = new RepeatedEnumerationRow(
            Array.AsReadOnly(terms),
            Array.AsReadOnly(new[] { terms[0], terms[1], terms[2] }),
            Array.AsReadOnly(new[] { terms[4] }));

        EuCellarObjectDecode.TryDecode(
            GdprCelex, [row], FamilyProfile, EuActForm.Regulation, Evidence("bad-base-kind"),
            out var refusal, out _);

        Assert.AreEqual(EuCellarObjectDecodeRefusal.FamilyRowTermKindMismatch, refusal);
    }

    [TestMethod]
    public void AStateTermThatIsUnboundRefusesAsTermKindMismatch()
    {
        var terms = new RepeatedEnumerationRdfTerm[]
        {
            RepeatedEnumerationRdfTerm.Literal(GdprCelex, XsdString, null),
            RepeatedEnumerationRdfTerm.Iri(GdprRoot),
            RepeatedEnumerationRdfTerm.Unbound(), // wrong kind: expected IRI
            RepeatedEnumerationRdfTerm.Literal("1", XsdInteger, null),
            RepeatedEnumerationRdfTerm.Literal(string.Empty, null, null),
        };
        var row = new RepeatedEnumerationRow(
            Array.AsReadOnly(terms),
            Array.AsReadOnly(new[] { terms[0], terms[1], terms[2] }),
            Array.AsReadOnly(new[] { terms[4] }));

        EuCellarObjectDecode.TryDecode(
            GdprCelex, [row], FamilyProfile, EuActForm.Regulation, Evidence("bad-state-kind"),
            out var refusal, out _);

        Assert.AreEqual(EuCellarObjectDecodeRefusal.FamilyRowTermKindMismatch, refusal);
    }

    [TestMethod]
    public void AStateTermThatCannotCanonicalizeRefusesAsTermKindMismatch()
    {
        var malformedState = StateA + "?x=1";
        var row = FamilyRow(GdprCelex, GdprRoot, malformedState);

        EuCellarObjectDecode.TryDecode(
            GdprCelex, [row], FamilyProfile, EuActForm.Regulation, Evidence("bad-state-shape"),
            out var refusal, out _);

        Assert.AreEqual(EuCellarObjectDecodeRefusal.FamilyRowTermKindMismatch, refusal);
    }

    [TestMethod]
    public void ARowNamingADifferentCelexThanRequestedRefusesAsADuplicateBinding()
    {
        var row = FamilyRow("32013R0575", GdprRoot, StateA); // CRR's own Appendix A CELEX, not GDPR's

        EuCellarObjectDecode.TryDecode(
            GdprCelex, [row], FamilyProfile, EuActForm.Regulation, Evidence("wrong-celex"),
            out var refusal, out _);

        Assert.AreEqual(EuCellarObjectDecodeRefusal.DuplicateSingleValuedBinding, refusal);
    }

    [TestMethod]
    public void ASecondRowNamingADifferentBaseRefusesAsADuplicateBinding()
    {
        var otherRoot =
            "http://publications.europa.eu/resource/cellar/66666666-6666-4666-8666-666666666666";
        var rows = new[]
        {
            FamilyRow(GdprCelex, GdprRoot, StateA),
            FamilyRow(GdprCelex, otherRoot, StateB),
        };

        EuCellarObjectDecode.TryDecode(
            GdprCelex, rows, FamilyProfile, EuActForm.Regulation, Evidence("wrong-base"),
            out var refusal, out _);

        Assert.AreEqual(EuCellarObjectDecodeRefusal.DuplicateSingleValuedBinding, refusal);
    }

    [TestMethod]
    public void AFabricatedOutOfPackBaseRefusesThroughTheSnapshotDoorEvenWithATrustedCelex()
    {
        // The row's own base names a real, well-formed Cellar Work root that is simply not one of
        // Appendix A's 82 seeds, while its base_celex still correctly echoes the requested seed - the
        // shape a corrupted or hostile SPARQL response could produce. Appendix A's own root for
        // GdprCelex is never substituted in its place; the row's own claim reaches
        // EuCellarObjectSnapshot.TryObserve and is refused there, on point 9 of the ruling.
        var notASeed =
            "http://publications.europa.eu/resource/cellar/00000000-0000-0000-0000-000000000000";
        var row = FamilyRow(GdprCelex, notASeed, StateA);

        var snapshot = EuCellarObjectDecode.TryDecode(
            GdprCelex, [row], FamilyProfile, EuActForm.Regulation, Evidence("out-of-pack"),
            out var refusal, out var snapshotRefusal);

        Assert.IsNull(snapshot);
        Assert.AreEqual(EuCellarObjectDecodeRefusal.ObjectSnapshotRejected, refusal);
        Assert.AreEqual(EuCellarObjectSnapshotRefusal.WorkRootOutsideAppendixAPack, snapshotRefusal);
    }

    // ---- Caller-contract guards. ----

    [TestMethod]
    public void ARequestedCelexOutsideAppendixARefusesAsACallerContractViolation()
    {
        Assert.ThrowsExactly<ArgumentException>(() => EuCellarObjectDecode.TryDecode(
            "31995L0046", // Directive 95/46: repealed by GDPR, never itself an Appendix A seed
            Array.Empty<RepeatedEnumerationRow>(),
            FamilyProfile,
            EuActForm.Regulation,
            Evidence("not-a-seed"),
            out _,
            out _));
    }

    [TestMethod]
    public void ABlankRequestedCelexThrows()
    {
        Assert.ThrowsExactly<ArgumentException>(() => EuCellarObjectDecode.TryDecode(
            "   ", Array.Empty<RepeatedEnumerationRow>(), FamilyProfile, EuActForm.Regulation,
            Evidence("blank"), out _, out _));
    }

    [TestMethod]
    public void ANullFamilyRowsThrows()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => EuCellarObjectDecode.TryDecode(
            GdprCelex, null!, FamilyProfile, EuActForm.Regulation, Evidence("null-rows"),
            out _, out _));
    }

    [TestMethod]
    public void ANullFamilyProfileThrows()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => EuCellarObjectDecode.TryDecode(
            GdprCelex, Array.Empty<RepeatedEnumerationRow>(), null!, EuActForm.Regulation,
            Evidence("null-profile"), out _, out _));
    }

    [TestMethod]
    public void ANullEvidenceRefThrows()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => EuCellarObjectDecode.TryDecode(
            GdprCelex, Array.Empty<RepeatedEnumerationRow>(), FamilyProfile, EuActForm.Regulation,
            null!, out _, out _));
    }

    [TestMethod]
    public void AnUndefinedRecordFormThrows()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => EuCellarObjectDecode.TryDecode(
            GdprCelex, Array.Empty<RepeatedEnumerationRow>(), FamilyProfile, (EuActForm)999,
            Evidence("bad-form"), out _, out _));
    }

    [TestMethod]
    public void ANullFamilyRowThrows()
    {
        Assert.ThrowsExactly<ArgumentException>(() => EuCellarObjectDecode.TryDecode(
            GdprCelex, new RepeatedEnumerationRow?[] { null }!, FamilyProfile, EuActForm.Regulation,
            Evidence("null-row"), out _, out _));
    }

    // Noted, not blocking, by the D1-05b decode refreeze
    // (lex-event-20260904T025508487Z-0d433eb3f5254b6188c05ab22e962acd): a row shorter than the
    // profile's own projection previously reached an unexplained ArgumentOutOfRangeException at
    // Term's single positional read. Not reachable from a real delivery (the item 17 door already
    // shapes every row to match the profile it was verified under), but a hand-built or corrupted row
    // is still a caller contract violation, not a reviewable data disagreement -- the same treatment
    // TryDecode already gives a null row above -- so this is a clean, explained ArgumentException
    // rather than a raw index exception with no message.
    [TestMethod]
    public void ARowWithFewerTermsThanTheProjectionThrowsACleanArgumentException()
    {
        var shortTerms = new RepeatedEnumerationRdfTerm[]
        {
            RepeatedEnumerationRdfTerm.Literal(GdprCelex, XsdString, null),
        };
        var row = new RepeatedEnumerationRow(
            Array.AsReadOnly(shortTerms),
            Array.AsReadOnly(new[] { shortTerms[0] }),
            Array.AsReadOnly(new[] { shortTerms[0] }));

        var thrown = Assert.ThrowsExactly<ArgumentException>(() => EuCellarObjectDecode.TryDecode(
            GdprCelex, [row], FamilyProfile, EuActForm.Regulation, Evidence("short-terms"),
            out _, out _));
        StringAssert.Contains(thrown.Message, "too few");
    }

    // ---- Construction surface. ----

    [TestMethod]
    public void TheDoorItselfIsNeverProducedBecauseItIsNeverAValue()
    {
        // EuCellarObjectDecode is a static class, the same shape as VerifiedRepeatedEnumerationRows
        // (Source/Core): nothing anywhere can ever hold, return or hand out a value of this type - it
        // only ever hands out an EuCellarObjectSnapshot - so both surfaces are empty by construction.
        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            ConstructionSurface.Of(typeof(EuCellarObjectDecode)).ToArray());
        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            ConstructionSurface.ProducersIn(
                typeof(EuCellarObjectDecode).Assembly, typeof(EuCellarObjectDecode), true).ToArray());
    }

    [TestMethod]
    public void TheRefusalEnumHasExactlyFourMembersAndOneHandOutPath()
    {
        const string N = "Lex.V3.Contracts.Source.Europe.";
        const string Refusal = N + "EuCellarObjectDecodeRefusal";
        CollectionAssert.AreEqual(
            new[]
            {
                "base-constructor protected instance System.Enum::.ctor() -> System.Enum",
                "base-constructor protected instance System.ValueType::.ctor() -> System.ValueType",
                "field public static " + Refusal + "::DuplicateSingleValuedBinding -> " + Refusal,
                "field public static " + Refusal + "::FamilyRowTermKindMismatch -> " + Refusal,
                "field public static " + Refusal + "::None -> " + Refusal,
                "field public static " + Refusal + "::ObjectSnapshotRejected -> " + Refusal,
            },
            ConstructionSurface.Of(typeof(EuCellarObjectDecodeRefusal)).ToArray());

        const string C = "Lex.V3.Contracts.";
        CollectionAssert.AreEqual(
            new[]
            {
                "by-ref-method public static " + N + "EuCellarObjectDecode::TryDecode(System.String, "
                + "System.Collections.Generic.IReadOnlyList<Lex.V3.Contracts.Source.Core."
                + "RepeatedEnumerationRow>, Lex.V3.Contracts.Source.Core."
                + "RepeatedEnumerationInterpretationProfile, " + C + "EuActForm, "
                + "Lex.V3.Contracts.Source.Core.SourceArtifactRef, out " + Refusal + "&, out " + N
                + "EuCellarObjectSnapshotRefusal&) -> " + N + "EuCellarObjectSnapshot",
            },
            ConstructionSurface.ProducersIn(
                typeof(EuCellarObjectDecodeRefusal).Assembly, typeof(EuCellarObjectDecodeRefusal), true)
                .ToArray());
    }
}
