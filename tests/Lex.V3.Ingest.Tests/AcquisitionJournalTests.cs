using System.Text;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The progress journal as its reader sees it: a journal the writer wrote reads back whole, and one that was reordered,
/// cut, edited or written by anything else does not read at all. Only an unterminated last line, the trace a kill leaves
/// in the middle of a write, is dropped. A journal resumes only in the build and invocation that wrote it, and only when
/// every object it names is held.
/// </summary>
[TestClass]
public sealed class AcquisitionJournalTests
{
    private static readonly string Arguments = new('a', 64);

    [TestMethod]
    public async Task AWrittenJournalReadsBackWithItsHeaderUnitsAndSpend()
    {
        var (store, header, held) = await HeldRenderersAsync();
        var journal = await WriteAsync(header, held);

        var resume = new AcquisitionResume(journal);

        Assert.AreEqual(3L, resume.LastSeq);
        Assert.AreEqual(CustodyDigest.Of(journal), resume.JournalSha256);
        Assert.AreEqual(header.Source, resume.Header.Source);
        Assert.HasCount(header.Renderers.Length, resume.Header.Renderers);
        Assert.AreEqual(7, resume.LastWireSpent);
        Assert.AreEqual(1, resume.Count(AcquisitionJournal.EuropeAdapterPhase));
        Assert.AreEqual(2, resume.Count(AcquisitionJournal.EuropeFormexEnumerationPhase));
        Assert.IsTrue(resume.TryTake(AcquisitionJournal.EuropeAdapterPhase, "query", out var payload));
        Assert.AreEqual(held[0], ContractJson.Deserialize<SourceArtifactRef>(payload.GetRawText()).Sha256);
        Assert.IsFalse(resume.TryTake(AcquisitionJournal.EuropeAdapterPhase, "query", out _), "a unit is taken once");
        await resume.RequireHeldAsync(store, CancellationToken.None);
    }

    [TestMethod]
    [DataRow("reordered")]
    [DataRow("gap")]
    [DataRow("edited_byte")]
    [DataRow("torn_middle_line")]
    [DataRow("crlf")]
    [DataRow("empty")]
    [DataRow("no_header")]
    [DataRow("unit_twice")]
    [DataRow("unknown_phase")]
    [DataRow("spend_decreases")]
    public async Task AJournalThatWasReorderedCutEditedOrWrittenOtherwiseDoesNotRead(string fault)
    {
        var (_, header, held) = await HeldRenderersAsync();
        var lines = Lines(await WriteAsync(header, held));
        var journal = fault switch
        {
            "reordered" => Join(lines[0], lines[2], lines[1], lines[3]),
            "gap" => Join(lines[0], lines[1], lines[3]),
            // One character of a middle line: the line still reads, and the next line no longer follows it.
            "edited_byte" => Join(lines[0], lines[1].Replace("\"query\"", "\"querz\"", StringComparison.Ordinal), lines[2], lines[3]),
            "torn_middle_line" => Join(lines[0], lines[1], lines[2][..(lines[2].Length / 2)], lines[3]),
            "crlf" => Encoding.UTF8.GetBytes(string.Join("\r\n", lines) + "\r\n"),
            "empty" => Array.Empty<byte>(),
            "no_header" => Chain(held, (AcquisitionJournal.EuropeAdapterPhase, "query", 0), (AcquisitionJournal.EuropeAdapterPhase, "other", 0)),
            "unit_twice" => Chain(held, (AcquisitionJournal.HeaderPhase, AcquisitionJournal.Schema, 0),
                (AcquisitionJournal.EuropeFormexEnumerationPhase, held[1], 0), (AcquisitionJournal.EuropeFormexEnumerationPhase, held[1], 0)),
            "unknown_phase" => Chain(held, (AcquisitionJournal.HeaderPhase, AcquisitionJournal.Schema, 0), ("lu-document", "1", 0)),
            "spend_decreases" => Chain(held, (AcquisitionJournal.HeaderPhase, AcquisitionJournal.Schema, 0),
                (AcquisitionJournal.EuropeAdapterPhase, "query", 5), (AcquisitionJournal.EuropeCatalogPhase, "europe", 4)),
            _ => throw new AssertFailedException(fault),
        };
        Assert.ThrowsExactly<CustodyIntegrityException>(() => new AcquisitionResume(journal));
    }

    [TestMethod]
    public async Task AnUnterminatedLastLineIsDroppedAndTheVerifiedPrefixResumes()
    {
        var (_, header, held) = await HeldRenderersAsync();
        var whole = await WriteAsync(header, held);
        var lines = Lines(whole);
        var torn = whole.Concat(Encoding.UTF8.GetBytes(lines[3][..(lines[3].Length / 2)])).ToArray();

        var resume = new AcquisitionResume(torn);

        Assert.AreEqual(3L, resume.LastSeq);
        Assert.AreEqual(CustodyDigest.Of(torn), resume.JournalSha256, "the held journal is the exact bytes read, its torn tail included");
    }

    [TestMethod]
    public async Task AJournalResumesOnlyInItsBuildAndInvocationAndOnlyWhenEverythingItNamesIsHeld()
    {
        var (store, header, held) = await HeldRenderersAsync();
        var journal = await WriteAsync(header, held);
        var resume = new AcquisitionResume(journal);
        Assert.ThrowsExactly<CustodyIntegrityException>(() => resume.RequireInvocation("another-source", Arguments));
        Assert.ThrowsExactly<CustodyIntegrityException>(() => resume.RequireInvocation(header.Source, new string('b', 64)));
        resume.RequireInvocation(header.Source, Arguments);

        var missing = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        foreach (var digest in held.Skip(1))
            await missing.CreateAsync(await store.ReadByDigestAsync(digest, CancellationToken.None), CustodyClass.NightlyFloor90d, CancellationToken.None);
        try
        {
            await AcquisitionResume.OpenAsync(missing, journal, header.Source, Arguments, TimeProvider.System, CancellationToken.None);
            Assert.Fail("A journal naming an object custody does not hold must not resume.");
        }
        catch (Exception exception) when (exception is CustodyRequiredException or CustodyIntegrityException)
        {
        }

        // Opened in full, the journal's exact bytes and its resume record are held, and a catalog's resumption is held to
        // that record.
        var opened = await AcquisitionResume.OpenAsync(store, journal, header.Source, Arguments, TimeProvider.System, CancellationToken.None);
        CollectionAssert.AreEqual(journal, (await store.ReadByDigestAsync(opened.JournalSha256, CancellationToken.None)).ToArray());
        Assert.AreEqual(opened.JournalSha256, opened.Continuation!.JournalSha256);
        var resumption = opened.Summarize([new AcquisitionResumedPhase(AcquisitionJournal.EuropeAdapterPhase, 1, 0)], 3, DateTimeOffset.UtcNow);
        await AcquisitionResume.VerifyAsync(store, resumption, CancellationToken.None);
        await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() => AcquisitionResume.VerifyAsync(store,
            resumption with { PreviousWireSpentAtLeast = resumption.PreviousWireSpentAtLeast + 1 }, CancellationToken.None));
    }

    // Three renderer sources held in an in-memory store, standing in for what a journal names.
    private static async Task<(EuAcquisitionTestFixture.EuInMemoryCustodyStore Store, AcquisitionJournalHeader Header, string[] Held)>
        HeldRenderersAsync()
    {
        var store = new EuAcquisitionTestFixture.EuInMemoryCustodyStore();
        var held = new List<string>();
        foreach (var text in new[] { "first held object", "second held object", "third held object" })
        {
            var receipt = await store.CreateAsync(Encoding.UTF8.GetBytes(text), CustodyClass.NightlyFloor90d, CancellationToken.None);
            held.Add(receipt.Reference.ContentSha256);
        }

        var header = new AcquisitionJournalHeader(AcquisitionJournal.CurrentSource(), Arguments, 1000,
            [new AcquisitionJournalRenderer("renderer.cs", new SourceArtifactRef($"urn:uuid:{Guid.NewGuid():D}", held[2]))], null);
        return (store, header, held.ToArray());
    }

    // A header and three units, through the production writer, spending as a run would between them.
    private static async Task<byte[]> WriteAsync(AcquisitionJournalHeader header, string[] held)
    {
        var budget = WireRequestBudget.OfWireRequests(1000);
        using var stream = new MemoryStream();
        await using (var journal = new AcquisitionJournal(stream, header, budget, TimeProvider.System))
        {
            for (var request = 0; request < 7; request++) Assert.IsTrue(budget.TryReserveAttempt());
            await journal.AppendAsync(AcquisitionJournal.EuropeAdapterPhase, "query",
                AcquisitionJournal.Payload(new SourceArtifactRef($"urn:uuid:{Guid.NewGuid():D}", held[0])), [held[0]]);
            await journal.AppendAsync(AcquisitionJournal.EuropeFormexEnumerationPhase, held[1],
                AcquisitionJournal.Payload(new { family = "f", expression_sha256 = held[1] }), [held[1]]);
            await journal.AppendAsync(AcquisitionJournal.EuropeFormexEnumerationPhase, held[2],
                AcquisitionJournal.Payload(new { family = "f", expression_sha256 = held[2] }), [held[2]]);
        }

        return stream.ToArray();
    }

    // Lines chained exactly as the writer chains them but chosen freely, so a journal the writer itself refuses to write
    // (a unit twice, a phase it does not know, a spend that falls) can be offered to the reader.
    private static byte[] Chain(string[] held, params (string Phase, string Key, int Spent)[] units)
    {
        var text = new StringBuilder();
        string? previous = null;
        for (var seq = 0; seq < units.Length; seq++)
        {
            var (phase, key, spent) = units[seq];
            var payload = phase == AcquisitionJournal.HeaderPhase
                ? AcquisitionJournal.Payload(new AcquisitionJournalHeader(AcquisitionJournal.CurrentSource(), Arguments, 1000, [], null))
                : AcquisitionJournal.Payload(new { stand_in = key });
            var line = ContractJson.Serialize(new AcquisitionJournal.Line(seq, previous, phase, key, payload,
                phase == AcquisitionJournal.HeaderPhase ? Array.Empty<string>() : new[] { held[0] }, spent, DateTimeOffset.UnixEpoch));
            text.Append(line).Append('\n');
            previous = CustodyDigest.Of(Encoding.UTF8.GetBytes(line));
        }

        return Encoding.UTF8.GetBytes(text.ToString());
    }

    private static string[] Lines(byte[] journal) =>
        Encoding.UTF8.GetString(journal).Split('\n', StringSplitOptions.RemoveEmptyEntries);

    private static byte[] Join(params string[] lines) => Encoding.UTF8.GetBytes(string.Join('\n', lines) + "\n");
}
