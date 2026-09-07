using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Contracts.Source.Scope;
using Lex.V3.TestSupport;
using Lex.V3.Tests.Contracts.Source.Absence;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Luxembourg;

[TestClass]
public sealed class LuxembourgScopeResolverTests
{
    private const string Jolux =
        "http://data.legilux.public.lu/resource/ontology/jolux#";
    private const string RdfType =
        "http://www.w3.org/1999/02/22-rdf-syntax-ns#type";
    private const string N = "Lex.V3.Contracts.Source.Luxembourg.";

    [TestMethod]
    public void EmptyExactObservationStaysFailClosedAndAccountsForEveryDimensionState()
    {
        var resolved = Assert.IsInstanceOfType<LuxembourgProfileResolution.Resolved>(
            Profile().Resolve(Proven([Observation(ObservationRef)])));

        var resource = resolved.Resources.Single();
        Assert.AreEqual(
            LuScopeTerminalState.MissingPublisherValue,
            resource.Dimensions.Record.State);
        Assert.AreEqual(LuScopeTerminalState.Point, resource.Dimensions.Body.State);
        Assert.HasCount(70, resolved.Accounting);
        foreach (var dimension in Enum.GetValues<LuxembourgDimension>())
        {
            Assert.HasCount(
                7,
                resolved.Accounting.Where(row => row.Dimension == dimension));
        }

        CollectionAssert.AreEqual(
            new[] { ObservationRef },
            resolved.OrderedEvidenceArtifacts.ToArray());
    }

    /// <summary>
    /// #409, clauses S2-A03 and S2-A01: when the publisher asserted no <c>rdf:type</c>, the record
    /// dimension and the record selector say the same thing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHAT THEY USED TO SAY. The selector's value set began with the object's own publisher URI,
    /// which is never empty, and <c>Selector</c> answers
    /// <see cref="ScopeSelectorState.PublisherValueAbsent"/> only on an empty set — so that state
    /// was unreachable for this dimension by construction. The resolution reported
    /// <c>missing_resource_class</c> while the wire reported a present value, about the same
    /// observation.
    /// </para>
    /// <para>
    /// Both halves were already pinned before this guard existed — the dimension in the test
    /// directly above, the selector in <c>LuxembourgSourceProfileAdversarialProofTests</c> — and
    /// both had been green the whole time, because they live in different files and nothing read
    /// them together. That is exactly what this asserts: not either statement alone, but that the
    /// two agree. Nothing is lost by dropping the IRI from the value set; <c>BuildScopeInput</c>
    /// hands <c>ObjectRef</c> to <c>ScopeObjectReductionInput</c> as that record's own first field,
    /// so the subject is on the wire either way — as identity, which is what it is.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void TheRecordSelectorAgreesWithItsDimensionWhenNoClassWasAsserted()
    {
        var profile = Profile();
        var resolved = Assert.IsInstanceOfType<LuxembourgProfileResolution.Resolved>(
            profile.Resolve(Proven([Observation(ObservationRef)])));

        var record = resolved.Resources.Single().Dimensions.Record;
        Assert.AreEqual(LuScopeTerminalState.MissingPublisherValue, record.State);
        Assert.AreEqual("missing_resource_class", record.ReasonCode);

        var keys = profile.ScopeBinding.OrderedSelectorMemberOrdinals
            .Select(ordinal => profile.ScopeBinding.OrderedMembers[ordinal].MemberKey)
            .ToArray();
        var index = Array.IndexOf(keys, "selector.record");
        Assert.IsTrue(index >= 0, "the record selector must exist for this to check anything.");
        var selector = resolved.ScopeInputs.Single().Selectors[index];

        Assert.AreEqual(
            ScopeSelectorState.PublisherValueAbsent,
            selector.State,
            "the dimension says the required publisher value is missing, so the wire must not say "
                + "a value is present.");
        Assert.AreEqual(0, selector.CanonicalValues.Count);
    }

    /// <summary>
    /// The narrower half of the same defect: a resource carrying <c>jolux:typeDocument</c> but no
    /// <c>rdf:type</c> must still publish an absent record selector.
    /// </summary>
    /// <remarks>
    /// This is the case my first repair missed and the reviewer found. Dropping the object IRI made
    /// the empty-observation case honest, but the value set still carried <c>types</c>, and the
    /// record dimension is derived from <c>classes</c> alone — <c>classes.Length == 0</c> is what
    /// produces <c>missing_resource_class</c>. So a typeDocument with no rdf:type kept the wire
    /// saying "present" while the resolution said "missing", exactly as before, only for a smaller
    /// set of observations. <c>typeDocument</c> already has <c>selector.publication_family</c>, so
    /// carrying it here represented one publisher assertion twice and aligned with neither
    /// dimension.
    /// </remarks>
    [TestMethod]
    public void ATypeDocumentWithNoResourceClassStillPublishesAnAbsentRecordSelector()
    {
        var profile = Profile();
        var observation = new LuxembourgResourceObservation(
            ObjectRef(ActIri),
            ObservationRef,
            [Iri(ActIri, Jolux + "typeDocument", JoluxAuthority + "resource-type/LOI")],
            [],
            new LuxembourgSparqlRightsChannelObservations(
                ObservationRef, SparqlEnumerationRef, []),
            new LuxembourgInFileRightsChannelObservations(
                ObservationRef, InFileEnumerationRef, []));
        var resolved = Assert.IsInstanceOfType<LuxembourgProfileResolution.Resolved>(
            profile.Resolve(Proven([observation])));

        var record = resolved.Resources.Single().Dimensions.Record;
        Assert.AreEqual(LuScopeTerminalState.MissingPublisherValue, record.State);
        Assert.AreEqual("missing_resource_class", record.ReasonCode);

        var keys = profile.ScopeBinding.OrderedSelectorMemberOrdinals
            .Select(ordinal => profile.ScopeBinding.OrderedMembers[ordinal].MemberKey)
            .ToArray();
        var selector = resolved.ScopeInputs.Single()
            .Selectors[Array.IndexOf(keys, "selector.record")];

        Assert.AreEqual(
            ScopeSelectorState.PublisherValueAbsent,
            selector.State,
            "no rdf:type was asserted, so the record selector must say absent whatever else the "
                + "publisher said about this resource.");
        Assert.AreEqual(
            0,
            selector.CanonicalValues.Count,
            "typeDocument belongs to selector.publication_family; representing it here too would "
                + "put one publisher assertion on two selectors and align it with neither.");
    }

    [TestMethod]
    public void ResourceObservationFromAnotherRunFailsBeforeScopeProjection()
    {
        var failed = Assert.IsInstanceOfType<LuxembourgProfileResolution.Failed>(
            Profile().Resolve(Proven([Observation(OtherObservationRef)])));

        Assert.AreEqual(
            LuxembourgProfileResolutionFailureCode.EvidenceBindingRejected,
            failed.Failure.Code);
    }

    [TestMethod]
    public void RightsCollectionsMustBeBoundToTheResourceObservationRun()
    {
        var observation = new LuxembourgResourceObservation(
            ObjectRef(),
            ObservationRef,
            [],
            [],
            new LuxembourgSparqlRightsChannelObservations(
                OtherObservationRef,
                SparqlEnumerationRef,
                []),
            new LuxembourgInFileRightsChannelObservations(
                ObservationRef,
                InFileEnumerationRef,
                []));

        var failed = Assert.IsInstanceOfType<LuxembourgProfileResolution.Failed>(
            Profile().Resolve(Proven([observation])));

        Assert.AreEqual(
            LuxembourgProfileResolutionFailureCode.EvidenceBindingRejected,
            failed.Failure.Code);
    }

    [TestMethod]
    public void ExactWemiTupleIsExposedAndItsWordingManifestationIsAccepted()
    {
        var resolved = Assert.IsInstanceOfType<LuxembourgProfileResolution.Resolved>(
            Profile().Resolve(Proven([BodyObservation()])));

        var resource = resolved.Resources.Single();
        Assert.HasCount(1, resource.WemiTopology.Candidates);
        Assert.HasCount(1, resource.BodyJoin.Candidates);
        var candidate = resource.BodyJoin.Candidates.Single();
        Assert.AreEqual(ExpressionIri, candidate.WemiCandidate.ExpressionIri);
        Assert.AreEqual(ManifestationIri, candidate.WemiCandidate.ManifestationIri);
        // Was Withheld with all eight milestone blockers. Owner principle
        // (RULING lex-event-20260904T205636383Z-e92b888b62c24df29fe3f8c1be5016f0): a law that can
        // legitimately be ingested is ingested, and none of the four legitimate reasons applies to
        // an exact tuple whose publisher listed a wording manifestation.
        Assert.AreEqual(
            LuxembourgBodyCandidateDisposition.AcceptedCandidate,
            candidate.Disposition);
        Assert.IsEmpty(candidate.BlockerCodes);
        Assert.AreEqual(
            LuScopeTerminalState.AcceptedCandidate,
            resource.Dimensions.PublicationFamily.State);
        Assert.AreEqual(
            LuScopeTerminalState.NotApplicable,
            resource.Dimensions.Rights.State);
        Assert.AreEqual(
            "not_applicable_no_manifestation",
            resource.Dimensions.Rights.ReasonCode);
        Assert.AreEqual(
            LuScopeTerminalState.NotApplicable,
            resource.Dimensions.Language.State);
        Assert.AreEqual(
            LuScopeTerminalState.NotApplicable,
            resource.Dimensions.Format.State);
        Assert.AreEqual(
            LuScopeTerminalState.NotApplicable,
            resource.Dimensions.Authenticity.State);
        Assert.AreEqual(
            LuScopeTerminalState.TypedQuarantine,
            resource.Dimensions.Transport.State);
        // The Body axis reaches its accepting arm. It had none: all five of ResolveBody's return
        // paths withheld, so this axis was the only one in the resolver with no accepting state and
        // every real Luxembourg manifest had an accepted fraction of zero.
        Assert.AreEqual(
            LuScopeTerminalState.AcceptedCandidate,
            resource.Dimensions.Body.State);
        Assert.AreEqual(
            "accepted_publisher_listed_wording_manifestation",
            resource.Dimensions.Body.ReasonCode);
        Assert.IsTrue(resolved.ScopeInputs.Single().Selectors.Any(selector =>
            selector.CanonicalValues.Any(value => value.StartsWith(
                "body_join_sha256:",
                StringComparison.Ordinal))));
    }

    [TestMethod]
    public void ConflictingPublisherValuesRemainTypedConflictsInScopeInput()
    {
        const string pdfFormat = JoluxAuthority + "user-format/pdf";
        const string xmlFormat = JoluxAuthority + "user-format/xml";
        var observation = new LuxembourgResourceObservation(
            ObjectRef(ManifestationIri),
            ObservationRef,
            [
                Iri(ManifestationIri, RdfType, Jolux + "Manifestation"),
                Iri(ManifestationIri, Jolux + "userFormat", xmlFormat),
                Iri(ManifestationIri, Jolux + "userFormat", pdfFormat),
            ],
            [],
            new LuxembourgSparqlRightsChannelObservations(
                ObservationRef,
                SparqlEnumerationRef,
                []),
            new LuxembourgInFileRightsChannelObservations(
                ObservationRef,
                InFileEnumerationRef,
                []));
        var resolved = Assert.IsInstanceOfType<LuxembourgProfileResolution.Resolved>(
            Profile().Resolve(Proven([observation])));

        var resource = resolved.Resources.Single();
        Assert.AreEqual(
            LuScopeTerminalState.TypedQuarantine,
            resource.Dimensions.Format.State);
        Assert.AreEqual(
            "typed_quarantine_selector_conflict",
            resource.Dimensions.Format.ReasonCode);

        var selector = resolved.ScopeInputs.Single().Selectors.Single(candidate =>
            candidate.CanonicalValues.Contains(pdfFormat));
        Assert.AreEqual(ScopeSelectorState.PublisherValueConflict, selector.State);
        Assert.AreEqual(
            ScopeSelectorEvidenceKind.ObservedConflictingValueSet,
            selector.EvidenceKind);
        CollectionAssert.AreEqual(
            new[] { pdfFormat, xmlFormat },
            selector.CanonicalValues.ToArray());
        Assert.IsNotNull(selector.EvidenceArtifactOrdinal);
        Assert.IsNotNull(selector.CauseMemberOrdinal);
        Assert.IsNull(selector.RuleOrdinal);
        CollectionAssert.AreEqual(
            new[]
            {
                ObservationRef,
                SparqlEnumerationRef,
                InFileEnumerationRef,
            },
            resolved.OrderedEvidenceArtifacts.ToArray());
    }

    /// <summary>
    /// E0(b) on #409, clauses S2-A03 and S2-A01: a manifestation whose publisher declared no licence
    /// publishes an absent rights selector, not a present one holding our own enumeration digest.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHAT THIS USED TO PUBLISH. Both rights selectors prepended
    /// <c>$"enumeration:{...EnumerationRef.Sha256}"</c> to their value set. The canonicaliser drops
    /// only empty strings, so that element always survived, and <c>Selector</c> answers
    /// <see cref="ScopeSelectorState.PublisherValueAbsent"/> only on an empty set - so for rights
    /// the absent state was unreachable by construction. Publisher silence published as
    /// <see cref="ScopeSelectorState.PublisherValuePresent"/> over a one-element set whose only
    /// member was a digest we computed ourselves, while the dimension beside it correctly said
    /// <c>lu_rights_observed_empty_channel</c>. A gap rendered as a present value, and our own
    /// artifact rendered as the publisher's own assertion.
    /// </para>
    /// <para>
    /// The two halves are asserted together on purpose. Checking the selector alone would let the
    /// dimension drift away from it again; the wire and the resolution have to agree that this
    /// channel was enumerated and found nothing, which is a different statement from both "never
    /// enumerated" and "found a value".
    /// </para>
    /// </remarks>
    [TestMethod]
    public void AManifestationWithNoDeclaredLicencePublishesAnAbsentRightsSelector()
    {
        var observation = new LuxembourgResourceObservation(
            ObjectRef(ManifestationIri),
            ObservationRef,
            [Iri(ManifestationIri, RdfType, Jolux + "Manifestation")],
            [],
            // Each channel carries a row FOR this manifestation whose licence list is empty. That
            // is the "observed and found nothing" case, and it is deliberately not the same as
            // handing the channel no row at all: an unseeded manifestation is
            // ChannelEnumerationUnproven, because the channel never looked at it. Both are honest,
            // and keeping them apart is the point of the dimension.
            new LuxembourgSparqlRightsChannelObservations(
                ObservationRef,
                SparqlEnumerationRef,
                [new LuxembourgRightsChannelObservation(
                    ManifestationIri, ObservationRef, SparqlEnumerationRef, [])]),
            new LuxembourgInFileRightsChannelObservations(
                ObservationRef,
                InFileEnumerationRef,
                [new LuxembourgRightsChannelObservation(
                    ManifestationIri, ObservationRef, InFileEnumerationRef, [])]));
        var resolved = Assert.IsInstanceOfType<LuxembourgProfileResolution.Resolved>(
            Profile().Resolve(Proven([observation])));

        // The resolution's own account of the same fact, so the wire cannot drift away from it.
        var rights = resolved.Resources.Single().Dimensions.Rights;
        Assert.AreEqual(LuScopeTerminalState.MissingPublisherValue, rights.State);
        Assert.AreEqual("missing_rights_value", rights.ReasonCode);

        var artifacts = resolved.OrderedEvidenceArtifacts.ToArray();
        var selectors = resolved.ScopeInputs.Single().Selectors;
        foreach (var enumerationRef in new[] { SparqlEnumerationRef, InFileEnumerationRef })
        {
            var ordinal = Array.IndexOf(artifacts, enumerationRef);
            Assert.IsTrue(ordinal >= 0, "the enumeration must still be carried as evidence.");
            var selector = selectors.Single(candidate =>
                candidate.EvidenceArtifactOrdinal == ordinal);

            Assert.AreEqual(
                ScopeSelectorState.PublisherValueAbsent,
                selector.State,
                "a publisher that declared no licence must publish as absent.");
            Assert.AreEqual(
                ScopeSelectorEvidenceKind.CompleteObservationAbsence,
                selector.EvidenceKind);
            Assert.AreEqual(
                0,
                selector.CanonicalValues.Count,
                "an absent selector carries no values at all.");
            Assert.IsFalse(
                selector.CanonicalValues.Any(value =>
                    value.StartsWith("enumeration:", StringComparison.Ordinal)),
                "our own enumeration digest is evidence, and must never be published as though the "
                    + "publisher had asserted it.");
        }
    }

    /// <summary>
    /// The rights selectors describe the manifestation being resolved, not whatever else the
    /// channel happened to enumerate: a sibling's licence is never published as this object's own.
    /// </summary>
    /// <remarks>
    /// THE HALF THE FIRST REPAIR MISSED, AND THE REVIEWER FOUND. Removing the enumeration sentinel
    /// made the absent state reachable, but both selectors still flattened
    /// <c>Observations.SelectMany(row =&gt; row.LicenceIris)</c> over every row in the channel. A
    /// channel's rows cover every manifestation the connected assertion graph reached, so an object
    /// the publisher said nothing about still published <c>PublisherValuePresent</c> carrying a
    /// sibling's CC BY. The empty-only guard above passes straight over that, because in its
    /// fixture there is no sibling to borrow from - which is exactly why this case is separate and
    /// why one guard was not enough. <c>ResolveRights</c> had always bound the dimension to this
    /// object through <c>LuxembourgRightsChannels.Resolve(resourceIri, ...)</c>; only the selector
    /// was unbound, so the wire disagreed with the resolution about whose licence it was.
    /// </remarks>
    [TestMethod]
    public void ARightsSelectorNeverPublishesASiblingManifestationsLicence()
    {
        const string admittingLicence = "http://creativecommons.org/licenses/by/4.0/";
        const string siblingIri = ExpressionIri + "/sibling";
        var observation = new LuxembourgResourceObservation(
            ObjectRef(ManifestationIri),
            ObservationRef,
            [Iri(ManifestationIri, RdfType, Jolux + "Manifestation")],
            [],
            // The selected manifestation is observed empty; the sibling carries the admitting
            // licence. Both rows live in the same channel collection, which is the real shape.
            new LuxembourgSparqlRightsChannelObservations(
                ObservationRef,
                SparqlEnumerationRef,
                [
                    new LuxembourgRightsChannelObservation(
                        ManifestationIri, ObservationRef, SparqlEnumerationRef, []),
                    new LuxembourgRightsChannelObservation(
                        siblingIri, ObservationRef, SparqlEnumerationRef, [admittingLicence]),
                ]),
            new LuxembourgInFileRightsChannelObservations(
                ObservationRef,
                InFileEnumerationRef,
                [
                    new LuxembourgRightsChannelObservation(
                        ManifestationIri, ObservationRef, InFileEnumerationRef, []),
                    new LuxembourgRightsChannelObservation(
                        siblingIri, ObservationRef, InFileEnumerationRef, [admittingLicence]),
                ]));
        var resolved = Assert.IsInstanceOfType<LuxembourgProfileResolution.Resolved>(
            Profile().Resolve(Proven([observation])));

        var rights = resolved.Resources.Single().Dimensions.Rights;
        Assert.AreEqual(
            LuScopeTerminalState.MissingPublisherValue,
            rights.State,
            "the dimension has always been bound to the selected manifestation.");

        var artifacts = resolved.OrderedEvidenceArtifacts.ToArray();
        var selectors = resolved.ScopeInputs.Single().Selectors;
        foreach (var enumerationRef in new[] { SparqlEnumerationRef, InFileEnumerationRef })
        {
            var selector = selectors.Single(candidate =>
                candidate.EvidenceArtifactOrdinal == Array.IndexOf(artifacts, enumerationRef));

            Assert.IsFalse(
                selector.CanonicalValues.Contains(admittingLicence),
                "a licence the publisher asserted about a different manifestation must never be "
                    + "published as this one's value.");
            Assert.AreEqual(
                ScopeSelectorState.PublisherValueAbsent,
                selector.State,
                "this manifestation was observed and carried no licence, so it publishes absent "
                    + "even though a sibling in the same channel carried one.");
            Assert.AreEqual(0, selector.CanonicalValues.Count);
        }
    }

    /// <summary>
    /// E0(b), #409, clauses S2-A05 then S2-A03: a licence the channel could not represent
    /// quarantines, and is never reported as an observed empty channel.
    /// </summary>
    /// <remarks>
    /// The two states this separates are easy to conflate and mean opposite things.
    /// <c>missing_rights_value</c> / <c>lu_rights_observed_empty_channel</c> asserts that the
    /// publisher's channel was read and declared no licence — a settled negative fact a later stage
    /// may rely on. This case is a gap in our own reading: the publisher said something and we
    /// could not carry it. Before the count existed, the second published as the first.
    /// </remarks>
    [TestMethod]
    public void ALicenceTheChannelCouldNotRepresentQuarantinesRatherThanReadingAsEmpty()
    {
        var observation = new LuxembourgResourceObservation(
            ObjectRef(ManifestationIri),
            ObservationRef,
            [Iri(ManifestationIri, RdfType, Jolux + "Manifestation")],
            [],
            new LuxembourgSparqlRightsChannelObservations(
                ObservationRef,
                SparqlEnumerationRef,
                [new LuxembourgRightsChannelObservation(
                    ManifestationIri, ObservationRef, SparqlEnumerationRef, [],
                    unrepresentableLicenceAssertions: 1)]),
            new LuxembourgInFileRightsChannelObservations(
                ObservationRef,
                InFileEnumerationRef,
                [new LuxembourgRightsChannelObservation(
                    ManifestationIri, ObservationRef, InFileEnumerationRef, [],
                    unrepresentableLicenceAssertions: 1)]));
        var resolved = Assert.IsInstanceOfType<LuxembourgProfileResolution.Resolved>(
            Profile().Resolve(Proven([observation])));

        var rights = resolved.Resources.Single().Dimensions.Rights;
        Assert.AreEqual(
            LuScopeTerminalState.TypedQuarantine,
            rights.State,
            "an unreadable licence is a gap in our reading, never a publisher value we can call "
                + "missing.");
        Assert.AreEqual("typed_quarantine_unrepresentable_licence_shape", rights.ReasonCode);
        Assert.AreNotEqual(
            "missing_rights_value",
            rights.ReasonCode,
            "this is the exact code the defect published, and publishing it here would assert the "
                + "channel was observed empty when it was observed and not understood.");
    }

    [TestMethod]
    public void ManifestationAuthenticityRemainsOnItsExactSubjectWithoutSiblingOrRootLift()
    {
        const string pdfManifestation = ExpressionIri + "/pdf";
        const string pdfItem =
            "http://data.legilux.public.lu/filestore/body-fr.pdf";
        const string nonOfficial = JoluxAuthority + "statut-version/non-officiel";
        const string official = JoluxAuthority + "statut-version/officiel";
        var resolved = Assert.IsInstanceOfType<LuxembourgProfileResolution.Resolved>(
            Profile().Resolve(Proven([BodyObservation(additionalAssertions:
            [
                Iri(ExpressionIri, Jolux + "isEmbodiedBy", pdfManifestation),
                Iri(pdfManifestation, RdfType, Jolux + "Manifestation"),
                Iri(pdfManifestation, Jolux + "userFormat", JoluxAuthority + "user-format/pdf"),
                Iri(pdfManifestation, Jolux + "isExemplifiedBy", pdfItem),
                Iri(ManifestationIri, Jolux + "legalValue", nonOfficial),
                Iri(pdfManifestation, Jolux + "legalValue", official),
            ])])));

        var resource = resolved.Resources.Single();
        Assert.AreEqual(
            LuScopeTerminalState.NotApplicable,
            resource.Dimensions.Authenticity.State);
        Assert.AreEqual(
            "not_applicable_no_expression_or_manifestation",
            resource.Dimensions.Authenticity.ReasonCode);

        var legalValues = resource.Assertions
            .Where(candidate => candidate.Assertion.PredicateIri == Jolux + "legalValue")
            .OrderBy(candidate => candidate.Assertion.SubjectIri, StringComparer.Ordinal)
            .ToArray();
        Assert.HasCount(2, legalValues);
        Assert.IsTrue(legalValues.All(candidate =>
            candidate.Disposition == LuxembourgAssertionDisposition.Accepted));
        Assert.AreEqual(
            nonOfficial,
            legalValues.Single(candidate =>
                candidate.Assertion.SubjectIri == ManifestationIri)
                .Assertion.ObjectIriOrLexical);
        Assert.AreEqual(
            official,
            legalValues.Single(candidate =>
                candidate.Assertion.SubjectIri == pdfManifestation)
                .Assertion.ObjectIriOrLexical);
    }

    [TestMethod]
    public void NonHttpAbsolutePublisherValueIsRetainedAndTypedQuarantined()
    {
        const string unknownLanguage = "urn:lex:language:unruled";
        var profile = LuxembourgProfiles.Opened(new LuxembourgVocabularySnapshot(
            ObservationRef,
            CompleteEnumerationRef,
            [
                .. VerifiedLuxembourgSourceProfile.RequiredIriVocabulary,
                new LuxembourgIriVocabularyValue(
                    LuxembourgVocabularyKind.Language,
                    unknownLanguage),
            ],
            []));
        var observation = new LuxembourgResourceObservation(
            ObjectRef(ExpressionIri),
            ObservationRef,
            [
                Iri(ExpressionIri, RdfType, Jolux + "Expression"),
                Iri(ExpressionIri, Jolux + "language", unknownLanguage),
            ],
            [],
            new LuxembourgSparqlRightsChannelObservations(
                ObservationRef,
                SparqlEnumerationRef,
                []),
            new LuxembourgInFileRightsChannelObservations(
                ObservationRef,
                InFileEnumerationRef,
                []));

        var resolved = Assert.IsInstanceOfType<LuxembourgProfileResolution.Resolved>(
            profile.Resolve(Proven([observation])));
        var resource = resolved.Resources.Single();
        Assert.AreEqual(
            LuScopeTerminalState.TypedQuarantine,
            resource.Dimensions.Language.State);
        Assert.AreEqual(
            "typed_quarantine_unknown_language",
            resource.Dimensions.Language.ReasonCode);
        var assertion = resource.Assertions.Single(candidate =>
            candidate.Assertion.ObjectIriOrLexical == unknownLanguage);
        Assert.AreEqual(
            LuxembourgAssertionDisposition.TypedQuarantine,
            assertion.Disposition);
    }

    [TestMethod]
    public void NonHttpAbsoluteAssertionSubjectIsRetainedAndTypedQuarantined()
    {
        const string externalSubject = "urn:lex:external-subject";
        var resolved = Assert.IsInstanceOfType<LuxembourgProfileResolution.Resolved>(
            Profile().Resolve(Proven([BodyObservation(additionalAssertions:
            [
                Iri(externalSubject, RdfType, Jolux + "Act"),
            ])])));

        var assertion = resolved.Resources.Single().Assertions.Single(candidate =>
            candidate.Assertion.SubjectIri == externalSubject);
        Assert.AreEqual(
            LuxembourgAssertionDisposition.TypedQuarantine,
            assertion.Disposition);
    }

    [TestMethod]
    public void NonHttpPreviousItemSubjectStaysRawQuarantineWithoutEnteringWemiProjection()
    {
        const string externalSubject = "urn:lex:external-manifestation";
        const string replacedItem =
            "http://data.legilux.public.lu/file/external-replaced-body.xml";
        var resolved = Assert.IsInstanceOfType<LuxembourgProfileResolution.Resolved>(
            Profile().Resolve(Proven([BodyObservation(additionalAssertions:
            [
                Iri(externalSubject, Jolux + "previousIsExemplifiedBy", replacedItem),
            ])])));

        var resource = resolved.Resources.Single();
        var assertion = resource.Assertions.Single(candidate =>
            candidate.Assertion.SubjectIri == externalSubject);
        Assert.AreEqual(
            LuxembourgAssertionDisposition.TypedQuarantine,
            assertion.Disposition);
        Assert.IsFalse(resource.WemiTopology.PreviousItems.Any(candidate =>
            candidate.ManifestationIri == externalSubject));
    }

    [TestMethod]
    public void NonHttpAbsoluteRelationTargetIsRetainedWithoutBecomingTransportAuthority()
    {
        const string externalTarget = "urn:lex:external-relation-target";
        var observation = new LuxembourgResourceObservation(
            ObjectRef(),
            ObservationRef,
            [],
            [
                new LuxembourgObservedRelation(
                    ActIri,
                    Jolux + "cites",
                    externalTarget,
                    ObservationRef),
            ],
            new LuxembourgSparqlRightsChannelObservations(
                ObservationRef,
                SparqlEnumerationRef,
                []),
            new LuxembourgInFileRightsChannelObservations(
                ObservationRef,
                InFileEnumerationRef,
                []));

        var resolved = Assert.IsInstanceOfType<LuxembourgProfileResolution.Resolved>(
            Profile().Resolve(Proven([observation])));
        var relation = resolved.Resources.Single().Relations.Single();
        Assert.AreEqual(externalTarget, relation.ObjectIri);
        Assert.AreEqual(LuxembourgRelationDisposition.Accepted, relation.Disposition);
        Assert.AreEqual(
            LuScopeTerminalState.AcceptedMetadata,
            resolved.Resources.Single().Dimensions.Relation.State);
        Assert.AreEqual(
            LuScopeTerminalState.NotApplicable,
            resolved.Resources.Single().Dimensions.Transport.State);
    }

    [TestMethod]
    public void NonHttpConsolidatesTargetIsRetainedAsMissingTargetShape()
    {
        const string externalTarget = "urn:lex:external-consolidation-target";
        var observation = new LuxembourgResourceObservation(
            ObjectRef(),
            ObservationRef,
            [
                Iri(ActIri, RdfType, Jolux + "Act"),
                Iri(ActIri, Jolux + "typeDocument", JoluxAuthority + "resource-type/TC"),
            ],
            [
                new LuxembourgObservedRelation(
                    ActIri,
                    Jolux + "consolidates",
                    externalTarget,
                    ObservationRef),
            ],
            new LuxembourgSparqlRightsChannelObservations(
                ObservationRef,
                SparqlEnumerationRef,
                []),
            new LuxembourgInFileRightsChannelObservations(
                ObservationRef,
                InFileEnumerationRef,
                []));

        var resolved = Assert.IsInstanceOfType<LuxembourgProfileResolution.Resolved>(
            Profile().Resolve(Proven([observation])));
        var relation = resolved.Resources.Single().Relations.Single();
        Assert.AreEqual(externalTarget, relation.ObjectIri);
        Assert.AreEqual(
            LuxembourgRelationDisposition.TypedQuarantine,
            relation.Disposition);
        Assert.AreEqual(
            LuxembourgConsolidatesShapeState.TypedQuarantineTargetResourceMissing,
            relation.ConsolidatesShape?.State);
    }

    [TestMethod]
    public void CurrentAndPreviousItemIdentityBothChangeTheBoundBodyJoinDigest()
    {
        const string otherCurrent =
            "http://data.legilux.public.lu/filestore/body-fr-other.xml";
        const string previous =
            "http://data.legilux.public.lu/file/replaced-body-fr.xml";
        var profile = Profile();

        var baseline = Assert.IsInstanceOfType<LuxembourgProfileResolution.Resolved>(
            profile.Resolve(Proven([BodyObservation()])));
        var currentChanged = Assert.IsInstanceOfType<LuxembourgProfileResolution.Resolved>(
            profile.Resolve(Proven([BodyObservation(otherCurrent)])));
        var previousAdded = Assert.IsInstanceOfType<LuxembourgProfileResolution.Resolved>(
            profile.Resolve(Proven([BodyObservation(previousItemIri: previous)])));

        Assert.AreNotEqual(BodyJoinSelector(baseline), BodyJoinSelector(currentChanged));
        Assert.AreNotEqual(BodyJoinSelector(baseline), BodyJoinSelector(previousAdded));
    }

    [TestMethod]
    public void MalformedIriTermIsRetainedButCannotClassifyOrBuildAWemiPath()
    {
        const string xsdString = "http://www.w3.org/2001/XMLSchema#string";
        var malformedRootType = new LuxembourgObservedAssertion(
            ActIri,
            RdfType,
            LuxembourgAssertionObjectKind.Iri,
            Jolux + "Act",
            xsdString,
            string.Empty,
            ObservationRef);
        var resolved = Assert.IsInstanceOfType<LuxembourgProfileResolution.Resolved>(
            Profile().Resolve(Proven([BodyObservation(
                removePredicate: RdfType,
                additionalAssertions: [malformedRootType])])));

        var resource = resolved.Resources.Single();
        Assert.IsFalse(resource.WemiTopology.Candidates.Any(candidate =>
            candidate.Disposition ==
            LuxembourgWemiCandidateDisposition.StructurallyConsistent));
        Assert.AreEqual(
            LuxembourgAssertionDisposition.TypedQuarantine,
            resource.Assertions.Single(assertion =>
                assertion.Assertion == malformedRootType).Disposition);
        Assert.AreEqual(
            LuScopeTerminalState.TypedQuarantine,
            resource.Dimensions.PublicationFamily.State);
    }

    [TestMethod]
    public void UnrelatedWemiSubtreeIsRetainedOnlyAsTypedQuarantine()
    {
        const string orphanManifestation =
            "http://data.legilux.public.lu/eli/orphan/manifestation";
        const string orphanCurrentItem =
            "http://data.legilux.public.lu/filestore/orphan.xml";
        const string orphanPreviousItem =
            "http://data.legilux.public.lu/file/orphan.xml";
        var observation = BodyObservation(additionalAssertions:
        [
            Iri(orphanManifestation, RdfType, Jolux + "Manifestation"),
            Iri(
                orphanManifestation,
                Jolux + "userFormat",
                JoluxAuthority + "user-format/xml"),
            Iri(orphanManifestation, Jolux + "isExemplifiedBy", orphanCurrentItem),
            Iri(
                orphanManifestation,
                Jolux + "previousIsExemplifiedBy",
                orphanPreviousItem),
        ]);

        var resolved = Assert.IsInstanceOfType<LuxembourgProfileResolution.Resolved>(
            Profile().Resolve(Proven([observation])));
        var resource = resolved.Resources.Single();
        var orphanAssertions = resource.Assertions.Where(assertion =>
            assertion.Assertion.SubjectIri == orphanManifestation).ToArray();

        Assert.HasCount(4, orphanAssertions);
        Assert.IsTrue(orphanAssertions.All(assertion =>
            assertion.Disposition == LuxembourgAssertionDisposition.TypedQuarantine));
        Assert.IsFalse(resource.WemiTopology.Candidates.Any(candidate =>
            candidate.ItemIri == orphanCurrentItem));
        Assert.AreEqual(
            LuxembourgPreviousItemDisposition.TypedQuarantineManifestationUnproven,
            resource.WemiTopology.PreviousItems.Single(previous =>
                previous.ItemIri == orphanPreviousItem).Disposition);
    }

    [TestMethod]
    [DataRow("Expression")]
    [DataRow("Manifestation")]
    public void MixedRootWemiRolesCannotQualifyPublicationFamily(string conflictingRole)
    {
        var resolved = Assert.IsInstanceOfType<LuxembourgProfileResolution.Resolved>(
            Profile().Resolve(Proven([BodyObservation(additionalAssertions:
            [
                Iri(ActIri, RdfType, Jolux + conflictingRole),
            ])])));

        Assert.AreEqual(
            LuScopeTerminalState.TypedQuarantine,
            resolved.Resources.Single().Dimensions.PublicationFamily.State);
    }

    /// <summary>
    /// E0(d), #409, clause S2-A05: four different findings that all quarantine keep four different
    /// reasons, so drift is distinguishable from a structural exclusion and from a missing gate.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHAT THIS USED TO PUBLISH. Every one of these cases resolved to
    /// <c>typed_quarantine_role_not_admitted</c>. The record said the type filled no admitted role
    /// — true of the last case and misleading about the other three. In particular a publisher
    /// value this profile has not settled, which is exactly the drift S2-A05 says must fail closed
    /// INTO TYPED EVIDENCE, failed closed into evidence that named a different finding.
    /// </para>
    /// <para>
    /// The four are asserted in one test on purpose. Individually each is a string comparison; the
    /// property that matters is that the four are DISTINCT, and that only shows when they are read
    /// together. The final assertion states it directly rather than leaving it to the reader to
    /// notice, so collapsing any two of them again fails here even if each arm still passes.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void EachQuarantiningFindingKeepsItsOwnReason()
    {
        // DIV is structurally excluded; NOT_A_SETTLED_TYPE is a value this profile never settled;
        // RCSF is a real regulator role whose qualifying evidence this observation does not carry;
        // LOI is a real ordinary candidate with neither consolidation nor as-published evidence.
        var cases = new (string Type, string ExpectedReason)[]
        {
            ("DIV", "typed_quarantine_structurally_excluded_type"),
            ("NOT_A_SETTLED_TYPE", "typed_quarantine_unsettled_type_vocabulary"),
            ("RCSF", "typed_quarantine_regulator_evidence_absent"),
            ("LOI", "typed_quarantine_ordinary_evidence_absent"),
        };

        var observed = new List<string>();
        foreach (var (type, expectedReason) in cases)
        {
            var resolved = Assert.IsInstanceOfType<LuxembourgProfileResolution.Resolved>(
                Profile().Resolve(Proven([TypedRoleObservation(type)])));
            var family = resolved.Resources.Single().Dimensions.PublicationFamily;

            Assert.AreEqual(
                LuScopeTerminalState.TypedQuarantine,
                family.State,
                $"{type} must still quarantine; this slice changes the reason, never the state.");
            Assert.AreEqual(
                expectedReason,
                family.ReasonCode,
                $"{type} must publish the finding that actually applies to it.");
            observed.Add(family.ReasonCode);
        }

        Assert.AreEqual(
            cases.Length,
            observed.Distinct(StringComparer.Ordinal).Count(),
            "four different findings must not share a reason code. This is the assertion the old "
                + "shared catchall would fail: every one of these cases published "
                + "typed_quarantine_role_not_admitted, so a reader could not tell publisher drift "
                + "from a rule we apply on purpose.");
    }

    [TestMethod]
    public void ATcActCarriesItsOwnCoordinatedTextRoleSeparatelyFromBucketMembership()
    {
        var resolved = Assert.IsInstanceOfType<LuxembourgProfileResolution.Resolved>(
            Profile().Resolve(Proven([TypedRoleObservation("TC")])));

        var resource = resolved.Resources.Single();
        Assert.AreEqual(
            LuScopeTerminalState.AcceptedCandidate,
            resource.Dimensions.PublicationFamily.State);
        Assert.AreEqual(
            "accepted_exact_family",
            resource.Dimensions.PublicationFamily.ReasonCode);
        Assert.AreEqual(LuxembourgTypedRoleKind.CoordinatedText, resource.TypedRole.Kind);
        Assert.AreEqual(ActIri, resource.TypedRole.OwnCoordinate);
        // Pinned as the literal wire value rather than the named constant: comparing against
        // LuxembourgTypedRoleDisclosures.ConsolidationWithoutLegalEffect itself can only ever
        // fail if production code stops assigning that exact same constant reference, never if
        // someone silently changes the constant's own string value.
        Assert.AreEqual(
            "disclosure_consolidation_without_legal_effect",
            resource.TypedRole.DisclosureCode);
    }

    [TestMethod]
    public void ARectActCarriesItsOwnCorrigendumRoleSeparatelyFromBucketMembership()
    {
        var resolved = Assert.IsInstanceOfType<LuxembourgProfileResolution.Resolved>(
            Profile().Resolve(Proven([TypedRoleObservation("RECT")])));

        var resource = resolved.Resources.Single();
        Assert.AreEqual(
            LuScopeTerminalState.AcceptedCandidate,
            resource.Dimensions.PublicationFamily.State);
        Assert.AreEqual(
            "accepted_exact_family",
            resource.Dimensions.PublicationFamily.ReasonCode);
        Assert.AreEqual(LuxembourgTypedRoleKind.Corrigendum, resource.TypedRole.Kind);
        Assert.AreEqual(ActIri, resource.TypedRole.OwnCoordinate);
        // Pinned as the literal wire value; see the TC test above for why the named constant
        // alone is not enough.
        Assert.AreEqual(
            "disclosure_corrective_material_never_corrected_act",
            resource.TypedRole.DisclosureCode);
    }

    [TestMethod]
    public void AnAccActCarriesItsOwnConstitutionalReviewDecisionRoleSeparatelyFromBucketMembership()
    {
        // Reviewer RULING lex-event-20260904T002301246Z-7699c8fdd1ad4868a7d94dcb152fbf57: R5.1 rule
        // 6's own evidence is the publisher's typeDocument assertion carrying the exact ACC IRI, so
        // ACC is admitted through PriorityCandidateTypes bucket membership exactly like TC and RECT
        // above, and separately carries its own constitutional_review_decision role -- never an
        // unconditional refusal on the type token, which was this lane's own earlier (and
        // reviewer-corrected) reading of the 23:48Z SCOPE_RULING.
        var resolved = Assert.IsInstanceOfType<LuxembourgProfileResolution.Resolved>(
            Profile().Resolve(Proven([TypedRoleObservation("ACC")])));

        var resource = resolved.Resources.Single();
        Assert.AreEqual(
            LuScopeTerminalState.AcceptedCandidate,
            resource.Dimensions.PublicationFamily.State);
        Assert.AreEqual(
            "accepted_exact_family",
            resource.Dimensions.PublicationFamily.ReasonCode);
        Assert.AreEqual(
            LuxembourgTypedRoleKind.ConstitutionalReviewDecision,
            resource.TypedRole.Kind);
        Assert.AreEqual(ActIri, resource.TypedRole.OwnCoordinate);
        // Pinned as the literal wire value; see the TC test above for why the named constant
        // alone is not enough.
        Assert.AreEqual(
            "disclosure_constitutional_review_decision_never_statutory_text",
            resource.TypedRole.DisclosureCode);
    }

    [TestMethod]
    public void AnActWhoseAccSignalArrivesOnlyViaTitleCarriesNoConstitutionalReviewRole()
    {
        // R5.1 rule 6's evidence is the publisher's own typeDocument assertion carrying the exact
        // ACC IRI, and nothing else may substitute (reviewer RULING
        // lex-event-20260904T002301246Z-7699c8fdd1ad4868a7d94dcb152fbf57: "no spelling, title,
        // relation or alternate format may widen it"). This resource's typeDocument assertion names
        // an ordinary LOI, but carries the exact ACC IRI as the object of a separately registered
        // assertion predicate, title, so a mutant that widened ResolveTypedRole's match to that
        // predicate, instead of reading TypeDocument alone, would wrongly admit this as a
        // constitutional-review role. Split from the isMemberOf channel below so each predicate has
        // its own failure signal. (The third named channel, an alternate format, cannot even be
        // constructed here: UserFormatPrefix + "ACC" is not a registered UserFormat vocabulary
        // value, so injecting it fails resolution outright with UnknownVocabularyDrift before
        // TypedRole is ever computed -- the publisher's own closed vocabulary already forecloses
        // that path structurally.)
        var accIri = JoluxAuthority + "resource-type/ACC";
        var observation = new LuxembourgResourceObservation(
            ObjectRef(),
            ObservationRef,
            [
                Iri(ActIri, RdfType, Jolux + "Act"),
                Iri(ActIri, Jolux + "typeDocument", JoluxAuthority + "resource-type/LOI"),
                Iri(ActIri, Jolux + "title", accIri),
            ],
            [],
            new LuxembourgSparqlRightsChannelObservations(
                ObservationRef,
                SparqlEnumerationRef,
                []),
            new LuxembourgInFileRightsChannelObservations(
                ObservationRef,
                InFileEnumerationRef,
                []));

        var resolved = Assert.IsInstanceOfType<LuxembourgProfileResolution.Resolved>(
            Profile().Resolve(Proven([observation])));

        Assert.AreEqual(
            LuxembourgTypedRoleKind.NotApplicable,
            resolved.Resources.Single().TypedRole.Kind);
    }

    [TestMethod]
    public void AnActWhoseAccSignalArrivesOnlyViaIsMemberOfCarriesNoConstitutionalReviewRole()
    {
        // Same rule as the title test above, the relation channel: this resource's typeDocument
        // assertion names an ordinary LOI, but carries the exact ACC IRI as the object of
        // isMemberOf, so a mutant that widened ResolveTypedRole's match to that relation predicate
        // would wrongly admit this as a constitutional-review role.
        var accIri = JoluxAuthority + "resource-type/ACC";
        var observation = new LuxembourgResourceObservation(
            ObjectRef(),
            ObservationRef,
            [
                Iri(ActIri, RdfType, Jolux + "Act"),
                Iri(ActIri, Jolux + "typeDocument", JoluxAuthority + "resource-type/LOI"),
                Iri(ActIri, Jolux + "isMemberOf", accIri),
            ],
            [],
            new LuxembourgSparqlRightsChannelObservations(
                ObservationRef,
                SparqlEnumerationRef,
                []),
            new LuxembourgInFileRightsChannelObservations(
                ObservationRef,
                InFileEnumerationRef,
                []));

        var resolved = Assert.IsInstanceOfType<LuxembourgProfileResolution.Resolved>(
            Profile().Resolve(Proven([observation])));

        Assert.AreEqual(
            LuxembourgTypedRoleKind.NotApplicable,
            resolved.Resources.Single().TypedRole.Kind);
    }

    [TestMethod]
    public void AnOrdinaryAcceptedActCarriesNoTypedRole()
    {
        var resolved = Assert.IsInstanceOfType<LuxembourgProfileResolution.Resolved>(
            Profile().Resolve(Proven([BodyObservation()])));

        var resource = resolved.Resources.Single();
        Assert.AreEqual(
            LuScopeTerminalState.AcceptedCandidate,
            resource.Dimensions.PublicationFamily.State);
        Assert.AreEqual(LuxembourgTypedRoleKind.NotApplicable, resource.TypedRole.Kind);
        Assert.IsNull(resource.TypedRole.OwnCoordinate);
        Assert.IsNull(resource.TypedRole.DisclosureCode);
    }

    [TestMethod]
    public void AnActWithConflictingTypeDocumentValuesCarriesNoTypedRoleEitherWay()
    {
        var observation = new LuxembourgResourceObservation(
            ObjectRef(),
            ObservationRef,
            [
                Iri(ActIri, RdfType, Jolux + "Act"),
                Iri(ActIri, Jolux + "typeDocument", JoluxAuthority + "resource-type/TC"),
                Iri(ActIri, Jolux + "typeDocument", JoluxAuthority + "resource-type/RECT"),
            ],
            [],
            new LuxembourgSparqlRightsChannelObservations(
                ObservationRef,
                SparqlEnumerationRef,
                []),
            new LuxembourgInFileRightsChannelObservations(
                ObservationRef,
                InFileEnumerationRef,
                []));

        var resolved = Assert.IsInstanceOfType<LuxembourgProfileResolution.Resolved>(
            Profile().Resolve(Proven([observation])));

        var resource = resolved.Resources.Single();
        Assert.AreEqual(
            LuScopeTerminalState.TypedQuarantine,
            resource.Dimensions.PublicationFamily.State);
        Assert.AreEqual(
            "typed_quarantine_selector_conflict",
            resource.Dimensions.PublicationFamily.ReasonCode);
        Assert.AreEqual(LuxembourgTypedRoleKind.NotApplicable, resource.TypedRole.Kind);
    }

    [TestMethod]
    public void ATypeDocumentTcAssertionOnANonActSubjectCarriesNoTypedRole()
    {
        var observation = new LuxembourgResourceObservation(
            ObjectRef(ExpressionIri),
            ObservationRef,
            [
                Iri(ExpressionIri, RdfType, Jolux + "Expression"),
                Iri(ExpressionIri, Jolux + "typeDocument", JoluxAuthority + "resource-type/TC"),
            ],
            [],
            new LuxembourgSparqlRightsChannelObservations(
                ObservationRef,
                SparqlEnumerationRef,
                []),
            new LuxembourgInFileRightsChannelObservations(
                ObservationRef,
                InFileEnumerationRef,
                []));

        var resolved = Assert.IsInstanceOfType<LuxembourgProfileResolution.Resolved>(
            Profile().Resolve(Proven([observation])));

        var resource = resolved.Resources.Single();
        Assert.AreEqual(LuxembourgTypedRoleKind.NotApplicable, resource.TypedRole.Kind);
        Assert.IsNull(resource.TypedRole.OwnCoordinate);
    }

    [TestMethod]
    public void AnActClassResourceWithNoTypeDocumentAssertionCarriesNoTypedRoleAndBodyQuarantine()
    {
        // No existing test drove an Act-class resource with zero typeDocument assertions at all,
        // as opposed to a wrong (non-Act subject) or conflicting (multiple values) one: both of
        // those are covered above, but ResolveTypedRole's "types.Length != 1" branch was never
        // actually exercised for the zero case.
        var observation = new LuxembourgResourceObservation(
            ObjectRef(),
            ObservationRef,
            [
                Iri(ActIri, RdfType, Jolux + "Act"),
            ],
            [],
            new LuxembourgSparqlRightsChannelObservations(
                ObservationRef,
                SparqlEnumerationRef,
                []),
            new LuxembourgInFileRightsChannelObservations(
                ObservationRef,
                InFileEnumerationRef,
                []));

        var resolved = Assert.IsInstanceOfType<LuxembourgProfileResolution.Resolved>(
            Profile().Resolve(Proven([observation])));

        var resource = resolved.Resources.Single();
        Assert.AreEqual(LuxembourgTypedRoleKind.NotApplicable, resource.TypedRole.Kind);
        Assert.IsNull(resource.TypedRole.OwnCoordinate);
        Assert.AreEqual(LuScopeTerminalState.TypedQuarantine, resource.Dimensions.Body.State);
        Assert.AreEqual(
            "typed_quarantine_publication_type_absent",
            resource.Dimensions.Body.ReasonCode);

        // Item 18 (R5.1 rule 11, Candidate 5 line 613): the family selector's canonical values are
        // the typeDocument IRIs alone, never folded together with the resource's rdf:type class
        // IRIs, so a genuinely absent typeDocument assertion reads PublisherValueAbsent here, not
        // PublisherValuePresent from the leftover class IRI. Located by the fixed ordinal the
        // resolver's own selectors array assigns this selector, never by recognising a value shape
        // (ScopeSelectorEvidence carries no axis or dimension field, and a value-shape search the
        // resolver produced can never fail to be satisfied by that same resolver's output).
        var familySelector = resolved.ScopeInputs.Single()
            .Selectors[LuxembourgScopeResolver.PublicationFamilySelectorIndex];
        Assert.AreEqual(ScopeSelectorState.PublisherValueAbsent, familySelector.State);
        Assert.AreEqual(
            ScopeSelectorEvidenceKind.CompleteObservationAbsence,
            familySelector.EvidenceKind);
        Assert.HasCount(0, familySelector.CanonicalValues);
    }

    [TestMethod]
    public void AnActClassCombinedWithAMetadataSupportClassAndNoTypeDocumentStillTypedQuarantines()
    {
        // Fold-in from item 18's review verdict (Opus lens on head 14b5bcee,
        // lex-event-20260904T034830674Z-07fe2476daac40fa82eb092cb094838c): the IsActClass branch
        // above is checked first, so it displaces the two pre-existing fallbacks below it,
        // PointSupportClasses and MetadataSupportClasses. This drives exactly that displacement:
        // an Act with zero typeDocument assertions that ALSO carries jolux:Work, a
        // MetadataSupportClasses member, must still land on TypedQuarantine with
        // typed_quarantine_publication_type_absent, never on the AcceptedMetadata outcome that
        // jolux:Work alone would otherwise reach. Conservative reading of rule 11 for an Act,
        // confirmed by the reviewer; untested before this fold-in.
        var observation = new LuxembourgResourceObservation(
            ObjectRef(),
            ObservationRef,
            [
                Iri(ActIri, RdfType, Jolux + "Act"),
                Iri(ActIri, RdfType, Jolux + "Work"),
            ],
            [],
            new LuxembourgSparqlRightsChannelObservations(
                ObservationRef,
                SparqlEnumerationRef,
                []),
            new LuxembourgInFileRightsChannelObservations(
                ObservationRef,
                InFileEnumerationRef,
                []));

        var resolved = Assert.IsInstanceOfType<LuxembourgProfileResolution.Resolved>(
            Profile().Resolve(Proven([observation])));

        var resource = resolved.Resources.Single();
        Assert.AreEqual(LuScopeTerminalState.TypedQuarantine, resource.Dimensions.Body.State);
        Assert.AreEqual(
            "typed_quarantine_publication_type_absent",
            resource.Dimensions.Body.ReasonCode);
    }

    [TestMethod]
    public void PublicationFamilySelectorIndexIsBoundToItsOwnSelectorKeyNotJustUsedByLocators()
    {
        // Fold-in from item 18's review verdict: PublicationFamilySelectorIndex was used twice
        // above (the two tests locating this selector) but asserted nowhere, so nothing proved it
        // was actually the right ordinal -- a reorder of the resolver's own `selectors` array in
        // BuildScopeInput could silently point this constant at the wrong selector, and those
        // locator-based tests would start passing against the wrong data with no failure signal.
        // Bind it here to VerifiedLuxembourgSourceProfile.SelectorKeys, the independent
        // member-key list both the profile and this resolver read the same fixed order from
        // (confirmed by reading VerifiedLuxembourgSourceProfile.SelectorKeys itself, and by the
        // end-to-end selector-signature order that
        // LuxembourgSourceProfileAdversarialProofTests.SelectorAndProjectionInventoriesCarryEveryExpectedStateAndEvidenceKind
        // already pins for the resolver's real output). A reorder of either array without the
        // other now fails this assertion, or that one, instead of silently relocating what a
        // locator finds.
        Assert.AreEqual(
            "selector.publication_family",
            VerifiedLuxembourgSourceProfile.SelectorKeys[
                LuxembourgScopeResolver.PublicationFamilySelectorIndex]);
    }

    [TestMethod]
    public void ThePublicationFamilySelectorCanonicalValuesNeverFoldInTheResourceClassIri()
    {
        // A mutation that restores the old `[.. classes, .. types]` fold would add the resource's
        // Act class IRI to this selector's canonical values, turning this single-value set into
        // two. Driven with a typeDocument present (TC) rather than absent, precisely so the
        // canonical-value set is non-empty either way and only its exact content, not its mere
        // presence, can catch the fold.
        var resolved = Assert.IsInstanceOfType<LuxembourgProfileResolution.Resolved>(
            Profile().Resolve(Proven([TypedRoleObservation("TC")])));

        var familySelector = resolved.ScopeInputs.Single()
            .Selectors[LuxembourgScopeResolver.PublicationFamilySelectorIndex];
        Assert.AreEqual(ScopeSelectorState.PublisherValuePresent, familySelector.State);
        CollectionAssert.AreEqual(
            new[] { JoluxAuthority + "resource-type/TC" },
            familySelector.CanonicalValues.ToArray());
    }

    /// <summary>
    /// The two typed-role output types, pinned the way
    /// <see cref="LuxembourgConstructionSurfaceTests"/> pins the resolver's other repeated-
    /// enumeration proof types, and following the same fold-in convention as
    /// <c>LuxembourgAssertionVocabularyTests</c> and <c>LuxembourgRelationVocabularyTests</c>: each
    /// fold-in's own new types get their construction-surface pin beside the tests that exercise
    /// them, rather than in the canonical pin file.
    /// </summary>
    [TestMethod]
    public void ATypedRoleKindExposesOnlyItsFourNamedValues()
    {
        // Transcribed from ConstructionSurface.Of's actual output, per this project's
        // print-then-transcribe technique (see LuxembourgConstructionSurfaceTests.cs's remarks).
        CollectionAssert.AreEqual(
            new[]
            {
                "base-constructor protected instance System.Enum::.ctor() -> System.Enum",
                "base-constructor protected instance System.ValueType::.ctor() -> System.ValueType",
                "field public static " + N
                    + "LuxembourgTypedRoleKind::ConstitutionalReviewDecision -> "
                    + N + "LuxembourgTypedRoleKind",
                "field public static " + N + "LuxembourgTypedRoleKind::CoordinatedText -> "
                    + N + "LuxembourgTypedRoleKind",
                "field public static " + N + "LuxembourgTypedRoleKind::Corrigendum -> "
                    + N + "LuxembourgTypedRoleKind",
                "field public static " + N + "LuxembourgTypedRoleKind::NotApplicable -> "
                    + N + "LuxembourgTypedRoleKind",
            },
            ConstructionSurface.Of(typeof(LuxembourgTypedRoleKind)).ToArray());
    }

    [TestMethod]
    public void ATypedRoleResolutionHasExactlyFourCheckedFactoriesAndNoOtherProducer()
    {
        // Transcribed from ConstructionSurface.Of's actual output, per this project's
        // print-then-transcribe technique (see LuxembourgConstructionSurfaceTests.cs's remarks).
        CollectionAssert.AreEqual(
            new[]
            {
                "constructor private instance " + N + "LuxembourgTypedRoleResolution::.ctor("
                    + N + "LuxembourgTypedRoleKind, System.String?, System.String?) -> "
                    + N + "LuxembourgTypedRoleResolution",
                "constructor private instance " + N + "LuxembourgTypedRoleResolution::.ctor("
                    + N + "LuxembourgTypedRoleResolution) -> " + N
                    + "LuxembourgTypedRoleResolution",
                "constructor private static " + N + "LuxembourgTypedRoleResolution::.cctor() -> "
                    + N + "LuxembourgTypedRoleResolution",
                "field internal static " + N
                    + "LuxembourgTypedRoleResolution::NotApplicableInstance -> "
                    + N + "LuxembourgTypedRoleResolution",
                "method internal static " + N + "LuxembourgTypedRoleResolution::"
                    + "AcceptedConstitutionalReviewDecision(System.String) -> "
                    + N + "LuxembourgTypedRoleResolution",
                "method internal static " + N
                    + "LuxembourgTypedRoleResolution::AcceptedCoordinatedText(System.String) -> "
                    + N + "LuxembourgTypedRoleResolution",
                "method internal static " + N
                    + "LuxembourgTypedRoleResolution::AcceptedCorrigendum(System.String) -> "
                    + N + "LuxembourgTypedRoleResolution",
                "method public instance " + N + "LuxembourgTypedRoleResolution::<Clone>$() -> "
                    + N + "LuxembourgTypedRoleResolution",
            },
            ConstructionSurface.Of(typeof(LuxembourgTypedRoleResolution)).ToArray());

        // Fold-in: paired the way the sibling Luxembourg pin file pairs every Of pin with a
        // ProducersIn assertion. The resolver's own ResolveTypedRole, the per-resource projection
        // closure that carries it into the anonymous type ResolveTypedRole's caller builds, and
        // LuxembourgResourceResolution's own TypedRole property (and its backing field) are the
        // only places elsewhere in Contracts that hand out a typed-role resolution.
        CollectionAssert.AreEqual(
            new[]
            {
                "field private instance " + N
                    + "LuxembourgResourceResolution::<TypedRole>k__BackingField -> "
                    + N + "LuxembourgTypedRoleResolution",
                // The display-class ordinal moved from 24 to 27 when D1-06c-LU-2's repair made the
                // three userFormat sets internal and added KnownUserFormatIris beside them (RULING
                // lex-event-20260904T194556163Z-dd9191017eaf4c3b83ea04862933006f item three). The
                // compiler numbers generated types by declaration position; this is not a new way
                // to hand out a typed-role resolution. Re-printed after the change, not guessed.
                "method internal instance " + N + "LuxembourgScopeResolver+<>c__DisplayClass27_0"
                    + "::<Resolve>b__3(" + N + "LuxembourgResourceObservation) -> "
                    + "<>f__AnonymousType0<" + N + "LuxembourgResourceObservation, "
                    + "Lex.V3.Contracts.LuScopeDimensions, "
                    + "System.Collections.Generic.IReadOnlyList<"
                    + N + "LuxembourgResolvedAssertion>, "
                    + "System.Collections.Generic.IReadOnlyList<" + N
                    + "LuxembourgResolvedRelation>, " + N + "LuxembourgWemiTopologyResolution, "
                    + N + "LuxembourgBodyJoinResolution, " + N + "LuxembourgTypedRoleResolution>",
                "method private static " + N + "LuxembourgScopeResolver::ResolveTypedRole("
                    + N + "LuxembourgResourceObservation) -> "
                    + N + "LuxembourgTypedRoleResolution",
                "property public instance " + N + "LuxembourgResourceResolution::TypedRole() -> "
                    + N + "LuxembourgTypedRoleResolution",
            },
            ConstructionSurface.ProducersIn(
                typeof(LuxembourgTypedRoleResolution).Assembly,
                typeof(LuxembourgTypedRoleResolution),
                true).ToArray(),
            "something other than the resolver now hands out a typed-role resolution");

        // The compiler-generated display-class ordinal above (24_0, was 23_0 before item 18 added
        // a new member to LuxembourgScopeResolver ahead of it) shifts whenever unrelated members
        // are added to the class, even without touching ResolveTypedRole itself or adding any new
        // closure -- exactly the brittleness item 15's reviewer flagged in this same pin. Re-print
        // and re-transcribe this assertion's expected array whenever LuxembourgScopeResolver next
        // gains or loses a member ahead of ResolveTypedRole's own closure.
    }

    /// <summary>
    /// The proof door every scope resolution now goes through
    /// (<see cref="LuxembourgProvenResourceObservations"/>). The proof is a REAL one from
    /// <see cref="AbsenceFixtures"/>, built through AbsenceFamilyEnumerationProof's own TryCreate
    /// from a real delivery comparison, so these tests exercise the same door production does
    /// rather than a relaxed one.
    /// </summary>
    private static LuxembourgProvenResourceObservations Proven(
        params LuxembourgResourceObservation[] observations) =>
        LuxembourgProvenResourceObservations.RequireProven(AbsenceFixtures.Proof(), observations);

    private static LuxembourgResourceObservation TypedRoleObservation(string typeDocumentSuffix) =>
        new(
            ObjectRef(),
            ObservationRef,
            [
                Iri(ActIri, RdfType, Jolux + "Act"),
                Iri(ActIri, Jolux + "typeDocument", JoluxAuthority + "resource-type/" + typeDocumentSuffix),
            ],
            [],
            new LuxembourgSparqlRightsChannelObservations(
                ObservationRef,
                SparqlEnumerationRef,
                []),
            new LuxembourgInFileRightsChannelObservations(
                ObservationRef,
                InFileEnumerationRef,
                []));

    private static VerifiedLuxembourgSourceProfile Profile() =>
        LuxembourgProfiles.Opened(new LuxembourgVocabularySnapshot(
            ObservationRef,
            CompleteEnumerationRef,
            VerifiedLuxembourgSourceProfile.RequiredIriVocabulary,
            []));

    private static LuxembourgResourceObservation Observation(SourceArtifactRef runRef) => new(
        ObjectRef(),
        runRef,
        [],
        [],
        new LuxembourgSparqlRightsChannelObservations(
            runRef,
            SparqlEnumerationRef,
            []),
        new LuxembourgInFileRightsChannelObservations(
            runRef,
            InFileEnumerationRef,
            []));

    private static LuxembourgResourceObservation BodyObservation(
        string itemIri = ItemIri,
        string? previousItemIri = null,
        string? removePredicate = null,
        IReadOnlyList<LuxembourgObservedAssertion>? additionalAssertions = null)
    {
        var assertions = new List<LuxembourgObservedAssertion>
        {
            Iri(ActIri, RdfType, Jolux + "Act"),
            Iri(ActIri, Jolux + "typeDocument", JoluxAuthority + "resource-type/LOI"),
            Iri(ActIri, Jolux + "isMemberOf", ActParentIri),
            Iri(ActIri, Jolux + "isRealizedBy", ExpressionIri),
            Iri(ExpressionIri, RdfType, Jolux + "Expression"),
            Iri(ExpressionIri, Jolux + "language", LanguageFra),
            Iri(ExpressionIri, Jolux + "isEmbodiedBy", ManifestationIri),
            Iri(ManifestationIri, RdfType, Jolux + "Manifestation"),
            Iri(ManifestationIri, Jolux + "userFormat", JoluxAuthority + "user-format/xml"),
            Iri(ManifestationIri, Jolux + "isExemplifiedBy", itemIri),
        };
        if (previousItemIri is not null)
        {
            assertions.Add(Iri(
                ManifestationIri,
                Jolux + "previousIsExemplifiedBy",
                previousItemIri));
        }

        if (removePredicate is not null)
        {
            assertions.RemoveAll(assertion =>
                assertion.SubjectIri == ActIri && assertion.PredicateIri == removePredicate);
        }

        if (additionalAssertions is not null)
        {
            assertions.AddRange(additionalAssertions);
        }

        return new LuxembourgResourceObservation(
            ObjectRef(),
            ObservationRef,
            assertions,
            [],
            new LuxembourgSparqlRightsChannelObservations(
                ObservationRef,
                SparqlEnumerationRef,
                [Rights(SparqlRightsEvidenceRef)]),
            new LuxembourgInFileRightsChannelObservations(
                ObservationRef,
                InFileEnumerationRef,
                [Rights(InFileRightsEvidenceRef)]));
    }

    private static string BodyJoinSelector(LuxembourgProfileResolution.Resolved value) =>
        value.ScopeInputs.Single().Selectors
            .SelectMany(static selector => selector.CanonicalValues)
            .Single(candidate => candidate.StartsWith(
                "body_join_sha256:",
                StringComparison.Ordinal));

    private static LuxembourgObservedAssertion Iri(
        string subject,
        string predicate,
        string value) => new(
        subject,
        predicate,
        LuxembourgAssertionObjectKind.Iri,
        value,
        string.Empty,
        string.Empty,
        ObservationRef);

    private static LuxembourgRightsChannelObservation Rights(SourceArtifactRef evidenceRef) =>
        new(
            ManifestationIri,
            ObservationRef,
            evidenceRef,
            ["http://creativecommons.org/licenses/by/4.0/"]);

    private static SourceObjectRef ObjectRef(string publisherUri = ActIri)
    {
        return new SourceObjectRef(
            SourceCoreSchemaIds.SourceObjectRef,
            SourceAuthority.Jolux,
            new SourceRegistryMemberRef(EntityRegistryRef, "legal_resource"),
            publisherUri,
            publisherUri,
            Sha256(publisherUri),
            IdentityProfileRef,
            null);
    }

    private static string Sha256(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static SourceArtifactRef ObservationRef { get; } = Artifact(
        "10dd0a6e-3fa4-468d-a2aa-570a93ec4bf0",
        '1');
    private static SourceArtifactRef OtherObservationRef { get; } = Artifact(
        "a796278c-f25b-4c55-a4b1-42ee7ef1c345",
        '2');
    private static SourceArtifactRef CompleteEnumerationRef { get; } = Artifact(
        "3f60c78d-6e8a-4208-9146-43b634db9bbc",
        '3');
    private static SourceArtifactRef SparqlEnumerationRef { get; } = Artifact(
        "7163031e-a002-4fa2-af00-868b92d77f54",
        '4');
    private static SourceArtifactRef InFileEnumerationRef { get; } = Artifact(
        "e7a24ee4-bb66-4352-8d35-edb8a3664526",
        '5');
    private static SourceArtifactRef SparqlRightsEvidenceRef { get; } = Artifact(
        "5be675e4-64b4-42df-aae4-4f2da91a76d4",
        '8');
    private static SourceArtifactRef InFileRightsEvidenceRef { get; } = Artifact(
        "4f17db13-a542-48fd-a253-96ea3ce9a57c",
        '9');
    private static SourceArtifactRef EntityRegistryRef { get; } = Artifact(
        "760b560c-15c2-407d-b38f-f99f4c59e345",
        '6');
    private static SourceArtifactRef IdentityProfileRef { get; } = Artifact(
        "54b9c06f-ed04-4d07-8239-72dce5fed499",
        '7');

    private static SourceArtifactRef Artifact(string id, char digestCharacter) => new(
        "urn:uuid:" + id,
        new string(digestCharacter, 64));

    private const string JoluxAuthority =
        "http://data.legilux.public.lu/resource/authority/";
    private const string ActParentIri =
        "http://data.legilux.public.lu/eli/etat/leg/loi/2026/01/01/a1";
    private const string ActIri = ActParentIri + "/jo";
    private const string ExpressionIri = ActIri + "/fr";
    private const string ManifestationIri = ExpressionIri + "/xml";
    private const string ItemIri =
        "http://data.legilux.public.lu/filestore/body-fr.xml";
    private const string LanguageFra =
        "http://publications.europa.eu/resource/authority/language/FRA";
}
