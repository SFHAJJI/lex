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
/// RESIDUE, recorded here rather than in prose somewhere: there is nowhere to record it yet.
/// <see cref="RepeatedEnumerationInterpretationProfile"/> has no witness-independence member, and
/// <c>SourceProfileTopology</c>, which carries the <c>single_publisher_store</c> vocabulary, is
/// constructed nowhere in <c>src</c> outside Core's own tests. Inventing a field here would be
/// inventing a contract; the declaration belongs on the LU source profile through Core's topology
/// record, which is carried condition D1-04 and another lane's task. Until it lands, this file is
/// the only place the honest reading is written down and checked, and the check is structural
/// rather than a comment: it fails if anyone ever makes the two templates independent, at which
/// point the claim can change and this test with it.
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
    public void NothingInTheDeliveryProfileRecordsWitnessIndependenceYet()
    {
        // The residue, asserted rather than described, so it stops being true loudly. When D1-04
        // gives the LU profile a place to declare single_publisher_store, this test fails and is
        // replaced by one that reads the declared value.
        var plan = LuxembourgQueryPlan.CreateDefaultGraph(Artifact('1'), Artifact('2'));
        var setId = plan.SetDefinitions
            .First(static definition => definition.Acquisition == LuxembourgQuerySetAcquisition.PublisherQuery)
            .SetId;
        var profile = plan.CreateDeliveryProfile("urn:uuid:00000000-0000-4000-8000-0000000000f1", setId);

        var members = typeof(RepeatedEnumerationInterpretationProfile)
            .GetProperties()
            .Select(static property => property.Name)
            .ToArray();
        // Pinned to the exact current member set rather than filtered by a guessed keyword
        // substring: a real single_publisher_store member would not contain "Witness", "Topology"
        // or "Independen" and would have slipped straight past the old filter. Any change to this
        // set, added or removed, is the signal this test exists to catch.
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
            "the interpretation profile's member set changed. If it gained single_publisher_store, "
            + "record it here, then replace this test with one that reads the declared value; "
            + "otherwise update expectedMembers to match the real change.");
        Assert.IsNotNull(profile);
    }

    private static SourceArtifactRef Artifact(char fill) =>
        new("urn:uuid:00000000-0000-4000-8000-0000000000e" + fill, new string(fill, 64));

    /// <summary>Whitespace-insensitive, so indentation differences between the two templates do not matter.</summary>
    private static string Normalize(string query) =>
        Regex.Replace(query.Replace("\r\n", "\n", StringComparison.Ordinal), @"\s+", " ").Trim();
}
