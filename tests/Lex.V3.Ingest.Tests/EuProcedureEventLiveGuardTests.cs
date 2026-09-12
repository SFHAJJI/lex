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
/// explanation of a property rather than by the property. This exact failure has already happened
/// twice in this programme. So every assertion below runs over <see cref="CodeOnly"/>, with
/// whole-line comments removed first.
/// </para>
/// <para>
/// AND A GUARD MUST PIN THE COMPARISON, NOT THE RECORDING. The reviewer demonstrated the difference
/// on an earlier head: he replaced the recorded request digest with sixty-four zeroes, and the
/// candidate built while all seven guards still passed, because they only proved a digest had been
/// written down. <see cref="TheAskedRequestBytesAreReopenedAndCompared"/> exists because of that
/// mutation, and it pins the reopen-and-byte-compare rather than the field that holds its result.
/// </para>
/// <para>
/// SEVERAL GUARDS ARE ORDERING GUARDS. Three of the four findings against the earlier head were
/// about order, not absence: unproved values reached the producer before they were checked, a
/// throwing interpretation ran before the terminal write, and a bootstrap was counted only after it
/// had succeeded. Counting occurrences cannot see any of those, so those guards compare source
/// positions.
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

    /// <summary>
    /// The asked request bytes are reopened out of custody and byte-compared.
    /// </summary>
    /// <remarks>
    /// THE GUARD THE REVIEWER'S ZERO-DIGEST MUTATION PROVED WAS MISSING. An earlier head recorded
    /// <c>LogicalRequest.Body.Sha256</c> and asserted nothing about it, so replacing that digest
    /// with zeroes changed no test result. Recording a digest is not proof of what was asked; only
    /// reopening the retained bytes by content address and comparing them to the re-derived query
    /// is. Both the reopen and the comparison are pinned, and so is the fact that both windows are
    /// judged on the result.
    /// </remarks>
    [TestMethod]
    public void TheAskedRequestBytesAreReopenedAndCompared()
    {
        var code = CodeOnly(HarnessSource());

        // THE REVIEWER'S OWN MUTATION, PINNED DIRECTLY. He replaced the recorded digest with
        // sixty-four zeroes and nothing failed. The digest must be the transport's own, because
        // every step after it - the reopen and the comparison - is only as good as the address it
        // was given.
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
        // The needle is the null-forgiving access the two assertions use, because a bare
        // "RequestBytesReopenedAndEqual," also matches the terminal index's own mapping - which is
        // wanted, but is evidence rather than judgement, and is asserted separately below.
        Assert.AreEqual(
            2, CountOf(code, "!.RequestBytesReopenedAndEqual,"),
            "and both windows are asserted on the comparison's result.");
        Assert.AreEqual(
            1, CountOf(code, "requestBytesReopenedAndEqual = window.RequestBytesReopenedAndEqual,"),
            "with the comparison's result recorded per window in the retained terminal index.");
    }

    /// <summary>
    /// The renderer's declared source is the file that implements the renderer.
    /// </summary>
    /// <remarks>
    /// THE HALF OF THE PROVENANCE FINDING MY FIRST MUTATION SET DID NOT COVER. Repointing the
    /// renderer source back at <c>EuProcedureEventDiscoveryPlan.cs</c> survived every other guard,
    /// which made the attribution defect exactly as untested as the digest recording had been: the
    /// retained plan would name a source file that does not contain this query's renderer, so a
    /// reader reopening the packet could not account for the bytes that were sent.
    /// </remarks>
    [TestMethod]
    public void TheRendererSourceIsTheFileThatImplementsIt()
    {
        var code = CodeOnly(HarnessSource());

        Assert.AreEqual(
            1, CountOf(code, "\"tests\", \"Lex.V3.Ingest.Tests\", HarnessFileName));"),
            "the renderer source bytes are this harness's own file, which implements the renderer.");
        Assert.AreEqual(
            1, CountOf(code, "private const string HarnessFileName = \"EuProcedureEventLiveAcceptance.cs\";"),
            "and that file is named once, as a constant.");
        Assert.AreEqual(
            0, CountOf(code, "EuProcedureEventDiscoveryPlan.cs"),
            "the renderer is not attributed to a source file that does not contain it.");
    }

    /// <summary>
    /// The dossiers are discovered, never written down.
    /// </summary>
    /// <remarks>
    /// THE GUARD THE OWNER DECISION TURNS ON. A single cellar IRI literal in this file would make
    /// the run pass, retain evidence, and no longer be a discovery.
    /// </remarks>
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
            1, CountOf(code, "outcome.Discovered = [narrowIris[0], narrowIris[1]];"),
            "the subjects handed on are exactly the two the publisher returned.");
    }

    /// <summary>Only a URI term becomes a subject.</summary>
    /// <remarks>
    /// An earlier head accepted any nonempty value, so a literal or blank node would have been sent
    /// to the producer as though it were a dossier IRI - and the producer would then honestly have
    /// reported finding no events for it.
    /// </remarks>
    [TestMethod]
    public void OnlyAUriTermBecomesASubject()
    {
        var code = CodeOnly(HarnessSource());

        Assert.AreEqual(
            1, CountOf(code, "string.Equals(kind.GetString(), \"uri\", StringComparison.Ordinal)"),
            "the SPARQL term type is part of the answer and is checked.");
        Assert.AreEqual(
            1, CountOf(code, "value.GetString() is { Length: > 0 } iri"),
            "and an empty value is not a subject.");
    }

    /// <summary>The predicate is the contract's constant, never a literal in the query.</summary>
    [TestMethod]
    public void TheDiscoveryPredicateIsTheContractsAndNotALiteral()
    {
        var code = CodeOnly(HarnessSource());

        Assert.AreEqual(
            1,
            CountOf(code, "EuProcedureEventVocabulary.PartOfDossierPredicateUri + \"> ?dossier"),
            "the discovery query asks the predicate the frozen contract declares.");
        Assert.AreEqual(
            1,
            CountOf(code, "predicate = EuProcedureEventVocabulary.PartOfDossierPredicateUri"),
            "and the terminal index records which predicate this run actually asked.");

        // The owner has ruled that the decision's original wording was descriptive and that the
        // contract member is authoritative. Both spellings may therefore appear ONLY inside the
        // evidence note that records that history - never in the code that builds the question.
        var retention = code.IndexOf(
            "private static async Task RetainAsync", StringComparison.Ordinal);
        Assert.IsGreaterThan(0, retention, "the retention method was not found.");
        foreach (var spelling in new[]
        {
            "procedure_event_belongs_to_procedure_dossier",
            "cdm:event_legal_part_of_dossier",
        })
        {
            Assert.AreEqual(
                1, CountOf(code, spelling),
                $"'{spelling}' is named once, where the evidence records the naming history.");
            Assert.IsGreaterThan(
                retention, code.IndexOf(spelling, StringComparison.Ordinal),
                $"'{spelling}' appears only inside the code that writes evidence.");
        }
    }

    /// <summary>
    /// Nothing unproved reaches the publisher: the discovery is established before acceptance opens.
    /// </summary>
    /// <remarks>
    /// AN ORDERING GUARD, BECAUSE THE DEFECT WAS AN ORDERING DEFECT. An earlier head invoked the
    /// producer as soon as two nonempty strings had been parsed and checked their kind, distinctness
    /// and cross-window agreement afterwards. Counting those checks would have passed on that head;
    /// only their position relative to the producer call catches it.
    /// </remarks>
    [TestMethod]
    public void TheDiscoveryIsProvedBeforeAcceptanceOpens()
    {
        var code = CodeOnly(HarnessSource());

        var producer = code.IndexOf(
            "new EuProcedureEventProducer(store, TimeProvider.System).RunAsync", StringComparison.Ordinal);
        Assert.IsGreaterThan(0, producer, "the producer call was not found.");

        foreach (var (marker, why) in new[]
        {
            ("AskedBytesDidNotReopenEqual", "the asked bytes are proved"),
            ("DiscoveryDidNotYieldTwoDossiers", "exactly two subjects are proved"),
            ("DiscoveryReturnedADuplicatePair", "distinctness is proved"),
            ("DiscoveryWindowsDisagree", "cross-window agreement is proved"),
        })
        {
            var position = code.IndexOf(marker, StringComparison.Ordinal);
            Assert.IsGreaterThan(0, position, $"the {marker} stop was not found.");
            Assert.IsGreaterThan(
                position, producer,
                $"{why} before the acceptance session is opened.");
        }
    }

    /// <summary>
    /// The terminal index is written in a finally, so no outcome escapes without it.
    /// </summary>
    /// <remarks>
    /// The earlier guard searched for assertions between two text markers and could not see a
    /// judgement that THREW rather than asserted - and <c>EventsOf</c> throws on a dossier the
    /// delivered result does not carry, which is the mismatch a packet most needs to record. A
    /// <c>finally</c> covers every exit, including faults, which is why the rule is now stated as
    /// the retention's position inside it rather than as an absence of assertions.
    /// </remarks>
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

        // Interpretation that can throw, and every assertion, must follow the retention.
        Assert.IsGreaterThan(
            code.IndexOf("production.EventsOf(", StringComparison.Ordinal), finallyStart,
            "interpretation that can throw runs before the finally that reports it.");
        Assert.IsGreaterThan(
            retainCall, code.IndexOf("var offenders = OffendingHosts(root);", StringComparison.Ordinal),
            "and judgement begins only after the terminal write.");
        Assert.AreEqual(
            1, CountOf(code, "outcome.Verdict = \"Faulted\";"),
            "a throw is recorded as an outcome rather than escaping unnamed.");
    }

    /// <summary>
    /// Bootstraps are counted before they are attempted, because a refused one has already sent.
    /// </summary>
    /// <remarks>
    /// AN ORDERING GUARD. An earlier head incremented only when a session object came back, so a
    /// bootstrap refused after its two-hop robots exchange reported zero hops and the refusal packet
    /// understated the traffic it had caused.
    /// </remarks>
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
