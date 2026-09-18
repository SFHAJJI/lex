using System.Text.Json;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Platform;
using Lex.V3.Ingest;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Api;

internal sealed class V3CorpusMount : IDisposable
{
    public const string IndexFileName = "luxembourg-index.sqlite3";
    public const string CapabilityManifestFileName = "luxembourg-capability-manifest.json";
    public const string CorpusFileName = "lex-corpus-6.json";
    private const int MaximumCapabilityManifestBytes = 4 * 1024 * 1024;

    private readonly LuxembourgIndexReader _reader;
    private readonly VerifiedLexCorpus6ManifestSet _corpus;

    private V3CorpusMount(
        LuxembourgIndexReader reader,
        VerifiedLexCorpus6ManifestSet corpus)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _corpus = corpus ?? throw new ArgumentNullException(nameof(corpus));
    }

    public static async Task<V3CorpusMount?> OpenAsync(
        string directory,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var indexPath = Path.Combine(directory, IndexFileName);
        var capabilityPath = Path.Combine(directory, CapabilityManifestFileName);
        var corpusPath = Path.Combine(directory, CorpusFileName);
        var hasIndex = File.Exists(indexPath);
        var hasCapability = File.Exists(capabilityPath);
        var hasCorpus = File.Exists(corpusPath);
        if (!hasIndex && !hasCapability && !hasCorpus)
        {
            return null;
        }

        if (!hasIndex || !hasCapability || !hasCorpus)
        {
            throw new InvalidDataException(
                "A V3 corpus mount requires the exact corpus, index and capability manifest.");
        }

        var capabilityInfo = new FileInfo(capabilityPath);
        if (capabilityInfo.Length is <= 0 or > MaximumCapabilityManifestBytes)
        {
            throw new InvalidDataException("The V3 capability manifest has an invalid byte length.");
        }

        var capabilityBytes = await File.ReadAllBytesAsync(capabilityPath, cancellationToken)
            .ConfigureAwait(false);
        if (capabilityBytes.Length != capabilityInfo.Length)
        {
            throw new InvalidDataException("The V3 capability manifest changed while it was read.");
        }

        var corpusBytes = await File.ReadAllBytesAsync(corpusPath, cancellationToken)
            .ConfigureAwait(false);
        var corpus = VerifiedLexCorpus6ManifestSet.ParseCanonicalAndVerify(corpusBytes);
        var reader = await LuxembourgIndexReader.OpenAndVerifyFileAsync(
                indexPath,
                capabilityBytes,
                cancellationToken)
            .ConfigureAwait(false);
        if (reader.CorpusRef != corpus.ArtifactRef)
        {
            reader.Dispose();
            throw new InvalidDataException("The mounted index does not bind the mounted corpus/6 artifact.");
        }

        return new V3CorpusMount(reader, corpus);
    }

    public V3PlatformOperationOutcome Resolve(
        V3PlatformOperationRequest request,
        DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(request.OperationId, "resolve", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The mounted corpus resolver only accepts resolve/1.");
        }

        var identifier = request.Parameters.TryGetProperty("identifier", out var value) &&
                         value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return Unknown(request, identifier, observedAt);
        }

        if (TryParsePinnedPermalink(identifier, out var pinned))
        {
            return ResolvePinned(request, pinned, observedAt);
        }

        var candidates = _reader.ResolveExact(identifier);
        if (candidates.Count == 0)
        {
            if (LooksLikeIdentifier(identifier))
            {
                return Unknown(request, identifier, observedAt);
            }

            var workResolution = _reader.ResolveWorkTitle(identifier);
            if (!workResolution.Available)
            {
                using var unavailable = JsonSerializer.SerializeToDocument(new
                {
                    requested_mode = "r1_work_discovery",
                    available_modes = new[] { "r0_exact_coordinate" },
                });
                return V3PlatformOperationOutcome.Refused(
                    Context("refusal", observedAt),
                    new V3PlatformOperationRefusal(
                        request, "retrieval_mode_unavailable", unavailable.RootElement));
            }

            if (workResolution.Candidates.Count == 0)
            {
                return Unknown(request, identifier, observedAt);
            }

            if (workResolution.Candidates.Count > 1)
            {
                using var ambiguous = JsonSerializer.SerializeToDocument(new
                {
                    requested_identifier = identifier,
                    candidates = workResolution.Candidates
                        .Select(static candidate => candidate.WorkIdentifier).ToArray(),
                    match_reason = workResolution.Candidates[0].MatchReason,
                });
                return V3PlatformOperationOutcome.Refused(
                    Context("refusal", observedAt),
                    new V3PlatformOperationRefusal(
                        request, "ambiguous_identifier", ambiguous.RootElement));
            }

            var work = workResolution.Candidates[0];
            using var workResult = JsonSerializer.SerializeToDocument(new
            {
                requested_identifier = identifier,
                publisher = "lu-legilux",
                work_identifier = work.WorkIdentifier,
                expressions = work.ExpressionIris,
                languages = work.Languages,
                matched_title = work.MatchedTitle,
                retrieval_lane = "r1_work_discovery",
                match_reason = work.MatchReason,
                corpus_sha256 = _corpus.ArtifactRef.Sha256,
                index_sha256 = _reader.IndexRef.Sha256,
            });
            return V3PlatformOperationOutcome.Success(
                Context("success", observedAt),
                new V3PlatformOperationResult(request, "work_resolution", workResult.RootElement));
        }

        if (candidates.Count > 1)
        {
            using var helpful = JsonSerializer.SerializeToDocument(new
            {
                requested_identifier = identifier,
                candidates = candidates.Select(static candidate => candidate.ExpressionIri).ToArray(),
            });
            return V3PlatformOperationOutcome.Refused(
                Context("refusal", observedAt),
                new V3PlatformOperationRefusal(request, "ambiguous_identifier", helpful.RootElement));
        }

        var resolved = candidates[0];
        using var result = JsonSerializer.SerializeToDocument(new
        {
            requested_identifier = identifier,
            publisher = "lu-legilux",
            expression_iri = resolved.ExpressionIri,
            publisher_wid = resolved.PublisherWid,
            language = resolved.Language,
            article_identities = resolved.ArticleIdentities,
            corpus_sha256 = _corpus.ArtifactRef.Sha256,
            index_sha256 = _reader.IndexRef.Sha256,
        });
        return V3PlatformOperationOutcome.Success(
            Context("success", observedAt),
            new V3PlatformOperationResult(request, "work_resolution", result.RootElement));
    }

    public void Dispose() => _reader.Dispose();

    private V3PlatformOperationOutcome ResolvePinned(
        V3PlatformOperationRequest request,
        PinnedPermalink pinned,
        DateTimeOffset observedAt)
    {
        var candidates = _reader.ResolveState(pinned.WorkKey, pinned.ApplicabilityDate);
        if (candidates.Count == 0)
        {
            return Unknown(request, pinned.Original, observedAt);
        }

        if (candidates.Count > 1)
        {
            using var ambiguous = JsonSerializer.SerializeToDocument(new
            {
                requested_identifier = pinned.Original,
                candidates = candidates.Select(StateUrl).ToArray(),
            });
            return V3PlatformOperationOutcome.Refused(
                Context("refusal", observedAt),
                new V3PlatformOperationRefusal(request, "ambiguous_identifier", ambiguous.RootElement));
        }

        var current = candidates[0];
        var stableCoordinate = StableCoordinate(current);
        var currentUrl = StateUrl(current);
        if (!string.Equals(pinned.RequestedDigest, current.StateSha256, StringComparison.Ordinal))
        {
            using var mismatch = JsonSerializer.SerializeToDocument(new
            {
                requested_digest = pinned.RequestedDigest,
                current_digest = current.StateSha256,
                stable_coordinate = stableCoordinate,
                current_hash_pinned_url = currentUrl,
                reason = "expression_state_identity_changed",
                rule_profile_sha256s = current.RuleProfileSha256s,
            });
            return V3PlatformOperationOutcome.Refused(
                Context("refusal", observedAt),
                new V3PlatformOperationRefusal(request, "pinned_digest_mismatch", mismatch.RootElement));
        }

        using var result = JsonSerializer.SerializeToDocument(new
        {
            requested_identifier = pinned.Original,
            publisher = "lu-legilux",
            work_key = current.WorkKey,
            applicability_date = current.ApplicabilityDate,
            state_sha256 = current.StateSha256,
            expression_iri = current.ExpressionIri,
            publisher_work_iri = current.PublisherWorkIri,
            publisher_legal_resource_iri = current.PublisherLegalResourceIri,
            language = current.Language,
            article_identities = current.ArticleIdentities,
            stable_coordinate = stableCoordinate,
            permalink = currentUrl,
            corpus_sha256 = _corpus.ArtifactRef.Sha256,
            index_sha256 = _reader.IndexRef.Sha256,
        });
        return V3PlatformOperationOutcome.Success(
            Context("success", observedAt),
            new V3PlatformOperationResult(request, "work_resolution", result.RootElement));
    }

    private static string StableCoordinate(LuxembourgIndexResolvedState state) =>
        $"/lu-legilux/{state.WorkKey}/{state.ApplicabilityDate}";

    private static string StateUrl(LuxembourgIndexResolvedState state) =>
        StableCoordinate(state) + "--" + state.StateSha256;

    private static bool TryParsePinnedPermalink(string value, out PinnedPermalink pinned)
    {
        pinned = default;
        string path;
        if (value.StartsWith("/", StringComparison.Ordinal))
        {
            path = value;
        }
        else if (Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
                 string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal) &&
                 string.Equals(uri.Host, "law.soufien.lu", StringComparison.Ordinal) &&
                 uri.IsDefaultPort && uri.UserInfo.Length == 0 && uri.Query.Length == 0 &&
                 uri.Fragment.Length == 0)
        {
            path = uri.AbsolutePath;
        }
        else
        {
            return false;
        }

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != 3 ||
            !string.Equals(segments[0], "lu-legilux", StringComparison.Ordinal) ||
            !IsWorkKey(segments[1]) || segments[2].Length != 10 + 2 + 64 ||
            !string.Equals(segments[2].Substring(10, 2), "--", StringComparison.Ordinal) ||
            !DateOnly.TryParseExact(
                segments[2][..10], "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out _) ||
            !segments[2][12..].All(static character =>
                character is >= '0' and <= '9' or >= 'a' and <= 'f'))
        {
            return false;
        }

        pinned = new PinnedPermalink(value, segments[1], segments[2][..10], segments[2][12..]);
        return true;
    }

    private static bool IsWorkKey(string value) => value.Length is > 0 and <= 512 &&
        value.All(static character =>
            character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_');

    private readonly record struct PinnedPermalink(
        string Original,
        string WorkKey,
        string ApplicabilityDate,
        string RequestedDigest);

    private static bool LooksLikeIdentifier(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out _) ||
        value.StartsWith("eli/", StringComparison.OrdinalIgnoreCase) ||
        value.Length == 64 && value.All(static character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private V3PlatformOperationOutcome Unknown(
        V3PlatformOperationRequest request,
        string identifier,
        DateTimeOffset observedAt)
    {
        using var helpful = JsonSerializer.SerializeToDocument(new
        {
            requested_identifier = identifier,
            official_search_actions = new[] { "search" },
            what_would_answer = "an exact identifier present in the mounted corpus",
        });
        return V3PlatformOperationOutcome.Refused(
            Context("refusal", observedAt),
            new V3PlatformOperationRefusal(request, "identifier_unknown", helpful.RootElement));
    }

    private V3EnvelopeContext Context(string status, DateTimeOffset observedAt) => new(
        PublisherId.LuLegilux,
        status,
        TimelineSemantics.PublisherApplicability,
        new V3SnapshotReference(
            "corpus-" + _corpus.ArtifactRef.Sha256[..16],
            _corpus.ArtifactRef.Sha256),
        "lu",
        false,
        new V3Freshness(observedAt, "stale"));
}
