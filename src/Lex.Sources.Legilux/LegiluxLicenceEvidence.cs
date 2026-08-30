using System.Xml;
using System.Xml.Linq;
using Lex.Law;

namespace Lex.Sources.Legilux;

internal static class LegiluxLicenceEvidence
{
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
            var root = document.Root;
            if (root is null || root.Name.LocalName != "akomaNtoso")
                return LicenceChannelEvidence.Invalid([]);

            var akn = root.Name.Namespace;
            var identification = root.Element(akn + "act")?
                .Element(akn + "meta")?
                .Element(akn + "identification");
            var frbrThis = identification?
                .Element(akn + "FRBRManifestation")?
                .Element(akn + "FRBRthis")?
                .Attribute("value")?.Value;
            if (!string.Equals(frbrThis, manifestationIdentifier, StringComparison.Ordinal))
                return LicenceChannelEvidence.Invalid([]);

            var matchingBlocks = identification?.Elements(Scl + "JOLUXManifestation")
                .Where(block => HasExactIdentity(block, manifestationIdentifier))
                .ToArray() ?? [];
            if (matchingBlocks.Length != 1)
                return LicenceChannelEvidence.Invalid([]);

            var values = matchingBlocks[0].Elements(Scl + "jolux")
                .Where(element => string.Equals(
                    element.Attribute(Scl + "name")?.Value,
                    "license", StringComparison.Ordinal))
                .Select(element => element.Value)
                .ToArray();
            if (values.Length == 0)
                return LicenceChannelEvidence.Absent;

            var claims = values.Select(value => new LicenceClaim(
                "token", value, MapFileToken(value))).ToArray();
            return claims.Any(claim => claim.LicenceUri is null)
                ? LicenceChannelEvidence.Invalid(claims)
                : LicenceChannelEvidence.Present(claims);
        }
        catch (XmlException)
        {
            return LicenceChannelEvidence.Invalid([]);
        }
    }

    private static bool HasExactIdentity(XElement block, string expected)
    {
        var identities = block.Elements(Scl + "jolux")
            .Where(element => string.Equals(
                element.Attribute(Scl + "name")?.Value,
                "uriThis", StringComparison.Ordinal))
            .Select(element => element.Value)
            .ToArray();
        return identities.Length == 1
            && string.Equals(identities[0], expected, StringComparison.Ordinal);
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
