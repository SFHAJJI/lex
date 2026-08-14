using Lex.Law;

namespace Lex.Sources.Legilux;

internal static class LegiluxPublisherMetadata
{
    private const string Prefixes = """
        PREFIX jolux: <http://data.legilux.public.lu/resource/ontology/jolux#>
        PREFIX skos: <http://www.w3.org/2004/02/skos/core#>
        """;
    private const string SchemePrefix =
        "http://data.legilux.public.lu/resource/authority/legal-subject-";
    private static readonly IReadOnlyDictionary<string, string> Schemes =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [SchemePrefix + "theme"] = "theme",
            [SchemePrefix + "organisation"] = "organisation",
            [SchemePrefix + "place"] = "place",
            [SchemePrefix + "legal-resource"] = "legal_resource",
            [SchemePrefix + "country"] = "country",
            ["http://publications.europa.eu/resource/authority/country"] = "country",
        };

    internal static string Query(IReadOnlyCollection<string> works)
    {
        var values = LegiluxAdapter.HeldWorkValues(works);
        var limit = checked(works.Count * LegiluxAdapter.SubjectRawRowsPerWorkMaximum + 1);
        return Prefixes + $$"""
        SELECT DISTINCT ?work ?level ?subject ?label ?scheme WHERE {
          VALUES ?work { {{values}} }
          ?act a jolux:Act ; jolux:isMemberOf ?work .
          {
            ?act jolux:subjectLevel1 ?subject .
            BIND("1" AS ?level)
          } UNION {
            ?act jolux:subjectLevel2 ?subject .
            BIND("2" AS ?level)
          }
          OPTIONAL { ?subject skos:prefLabel ?label . FILTER(LANG(?label) = "fr") }
          OPTIONAL { ?subject skos:inScheme ?scheme }
        } ORDER BY ?work ?level ?subject ?scheme ?label
        LIMIT {{limit}}
        """;
    }

    internal static IReadOnlyDictionary<string, IReadOnlyList<PublisherMetadataRecord>> ParseSubjects(
        IReadOnlyList<Dictionary<string, string>> rows)
    {
        var result = new Dictionary<string, IReadOnlyList<PublisherMetadataRecord>>(
            StringComparer.Ordinal);
        foreach (var workGroup in rows.GroupBy(RequiredWork, StringComparer.Ordinal))
        {
            var values = new List<PublisherMetadataRecord>();
            foreach (var subjectGroup in workGroup.GroupBy(row =>
                         (Level: Required(row, "level"), Subject: RequiredUri(row, "subject"))))
            {
                if (subjectGroup.Key.Level is not ("1" or "2"))
                    throw new InvalidDataException(
                        $"Legilux subject {subjectGroup.Key.Subject} has unsupported level '{subjectGroup.Key.Level}'.");
                var labels = subjectGroup.Select(row => row.GetValueOrDefault("label"))
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value!.Trim()).Distinct(StringComparer.Ordinal).ToArray();
                if (labels.Length != 1)
                    throw new InvalidDataException(
                        $"Legilux subject {subjectGroup.Key.Subject} must have exactly one French prefLabel.");

                var specific = new HashSet<string>(StringComparer.Ordinal);
                foreach (var scheme in subjectGroup.Select(row => row.GetValueOrDefault("scheme"))
                             .Where(value => !string.IsNullOrWhiteSpace(value))
                             .Distinct(StringComparer.Ordinal))
                {
                    if (Schemes.TryGetValue(scheme!, out var suffix)) specific.Add(suffix);
                    else if (!string.Equals(scheme,
                                 "http://data.legilux.public.lu/resource/authority/legal-subject",
                                 StringComparison.Ordinal))
                        throw new InvalidDataException(
                            $"Legilux subject {subjectGroup.Key.Subject} has unknown scheme '{scheme}'.");
                }
                if (specific.Count != 1)
                    throw new InvalidDataException(
                        $"Legilux subject {subjectGroup.Key.Subject} must have exactly one specific scheme.");
                values.Add(new PublisherMetadataRecord(
                    $"legilux_subject_level{subjectGroup.Key.Level}_{specific.Single()}",
                    subjectGroup.Key.Subject, "fr", labels[0], subjectGroup.Key.Subject));
            }

            var canonical = values.Distinct().OrderBy(value => value.Kind, StringComparer.Ordinal)
                .ThenBy(value => value.Identifier, StringComparer.Ordinal)
                .ThenBy(value => value.Language, StringComparer.Ordinal)
                .ThenBy(value => value.Label, StringComparer.Ordinal).ToArray();
            if (canonical.Length > 512)
                throw new InvalidDataException(
                    $"Legilux work {workGroup.Key} exceeds 512 publisher metadata records.");
            result[workGroup.Key] = canonical;
        }
        return result;
    }

    private static string RequiredWork(Dictionary<string, string> row) => RequiredUri(row, "work");
    private static string RequiredUri(Dictionary<string, string> row, string field)
    {
        var value = Required(row, field);
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new InvalidDataException($"Legilux {field} must be an absolute HTTP(S) URI.");
        return value;
    }
    private static string Required(Dictionary<string, string> row, string field) =>
        row.TryGetValue(field, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidDataException($"Legilux subject row is missing {field}.");
}

internal static class LegiluxOfficialIdentities
{
    private const string Prefixes = """
        PREFIX owl: <http://www.w3.org/2002/07/owl#>
        """;

    internal static string Query(IReadOnlyCollection<string> works)
    {
        var values = LegiluxAdapter.HeldWorkValues(works);
        var limit = checked(works.Count * LegiluxAdapter.IdentityRowsPerWorkMaximum + 1);
        return Prefixes + $$"""
        SELECT DISTINCT ?work ?identifier WHERE {
          VALUES ?work { {{values}} }
          { ?work owl:sameAs ?identifier }
          UNION
          { ?identifier owl:sameAs ?work }
          FILTER(?identifier != ?work)
        } ORDER BY ?work ?identifier
        LIMIT {{limit}}
        """;
    }

    internal static IReadOnlyDictionary<string, IReadOnlyList<PublisherMetadataRecord>> Parse(
        IReadOnlyList<Dictionary<string, string>> rows)
    {
        var result = new Dictionary<string, IReadOnlyList<PublisherMetadataRecord>>(
            StringComparer.Ordinal);
        foreach (var group in rows.GroupBy(row => RequiredUri(row, "work"), StringComparer.Ordinal))
        {
            var values = group.Select(row => RequiredUri(row, "identifier"))
                .Where(identifier => !string.Equals(identifier, group.Key, StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .Select(identifier => new PublisherMetadataRecord(
                    "legilux_same_as", identifier, null, AliasLabel(identifier), identifier))
                .OrderBy(value => value.Identifier, StringComparer.Ordinal).ToArray();
            result[group.Key] = values;
        }
        return result;
    }

    private static string AliasLabel(string uri)
    {
        const string marker = "/eli/etat/leg/";
        var index = uri.IndexOf(marker, StringComparison.Ordinal);
        var value = index >= 0 ? uri[(index + marker.Length)..] : new Uri(uri).AbsolutePath.Trim('/');
        return value.Trim('/').Replace('/', '-');
    }

    private static string RequiredUri(Dictionary<string, string> row, string field)
    {
        if (!row.TryGetValue(field, out var value)
            || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
            throw new InvalidDataException($"Legilux sameAs row has invalid {field}.");
        return value;
    }
}
