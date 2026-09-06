using System;
using System.Linq;
using System.Text.RegularExpressions;
using Lex.V3.Contracts.Source.Europe;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts.Source.Europe;

/// <summary>
/// Every keyset page template excludes the cursor row explicitly, over ITS OWN full cursor.
/// </summary>
/// <remarks>
/// <para>
/// WHY AN EXPLICIT EXCLUSION EXISTS AT ALL, since on paper it is unreachable. The ordinary keyset
/// disjunction admits a row strictly after the cursor, so a row equal to the cursor in every key
/// satisfies no disjunct and is already excluded. The endpoint disagrees. The 82-seed population run
/// of S1-A09 refused seeds at <c>object_facts_family_not_proven</c>, cause "Keys must be unique and
/// cursors strictly increase": for seed 32003L0087, family X's page one ended at cursor T, the
/// continuation bound exactly T's seven keys, and the endpoint returned T again as the first row.
/// Replaying that retained continuation with the disjunction PROJECTED rather than applied gives 0
/// for that row, so the endpoint returns a row its own answer to the filter excludes. It honours the
/// boundary as <c>&gt;=</c> where the query says <c>&gt;</c>.
/// </para>
/// <para>
/// WHAT THIS TEST HOLDS, and why the arity half is the part that matters. Asserting merely that the
/// clause is present would pass on a seven-key family guarded over six keys, and that is exactly the
/// shape that fails silently: the six-key exclusion would drop legitimate rows differing only in
/// key_7 while still admitting the duplicate it was added to remove. So the clause is checked
/// against each set's OWN declared cursor arity, taken from the plan rather than restated here.
/// </para>
/// </remarks>
[TestClass]
public sealed class EuKeysetBoundaryExclusionTests
{
    [TestMethod]
    public void EveryPageTemplateExcludesTheCursorRowOverItsOwnFullCursor()
    {
        var plan = EuObjectFactsDiscoveryPlan.Create();
        var sets = Enum.GetValues<EuObjectFactsQuerySet>();

        // The sweep must actually see every set, or it would pass by looking at none of them.
        Assert.IsTrue(sets.Length >= 4, "the plan declares four query sets.");

        foreach (var set in sets)
        {
            var definition = plan.Definition(set);
            var template = definition.PageTemplate;
            var cursorKeys = definition.CursorVariables
                .Where(static name => name.StartsWith("key_", StringComparison.Ordinal))
                .ToArray();

            Assert.IsTrue(
                cursorKeys.Length > 0,
                $"{set} declares no cursor keys, so this test would hold nothing for it.");

            var exclusion = Regex.Match(
                template,
                @"FILTER\(\?has_cursor = 0 \|\| !\((?<body>[^)]*)\)\)",
                RegexOptions.Singleline);
            Assert.IsTrue(
                exclusion.Success,
                $"{set}'s page template carries no explicit cursor-row exclusion. The endpoint "
                    + "returns the cursor row again despite the strict disjunction, so without this "
                    + "clause the continuation duplicates its boundary row and the delivery proof "
                    + "refuses the whole family.");

            var body = exclusion.Groups["body"].Value;
            var excluded = Regex.Matches(body, @"\?(key_\d+) = \?last_\1")
                .Select(static match => match.Groups[1].Value)
                .ToArray();

            // Joined rather than compared element-wise so a diff names the key that is missing.
            Assert.AreEqual(
                string.Join(",", cursorKeys.OrderBy(static key => key, StringComparer.Ordinal)),
                string.Join(",", excluded.OrderBy(static key => key, StringComparer.Ordinal)),
                $"{set}'s cursor-row exclusion does not name exactly its own cursor keys. An "
                    + "exclusion over FEWER keys than the cursor removes legitimate rows that differ "
                    + "only in the keys it omits, which is silent data loss rather than a refusal.");
        }
    }
}
