using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Europe;

/// <summary>
/// D1-05c-1: the decode extended from D1-05a's family census plus D1-05c-1's own object-facts
/// (family P) and Expression-facts (family X) row sets into one <see cref="EuCellarObjectSnapshot"/>
/// per object in the closure's own object set <c>O</c>. Fixtures mirror the real plans' own delivery
/// profiles (<c>EuConsolidationDiscoveryPlan.CreateDeliveryProfile</c>,
/// <c>EuObjectFactsDiscoveryPlan.CreateDeliveryProfile</c>) and reuse GDPR's real CELEX and Cellar
/// coordinates from Appendix A, per SCOPE_RULING
/// <c>lex-event-20260904T040718222Z-7e6f29af07024cf5b2cb716f94f288e3</c>: no live SPARQL call, no law
/// text.
/// </summary>
[TestClass]
public sealed class EuCellarObjectDecodeTests
{
    // GDPR: Appendix A seed 32016R0679.
    private const string GdprCelex = "32016R0679";
    private const string GdprRoot =
        "http://publications.europa.eu/resource/cellar/3e485e15-11bd-11e6-ba9a-01aa75ed71a1";
    private const string StateA =
        "http://publications.europa.eu/resource/cellar/44444444-4444-4444-8444-444444444444";
    private const string StateB =
        "http://publications.europa.eu/resource/cellar/55555555-5555-4555-8555-555555555555";
    private const string ExprA =
        "http://publications.europa.eu/resource/cellar/66666666-6666-4666-8666-666666666666.0001";
    private const string ExprB =
        "http://publications.europa.eu/resource/cellar/77777777-7777-4777-8777-777777777777.0001";
    private const string CitedRoot =
        "http://publications.europa.eu/resource/cellar/88888888-8888-4888-8888-888888888888";

    private const string XsdString = "http://www.w3.org/2001/XMLSchema#string";
    private const string XsdInteger = "http://www.w3.org/2001/XMLSchema#integer";
    private const string ConsolidatedActResourceTypeIri =
        "http://publications.europa.eu/resource/authority/resource-type/CONSOLID_ACT";
    private const string OrdinaryActResourceTypeIri =
        "http://publications.europa.eu/resource/authority/resource-type/REG";
    private const string EnglishLanguageAuthorityIri =
        "http://publications.europa.eu/resource/authority/language/ENG";
    private const string FrenchLanguageAuthorityIri =
        "http://publications.europa.eu/resource/authority/language/FRA";

    private static readonly string CelexIri =
        EuObjectFactsDiscoveryPlan.CdmIri(EuCdmPredicate.ResourceLegalIdCelex);
    private static readonly string WorkHasResourceTypeIri =
        EuObjectFactsDiscoveryPlan.CdmIri(EuCdmPredicate.WorkHasResourceType);
    private static readonly string AmendsIri =
        EuObjectFactsDiscoveryPlan.RelationIri(EuRelationFamily.Amends);
    private static readonly string ConsolidatedBasedOnIri =
        EuObjectFactsDiscoveryPlan.RelationIri(EuRelationFamily.ConsolidatedBasedOn);
    private static readonly string ExpressionBelongsToWorkIri =
        EuObjectFactsDiscoveryPlan.CdmIri(EuCdmPredicate.ExpressionBelongsToWork);
    private static readonly string ExpressionUsesLanguageIri =
        EuObjectFactsDiscoveryPlan.CdmIri(EuCdmPredicate.ExpressionUsesLanguage);

    private static readonly RepeatedEnumerationInterpretationProfile FamilyProfile =
        EuConsolidationDiscoveryPlan.Create().CreateDeliveryProfile(EuConsolidationQuerySet.Family);
    private static readonly RepeatedEnumerationInterpretationProfile ObjectFactsProfile =
        EuObjectFactsDiscoveryPlan.Create().CreateDeliveryProfile(EuObjectFactsQuerySet.ObjectFacts);
    private static readonly RepeatedEnumerationInterpretationProfile ExpressionFactsProfile =
        EuObjectFactsDiscoveryPlan.Create().CreateDeliveryProfile(EuObjectFactsQuerySet.ExpressionFacts);
    private static readonly RepeatedEnumerationInterpretationProfile ManifestationFactsProfile =
        EuObjectFactsDiscoveryPlan.Create().CreateDeliveryProfile(EuObjectFactsQuerySet.ManifestationFacts);

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

    // ---- Object-facts (family P) row fixtures. ----

    private sealed record PValue(string Value, bool IsIri = true, string? Datatype = null, string? Lang = null);

    private static RepeatedEnumerationRow PBoundRow(string objectIri, string predicateIri, PValue value)
    {
        var kind = value.IsIri ? "iri" : "literal";
        var datatype = value.IsIri ? "" : value.Datatype ?? "";
        var lang = value.IsIri ? "" : value.Lang ?? "";
        var terms = new[]
        {
            RepeatedEnumerationRdfTerm.Iri(objectIri),
            RepeatedEnumerationRdfTerm.Iri(predicateIri),
            value.IsIri
                ? RepeatedEnumerationRdfTerm.Iri(value.Value)
                : RepeatedEnumerationRdfTerm.Literal(value.Value, value.Datatype, value.Lang),
            RepeatedEnumerationRdfTerm.Literal(kind, null, null),
            RepeatedEnumerationRdfTerm.Literal(datatype, null, null),
            RepeatedEnumerationRdfTerm.Literal(lang, null, null),
            RepeatedEnumerationRdfTerm.Literal("1", XsdInteger, null),
            RepeatedEnumerationRdfTerm.Literal(objectIri, null, null),
            RepeatedEnumerationRdfTerm.Literal(predicateIri, null, null),
            RepeatedEnumerationRdfTerm.Literal(kind, null, null),
            RepeatedEnumerationRdfTerm.Literal(value.Value, null, null),
            RepeatedEnumerationRdfTerm.Literal(datatype, null, null),
            RepeatedEnumerationRdfTerm.Literal(lang, null, null),
        };
        return new RepeatedEnumerationRow(
            Array.AsReadOnly(terms),
            Array.AsReadOnly(new[] { terms[0], terms[1], terms[2] }),
            Array.AsReadOnly(terms[7..13]));
    }

    private static RepeatedEnumerationRow PUnboundRow(string objectIri, string predicateIri)
    {
        var terms = new[]
        {
            RepeatedEnumerationRdfTerm.Iri(objectIri),
            RepeatedEnumerationRdfTerm.Iri(predicateIri),
            RepeatedEnumerationRdfTerm.Unbound(),
            RepeatedEnumerationRdfTerm.Literal("unbound", null, null),
            RepeatedEnumerationRdfTerm.Literal("", null, null),
            RepeatedEnumerationRdfTerm.Literal("", null, null),
            RepeatedEnumerationRdfTerm.Literal("0", XsdInteger, null),
            RepeatedEnumerationRdfTerm.Literal(objectIri, null, null),
            RepeatedEnumerationRdfTerm.Literal(predicateIri, null, null),
            RepeatedEnumerationRdfTerm.Literal("unbound", null, null),
            RepeatedEnumerationRdfTerm.Literal("", null, null),
            RepeatedEnumerationRdfTerm.Literal("", null, null),
            RepeatedEnumerationRdfTerm.Literal("", null, null),
        };
        return new RepeatedEnumerationRow(
            Array.AsReadOnly(terms),
            Array.AsReadOnly(new[] { terms[0], terms[1], terms[2] }),
            Array.AsReadOnly(terms[7..13]));
    }

    private static IEnumerable<string> AllPPredicateIris() =>
        EuObjectFactsDiscoveryPlan.ObjectAuthorityPredicates.Select(EuObjectFactsDiscoveryPlan.CdmIri)
            .Concat(EuScopeVocabulary.ReadRelationFamilies.Select(EuObjectFactsDiscoveryPlan.RelationIri));

    /// <summary>
    /// A complete family-P delivery for one object: every one of the thirteen predicates this
    /// family asks gets exactly one outcome (the supplied bound values, or an explicit unbound row
    /// when the caller supplies none), matching what a real bounded-and-proven P delivery always
    /// carries.
    /// </summary>
    private static IReadOnlyList<RepeatedEnumerationRow> CompleteObjectRows(
        string objectIri, IReadOnlyDictionary<string, PValue[]>? overrides = null)
    {
        overrides ??= new Dictionary<string, PValue[]>();
        var rows = new List<RepeatedEnumerationRow>();
        foreach (var predicateIri in AllPPredicateIris())
        {
            if (overrides.TryGetValue(predicateIri, out var values) && values.Length > 0)
            {
                rows.AddRange(values.Select(value => PBoundRow(objectIri, predicateIri, value)));
            }
            else
            {
                rows.Add(PUnboundRow(objectIri, predicateIri));
            }
        }

        return rows;
    }

    /// <summary>A root's complete family-P rows: real CELEX, ordinary (non-consolidated) type.</summary>
    private static IReadOnlyList<RepeatedEnumerationRow> RootObjectRows(
        string rootIri, string celex, IReadOnlyDictionary<string, PValue[]>? extra = null)
    {
        var overrides = new Dictionary<string, PValue[]>
        {
            [CelexIri] = [new PValue(celex, IsIri: false, Datatype: XsdString)],
            [WorkHasResourceTypeIri] = [new PValue(OrdinaryActResourceTypeIri)],
        };
        if (extra is not null)
        {
            foreach (var (key, value) in extra)
            {
                overrides[key] = value;
            }
        }

        return CompleteObjectRows(rootIri, overrides);
    }

    /// <summary>
    /// A state's complete family-P rows: consolidated type marker, and its own
    /// <c>ConsolidatedBasedOn</c> edge back to <paramref name="baseIri"/>, matching what P's own
    /// uniform per-object query naturally returns for a consolidated state.
    /// </summary>
    private static IReadOnlyList<RepeatedEnumerationRow> StateObjectRows(
        string stateIri, string baseIri, IReadOnlyDictionary<string, PValue[]>? extra = null)
    {
        var overrides = new Dictionary<string, PValue[]>
        {
            [WorkHasResourceTypeIri] = [new PValue(ConsolidatedActResourceTypeIri)],
            [ConsolidatedBasedOnIri] = [new PValue(baseIri)],
        };
        if (extra is not null)
        {
            foreach (var (key, value) in extra)
            {
                overrides[key] = value;
            }
        }

        return CompleteObjectRows(stateIri, overrides);
    }

    // ---- Expression-facts (family X) row fixtures. ----

    private static RepeatedEnumerationRow XBoundRow(
        string parentIri, string exprIri, string predicateIri, PValue value)
    {
        var kind = value.IsIri ? "iri" : "literal";
        var datatype = value.IsIri ? "" : value.Datatype ?? "";
        var lang = value.IsIri ? "" : value.Lang ?? "";
        var terms = new[]
        {
            RepeatedEnumerationRdfTerm.Iri(parentIri),
            RepeatedEnumerationRdfTerm.Iri(exprIri),
            RepeatedEnumerationRdfTerm.Iri(predicateIri),
            value.IsIri
                ? RepeatedEnumerationRdfTerm.Iri(value.Value)
                : RepeatedEnumerationRdfTerm.Literal(value.Value, value.Datatype, value.Lang),
            RepeatedEnumerationRdfTerm.Literal(kind, null, null),
            RepeatedEnumerationRdfTerm.Literal(datatype, null, null),
            RepeatedEnumerationRdfTerm.Literal(lang, null, null),
            RepeatedEnumerationRdfTerm.Literal("1", XsdInteger, null),
            RepeatedEnumerationRdfTerm.Literal(exprIri, null, null),
            RepeatedEnumerationRdfTerm.Literal(predicateIri, null, null),
            RepeatedEnumerationRdfTerm.Literal(kind, null, null),
            RepeatedEnumerationRdfTerm.Literal(value.Value, null, null),
            RepeatedEnumerationRdfTerm.Literal(datatype, null, null),
            RepeatedEnumerationRdfTerm.Literal(lang, null, null),
        };
        return new RepeatedEnumerationRow(
            Array.AsReadOnly(terms),
            Array.AsReadOnly(new[] { terms[0], terms[1], terms[2], terms[3] }),
            Array.AsReadOnly(terms[8..14]));
    }

    /// <summary>One Expression's complete family-X rows: belongs-to-work plus a language.</summary>
    private static IReadOnlyList<RepeatedEnumerationRow> ExpressionRows(
        string parentIri, string exprIri, string languageAuthorityIri)
    {
        return
        [
            XBoundRow(parentIri, exprIri, ExpressionBelongsToWorkIri, new PValue(parentIri)),
            XBoundRow(parentIri, exprIri, ExpressionUsesLanguageIri, new PValue(languageAuthorityIri)),
        ];
    }

    private static SourceArtifactRef Ev(string label) => Evidence(label);

    private static IReadOnlyList<EuCellarObjectSnapshot>? Decode(
        string celex,
        IReadOnlyList<RepeatedEnumerationRow> familyRows,
        IReadOnlyList<RepeatedEnumerationRow> pRows,
        IReadOnlyList<RepeatedEnumerationRow> xRows,
        out EuCellarObjectDecodeRefusal refusal,
        out string? offendingIri,
        out EuCellarObjectSnapshotRefusal snapshotRefusal,
        string evidenceLabel = "ev",
        IReadOnlyList<RepeatedEnumerationRow>? mRows = null) =>
        EuCellarObjectDecode.TryDecode(
            celex, familyRows, FamilyProfile, pRows, ObjectFactsProfile, xRows, ExpressionFactsProfile,
            mRows ?? [], ManifestationFactsProfile, Ev(ManifestationEvidenceLabel),
            EuActForm.Regulation, Ev(evidenceLabel), out refusal, out offendingIri, out snapshotRefusal, out _);

    /// <summary>
    /// The label whose evidence ref stands in for family M's own delivery evidence in these
    /// fixtures. Deliberately different from the sibling families' label, so a decode that stamped
    /// P's evidence on a format observation would be visible here rather than hidden by both refs
    /// happening to be the same object.
    /// </summary>
    private const string ManifestationEvidenceLabel = "m-ev";

    // ---- Happy path: root only, no discovered states. ----

    [TestMethod]
    public void ARootWithNoStatesDecodesToExactlyOneSnapshot()
    {
        var pRows = RootObjectRows(GdprRoot, GdprCelex);
        var snapshots = Decode(
            GdprCelex, [], pRows, [], out var refusal, out var offendingIri, out var snapshotRefusal);

        Assert.IsNotNull(snapshots);
        Assert.AreEqual(EuCellarObjectDecodeRefusal.None, refusal);
        Assert.IsNull(offendingIri);
        Assert.AreEqual(EuCellarObjectSnapshotRefusal.None, snapshotRefusal);
        Assert.AreEqual(1, snapshots!.Count);

        var root = snapshots[0];
        Assert.AreEqual(GdprRoot, root.CanonicalWorkRoot);
        Assert.AreEqual(GdprRoot, root.ObjectRef.PublisherUri);
        Assert.AreEqual(
            EuPredicateObservationState.ObservedPresent,
            root.Predicate(EuCdmPredicate.ResourceLegalIdCelex).State);
        CollectionAssert.AreEqual(
            new[] { GdprCelex }, root.Predicate(EuCdmPredicate.ResourceLegalIdCelex).Values.ToArray());

        var consolidated = root.Relation(EuRelationFamily.ConsolidatedBasedOn);
        Assert.AreEqual(EuRelationAcquisitionState.Complete, consolidated.Acquisition);
        Assert.AreEqual(0, consolidated.Edges.Count);

        Assert.IsNotNull(root.Rights);
        Assert.AreEqual(EuContentClass.OriginalLegalText, root.Rights!.ContentClass);
        Assert.IsNotNull(root.Language);
        Assert.AreEqual(EuExpressionObservationState.NotObserved, root.Language!.State);
    }

    [TestMethod]
    public void EveryExpressionAuthorityPredicateIsHonestlyNotObservedOnEveryObject()
    {
        var pRows = RootObjectRows(GdprRoot, GdprCelex);
        var snapshots = Decode(GdprCelex, [], pRows, [], out var refusal, out _, out _);

        Assert.AreEqual(EuCellarObjectDecodeRefusal.None, refusal);
        foreach (var predicate in EuObjectFactsDiscoveryPlan.ExpressionAuthorityPredicates)
        {
            Assert.AreEqual(
                EuPredicateObservationState.NotObserved,
                snapshots![0].Predicate(predicate).State,
                $"{predicate} is an Expression-authority predicate; family P never asks it of a Work.");
        }
    }

    // ---- Happy path: root plus discovered states, the edge-placement move. ----

    [TestMethod]
    public void EachDiscoveredStateGetsItsOwnSnapshotWithThePublisherAssertedEdgeAndTheRootIsCompleteWithZeroEdges()
    {
        var familyRows = new[] { FamilyRow(GdprCelex, GdprRoot, StateA), FamilyRow(GdprCelex, GdprRoot, StateB) };
        var pRows = RootObjectRows(GdprRoot, GdprCelex)
            .Concat(StateObjectRows(StateA, GdprRoot))
            .Concat(StateObjectRows(StateB, GdprRoot))
            .ToArray();

        var snapshots = Decode(
            GdprCelex, familyRows, pRows, [], out var refusal, out var offendingIri, out var snapshotRefusal);

        Assert.IsNotNull(snapshots);
        Assert.AreEqual(EuCellarObjectDecodeRefusal.None, refusal);
        Assert.IsNull(offendingIri);
        Assert.AreEqual(EuCellarObjectSnapshotRefusal.None, snapshotRefusal);
        Assert.AreEqual(3, snapshots!.Count);

        var root = snapshots.Single(s => s.ObjectRef.PublisherUri == GdprRoot);
        var rootConsolidated = root.Relation(EuRelationFamily.ConsolidatedBasedOn);
        Assert.AreEqual(EuRelationAcquisitionState.Complete, rootConsolidated.Acquisition);
        Assert.AreEqual(0, rootConsolidated.Edges.Count);
        Assert.AreEqual(EuContentClass.OriginalLegalText, root.Rights!.ContentClass);

        foreach (var stateIri in new[] { StateA, StateB })
        {
            var state = snapshots.Single(s => s.ObjectRef.PublisherUri == stateIri);
            Assert.AreEqual(GdprRoot, state.CanonicalWorkRoot, "every object's resolved work root is this call's own base");
            var edge = state.Relation(EuRelationFamily.ConsolidatedBasedOn);
            Assert.AreEqual(EuRelationAcquisitionState.Complete, edge.Acquisition);
            Assert.AreEqual(1, edge.Edges.Count);
            Assert.AreEqual(GdprRoot, edge.Edges[0].TargetWorkRoot);
            Assert.AreEqual(EuRelationAuthority.PublisherAsserted, edge.Edges[0].Authority);
            Assert.AreEqual(EuContentClass.Consolidation, state.Rights!.ContentClass);
        }

        // No edge anywhere carries the retired inverse authority.
        Assert.IsFalse(snapshots.SelectMany(s => s.RelationObservations).SelectMany(r => r.Edges)
            .Any(edge => edge.Authority == EuRelationAuthority.OntologyAuthorizedInverse));
    }

    [TestMethod]
    public void ObjectRefsDifferBetweenTheRootAndAStateAndBothAreCanonicallyKeyed()
    {
        var familyRows = new[] { FamilyRow(GdprCelex, GdprRoot, StateA) };
        var pRows = RootObjectRows(GdprRoot, GdprCelex).Concat(StateObjectRows(StateA, GdprRoot)).ToArray();

        var snapshots = Decode(GdprCelex, familyRows, pRows, [], out var refusal, out _, out _);
        Assert.AreEqual(EuCellarObjectDecodeRefusal.None, refusal);

        var root = snapshots!.Single(s => s.ObjectRef.PublisherUri == GdprRoot);
        var state = snapshots!.Single(s => s.ObjectRef.PublisherUri == StateA);

        StringAssert.StartsWith(root.ObjectRef.CanonicalKey, "eu-consolidation-root:");
        StringAssert.StartsWith(state.ObjectRef.CanonicalKey, "eu-consolidation-state:");
        Assert.AreNotEqual(root.ObjectRef.CanonicalKey, state.ObjectRef.CanonicalKey);
    }

    // Fold-in for the D1-05b decode refreeze
    // (lex-event-20260904T025508487Z-0d433eb3f5254b6188c05ab22e962acd), still pinned after the
    // D1-05c-1 extension: the root's own canonical key and digest never move.
    [TestMethod]
    public void TheRootObjectRefsCanonicalKeyAndDigestArePinnedLiterally()
    {
        var pRows = RootObjectRows(GdprRoot, GdprCelex);
        var snapshots = Decode(GdprCelex, [], pRows, [], out var refusal, out _, out _);

        Assert.AreEqual(EuCellarObjectDecodeRefusal.None, refusal);
        var root = snapshots!.Single();
        Assert.AreEqual(
            "eu-consolidation-root:"
                + "http://publications.europa.eu/resource/cellar/3e485e15-11bd-11e6-ba9a-01aa75ed71a1",
            root.ObjectRef.CanonicalKey);
        Assert.AreEqual(
            "7c5c4154a86ab3396956c0c1440e15710914741e9272273c1ca49eff5da51f68",
            root.ObjectRef.CanonicalKeySha256);
    }

    // ---- Relation families beyond ConsolidatedBasedOn. ----

    [TestMethod]
    public void AnAmendsEdgeOnTheRootIsPublisherAssertedFromFamilyPDirectly()
    {
        var pRows = RootObjectRows(GdprRoot, GdprCelex, new Dictionary<string, PValue[]>
        {
            [AmendsIri] = [new PValue(CitedRoot)],
        });

        var snapshots = Decode(GdprCelex, [], pRows, [], out var refusal, out _, out _);
        Assert.AreEqual(EuCellarObjectDecodeRefusal.None, refusal);

        var amends = snapshots!.Single().Relation(EuRelationFamily.Amends);
        Assert.AreEqual(EuRelationAcquisitionState.Complete, amends.Acquisition);
        Assert.AreEqual(1, amends.Edges.Count);
        Assert.AreEqual(CitedRoot, amends.Edges[0].TargetWorkRoot);
        Assert.AreEqual(EuRelationAuthority.PublisherAsserted, amends.Edges[0].Authority);
    }

    // ---- Language, filled from family X. ----

    [TestMethod]
    public void AnObservedEnglishExpressionIsABodyCandidate()
    {
        // D1-05d changes this answer, and the change is deliberate. Before this slice the decode
        // reported every observed Expression as ExpressionObservedBodyNotHeld, because no body
        // acquisition existed anywhere and claiming otherwise would have described a fetch nobody
        // could perform. The reviewed scope's own closed policy,
        // EuLanguageBodyDisposition.BodyCandidateLanguages, holds exactly English and French, so an
        // OBSERVED English Expression IS a body candidate under it; reporting it as body-not-held
        // reported a reviewed inclusion as a reviewed exclusion, and capped the body axis at point
        // for every EU object regardless of what the office offered.
        var pRows = RootObjectRows(GdprRoot, GdprCelex);
        var xRows = ExpressionRows(GdprRoot, ExprA, EnglishLanguageAuthorityIri);

        var snapshots = Decode(GdprCelex, [], pRows, xRows, out var refusal, out _, out _);
        Assert.AreEqual(EuCellarObjectDecodeRefusal.None, refusal);

        var language = snapshots!.Single().Language;
        Assert.IsNotNull(language);
        Assert.AreEqual(EuOfficialLanguage.English, language!.Language);
        Assert.AreEqual(EuExpressionObservationState.ExpressionObservedBodyCandidate, language.State);
        Assert.AreEqual(
            "eu_cellar_object_decode.language_english_observed_body_candidate", language.RuleId);
    }

    [TestMethod]
    public void AFrenchExpressionIsObservedWhenNoEnglishExpressionExists()
    {
        var pRows = RootObjectRows(GdprRoot, GdprCelex);
        var xRows = ExpressionRows(GdprRoot, ExprA, FrenchLanguageAuthorityIri);

        var snapshots = Decode(GdprCelex, [], pRows, xRows, out var refusal, out _, out _);
        Assert.AreEqual(EuCellarObjectDecodeRefusal.None, refusal);

        var language = snapshots!.Single().Language;
        Assert.IsNotNull(language);
        Assert.AreEqual(EuOfficialLanguage.French, language!.Language);
        // See AnObservedEnglishExpressionIsABodyCandidate for why this is body-candidate from D1-05d.
        Assert.AreEqual(EuExpressionObservationState.ExpressionObservedBodyCandidate, language.State);
        Assert.AreEqual(
            "eu_cellar_object_decode.language_french_observed_body_candidate", language.RuleId);
    }

    [TestMethod]
    public void EnglishIsPreferredWhenBothEnglishAndFrenchExpressionsExist()
    {
        var pRows = RootObjectRows(GdprRoot, GdprCelex);
        var xRows = ExpressionRows(GdprRoot, ExprA, EnglishLanguageAuthorityIri)
            .Concat(ExpressionRows(GdprRoot, ExprB, FrenchLanguageAuthorityIri))
            .ToArray();

        var snapshots = Decode(GdprCelex, [], pRows, xRows, out var refusal, out _, out _);
        Assert.AreEqual(EuCellarObjectDecodeRefusal.None, refusal);
        Assert.AreEqual(EuOfficialLanguage.English, snapshots!.Single().Language!.Language);
    }

    [TestMethod]
    public void NoEligibleExpressionYieldsAnExplicitNotObservedEnglishObservation()
    {
        var pRows = RootObjectRows(GdprRoot, GdprCelex);
        var snapshots = Decode(GdprCelex, [], pRows, [], out var refusal, out _, out _);

        Assert.AreEqual(EuCellarObjectDecodeRefusal.None, refusal);
        var language = snapshots!.Single().Language;
        Assert.IsNotNull(language);
        Assert.AreEqual(EuOfficialLanguage.English, language!.Language);
        Assert.AreEqual(EuExpressionObservationState.NotObserved, language.State);
    }

    // ---- Content class: derived from P, closure position as a consistency check. ----

    [TestMethod]
    public void AStateWhoseTypeAssertionsDoNotMarkItConsolidatedRefusesOnContentClassMismatch()
    {
        var familyRows = new[] { FamilyRow(GdprCelex, GdprRoot, StateA) };
        // The state's own P rows claim the ordinary (non-consolidated) resource type - disagrees
        // with its closure position as a discovered state.
        var pRows = RootObjectRows(GdprRoot, GdprCelex)
            .Concat(CompleteObjectRows(StateA, new Dictionary<string, PValue[]>
            {
                [WorkHasResourceTypeIri] = [new PValue(OrdinaryActResourceTypeIri)],
                [ConsolidatedBasedOnIri] = [new PValue(GdprRoot)],
            }))
            .ToArray();

        var snapshots = Decode(GdprCelex, familyRows, pRows, [], out var refusal, out _, out _);
        Assert.IsNull(snapshots);
        Assert.AreEqual(EuCellarObjectDecodeRefusal.ContentClassClosurePositionMismatch, refusal);
    }

    [TestMethod]
    public void ARootWhoseTypeAssertionsMarkItConsolidatedRefusesOnContentClassMismatch()
    {
        var pRows = RootObjectRows(GdprRoot, GdprCelex, new Dictionary<string, PValue[]>
        {
            [WorkHasResourceTypeIri] = [new PValue(ConsolidatedActResourceTypeIri)],
        });

        var snapshots = Decode(GdprCelex, [], pRows, [], out var refusal, out _, out _);
        Assert.IsNull(snapshots);
        Assert.AreEqual(EuCellarObjectDecodeRefusal.ContentClassClosurePositionMismatch, refusal);
    }

    // ---- The ConsolidatedBasedOn cross-check between family P and the family census. ----

    [TestMethod]
    public void ARootCarryingAConsolidatedBasedOnEdgeInFamilyPRefusesAsDisagreeingWithTheFamilyCensus()
    {
        var pRows = RootObjectRows(GdprRoot, GdprCelex, new Dictionary<string, PValue[]>
        {
            [ConsolidatedBasedOnIri] = [new PValue(CitedRoot)],
        });

        var snapshots = Decode(GdprCelex, [], pRows, [], out var refusal, out _, out _);
        Assert.IsNull(snapshots);
        Assert.AreEqual(EuCellarObjectDecodeRefusal.ConsolidatedBasedOnEdgeDisagreesWithFamily, refusal);
    }

    [TestMethod]
    public void AStateWhoseFamilyPEdgeTargetsTheWrongRootRefusesAsDisagreeingWithTheFamilyCensus()
    {
        var familyRows = new[] { FamilyRow(GdprCelex, GdprRoot, StateA) };
        var pRows = RootObjectRows(GdprRoot, GdprCelex)
            .Concat(StateObjectRows(StateA, CitedRoot)) // wrong target: not this call's own root
            .ToArray();

        var snapshots = Decode(GdprCelex, familyRows, pRows, [], out var refusal, out _, out _);
        Assert.IsNull(snapshots);
        Assert.AreEqual(EuCellarObjectDecodeRefusal.ConsolidatedBasedOnEdgeDisagreesWithFamily, refusal);
    }

    // ---- Family P closure and shape refusals. ----

    [TestMethod]
    public void AFamilyPRowNamingAnObjectOutsideTheClosureRefusesNamingTheIri()
    {
        var outsideObject = CitedRoot;
        var pRows = RootObjectRows(GdprRoot, GdprCelex).Concat(CompleteObjectRows(outsideObject)).ToArray();

        var snapshots = Decode(GdprCelex, [], pRows, [], out var refusal, out var offendingIri, out _);
        Assert.IsNull(snapshots);
        Assert.AreEqual(EuCellarObjectDecodeRefusal.ObjectFactRowNotInClosure, refusal);
        Assert.AreEqual(outsideObject, offendingIri);
    }

    [TestMethod]
    public void AFamilyPRowMissingAnOutcomeForAClosedPredicateRefusesAsTermKindMismatch()
    {
        // A malformed delivery: one of the thirteen predicates has no row at all for this object,
        // neither a bound value nor the explicit unbound marker.
        var incomplete = AllPPredicateIris()
            .Where(iri => iri != CelexIri)
            .Select(iri => PUnboundRow(GdprRoot, iri))
            .Append(PBoundRow(GdprRoot, CelexIri, new PValue(GdprCelex, IsIri: false, Datatype: XsdString)))
            .ToList();
        incomplete.RemoveAt(0); // drop one predicate's outcome entirely

        var snapshots = Decode(GdprCelex, [], incomplete, [], out var refusal, out _, out _);
        Assert.IsNull(snapshots);
        Assert.AreEqual(EuCellarObjectDecodeRefusal.ObjectFactRowTermKindMismatch, refusal);
    }

    [TestMethod]
    public void AFamilyPRowWhoseValueKindDisagreesWithTheTermRefusesAsTermKindMismatch()
    {
        var badRow = PUnboundRow(GdprRoot, CelexIri) with { };
        // Construct a row claiming value_kind "unbound" while the value term is actually bound.
        var terms = new[]
        {
            RepeatedEnumerationRdfTerm.Iri(GdprRoot),
            RepeatedEnumerationRdfTerm.Iri(CelexIri),
            RepeatedEnumerationRdfTerm.Literal(GdprCelex, XsdString, null),
            RepeatedEnumerationRdfTerm.Literal("unbound", null, null), // disagrees with a bound value
            RepeatedEnumerationRdfTerm.Literal("", null, null),
            RepeatedEnumerationRdfTerm.Literal("", null, null),
            RepeatedEnumerationRdfTerm.Literal("1", XsdInteger, null),
            RepeatedEnumerationRdfTerm.Literal(GdprRoot, null, null),
            RepeatedEnumerationRdfTerm.Literal(CelexIri, null, null),
            RepeatedEnumerationRdfTerm.Literal("unbound", null, null),
            RepeatedEnumerationRdfTerm.Literal(GdprCelex, null, null),
            RepeatedEnumerationRdfTerm.Literal("", null, null),
            RepeatedEnumerationRdfTerm.Literal("", null, null),
        };
        var mismatched = new RepeatedEnumerationRow(
            Array.AsReadOnly(terms),
            Array.AsReadOnly(new[] { terms[0], terms[1], terms[2] }),
            Array.AsReadOnly(terms[7..13]));

        var rows = AllPPredicateIris().Where(iri => iri != CelexIri)
            .Select(iri => PUnboundRow(GdprRoot, iri)).Append(mismatched).ToArray();

        var snapshots = Decode(GdprCelex, [], rows, [], out var refusal, out _, out _);
        Assert.IsNull(snapshots);
        Assert.AreEqual(EuCellarObjectDecodeRefusal.ObjectFactRowTermKindMismatch, refusal);
    }

    // ---- Family X row-shape refusal. ----

    /// <summary>
    /// Test fold-in: before this test, nothing in this file reached
    /// <see cref="EuCellarObjectDecodeRefusal.ExpressionFactRowTermKindMismatch"/> - every family X
    /// test used well-formed rows from <see cref="XBoundRow"/>. This is family P's own
    /// <c>AFamilyPRowWhoseValueKindDisagreesWithTheTermRefusesAsTermKindMismatch</c> mirrored for
    /// family X: a row claims <c>value_kind</c> "unbound" while its own <c>value</c> term is
    /// actually bound.
    /// </summary>
    [TestMethod]
    public void AFamilyXRowWhoseValueKindDisagreesWithTheTermRefusesAsTermKindMismatch()
    {
        var pRows = RootObjectRows(GdprRoot, GdprCelex);
        var terms = new[]
        {
            RepeatedEnumerationRdfTerm.Iri(GdprRoot),
            RepeatedEnumerationRdfTerm.Iri(ExprA),
            RepeatedEnumerationRdfTerm.Iri(ExpressionUsesLanguageIri),
            RepeatedEnumerationRdfTerm.Iri(EnglishLanguageAuthorityIri),
            RepeatedEnumerationRdfTerm.Literal("unbound", null, null), // disagrees with a bound value
            RepeatedEnumerationRdfTerm.Literal("", null, null),
            RepeatedEnumerationRdfTerm.Literal("", null, null),
            RepeatedEnumerationRdfTerm.Literal("1", XsdInteger, null),
            RepeatedEnumerationRdfTerm.Literal(ExprA, null, null),
            RepeatedEnumerationRdfTerm.Literal(ExpressionUsesLanguageIri, null, null),
            RepeatedEnumerationRdfTerm.Literal("unbound", null, null),
            RepeatedEnumerationRdfTerm.Literal(EnglishLanguageAuthorityIri, null, null),
            RepeatedEnumerationRdfTerm.Literal("", null, null),
            RepeatedEnumerationRdfTerm.Literal("", null, null),
        };
        var mismatched = new RepeatedEnumerationRow(
            Array.AsReadOnly(terms),
            Array.AsReadOnly(new[] { terms[0], terms[1], terms[2], terms[3] }),
            Array.AsReadOnly(terms[8..14]));

        var snapshots = Decode(GdprCelex, [], pRows, [mismatched], out var refusal, out _, out _);
        Assert.IsNull(snapshots);
        Assert.AreEqual(EuCellarObjectDecodeRefusal.ExpressionFactRowTermKindMismatch, refusal);
    }

    // ---- Family X closure refusals. ----

    [TestMethod]
    public void AFamilyXRowWhoseParentIsOutsideTheClosureRefusesNamingTheIri()
    {
        var pRows = RootObjectRows(GdprRoot, GdprCelex);
        var xRows = ExpressionRows(CitedRoot, ExprA, EnglishLanguageAuthorityIri);

        var snapshots = Decode(GdprCelex, [], pRows, xRows, out var refusal, out var offendingIri, out _);
        Assert.IsNull(snapshots);
        Assert.AreEqual(EuCellarObjectDecodeRefusal.ExpressionParentNotInClosure, refusal);
        Assert.AreEqual(CitedRoot, offendingIri);
    }

    [TestMethod]
    public void AFamilyXSubjectWithNoBelongsToWorkRowOfItsOwnRefusesAsNotSelfClosed()
    {
        var pRows = RootObjectRows(GdprRoot, GdprCelex);
        // Only the language row is delivered for ExprA; its own expression_belongs_to_work row is
        // missing from this (hostile or corrupted) delivery, so X cannot prove its own closure.
        var xRows = new[]
        {
            XBoundRow(GdprRoot, ExprA, ExpressionUsesLanguageIri, new PValue(EnglishLanguageAuthorityIri)),
        };

        var snapshots = Decode(GdprCelex, [], pRows, xRows, out var refusal, out var offendingIri, out _);
        Assert.IsNull(snapshots);
        Assert.AreEqual(EuCellarObjectDecodeRefusal.ExpressionSubjectNotSelfClosed, refusal);
        Assert.AreEqual(ExprA, offendingIri);
    }

    // ---- Family census refusals, unchanged from D1-05b. ----

    [TestMethod]
    public void ABaseCelexTermThatIsAnIriRefusesAsTermKindMismatch()
    {
        var terms = new RepeatedEnumerationRdfTerm[]
        {
            RepeatedEnumerationRdfTerm.Iri(GdprCelex),
            RepeatedEnumerationRdfTerm.Iri(GdprRoot),
            RepeatedEnumerationRdfTerm.Iri(StateA),
            RepeatedEnumerationRdfTerm.Literal("1", XsdInteger, null),
            RepeatedEnumerationRdfTerm.Literal(StateA, null, null),
        };
        var row = new RepeatedEnumerationRow(
            Array.AsReadOnly(terms),
            Array.AsReadOnly(new[] { terms[0], terms[1], terms[2] }),
            Array.AsReadOnly(new[] { terms[4] }));

        var pRows = RootObjectRows(GdprRoot, GdprCelex);
        var snapshots = Decode(GdprCelex, [row], pRows, [], out var refusal, out _, out _);

        Assert.IsNull(snapshots);
        Assert.AreEqual(EuCellarObjectDecodeRefusal.FamilyRowTermKindMismatch, refusal);
    }

    [TestMethod]
    public void ARowNamingADifferentCelexThanRequestedRefusesAsADuplicateBinding()
    {
        var row = FamilyRow("32013R0575", GdprRoot, StateA);
        var pRows = RootObjectRows(GdprRoot, GdprCelex);

        var snapshots = Decode(GdprCelex, [row], pRows, [], out var refusal, out _, out _);
        Assert.IsNull(snapshots);
        Assert.AreEqual(EuCellarObjectDecodeRefusal.DuplicateSingleValuedBinding, refusal);
    }

    [TestMethod]
    public void AFabricatedOutOfPackBaseRefusesThroughTheSnapshotDoorEvenWithATrustedCelex()
    {
        var notASeed =
            "http://publications.europa.eu/resource/cellar/00000000-0000-0000-0000-000000000000";
        var row = FamilyRow(GdprCelex, notASeed, StateA);
        var pRows = RootObjectRows(notASeed, GdprCelex).Concat(StateObjectRows(StateA, notASeed)).ToArray();

        var snapshots = Decode(GdprCelex, [row], pRows, [], out var refusal, out _, out var snapshotRefusal);
        Assert.IsNull(snapshots);
        Assert.AreEqual(EuCellarObjectDecodeRefusal.ObjectSnapshotRejected, refusal);
        Assert.AreEqual(EuCellarObjectSnapshotRefusal.WorkRootOutsideAppendixAPack, snapshotRefusal);
    }

    // ---- Caller-contract guards. ----

    // ---- Family M at the contracts level (D1-05d REVIEW_RESULT fold-in). ----

    /// <summary>
    /// The decode really reads family M's rows and mints a format observation on the snapshot from
    /// them, naming family M's OWN evidence rather than the sibling families' evidence.
    /// </summary>
    /// <remarks>
    /// Every other call site in this file passes the empty default, so before this test nothing at
    /// the contracts level proved the M parameter was wired to anything at all.
    /// </remarks>
    [TestMethod]
    public void TheDecodeMintsAFormatObservationFromFamilyMsRowsAndNamesFamilyMsOwnEvidence()
    {
        var pRows = RootObjectRows(GdprRoot, GdprCelex);
        var xRows = ExpressionRows(GdprRoot, ExprA, EnglishLanguageAuthorityIri);
        var mRows = new[]
        {
            ManifestationRow(GdprRoot, "print"),
            ManifestationRow(GdprRoot, "xhtml"),
        };

        var snapshots = Decode(
            GdprCelex, [], pRows, xRows, out var refusal, out _, out _, "ev", mRows);

        Assert.AreEqual(EuCellarObjectDecodeRefusal.None, refusal);
        var format = snapshots!.Single().Format;
        Assert.IsNotNull(format);
        Assert.AreEqual(EuManifestationFormat.Xhtml, format!.Format);
        Assert.AreEqual(EuFormatBodyAdmission.BodyAdmitted, format.Admission);
        CollectionAssert.AreEqual(
            new[] { EuManifestationFormat.Xhtml }, format.OrderedCandidates.ToArray());

        // The two refs are built from different labels, so this fails if the decode ever stamps the
        // sibling families' evidence on a format observation again.
        Assert.AreEqual(Ev(ManifestationEvidenceLabel), format.EvidenceRef);
        Assert.AreNotEqual(Ev("ev"), format.EvidenceRef);
    }

    /// <summary>
    /// An object family M says nothing about keeps a null format observation: the typed absence.
    /// </summary>
    [TestMethod]
    public void AnObjectFamilyMSaysNothingAboutKeepsNoFormatObservation()
    {
        var pRows = RootObjectRows(GdprRoot, GdprCelex);
        var xRows = ExpressionRows(GdprRoot, ExprA, EnglishLanguageAuthorityIri);

        var snapshots = Decode(GdprCelex, [], pRows, xRows, out var refusal, out _, out _);

        Assert.AreEqual(EuCellarObjectDecodeRefusal.None, refusal);
        Assert.IsNull(snapshots!.Single().Format);
    }

    /// <summary>
    /// Fix two, at the decode level: an unadmitted manifestation type refuses THAT WORK's format
    /// observation by name and lets the decode deliver every snapshot, rather than refusing the
    /// whole call.
    /// </summary>
    [TestMethod]
    public void AnUnadmittedManifestationTypeQuarantinesThatWorkRatherThanRefusingTheDecode()
    {
        var pRows = RootObjectRows(GdprRoot, GdprCelex);
        var xRows = ExpressionRows(GdprRoot, ExprA, EnglishLanguageAuthorityIri);
        var mRows = new[]
        {
            ManifestationRow(GdprRoot, "epub3"),
            ManifestationRow(GdprRoot, "xhtml"),
        };

        var snapshots = Decode(
            GdprCelex, [], pRows, xRows, out var refusal, out _, out _, "ev", mRows);

        Assert.AreEqual(
            EuCellarObjectDecodeRefusal.None,
            refusal,
            "an unknown publisher token must never refuse the whole decode.");
        Assert.IsNotNull(snapshots);
        var format = snapshots!.Single().Format;
        Assert.IsNotNull(format);
        Assert.AreEqual(EuFormatBodyAdmission.BodyNotAdmitted, format!.Admission);
        Assert.AreEqual("listing_type_not_admitted:epub3", format.ReasonCode);
        Assert.HasCount(0, format.OrderedCandidates);
        Assert.AreNotEqual(
            EuManifestationFormat.Print,
            format.Format,
            "naming print would send the body axis to never_ingest, which an unread listing does " +
            "not license.");
    }

    /// <summary>
    /// The one condition family M still refuses the WHOLE decode for: a row naming a parent outside
    /// this call's own closure. Driven through the real decode rather than by constructing the enum,
    /// so the member is proven reachable.
    /// </summary>
    /// <remarks>
    /// This is a violation of what the call was asked to decode, unlike an unadmitted publisher
    /// token, which is a fact about the office's vocabulary and is now contained to one Work.
    /// </remarks>
    [TestMethod]
    public void AManifestationRowOutsideTheClosureRefusesTheWholeDecodeByName()
    {
        var pRows = RootObjectRows(GdprRoot, GdprCelex);
        var xRows = ExpressionRows(GdprRoot, ExprA, EnglishLanguageAuthorityIri);
        var mRows = new[] { ManifestationRow(CitedRoot, "xhtml") };

        var snapshots = EuCellarObjectDecode.TryDecode(
            GdprCelex, [], FamilyProfile, pRows, ObjectFactsProfile, xRows, ExpressionFactsProfile,
            mRows, ManifestationFactsProfile, Ev(ManifestationEvidenceLabel),
            EuActForm.Regulation, Ev("ev"),
            out var refusal, out var offendingIri, out _, out var listingRefusal);

        Assert.IsNull(snapshots);
        Assert.AreEqual(EuCellarObjectDecodeRefusal.ManifestationListingRefused, refusal);
        Assert.AreEqual(EuManifestationListingRefusal.ListingParentNotInClosure, listingRefusal);
        Assert.AreEqual(CitedRoot, offendingIri, "the refusal must name the offending parent.");
    }

    [TestMethod]
    public void ANullManifestationRowsThrows()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => EuCellarObjectDecode.TryDecode(
            GdprCelex, [], FamilyProfile, [], ObjectFactsProfile, [], ExpressionFactsProfile,
            null!, ManifestationFactsProfile, Ev(ManifestationEvidenceLabel),
            EuActForm.Regulation, Ev("null-m-rows"), out _, out _, out _, out _));
    }

    [TestMethod]
    public void ANullManifestationProfileThrows()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => EuCellarObjectDecode.TryDecode(
            GdprCelex, [], FamilyProfile, [], ObjectFactsProfile, [], ExpressionFactsProfile,
            [], null!, Ev(ManifestationEvidenceLabel),
            EuActForm.Regulation, Ev("null-m-profile"), out _, out _, out _, out _));
    }

    [TestMethod]
    public void ANullManifestationEvidenceRefThrows()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => EuCellarObjectDecode.TryDecode(
            GdprCelex, [], FamilyProfile, [], ObjectFactsProfile, [], ExpressionFactsProfile,
            [], ManifestationFactsProfile, null!,
            EuActForm.Regulation, Ev("null-m-evidence"), out _, out _, out _, out _));
    }

    /// <summary>One family-M row: one Work's own listed manifestation type, as a plain xsd:string.</summary>
    private static RepeatedEnumerationRow ManifestationRow(string parentIri, string listedType)
    {
        var terms = new[]
        {
            RepeatedEnumerationRdfTerm.Iri(parentIri),
            RepeatedEnumerationRdfTerm.Literal(listedType, XsdString, null),
            RepeatedEnumerationRdfTerm.Literal("literal", null, null),
            RepeatedEnumerationRdfTerm.Literal(XsdString, null, null),
            RepeatedEnumerationRdfTerm.Literal("", null, null),
            RepeatedEnumerationRdfTerm.Literal(parentIri, null, null),
            RepeatedEnumerationRdfTerm.Literal("literal", null, null),
            RepeatedEnumerationRdfTerm.Literal(listedType, null, null),
            RepeatedEnumerationRdfTerm.Literal(XsdString, null, null),
            RepeatedEnumerationRdfTerm.Literal("", null, null),
        };
        return new RepeatedEnumerationRow(
            Array.AsReadOnly(terms),
            Array.AsReadOnly(new[] { terms[0], terms[1] }),
            Array.AsReadOnly(terms[5..10]));
    }

    [TestMethod]
    public void ARequestedCelexOutsideAppendixARefusesAsACallerContractViolation()
    {
        Assert.ThrowsExactly<ArgumentException>(() => EuCellarObjectDecode.TryDecode(
            "31995L0046", [], FamilyProfile, [], ObjectFactsProfile, [], ExpressionFactsProfile, [], ManifestationFactsProfile, Ev(ManifestationEvidenceLabel),
            EuActForm.Regulation, Ev("not-a-seed"), out _, out _, out _, out _));
    }

    [TestMethod]
    public void ABlankRequestedCelexThrows()
    {
        Assert.ThrowsExactly<ArgumentException>(() => EuCellarObjectDecode.TryDecode(
            "   ", [], FamilyProfile, [], ObjectFactsProfile, [], ExpressionFactsProfile, [], ManifestationFactsProfile, Ev(ManifestationEvidenceLabel),
            EuActForm.Regulation, Ev("blank"), out _, out _, out _, out _));
    }

    [TestMethod]
    public void ANullFamilyRowsThrows()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => EuCellarObjectDecode.TryDecode(
            GdprCelex, null!, FamilyProfile, [], ObjectFactsProfile, [], ExpressionFactsProfile, [], ManifestationFactsProfile, Ev(ManifestationEvidenceLabel),
            EuActForm.Regulation, Ev("null-rows"), out _, out _, out _, out _));
    }

    [TestMethod]
    public void ANullObjectFactRowsThrows()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => EuCellarObjectDecode.TryDecode(
            GdprCelex, [], FamilyProfile, null!, ObjectFactsProfile, [], ExpressionFactsProfile, [], ManifestationFactsProfile, Ev(ManifestationEvidenceLabel),
            EuActForm.Regulation, Ev("null-p-rows"), out _, out _, out _, out _));
    }

    [TestMethod]
    public void ANullExpressionFactRowsThrows()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => EuCellarObjectDecode.TryDecode(
            GdprCelex, [], FamilyProfile, [], ObjectFactsProfile, null!, ExpressionFactsProfile, [], ManifestationFactsProfile, Ev(ManifestationEvidenceLabel),
            EuActForm.Regulation, Ev("null-x-rows"), out _, out _, out _, out _));
    }

    [TestMethod]
    public void ANullObjectFactProfileThrows()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => EuCellarObjectDecode.TryDecode(
            GdprCelex, [], FamilyProfile, [], null!, [], ExpressionFactsProfile, [], ManifestationFactsProfile, Ev(ManifestationEvidenceLabel),
            EuActForm.Regulation, Ev("null-p-profile"), out _, out _, out _, out _));
    }

    [TestMethod]
    public void ANullExpressionFactProfileThrows()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => EuCellarObjectDecode.TryDecode(
            GdprCelex, [], FamilyProfile, [], ObjectFactsProfile, [], null!, [], ManifestationFactsProfile, Ev(ManifestationEvidenceLabel),
            EuActForm.Regulation, Ev("null-x-profile"), out _, out _, out _, out _));
    }

    [TestMethod]
    public void ANullEvidenceRefThrows()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => EuCellarObjectDecode.TryDecode(
            GdprCelex, [], FamilyProfile, [], ObjectFactsProfile, [], ExpressionFactsProfile, [], ManifestationFactsProfile, Ev(ManifestationEvidenceLabel),
            EuActForm.Regulation, null!, out _, out _, out _, out _));
    }

    [TestMethod]
    public void AnUndefinedRecordFormThrows()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => EuCellarObjectDecode.TryDecode(
            GdprCelex, [], FamilyProfile, [], ObjectFactsProfile, [], ExpressionFactsProfile, [], ManifestationFactsProfile, Ev(ManifestationEvidenceLabel),
            (EuActForm)999, Ev("bad-form"), out _, out _, out _, out _));
    }

    // ---- Construction surface. ----

    [TestMethod]
    public void TheDoorItselfIsNeverProducedBecauseItIsNeverAValue()
    {
        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            ConstructionSurface.Of(typeof(EuCellarObjectDecode)).ToArray());
        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            ConstructionSurface.ProducersIn(
                typeof(EuCellarObjectDecode).Assembly, typeof(EuCellarObjectDecode), true).ToArray());
    }

    [TestMethod]
    public void TheRefusalEnumHasExactlyTwelveMembersAndTwoHandOutPaths()
    {
        const string N = "Lex.V3.Contracts.Source.Europe.";
        const string Refusal = N + "EuCellarObjectDecodeRefusal";
        CollectionAssert.AreEqual(
            new[]
            {
                "base-constructor protected instance System.Enum::.ctor() -> System.Enum",
                "base-constructor protected instance System.ValueType::.ctor() -> System.ValueType",
                "field public static " + Refusal + "::ConsolidatedBasedOnEdgeDisagreesWithFamily -> "
                    + Refusal,
                "field public static " + Refusal + "::ContentClassClosurePositionMismatch -> " + Refusal,
                "field public static " + Refusal + "::DuplicateSingleValuedBinding -> " + Refusal,
                "field public static " + Refusal + "::ExpressionFactRowTermKindMismatch -> " + Refusal,
                "field public static " + Refusal + "::ExpressionParentNotInClosure -> " + Refusal,
                "field public static " + Refusal + "::ExpressionSubjectNotSelfClosed -> " + Refusal,
                "field public static " + Refusal + "::FamilyRowTermKindMismatch -> " + Refusal,
                "field public static " + Refusal + "::ManifestationListingRefused -> " + Refusal,
                "field public static " + Refusal + "::None -> " + Refusal,
                "field public static " + Refusal + "::ObjectFactRowNotInClosure -> " + Refusal,
                "field public static " + Refusal + "::ObjectFactRowTermKindMismatch -> " + Refusal,
                "field public static " + Refusal + "::ObjectSnapshotRejected -> " + Refusal,
            },
            ConstructionSurface.Of(typeof(EuCellarObjectDecodeRefusal)).ToArray());

        const string C = "Lex.V3.Contracts.";
        const string RowList =
            "System.Collections.Generic.IReadOnlyList<Lex.V3.Contracts.Source.Core.RepeatedEnumerationRow>, "
            + "Lex.V3.Contracts.Source.Core.RepeatedEnumerationInterpretationProfile, ";
        CollectionAssert.AreEqual(
            new[]
            {
                // BuildOneObject is TryDecode's own private per-object helper: it also carries an
                // `out EuCellarObjectDecodeRefusal` parameter, so it is a second real hand-out path,
                // not only TryDecode itself.
                "by-ref-method private static " + N + "EuCellarObjectDecode::BuildOneObject(System.String, "
                + "System.Boolean, System.String, "
                + "System.Collections.Generic.IReadOnlyList<" + N + "EuCellarObjectDecode+ObjectFactRow>, "
                + "System.Collections.Generic.IReadOnlyList<" + N + "EuCellarObjectDecode+ExpressionFactRow>, "
                + N + "EuFormatObservation, "
                + C + "EuActForm, Lex.V3.Contracts.Source.Core.SourceArtifactRef, out " + Refusal
                + "&, out System.String&, out " + N + "EuCellarObjectSnapshotRefusal&) -> "
                + N + "EuCellarObjectSnapshot",
                "by-ref-method public static " + N + "EuCellarObjectDecode::TryDecode(System.String, "
                + RowList + RowList + RowList + RowList
                + "Lex.V3.Contracts.Source.Core.SourceArtifactRef, " + C + "EuActForm, "
                + "Lex.V3.Contracts.Source.Core.SourceArtifactRef, out " + Refusal + "&, out "
                + "System.String&, out " + N
                + "EuCellarObjectSnapshotRefusal&, out " + N + "EuManifestationListingRefusal&) -> "
                + "System.Collections.Generic.IReadOnlyList<" + N + "EuCellarObjectSnapshot>",
            },
            ConstructionSurface.ProducersIn(
                typeof(EuCellarObjectDecodeRefusal).Assembly, typeof(EuCellarObjectDecodeRefusal), true)
                .ToArray());
    }
}
