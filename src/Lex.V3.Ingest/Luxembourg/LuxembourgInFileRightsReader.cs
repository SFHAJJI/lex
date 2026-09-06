using System.Xml;
using System.Xml.Linq;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Http;
using Lex.V3.Contracts.Source.Luxembourg;

namespace Lex.V3.Ingest.Luxembourg;

internal enum LuxembourgInFileRightsReadStatus
{
    Observed,
    UnsupportedRepresentation,
    MalformedXml,
    ManifestationIdentityMismatch,
    InvalidLicenceIri,
}

internal sealed record LuxembourgInFileRightsReading(
    string ManifestationIri,
    SourceArtifactRef BodyRef,
    LuxembourgInFileRightsReadStatus Status,
    IReadOnlyList<string> LicenceIris);

internal static class LuxembourgInFileRightsReader
{
    internal static async Task<LuxembourgInFileRightsReading> ReadAsync(
        ICustodyStore store,
        SourceArtifactRef bodyRef,
        string manifestationIri,
        SourceArtifactRef runIdentity,
        LuxembourgUserFormatToken format,
        CancellationToken cancellationToken)
    {
        var bytes = await CustodyRestore.ReadByDigestCheckedAsync(store, bodyRef.Sha256, cancellationToken);
        if (format is not (LuxembourgUserFormatToken.Xml or LuxembourgUserFormatToken.XmlAkomaNtoso))
        {
            return Failure(LuxembourgInFileRightsReadStatus.UnsupportedRepresentation);
        }

        try
        {
            using var stream = new MemoryStream(bytes.ToArray(), writable: false);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = CustodyBounds.MaxObjectBytes,
            });
            // Parse the complete retained representation: malformed trailing content cannot
            // authorize merely because a plausible declaration appeared near the beginning.
            var document = XDocument.Load(reader, LoadOptions.None);
            var root = document.Root;
            if (root is null || root.Name.LocalName != "akomaNtoso" ||
                root.Name.NamespaceName is not
                    ("http://docs.oasis-open.org/legaldocml/ns/akn/3.0/CSD13" or
                     "http://docs.oasis-open.org/legaldocml/ns/akn/3.0"))
            {
                return Failure(LuxembourgInFileRightsReadStatus.UnsupportedRepresentation);
            }
            XNamespace akn = root.Name.Namespace;
            XNamespace scl = "http://www.scl.lu";
            var acts = root.Elements().ToArray();
            if (acts.Length != 1 || acts[0].Name != akn + "act")
            {
                return Failure(LuxembourgInFileRightsReadStatus.ManifestationIdentityMismatch);
            }
            var metas = acts[0].Elements(akn + "meta").ToArray();
            if (metas.Length != 1)
            {
                return Failure(LuxembourgInFileRightsReadStatus.ManifestationIdentityMismatch);
            }
            var identifications = metas[0].Elements(akn + "identification").ToArray();
            if (identifications.Length != 1)
            {
                return Failure(LuxembourgInFileRightsReadStatus.ManifestationIdentityMismatch);
            }
            var identification = identifications[0];
            var manifestations = identification.Elements(akn + "FRBRManifestation").ToArray();
            if (manifestations.Length != 1)
            {
                return Failure(LuxembourgInFileRightsReadStatus.ManifestationIdentityMismatch);
            }
            var frbrIdentities = manifestations
                .Elements(akn + "FRBRthis").Select(element => (string?)element.Attribute("value")).ToArray();
            var metadata = identification.Elements(scl + "JOLUXManifestation").ToArray();
            var joluxIdentities = metadata.Elements(scl + "jolux")
                .Where(element => (string?)element.Attribute(scl + "name") == "uriThis")
                .Select(element => element.Value).ToArray();
            if (frbrIdentities.Length != 1 || frbrIdentities[0] != manifestationIri ||
                metadata.Length != 1 || joluxIdentities.Length != 1 || joluxIdentities[0] != manifestationIri)
            {
                return Failure(LuxembourgInFileRightsReadStatus.ManifestationIdentityMismatch);
            }

            var declarations = metadata.Elements(scl + "jolux")
                .Where(element => (string?)element.Attribute(scl + "name") == "license").ToArray();
            if (declarations.Any(static element => element.HasElements))
            {
                return Failure(LuxembourgInFileRightsReadStatus.InvalidLicenceIri);
            }
            var values = declarations.Select(element => element.Value)
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            try
            {
                // Reuse the rights contract's exact IRI validation; do not trim, repair or
                // normalize an unruled publisher declaration into an admitting one.
                var observation = new LuxembourgRightsChannelObservation(
                    manifestationIri, runIdentity, bodyRef, values);
                return new(manifestationIri, bodyRef, LuxembourgInFileRightsReadStatus.Observed, observation.LicenceIris);
            }
            catch (ArgumentException)
            {
                return Failure(LuxembourgInFileRightsReadStatus.InvalidLicenceIri);
            }
        }
        catch (XmlException)
        {
            return Failure(LuxembourgInFileRightsReadStatus.MalformedXml);
        }

        LuxembourgInFileRightsReading Failure(LuxembourgInFileRightsReadStatus status) =>
            new(manifestationIri, bodyRef, status, []);
    }
}
