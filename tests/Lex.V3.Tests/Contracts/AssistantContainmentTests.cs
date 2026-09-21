using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Lex.V3.Api;
using Lex.V3.Contracts.Platform;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Contracts;

/// <summary>
/// Decision 91's containment, made falsifiable: the closed refusal registry is exactly twenty codes,
/// the platform and the web surface hold the same twenty, and neither typed presentation result of
/// <c>ask</c> is among them.
/// </summary>
/// <remarks>
/// <para>
/// Decision 91 rules that <c>assistant_v3_unavailable</c> and <c>localization_unavailable</c> are
/// <b>typed presentation results of the <c>ask</c> operation, not refusal codes</b>, and that the
/// closed registry <i>"stays at its twenty codes … neither token is added to it by this Decision or
/// by implication"</i>. S4-A05 keeps the legacy assistant route disabled until the advice-boundary
/// and <c>answer_dossier</c> slices are reviewed and integrated.
/// </para>
/// <para>
/// <b>What was already guarded, corrected after review.</b> An earlier version of this file said
/// nothing on the platform side guarded either token and that a size change would pass. <b>Both were
/// wrong and a reviewer measured it.</b> <see cref="V3OperationRegistry"/>'s constructor requires the
/// refusals to be exactly its required list in sequence, the registry digest is pinned in the
/// envelope tests, and <c>Reviewed.RefusalCodes</c> is a public collection that answers how many
/// members there are. On the web, <c>refusal-card.test.mjs</c> and <c>localization.test.mjs</c>
/// already assert the list's length and one of them is titled for the platform's twenty. <b>Each
/// surface was guarded. Neither read the other.</b>
/// </para>
/// <para>
/// <b>What this adds, stated at the strength it holds.</b> The two surfaces hard-code their own
/// twenty and neither reads the other, so <b>a deliberate one-sided change that also updates that
/// side's own pins</b> — the digest and the list together on the platform, or the length constant
/// and the per-code catalogue together on the web — <b>leaves every other test green and the two
/// surfaces disagreeing.</b> That is the gap this closes, and it is a reading rather than a mutant:
/// the one-sided consistent update needs the digest constants recomputed, which this file's sweep
/// did not do.
/// </para>
/// <para>
/// <b>Why the platform is asked and the web is read.</b> The registry answers its own membership and
/// its own size, so it is asked. .NET cannot ask a JavaScript module, so the web list is read from
/// source between its declaration's brackets, and the twenty named literally below are what both are
/// compared against — an extraction that silently returned nothing fails rather than passing
/// vacuously. Both are ordered before comparing, because the literal is in ordinal order and the web
/// list is in the spec's order; the sets are the contract and the order is not.
/// </para>
/// <para>
/// <b>This does not implement S4-A05.</b> It pins the containment the clause and the Decision
/// describe while the assistant does not exist: the registry's size and membership, the two tokens'
/// absence from it, and the two surfaces' agreement. The seven prohibitions S4-A05 names are about
/// an assistant's behaviour and need controls of their own when there is one to control.
/// </para>
/// </remarks>
[TestClass]
public sealed class AssistantContainmentTests
{
    /// <summary>
    /// The closed registry, named rather than counted, because a count is what drifted: three web
    /// comments said "nineteen" while all three files listed twenty.
    /// </summary>
    private static readonly string[] ClosedRefusalRegistry =
    [
        "advice_boundary",
        "ambiguous_identifier",
        "ambiguous_version",
        "anchor_not_in_version",
        "derivation_refused",
        "format_not_available",
        "identifier_unknown",
        "language_not_available",
        "no_corpus_mounted",
        "no_version_for_date",
        "not_transposable",
        "out_of_corpus_scope",
        "pinned_digest_mismatch",
        "profiles_differ",
        "rate_limited",
        "retrieval_mode_unavailable",
        "snapshot_unknown",
        "text_not_available",
        "text_withheld",
        "upstream_unreachable",
    ];

    /// <summary>
    /// The two Decision 91 names. They are typed presentation results of <c>ask</c> and must never
    /// be refusal codes; admitting either is a versioned API contract change and a ruling of its own.
    /// </summary>
    private static readonly string[] PresentationResultsOfAsk =
        ["assistant_v3_unavailable", "localization_unavailable"];

    private const string WebRegistry = "web/scripts/refusal-card.mjs";

    [TestMethod]
    public void ThePlatformDeclaresExactlyTheClosedRegistry()
    {
        CollectionAssert.AreEqual(
            Ordered(ClosedRefusalRegistry),
            Ordered(V3OperationRegistry.Reviewed.RefusalCodes),
            "The platform's closed refusal registry is not the twenty codes named here. Decision 91 "
            + "keeps it at twenty and says neither presentation result of ask is added to it by that "
            + "Decision or by implication, so a change here is a versioned API contract change and "
            + "needs its own ruling.");

        foreach (var code in ClosedRefusalRegistry)
        {
            Assert.IsTrue(
                V3OperationRegistry.Reviewed.DeclaresRefusal(code),
                $"{code} is one of the twenty and the reviewed registry does not declare it.");
        }
    }

    [TestMethod]
    public void TheWebSurfaceHoldsTheSameTwentyAndNeitherSurfaceCanDriftAlone()
    {
        var platform = Ordered(V3OperationRegistry.Reviewed.RefusalCodes);
        var web = Ordered(DeclaredCodes(WebRegistry, "REFUSAL_CODES"));

        CollectionAssert.AreEqual(
            Ordered(ClosedRefusalRegistry), web,
            "The web surface's closed refusal registry is not the twenty codes named here.");
        CollectionAssert.AreEqual(
            platform, web,
            "The platform and the web surface hold different closed refusal registries. Each passes "
            + "its own tests while disagreeing about which refusals exist, which is the reader and "
            + "the producer holding separate copies of one contract.");
    }

    [TestMethod]
    public void NeitherTypedPresentationResultOfAskIsARefusalCode()
    {
        foreach (var name in PresentationResultsOfAsk)
        {
            Assert.IsFalse(
                V3OperationRegistry.Reviewed.DeclaresRefusal(name),
                $"{name} is now a declared refusal code. Decision 91 rules it a typed presentation "
                + "result of ask and not a refusal, and that the registry stays at its twenty; "
                + "admitting it is a versioned API contract change that needs its own ruling on #348.");
            CollectionAssert.DoesNotContain(
                ClosedRefusalRegistry, name,
                $"{name} has been written into this file's own registry list.");
            CollectionAssert.DoesNotContain(
                V3OperationRegistry.Reviewed.RefusalCodes.ToArray(), name,
                $"{name} is in the platform's reviewed registry.");
            CollectionAssert.DoesNotContain(
                DeclaredCodes(WebRegistry, "REFUSAL_CODES"), name,
                $"{name} is in the web surface's registry.");
        }
    }

    [TestMethod]
    public void TheLegacyAssistantRouteIsStillDisabled()
    {
        // Decision 91: "The legacy assistant route stays disabled", until the answer_dossier and
        // advice-boundary slices are reviewed at exact heads and integrated. `ask` is a reviewed
        // operation with no route, which is how the route is disabled today.
        var registered = V3OperationRegistry.Reviewed.Operations
            .Select(static operation => operation.OperationId)
            .ToArray();
        CollectionAssert.Contains(registered, "ask", "ask must be a reviewed operation.");

        CollectionAssert.DoesNotContain(
            V3RestRouteBinding.Served.Select(static binding => binding.OperationId).ToArray(),
            "ask",
            "ask now has a REST route. Decision 91 holds the legacy assistant route disabled until "
            + "the answer_dossier (S4-A04) and advice-boundary (S4-A05) slices are independently "
            + "reviewed at exact heads and integrated, so serving it is a decision that needs both "
            + "of those on the record first.");
    }

    /// <summary>
    /// The quoted codes of one declaration, read from the file. Used for the web list only, because
    /// .NET cannot ask a JavaScript module what it holds; the platform's registry is asked directly.
    /// The declaration is found by name and the codes are taken to its closing bracket, so a list
    /// that moved or was renamed reads as empty and fails against the literal twenty rather than
    /// passing on a shorter list it happened to find.
    /// </summary>
    private static string[] DeclaredCodes(string relativePath, string declaration)
    {
        var path = Path.Combine(FindRepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.IsTrue(File.Exists(path), $"the registry source must exist to be read: {relativePath}");
        var text = File.ReadAllText(path);
        var start = text.IndexOf(declaration, StringComparison.Ordinal);
        Assert.IsGreaterThan(-1, start, $"{relativePath} no longer declares {declaration}.");
        var open = text.IndexOf('[', start);
        var close = text.IndexOf(']', open < 0 ? start : open);
        Assert.IsGreaterThan(-1, open, $"{declaration} in {relativePath} is not a list.");
        Assert.IsGreaterThan(open, close, $"{declaration} in {relativePath} is not closed.");

        return Regex.Matches(text[open..close], "['\"]([a-z][a-z0-9_]*)['\"]")
            .Select(static match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static string[] Ordered(IEnumerable<string> values) =>
        values.OrderBy(static value => value, StringComparer.Ordinal).ToArray();

    private static string FindRepositoryRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "Lex.V3.slnx")))
            {
                return current.FullName;
            }
        }

        throw new DirectoryNotFoundException("Cannot locate the V3 repository root.");
    }
}
