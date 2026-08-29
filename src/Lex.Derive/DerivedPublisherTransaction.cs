using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lex.Derive;

/// <summary>
/// Stages one complete publisher and its generation manifest, then publishes both behind one
/// durable commit journal. Callers must hold the publisher and generation locks for the entire
/// lifetime. A journal is the commit point: recovery always rolls it forward; an unjournalled
/// scratch directory is unpublished and may only be discarded.
/// </summary>
internal sealed class DerivedPublisherTransaction : IDisposable
{
    internal enum Step
    {
        CandidatePrepared,
        JournalPublished,
        PublisherSaved,
        PublisherInstalled,
        GenerationSaved,
        GenerationInstalled,
    }

    internal const string JournalSchema = "lex-derived-publisher-transaction/1";
    private const string ScratchPrefix = ".lex-derived-publish-";
    private readonly string _articlesRoot;
    private readonly string _publisher;
    private readonly string _token;
    private readonly string _scratch;
    private readonly string _publisherCandidate;
    private readonly string _generationCandidate;
    private readonly string _journalPath;
    private readonly bool _hadPublisher;
    private readonly bool _hadGeneration;
    private bool _armed;
    private bool _committed;

    internal DerivedPublisherTransaction(string articlesRoot, string publisher)
    {
        _articlesRoot = DerivationGeneration.ResolvedRoot(articlesRoot);
        _publisher = DerivationGeneration.RequirePublisherSegment(publisher);
        Directory.CreateDirectory(_articlesRoot);
        if (File.Exists(JournalPathFor(_articlesRoot)))
            throw new InvalidOperationException(
                "An interrupted derived-publisher transaction must be recovered before staging.");

        _token = Guid.NewGuid().ToString("N");
        _scratch = Path.Combine(_articlesRoot, ScratchPrefix + _token);
        _publisherCandidate = Path.Combine(_scratch, "publisher.new");
        _generationCandidate = Path.Combine(_scratch, "generation.new");
        _journalPath = JournalPathFor(_articlesRoot);
        _hadPublisher = Directory.Exists(LivePublisherPath(_articlesRoot, _publisher));
        _hadGeneration = File.Exists(LiveGenerationPath(_articlesRoot));
        Directory.CreateDirectory(_publisherCandidate);
        Directory.CreateDirectory(Path.Combine(_publisherCandidate, "works"));
    }

    internal string PublisherCandidateRoot => _publisherCandidate;

    internal void WriteGenerationCandidate(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        using var stream = new FileStream(
            _generationCandidate, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            bufferSize: 16 * 1024, FileOptions.WriteThrough);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }

    internal void Commit(Action<Step>? failpoint = null)
    {
        if (_committed) throw new InvalidOperationException("Publisher transaction is already committed.");
        if (!Directory.Exists(_publisherCandidate) || !File.Exists(_generationCandidate))
            throw new InvalidOperationException(
                "Publisher and generation candidates must both exist before publication.");
        if (!Directory.Exists(Path.Combine(_publisherCandidate, "works")))
            throw new InvalidDataException(
                "Publisher candidate must contain its complete works directory.");

        FlushCandidateFiles(_scratch);
        var generationSha = DerivationGeneration.Sha256File(_generationCandidate);
        var journal = new Journal(
            _publisher, _token, _hadPublisher, _hadGeneration, generationSha);
        failpoint?.Invoke(Step.CandidatePrepared);
        PublishJournal(_journalPath, journal);
        _armed = true;
        failpoint?.Invoke(Step.JournalPublished);
        RollForward(_articlesRoot, _journalPath, journal, failpoint);
        _committed = true;
    }

    /// <summary>Recovers a committed transaction or discards only validated pre-commit scratch.</summary>
    internal static void RecoverUnderLocks(string articlesRoot)
    {
        var root = DerivationGeneration.ResolvedRoot(articlesRoot);
        Directory.CreateDirectory(root);
        var journalPath = JournalPathFor(root);
        if (File.Exists(journalPath))
        {
            RollForward(root, journalPath, ReadJournal(journalPath), failpoint: null);
            return;
        }

        foreach (var scratch in Directory.EnumerateDirectories(
                     root, ScratchPrefix + "*", SearchOption.TopDirectoryOnly))
        {
            var token = Path.GetFileName(scratch)[ScratchPrefix.Length..];
            RequireToken(token, "uncommitted scratch token");
            var publisherOld = Path.Combine(scratch, "publisher.old");
            var generationOld = Path.Combine(scratch, "generation.old");
            if (Directory.Exists(publisherOld) || File.Exists(generationOld))
                throw new InvalidDataException(
                    "A derived-publisher backup exists without its commit journal.");
            DeleteScratch(scratch);
        }

        var parent = Directory.GetParent(root)?.FullName
            ?? throw new InvalidDataException("Articles root must have a parent directory.");
        var temporaryPrefix = Path.GetFileName(journalPath) + ".";
        foreach (var temporary in Directory.EnumerateFiles(
                     parent, temporaryPrefix + "*.tmp", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(temporary);
            var token = name[temporaryPrefix.Length..^4];
            RequireToken(token, "temporary journal token");
            File.Delete(temporary);
        }
    }

    internal static void RequireNoCommittedTransaction(string articlesRoot)
    {
        var journal = JournalPathFor(articlesRoot);
        if (File.Exists(journal))
            throw new InvalidDataException(
                "A derived-publisher transaction is in progress; recover it before indexing.");
    }

    internal static string JournalPathFor(string articlesRoot)
    {
        var root = DerivationGeneration.ResolvedRoot(articlesRoot);
        var parent = Directory.GetParent(root)?.FullName
            ?? throw new InvalidDataException("Articles root must have a parent directory.");
        var identity = OperatingSystem.IsWindows() ? root.ToUpperInvariant() : root;
        var digest = Convert.ToHexStringLower(SHA256.HashData(
            Encoding.UTF8.GetBytes(identity)));
        return Path.Combine(parent, $".lex-derived-publish-{digest}.json");
    }

    private static void RollForward(
        string root,
        string journalPath,
        Journal journal,
        Action<Step>? failpoint)
    {
        var scratch = Path.Combine(root, ScratchPrefix + journal.Token);
        var publisherNew = Path.Combine(scratch, "publisher.new");
        var publisherOld = Path.Combine(scratch, "publisher.old");
        var generationNew = Path.Combine(scratch, "generation.new");
        var generationOld = Path.Combine(scratch, "generation.old");
        var publisherLive = LivePublisherPath(root, journal.Publisher);
        var generationLive = LiveGenerationPath(root);

        InstallDirectory(
            publisherLive, publisherNew, publisherOld, journal.HadPublisher,
            "publisher", Step.PublisherSaved, Step.PublisherInstalled, failpoint);
        InstallFile(
            generationLive, generationNew, generationOld, journal.HadGeneration,
            "generation", Step.GenerationSaved, Step.GenerationInstalled, failpoint);

        if (!Directory.Exists(publisherLive))
            throw new InvalidDataException(
                "Committed derived-publisher transaction has no live publisher output.");
        if (!File.Exists(generationLive)
            || !string.Equals(
                DerivationGeneration.Sha256File(generationLive),
                journal.GenerationSha256,
                StringComparison.Ordinal))
            throw new InvalidDataException(
                "Committed derived-publisher transaction has the wrong generation candidate.");

        if (Directory.Exists(publisherOld))
            DeleteScratch(publisherOld);
        if (File.Exists(generationOld)) File.Delete(generationOld);
        if (Directory.Exists(publisherNew) || File.Exists(generationNew))
            throw new InvalidDataException(
                "Committed derived-publisher transaction retained an ambiguous new candidate.");
        if (Directory.Exists(scratch)) DeleteScratch(scratch);
        File.Delete(journalPath);
    }

    private static void InstallDirectory(
        string live,
        string candidate,
        string backup,
        bool hadLive,
        string label,
        Step savedStep,
        Step installedStep,
        Action<Step>? failpoint)
    {
        var liveExists = Directory.Exists(live);
        var candidateExists = Directory.Exists(candidate);
        var backupExists = Directory.Exists(backup);
        if (!hadLive && backupExists)
            throw new InvalidDataException(
                $"Derived-publisher {label} backup contradicts the journal.");

        if (hadLive && !backupExists && liveExists && candidateExists)
        {
            Directory.Move(live, backup);
            liveExists = false;
            backupExists = true;
            failpoint?.Invoke(savedStep);
        }

        if (backupExists)
        {
            if (liveExists && candidateExists)
                throw new InvalidDataException(
                    $"Derived-publisher {label} transaction has live, candidate, and backup entries.");
            if (!liveExists && candidateExists)
            {
                Directory.Move(candidate, live);
                liveExists = true;
                candidateExists = false;
                failpoint?.Invoke(installedStep);
            }
            else if (!liveExists)
                throw new InvalidDataException(
                    $"Derived-publisher {label} transaction lost both live and candidate entries.");
        }
        else if (!hadLive)
        {
            if (liveExists && candidateExists)
                throw new InvalidDataException(
                    $"Derived-publisher {label} first publication is ambiguous.");
            if (!liveExists && candidateExists)
            {
                Directory.Move(candidate, live);
                liveExists = true;
                candidateExists = false;
                failpoint?.Invoke(installedStep);
            }
            else if (!liveExists)
                throw new InvalidDataException(
                    $"Derived-publisher {label} first publication lost its candidate.");
        }
        else if (!liveExists || candidateExists)
        {
            throw new InvalidDataException(
                $"Derived-publisher {label} transaction has an invalid cleanup state.");
        }
    }

    private static void InstallFile(
        string live,
        string candidate,
        string backup,
        bool hadLive,
        string label,
        Step savedStep,
        Step installedStep,
        Action<Step>? failpoint)
    {
        var liveExists = File.Exists(live);
        var candidateExists = File.Exists(candidate);
        var backupExists = File.Exists(backup);
        if (!hadLive && backupExists)
            throw new InvalidDataException(
                $"Derived-publisher {label} backup contradicts the journal.");

        if (hadLive && !backupExists && liveExists && candidateExists)
        {
            File.Move(live, backup);
            liveExists = false;
            backupExists = true;
            failpoint?.Invoke(savedStep);
        }

        if (backupExists)
        {
            if (liveExists && candidateExists)
                throw new InvalidDataException(
                    $"Derived-publisher {label} transaction has live, candidate, and backup entries.");
            if (!liveExists && candidateExists)
            {
                File.Move(candidate, live);
                liveExists = true;
                candidateExists = false;
                failpoint?.Invoke(installedStep);
            }
            else if (!liveExists)
                throw new InvalidDataException(
                    $"Derived-publisher {label} transaction lost both live and candidate entries.");
        }
        else if (!hadLive)
        {
            if (liveExists && candidateExists)
                throw new InvalidDataException(
                    $"Derived-publisher {label} first publication is ambiguous.");
            if (!liveExists && candidateExists)
            {
                File.Move(candidate, live);
                liveExists = true;
                candidateExists = false;
                failpoint?.Invoke(installedStep);
            }
            else if (!liveExists)
                throw new InvalidDataException(
                    $"Derived-publisher {label} first publication lost its candidate.");
        }
        else if (!liveExists || candidateExists)
        {
            throw new InvalidDataException(
                $"Derived-publisher {label} transaction has an invalid cleanup state.");
        }
    }

    private static void PublishJournal(string journalPath, Journal journal)
    {
        var temporary = journalPath + "." + journal.Token + ".tmp";
        var document = new JsonObject
        {
            ["schema"] = JournalSchema,
            ["publisher"] = journal.Publisher,
            ["token"] = journal.Token,
            ["had_publisher"] = journal.HadPublisher,
            ["had_generation"] = journal.HadGeneration,
            ["generation_sha256"] = journal.GenerationSha256,
        };
        var bytes = Encoding.UTF8.GetBytes(document.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }) + "\n");
        try
        {
            using (var stream = new FileStream(
                       temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                       bufferSize: 4 * 1024, FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, journalPath);
        }
        finally
        {
            if (File.Exists(temporary))
                try { File.Delete(temporary); } catch { }
        }
    }

    private static Journal ReadJournal(string path)
    {
        var info = new FileInfo(path);
        if (info.Length is <= 2 or > 4_096)
            throw new InvalidDataException("Derived-publisher journal size is invalid.");
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Derived-publisher journal must be an object.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
            if (!names.Add(property.Name))
                throw new InvalidDataException(
                    $"Derived-publisher journal duplicates '{property.Name}'.");
        var expected = new HashSet<string>(StringComparer.Ordinal)
        {
            "schema", "publisher", "token", "had_publisher", "had_generation",
            "generation_sha256",
        };
        if (!names.SetEquals(expected))
            throw new InvalidDataException(
                "Derived-publisher journal has an invalid property set.");
        if (root.GetProperty("schema").GetString() != JournalSchema)
            throw new InvalidDataException("Derived-publisher journal schema is invalid.");
        var publisher = DerivationGeneration.RequirePublisherSegment(
            root.GetProperty("publisher").GetString() ?? "");
        var token = RequireToken(root.GetProperty("token").GetString() ?? "", "journal token");
        var generationSha = root.GetProperty("generation_sha256").GetString() ?? "";
        if (generationSha.Length != 64
            || generationSha.Any(character => character is not (>= '0' and <= '9')
                && character is not (>= 'a' and <= 'f')))
            throw new InvalidDataException(
                "Derived-publisher journal generation digest is invalid.");
        if (root.GetProperty("had_publisher").ValueKind is not (
                JsonValueKind.True or JsonValueKind.False)
            || root.GetProperty("had_generation").ValueKind is not (
                JsonValueKind.True or JsonValueKind.False))
            throw new InvalidDataException(
                "Derived-publisher journal state flags must be booleans.");
        return new Journal(
            publisher, token,
            root.GetProperty("had_publisher").GetBoolean(),
            root.GetProperty("had_generation").GetBoolean(),
            generationSha);
    }

    private static string RequireToken(string token, string label)
    {
        if (token.Length != 32
            || token.Any(character => character is not (>= '0' and <= '9')
                && character is not (>= 'a' and <= 'f')))
            throw new InvalidDataException($"Derived-publisher {label} is invalid.");
        return token;
    }

    private static string LivePublisherPath(string root, string publisher) =>
        Path.Combine(root, publisher);

    private static string LiveGenerationPath(string root) =>
        Path.Combine(root, DerivationGeneration.FileName);

    private static void FlushCandidateFiles(string root)
    {
        EnsureNoReparsePoints(root);
        foreach (var file in Directory.EnumerateFiles(
                     root, "*", SearchOption.AllDirectories))
        {
            using var stream = new FileStream(
                file, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 1, FileOptions.SequentialScan);
            stream.Flush(flushToDisk: true);
        }
    }

    private static void DeleteScratch(string path)
    {
        EnsureNoReparsePoints(path);
        Directory.Delete(path, recursive: true);
    }

    private static void EnsureNoReparsePoints(string root)
    {
        if (!Directory.Exists(root)) return;
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException(
                "Derived-publisher scratch must not contain filesystem links.");
        foreach (var entry in Directory.EnumerateFileSystemEntries(
                     root, "*", SearchOption.AllDirectories))
            if ((File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException(
                    "Derived-publisher scratch must not contain filesystem links.");
    }

    public void Dispose()
    {
        if (_committed || _armed || !Directory.Exists(_scratch)) return;
        try { DeleteScratch(_scratch); } catch { }
    }

    private sealed record Journal(
        string Publisher,
        string Token,
        bool HadPublisher,
        bool HadGeneration,
        string GenerationSha256);
}
