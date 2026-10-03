using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest;

/// <summary>
/// Where an acquisition writes its progress: one line for each unit whose evidence custody already holds, so an
/// interrupted run can be resumed without observing those units again.
/// </summary>
/// <remarks>
/// <para>
/// THE JOURNAL ONLY SAYS WHERE TO LOOK. A line names the checkpoint a unit retained and the custody objects that record
/// depends on; it is not evidence of anything. A resumed run admits every journaled unit through the same checked
/// reader a full offline replay uses (the reopen paths that pin schema versions and recompute result digests), so a
/// journal that is wrong can make a resume refuse or redo a unit, and can never put an unproven claim into the
/// population.
/// </para>
/// <para>
/// A LINE IS APPENDED ONLY AFTER ITS UNIT'S CUSTODY HOLDS HAVE RETURNED. A hold flushes and reads back before it
/// publishes (<c>CustodyHold</c>), so every digest a line names was demonstrably held when the line was written. A
/// unit in flight when the run stopped has no line, and a resumed run acquires it again; a checkpoint it retained
/// without a line stays in custody, orphaned and harmless.
/// </para>
/// <para>
/// AN APPEND IS NOT CANCELLED. A unit whose holds returned is worth recording even while the run is stopping, and a
/// write that a kill interrupts leaves at most a torn last line, which a resume drops. So there is no cancellation
/// token here, and none is missing.
/// </para>
/// </remarks>
public interface IAcquisitionJournal
{
    /// <param name="phase">What kind of unit this is; a resume admits only the phases this build knows how to replay.</param>
    /// <param name="key">The unit's identity within its phase. A journal names each (phase, key) once.</param>
    /// <param name="payload">The existing checkpoint record that says where the unit is held, as the phase's replay reads it.</param>
    /// <param name="held">The digests of the custody objects <paramref name="payload"/> names, each read back before a resume sends any request.</param>
    Task AppendAsync(string phase, string key, JsonElement payload, IReadOnlyList<string> held);
}

/// <summary>
/// The first line of a progress journal: the build and invocation the run belongs to, the renderer sources it opened,
/// and the journal it resumed from, if any. A resume requires the same source and the same arguments.
/// </summary>
/// <param name="Source">The source head of the build that wrote the journal (<see cref="AcquisitionJournal.CurrentSource"/>).</param>
/// <param name="Arguments">The digest of the acquisition arguments (<see cref="AcquisitionJournal.DigestArguments"/>).</param>
/// <param name="WireCeiling">The ceiling this process ran under; a resumed run reports it as the interrupted run's upper bound.</param>
/// <param name="Renderers">The renderer sources this process opened, by checkout file: eight, or two when the EU half was reused.</param>
/// <param name="ResumedFrom">The journal this run resumed, held in custody with its resume record; null for a run started afresh.</param>
public sealed record AcquisitionJournalHeader(string Source, string Arguments, int WireCeiling,
    AcquisitionJournalRenderer[] Renderers, AcquisitionJournalContinuation? ResumedFrom);

/// <summary>One renderer source a journaled run opened: its checkout file and the reference it held the file's bytes under.</summary>
public sealed record AcquisitionJournalRenderer(string File, SourceArtifactRef Reference);

/// <summary>
/// What a resumed run's journal says about the journal it resumed: that journal's digest and last verified line, and
/// the <c>lex-v3-acquisition-resume/1</c> record holding the rest.
/// </summary>
public sealed record AcquisitionJournalContinuation(string JournalSha256, long LastSeq, SourceArtifactRef Record);

/// <summary>
/// What a catalog retained by a resumed run says about the resumption: the resume record and the interrupted run's
/// window and spend it states, then, for the half the catalog describes, how many units of each phase were replayed
/// from the interrupted run's journal and how many were acquired live, with this run's wire spend and time when the
/// catalog was retained. The report of a mount built from such a catalog says all of it.
/// </summary>
/// <remarks>
/// A resumed population was observed in two windows, and nothing here may suggest one. The replayed units were observed
/// by the interrupted run, between <see cref="PreviousStartedAt"/> and <see cref="PreviousLastJournaledAt"/>; the live
/// ones by this run, after <see cref="ResumedAt"/>. The interrupted run's spend is known only as a range: at least what
/// its last journaled line recorded, at most its ceiling.
/// </remarks>
public sealed record AcquisitionResumption(
    SourceArtifactRef Record,
    string JournalSha256,
    long LastSeq,
    DateTimeOffset PreviousStartedAt,
    DateTimeOffset PreviousLastJournaledAt,
    int PreviousWireSpentAtLeast,
    int PreviousWireCeiling,
    DateTimeOffset ResumedAt,
    AcquisitionResumedPhase[] Phases,
    int WireSpent,
    DateTimeOffset CatalogedAt);

/// <summary>One phase of a resumed half: the units replayed from the interrupted run's journal, and those acquired live.</summary>
public sealed record AcquisitionResumedPhase(string Phase, int Replayed, int Live);

/// <summary>
/// The progress journal as append-only JSON lines in the run's custody root, chained by SHA-256 so a resume can tell a
/// journal that is whole from one that was reordered, cut or edited.
/// </summary>
/// <remarks>
/// <para>
/// Each line is <c>{seq, previous, phase, key, payload, held, wire_spent, at}</c>: its position from zero, the SHA-256
/// of the line before it without its newline (null on the first), what the unit is, the existing checkpoint record
/// that says where it is held, the digests of the custody objects that record names, what this process's wire budget
/// had spent when the line was written, and when. Line zero is the header (<see cref="AcquisitionJournalHeader"/>),
/// whose <c>held</c> digests are the renderer sources it names. Every line is the exact canonical form of its record,
/// so one written any other way does not read.
/// </para>
/// <para>
/// THE FILE IS NOT CUSTODY. It is written beside the store's class directories, never into them, and a resumed run's
/// root never links it: a resume reads it, holds its exact bytes as an ordinary custody object together with a
/// <c>lex-v3-acquisition-resume/1</c> record, and then starts a journal of its own that names both.
/// </para>
/// </remarks>
public sealed class AcquisitionJournal : IAcquisitionJournal, IAsyncDisposable
{
    internal const string Schema = "lex-v3-acquisition-journal/1";
    internal const string HeaderPhase = "journal";

    /// <summary>The EU adapter run, keyed <c>query</c>: its retained acquisition checkpoint.</summary>
    internal const string EuropeAdapterPhase = "eu-adapter";

    /// <summary>One Formex manifestation enumeration, keyed by its expression's content digest.</summary>
    internal const string EuropeFormexEnumerationPhase = "eu-formex-enumeration";

    /// <summary>One Formex package acquisition, keyed by its expression's content digest.</summary>
    internal const string EuropeFormexPackagePhase = "eu-formex-package";

    /// <summary>The retained EU acquisition catalog, keyed <c>europe</c>: the whole EU half, reused through its checked reopen.</summary>
    internal const string EuropeCatalogPhase = "eu-catalog";

    /// <summary>The Luxembourg declared scope and plan identity, keyed <c>scope</c>: every Luxembourg unit binds to them.</summary>
    internal const string LuxembourgScopePhase = "lu-scope";

    /// <summary>The Luxembourg vocabulary checkpoint and observation, keyed <c>vocabulary</c>.</summary>
    internal const string LuxembourgVocabularyPhase = "lu-vocabulary";

    /// <summary>One proven Luxembourg query family (a partition or a reconciled cover), keyed by its partition id.</summary>
    internal const string LuxembourgQueryFamilyPhase = "lu-query-family";

    /// <summary>One selected-document GET, keyed by its manifest ordinal and address digest.</summary>
    internal const string LuxembourgDocumentPhase = "lu-document";

    /// <summary>One Gazette listing GET, keyed by its act's ordinal and address digest.</summary>
    internal const string LuxembourgGazettePhase = "lu-gazette";

    private readonly Stream _stream;
    private readonly WireRequestBudget _budget;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly HashSet<(string Phase, string Key)> _written = [];
    private long _seq;
    private string _previous;

    /// <summary>
    /// Starts a journal on <paramref name="stream"/> by writing its header. The header is written here, synchronously, so
    /// no unit can be journaled into a stream that does not yet say which run it belongs to.
    /// </summary>
    public AcquisitionJournal(Stream stream, AcquisitionJournalHeader header, WireRequestBudget budget, TimeProvider timeProvider)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        ArgumentNullException.ThrowIfNull(header);
        _budget = budget ?? throw new ArgumentNullException(nameof(budget));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        if (!stream.CanWrite) throw new ArgumentException("A journal is written to a writable stream.", nameof(stream));
        if (string.IsNullOrWhiteSpace(header.Source) || !CustodyDigest.IsLowercaseSha256(header.Arguments) ||
            header.Renderers is null || header.Renderers.Any(static renderer => renderer is null || renderer.Reference is null))
            throw new ArgumentException("A journal header names its source, its argument digest and its renderer sources.", nameof(header));
        var line = Encode(0, null, HeaderPhase, Schema, Payload(header),
            header.Renderers.Select(static renderer => renderer.Reference.Sha256).ToArray());
        _stream.Write(line);
        _stream.Flush();
        _previous = CustodyDigest.Of(line.AsSpan(0, line.Length - 1));
    }

    /// <summary>The journal file, when <see cref="CreateInCustody"/> opened one.</summary>
    public string? Location { get; private init; }

    /// <summary>
    /// Opens a new journal file in <paramref name="custodyRoot"/> (create-new, written through) and writes its header.
    /// The name carries the start time and a fresh identifier, so two runs in one root never share a journal.
    /// </summary>
    public static AcquisitionJournal CreateInCustody(string custodyRoot, AcquisitionJournalHeader header,
        WireRequestBudget budget, TimeProvider timeProvider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(custodyRoot);
        ArgumentNullException.ThrowIfNull(timeProvider);
        var path = Path.Combine(Path.GetFullPath(custodyRoot), "acquisition-progress-"
            + timeProvider.GetUtcNow().ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture)
            + "-" + Guid.NewGuid().ToString("N") + ".jsonl");
        var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough);
        try
        {
            return new AcquisitionJournal(stream, header, budget, timeProvider) { Location = path };
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>
    /// The source head of this build: the commit the SDK stamps after <c>+</c> in the informational version of every
    /// Lex assembly, or the whole version when there is none. A journal written by one build resumes only in the same one.
    /// </summary>
    public static string CurrentSource()
    {
        var version = typeof(AcquisitionJournal).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(version)) return "unversioned";
        var plus = version.LastIndexOf('+');
        return plus >= 0 && plus < version.Length - 1 ? version[(plus + 1)..] : version;
    }

    /// <summary>
    /// The digest of what an acquisition was asked to acquire: its sorted seeds, its Luxembourg selection, its custody
    /// encoding and the retained EU catalog it reuses. Paths and the wire ceiling are not in it: a resumed run writes a
    /// fresh root, and its ceiling bounds only its own process.
    /// </summary>
    public static string DigestArguments(IReadOnlyList<string> seeds, LuxembourgActRange? act,
        LuxembourgPopulationScope? population, string custodyEncoding, SourceArtifactRef? europeCheckpoint)
    {
        ArgumentNullException.ThrowIfNull(seeds);
        ArgumentException.ThrowIfNullOrWhiteSpace(custodyEncoding);
        return CustodyDigest.Of(Encoding.UTF8.GetBytes(ContractJson.Serialize(new
        {
            schema = "lex-v3-acquisition-arguments/1",
            seeds = seeds.Order(StringComparer.Ordinal).ToArray(),
            act,
            population = population is null ? null : new { policy = population.Policy, ranges = population.Ranges.ToArray() },
            custody_encoding = custodyEncoding,
            europe_checkpoint = europeCheckpoint,
        })));
    }

    /// <summary>
    /// Appends one unit. The line is written and flushed whole before this returns; a second line for the same
    /// (phase, key) is a defect in the caller and throws rather than writing an ambiguous journal.
    /// </summary>
    public async Task AppendAsync(string phase, string key, JsonElement payload, IReadOnlyList<string> held)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phase);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(held);
        if (!IsUnitPhase(phase)) throw new ArgumentException("A journal holds only the unit phases a resume can replay.", nameof(phase));
        if (held.Any(static digest => !CustodyDigest.IsLowercaseSha256(digest)))
            throw new ArgumentException("A journal names held objects by their lowercase SHA-256.", nameof(held));
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_written.Contains((phase, key)))
                throw new InvalidOperationException($"The journal already names {phase} {key}; a unit is journaled once.");
            var line = Encode(_seq + 1, _previous, phase, key, payload, held.ToArray());
            await _stream.WriteAsync(line).ConfigureAwait(false);
            await _stream.FlushAsync().ConfigureAwait(false);
            _written.Add((phase, key));
            _seq++;
            _previous = CustodyDigest.Of(line.AsSpan(0, line.Length - 1));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stream.DisposeAsync().ConfigureAwait(false);
        _gate.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>A checkpoint record as a journal payload: its exact contract JSON.</summary>
    internal static JsonElement Payload<T>(T value)
    {
        using var document = JsonDocument.Parse(ContractJson.Serialize(value));
        return document.RootElement.Clone();
    }

    /// <summary>The unit phases this build writes and resumes. A journal naming any other phase does not resume here.</summary>
    internal static bool IsUnitPhase(string phase) => phase is EuropeAdapterPhase or EuropeFormexEnumerationPhase
        or EuropeFormexPackagePhase or EuropeCatalogPhase or LuxembourgScopePhase or LuxembourgVocabularyPhase
        or LuxembourgQueryFamilyPhase or LuxembourgDocumentPhase or LuxembourgGazettePhase;

    /// <summary>
    /// One line, read only when it is exactly the canonical form this journal writes: strict UTF-8, the contract
    /// serializer's own property order and spelling, lowercase digests and a non-negative spend.
    /// </summary>
    internal static Line Read(ReadOnlySpan<byte> bytes)
    {
        try
        {
            var line = ContractJson.Deserialize<Line>(new UTF8Encoding(false, true).GetString(bytes));
            if (!bytes.SequenceEqual(Encoding.UTF8.GetBytes(ContractJson.Serialize(line))) ||
                string.IsNullOrEmpty(line.Phase) || string.IsNullOrEmpty(line.Key) || line.Held is null ||
                line.Held.Any(static digest => !CustodyDigest.IsLowercaseSha256(digest)) || line.WireSpent < 0 ||
                line.Previous is not null && !CustodyDigest.IsLowercaseSha256(line.Previous))
                throw new CustodyIntegrityException("A progress journal line is not in the form the journal writes.");
            return line;
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException or DecoderFallbackException)
        {
            throw new CustodyIntegrityException("A progress journal line does not read.", exception);
        }
    }

    private byte[] Encode(long seq, string? previous, string phase, string key, JsonElement payload, string[] held) =>
        Encoding.UTF8.GetBytes(ContractJson.Serialize(new Line(seq, previous, phase, key, payload, held,
            _budget.Spent, _timeProvider.GetUtcNow())) + "\n");

    internal sealed record Line(long Seq, string? Previous, string Phase, string Key, JsonElement Payload,
        string[] Held, int WireSpent, DateTimeOffset At);
}

/// <summary>
/// The verified journal of an interrupted acquisition: which of its units are held, and where. A resumed run takes each
/// journaled unit from here exactly once and admits it through that unit's own checked reader; a unit nobody takes is a
/// journal that does not describe this acquisition, and refuses.
/// </summary>
/// <remarks>
/// <para>
/// Verification is all before any request. The constructor reads the lines: each must be canonical, numbered from zero
/// without a gap and chained to the one before it by SHA-256; line zero must be a header; no unit may appear twice. An
/// unterminated last line is the trace of a write a kill interrupted, and is dropped; every other fault refuses.
/// <see cref="OpenAsync"/> then requires the header to name this build's source and this invocation's arguments, reads
/// back every custody object any line names, and holds the journal's exact bytes with a
/// <c>lex-v3-acquisition-resume/1</c> record, so the resumed run's catalogs can name what they resumed.
/// </para>
/// <para>
/// The interrupted run's spend is never more precise than the journal makes it: at least the spend its last line
/// recorded, at most the ceiling its header names. The requests it made after its last line, the unit in flight among
/// them, lie between those bounds; neither run's report counts them exactly.
/// </para>
/// </remarks>
public sealed class AcquisitionResume
{
    private const string RecordSchema = "lex-v3-acquisition-resume/1";

    private readonly byte[] _journal;
    private readonly Dictionary<(string Phase, string Key), Entry> _entries = new();
    private readonly HashSet<string> _held = new(StringComparer.Ordinal);

    /// <summary>Reads and verifies <paramref name="journal"/>'s lines. Throws <see cref="CustodyIntegrityException"/> on any fault.</summary>
    public AcquisitionResume(ReadOnlyMemory<byte> journal)
    {
        _journal = journal.ToArray();
        JournalSha256 = CustodyDigest.Of(_journal);
        AcquisitionJournalHeader? header = null;
        AcquisitionJournal.Line? last = null;
        string? previous = null;
        var startedAt = default(DateTimeOffset);
        var seq = 0L;
        var start = 0;
        while (start < _journal.Length)
        {
            var newline = _journal.AsSpan(start).IndexOf((byte)'\n');
            if (newline < 0) break;
            var bytes = _journal.AsSpan(start, newline);
            var line = AcquisitionJournal.Read(bytes);
            if (line.Seq != seq)
                throw new CustodyIntegrityException($"Journal line {seq} carries position {line.Seq}: a line is missing or out of order.");
            if (!string.Equals(line.Previous, previous, StringComparison.Ordinal))
                throw new CustodyIntegrityException($"Journal line {seq} does not follow the line before it.");
            if (seq == 0)
            {
                header = ReadHeader(line);
                startedAt = line.At;
            }
            else
            {
                if (!AcquisitionJournal.IsUnitPhase(line.Phase))
                    throw new CustodyIntegrityException($"Journal line {seq} names a {line.Phase} unit, which this build does not resume.");
                if (line.WireSpent < last!.WireSpent)
                    throw new CustodyIntegrityException($"Journal line {seq} records less spend than the line before it.");
                if (!_entries.TryAdd((line.Phase, line.Key), new Entry(line.Seq, line.Payload)))
                    throw new CustodyIntegrityException($"Journal line {seq} names {line.Phase} {line.Key} a second time.");
            }

            _held.UnionWith(line.Held);
            previous = CustodyDigest.Of(bytes);
            last = line;
            seq++;
            start += newline + 1;
        }

        if (header is null || last is null)
            throw new CustodyIntegrityException("The journal has no complete first line, so it names no run.");
        Header = header;
        LastSeq = last.Seq;
        StartedAt = startedAt;
        LastJournaledAt = last.At;
        LastWireSpent = last.WireSpent;
    }

    /// <summary>The SHA-256 of the journal's exact bytes, a torn tail included; the digest it is held under.</summary>
    public string JournalSha256 { get; }

    /// <summary>The last whole, verified line: the header alone is line zero.</summary>
    public long LastSeq { get; }

    public AcquisitionJournalHeader Header { get; }

    /// <summary>When the interrupted run wrote its header.</summary>
    public DateTimeOffset StartedAt { get; }

    /// <summary>When the interrupted run wrote its last whole line.</summary>
    public DateTimeOffset LastJournaledAt { get; }

    /// <summary>What the interrupted run had spent at its last whole line: a lower bound on its spend.</summary>
    public int LastWireSpent { get; }

    /// <summary>The <c>lex-v3-acquisition-resume/1</c> record, once <see cref="RetainAsync"/> has held it.</summary>
    public SourceArtifactRef? Record { get; private set; }

    /// <summary>When this run held its resume record, the start of its own observation window.</summary>
    public DateTimeOffset ResumedAt { get; private set; }

    /// <summary>What the resumed run's journal header names, once the resume record is held.</summary>
    public AcquisitionJournalContinuation? Continuation =>
        Record is null ? null : new AcquisitionJournalContinuation(JournalSha256, LastSeq, Record);

    /// <summary>
    /// Everything before any request, in order: the lines verify; the header names this build's source and this
    /// invocation's arguments; every object any line names reads back from <paramref name="store"/>; and the journal and
    /// its resume record are held there. Throws <see cref="CustodyIntegrityException"/> or
    /// <see cref="CustodyRequiredException"/> when any of them fails.
    /// </summary>
    public static async Task<AcquisitionResume> OpenAsync(ICustodyStore store, ReadOnlyMemory<byte> journal,
        string source, string arguments, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        var resume = new AcquisitionResume(journal);
        resume.RequireInvocation(source, arguments);
        await resume.RequireHeldAsync(store, cancellationToken).ConfigureAwait(false);
        await resume.RetainAsync(store, timeProvider, cancellationToken).ConfigureAwait(false);
        return resume;
    }

    /// <summary>A journal resumes only in the build that wrote it and only for the same acquisition.</summary>
    public void RequireInvocation(string source, string arguments)
    {
        if (!string.Equals(Header.Source, source, StringComparison.Ordinal))
            throw new CustodyIntegrityException($"The journal was written by source {Header.Source}, not by this build's {source}.");
        if (!string.Equals(Header.Arguments, arguments, StringComparison.Ordinal))
            throw new CustodyIntegrityException("The journal belongs to an acquisition with other seeds, scope, encoding or retained EU input.");
    }

    /// <summary>Every custody object any line names, read back through the checked reader, each once.</summary>
    public async Task RequireHeldAsync(ICustodyStore store, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        foreach (var digest in _held.Order(StringComparer.Ordinal))
            _ = await CustodyRestore.ReadByDigestCheckedAsync(store, digest, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Holds the journal's exact bytes and the <c>lex-v3-acquisition-resume/1</c> record that names them, the window and
    /// spend the interrupted run states, and the time this run resumed.
    /// </summary>
    public async Task<SourceArtifactRef> RetainAsync(ICustodyStore store, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(timeProvider);
        var (journalReceipt, journalFailure) = await CustodyHold.TryHoldAsync(store, _journal, cancellationToken).ConfigureAwait(false);
        if (journalReceipt is null) throw new CustodyRequiredException("The interrupted run's journal cannot be held: " + journalFailure);
        if (journalReceipt.Reference.ContentSha256 != JournalSha256 || journalReceipt.Reference.ByteLength != _journal.Length)
            throw new CustodyIntegrityException("The interrupted run's journal receipt names different bytes.");
        var record = new ResumeRecord(RecordSchema, JournalSha256, _journal.Length, LastSeq, Header.Source, Header.Arguments,
            StartedAt, LastJournaledAt, LastWireSpent, Header.WireCeiling, Header.ResumedFrom, timeProvider.GetUtcNow());
        var bytes = Encoding.UTF8.GetBytes(ContractJson.Serialize(record));
        var (receipt, failure) = await CustodyHold.TryHoldAsync(store, bytes, cancellationToken).ConfigureAwait(false);
        if (receipt is null) throw new CustodyRequiredException("The resume record cannot be held: " + failure);
        if (receipt.Reference.ContentSha256 != CustodyDigest.Of(bytes) || receipt.Reference.ByteLength != bytes.Length)
            throw new CustodyIntegrityException("The resume record receipt names different bytes.");
        ResumedAt = record.ResumedAt;
        Record = new SourceArtifactRef($"urn:uuid:{Guid.NewGuid():D}", receipt.Reference.ContentSha256);
        return Record;
    }

    /// <summary>
    /// The renderer sources the interrupted run opened, by file, after checking that each file under
    /// <paramref name="checkoutRoot"/> still holds the bytes it held. The resumed run reopens these exact references from
    /// custody: opening the checkout again would mint fresh identities that every replayed unit's binding would refuse.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, SourceArtifactRef>> VerifyRenderersAsync(string checkoutRoot,
        IReadOnlyList<string> expectedFiles, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(checkoutRoot);
        ArgumentNullException.ThrowIfNull(expectedFiles);
        if (!Header.Renderers.Select(static renderer => renderer.File).Order(StringComparer.Ordinal)
                .SequenceEqual(expectedFiles.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            throw new CustodyIntegrityException("The interrupted run opened other renderer sources than this invocation opens.");
        var references = new Dictionary<string, SourceArtifactRef>(StringComparer.Ordinal);
        foreach (var renderer in Header.Renderers)
        {
            var path = Path.Combine(checkoutRoot, renderer.File);
            if (!File.Exists(path))
                throw new CustodyIntegrityException($"Renderer source file '{renderer.File}' is not under the checkout root.");
            var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(CustodyDigest.Of(bytes), renderer.Reference.Sha256, StringComparison.Ordinal))
                throw new CustodyIntegrityException($"The checkout's '{renderer.File}' differs from the renderer source the interrupted run held.");
            references.Add(renderer.File, renderer.Reference);
        }

        return references;
    }

    /// <summary>Takes the journaled unit (phase, key), once: the caller admits it through its own checked reader.</summary>
    internal bool TryTake(string phase, string key, out JsonElement payload)
    {
        if (_entries.Remove((phase, key), out var entry))
        {
            payload = entry.Payload;
            return true;
        }

        payload = default;
        return false;
    }

    internal bool Contains(string phase, string key) => _entries.ContainsKey((phase, key));

    /// <summary>How many units of <paramref name="phase"/> are still untaken.</summary>
    internal int Count(string phase) => _entries.Keys.Count(entry => entry.Phase == phase);

    /// <summary>
    /// Takes every remaining unit of <paramref name="phase"/> at once: a journaled catalog that covers them (the EU
    /// catalog covers the adapter run and Formex units journaled before it) is admitted through its own reopen instead.
    /// </summary>
    internal void Settle(string phase)
    {
        foreach (var entry in _entries.Keys.Where(entry => entry.Phase == phase).ToArray())
            _entries.Remove(entry);
    }

    /// <summary>
    /// Refuses when a unit of any of <paramref name="phases"/> was never taken: a journal naming a unit the walk never
    /// reaches (an expression outside the run, a package nobody acquires) does not describe this acquisition.
    /// </summary>
    internal void RequireTaken(params string[] phases)
    {
        var left = _entries.Where(entry => phases.Contains(entry.Key.Phase, StringComparer.Ordinal))
            .OrderBy(static entry => entry.Value.Seq).Select(static entry => entry.Key).ToArray();
        if (left.Length != 0)
            throw new CustodyIntegrityException($"The journal names {left.Length} unit(s) this acquisition never reaches, "
                + $"the first being {left[0].Phase} {left[0].Key}.");
    }

    /// <summary>What a catalog this resumed run retains says about the resumption, for the half it describes.</summary>
    internal AcquisitionResumption Summarize(AcquisitionResumedPhase[] phases, int wireSpent, DateTimeOffset catalogedAt) =>
        new(Record ?? throw new InvalidOperationException("A resumed run holds its resume record before its first request."),
            JournalSha256, LastSeq, StartedAt, LastJournaledAt, LastWireSpent, Header.WireCeiling, ResumedAt,
            phases, wireSpent, catalogedAt);

    /// <summary>
    /// Holds a catalog's resumption to the resume record it names: the record must be canonical, state the same journal,
    /// window, spend and resume time, and the journal it names must be held.
    /// </summary>
    internal static async Task VerifyAsync(ICustodyStore store, AcquisitionResumption resumption, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(resumption);
        if (resumption.Record is null || resumption.Phases is null || resumption.WireSpent < 0 ||
            resumption.Phases.Any(static phase => phase is null || string.IsNullOrEmpty(phase.Phase) || phase.Replayed < 0 || phase.Live < 0))
            throw new CustodyIntegrityException("A catalog's resumption is not complete.");
        var bytes = await CustodyRestore.ReadByDigestCheckedAsync(store, resumption.Record.Sha256, cancellationToken).ConfigureAwait(false);
        ResumeRecord record;
        try
        {
            record = ContractJson.Deserialize<ResumeRecord>(new UTF8Encoding(false, true).GetString(bytes.Span));
            if (record.Schema != RecordSchema || !bytes.Span.SequenceEqual(Encoding.UTF8.GetBytes(ContractJson.Serialize(record))))
                throw new CustodyIntegrityException("The resume record is not in its canonical form.");
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException or DecoderFallbackException)
        {
            throw new CustodyIntegrityException("The resume record does not read.", exception);
        }

        if (record.Journal != resumption.JournalSha256 || record.LastSeq != resumption.LastSeq ||
            record.PreviousStartedAt != resumption.PreviousStartedAt || record.PreviousLastJournaledAt != resumption.PreviousLastJournaledAt ||
            record.PreviousWireSpentAtLeast != resumption.PreviousWireSpentAtLeast || record.PreviousWireCeiling != resumption.PreviousWireCeiling ||
            record.ResumedAt != resumption.ResumedAt)
            throw new CustodyIntegrityException("A catalog's resumption is not what the resume record it names states.");
        var journal = await CustodyRestore.ReadByDigestCheckedAsync(store, record.Journal, cancellationToken).ConfigureAwait(false);
        if (journal.Length != record.JournalLength)
            throw new CustodyIntegrityException("The resumed journal is held at another length than its record states.");
    }

    private static AcquisitionJournalHeader ReadHeader(AcquisitionJournal.Line line)
    {
        if (line.Phase != AcquisitionJournal.HeaderPhase || line.Key != AcquisitionJournal.Schema)
            throw new CustodyIntegrityException("The journal's first line is not a " + AcquisitionJournal.Schema + " header.");
        AcquisitionJournalHeader header;
        try
        {
            header = ContractJson.Deserialize<AcquisitionJournalHeader>(line.Payload.GetRawText());
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException)
        {
            throw new CustodyIntegrityException("The journal header does not read.", exception);
        }

        if (string.IsNullOrWhiteSpace(header.Source) || !CustodyDigest.IsLowercaseSha256(header.Arguments) || header.WireCeiling < 2 ||
            header.Renderers is null || header.Renderers.Any(static renderer => renderer is null || renderer.Reference is null ||
                string.IsNullOrEmpty(renderer.File)) ||
            header.Renderers.Select(static renderer => renderer.File).Distinct(StringComparer.Ordinal).Count() != header.Renderers.Length ||
            !line.Held.SequenceEqual(header.Renderers.Select(static renderer => renderer.Reference.Sha256), StringComparer.Ordinal))
            throw new CustodyIntegrityException("The journal header does not name its source, arguments, ceiling and renderer sources.");
        return header;
    }

    private sealed record Entry(long Seq, JsonElement Payload);

    private sealed record ResumeRecord(string Schema, string Journal, long JournalLength, long LastSeq, string Source,
        string Arguments, DateTimeOffset PreviousStartedAt, DateTimeOffset PreviousLastJournaledAt,
        int PreviousWireSpentAtLeast, int PreviousWireCeiling, AcquisitionJournalContinuation? PreviousResumedFrom,
        DateTimeOffset ResumedAt);
}
