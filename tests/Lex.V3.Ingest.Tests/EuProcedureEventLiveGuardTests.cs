using System.Text;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// Structural guards over the EU procedure-event live harness: the properties a reader cannot
/// confirm by running it, because it is skipped by default.
/// </summary>
/// <remarks>
/// <para>
/// GUARDS READ CODE, NEVER PROSE. That harness documents its predicate, both ceilings and its
/// session bound in doc comments, so a <c>Contains</c> over the raw file would be satisfied by the
/// explanation of a property rather than by the property. This exact failure has already happened
/// twice in this programme - once where a guard passed while the array entry it guarded was mutated
/// away, because the comment beside it named the same draft. So every assertion below runs over
/// <see cref="CodeOnly"/>, with whole-line comments removed first.
/// </para>
/// <para>
/// THE PROPERTIES THAT MATTER HERE ARE DIFFERENT FROM THE DRAFT SWEEP'S. That one had to be stopped
/// from substituting its subjects; this one has no subjects to substitute, because it discovers
/// them. What it must instead be stopped from doing is SHORT-CIRCUITING the discovery: a hardcoded
/// dossier IRI, or a predicate literal that could drift from the contract, would turn a proved
/// discovery back into the caller guess the owner decision exists to forbid.
/// </para>
/// </remarks>
[TestClass]
public sealed class EuProcedureEventLiveGuardTests
{
    private const string HarnessFile = "EuProcedureEventLiveAcceptance.cs";

    /// <summary>The two owner-fixed ceilings and the session bound are the numbers in the code.</summary>
    /// <remarks>
    /// The owner decision fixed 34 charged requests and 36 actual sends. The session bound is
    /// derived rather than granted: sends exceed charges by one per session on this origin, so 36 is
    /// only binding while sessions are. All three are asserted as declarations, because a number in
    /// a doc comment enforces nothing.
    /// </remarks>
    [TestMethod]
    public void TheOwnerFixedCeilingsAreTheNumbersTheRunEnforces()
    {
        var code = CodeOnly(HarnessSource());

        Assert.AreEqual(
            1, CountOf(code, "private const int SharedWireCeiling = 34;"),
            "the owner-fixed charged-request ceiling, named and declared once.");
        Assert.AreEqual(
            1, CountOf(code, "private const int SendCeiling = 36;"),
            "the owner-fixed actual-send ceiling.");
        Assert.AreEqual(
            1, CountOf(code, "private const int SessionCeiling = 2;"),
            "the derived session bound, without which the send ceiling is not enforced.");
    }

    /// <summary>One budget for the whole operation, and never the offline helper's ceiling.</summary>
    /// <remarks>
    /// The precedent this guards against is concrete: an earlier head put the offline fixture's
    /// 100,000-request helper into a live sweep, giving a 156-batch run 157 independent ceilings and
    /// no bound at all. A per-half budget here would do the same thing to a two-half operation.
    /// </remarks>
    [TestMethod]
    public void OneBudgetSpansDiscoveryAndAcceptance()
    {
        var code = CodeOnly(HarnessSource());

        Assert.AreEqual(
            1, CountOf(code, "WireRequestBudget.OfWireRequests("),
            "exactly one budget is constructed for the whole operation.");
        Assert.AreEqual(
            1, CountOf(code, "WireRequestBudget.OfWireRequests(SharedWireCeiling)"),
            "and it is built from the named ceiling, not an inline number.");
        Assert.AreEqual(
            0, CountOf(code, "TestWireBudget()"),
            "the offline helper's 100,000-request ceiling has no place in a live path.");
    }

    /// <summary>
    /// The dossiers are discovered, never written down.
    /// </summary>
    /// <remarks>
    /// THE GUARD THE OWNER DECISION ACTUALLY TURNS ON. Clause 2 requires the discovery to be proved
    /// rather than the subjects accepted as caller guesses, and clause 5 forbids inferring them from
    /// similar identifiers. A single cellar IRI literal in this file would satisfy neither, and it
    /// is the cheapest possible way for this harness to stop being what it claims: the run would
    /// still pass, still retain evidence, and no longer be a discovery. So the absence of a literal
    /// is asserted, and the read-back from the retained payload is asserted present.
    /// </remarks>
    [TestMethod]
    public void NoDossierIsHardcodedAndEveryOneComesFromTheRetainedPayload()
    {
        var code = CodeOnly(HarnessSource());

        Assert.AreEqual(
            0, CountOf(code, "resource/cellar/"),
            "a dossier IRI literal would make this a caller guess rather than a discovery.");
        // THE MECHANISM, NOT A HEADCOUNT. One of these occurrences is the declaration, so a bare
        // total would have to be revised every time the method moved and would assert nothing. What
        // matters is that every CALL parses a payload read back out of custody: if a call were added
        // that parsed an in-memory buffer instead, these two counts would diverge.
        Assert.AreEqual(
            CountOf(code, "ParseDossiers(") - 1,
            CountOf(code, "RetainedPayloadBytes.Span)"),
            "every parse reads a retained payload, and nothing else is parsed.");
        Assert.AreEqual(
            1, CountOf(code, "ParseDossiers(narrowTransport.RetainedPayloadBytes.Span)"),
            "the narrow window's subjects are read back out of custody.");
        Assert.AreEqual(
            1, CountOf(code, "ParseDossiers(wideTransport.RetainedPayloadBytes.Span)"),
            "and so are the wider window's.");
    }

    /// <summary>The predicate is the contract's constant, never a string in this harness.</summary>
    /// <remarks>
    /// A literal would let discovery and acceptance drift apart silently: the producer asks
    /// <c>EuProcedureEventVocabulary.PartOfDossierPredicateUri</c>, and if this file spelled the
    /// same IRI out by hand, a change to the contract would leave the discovery asking the old
    /// predicate while every test still passed.
    /// </remarks>
    [TestMethod]
    public void TheDiscoveryPredicateIsTheContractsAndNotALiteral()
    {
        var code = CodeOnly(HarnessSource());

        // Two occurrences are correct and each is asserted where it belongs: the query has to ASK
        // the contract's predicate, and the terminal index has to RECORD which predicate was asked.
        // A single total would be satisfied by either one alone.
        Assert.AreEqual(
            1,
            CountOf(code, "EuProcedureEventVocabulary.PartOfDossierPredicateUri + \"> ?dossier"),
            "the discovery query asks the predicate the frozen contract declares.");
        Assert.AreEqual(
            1,
            CountOf(code, "predicate = EuProcedureEventVocabulary.PartOfDossierPredicateUri"),
            "and the terminal index records which predicate this run actually asked.");
        Assert.AreEqual(
            0, CountOf(code, "event_legal_part_of_dossier"),
            "and it never spells that predicate out, which could drift from the contract.");
        // RECORDED, NOT ASKED - and the difference is the whole point. The predicate the owner
        // decision named exists nowhere in this codebase outside one prose comment, so this run
        // cannot ask it. It MUST still say so in its own evidence, or a reader of the retained
        // packet would have no way to know the run asked something other than what was ordered.
        // So the literal is required to appear exactly once, inside the terminal index's note, and
        // nowhere else - a guard of "zero occurrences" would have forced the discrepancy to go
        // unrecorded, and a guard of "one occurrence" alone would not care where it was.
        Assert.AreEqual(
            1, CountOf(code, "procedure_event_belongs_to_procedure_dossier"),
            "the decision's predicate is named once, where the evidence records the discrepancy.");

        // Bracketed by the retention method, not by the fields around the note: askedBodySha256
        // first occurs in RetainAsync's own signature, so bracketing on it put the window in the
        // wrong place and the guard failed for a reason that had nothing to do with the property.
        var retention = code.IndexOf(
            "private static async Task RetainAsync", StringComparison.Ordinal);
        var named = code.IndexOf(
            "procedure_event_belongs_to_procedure_dossier", StringComparison.Ordinal);
        Assert.IsGreaterThan(0, retention, "the retention method was not found.");
        Assert.IsGreaterThan(
            retention, named,
            "the decision's predicate is mentioned only inside the code that writes evidence, "
            + "never in the code that builds the question this run sends.");
        Assert.AreEqual(
            1, CountOf(code, "predicateNote"),
            "and the note carrying it is part of the retained terminal index.");
    }

    /// <summary>Nothing concludes the operation before its cost is on disk.</summary>
    /// <remarks>
    /// STATES THE MECHANISM, NOT THE INSTANCE. A stopped run is the one whose cost most needs
    /// reporting, so the terminal index is written on every outcome before anything is judged. The
    /// rule is that no assertion may stand between the producer returning and the retention of what
    /// it spent; asserting on the presence of one named call would pass on a file that had added a
    /// second, earlier judgement.
    /// </remarks>
    [TestMethod]
    public void TheTerminalIndexIsRetainedBeforeAnythingIsJudged()
    {
        var code = CodeOnly(HarnessSource());

        var producerReturn = code.IndexOf("acceptanceRefusal = production.Delivered", StringComparison.Ordinal);
        Assert.IsGreaterThan(0, producerReturn, "the producer call was not found.");
        var retain = code.IndexOf("await RetainAsync(\n            root, store,", StringComparison.Ordinal);
        if (retain < 0)
        {
            retain = code.IndexOf("await RetainAsync(", producerReturn, StringComparison.Ordinal);
        }

        Assert.IsGreaterThan(producerReturn, retain, "the retention must follow the producer call.");
        Assert.AreEqual(
            0, CountOf(code[producerReturn..retain], "Assert."),
            "no assertion may stand between the producer returning and its cost being retained.");
    }

    /// <summary>Both ceilings are asserted, and sessions are counted rather than assumed.</summary>
    /// <remarks>
    /// The charged ceiling is enforced by the budget itself; the send ceiling is not, so it has to
    /// be asserted from the arithmetic. Sessions are counted by observation for the reason the draft
    /// sweep learned the hard way: a producer call does not always open a session, and counting
    /// attempted calls put a phantom robots fetch into the expected total.
    /// </remarks>
    [TestMethod]
    public void BothCeilingsAreAssertedAndSessionsAreObserved()
    {
        var code = CodeOnly(HarnessSource());

        Assert.AreEqual(
            1, CountOf(code, "SharedWireCeiling, budget.Spent"),
            "the charged ceiling is asserted against what was actually charged.");
        Assert.AreEqual(
            1, CountOf(code, "SendCeiling, budget.Spent + sessionsOpened"),
            "the send ceiling is asserted from the derivation, since the budget cannot enforce it.");
        Assert.AreEqual(
            1, CountOf(code, "SessionCeiling, sessionsOpened"),
            "and the session bound the send ceiling depends on is asserted too.");
        Assert.AreEqual(
            2, CountOf(code, "sessionsOpened++"),
            "two sessions, each counted where it is observed to have opened.");
        Assert.AreEqual(
            0, CountOf(code, "sessionsOpened = 2"),
            "the session count is observed, never asserted into existence.");
    }

    /// <summary>One environment read, and it is the gate.</summary>
    /// <remarks>
    /// Exactly one read, because a second is how a subject or a ceiling gets substituted at run time
    /// in a way the reviewed candidate never showed. This harness has no subjects to substitute, but
    /// it does have two ceilings worth protecting from an environment override.
    /// </remarks>
    [TestMethod]
    public void ExactlyOneEnvironmentReadAndItIsTheGate()
    {
        var code = CodeOnly(HarnessSource());

        Assert.AreEqual(
            1, CountOf(code, "GetEnvironmentVariable("),
            "exactly one environment read - the enable gate - and nothing that selects subjects.");
        Assert.AreEqual(
            1, CountOf(code, "GetEnvironmentVariable(EnableVariable)"),
            "and that one read is the gate.");
    }

    /// <summary>
    /// Whole-line comments removed, so a guard cannot be satisfied by the prose explaining it.
    /// </summary>
    private static string CodeOnly(string source)
    {
        var builder = new StringBuilder(source.Length);
        foreach (var line in source.Split('\n'))
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("///", StringComparison.Ordinal)
                || trimmed.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            builder.Append(line).Append('\n');
        }

        return builder.ToString();
    }

    private static int CountOf(string haystack, string needle)
    {
        var count = 0;
        var index = haystack.IndexOf(needle, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = haystack.IndexOf(needle, index + needle.Length, StringComparison.Ordinal);
        }

        return count;
    }

    private static string HarnessSource()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
            && !File.Exists(Path.Combine(directory.FullName, "Lex.V3.slnx")))
        {
            directory = directory.Parent;
        }

        var root = directory?.FullName
            ?? throw new InvalidOperationException("Checkout root not found.");
        return File.ReadAllText(
            Path.Combine(root, "tests", "Lex.V3.Ingest.Tests", HarnessFile));
    }
}
