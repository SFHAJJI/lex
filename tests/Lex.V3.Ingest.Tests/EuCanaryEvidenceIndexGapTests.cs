using System;
using System.Linq;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The EU canary evidence index's declared gaps, pinned by ROLE so one cannot be dropped silently.
/// </summary>
/// <remarks>
/// <para>
/// THE GAP THIS CLOSES, found while adding the fourth gap. The roles this index cannot yet carry
/// were built inline inside <c>WriteEvidenceIndexAsync</c>, which only ever runs when
/// LEX_EU_CANARY=1. The canary is skipped by default, so NOTHING IN EITHER LANE ASSERTED THESE
/// ROLES: a gap could have been deleted, renamed, or never added at all and the whole suite would
/// have stayed green. A declaration nothing holds is a comment that happens to be JSON. The array
/// is now built by <c>EuStageOneAcquisitionCanary.RolesThisIndexCannotYetCarry</c>, which this
/// test can call without a store, a run, or a byte of publisher traffic.
/// </para>
/// <para>
/// WHAT IS PINNED AND WHAT IS DELIBERATELY NOT. The ROLE NAMES and their ORDER are pinned, because
/// a reader diffing two runs is told by role which values are comparable, and a role vanishing is
/// exactly the silent change worth catching. The <c>why</c> PROSE IS NOT PINNED, only required to
/// be present and substantial: each gap's explanation is expected to be rewritten as the gap is
/// understood better, and the D1-05f rebase showed what happens to prose pinned by substring, when
/// a sentence about fixture line endings survived three revisions while being false. Pinning prose
/// would make every honest rewording look like a contract change and tempt the next author to edit
/// the assertion rather than the text.
/// </para>
/// <para>
/// A NOTE ON WHY THIS IS NOT THE CANARY'S JOB. The canary asserts what a RUN produced. This asserts
/// what the index PROMISES to say about what it cannot produce, which is a property of the code and
/// needs no run. Keeping them apart is what lets this one be unconditional while the canary stays
/// gated behind a live publisher.
/// </para>
/// </remarks>
[TestClass]
public sealed class EuCanaryEvidenceIndexGapTests
{
    [TestMethod]
    public void TheIndexDeclaresExactlyTheFourGapsItCannotYetCarry()
    {
        var roles = EuStageOneAcquisitionCanary.RolesThisIndexCannotYetCarry()
            .Select(static entry => entry!["role"]!.GetValue<string>())
            .ToArray();

        // Joined rather than compared element-wise so a diff names the role that moved.
        Assert.AreEqual(
            string.Join("\n", new[]
            {
                "familyPassBodiesAndCursors",
                "countAnswerBesideARefusedPage",
                "robotsBootstrapArtifact",
                "crossRunCorpusRecordSetIdentity",
            }),
            string.Join("\n", roles),
            "a gap dropped from this index is a reader silently no longer told that a value is "
                + "not comparable across runs. Adding one is a normal change; losing one is not.");
    }

    [TestMethod]
    public void EveryDeclaredGapExplainsItselfAndNoRoleIsDeclaredTwice()
    {
        var entries = EuStageOneAcquisitionCanary.RolesThisIndexCannotYetCarry();

        foreach (var entry in entries)
        {
            var role = entry!["role"]!.GetValue<string>();
            var why = entry["why"]?.GetValue<string>();

            Assert.IsFalse(
                string.IsNullOrWhiteSpace(why),
                role + " is declared as a gap with no explanation. A role name alone tells a "
                    + "reader that something is missing and not why, which is the half that "
                    + "decides whether they can work around it.");

            // Not a spelling check on the prose. A gap explained in one clause is a gap nobody
            // has actually worked out yet, and this is the cheapest way to say so.
            Assert.IsTrue(
                why!.Length >= 120,
                role + "'s explanation is " + why.Length + " characters. A gap this index carries "
                    + "instead of a value has to say what is missing, why it cannot be reached "
                    + "from here, and what to do instead.");
        }

        var roles = entries.Select(static entry => entry!["role"]!.GetValue<string>()).ToArray();
        Assert.AreEqual(
            roles.Length,
            roles.Distinct(StringComparer.Ordinal).Count(),
            "two gaps declared under one role name would make the index self-contradicting: "
                + string.Join(", ", roles));
    }
}
