using System.Text;
using System.Text.Json;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Ingest.Europe;

public sealed partial class EuFirstMountAcquisition
{
    private const string AcquisitionSchema = "lex-eu-first-mount-acquisition/1";
    private const string ResumedAcquisitionSchema = "lex-eu-first-mount-acquisition/2";
    private const string DatedAcquisitionSchema = "lex-eu-first-mount-acquisition/3";

    /// <summary>
    /// Restores one query run and supplies that same instance to Formex reconciliation. The caller
    /// pins the retained catalog and its original sorted seed scope. No publisher session is opened.
    /// A catalog a resumed run retained (<c>/2</c>) restores the same way and carries its
    /// <see cref="EuFirstMountAcquisitionResult.Resumption"/>, held to the resume record it names; a dated one (<c>/3</c>)
    /// carries its <see cref="EuFirstMountAcquisitionResult.CatalogedAt"/>.
    /// </summary>
    public static async Task<EuFirstMountAcquisitionResult> ReopenAsync(ICustodyStore store,
        SourceArtifactRef checkpoint, IReadOnlyList<string> expectedSeeds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(expectedSeeds);
        var seeds = expectedSeeds.ToArray();
        cancellationToken.ThrowIfCancellationRequested();
        var bytes = await CustodyRestore.ReadByDigestCheckedAsync(store, checkpoint.Sha256, cancellationToken).ConfigureAwait(false);
        try
        {
            var document = ReadCatalog(bytes.Span, out var resumption, out var catalogedAt);
            if (document.Seeds is null || document.Query is null || document.Formex is null || document.Rights is null ||
                document.CorpusRun is null || document.Renderers is null || document.Renderers.Length != EuRendererSources.RendererFiles.Count ||
                document.Renderers.Any(static role => role is null || role.Reference is null) ||
                document.Seeds.Length == 0 || document.Seeds.Any(string.IsNullOrWhiteSpace) ||
                !document.Seeds.SequenceEqual(document.Seeds.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal), StringComparer.Ordinal) ||
                !seeds.SequenceEqual(document.Seeds, StringComparer.Ordinal) ||
                !document.Renderers.Select(static role => role.File).SequenceEqual(EuRendererSources.RendererFiles, StringComparer.Ordinal))
                throw new CustodyIntegrityException("EU acquisition catalog framing or requested scope disagrees.");
            if (resumption is not null)
                await AcquisitionResume.VerifyAsync(store, resumption, cancellationToken).ConfigureAwait(false);
            var sources = await EuRendererSources.FromCustodyAsync(store,
                document.Renderers.ToDictionary(static role => role.File, static role => role.Reference, StringComparer.Ordinal),
                cancellationToken).ConfigureAwait(false);
            await EuQueryExecutionAdapter.VerifyRendererBindingsAsync(store, document.Query, sources, cancellationToken).ConfigureAwait(false);
            var run = await EuQueryExecutionAdapter.ReopenAsync(store, document.Query, seeds, cancellationToken).ConfigureAwait(false);
            var records = run.CorpusRecordSet?.Set.Records;
            if (records is null || records.Count == 0 || records.Any(record => record.RunIdentity != document.CorpusRun))
                throw new CustodyIntegrityException("EU acquisition catalog names a different corpus run.");
            var formex = await EuFormexPackagePopulationProducer.ReopenAsync(store, document.Formex, run,
                sources.FormexManifestation, sources.DocumentFetch, workCelex: null, cancellationToken).ConfigureAwait(false);
            var rights = await EuLegalNoticeRouteProducer.ReopenAsync(store, document.Rights, document.CorpusRun,
                cancellationToken).ConfigureAwait(false);
            if (!formex.Delivered || !ReferenceEquals(formex.Reconciliation?.Run, run) || rights.Route is null)
                throw new CustodyIntegrityException("EU acquisition components did not restore their original association: " + rights.Detail);
            return EuFirstMountAcquisitionResult.Success(run, formex, rights).WithCheckpoint(checkpoint, resumption, catalogedAt);
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException or DecoderFallbackException)
        {
            throw new CustodyIntegrityException("EU acquisition catalog failed independent verification.", exception);
        }
    }

    private static AcquisitionCatalog ReadCatalog(ReadOnlySpan<byte> bytes, out AcquisitionResumption? resumption) =>
        ReadCatalog(bytes, out resumption, out _);

    /// <summary>
    /// Every catalog schema through one door. <c>/1</c> is what every run wrote before catalogs were dated, and what a reuse
    /// of an undated <c>/1</c> population still renews it as; <c>/2</c> is <c>/1</c> with the resumption a resumed run
    /// states; <c>/3</c> is <c>/1</c> with the instant a run that was not resumed retained it (<paramref name="catalogedAt"/>).
    /// Each must be its own exact canonical bytes, so neither a resumption nor a time can be added to a held catalog or
    /// stripped from one without changing the digest every later reader names it by.
    /// </summary>
    private static AcquisitionCatalog ReadCatalog(ReadOnlySpan<byte> bytes, out AcquisitionResumption? resumption,
        out DateTimeOffset? catalogedAt)
    {
        var text = new UTF8Encoding(false, true).GetString(bytes);
        catalogedAt = null;
        using (var framing = JsonDocument.Parse(text))
        {
            var schema = framing.RootElement.ValueKind == JsonValueKind.Object &&
                framing.RootElement.TryGetProperty("schema", out var named) && named.ValueKind == JsonValueKind.String
                    ? named.GetString()
                    : null;
            if (schema == ResumedAcquisitionSchema)
            {
                var resumed = ContractJson.Deserialize<ResumedAcquisitionCatalog>(text);
                if (resumed.Resume is null || !bytes.SequenceEqual(Encoding.UTF8.GetBytes(ContractJson.Serialize(resumed))))
                    throw new CustodyIntegrityException("Resumed EU acquisition catalog framing is invalid.");
                resumption = resumed.Resume;
                return new AcquisitionCatalog(resumed.Schema, resumed.Seeds, resumed.Query, resumed.Formex, resumed.Rights,
                    resumed.CorpusRun, resumed.Renderers);
            }

            if (schema == DatedAcquisitionSchema)
            {
                var dated = ContractJson.Deserialize<DatedAcquisitionCatalog>(text);
                if (dated.CatalogedAt == default || !bytes.SequenceEqual(Encoding.UTF8.GetBytes(ContractJson.Serialize(dated))))
                    throw new CustodyIntegrityException("Dated EU acquisition catalog framing is invalid.");
                resumption = null;
                catalogedAt = dated.CatalogedAt;
                return new AcquisitionCatalog(dated.Schema, dated.Seeds, dated.Query, dated.Formex, dated.Rights,
                    dated.CorpusRun, dated.Renderers);
            }
        }

        resumption = null;
        var document = ContractJson.Deserialize<AcquisitionCatalog>(text);
        if (document is null || document.Schema != AcquisitionSchema || !bytes.SequenceEqual(EncodeAcquisition(document)))
            throw new CustodyIntegrityException("EU acquisition catalog framing or requested scope disagrees.");
        return document;
    }

    private async Task<SourceArtifactRef> RetainAcquisitionAsync(string[] seeds, EuRendererSources sources,
        EuQueryExecutionResult run, EuFormexPackagePopulationResult formex, EuLegalNoticeRouteResult rights,
        AcquisitionResumption? resumption, DateTimeOffset? catalogedAt, CancellationToken cancellationToken)
    {
        if (resumption is not null && catalogedAt is not null)
            throw new ArgumentException("A resumed catalog states its time in its resumption, never beside it.", nameof(catalogedAt));
        if (run.AcquisitionCheckpointRef is null || formex.CheckpointRef is null || rights.Route is null ||
            !ReferenceEquals(formex.Reconciliation?.Run, run))
            throw new CustodyIntegrityException("A successful EU acquisition omitted its checked component association.");
        var all = new[] { sources.Census, sources.ObjectFacts, sources.Witness, sources.DocumentFetch,
            sources.FormexManifestation, sources.LegalNotice };
        // Retain even the legacy legal-notice role, which the current producer does not execute.
        // This completes the source map without manufacturing an observation of that renderer.
        foreach (var renderer in all)
        {
            var sourceBytes = renderer.CopyBytes();
            var (held, failure) = await CustodyHold.TryHoldAsync(_custodyStore, sourceBytes, cancellationToken).ConfigureAwait(false);
            if (held is null) throw new CustodyRequiredException("EU acquisition source retention refused: " + failure);
            if (held.Reference.ContentSha256 != renderer.Reference.Sha256 || held.Reference.ByteLength != sourceBytes.Length)
                throw new CustodyIntegrityException("EU acquisition source receipt names different bytes.");
        }
        var records = run.CorpusRecordSet!.Set.Records;
        var corpusRun = records[0].RunIdentity;
        if (records.Any(record => record.RunIdentity != corpusRun) || rights.Route.RunIdentity != corpusRun)
            throw new CustodyIntegrityException("EU acquisition rights and records belong to different runs.");
        var routeBytes = rights.Route.CopyCanonicalBytes();
        var routeDigest = CustodyDigest.Of(routeBytes);
        var heldRoute = await CustodyRestore.ReadByDigestCheckedAsync(_custodyStore, routeDigest, cancellationToken).ConfigureAwait(false);
        if (!heldRoute.Span.SequenceEqual(routeBytes))
            throw new CustodyIntegrityException("EU acquisition rights route was not retained exactly.");
        var document = new AcquisitionCatalog(AcquisitionSchema, seeds.ToArray(), run.AcquisitionCheckpointRef,
            formex.CheckpointRef, new SourceArtifactRef(NewUrn(), routeDigest), corpusRun,
            all.Select((renderer, index) => new RendererRole(EuRendererSources.RendererFiles[index], renderer.Reference)).ToArray());
        // A resumed run writes the /2 catalog and a run that was not resumed the dated /3; /1 is written only when an undated
        // /1 population is renewed (a reuse), exactly the bytes it always was.
        var bytes = resumption is not null
            ? Encoding.UTF8.GetBytes(ContractJson.Serialize(new ResumedAcquisitionCatalog(ResumedAcquisitionSchema,
                document.Seeds, document.Query, document.Formex, document.Rights, document.CorpusRun, document.Renderers, resumption)))
            : catalogedAt is { } at
                ? Encoding.UTF8.GetBytes(ContractJson.Serialize(new DatedAcquisitionCatalog(DatedAcquisitionSchema,
                    document.Seeds, document.Query, document.Formex, document.Rights, document.CorpusRun, document.Renderers, at)))
                : EncodeAcquisition(document);
        var (receipt, detail) = await CustodyHold.TryHoldAsync(_custodyStore, bytes, cancellationToken).ConfigureAwait(false);
        if (receipt is null) throw new CustodyRequiredException("EU acquisition catalog retention refused: " + detail);
        if (receipt.Reference.ContentSha256 != CustodyDigest.Of(bytes) || receipt.Reference.ByteLength != bytes.Length)
            throw new CustodyIntegrityException("EU acquisition catalog receipt names different bytes.");
        return new SourceArtifactRef(NewUrn(), receipt.Reference.ContentSha256);
    }

    private static byte[] EncodeAcquisition(AcquisitionCatalog document) => Encoding.UTF8.GetBytes(ContractJson.Serialize(document));
    private sealed record AcquisitionCatalog(string Schema, string[] Seeds, SourceArtifactRef Query,
        SourceArtifactRef Formex, SourceArtifactRef Rights, SourceArtifactRef CorpusRun, RendererRole[] Renderers);
    private sealed record ResumedAcquisitionCatalog(string Schema, string[] Seeds, SourceArtifactRef Query,
        SourceArtifactRef Formex, SourceArtifactRef Rights, SourceArtifactRef CorpusRun, RendererRole[] Renderers,
        AcquisitionResumption Resume);

    /// <summary>A catalog of a run that was not resumed: <c>/1</c>'s members and the instant the run retained it.</summary>
    private sealed record DatedAcquisitionCatalog(string Schema, string[] Seeds, SourceArtifactRef Query,
        SourceArtifactRef Formex, SourceArtifactRef Rights, SourceArtifactRef CorpusRun, RendererRole[] Renderers,
        DateTimeOffset CatalogedAt);
    private sealed record RendererRole(string File, SourceArtifactRef Reference);
}
