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

    /// <summary>The first line of reading /2's canonical text: its name.</summary>
    internal const string ReadingTwoProfileName = "lex-lu-in-file-rights-reading/2";

    /// <summary>
    /// Reading /2's rules as canonical text (LU-DATA-DESIGN v2 §1.1, "Rules /2"): what <see cref="ReadTwoAsync"/> does, one
    /// rule per line. Reading /1 (<see cref="ReadAsync"/>) stays byte-identical: run 15's replay runs it.
    /// </summary>
    private const string ReadingTwoProfileText =
        ReadingTwoProfileName + "\n" +
        "representation=xml-or-xml-akomantoso;any-other-format-is-not_applicable(the-channel-cannot-read-it;not-a-defect-of-the-file)\n" +
        "parse=the-whole-document;dtd-prohibited;no-resolver;malformed-is-malformed_xml\n" +
        "root=akomaNtoso-in-the-akn-3.0-or-csd13-namespace;its-element-children-after-any-leading-scl:postit-are-exactly-one-akn-document-element;comments-tolerated;else-root_rejected\n" +
        "identification=the-document-element's-own-meta/identification;more-than-one-meta-or-identification-is-root_rejected;none-means-no-own-block\n" +
        "declarations=every-scl:jolux[@scl:name='license']-anywhere-in-the-document;one-with-element-content-or-whose-value-is-not-one-exact-absolute-iri-is-invalid_licence_iri;values-never-trimmed-or-repaired\n" +
        "named-identities=every-scl:JOLUXManifestation-anywhere,each-scl:jolux[@scl:name='uriThis']-value-verbatim-with-that-block's-licence-values\n" +
        "binding=the-scl:JOLUXManifestation-of-the-own-identification-whose-uriThis-equals-the-fetched-manifestation-exactly;two-such-blocks,or-such-a-block-naming-a-second-identity,is-ambiguous_binding;sibling-blocks-(the-pdf-twin)-tolerated\n" +
        "frbr=the-own-identification's-FRBRManifestation/FRBRthis-values-recorded,never-required\n" +
        "status=bound-when-the-own-block-states-a-licence;own_block_empty-when-it-states-none;else-unbound-when-the-document-declares-a-licence;else-silent\n" +
        "licence-like-text-outside-scl:jolux-never-counts\n";

    /// <summary>The SHA-256 of reading /2's canonical text, lowercase hex.</summary>
    internal static string ReadingTwoProfileSha256 { get; } =
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(ReadingTwoProfileText)));

    /// <summary>Reading /2's canonical text, as UTF-8 bytes (a copy).</summary>
    internal static byte[] CopyReadingTwoProfileBytes() => System.Text.Encoding.UTF8.GetBytes(ReadingTwoProfileText);

    /// <summary>
    /// In-file rights reading /2 (<see cref="ReadingTwoProfileText"/>) of the retained body <paramref name="bodyRef"/>, read by
    /// its digest and checked: the licence the document's own block states for <paramref name="manifestationIri"/>, every
    /// licence the file declares, every identity its blocks name, and its FRBR identity, recorded. It binds by the
    /// publisher's own block for the fetched manifestation, never by FRBR and never by a neighbour's block: a PDF twin's block
    /// is a named identity, never the held body's licence.
    /// </summary>
    internal static async Task<LuxembourgInFileRightsReadingTwo> ReadTwoAsync(
        ICustodyStore store,
        SourceArtifactRef bodyRef,
        string manifestationIri,
        LuxembourgUserFormatToken format,
        CancellationToken cancellationToken)
    {
        if (format is not (LuxembourgUserFormatToken.Xml or LuxembourgUserFormatToken.XmlAkomaNtoso))
        {
            // The body is still read by its digest: a reading names only bytes custody holds.
            _ = await CustodyRestore.ReadByDigestCheckedAsync(store, bodyRef.Sha256, cancellationToken).ConfigureAwait(false);
            return Empty(LuxembourgInFileRightsReadingTwoStatus.NotApplicable);
        }

        var bytes = await CustodyRestore.ReadByDigestCheckedAsync(store, bodyRef.Sha256, cancellationToken).ConfigureAwait(false);
        return ReadTwo(bytes, bodyRef, manifestationIri);

        LuxembourgInFileRightsReadingTwo Empty(LuxembourgInFileRightsReadingTwoStatus status) =>
            new(manifestationIri, bodyRef, status, [], [], [], []);
    }

    /// <summary>Reading /2 over bytes already read by their digest (<see cref="ReadTwoAsync"/>).</summary>
    internal static LuxembourgInFileRightsReadingTwo ReadTwo(ReadOnlyMemory<byte> bytes, SourceArtifactRef bodyRef, string manifestationIri)
    {
        XDocument document;
        try
        {
            using var stream = new MemoryStream(bytes.ToArray(), writable: false);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = CustodyBounds.MaxObjectBytes,
            });
            document = XDocument.Load(reader, LoadOptions.None);
        }
        catch (XmlException)
        {
            return Empty(LuxembourgInFileRightsReadingTwoStatus.MalformedXml);
        }

        var root = document.Root;
        if (root is null || root.Name.LocalName != "akomaNtoso" ||
            root.Name.NamespaceName is not
                ("http://docs.oasis-open.org/legaldocml/ns/akn/3.0/CSD13" or
                 "http://docs.oasis-open.org/legaldocml/ns/akn/3.0"))
        {
            return Empty(LuxembourgInFileRightsReadingTwoStatus.RootRejected);
        }

        XNamespace akn = root.Name.Namespace;
        XNamespace scl = "http://www.scl.lu";
        var children = root.Elements().SkipWhile(element => element.Name == scl + "postit").ToArray();
        if (children.Length != 1 || children[0].Name.Namespace != akn)
        {
            return Empty(LuxembourgInFileRightsReadingTwoStatus.RootRejected);
        }

        var metas = children[0].Elements(akn + "meta").ToArray();
        var identifications = metas.SelectMany(meta => meta.Elements(akn + "identification")).ToArray();
        if (metas.Length > 1 || identifications.Length > 1)
        {
            return Empty(LuxembourgInFileRightsReadingTwoStatus.RootRejected);
        }

        // Every licence declaration in the document: markup inside one, or a value that is not one exact IRI, refuses.
        var declarations = root.Descendants(scl + "jolux").Where(IsLicence).ToArray();
        if (declarations.Any(static element => element.HasElements) ||
            declarations.Any(static element => !IsExactAbsoluteIri(element.Value)))
        {
            return Empty(LuxembourgInFileRightsReadingTwoStatus.InvalidLicenceIri);
        }

        var declared = Sorted(declarations.Select(static element => element.Value));
        var named = new List<LuxembourgInFileNamedIdentity>();
        foreach (var block in root.Descendants(scl + "JOLUXManifestation"))
        {
            var licences = Sorted(block.Elements(scl + "jolux").Where(IsLicence).Select(static element => element.Value));
            foreach (var identity in UriThis(block))
            {
                named.Add(new LuxembourgInFileNamedIdentity(identity, licences));
            }
        }

        var identification = identifications.SingleOrDefault();
        var frbr = identification is null
            ? []
            : Sorted(identification.Elements(akn + "FRBRManifestation").Elements(akn + "FRBRthis")
                .Select(static element => (string?)element.Attribute("value"))
                .Where(static value => !string.IsNullOrEmpty(value))
                .Select(static value => value!));
        var own = identification is null
            ? []
            : identification.Elements(scl + "JOLUXManifestation")
                .Where(block => UriThis(block).Contains(manifestationIri, StringComparer.Ordinal))
                .ToArray();
        if (own.Length > 1 || own.Length == 1 && UriThis(own[0]).Count() != 1)
        {
            return Empty(LuxembourgInFileRightsReadingTwoStatus.AmbiguousBinding);
        }

        if (own.Length == 1)
        {
            var ownLicences = Sorted(own[0].Elements(scl + "jolux").Where(IsLicence).Select(static element => element.Value));
            return new(manifestationIri, bodyRef,
                ownLicences.Length == 0 ? LuxembourgInFileRightsReadingTwoStatus.OwnBlockEmpty : LuxembourgInFileRightsReadingTwoStatus.Bound,
                ownLicences, declared, named, frbr);
        }

        return new(manifestationIri, bodyRef,
            declared.Length == 0 ? LuxembourgInFileRightsReadingTwoStatus.Silent : LuxembourgInFileRightsReadingTwoStatus.Unbound,
            [], declared, named, frbr);

        bool IsLicence(XElement element) => (string?)element.Attribute(scl + "name") == "license";

        IEnumerable<string> UriThis(XElement block) => block.Elements(scl + "jolux")
            .Where(element => (string?)element.Attribute(scl + "name") == "uriThis" && element.Value.Length != 0)
            .Select(static element => element.Value);

        LuxembourgInFileRightsReadingTwo Empty(LuxembourgInFileRightsReadingTwoStatus status) =>
            new(manifestationIri, bodyRef, status, [], [], [], []);
    }

    // Distinct only: the reading's record sorts its own lists in the contract's order.
    private static string[] Sorted(IEnumerable<string> values) => values.Distinct(StringComparer.Ordinal).ToArray();

    // The rights contract's exact IRI validation, as reading /1 reuses it; nothing is trimmed or repaired.
    private static bool IsExactAbsoluteIri(string value) => LuxembourgInFileRightsReadingTwo.IsExactLicenceIri(value);
}
