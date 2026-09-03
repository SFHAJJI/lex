using System.Text.RegularExpressions;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;

namespace Lex.V3.Tests.Contracts.Source.Luxembourg;

/// <summary>
/// What the two Luxembourg passes are evidence OF, pinned so nobody has to take a packet's word
/// for it.
///
/// <para>
/// The claim that was made and was false: that the count and the pages are independent witnesses,
/// so a root-removal mutation on the traversal would be caught by comparing them. They are not.
/// Every LU template builds its count query and its page query from ONE range-selection block
/// written once in <c>LuxembourgQueryPlan.Template</c>; the count wraps that block in
/// <c>SELECT (COUNT(*) AS ?count)</c> and the page adds the cursor VALUES, the FILTER, the ORDER BY
/// and the LIMIT. Remove a triple pattern from the traversal and both queries change in the same
/// direction by the same amount, so no comparison between them can see it.
/// </para>
/// <para>
/// What the two passes ARE is reconciliation evidence over one publisher store: the same selection
/// asked for twice, in one acquisition run, delivered whole both times. R3.2 admits exactly that,
/// on condition the profile records witness independence as single_publisher_store.
/// </para>
/// <para>
/// RESOLVED by D1-04 (lex-event-20260903T192615392Z-b13dee192bd84cea970b71cd8ffd4b89):
/// <see cref="LuxembourgSourceProfileTopology"/> now mints the <c>SourceProfileTopology</c> that
/// carries <c>single_publisher_store</c>, bound to the LU source profile's own identity
/// (<see cref="VerifiedLuxembourgSourceProfile.ScopeBinding"/>'s <c>SourceProfileRef</c>), exactly
/// as R3.2 requires. <see cref="RepeatedEnumerationInterpretationProfile"/> deliberately still has
/// no witness-independence member: R3.2 places <c>source_profile_topology/1</c> on the source
/// profile, not on the per-set delivery/interpretation profile the repeated-enumeration executor
/// binds, so the two-pass reconciliation this file's first test verifies stays exactly what it was
/// (evidence over one publisher store), and the honest "not independent" declaration lives beside
/// the profile identity instead. The second test below used to assert the field was missing
/// everywhere; it now asserts the topology exists, names the right member, and is bound to the
/// right profile, which is what its own comment said would replace it once D1-04 landed.
/// </para>
/// </summary>
[TestClass]
public sealed class LuxembourgWitnessIndependenceTests
{
    [TestMethod]
    public void TheCountQueryIsThePageQuerySelectionReprojectedNotASecondWitness()
    {
        var plan = LuxembourgQueryPlan.CreateDefaultGraph(Artifact('1'), Artifact('2'));
        Assert.IsTrue(plan.QueryTemplates.Count > 0);

        foreach (var template in plan.QueryTemplates)
        {
            var page = Normalize(template.Utf8QueryTemplate);
            var count = Normalize(template.Utf8CountTemplate);

            // The count is exactly a COUNT wrapper around an inner SELECT.
            const string Wrapper = "SELECT (COUNT(*) AS ?count) WHERE { { ";
            Assert.IsTrue(
                count.StartsWith(Wrapper, StringComparison.Ordinal),
                $"{template.TemplateId}: the count query is no longer a COUNT wrapper");
            Assert.IsTrue(count.EndsWith(" } }", StringComparison.Ordinal));

            // Strip the wrapper and the inner SELECT's own closing brace. What is left is the
            // projection, the key variables and the range selection, verbatim.
            var inner = count[Wrapper.Length..^" } }".Length];
            Assert.IsTrue(
                inner.EndsWith(" }", StringComparison.Ordinal),
                $"{template.TemplateId}: the inner select is not brace-closed as expected");
            var sharedSelection = inner[..^" }".Length];

            // And that is character for character how the page query starts. The page then adds
            // the cursor and the ordering; it selects from the same graph pattern.
            Assert.IsTrue(
                page.StartsWith(sharedSelection, StringComparison.Ordinal),
                $"{template.TemplateId}: the count and page queries no longer share one selection. "
                + "If that is deliberate, the reconciliation claim in this file's summary is out of "
                + "date and the profile's witness independence has to be restated, not just this "
                + "assertion updated.");

            // The part the page adds, so the two are not accidentally identical either.
            var pageOnly = page[sharedSelection.Length..];
            StringAssert.Contains(pageOnly, "?has_cursor");
            StringAssert.Contains(pageOnly, "ORDER BY");
            StringAssert.Contains(pageOnly, "LIMIT");
            Assert.IsFalse(sharedSelection.Contains("?has_cursor", StringComparison.Ordinal));
        }
    }

    [TestMethod]
    public void TheDeliveryProfileStillCarriesNoWitnessIndependenceMemberBecauseTopologyLivesOnTheSourceProfile()
    {
        // R3.2 places source_profile_topology/1 on the LU SOURCE profile
        // (VerifiedLuxembourgSourceProfile), not on the per-set delivery/interpretation profile the
        // repeated-enumeration executor binds per query template. This test keeps proving the half
        // of the old residue that stays true on purpose: RepeatedEnumerationInterpretationProfile
        // gained no field for it, and could not correctly do so, because one interpretation profile
        // exists per query set (ten of them) while there is exactly one LU source-profile identity.
        // A member here would have to be repeated identically ten times or attached to the wrong
        // object; neither is what R3.2 asked for.
        var plan = LuxembourgQueryPlan.CreateDefaultGraph(Artifact('1'), Artifact('2'));
        var setId = plan.SetDefinitions
            .First(static definition => definition.Acquisition == LuxembourgQuerySetAcquisition.PublisherQuery)
            .SetId;
        var profile = plan.CreateDeliveryProfile("urn:uuid:00000000-0000-4000-8000-0000000000f1", setId);

        var members = typeof(RepeatedEnumerationInterpretationProfile)
            .GetProperties()
            .Select(static property => property.Name)
            .ToArray();
        var expectedMembers = new[]
        {
            nameof(RepeatedEnumerationInterpretationProfile.Schema),
            nameof(RepeatedEnumerationInterpretationProfile.Dialect),
            nameof(RepeatedEnumerationInterpretationProfile.ExpectedMediaType),
            nameof(RepeatedEnumerationInterpretationProfile.CursorEnvelopeIdentity),
            nameof(RepeatedEnumerationInterpretationProfile.MaximumDeliverableRows),
            nameof(RepeatedEnumerationInterpretationProfile.ThresholdDetectorIdentity),
            nameof(RepeatedEnumerationInterpretationProfile.CountQueryFamilyRef),
            nameof(RepeatedEnumerationInterpretationProfile.PageQueryFamilyRef),
            nameof(RepeatedEnumerationInterpretationProfile.CountVariable),
            nameof(RepeatedEnumerationInterpretationProfile.ProjectionVariables),
            nameof(RepeatedEnumerationInterpretationProfile.CanonicalKeyVariables),
            nameof(RepeatedEnumerationInterpretationProfile.CursorVariables),
            nameof(RepeatedEnumerationInterpretationProfile.SelectionParameterNames),
            nameof(RepeatedEnumerationInterpretationProfile.PassParameterName),
            nameof(RepeatedEnumerationInterpretationProfile.CursorParameterNames),
            nameof(RepeatedEnumerationInterpretationProfile.HasCursorParameterName),
            nameof(RepeatedEnumerationInterpretationProfile.TerminalPagePolicy),
        };
        CollectionAssert.AreEquivalent(
            expectedMembers,
            members,
            "the interpretation profile's member set changed. If it gained a witness-independence "
            + "field, that is a design change beyond D1-04's ruled placement on the source profile "
            + "and needs its own review; otherwise update expectedMembers to match the real change.");
        Assert.IsNotNull(profile);
    }

    [TestMethod]
    public void TheLuSourceProfileNowMintsSinglePublisherStoreTopology()
    {
        // The other half: the declared value now exists, on the object R3.2 actually names. Built
        // from the same minimal complete snapshot LuxembourgSourceProfileTests uses elsewhere.
        var observationRef = new SourceArtifactRef(
            "urn:uuid:10dd0a6e-3fa4-468d-a2aa-570a93ec4bf0", new string('1', 64));
        var enumerationRef = new SourceArtifactRef(
            "urn:uuid:3f60c78d-6e8a-4208-9146-43b634db9bbc", new string('2', 64));
        var snapshot = new LuxembourgVocabularySnapshot(
            observationRef,
            enumerationRef,
            VerifiedLuxembourgSourceProfile.RequiredIriVocabulary,
            []);
        var profile = VerifiedLuxembourgSourceProfile.Open(snapshot);

        var topology = LuxembourgSourceProfileTopology.Mint(profile);

        Assert.AreEqual(Lex.V3.Contracts.Source.Core.SourceCoreSchemaIds.SourceProfileTopology, topology.Schema);
        Assert.AreEqual(
            LuxembourgSourceProfileTopology.SinglePublisherStoreMemberKey,
            topology.Topology.MemberKey);
        Assert.AreEqual(profile.ScopeBinding.SourceProfileRef, topology.IdentityProfileRef);
        Assert.AreEqual(LuxembourgSourceProfileTopology.RegistryRef, topology.Topology.RegistryRef);

        // Two profiles built from the same complete vocabulary share a source-profile identity
        // (VerifiedLuxembourgSourceProfile.Open is a pure function of its snapshot), so minting
        // twice must be stable, not merely equal by accident of a shared instance.
        var second = LuxembourgSourceProfileTopology.Mint(VerifiedLuxembourgSourceProfile.Open(snapshot));
        Assert.AreEqual(topology.IdentityProfileRef, second.IdentityProfileRef);
        Assert.AreEqual(topology.Topology, second.Topology);
    }

    private static SourceArtifactRef Artifact(char fill) =>
        new("urn:uuid:00000000-0000-4000-8000-0000000000e" + fill, new string(fill, 64));

    /// <summary>Whitespace-insensitive, so indentation differences between the two templates do not matter.</summary>
    private static string Normalize(string query) =>
        Regex.Replace(query.Replace("\r\n", "\n", StringComparison.Ordinal), @"\s+", " ").Trim();
}
