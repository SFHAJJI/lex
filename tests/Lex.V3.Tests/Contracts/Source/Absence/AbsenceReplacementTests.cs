using Lex.V3.Contracts.Source.Absence;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Absence;

/// <summary>
/// D1-03, R3.3 lines 518 to 528: replacement detection runs before absence, the total A, B and R
/// classification, and the explicit refusal of greedy pairing.
/// </summary>
[TestClass]
public sealed class AbsenceReplacementTests
{
    private static AbsenceReplacementClassification Classify(
        IReadOnlyList<string> oldClass,
        IReadOnlyList<string> newClass)
    {
        var classification = AbsenceReplacementClassification.TryClassify(
            AbsenceFixtures.CoordinateProfile(), "cut-old", "cut-new", oldClass, newClass,
            out var refusal);
        Assert.IsNotNull(classification, $"the classification was refused as {refusal}");
        Assert.AreEqual(AbsenceReplacementClassificationRefusal.None, refusal);
        return classification;
    }

    /// <summary>
    /// The complete table, transcribed from R3.3 and pinned against literal set shapes rather than
    /// read back from the classifier.
    /// </summary>
    [TestMethod]
    public void TheTotalClassificationOfEveryShapeIsPinned()
    {
        var a = AbsenceFixtures.RootUri;
        var b = AbsenceFixtures.OtherUri;
        var c = AbsenceFixtures.ThirdUri;

        var table = new (string[] Old, string[] New, AbsenceReplacementDisposition Expected)[]
        {
            ([], [], AbsenceReplacementDisposition.CoordinateUnchanged),
            ([a], [a], AbsenceReplacementDisposition.CoordinateUnchanged),
            ([a, b], [b, a], AbsenceReplacementDisposition.CoordinateUnchanged),
            ([a], [], AbsenceReplacementDisposition.OrdinaryCoordinateDisappearance),
            ([a, b], [b], AbsenceReplacementDisposition.OrdinaryCoordinateDisappearance),
            ([], [a], AbsenceReplacementDisposition.OrdinaryCoordinateAddition),
            ([a], [a, b], AbsenceReplacementDisposition.OrdinaryCoordinateAddition),
            ([a], [b], AbsenceReplacementDisposition.ReplacementCandidateOneToOne),
            ([a, b], [b, c], AbsenceReplacementDisposition.ReplacementCollisionFullSet),
            ([a, b], [c], AbsenceReplacementDisposition.ReplacementCollisionFullSet),
            ([a], [b, c], AbsenceReplacementDisposition.ReplacementCollisionFullSet),
        };

        foreach (var (oldClass, newClass, expected) in table)
        {
            Assert.AreEqual(
                expected,
                Classify(oldClass, newClass).Disposition,
                $"O={{{string.Join(",", oldClass)}}} N={{{string.Join(",", newClass)}}} classified wrongly");
        }
    }

    /// <summary>
    /// The case R3.3 calls out by name. One identity out and nothing in is an ordinary
    /// disappearance that proceeds to its own absence evaluation, never a collision that freezes it
    /// forever.
    /// </summary>
    [TestMethod]
    public void ASingleDisappearanceWithNoArrivalIsNeverACollision()
    {
        var classification = Classify([AbsenceFixtures.RootUri], []);

        Assert.AreEqual(
            AbsenceReplacementDisposition.OrdinaryCoordinateDisappearance, classification.Disposition);
        Assert.IsFalse(classification.FreezesAbsence());
        Assert.AreEqual(
            AbsenceReplacementEffect.MayProceedToAbsence,
            classification.EffectOn(AbsenceFixtures.RootUri));
    }

    /// <summary>
    /// A retained identity beside a one-in one-out pair is a collision, not a candidate. Without the
    /// empty-R condition, greedy pairing would read the same three sets as a replacement.
    /// </summary>
    [TestMethod]
    public void ARetainedIdentityTurnsAOneInOneOutPairIntoACollision()
    {
        var candidate = Classify([AbsenceFixtures.RootUri], [AbsenceFixtures.OtherUri]);
        Assert.AreEqual(
            AbsenceReplacementDisposition.ReplacementCandidateOneToOne, candidate.Disposition);

        var withRetained = Classify(
            [AbsenceFixtures.RootUri, AbsenceFixtures.ThirdUri],
            [AbsenceFixtures.OtherUri, AbsenceFixtures.ThirdUri]);
        Assert.AreEqual(
            AbsenceReplacementDisposition.ReplacementCollisionFullSet, withRetained.Disposition);
        Assert.HasCount(1, withRetained.Retained());
    }

    [TestMethod]
    public void EveryClassificationRecordsTheCompleteSets()
    {
        var classification = Classify(
            [AbsenceFixtures.RootUri, AbsenceFixtures.ThirdUri],
            [AbsenceFixtures.OtherUri, AbsenceFixtures.ThirdUri]);

        CollectionAssert.AreEqual(
            new[] { AbsenceFixtures.RootUri, AbsenceFixtures.ThirdUri }
                .OrderBy(static uri => uri, StringComparer.Ordinal).ToArray(),
            classification.OldClass().ToArray());
        CollectionAssert.AreEqual(
            new[] { AbsenceFixtures.OtherUri, AbsenceFixtures.ThirdUri }
                .OrderBy(static uri => uri, StringComparer.Ordinal).ToArray(),
            classification.NewClass().ToArray());
        CollectionAssert.AreEqual(new[] { AbsenceFixtures.RootUri }, classification.Gone().ToArray());
        CollectionAssert.AreEqual(new[] { AbsenceFixtures.OtherUri }, classification.Arrived().ToArray());
        CollectionAssert.AreEqual(new[] { AbsenceFixtures.ThirdUri }, classification.Retained().ToArray());
        Assert.AreEqual("cut-old", classification.OldCutId);
        Assert.AreEqual("cut-new", classification.NewCutId);
        Assert.AreEqual(AbsenceFixtures.CoordinateProfile().ProfileDigest, classification.Profile.ProfileDigest);
    }

    /// <summary>Every effect the vocabulary declares is reachable, so none is decoration.</summary>
    [TestMethod]
    public void EveryDeclaredEffectIsReachable()
    {
        var reached = new[]
        {
            Classify([AbsenceFixtures.RootUri], []).EffectOn(AbsenceFixtures.OtherUri),
            Classify([AbsenceFixtures.RootUri], []).EffectOn(AbsenceFixtures.RootUri),
            Classify([AbsenceFixtures.RootUri], [AbsenceFixtures.RootUri, AbsenceFixtures.OtherUri])
                .EffectOn(AbsenceFixtures.RootUri),
            Classify([AbsenceFixtures.RootUri], [AbsenceFixtures.OtherUri])
                .EffectOn(AbsenceFixtures.RootUri),
        };

        CollectionAssert.AreEqual(
            new[]
            {
                AbsenceReplacementEffect.OutsideThisCoordinate,
                AbsenceReplacementEffect.MayProceedToAbsence,
                AbsenceReplacementEffect.NoAbsenceEvent,
                AbsenceReplacementEffect.FrozenPendingReview,
            },
            reached);

        CollectionAssert.AreEquivalent(
            Enum.GetValues<AbsenceReplacementEffect>(),
            reached.Distinct().ToArray(),
            "an effect in the closed vocabulary is unreachable, so it claims coverage it has none of");
    }

    /// <summary>
    /// R3.3: a date alone is never a coordinate. A profile of dates only is refused, and one that
    /// also carries a stable publisher field is admitted, so the guard is shown doing both.
    /// </summary>
    [TestMethod]
    public void ACoordinateProfileOfDatesAloneIsRefused()
    {
        Assert.IsNull(AbsenceReplacementCoordinateProfile.TryCreate(
            new string('f', 64),
            [
                new AbsenceCoordinateField("publication_date", AbsenceCoordinateFieldKind.PublisherDate),
                new AbsenceCoordinateField("signature_date", AbsenceCoordinateFieldKind.PublisherDate),
            ],
            out var datesOnly));
        Assert.AreEqual(
            AbsenceReplacementCoordinateProfileRefusal.CoordinateIsDateAlone, datesOnly);

        Assert.IsNotNull(AbsenceReplacementCoordinateProfile.TryCreate(
            new string('f', 64),
            [
                new AbsenceCoordinateField("publication_date", AbsenceCoordinateFieldKind.PublisherDate),
                new AbsenceCoordinateField("memorial_series", AbsenceCoordinateFieldKind.StablePublisherField),
            ],
            out var mixed));
        Assert.AreEqual(AbsenceReplacementCoordinateProfileRefusal.None, mixed);
    }

    [TestMethod]
    public void ACoordinateProfileRefusesAnUnusableDeclaration()
    {
        Assert.IsNull(AbsenceReplacementCoordinateProfile.TryCreate(
            "not-a-digest",
            [new AbsenceCoordinateField("f", AbsenceCoordinateFieldKind.FamilyRule)],
            out var digest));
        Assert.AreEqual(AbsenceReplacementCoordinateProfileRefusal.ProfileDigestNotSha256, digest);

        Assert.IsNull(AbsenceReplacementCoordinateProfile.TryCreate(
            new string('f', 64), [], out var empty));
        Assert.AreEqual(AbsenceReplacementCoordinateProfileRefusal.FieldsEmpty, empty);

        Assert.IsNull(AbsenceReplacementCoordinateProfile.TryCreate(
            new string('f', 64),
            [
                new AbsenceCoordinateField("f", AbsenceCoordinateFieldKind.FamilyRule),
                new AbsenceCoordinateField("f", AbsenceCoordinateFieldKind.StablePublisherField),
            ],
            out var duplicate));
        Assert.AreEqual(AbsenceReplacementCoordinateProfileRefusal.DuplicateFieldName, duplicate);

        Assert.IsNull(AbsenceReplacementCoordinateProfile.TryCreate(
            new string('f', 64),
            [new AbsenceCoordinateField("f", (AbsenceCoordinateFieldKind)77)],
            out var kind));
        Assert.AreEqual(AbsenceReplacementCoordinateProfileRefusal.FieldKindUndefined, kind);

        Assert.IsNull(AbsenceReplacementCoordinateProfile.TryCreate(
            new string('f', 64),
            [new AbsenceCoordinateField("  ", AbsenceCoordinateFieldKind.FamilyRule)],
            out var name));
        Assert.AreEqual(AbsenceReplacementCoordinateProfileRefusal.FieldNameInvalid, name);
    }

    /// <summary>
    /// A coordinate compared against itself across one cut is not evidence. Refusing it keeps the
    /// classifier from producing a disposition out of a value and a copy of that value.
    /// </summary>
    [TestMethod]
    public void AClassificationRefusesOneCutComparedWithItself()
    {
        Assert.IsNull(AbsenceReplacementClassification.TryClassify(
            AbsenceFixtures.CoordinateProfile(), "cut-1", "cut-1",
            [AbsenceFixtures.RootUri], [AbsenceFixtures.OtherUri], out var same));
        Assert.AreEqual(AbsenceReplacementClassificationRefusal.CutIdsIdentical, same);

        Assert.IsNull(AbsenceReplacementClassification.TryClassify(
            AbsenceFixtures.CoordinateProfile(), "  ", "cut-2",
            [AbsenceFixtures.RootUri], [AbsenceFixtures.OtherUri], out var blank));
        Assert.AreEqual(AbsenceReplacementClassificationRefusal.CutIdInvalid, blank);
    }

    [TestMethod]
    public void AClassificationRefusesAnUnusableEquivalenceClass()
    {
        Assert.IsNull(AbsenceReplacementClassification.TryClassify(
            AbsenceFixtures.CoordinateProfile(), "cut-1", "cut-2",
            ["not a uri"], [AbsenceFixtures.OtherUri], out var invalid));
        Assert.AreEqual(AbsenceReplacementClassificationRefusal.ClassMemberInvalid, invalid);

        Assert.IsNull(AbsenceReplacementClassification.TryClassify(
            AbsenceFixtures.CoordinateProfile(), "cut-1", "cut-2",
            [AbsenceFixtures.RootUri, AbsenceFixtures.RootUri], [AbsenceFixtures.OtherUri],
            out var duplicate));
        Assert.AreEqual(AbsenceReplacementClassificationRefusal.DuplicateClassMember, duplicate);
    }
}
