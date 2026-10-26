using System.Globalization;
using System.Text.Json;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Platform;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Ingest.Europe;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Api;

/// <summary>
/// What every answer says of how fresh the mounted law is and what built it (the specification of record's R87 and R23; the
/// plan's items 6.1 and 6.5), read once when the mount opens, from the mount alone: the index's build time from its log, and,
/// from the build report written beside the mount when it is bound to the mounted artifacts, when each publisher channel's
/// newest acquisition completed and the commit of the code that built the index.
/// </summary>
internal sealed partial class V3CorpusMount
{
    private const int MaximumBuildReportBytes = 1024 * 1024;

    /// <summary>
    /// The freshness and the commit every answer of this mount names: the index's build time (null when its log records none),
    /// each publisher channel it holds an index for (Luxembourg first) with its newest completed observation or null, and the
    /// commit the index was built from, or null.
    /// </summary>
    internal sealed record MountFreshness(
        DateTimeOffset? IndexBuiltAt,
        IReadOnlyList<V3FreshnessChannel> Channels,
        string? IndexBuilderSourceCommit);

    /// <summary>
    /// What the build report beside a mount states, once it is held to the mounted artifacts: when the newest acquisition of
    /// each half completed (its catalog retained), and the commit of the code that built the mount; each null when the report
    /// does not state it.
    /// </summary>
    internal sealed record BuildReportFacts(
        DateTimeOffset? LuxembourgCompletedAt,
        DateTimeOffset? EuropeCompletedAt,
        string? IndexBuilderSourceCommit)
    {
        internal static BuildReportFacts None { get; } = new(null, null, null);
    }

    /// <summary>What this mount's answers say of its freshness and its index's commit (R87, R23).</summary>
    internal MountFreshness Freshness => _freshness;

    /// <summary>
    /// The build report beside the mount (<see cref="BuildReportFileName"/>), held to what the mount verified: each artifact it
    /// names (the corpus, the Luxembourg index, the EU index) must be the mounted one, by digest, or the mount is refused, as
    /// every other check at opening refuses it. A report that names no corpus binds nothing and states nothing (a test fixture
    /// may write one to say only that a mount is built); no report states nothing either.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A half's completion is the time its acquisition's catalog was retained, which the report states for a half acquired
    /// across an interruption (<c>resumption.{half}.catalogedAt</c>): the latest time the mount states for that half, so the
    /// age counted from it is never less than the data's; it is read down to the whole second. A half acquired in one window
    /// is not dated by the report, and its completion is then said to be unstated rather than taken from the index's build
    /// time, which bounds when the Luxembourg half was observed and says nothing of an EU half the build reused.
    /// </para>
    /// <para>
    /// The commit is the report's <c>indexBuilderSource</c> (the code that wrote the mount), or for a mount re-derived from
    /// custody the derivation's <c>toolSource</c>, when it is a full commit; anything else (an unversioned build) is unstated.
    /// </para>
    /// </remarks>
    internal static async Task<BuildReportFacts> ReadBuildReportAsync(
        string directory,
        SourceArtifactRef corpus,
        LuxembourgIndexReader? luxembourg,
        EuropeIndexReader? europe,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(directory, BuildReportFileName);
        if (!File.Exists(path))
        {
            return BuildReportFacts.None;
        }

        var info = new FileInfo(path);
        if (info.Length > MaximumBuildReportBytes)
        {
            throw new InvalidDataException("The build report beside the mount is larger than a build report.");
        }

        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        return ReadBuildReport(bytes, corpus, luxembourg?.IndexRef, europe?.IndexRef);
    }

    /// <summary>The build report's bytes read as <see cref="ReadBuildReportAsync"/> reads them.</summary>
    internal static BuildReportFacts ReadBuildReport(
        ReadOnlySpan<byte> bytes,
        SourceArtifactRef corpus,
        SourceArtifactRef? luxembourgIndex,
        SourceArtifactRef? europeIndex)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(bytes.ToArray(), new JsonDocumentOptions { MaxDepth = 16, AllowDuplicateProperties = false });
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The build report beside the mount is not JSON.", exception);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException("The build report beside the mount is not a JSON object.");
            }

            // Every artifact the report names is the mounted one, or the report describes another mount.
            var namesCorpus = Bound(root, "corpus", corpus.Sha256);
            Bound(root, "luxembourgIndex", luxembourgIndex?.Sha256);
            Bound(root, "europeIndex", europeIndex?.Sha256);
            if (!namesCorpus)
            {
                return BuildReportFacts.None;
            }

            var source = Text(root, "indexBuilderSource") ??
                         (root.TryGetProperty("derivation", out var derivation) && derivation.ValueKind == JsonValueKind.Object
                             ? Text(derivation, "toolSource")
                             : null);
            return new BuildReportFacts(
                luxembourgIndex is null ? null : CatalogedAt(root, "luxembourg"),
                europeIndex is null ? null : CatalogedAt(root, "europe"),
                V3Commits.IsCommit(source) ? source : null);
        }

        static bool Bound(JsonElement root, string member, string? mounted)
        {
            if (!root.TryGetProperty(member, out var named) || named.ValueKind == JsonValueKind.Null)
            {
                return false;
            }

            var digest = named.ValueKind == JsonValueKind.Object ? Text(named, "Sha256") : null;
            if (digest is null || !string.Equals(digest, mounted, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"The build report beside the mount names a {member} that is not the mounted one ({digest ?? "no digest"}, mounted {mounted ?? "none"}).");
            }

            return true;
        }

        static string? Text(JsonElement value, string member) =>
            value.TryGetProperty(member, out var text) && text.ValueKind == JsonValueKind.String ? text.GetString() : null;

        static DateTimeOffset? CatalogedAt(JsonElement root, string half)
        {
            if (!root.TryGetProperty("resumption", out var resumption) || resumption.ValueKind != JsonValueKind.Object ||
                !resumption.TryGetProperty(half, out var stated) || stated.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var text = Text(stated, "catalogedAt");
            if (text is null)
            {
                return null;
            }

            if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at) || at == default)
            {
                throw new InvalidDataException($"The build report's {half} half states a catalog time that is not an instant ({text}).");
            }

            var utc = at.ToUniversalTime();
            return new DateTimeOffset(utc.UtcTicks - utc.UtcTicks % TimeSpan.TicksPerSecond, TimeSpan.Zero);
        }
    }

    /// <summary>
    /// The freshness and the commit a mount's answers name, from its readers and its build report's facts: the Luxembourg
    /// log's newest build time, and a channel for each publisher whose index is mounted, Luxembourg first.
    /// </summary>
    internal static MountFreshness FreshnessOf(LuxembourgIndexReader? luxembourg, EuropeIndexReader? europe, BuildReportFacts report)
    {
        ArgumentNullException.ThrowIfNull(report);
        DateTimeOffset? builtAt = null;
        if (luxembourg?.RecordsBuilds == true && luxembourg.ResolveObservations() is { Count: > 0 } observations)
        {
            builtAt = DateTimeOffset.ParseExact(
                observations[^1].BuiltAt, "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        }

        var channels = new List<V3FreshnessChannel>();
        if (luxembourg is not null)
        {
            channels.Add(new V3FreshnessChannel(PublisherId.LuLegilux, report.LuxembourgCompletedAt));
        }

        if (europe is not null)
        {
            channels.Add(new V3FreshnessChannel(PublisherId.EuEurLex, report.EuropeCompletedAt));
        }

        return new MountFreshness(builtAt, channels.AsReadOnly(), report.IndexBuilderSourceCommit);
    }
}
