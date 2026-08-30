using System.Xml;
using System.Xml.Linq;
using Lex.Law;

namespace Lex.Sources.Legilux;

internal static class LegiluxLicenceEvidence
{
    private const string AkomaNtosoNamespace =
        "http://docs.oasis-open.org/legaldocml/ns/akn/3.0/CSD13";
    internal const string CreativeCommonsBy40 =
        "http://creativecommons.org/licenses/by/4.0/";
    internal const string LicenceScl =
        "http://data.legilux.public.lu/resource/authority/license/licenceSCL";

    private static readonly XNamespace Scl = "http://www.scl.lu";

    internal static LicenceChannelEvidence FromSparqlTerms(
        IEnumerable<SparqlTerm> terms)
    {
        var claims = terms.Select(term => new LicenceClaim(
            term.Type,
            term.Value,
            IsHttpUriTerm(term) ? term.Value : null)).ToArray();
        if (claims.Length == 0)
            return LicenceChannelEvidence.Absent;
        return claims.Any(claim => claim.LicenceUri is null)
            ? LicenceChannelEvidence.Invalid(claims)
            : LicenceChannelEvidence.Present(claims);
    }

    internal static LicenceChannelEvidence FromAkomaNtoso(
        byte[] bytes, string manifestationIdentifier)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestationIdentifier);

        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersFromEntities = 0,
                MaxCharactersInDocument = 40L * 1024 * 1024,
            });
            var document = XDocument.Load(reader, LoadOptions.None);
            var akn = (XNamespace)AkomaNtosoNamespace;
            var root = document.Root;
            if (root?.Name != akn + "akomaNtoso")
                return LicenceChannelEvidence.Invalid([]);

            var act = SingleDirect(root, akn + "act");
            var meta = act is null ? null : SingleDirect(act, akn + "meta");
            var identification = meta is null
                ? null : SingleDirect(meta, akn + "identification");
            var frbrManifestation = identification is null
                ? null : SingleDirect(identification, akn + "FRBRManifestation");
            var frbrThis = frbrManifestation is null
                ? null : SingleDirect(frbrManifestation, akn + "FRBRthis");
            if (!IsExactFrbrThis(frbrThis, manifestationIdentifier))
                return LicenceChannelEvidence.Invalid([]);

            var blocks = identification!.Elements(Scl + "JOLUXManifestation").ToArray();
            if (blocks.Length is 0 or > 4_096)
                return LicenceChannelEvidence.Invalid([]);
            var directBlocks = blocks.ToHashSet();
            if (document.Descendants(Scl + "JOLUXManifestation")
                .Any(block => !directBlocks.Contains(block)))
                return LicenceChannelEvidence.Invalid([]);
            var directChildren = blocks.SelectMany(block => block.Elements(Scl + "jolux"))
                .ToHashSet();
            if (document.Descendants(Scl + "jolux").Any(element =>
                    element.Attribute(Scl + "name")?.Value is ("uriThis" or "license")
                    && !directChildren.Contains(element)))
                return LicenceChannelEvidence.Invalid([]);
            var validated = new List<(string Identity, string[] Licences)>(blocks.Length);
            foreach (var block in blocks)
            {
                if (!TryReadBlock(block, out var identity, out var licences))
                    return LicenceChannelEvidence.Invalid([]);
                validated.Add((identity, licences));
            }

            var matchingBlocks = validated.Where(block => string.Equals(
                block.Identity, manifestationIdentifier, StringComparison.Ordinal)).ToArray();
            if (matchingBlocks.Length != 1)
                return LicenceChannelEvidence.Invalid([]);

            var values = matchingBlocks[0].Licences;
            if (values.Length == 0)
                return LicenceChannelEvidence.Absent;

            var claims = values.Select(value => new LicenceClaim(
                "token", value, MapFileToken(value))).ToArray();
            if (claims.Distinct().Count() != claims.Length)
                return LicenceChannelEvidence.Invalid(claims);
            return claims.Any(claim => claim.LicenceUri is null)
                ? LicenceChannelEvidence.Invalid(claims)
                : LicenceChannelEvidence.Present(claims);
        }
        catch (Exception error) when (error is XmlException
                                      or InvalidDataException
                                      or ArgumentException)
        {
            return LicenceChannelEvidence.Invalid([]);
        }
    }

    private static XElement? SingleDirect(XElement parent, XName name)
    {
        var values = parent.Elements(name).Take(2).ToArray();
        return values.Length == 1 ? values[0] : null;
    }

    private static bool IsExactFrbrThis(XElement? element, string expected)
    {
        if (element is null || element.HasElements || element.Value.Length != 0)
            return false;
        var attributes = element.Attributes().ToArray();
        return attributes.Length == 1
            && attributes[0].Name == "value"
            && string.Equals(attributes[0].Value, expected, StringComparison.Ordinal);
    }

    private static bool TryReadBlock(
        XElement block, out string identity, out string[] licences)
    {
        identity = "";
        licences = [];
        if (block.Attributes().Any() || block.Elements().Any(child => child.Name != Scl + "jolux"))
            return false;

        var identities = new List<string>();
        var values = new List<string>();
        foreach (var child in block.Elements())
        {
            var attributes = child.Attributes().ToArray();
            if (child.HasElements
                || attributes.Length != 1
                || attributes[0].Name != Scl + "name"
                || string.IsNullOrEmpty(child.Value))
                return false;

            switch (attributes[0].Value)
            {
                case "uriThis":
                    identities.Add(child.Value);
                    break;
                case "license":
                    values.Add(child.Value);
                    break;
                default:
                    return false;
            }
        }

        if (identities.Count != 1 || values.Count > 2)
            return false;
        identity = identities[0];
        licences = values.ToArray();
        return true;
    }

    private static bool IsHttpUriTerm(SparqlTerm term) =>
        string.Equals(term.Type, "uri", StringComparison.Ordinal)
        && Uri.TryCreate(term.Value, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https";

    private static string? MapFileToken(string value) => value switch
    {
        "CC-BY-4.0" => CreativeCommonsBy40,
        "licenceSCL" => LicenceScl,
        _ => null,
    };
}
