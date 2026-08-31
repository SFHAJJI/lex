using System.Text;
using Lex.V3.Artifacts;
using Lex.V3.Contracts;

namespace Lex.V3.Api;

internal static class SyntheticResponseMapper
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static SyntheticResolveEnvelope Map(
        SyntheticSliceVerification verification,
        SyntheticResolvedRow? row,
        string family,
        string coordinate,
        string requestReference,
        ComponentIdentity runtime)
    {
        ArgumentNullException.ThrowIfNull(verification);
        ArgumentNullException.ThrowIfNull(runtime);
        if (!verification.Verified ||
            verification.Control is null ||
            verification.ManifestSha256 is null)
        {
            throw new InvalidOperationException("Only an admitted synthetic graph can produce a response.");
        }

        var control = verification.Control;
        var context = new SyntheticResolveContext(
            requestReference,
            new PreviewOperationReference(
                "resolve",
                control.OperationCatalog.CatalogId,
                PreviewSchemaExporter.ComputeDocumentSha256(control.OperationCatalog)),
            new PreviewRefusalRegistryReference(
                control.RefusalRegistry.RegistryId,
                control.RefusalRegistry.Schema,
                PreviewSchemaExporter.ComputeDocumentSha256(control.RefusalRegistry)),
            control.Snapshot,
            new SyntheticSliceArtifactReference(verification.ManifestSha256),
            new SyntheticSliceIndexReference(
                control.IndexStamp.Schema,
                control.Blobs.Single(static blob => blob.Kind == SyntheticSliceBlobKind.SqliteIndex).Sha256,
                control.IndexStamp.BuildId),
            runtime,
            control.Builder);

        if (string.Equals(family, "eli", StringComparison.Ordinal) &&
            string.Equals(coordinate, "eli/synthetic-preview", StringComparison.Ordinal) &&
            row?.Disposition == SyntheticResolutionDisposition.Held)
        {
            return SyntheticResolveSuccessEnvelope.Create(
                context,
                IdentifierFamily.Eli,
                coordinate,
                CreateObjectSet(row));
        }

        if (string.Equals(family, "historical_legal_id", StringComparison.Ordinal) &&
            string.Equals(
                coordinate,
                "historical_legal_id:synthetic-preview",
                StringComparison.Ordinal) &&
            (row is null || row.Disposition == SyntheticResolutionDisposition.CandidateOnly))
        {
            var candidates = row is null
                ? Array.Empty<SyntheticHeldRecordCandidate>()
                : new[]
                {
                    new SyntheticHeldRecordCandidate(
                        IdentifierFamily.Eli,
                        row.CanonicalIdentifier,
                        PublisherId.LuLegilux),
                };
            return SyntheticResolveRefusalEnvelope.Create(
                context,
                SyntheticIdentifierUnknownRefusal.Create(candidates));
        }

        throw new InvalidDataException("The SQL row does not match the accepted resolve request.");
    }

    private static PreviewObjectSet CreateObjectSet(SyntheticResolvedRow row)
    {
        string body;
        try
        {
            body = StrictUtf8.GetString(row.Body);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException("The admitted SQL body is not strict UTF-8.", exception);
        }

        var coordinate = new PreviewSyntheticCoordinate(
            $"preview:{row.CanonicalIdentifier}#{row.Anchor}",
            synthetic: true,
            $"preview:{row.CanonicalIdentifier}",
            $"preview:{row.VersionKey}",
            $"preview:{row.Anchor}",
            BodyHoldingState.HeldPublic,
            PreviewBodyDispositionReason.SyntheticFixture,
            body,
            row.BlobSha256);
        return new PreviewObjectSet(
            V3SchemaIds.PreviewObjectSet,
            "s0-05-sql-object-set",
            new PreviewObject[] { coordinate });
    }
}
