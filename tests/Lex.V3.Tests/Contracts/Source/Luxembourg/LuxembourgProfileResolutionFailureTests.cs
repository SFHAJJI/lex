using System.Text.Json.Serialization;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Luxembourg;

/// <summary>
/// The two whole-run failure codes that no production path used to construct, each driven through
/// the real door by the condition its summary names.
/// </summary>
/// <remarks>
/// <para>
/// Residue R1. RULING lex-event-20260905T044206627Z-43bd39db4edb474c834cb2acd1e1e1ff, finding
/// lex-event-20260904T215524557Z-7cb36f1f533c4318b978a4ff97c929d7, mapping confirmed by
/// lex-event-20260905T045022959Z-6cfc993cf30e46f784028e1ad91f04ea.
/// <see cref="LuxembourgProfileResolutionFailureCode.IncompleteVocabulary"/> and
/// <see cref="LuxembourgProfileResolutionFailureCode.SelectorConflict"/> were declared, carried
/// wire strings, and were constructed by nothing, while the conditions they name escaped
/// <c>VerifiedLuxembourgSourceProfile.Open</c> as an untyped <c>ArgumentException</c>. So the
/// vocabulary advertised coverage it did not have.
/// </para>
/// <para>
/// EVERY TEST HERE DRIVES THE CONDITION THROUGH <c>TryOpen</c> WITH A REAL SNAPSHOT and never
/// constructs a refusal, because a test that builds the failure proves the type exists and not that
/// anything produces it. That distinction IS the defect R1 removes, so reproducing it inside R1's
/// own tests would be the third time in this repository that an instrument carried the flaw it was
/// built to remove. THE DISCIPLINE IS THE TESTS' AND NOT THE COMPILER'S: an earlier version of
/// this remark claimed the refusal could not be constructed here because its constructor is
/// internal, which is false, because src/Lex.V3.Contracts/AssemblyInfo.cs grants InternalsVisibleTo
/// to both test assemblies and other tests already call internal constructors through that door.
/// The predicate was constructor internal, the sentence was cannot construct, and the friend
/// declaration sat between them.
/// </para>
/// <para>
/// The wire strings are asserted on refusals the real path produced rather than on values handed to
/// a constructor. They are pinned because a rename would be silent otherwise, which is not
/// hypothetical: on the day this was written the other publisher was found to pin no refusal wire
/// names at all, so a token there could have been renamed with nothing to notice.
/// </para>
/// </remarks>
[TestClass]
public sealed class LuxembourgProfileResolutionFailureTests
{
    /// <summary>
    /// A snapshot that omits one settled rule value is refused as
    /// <see cref="LuxembourgProfileResolutionFailureCode.IncompleteVocabulary"/>.
    /// </summary>
    [TestMethod]
    public void AnOmittedSettledValueIsRefusedAsIncompleteVocabulary()
    {
        var required = VerifiedLuxembourgSourceProfile.RequiredIriVocabulary;
        var omitted = required[0];
        var snapshot = new LuxembourgVocabularySnapshot(
            ObservationRef,
            EnumerationRef,
            required.Skip(1).ToArray(),
            []);

        var profile = VerifiedLuxembourgSourceProfile.TryOpen(snapshot, out var failure);

        Assert.IsNull(profile, "an incomplete vocabulary must not open as a verified profile");
        Assert.IsNotNull(failure);
        Assert.AreEqual(
            LuxembourgProfileResolutionFailureCode.IncompleteVocabulary, failure.Code);
        Assert.AreEqual(
            "profile_resolution_failed_incomplete_vocabulary",
            failure.ReasonCode,
            "the wire string a consumer reads changed");
        StringAssert.Contains(
            failure.Subject,
            omitted.FullIri,
            "the refusal must name the row that is missing, not merely that one is");
    }

    /// <summary>
    /// A snapshot presenting the same IRI row twice is refused as
    /// <see cref="LuxembourgProfileResolutionFailureCode.SelectorConflict"/>: two rows competing for
    /// one selector position.
    /// </summary>
    [TestMethod]
    public void TwoIriRowsCompetingForOneSelectorAreRefusedAsSelectorConflict()
    {
        var required = VerifiedLuxembourgSourceProfile.RequiredIriVocabulary;
        var duplicated = required[0];
        var snapshot = new LuxembourgVocabularySnapshot(
            ObservationRef,
            EnumerationRef,
            [.. required, duplicated],
            []);

        var profile = VerifiedLuxembourgSourceProfile.TryOpen(snapshot, out var failure);

        Assert.IsNull(profile, "a duplicated selector row must not open as a verified profile");
        Assert.IsNotNull(failure);
        Assert.AreEqual(LuxembourgProfileResolutionFailureCode.SelectorConflict, failure.Code);
        Assert.AreEqual(
            "profile_resolution_failed_selector_conflict",
            failure.ReasonCode,
            "the wire string a consumer reads changed");
        StringAssert.Contains(
            failure.Subject,
            duplicated.FullIri,
            "the refusal must name the row that competes, not merely that one does");
    }

    /// <summary>
    /// The literal vocabulary has its own producer for the same code, so it is driven too rather
    /// than assumed to behave like the IRI one.
    /// </summary>
    [TestMethod]
    public void TwoLiteralRowsCompetingForOneSelectorAreRefusedAsSelectorConflict()
    {
        var literal = new LuxembourgLiteralVocabularyValue(
            LuxembourgVocabularyKind.Language,
            string.Empty,
            "fr",
            "francais");
        var snapshot = new LuxembourgVocabularySnapshot(
            ObservationRef,
            EnumerationRef,
            VerifiedLuxembourgSourceProfile.RequiredIriVocabulary,
            [literal, literal]);

        var profile = VerifiedLuxembourgSourceProfile.TryOpen(snapshot, out var failure);

        Assert.IsNull(profile);
        Assert.IsNotNull(failure);
        Assert.AreEqual(LuxembourgProfileResolutionFailureCode.SelectorConflict, failure.Code);
        Assert.AreEqual("profile_resolution_failed_selector_conflict", failure.ReasonCode);
        StringAssert.Contains(
            failure.Subject,
            "francais",
            "the refusal must name the row that competes, as its IRI sibling does");
    }

    /// <summary>
    /// A snapshot that is both incomplete and conflicting refuses as the conflict, because the
    /// conflict checks run first. Precedence is asserted rather than left to the order of the code.
    /// </summary>
    [TestMethod]
    public void ASnapshotBothIncompleteAndConflictingRefusesAsSelectorConflict()
    {
        var required = VerifiedLuxembourgSourceProfile.RequiredIriVocabulary;
        var snapshot = new LuxembourgVocabularySnapshot(
            ObservationRef,
            EnumerationRef,
            [.. required.Skip(1), required[1]],
            []);

        var profile = VerifiedLuxembourgSourceProfile.TryOpen(snapshot, out var failure);

        Assert.IsNull(profile);
        Assert.AreEqual(
            LuxembourgProfileResolutionFailureCode.SelectorConflict,
            failure!.Code,
            "IRI conflict is checked before completeness, so the conflict is the refusal");
    }

    /// <summary>
    /// Every member of the vocabulary keeps its exact wire token, all five of them.
    /// </summary>
    /// <remarks>
    /// The tokens used to come from a hand-written switch that no member carried an attribute for,
    /// and the pin covered three of five: renaming one of the two unpinned tokens left the whole
    /// suite green. The reader below falls back to the CLR member name exactly as the contract
    /// converter does, so a member whose attribute is removed shows up here as its PascalCase name
    /// rather than as an exception, which is the failure a reader can act on.
    /// </remarks>
    [TestMethod]
    public void TheProfileResolutionFailureVocabularyKeepsItsExactWireNames()
    {
        var actual = Enum.GetValues<LuxembourgProfileResolutionFailureCode>()
            .Select(static value =>
            {
                var name = value.ToString();
                return typeof(LuxembourgProfileResolutionFailureCode)
                    .GetField(name)!
                    .GetCustomAttributes(typeof(JsonStringEnumMemberNameAttribute), false)
                    .Cast<JsonStringEnumMemberNameAttribute>()
                    .SingleOrDefault()
                    ?.Name ?? name;
            });

        // Joined rather than compared element-wise: CollectionAssert reports a count difference for
        // two equal-length sequences that differ only in a name, which is precisely the failure a
        // wire pin exists to describe. A string diff names the token that moved.
        Assert.AreEqual(
            string.Join("\n", new[]
            {
                "profile_resolution_failed_invalid_publisher_iri",
                "profile_resolution_failed_incomplete_vocabulary",
                "profile_resolution_failed_unknown_vocabulary_drift",
                "profile_resolution_failed_selector_conflict",
                "profile_resolution_failed_evidence_binding_rejected",
            }),
            string.Join("\n", actual),
            "a wire token a consumer reads moved");
    }

    /// <summary>
    /// A complete, conflict-free snapshot still opens, so the two refusals above are about their
    /// conditions rather than about the door refusing everything.
    /// </summary>
    [TestMethod]
    public void ACompleteSnapshotStillOpens()
    {
        var snapshot = new LuxembourgVocabularySnapshot(
            ObservationRef,
            EnumerationRef,
            VerifiedLuxembourgSourceProfile.RequiredIriVocabulary,
            []);

        var profile = VerifiedLuxembourgSourceProfile.TryOpen(snapshot, out var failure);

        Assert.IsNotNull(profile);
        Assert.IsNull(failure);
    }

    private static SourceArtifactRef ObservationRef { get; } = new(
        "urn:uuid:4a1f6c3e-7b2d-4f58-9c10-2d3e5f7a9b41",
        new string('4', 64));

    private static SourceArtifactRef EnumerationRef { get; } = new(
        "urn:uuid:6c2b8d0a-1e34-4a76-b5c9-8f0d1e2a3b57",
        new string('5', 64));
}
