using System.Text;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// Structural guards over the EU procedure-event live harness: the properties a reader cannot
/// confirm by running it, because it is skipped by default.
/// </summary>
/// <remarks>
/// <para>
/// GUARDS READ CODE, NEVER PROSE. That harness documents its predicate, both ceilings and its
/// bootstrap bound in doc comments, so a <c>Contains</c> over the raw file would be satisfied by the
/// explanation of a property rather than by the property. This failure has already happened twice in
/// this programme, so every assertion below runs over <see cref="CodeOnly"/>.
/// </para>
/// <para>
/// AND A GUARD MUST PIN THE MECHANISM, NOT ITS TRACE. Three separate review findings were about
/// things a presence check cannot see. A recorded digest passed while being sixty-four zeroes. A
/// <c>finally</c> was present while every controlled stop still reported green, because the stops
/// <c>return</c>ed past the judgement the <c>finally</c> was supposed to precede. Checks existed for
/// distinctness while running after the traffic they were meant to prevent. So several guards below
/// compare source POSITIONS, and one removes the operation's own body before asserting that nothing
/// in what remains can skip the judgement.
/// </para>
/// </remarks>
[TestClass]
public sealed class EuProcedureEventLiveGuardTests
{
    private const string HarnessFile = "EuProcedureEventLiveAcceptance.cs";

    /// <summary>The two owner-fixed ceilings and the bootstrap bound are the numbers in the code.</summary>
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
            1, CountOf(code, "private const int BootstrapCeiling = 2;"),
            "the derived bootstrap bound, without which the send ceiling is not enforced.");
    }

    /// <summary>
    /// No controlled stop can skip the judgement.
    /// </summary>
    /// <remarks>
    /// THE GUARD FOR THE WORST DEFECT THIS HARNESS HAD. Every stop used to <c>return</c> out of the
    /// gated method: the <c>finally</c> wrote the terminal index and the method then returned
    /// normally, so a refused one-shot operation was retained as a refusal and REPORTED GREEN.
    /// Counting the <c>finally</c> could not see it, as the reviewer said.
    /// </para>
    /// <para>
    /// So this excises the operation's own body - the local function, whose early returns are
    /// legitimate because they leave only it - and asserts that nothing in what remains can return
    /// before the verdict is judged. A stop moved back out into the test method reintroduces a
    /// <c>return;</c> here and fails.
    /// </remarks>
    [TestMethod]
    public void NoControlledStopCanSkipTheJudgement()
    {
        var code = CodeOnly(HarnessSource());

        // Scoped to the gated TEST METHOD, then with the operation's own body excised. A helper
        // such as the window observer returns early for legitimate reasons and is out of scope;
        // what matters is that nothing in the test method itself can return before the judgement.
        var testBody = ExtractBlock(
            code, "public async Task TwoDiscoveredDossiersAreAnsweredByThePublisher()");
        var withoutOperation = ExciseBlock(testBody, "async Task RunOperationAsync()");

        Assert.AreEqual(
            0, CountOf(withoutOperation, "return;"),
            "outside the operation's own body nothing may return, or a stop would skip the "
            + "judgement and a refused run would report green.");

        var finallyEnd = code.IndexOf("terminal evidence: ", StringComparison.Ordinal);
        var judgement = code.IndexOf(
            "Assert.AreEqual(\n            DeliveredVerdict, outcome.Verdict,", StringComparison.Ordinal);
        Assert.IsGreaterThan(0, judgement, "the verdict must be judged.");
        Assert.IsGreaterThan(finallyEnd, judgement, "and judged after the terminal write.");
        Assert.AreEqual(
            1, CountOf(code, "internal const string DeliveredVerdict = \"AcceptanceDelivered\";"),
            "one verdict is a pass, named once, and every other outcome fails the assertion.");
        // POSITION, NOT JUST COUNT. Moving this one assignment out of the operation and after it
        // survived a count-only guard while making EVERY run report delivered - the same defect
        // class as the early return, arriving from the other direction.
        var operation = ExtractBlock(testBody, "async Task RunOperationAsync()");
        Assert.AreEqual(
            1, CountOf(operation, "outcome.Verdict = DeliveredVerdict;"),
            "the delivered verdict is set inside the operation, at the end of a complete run.");
        Assert.AreEqual(
            0, CountOf(withoutOperation, "outcome.Verdict = DeliveredVerdict;"),
            "and never outside it, where it would apply to every outcome including a refusal.");
    }

    /// <summary>One budget for the whole operation, and never the offline helper's ceiling.</summary>
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

    /// <summary>The asked request bytes are reopened out of custody and byte-compared.</summary>
    /// <remarks>
    /// The reviewer replaced the recorded digest with sixty-four zeroes and nothing failed.
    /// Recording a digest is not proof of what was asked; reopening the retained bytes by that
    /// address and comparing them is.
    /// </remarks>
    [TestMethod]
    public void TheAskedRequestBytesAreReopenedAndCompared()
    {
        var code = CodeOnly(HarnessSource());

        Assert.AreEqual(
            1, CountOf(code, "window.AskedBodySha256 = transport.LogicalRequest.Body.Sha256;"),
            "the reopened address is the one the transport recorded for the body it sent.");
        Assert.AreEqual(
            1, CountOf(code, "store.ReadByDigestAsync("),
            "the asked bytes are reopened from custody by their content address.");
        Assert.AreEqual(
            1, CountOf(code, "reopened.Span.SequenceEqual(bound.Body)"),
            "and compared byte for byte against the query this run re-derived.");
        Assert.AreEqual(
            1, CountOf(code, "window.AskedBodyLength == bound.Body.Length"),
            "with the length checked too, so a truncation cannot pass on a digest alone.");
        // Comma-suffixed: the two judgement assertions. The pre-acceptance stop reads the same two
        // properties without a trailing comma, so a bare count of four would not distinguish
        // stopping from judging - and the stop's position is asserted by the ordering guard.
        Assert.AreEqual(
            2, CountOf(code, "!.RequestBytesReopenedAndEqual,"),
            "and both windows are judged on the comparison's result.");
    }

    /// <summary>
    /// Each window is attached to the outcome before anything that can throw.
    /// </summary>
    /// <remarks>
    /// An earlier head filled a local window and assigned it on return, so a fault inside the
    /// reopen or the parse serialized that window as null - losing exactly the asked identity and
    /// digest the repair claimed were retained on fault.
    /// </remarks>
    [TestMethod]
    public void WindowEvidenceSurvivesAFault()
    {
        var code = CodeOnly(HarnessSource());

        Assert.AreEqual(
            1, CountOf(code, "outcome.Narrow = new Window();"),
            "the narrow window is attached before it is populated.");
        Assert.AreEqual(
            1, CountOf(code, "outcome.Wide = new Window();"),
            "and so is the wider one.");
        Assert.AreEqual(
            1, CountOf(code, "Window window,"),
            "the observer takes the window and populates it in place.");
        Assert.AreEqual(
            0, CountOf(code, "var window = new Window();"),
            "no window is built locally, where a throw would lose it.");

        // Each attachment is compared with the call that passes THAT window. Comparing both
        // against the first call of either passed for narrow and failed for wide, which measured
        // this guard's own sloppiness rather than the harness's ordering.
        foreach (var (attach, passed) in new[]
        {
            ("outcome.Narrow = new Window();", "outcome.Narrow, glue"),
            ("outcome.Wide = new Window();", "outcome.Wide, glue"),
        })
        {
            Assert.IsGreaterThan(
                code.IndexOf(attach, StringComparison.Ordinal),
                code.IndexOf(passed, StringComparison.Ordinal),
                $"'{attach}' precedes the call that can throw with it.");
        }
    }

    /// <summary>The renderer's declared source is the file that implements the renderer.</summary>
    [TestMethod]
    public void TheRendererSourceIsTheFileThatImplementsIt()
    {
        var code = CodeOnly(HarnessSource());

        Assert.AreEqual(
            1, CountOf(code, "\"tests\", \"Lex.V3.Ingest.Tests\", HarnessFileName));"),
            "the renderer source bytes are this harness's own file, which implements the renderer.");
        Assert.AreEqual(
            0, CountOf(code, "EuProcedureEventDiscoveryPlan.cs"),
            "the renderer is not attributed to a source file that does not contain it.");
    }

    /// <summary>The dossiers are discovered, never written down.</summary>
    [TestMethod]
    public void NoDossierIsHardcodedAndEveryOneComesFromTheRetainedPayload()
    {
        var code = CodeOnly(HarnessSource());

        Assert.AreEqual(
            0, CountOf(code, "resource/cellar/"),
            "a dossier IRI literal would make this a caller guess rather than a discovery.");
        Assert.AreEqual(
            CountOf(code, "ParseDossiers(") - 1,
            CountOf(code, "ParseDossiers(transport.RetainedPayloadBytes.Span)"),
            "every parse reads a retained payload, and nothing else is parsed.");
        Assert.AreEqual(
            1, CountOf(code, "outcome.Discovered = proof.Raw;"),
            "the subjects handed on are the ones the proof admitted.");
    }

    /// <summary>
    /// Two distinct dossiers are proved on the producer's own terms before its bootstrap opens.
    /// </summary>
    /// <remarks>
    /// AN ORDERING GUARD, AND THE REVIEWER'S SECOND FINDING. A SPARQL <c>"type":"uri"</c> label is
    /// the publisher's claim: his probe admitted <c>"not an iri"</c>. And the producer opens its
    /// robots bootstrap BEFORE canonicalizing its batch, so a raw-distinct pair reducing to one
    /// member would have caused that bootstrap and then thrown. Both values must therefore reduce
    /// through the producer's own canonical form, and be distinct in it, before the bootstrap is
    /// counted or opened.
    /// </remarks>
    [TestMethod]
    public void TwoDistinctDossiersAreProvedCanonicallyBeforeAcceptanceOpens()
    {
        var code = CodeOnly(HarnessSource());

        Assert.AreEqual(
            1, CountOf(code, "EuPackRootCanonicalForm.TryCanonicalize("),
            "reduction uses the producer's own canonical form, not an approximation of it.");
        Assert.AreEqual(
            1, CountOf(code, "DiscoveryValueIsNotCanonical"),
            "a value that does not reduce is a named stop.");
        Assert.AreEqual(
            1, CountOf(code, "DiscoveryReturnedACanonicalDuplicate"),
            "and a pair that reduces to one member is a named stop.");

        var proof = code.IndexOf("var proof = ProveTwoDistinctDossiers(", StringComparison.Ordinal);
        var counted = code.IndexOf(
            "accounting.BootstrapsAttempted++;\n            var production", StringComparison.Ordinal);
        var producer = code.IndexOf(
            "new EuProcedureEventProducer(store, TimeProvider.System).RunAsync", StringComparison.Ordinal);
        Assert.IsGreaterThan(0, proof, "the proof call was not found.");
        Assert.IsGreaterThan(proof, counted, "the acceptance bootstrap is counted after the proof,");
        Assert.IsGreaterThan(proof, producer, "and opened after it.");
        Assert.AreEqual(
            1, CountOf(code, "if (proof.Canonical is null)"),
            "and a failed proof stops the run rather than handing values on.");
    }

    /// <summary>The terminal index is written in a finally, so no outcome escapes without it.</summary>
    [TestMethod]
    public void TheTerminalIndexIsWrittenInAFinallySoNoOutcomeEscapes()
    {
        var code = CodeOnly(HarnessSource());

        var finallyStart = code.IndexOf("\n        finally\n", StringComparison.Ordinal);
        Assert.IsGreaterThan(0, finallyStart, "the gated run must retain its cost in a finally.");
        var retainCall = code.IndexOf("await RetainAsync(", finallyStart, StringComparison.Ordinal);
        Assert.IsGreaterThan(finallyStart, retainCall, "and the retention is inside that finally.");
        Assert.AreEqual(
            1, CountOf(code, "await RetainAsync("),
            "one retention, on every path, rather than one per outcome that someone remembered.");
        Assert.IsGreaterThan(
            code.IndexOf("production.EventsOf(", StringComparison.Ordinal), finallyStart,
            "interpretation that can throw runs before the finally that reports it.");
        Assert.AreEqual(
            1, CountOf(code, "outcome.Verdict = \"Faulted\";"),
            "a throw is recorded as an outcome rather than escaping unnamed.");
    }

    /// <summary>Bootstraps are counted before they are attempted, because a refused one has sent.</summary>
    [TestMethod]
    public void BootstrapsAreCountedBeforeTheyAreAttempted()
    {
        var code = CodeOnly(HarnessSource());

        var counted = code.IndexOf("accounting.BootstrapsAttempted++;", StringComparison.Ordinal);
        var started = code.IndexOf(
            "RoutedHttpAcquisitionSession.StartAsync(", StringComparison.Ordinal);
        Assert.IsGreaterThan(0, counted, "the bootstrap count was not found.");
        Assert.IsGreaterThan(counted, started, "the count precedes the attempt it accounts for.");
        Assert.AreEqual(
            2, CountOf(code, "accounting.BootstrapsAttempted++;"),
            "both bootstraps are counted: discovery's and the producer's.");
        Assert.AreEqual(
            1, CountOf(code, "accounting.DiscoveryBootstrapEvidencePresent = start.Evidence is not null;"),
            "and a refused bootstrap records whether it carried response evidence.");
    }

    /// <summary>Both ceilings are asserted, and the send bound uses attempts rather than successes.</summary>
    [TestMethod]
    public void BothCeilingsAreAssertedFromWhatWasCounted()
    {
        var code = CodeOnly(HarnessSource());

        Assert.AreEqual(
            1, CountOf(code, "SharedWireCeiling, budget.Spent"),
            "the charged ceiling is asserted against what was actually charged.");
        Assert.AreEqual(
            1, CountOf(code, "SendCeiling, budget.Spent + accounting.BootstrapsAttempted"),
            "the send ceiling is asserted from attempts, since the budget cannot enforce it and a "
            + "refused bootstrap still sent.");
        Assert.AreEqual(
            1, CountOf(code, "BootstrapCeiling, accounting.BootstrapsAttempted"),
            "and the bootstrap bound the send ceiling depends on is asserted too.");
        Assert.AreEqual(
            0, CountOf(code, "budget.Spent + accounting.SessionsOpened"),
            "the send bound must not be derived from sessions that opened.");
    }

    /// <summary>One environment read, and it is the gate.</summary>
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

    /// <summary>Returns one brace-delimited block, named by the line that opens it.</summary>
    private static string ExtractBlock(string code, string signature)
    {
        var start = code.IndexOf(signature, StringComparison.Ordinal);
        Assert.IsGreaterThan(0, start, $"'{signature}' was not found.");
        var open = code.IndexOf('{', start);
        var depth = 0;
        for (var index = open; index < code.Length; index++)
        {
            if (code[index] == '{')
            {
                depth++;
            }
            else if (code[index] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return code[start..(index + 1)];
                }
            }
        }

        Assert.Fail($"'{signature}' block was not closed.");
        return code;
    }

    /// <summary>
    /// Removes one brace-delimited block, named by the line that opens it.
    /// </summary>
    /// <remarks>
    /// Used to take the operation's own body out of the file before asserting that nothing in what
    /// remains can return early. Counting braces is crude but exact enough for a declaration this
    /// guard also asserts is present.
    /// </remarks>
    private static string ExciseBlock(string code, string signature)
    {
        var start = code.IndexOf(signature, StringComparison.Ordinal);
        Assert.IsGreaterThan(0, start, $"'{signature}' was not found.");
        var open = code.IndexOf('{', start);
        Assert.IsGreaterThan(0, open, "the block's opening brace was not found.");

        var depth = 0;
        for (var index = open; index < code.Length; index++)
        {
            if (code[index] == '{')
            {
                depth++;
            }
            else if (code[index] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return code[..start] + code[(index + 1)..];
                }
            }
        }

        Assert.Fail("the block was not closed.");
        return code;
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
