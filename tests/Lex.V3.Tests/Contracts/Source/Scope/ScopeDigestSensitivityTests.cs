using System;
using System.Collections.Generic;
using System.Linq;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Scope;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Scope;

/// <summary>
/// The two scope digests that nothing else states anything about:
/// <c>ComputeSelectorSetSha256</c> and <c>ComputeRuleEvaluationSha256</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this file exists.</b> Each function has exactly one production caller,
/// <see cref="ScopeReducer"/>, which computes the digest and then hands the binding to a resolver. A
/// resolver that recomputed it would be the reducer checking its own arithmetic, so both production
/// resolvers admit these digests on SHA-256 shape alone, correctly. The byte-for-byte manifest
/// replay does not reach them either: it rebuilds through this same writer, so an error here
/// reproduces itself and the replay still matches. <b>Before this file, every test that touched
/// these functions called them to build the value it then compared against</b> — they were measured
/// against themselves everywhere they appeared.
/// </para>
/// <para>
/// <b>The first test is a GOLDEN MASTER and nothing more.</b> Fixed inputs, and the digest this code
/// produces today, written as a literal. <b>It detects change and states nothing about
/// correctness.</b> A known-answer vector would need the answer known independently, and the
/// canonical encoding is specified nowhere: the two hash domains appear only in this codebase, not
/// in the governance repository, so there is no statement of what the bytes should be. Anything
/// computed "outside" would have this implementation as its only reference and would be a
/// transcription of it. <b>Read it as "the reducer's bytes moved", never as "the bytes are
/// right".</b>
/// </para>
/// <para>
/// <b>The rest state a property that needs no specification:</b> every field that can legally vary
/// must reach the digest. That catches what a golden master cannot — <b>a writer that silently omits
/// a field</b>, which makes two genuinely different selector sets or evaluations hash the same and
/// lets a manifest claim evidence it does not have. A golden master would happily pin the digest of
/// a writer that had been ignoring <c>state</c> since the day it was written.
/// </para>
/// <para>
/// <b>Only legal variants are varied, which is a fact about the types worth recording here.</b>
/// <see cref="ScopeSelectorEvidence"/> is a closed union: each state fixes which of the values, the
/// evidence kind and the three ordinals must be present, and an inconsistent combination is refused
/// at construction. And canonical values are not sorted on the way in — <b>they are required to
/// arrive strictly ascending in UTF-8 order</b>, so "the same values in another order" is not an
/// input this type accepts and is not a variation this file can or should test.
/// </para>
/// </remarks>
[TestClass]
public sealed class ScopeDigestSensitivityTests
{
    private static readonly SourceArtifactRef ProfileRef = new(
        "urn:uuid:11111111-1111-4111-8111-111111111111", new string('1', 64));

    private static readonly SourceArtifactRef TableRef = new(
        "urn:uuid:22222222-2222-4222-8222-222222222222", new string('2', 64));

    private static readonly SourceArtifactRef FirstEvidence = new(
        "urn:uuid:33333333-3333-4333-8333-333333333333", new string('3', 64));

    private static readonly SourceArtifactRef SecondEvidence = new(
        "urn:uuid:44444444-4444-4444-8444-444444444444", new string('4', 64));

    /// <summary>Two selector members, two rules, so both ordinals have something to vary between.</summary>
    private static ScopeProfileBinding Profile()
    {
        // Canonically sorted: by the registry ref's resource id, then its digest, then the member
        // key, all ordinal. The profile's refs sort before the table's, and the keys within each.
        // Canonically sorted: by the registry ref's resource id, then its digest, then the member
        // key, all ordinal. The profile's refs sort before the table's, and the keys within each.
        SourceRegistryMemberRef[] members =
        [
            new(ProfileRef, "body_candidate"),      // 0
            new(ProfileRef, "reviewer"),            // 1
            new(TableRef, "body_allow"),            // 2
            new(TableRef, "format"),                // 3
            new(TableRef, "language"),              // 4
            new(TableRef, "record_allow"),          // 5
            new(TableRef, "relation_allow"),        // 6
            new(TableRef, "support_allow"),         // 7
        ];

        // Every scope axis must appear in the rule table, so the profile carries all four; the tests
        // vary between the first two.
        return new ScopeProfileBinding(
            ProfileRef,
            TableRef,
            members,
            [3, 4],
            [
                new ScopeRuleBinding(ScopeAxis.Record, 5, 0),
                new ScopeRuleBinding(ScopeAxis.Body, 2, 1),
                new ScopeRuleBinding(ScopeAxis.Relation, 6, 2),
                new ScopeRuleBinding(ScopeAxis.SupportingDocument, 7, 3),
            ],
            0);
    }

    // The four legal selector variants. Each is the only shape its state permits.
    private static ScopeSelectorEvidence Present(string[] values, int artifactOrdinal = 0) =>
        new(ScopeSelectorState.PublisherValuePresent, values,
            ScopeSelectorEvidenceKind.ObservedValueSet, artifactOrdinal, null, null);

    private static ScopeSelectorEvidence Absent(int artifactOrdinal = 0) =>
        new(ScopeSelectorState.PublisherValueAbsent, [],
            ScopeSelectorEvidenceKind.CompleteObservationAbsence, artifactOrdinal, null, null);

    private static ScopeSelectorEvidence Conflict(string[] values, int causeMemberOrdinal = 1) =>
        new(ScopeSelectorState.PublisherValueConflict, values,
            ScopeSelectorEvidenceKind.ObservedConflictingValueSet, 0, null, causeMemberOrdinal);

    private static ScopeSelectorEvidence NotApplicable(int ruleOrdinal = 0) =>
        new(ScopeSelectorState.SelectorNotApplicable, [], null, null, ruleOrdinal, null);

    // A matched evaluation's legal shapes. Only an accepted selection may carry roles or
    // capabilities, and an exact denial can never be an accepted selection, so the two shapes are
    // separated here rather than left to a caller to combine wrongly.
    private static ScopeRuleEvaluation Accepted(
        int ruleOrdinal = 0, int[]? roles = null, int[]? capabilities = null) =>
        new(ruleOrdinal, ScopeRuleEvaluationState.Matched, ScopeRuleEffect.Positive,
            ScopeDisposition.AcceptedSelected, roles ?? [0], capabilities ?? [1]);

    private static ScopeRuleEvaluation Outcome(
        ScopeRuleEffect effect, ScopeDisposition disposition, int ruleOrdinal = 0) =>
        new(ruleOrdinal, ScopeRuleEvaluationState.Matched, effect, disposition, [], []);

    private static ScopeRuleEvaluation NotMatched(int ruleOrdinal = 0) =>
        new(ruleOrdinal, ScopeRuleEvaluationState.NotMatched, null, null, [], []);

    private static string SelectorSet(params ScopeSelectorEvidence[] selectors) =>
        ScopeManifestCanonicalWriter.ComputeSelectorSetSha256(
            Profile(), [FirstEvidence, SecondEvidence], selectors);

    private static string Rule(ScopeRuleEvaluation evaluation) =>
        ScopeManifestCanonicalWriter.ComputeRuleEvaluationSha256(Profile(), evaluation);

    /// <summary>
    /// GOLDEN MASTER. The bytes this reducer produces today for one fixed input each. Drift
    /// detection only: a change means the canonical encoding moved, which is a change to every
    /// manifest this product has written and must be a line in a diff somebody reads. <b>It is not
    /// evidence that either encoding is correct.</b>
    /// </summary>
    [TestMethod]
    public void TheDigestsOfOneFixedInputAreTheseExactBytesToday()
    {
        Assert.AreEqual(
            "9f7048d1f034d72d8703d9c025a8668432e218c812172a9632600329925d8477",
            SelectorSet(Present(["application/xml"]), Absent()),
            "The selector-set encoding moved. Every manifest already written used the old bytes.");

        Assert.AreEqual(
            "ac1b2c5f2a09b36cfd62002c66773ee07e5779916acacd62434923381cb17e25",
            Rule(Accepted()),
            "The rule-evaluation encoding moved. Every manifest already written used the old bytes.");
    }

    /// <summary>
    /// Every part of a selector set that can legally differ reaches its digest: the state and its
    /// typed shape, the values, how many there are, which evidence artifact is cited, which rule a
    /// not-applicable selector names, which member caused a conflict, and the ordinal a selector
    /// sits at.
    /// </summary>
    [TestMethod]
    public void EveryLegalVariationOfASelectorSetChangesItsDigest()
    {
        AssertAllDistinct("selector set", new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["present"] = SelectorSet(Present(["application/xml"])),
            ["present, another value"] = SelectorSet(Present(["application/pdf"])),
            ["present, two values"] = SelectorSet(Present(["application/pdf", "application/xml"])),
            ["present, second evidence artifact"] = SelectorSet(Present(["application/xml"], 1)),
            ["absent"] = SelectorSet(Absent()),
            ["absent, second evidence artifact"] = SelectorSet(Absent(1)),
            ["conflict"] = SelectorSet(Conflict(["application/pdf", "application/xml"])),
            ["conflict, another cause member"] = SelectorSet(Conflict(["application/pdf", "application/xml"], 0)),
            ["not applicable"] = SelectorSet(NotApplicable()),
            ["not applicable, second rule"] = SelectorSet(NotApplicable(1)),
            ["two selectors"] = SelectorSet(Present(["application/xml"]), Absent()),
            ["the same two, swapped"] = SelectorSet(Absent(), Present(["application/xml"])),
            ["empty set"] = SelectorSet(),
        });
    }

    /// <summary>Every part of a rule evaluation that can legally differ reaches its digest.</summary>
    [TestMethod]
    public void EveryLegalVariationOfARuleEvaluationChangesItsDigest()
    {
        AssertAllDistinct("rule evaluation", new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["accepted"] = Rule(Accepted()),
            ["accepted, second rule"] = Rule(Accepted(ruleOrdinal: 1)),
            ["accepted, another role"] = Rule(Accepted(roles: [1])),
            ["accepted, two roles"] = Rule(Accepted(roles: [0, 1])),
            ["accepted, another capability"] = Rule(Accepted(capabilities: [0])),
            ["accepted, two capabilities"] = Rule(Accepted(capabilities: [0, 1])),
            ["positive, quarantined"] = Rule(Outcome(ScopeRuleEffect.Positive, ScopeDisposition.TypedQuarantine)),
            ["positive, point"] = Rule(Outcome(ScopeRuleEffect.Positive, ScopeDisposition.Point)),
            ["denial, quarantined"] = Rule(Outcome(ScopeRuleEffect.ExactDenial, ScopeDisposition.TypedQuarantine)),
            ["denial, never ingest"] = Rule(Outcome(ScopeRuleEffect.ExactDenial, ScopeDisposition.NeverIngest)),
            ["denial, quarantined, second rule"] = Rule(Outcome(ScopeRuleEffect.ExactDenial, ScopeDisposition.TypedQuarantine, 1)),
            ["not matched"] = Rule(NotMatched()),
            ["not matched, second rule"] = Rule(NotMatched(1)),
        });
    }

    /// <summary>
    /// The two functions are domain separated, so content under one is never a digest under the
    /// other. Without the domain prefix a selector set and an evaluation that happened to encode
    /// alike would be interchangeable.
    /// </summary>
    [TestMethod]
    public void TheTwoDigestsDoNotCollideOnTheirDomains()
    {
        Assert.AreNotEqual(SelectorSet(Present(["application/xml"])), Rule(Accepted()));
        Assert.AreNotEqual(SelectorSet(), Rule(NotMatched()));
    }

    private static void AssertAllDistinct(string what, Dictionary<string, string> digests)
    {
        foreach (var (name, digest) in digests)
        {
            Assert.AreEqual(64, digest.Length, $"{what} '{name}' is not a digest");
        }

        var collisions = digests
            .GroupBy(static entry => entry.Value, StringComparer.Ordinal)
            .Where(static group => group.Count() > 1)
            .Select(static group => string.Join(" == ", group.Select(static entry => entry.Key).Order(StringComparer.Ordinal)))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.IsEmpty(
            collisions,
            $"These {what} variations hash alike, so what differs between them never reached the "
            + $"digest and no manifest can tell them apart: {string.Join("; ", collisions)}");
    }
}
