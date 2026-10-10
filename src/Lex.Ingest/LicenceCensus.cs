using System.Net.Http.Headers;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lex.Temporal;

namespace Lex.Ingest;

public sealed record LicenceCensusAcquisitionOptions(
    int PageSize = 5_000,
    int MaximumRows = 50_000,
    TimeSpan? Pause = null,
    int SparqlMaximumBytes = 128 * 1024 * 1024,
    int FileMaximumBytes = 32 * 1024 * 1024);

public sealed record LuLicenceRawObservation(
    string CoordinateSha256,
    string Channel,
    string ObjectRelativePath,
    ulong Length,
    string Sha256,
    string HttpEvidenceSha256,
    DateTimeOffset FetchedAt);

public sealed class LuLicenceObservationRunReceipt
{
    internal LuLicenceObservationRunReceipt(
        string runIdentity,
        string codeCommit,
        DateTimeOffset startedAt,
        DateTimeOffset endedAt,
        IReadOnlyList<LuLicenceRawObservation> observations,
        byte[] manifestBytes)
    {
        RunIdentity = runIdentity;
        CodeCommit = codeCommit;
        StartedAt = startedAt;
        EndedAt = endedAt;
        Observations = observations;
        CoordinateCount = observations.Select(item => item.CoordinateSha256)
            .Distinct(StringComparer.Ordinal).Count();
        ObservationCount = observations.Count;
        ManifestBytes = manifestBytes;
        ManifestSha256 = LicenceCensusValidation.Sha256(manifestBytes);
    }

    public string RunIdentity { get; }
    public string CodeCommit { get; }
    public DateTimeOffset StartedAt { get; }
    public DateTimeOffset EndedAt { get; }
    public int CoordinateCount { get; }
    public int ObservationCount { get; }
    public IReadOnlyList<LuLicenceRawObservation> Observations { get; }
    public byte[] ManifestBytes { get; }
    public string ManifestSha256 { get; }
}

/// <summary>
/// Private support tooling only. It retains fresh Legilux responses before parsing and
/// produces no corpus, article, alias, licence-admission, or public-build artifact.
/// </summary>
public static class LicenceCensus
{
    public const string ObservationManifestFileName = "observation-run.json";
    private const string ObservationManifestSchema =
        "lex-private-lu-dual-channel-observation-run/1";
    private const string Publisher = "lu-legilux";
    private const string Endpoint =
        "https://data.legilux.public.lu/sparqlendpoint";
    private const string XmlFormat =
        "http://data.legilux.public.lu/resource/authority/user-format/xml";
    private const string UserAgent =
        "Lex/0.1 (+https://github.com/SFHAJJI/lex)";

    public static async Task<LicenceCensusGitSnapshot> ReadGitInventoryAsync(
        string repositoryPath,
        string repositoryIdentity,
        string commit,
        DateTimeOffset cutoffUtc,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(repositoryPath)
            || !Path.IsPathFullyQualified(repositoryPath)
            || !Directory.Exists(repositoryPath))
            throw new InvalidDataException(
                "The census Git repository path must be an existing absolute directory.");
        var repository = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(repositoryPath));
        commit = CodeIdentity.RequireFullCommit(commit, nameof(commit));
        cutoffUtc = LicenceCensusValidation.RequireUtc(cutoffUtc, nameof(cutoffUtc));
        var resolved = (await RunGitTextAsync(
                repository,
                ["rev-parse", "--verify", commit + "^{commit}"],
                1_024,
                cancellationToken).ConfigureAwait(false)).TrimEnd('\r', '\n');
        if (!string.Equals(resolved, commit, StringComparison.Ordinal))
            throw new InvalidDataException(
                "The census commit did not resolve to its exact supplied identity.");
        var tree = (await RunGitTextAsync(
                repository,
                ["rev-parse", commit + "^{tree}"],
                1_024,
                cancellationToken).ConfigureAwait(false)).TrimEnd('\r', '\n');
        var worksTree = (await RunGitTextAsync(
                repository,
                ["rev-parse", commit + ":works"],
                1_024,
                cancellationToken).ConfigureAwait(false)).TrimEnd('\r', '\n');
        _ = CodeIdentity.RequireFullGitObjectId(tree, "census commit tree");
        _ = CodeIdentity.RequireFullGitObjectId(worksTree, "census works subtree");

        var listingBytes = await RunGitBytesAsync(
            repository,
            ["ls-tree", "-r", "-z", "--full-tree", commit],
            64 * 1024 * 1024,
            cancellationToken).ConfigureAwait(false);
        var listing = new UTF8Encoding(false, true).GetString(listingBytes);
        var entries = new List<GitBlobEntry>();
        foreach (var line in listing.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var tab = line.IndexOf('\t');
            if (tab <= 0)
                throw new InvalidDataException(
                    "The detached Git tree listing is malformed.");
            var header = line[..tab].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (header.Length != 3)
                throw new InvalidDataException(
                    "The detached Git tree entry header is malformed.");
            var path = LicenceCensusValidation.RequireRelativePath(
                line[(tab + 1)..], "detached Git path");
            var regular = header[0] is "100644" or "100755";
            if (!regular
                || header[1] != "blob"
                || !path.EndsWith(".xml", StringComparison.Ordinal))
                continue;
            if (header[2].Length != 40
                || header[2].Any(character => character is not (>= '0' and <= '9')
                    and not (>= 'a' and <= 'f')))
                throw new InvalidDataException(
                    "A census Git blob has an invalid object identity.");
            entries.Add(new GitBlobEntry(path, header[2]));
        }
        var ordered = entries.OrderBy(entry => entry.Path, StringComparer.Ordinal).ToArray();
        if (ordered.Select(entry => entry.Path).Distinct(StringComparer.Ordinal).Count()
            != ordered.Length)
            throw new InvalidDataException(
                "The detached Git census contains duplicate exact paths.");
        var hashes = await HashGitBlobsAsync(
            repository,
            ordered.Select(entry => entry.ObjectId).Distinct(StringComparer.Ordinal).ToArray(),
            cancellationToken).ConfigureAwait(false);
        var inventory = ordered.Select(entry => new LicenceCensusBlob(
            entry.Path, hashes[entry.ObjectId])).ToArray();
        return new LicenceCensusGitSnapshot(
            new LicenceCensusCorpus(
                repositoryIdentity, commit, tree, worksTree, cutoffUtc),
            Array.AsReadOnly(inventory));
    }

    public static async Task<LuLicenceObservationRunReceipt>
        AcquireLuObservationsAsync(
            string privateRoot,
            string runIdentity,
            string codeCommit,
            DateTimeOffset startedAt,
            HttpClient http,
            LicenceCensusAcquisitionOptions? options = null,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(http);
        options ??= new LicenceCensusAcquisitionOptions();
        ValidateOptions(options);
        runIdentity = IngestRunIdentity.Require(
            runIdentity, "Licence census run identity");
        codeCommit = CodeIdentity.RequireFullCommit(codeCommit, nameof(codeCommit));
        startedAt = LicenceCensusValidation.RequireUtc(startedAt, nameof(startedAt));
        var root = RequirePrivateEmptyRoot(privateRoot);

        using var owner = EvidenceFiles.CreateOwnerLock(root);
        using var handle = HandleBoundRename.OpenRoot(root);
        EvidenceFiles.RequireRootIdentity(root, handle, "Licence census private root");
        handle.EnsureDirectory("pending");
        handle.EnsureDirectory("sha256");
        handle.EnsureDirectory("http");
        handle.FlushDirectory(".");

        var pace = options.Pause ?? TimeSpan.FromMilliseconds(1_500);
        var pages = new List<RawCapture>();
        var tuples = new Dictionary<string, MutableTuple>(StringComparer.Ordinal);
        var rowCount = 0;
        var offset = 0;
        var captureOrdinal = 0;
        var lastSparqlRequest = DateTimeOffset.MinValue;

        async Task<RawCapture> SendSparqlAsync(string query, string queryKind)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var now = DateTimeOffset.UtcNow;
            var since = now - lastSparqlRequest;
            if (lastSparqlRequest != DateTimeOffset.MinValue
                && since < pace)
                await Task.Delay(pace - since, cancellationToken)
                    .ConfigureAwait(false);
            using var form = new FormUrlEncodedContent(
                [new KeyValuePair<string, string>("query", query)]);
            var requestBody = await form.ReadAsByteArrayAsync(cancellationToken)
                .ConfigureAwait(false);
            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
            {
                Content = new ByteArrayContent(requestBody),
            };
            request.Content.Headers.ContentType = new MediaTypeHeaderValue(
                "application/x-www-form-urlencoded");
            request.Headers.Accept.ParseAdd("application/sparql-results+json");
            request.Headers.UserAgent.ParseAdd(UserAgent);
            lastSparqlRequest = DateTimeOffset.UtcNow;
            using var response = await http.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            var capture = await CaptureResponseAsync(
                handle,
                response,
                captureOrdinal++,
                queryKind,
                Endpoint,
                LicenceCensusValidation.Sha256(requestBody),
                options.SparqlMaximumBytes,
                startedAt,
                cancellationToken).ConfigureAwait(false);
            pages.Add(capture);
            RequireSuccessfulExactResponse(
                capture, Endpoint, "application/sparql-results+json");
            return capture;
        }

        var countBeforeCapture = await SendSparqlAsync(
            CountQuery(), "count_before").ConfigureAwait(false);
        var countBefore = ParseCount(ReadRaw(handle, countBeforeCapture));
        if (countBefore is 0 || countBefore > checked((ulong)options.MaximumRows))
            throw new InvalidDataException(
                "The fresh Legilux manifestation count is zero or exceeds its row bound.");
        while (checked((ulong)rowCount) < countBefore)
        {
            var capture = await SendSparqlAsync(
                EnumerationQuery(options.PageSize, offset), "data")
                .ConfigureAwait(false);
            var rows = ParseEnumerationPage(ReadRaw(handle, capture));
            var remaining = countBefore - checked((ulong)rowCount);
            var expected = checked((int)Math.Min(
                checked((ulong)options.PageSize), remaining));
            if (rows.Count != expected
                || rowCount > options.MaximumRows - rows.Count)
                throw new InvalidDataException(
                    "The fresh Legilux manifestation data is partial or exceeds its row bound.");
            rowCount += rows.Count;
            foreach (var row in rows)
            {
                if (!tuples.TryGetValue(row.Manifestation, out var current))
                    tuples.Add(row.Manifestation, new MutableTuple(row, capture));
                else
                    current.Add(row, capture);
            }
            offset = checked(offset + rows.Count);
        }
        var countAfterCapture = await SendSparqlAsync(
            CountQuery(), "count_after").ConfigureAwait(false);
        var countAfter = ParseCount(ReadRaw(handle, countAfterCapture));
        if (countAfter != countBefore || countAfter != checked((ulong)rowCount))
            throw new InvalidDataException(
                "The fresh Legilux count-data-count observations drifted in one run.");

        var observations = new List<LuLicenceRawObservation>(
            checked(tuples.Count * 2));
        var ordered = tuples.Values.OrderBy(
            tuple => tuple.Manifestation, StringComparer.Ordinal).ToArray();
        var lastFileRequest = DateTimeOffset.MinValue;
        foreach (var tuple in ordered)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var now = DateTimeOffset.UtcNow;
            var since = now - lastFileRequest;
            if (lastFileRequest != DateTimeOffset.MinValue
                && since < pace)
                await Task.Delay(pace - since, cancellationToken)
                    .ConfigureAwait(false);

            using var request = new HttpRequestMessage(HttpMethod.Get, tuple.FetchUri);
            request.Headers.Accept.ParseAdd("application/xml");
            request.Headers.UserAgent.ParseAdd(UserAgent);
            lastFileRequest = DateTimeOffset.UtcNow;
            using var response = await http.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            var file = await CaptureResponseAsync(
                handle,
                response,
                captureOrdinal++,
                "file",
                tuple.FetchUri,
                requestBodySha256: null,
                options.FileMaximumBytes,
                startedAt,
                cancellationToken).ConfigureAwait(false);
            if (!IsSuccessfulExactResponse(file, tuple.FetchUri, "application/xml"))
                throw new InvalidDataException(
                    "Every manifestation requires complete same-run SPARQL and file observations; a file response was partial or invalid.");

            var coordinateSha256 = tuple.CoordinateSha256;
            var sparqlObjects = tuple.Pages
                .OrderBy(page => page.Ordinal)
                .DistinctBy(page => page.Sha256)
                .ToArray();
            var sparqlBinding = HashSparqlBinding(handle, sparqlObjects);
            observations.Add(new LuLicenceRawObservation(
                coordinateSha256,
                "sparql",
                sparqlBinding.RelativePath,
                sparqlBinding.Length,
                sparqlBinding.Sha256,
                sparqlBinding.HttpEvidenceSha256,
                sparqlBinding.FetchedAt));
            observations.Add(ToObservation(coordinateSha256, "file", file));
        }

        if (observations.Count != tuples.Count * 2
            || observations.GroupBy(item => item.CoordinateSha256,
                    StringComparer.Ordinal)
                .Any(group => group.Select(item => item.Channel)
                    .Order(StringComparer.Ordinal)
                    .SequenceEqual(["file", "sparql"]) is false))
            throw new InvalidDataException(
                "Every manifestation requires complete same-run SPARQL and file observations.");

        var endedAt = DateTimeOffset.UtcNow;
        if (endedAt.Offset != TimeSpan.Zero || endedAt < startedAt)
            throw new InvalidDataException(
                "Licence census observation timestamps are stale or invalid.");
        var manifest = SerializeObservationManifest(
            runIdentity, codeCommit, startedAt, endedAt, pages, ordered, observations);
        EvidenceFiles.WriteAtomic(handle, ObservationManifestFileName, manifest);
        handle.FlushFile(ObservationManifestFileName);
        EvidenceFiles.RequireRootIdentity(root, handle, "Licence census private root");
        return new LuLicenceObservationRunReceipt(
            runIdentity,
            codeCommit,
            startedAt,
            endedAt,
            observations.AsReadOnly(),
            manifest);
    }

    private static void ValidateOptions(LicenceCensusAcquisitionOptions options)
    {
        if (options.PageSize is < 1 or > 10_000
            || options.MaximumRows < options.PageSize
            || options.MaximumRows > 1_000_000
            || options.Pause is { } pause && pause < TimeSpan.Zero
            || options.SparqlMaximumBytes is < 1 or > 128 * 1024 * 1024
            || options.FileMaximumBytes is < 1 or > 128 * 1024 * 1024)
            throw new ArgumentOutOfRangeException(
                nameof(options), "Licence census acquisition bounds are invalid.");
    }

    private static string RequirePrivateEmptyRoot(string path)
    {
        var root = EvidenceFiles.RequireRoot(path, "Licence census private root");
        for (var current = new DirectoryInfo(root);
             current is not null;
             current = current.Parent)
            if (Directory.Exists(Path.Combine(current.FullName, ".git"))
                || File.Exists(Path.Combine(current.FullName, ".git")))
                throw new InvalidDataException(
                    "Licence census private evidence cannot live in a Git worktree.");
        if (Directory.EnumerateFileSystemEntries(root).Any())
            throw new InvalidDataException(
                "Licence census private root must be empty and create-only.");
        return root;
    }

    private static async Task<RawCapture> CaptureResponseAsync(
        HandleBoundRoot root,
        HttpResponseMessage response,
        int ordinal,
        string channel,
        string expectedUri,
        string? requestBodySha256,
        int maximumBytes,
        DateTimeOffset runStartedAt,
        CancellationToken cancellationToken)
    {
        var pending = $"pending/{ordinal:D8}-{Guid.NewGuid():N}.raw";
        long length = 0;
        bool overflow = false;
        Exception? readFailure = null;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using (var destination = root.CreateNewFile(pending))
        {
            try
            {
                await using var source = await response.Content
                    .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                var buffer = new byte[64 * 1024];
                while (true)
                {
                    var remaining = maximumBytes + 1L - length;
                    if (remaining <= 0)
                    {
                        overflow = true;
                        break;
                    }
                    var requestLength = checked((int)Math.Min(buffer.Length, remaining));
                    var read = await source.ReadAsync(
                        buffer.AsMemory(0, requestLength), cancellationToken)
                        .ConfigureAwait(false);
                    if (read == 0) break;
                    destination.Write(buffer, 0, read);
                    hash.AppendData(buffer, 0, read);
                    length += read;
                    if (length > maximumBytes)
                    {
                        overflow = true;
                        break;
                    }
                }
            }
            catch (Exception error) when (error is IOException
                                          or HttpRequestException
                                          or TimeoutException)
            {
                readFailure = error;
            }
            destination.Flush(flushToDisk: true);
        }
        root.FlushDirectory("pending");
        var sha256 = Convert.ToHexStringLower(hash.GetHashAndReset());
        var objectDirectory = $"sha256/{sha256[..2]}";
        root.EnsureDirectory(objectDirectory);
        var objectPath = $"{objectDirectory}/{sha256}";
        if (root.EntryExists(objectPath))
        {
            EvidenceFiles.VerifyObject(root, objectPath, sha256, length);
            root.DeleteFile(pending);
        }
        else
        {
            root.Move(pending, objectPath, replace: false);
        }
        root.FlushFile(objectPath);
        root.FlushDirectory(objectDirectory);
        EvidenceFiles.VerifyObject(root, objectPath, sha256, length);

        var fetchedAt = DateTimeOffset.UtcNow;
        if (fetchedAt.Offset != TimeSpan.Zero || fetchedAt < runStartedAt)
            throw new InvalidDataException(
                "A licence observation predates its active run.");
        var effectiveUri = response.RequestMessage?.RequestUri?.AbsoluteUri
                           ?? expectedUri;
        var mediaType = response.Content.Headers.ContentType?.MediaType?
            .ToLowerInvariant();
        var charset = response.Content.Headers.ContentType?.CharSet?
            .Trim('"').ToLowerInvariant();
        var completed = !overflow && readFailure is null;
        var httpEvidence = SerializeHttpEvidence(
            (int)response.StatusCode,
            mediaType,
            charset,
            response.Headers.ETag?.ToString(),
            fetchedAt,
            effectiveUri,
            completed,
            checked((ulong)length),
            sha256);
        var httpSha256 = LicenceCensusValidation.Sha256(httpEvidence);
        EvidenceFiles.WriteAtomic(root, $"http/{ordinal:D8}-{httpSha256}.json",
            httpEvidence);
        return new RawCapture(
            ordinal,
            channel,
            objectPath,
            checked((ulong)length),
            sha256,
            httpSha256,
            fetchedAt,
            (int)response.StatusCode,
            mediaType,
            charset,
            effectiveUri,
            completed,
            requestBodySha256);
    }

    private static byte[] ReadRaw(HandleBoundRoot root, RawCapture capture)
    {
        if (capture.Length > int.MaxValue)
            throw new InvalidDataException(
                "A private observation is too large to parse in memory.");
        using var stream = root.OpenRead(capture.RelativePath);
        var bytes = new byte[checked((int)capture.Length)];
        stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1
            || LicenceCensusValidation.Sha256(bytes) != capture.Sha256)
            throw new InvalidDataException(
                "A private observation changed before parsing.");
        return bytes;
    }

    private static void RequireSuccessfulExactResponse(
        RawCapture capture, string expectedUri, string expectedMediaType)
    {
        if (!string.Equals(
                capture.EffectiveUri, expectedUri, StringComparison.Ordinal))
            throw new InvalidDataException(
                "The publisher response effective URI does not match the exact request URI; redirects are forbidden.");
        if (!IsSuccessfulExactResponse(capture, expectedUri, expectedMediaType))
            throw new InvalidDataException(
                "A fresh publisher response is incomplete, redirected, stale, or has the wrong media type.");
    }

    private static bool IsSuccessfulExactResponse(
        RawCapture capture, string expectedUri, string expectedMediaType) =>
        capture.Completed
        && capture.Status == 200
        && string.Equals(capture.EffectiveUri, expectedUri, StringComparison.Ordinal)
        && string.Equals(capture.MediaType, expectedMediaType, StringComparison.Ordinal)
        && capture.Charset is null or "utf-8";

    private static List<EnumerationRow> ParseEnumerationPage(byte[] bytes)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 16,
            });
            RejectDuplicateProperties(document.RootElement);
            var root = document.RootElement;
            var head = root.GetProperty("head");
            var variables = head.GetProperty("vars").EnumerateArray()
                .Select(item => item.GetString()).ToArray();
            if (!variables.SequenceEqual(
                    new[] { "c", "expr", "m", "fmt", "file", "license" }))
                throw new InvalidDataException(
                    "The fresh SPARQL projection is not the exact six-variable tuple.");
            var result = new List<EnumerationRow>();
            foreach (var binding in root.GetProperty("results")
                         .GetProperty("bindings").EnumerateArray())
            {
                var allowed = new HashSet<string>(
                    ["c", "expr", "m", "fmt", "file", "license"],
                    StringComparer.Ordinal);
                if (binding.ValueKind != JsonValueKind.Object
                    || binding.EnumerateObject().Any(item => !allowed.Contains(item.Name)))
                    throw new InvalidDataException(
                        "A fresh SPARQL row contains an unknown binding.");
                var consolidation = RequireUriTerm(binding, "c");
                var expression = RequireUriTerm(binding, "expr");
                var manifestation = RequireUriTerm(binding, "m");
                var format = RequireUriTerm(binding, "fmt");
                var file = RequireUriTerm(binding, "file");
                if (binding.TryGetProperty("license", out var licence))
                    _ = RequireTerm(licence);
                if (format != XmlFormat)
                    throw new InvalidDataException(
                        "The LU observation query returned a non-XML manifestation.");
                result.Add(new EnumerationRow(
                    consolidation,
                    expression,
                    manifestation,
                    format,
                    file,
                    OfficialFetchUri(file)));
            }
            return result;
        }
        catch (Exception error) when (error is JsonException
                                      or KeyNotFoundException
                                      or InvalidOperationException)
        {
            throw new InvalidDataException(
                "The fresh SPARQL observation is not strict result JSON.", error);
        }
    }

    private static ulong ParseCount(byte[] bytes)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 12,
            });
            RejectDuplicateProperties(document.RootElement);
            var root = document.RootElement;
            var variables = root.GetProperty("head").GetProperty("vars")
                .EnumerateArray().Select(item => item.GetString()).ToArray();
            if (!variables.SequenceEqual(new[] { "count" }))
                throw new InvalidDataException(
                    "The fresh SPARQL count projection is not exact.");
            var bindings = root.GetProperty("results").GetProperty("bindings")
                .EnumerateArray().ToArray();
            if (bindings.Length != 1
                || bindings[0].EnumerateObject().Select(item => item.Name)
                    .SequenceEqual(new[] { "count" }) is false)
                throw new InvalidDataException(
                    "The fresh SPARQL count result is not one exact row.");
            var term = RequireTerm(bindings[0].GetProperty("count"));
            if (term.Type is not ("literal" or "typed-literal")
                || term.Value.Length == 0
                || term.Value.Any(character => character is < '0' or > '9')
                || !ulong.TryParse(term.Value,
                    System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var count))
                throw new InvalidDataException(
                    "The fresh SPARQL count term is not uint64.");
            return count;
        }
        catch (Exception error) when (error is JsonException
                                      or KeyNotFoundException
                                      or InvalidOperationException)
        {
            throw new InvalidDataException(
                "The fresh SPARQL count is not strict result JSON.", error);
        }
    }

    private static string RequireUriTerm(JsonElement binding, string name)
    {
        if (!binding.TryGetProperty(name, out var term))
            throw new InvalidDataException(
                $"The fresh SPARQL row is missing {name}.");
        var parsed = RequireTerm(term);
        if (parsed.Type != "uri"
            || !Uri.TryCreate(parsed.Value, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || !string.Equals(uri.AbsoluteUri, parsed.Value,
                StringComparison.Ordinal))
            throw new InvalidDataException(
                $"The fresh SPARQL {name} value is not an exact URI term.");
        return parsed.Value;
    }

    private static (string Type, string Value) RequireTerm(JsonElement term)
    {
        if (term.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("A SPARQL term is not an object.");
        var allowed = new HashSet<string>(
            ["type", "value", "datatype", "xml:lang"], StringComparer.Ordinal);
        if (term.EnumerateObject().Any(item => !allowed.Contains(item.Name)))
            throw new InvalidDataException("A SPARQL term contains an unknown member.");
        var type = term.GetProperty("type").GetString();
        var value = term.GetProperty("value").GetString();
        if (string.IsNullOrEmpty(type) || value is null
            || type.Length > 32 || value.Length > 4_096)
            throw new InvalidDataException("A SPARQL term is empty or unbounded.");
        return (type, value);
    }

    private static void RejectDuplicateProperties(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw new InvalidDataException(
                        "A fresh SPARQL object contains a duplicate property.");
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
                RejectDuplicateProperties(item);
        }
    }

    // Derived from the producer contract in LegiluxAdapter.OfficialManifestationTransport
    // at protected-main b910816. Any divergence must fail closed in review.
    private static string OfficialFetchUri(string file)
    {
        if (!Uri.TryCreate(file, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || !string.Equals(uri.Host, "data.legilux.public.lu",
                StringComparison.OrdinalIgnoreCase)
            || !uri.IsDefaultPort
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || !uri.AbsolutePath.StartsWith("/filestore/", StringComparison.Ordinal)
            || !uri.AbsolutePath.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                "A fresh manifestation file is not an official Legilux XML file.");
        return "https://legilux.public.lu" + uri.AbsolutePath;
    }

    private static string EnumerationQuery(int limit, int offset) => $$"""
        PREFIX jolux: <http://data.legilux.public.lu/resource/ontology/jolux#>
        SELECT ?c ?expr ?m ?fmt ?file ?license WHERE {
          ?c a jolux:Consolidation ; jolux:isRealizedBy ?expr .
          ?expr jolux:isEmbodiedBy ?m .
          ?m jolux:userFormat ?fmt ; jolux:isExemplifiedBy ?file .
          OPTIONAL { ?m jolux:license ?license }
          VALUES ?fmt { <http://data.legilux.public.lu/resource/authority/user-format/xml> }
        } ORDER BY ?c ?expr ?m ?fmt ?file ?license LIMIT {{limit}} OFFSET {{offset}}
        """;

    private static string CountQuery() => """
        PREFIX jolux: <http://data.legilux.public.lu/resource/ontology/jolux#>
        SELECT (COUNT(*) AS ?count) WHERE {
          ?c a jolux:Consolidation ; jolux:isRealizedBy ?expr .
          ?expr jolux:isEmbodiedBy ?m .
          ?m jolux:userFormat ?fmt ; jolux:isExemplifiedBy ?file .
          OPTIONAL { ?m jolux:license ?license }
          VALUES ?fmt { <http://data.legilux.public.lu/resource/authority/user-format/xml> }
        }
        """;

    private static string CoordinateSha256(EnumerationRow row) =>
        LicenceCensusValidation.Sha256(LicenceCensusValidation.StrictUtf8(
            string.Join('\n',
                "lex-private-lu-observation-coordinate/1",
                row.Consolidation,
                row.Expression,
                row.Manifestation,
                row.Format,
                row.File)));

    private static SparqlBinding HashSparqlBinding(
        HandleBoundRoot root, RawCapture[] pages)
    {
        if (pages.Length == 1)
            return new SparqlBinding(
                pages[0].RelativePath,
                pages[0].Length,
                pages[0].Sha256,
                pages[0].HttpEvidenceSha256,
                pages[0].FetchedAt);
        using var payload = new MemoryStream();
        LicenceCensusValidation.WriteLengthPrefixed(
            payload, Encoding.ASCII.GetBytes(
                "lex-private-lu-sparql-observation-set/1"));
        foreach (var page in pages)
            LicenceCensusValidation.WriteLengthPrefixed(
                payload, Convert.FromHexString(page.Sha256));
        var setDigest = LicenceCensusValidation.Sha256(payload.ToArray());
        var writer = new CanonicalJson();
        writer.BeginObject();
        var first = true;
        writer.Property("schema", ref first);
        writer.String("lex-private-lu-sparql-observation-set/1");
        writer.Property("set_sha256", ref first); writer.String(setDigest);
        writer.Property("pages", ref first); writer.BeginArray();
        for (var index = 0; index < pages.Length; index++)
        {
            if (index > 0) writer.Comma();
            writer.BeginObject();
            var pageFirst = true;
            writer.Property("sha256", ref pageFirst); writer.String(pages[index].Sha256);
            writer.Property("http_evidence_sha256", ref pageFirst);
            writer.String(pages[index].HttpEvidenceSha256);
            writer.EndObject();
        }
        writer.EndArray();
        writer.EndObject();
        var bytes = writer.Finish();
        var sha = LicenceCensusValidation.Sha256(bytes);
        root.EnsureDirectory("bindings");
        var relative = $"bindings/{sha}.json";
        if (!root.EntryExists(relative)) EvidenceFiles.WriteAtomic(root, relative, bytes);
        else EvidenceFiles.VerifyObject(root, relative, sha, bytes.LongLength);
        return new SparqlBinding(
            relative,
            checked((ulong)bytes.LongLength),
            sha,
            LicenceCensusValidation.Sha256(LicenceCensusValidation.StrictUtf8(
                string.Join('\n', pages.Select(page => page.HttpEvidenceSha256)))),
            pages.Max(page => page.FetchedAt));
    }

    private static LuLicenceRawObservation ToObservation(
        string coordinateSha256, string channel, RawCapture capture) => new(
        coordinateSha256,
        channel,
        capture.RelativePath,
        capture.Length,
        capture.Sha256,
        capture.HttpEvidenceSha256,
        capture.FetchedAt);

    private static byte[] SerializeHttpEvidence(
        int status,
        string? mediaType,
        string? charset,
        string? etag,
        DateTimeOffset fetchedAt,
        string effectiveUri,
        bool completed,
        ulong length,
        string sha256)
    {
        if (status is < 100 or > 599
            || string.IsNullOrEmpty(mediaType)
            || mediaType.Any(character => character is < '!' or > '~'
                || char.IsUpper(character))
            || charset is not null and not "utf-8"
            || etag is not null && (Encoding.UTF8.GetByteCount(etag) > 1_024
                || etag.Any(character => character is < ' ' or > '~'))
            || !Uri.TryCreate(effectiveUri, UriKind.Absolute, out _))
            throw new InvalidDataException(
                "Publisher HTTP evidence is outside the OBS-01 closed shape.");
        _ = CodeIdentity.RequireSha256(sha256, nameof(sha256));
        var writer = new CanonicalJson();
        writer.BeginObject();
        var first = true;
        writer.Property("schema", ref first); writer.String("lex-license-http-evidence/1");
        writer.Property("status", ref first); writer.Number(checked((ulong)status));
        writer.Property("media_type", ref first); writer.String(mediaType);
        writer.Property("charset", ref first);
        if (charset is null) writer.Null(); else writer.String(charset);
        writer.Property("etag", ref first);
        if (etag is null) writer.Null(); else writer.String(etag);
        writer.Property("fetched_at", ref first); writer.Timestamp(fetchedAt);
        writer.Property("effective_uri", ref first); writer.String(effectiveUri);
        writer.Property("completed", ref first); writer.Boolean(completed);
        writer.Property("length", ref first); writer.Number(length);
        writer.Property("sha256", ref first); writer.String(sha256);
        writer.EndObject();
        return writer.Finish();
    }

    private static byte[] SerializeObservationManifest(
        string runIdentity,
        string codeCommit,
        DateTimeOffset startedAt,
        DateTimeOffset endedAt,
        IReadOnlyList<RawCapture> pages,
        IReadOnlyList<MutableTuple> tuples,
        IReadOnlyList<LuLicenceRawObservation> observations)
    {
        var byCoordinate = observations.GroupBy(
                observation => observation.CoordinateSha256,
                StringComparer.Ordinal)
            .ToDictionary(group => group.Key,
                group => group.ToDictionary(item => item.Channel,
                    StringComparer.Ordinal), StringComparer.Ordinal);
        var writer = new CanonicalJson();
        writer.BeginObject();
        var first = true;
        writer.Property("schema", ref first); writer.String(ObservationManifestSchema);
        writer.Property("run_identity", ref first); writer.String(runIdentity);
        writer.Property("code_commit", ref first); writer.String(codeCommit);
        writer.Property("publisher", ref first); writer.String(Publisher);
        writer.Property("started_at", ref first); writer.Timestamp(startedAt);
        writer.Property("ended_at", ref first); writer.Timestamp(endedAt);
        writer.Property("endpoint", ref first); writer.String(Endpoint);
        writer.Property("enumeration_pages", ref first); writer.BeginArray();
        foreach (var pair in pages.Select((page, index) => (page, index)))
        {
            if (pair.index > 0) writer.Comma();
            WriteCapture(writer, pair.page);
        }
        writer.EndArray();
        writer.Property("coordinates", ref first); writer.BeginArray();
        for (var index = 0; index < tuples.Count; index++)
        {
            if (index > 0) writer.Comma();
            var tuple = tuples[index];
            var coordinate = tuple.CoordinateSha256;
            writer.BeginObject();
            var coordinateFirst = true;
            writer.Property("coordinate_sha256", ref coordinateFirst); writer.String(coordinate);
            writer.Property("consolidation", ref coordinateFirst); writer.String(tuple.Consolidation);
            writer.Property("expression", ref coordinateFirst); writer.String(tuple.Expression);
            writer.Property("manifestation", ref coordinateFirst); writer.String(tuple.Manifestation);
            writer.Property("format", ref coordinateFirst); writer.String(tuple.Format);
            writer.Property("file", ref coordinateFirst); writer.String(tuple.File);
            writer.Property("sparql", ref coordinateFirst);
            WriteObservation(writer, byCoordinate[coordinate]["sparql"]);
            writer.Property("file_channel", ref coordinateFirst);
            WriteObservation(writer, byCoordinate[coordinate]["file"]);
            writer.EndObject();
        }
        writer.EndArray();
        writer.EndObject();
        return writer.Finish();
    }

    private static void WriteCapture(CanonicalJson writer, RawCapture capture)
    {
        writer.BeginObject();
        var first = true;
        writer.Property("ordinal", ref first); writer.Number(checked((ulong)capture.Ordinal));
        writer.Property("query_kind", ref first); writer.String(capture.Channel);
        writer.Property("object_path", ref first); writer.String(capture.RelativePath);
        writer.Property("length", ref first); writer.Number(capture.Length);
        writer.Property("sha256", ref first); writer.String(capture.Sha256);
        writer.Property("http_evidence_sha256", ref first); writer.String(capture.HttpEvidenceSha256);
        writer.Property("request_body_sha256", ref first);
        if (capture.RequestBodySha256 is null) writer.Null(); else writer.String(capture.RequestBodySha256);
        writer.Property("fetched_at", ref first); writer.Timestamp(capture.FetchedAt);
        writer.EndObject();
    }

    private static void WriteObservation(
        CanonicalJson writer, LuLicenceRawObservation observation)
    {
        writer.BeginObject();
        var first = true;
        writer.Property("object_path", ref first); writer.String(observation.ObjectRelativePath);
        writer.Property("length", ref first); writer.Number(observation.Length);
        writer.Property("sha256", ref first); writer.String(observation.Sha256);
        writer.Property("http_evidence_sha256", ref first); writer.String(observation.HttpEvidenceSha256);
        writer.Property("fetched_at", ref first); writer.Timestamp(observation.FetchedAt);
        writer.EndObject();
    }

    private static async Task<Dictionary<string, string>> HashGitBlobsAsync(
        string repository,
        IReadOnlyList<string> objectIds,
        CancellationToken cancellationToken)
    {
        var start = GitStart(repository, ["cat-file", "--batch"]);
        using var process = Process.Start(start)
            ?? throw new InvalidDataException("Could not start git cat-file.");
        var stderrTask = ReadBoundedAsync(
            process.StandardError.BaseStream, 1024 * 1024, cancellationToken);
        foreach (var objectId in objectIds)
            await process.StandardInput.WriteLineAsync(objectId.AsMemory(), cancellationToken)
                .ConfigureAwait(false);
        process.StandardInput.Close();

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var expected in objectIds)
        {
            var header = await ReadAsciiLineAsync(
                process.StandardOutput.BaseStream, 256, cancellationToken)
                .ConfigureAwait(false);
            var members = header.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (members.Length != 3
                || members[0] != expected
                || members[1] != "blob"
                || !long.TryParse(members[2],
                    System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var length)
                || length is < 0 or > 128L * 1024 * 1024)
                throw new InvalidDataException(
                    "Git cat-file returned an unexpected or oversized census object.");
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var remaining = length;
            var buffer = new byte[128 * 1024];
            while (remaining > 0)
            {
                var read = await process.StandardOutput.BaseStream.ReadAsync(
                    buffer.AsMemory(0, checked((int)Math.Min(buffer.Length, remaining))),
                    cancellationToken).ConfigureAwait(false);
                if (read == 0)
                    throw new EndOfStreamException(
                        "Git cat-file ended inside a census blob.");
                hash.AppendData(buffer, 0, read);
                remaining -= read;
            }
            if (process.StandardOutput.BaseStream.ReadByte() != '\n')
                throw new InvalidDataException(
                    "Git cat-file did not delimit a census blob exactly.");
            result.Add(expected, Convert.ToHexStringLower(hash.GetHashAndReset()));
        }
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        if (process.ExitCode != 0 || stderr.Length != 0)
            throw new InvalidDataException(
                "Git cat-file failed while hashing the detached census tree.");
        return result;
    }

    private static async Task<string> RunGitTextAsync(
        string repository,
        IReadOnlyList<string> arguments,
        int maximumBytes,
        CancellationToken cancellationToken) =>
        new UTF8Encoding(false, true).GetString(await RunGitBytesAsync(
            repository, arguments, maximumBytes, cancellationToken)
            .ConfigureAwait(false));

    private static async Task<byte[]> RunGitBytesAsync(
        string repository,
        IReadOnlyList<string> arguments,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        var start = GitStart(repository, arguments);
        using var process = Process.Start(start)
            ?? throw new InvalidDataException("Could not start Git census plumbing.");
        var stdoutTask = ReadBoundedAsync(
            process.StandardOutput.BaseStream, maximumBytes, cancellationToken);
        var stderrTask = ReadBoundedAsync(
            process.StandardError.BaseStream, 1024 * 1024, cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        if (process.ExitCode != 0)
            throw new InvalidDataException(
                "Git census plumbing failed: "
                + new UTF8Encoding(false, true).GetString(stderr));
        if (stderr.Length != 0)
            throw new InvalidDataException(
                "Git census plumbing emitted unexpected stderr.");
        return stdout;
    }

    private static ProcessStartInfo GitStart(
        string repository, IReadOnlyList<string> arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = repository,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        return start;
    }

    private static async Task<byte[]> ReadBoundedAsync(
        Stream stream, int maximumBytes, CancellationToken cancellationToken)
    {
        using var output = new MemoryStream(Math.Min(maximumBytes, 1024 * 1024));
        var buffer = new byte[64 * 1024];
        while (true)
        {
            var remaining = maximumBytes - checked((int)output.Length);
            var requested = remaining < buffer.Length ? remaining + 1 : buffer.Length;
            var read = await stream.ReadAsync(
                buffer.AsMemory(0, requested), cancellationToken).ConfigureAwait(false);
            if (read == 0) return output.ToArray();
            if (read > remaining)
                throw new InvalidDataException(
                    "A census subprocess exceeded its output bound.");
            output.Write(buffer, 0, read);
        }
    }

    private static async Task<string> ReadAsciiLineAsync(
        Stream stream, int maximumBytes, CancellationToken cancellationToken)
    {
        var bytes = new List<byte>();
        while (bytes.Count <= maximumBytes)
        {
            var one = new byte[1];
            var read = await stream.ReadAsync(one, cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
                throw new EndOfStreamException(
                    "Git cat-file ended before its object header.");
            if (one[0] == '\n')
                return Encoding.ASCII.GetString(bytes.ToArray());
            if (one[0] is < 0x20 or > 0x7e)
                throw new InvalidDataException(
                    "Git cat-file emitted a non-ASCII object header.");
            bytes.Add(one[0]);
        }
        throw new InvalidDataException("Git cat-file object header is oversized.");
    }

    private sealed record EnumerationRow(
        string Consolidation,
        string Expression,
        string Manifestation,
        string Format,
        string File,
        string FetchUri);

    private sealed class MutableTuple
    {
        private readonly List<RawCapture> _pages = [];

        public MutableTuple(EnumerationRow row, RawCapture page)
        {
            Consolidation = row.Consolidation;
            Expression = row.Expression;
            Manifestation = row.Manifestation;
            Format = row.Format;
            File = row.File;
            FetchUri = row.FetchUri;
            CoordinateSha256 = LicenceCensus.CoordinateSha256(row);
            _pages.Add(page);
        }

        public string Consolidation { get; }
        public string Expression { get; }
        public string Manifestation { get; }
        public string Format { get; }
        public string File { get; }
        public string FetchUri { get; }
        public string CoordinateSha256 { get; }
        public IReadOnlyList<RawCapture> Pages => _pages;

        public void Add(EnumerationRow row, RawCapture page)
        {
            if (row.Consolidation != Consolidation
                || row.Expression != Expression
                || row.Format != Format
                || row.File != File
                || row.FetchUri != FetchUri)
                throw new InvalidDataException(
                    "One fresh Legilux manifestation has conflicting identity or file rows.");
            _pages.Add(page);
        }
    }

    private sealed record RawCapture(
        int Ordinal,
        string Channel,
        string RelativePath,
        ulong Length,
        string Sha256,
        string HttpEvidenceSha256,
        DateTimeOffset FetchedAt,
        int Status,
        string? MediaType,
        string? Charset,
        string EffectiveUri,
        bool Completed,
        string? RequestBodySha256);

    private sealed record SparqlBinding(
        string RelativePath,
        ulong Length,
        string Sha256,
        string HttpEvidenceSha256,
        DateTimeOffset FetchedAt);

    private sealed record GitBlobEntry(string Path, string ObjectId);
}
