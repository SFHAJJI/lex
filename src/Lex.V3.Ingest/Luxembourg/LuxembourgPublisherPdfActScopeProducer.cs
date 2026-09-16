using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace Lex.V3.Ingest.Luxembourg;

/// <summary>The publisher coordinate scope proven for one publisher-PDF text-layer member.</summary>
public enum LuxembourgPublisherPdfActScopeDisposition
{
    [JsonStringEnumMemberName("not_applicable")]
    NotApplicable = 1,

    [JsonStringEnumMemberName("gazette_issue_scope")]
    GazetteIssueScope = 2,

    [JsonStringEnumMemberName("typed_gap")]
    TypedGap = 3,
}

/// <summary>Why an exact publisher-PDF member has no usable act-scope coordinate.</summary>
public enum LuxembourgPublisherPdfActScopeGapReason
{
    [JsonStringEnumMemberName("upstream_text_layer_gap")]
    UpstreamTextLayerGap = 1,

    [JsonStringEnumMemberName("act_scope_unproven")]
    ActScopeUnproven = 2,
}

/// <summary>
/// One publisher-PDF member's coordinate scope. This value says where the publisher placed the
/// item. It makes no claim that PDF glyphs are legal wording or that an issue has been split.
/// </summary>
public sealed class LuxembourgPublisherPdfActScopeOutcome
{
    internal LuxembourgPublisherPdfActScopeOutcome(
        LuxembourgPublisherPdfTextLayerOutcome sourceTextLayer,
        LuxembourgPublisherPdfActScopeDisposition disposition,
        LuxembourgPublisherPdfActScopeGapReason? gapReason,
        string ruleProfileSha256)
    {
        SourceTextLayer = sourceTextLayer ?? throw new ArgumentNullException(nameof(sourceTextLayer));
        Disposition = disposition;
        GapReason = gapReason;
        RuleProfileSha256 = ruleProfileSha256;
        var eligibility = sourceTextLayer.SourceLayoutEvidence.SourceEligibility;
        PublisherExpressionIri = eligibility.PublisherExpressionIri;
        PublisherManifestationIri = eligibility.PublisherManifestationIri;
        PublisherItemIri = eligibility.PublisherItemIri;
        SemanticIdentitySha256 = Digest(string.Join(
            '\n',
            "lex-v3-luxembourg-publisher-pdf-act-scope-outcome/1",
            ruleProfileSha256,
            sourceTextLayer.SemanticIdentitySha256,
            PublisherExpressionIri,
            PublisherManifestationIri,
            PublisherItemIri,
            ((int)disposition).ToString(CultureInfo.InvariantCulture),
            gapReason is { } gap
                ? ((int)gap).ToString(CultureInfo.InvariantCulture)
                : string.Empty));
    }

    public LuxembourgPublisherPdfTextLayerOutcome SourceTextLayer { get; }

    public string PublisherExpressionIri { get; }

    public string PublisherManifestationIri { get; }

    public string PublisherItemIri { get; }

    public LuxembourgPublisherPdfActScopeDisposition Disposition { get; }

    public LuxembourgPublisherPdfActScopeGapReason? GapReason { get; }

    public string RuleProfileSha256 { get; }

    public string SemanticIdentitySha256 { get; }

    private static string Digest(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

/// <summary>Exactly one coordinate-scope outcome per exact text-layer member.</summary>
public sealed class LuxembourgPublisherPdfActScopePopulation
{
    private LuxembourgPublisherPdfActScopePopulation(
        LuxembourgPublisherPdfTextLayerPopulation sourceTextLayerPopulation,
        IReadOnlyList<LuxembourgPublisherPdfActScopeOutcome> outcomes,
        string ruleProfileSha256)
    {
        SourceTextLayerPopulation = sourceTextLayerPopulation;
        Outcomes = Array.AsReadOnly(outcomes.ToArray());
        RuleProfileSha256 = ruleProfileSha256;
        IdentitySha256 = Digest(string.Join(
            '\n',
            new[]
            {
                "lex-v3-luxembourg-publisher-pdf-act-scope-population/1",
                ruleProfileSha256,
                sourceTextLayerPopulation.IdentitySha256,
            }.Concat(Outcomes.Select(static outcome => outcome.SemanticIdentitySha256))));
    }

    public LuxembourgPublisherPdfTextLayerPopulation SourceTextLayerPopulation { get; }

    public IReadOnlyList<LuxembourgPublisherPdfActScopeOutcome> Outcomes { get; }

    public string RuleProfileSha256 { get; }

    public string IdentitySha256 { get; }

    internal static LuxembourgPublisherPdfActScopePopulation Create(
        LuxembourgPublisherPdfTextLayerPopulation source,
        IReadOnlyList<LuxembourgPublisherPdfActScopeOutcome> outcomes,
        string ruleProfileSha256)
    {
        if (outcomes.Count != source.Outcomes.Count
            || outcomes.Where((outcome, index) =>
                !ReferenceEquals(outcome.SourceTextLayer, source.Outcomes[index])).Any())
        {
            throw new InvalidOperationException(
                "Every publisher-PDF text-layer member needs exactly one ordered act-scope outcome.");
        }

        return new(source, outcomes, ruleProfileSha256);
    }

    private static string Digest(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

/// <summary>
/// Classifies only the exact publisher coordinate already proven by the acquisition chain. No
/// caller-supplied package map, PDF text, title, date, article or page boundary enters this door.
/// </summary>
public static class LuxembourgPublisherPdfActScopeProducer
{
    private const string RuleProfile =
        "lex-v3-luxembourg-publisher-pdf-act-scope-rule/1\n" +
        "source=exact-luxembourg-publisher-pdf-text-layer-population/1\n" +
        "gazette-issue=/filestore/eli/etat/{leg|adm}/memorial/=>issue-scope;act-splitting-required\n" +
        "gazette-issue-scope-does-not-prove-an-act-boundary\n" +
        "all-other-admitted-publisher-pdf=typed-gap:act-scope-unproven\n" +
        "semantics=publisher-coordinate-only;no-pdf-content-or-legal-wording-claim\n";

    private const string LegislativeMemorialPrefix = "/filestore/eli/etat/leg/memorial/";
    private const string AdministrativeMemorialPrefix = "/filestore/eli/etat/adm/memorial/";
    private const string ExpectedResourceHost = "data.legilux.public.lu";

    public static string RuleProfileSha256 { get; } = Digest(RuleProfile);

    public static LuxembourgPublisherPdfActScopePopulation Produce(
        LuxembourgPublisherPdfTextLayerPopulation sourceTextLayerPopulation)
    {
        ArgumentNullException.ThrowIfNull(sourceTextLayerPopulation);
        var outcomes = sourceTextLayerPopulation.Outcomes.Select(Classify).ToArray();
        return LuxembourgPublisherPdfActScopePopulation.Create(
            sourceTextLayerPopulation, outcomes, RuleProfileSha256);
    }

    private static LuxembourgPublisherPdfActScopeOutcome Classify(
        LuxembourgPublisherPdfTextLayerOutcome source)
    {
        if (source.Disposition == LuxembourgPublisherPdfTextLayerDisposition.NotApplicable)
        {
            return Outcome(source, LuxembourgPublisherPdfActScopeDisposition.NotApplicable);
        }

        if (source.Disposition != LuxembourgPublisherPdfTextLayerDisposition.Admitted)
        {
            return Outcome(
                source,
                LuxembourgPublisherPdfActScopeDisposition.TypedGap,
                LuxembourgPublisherPdfActScopeGapReason.UpstreamTextLayerGap);
        }

        var eligibility = source.SourceLayoutEvidence.SourceEligibility;
        var item = new Uri(eligibility.PublisherItemIri, UriKind.Absolute);
        var manifestation = new Uri(eligibility.PublisherManifestationIri, UriKind.Absolute);
        if (!string.Equals(item.Host, ExpectedResourceHost, StringComparison.Ordinal)
            || !string.Equals(manifestation.Host, ExpectedResourceHost, StringComparison.Ordinal))
        {
            return Outcome(
                source,
                LuxembourgPublisherPdfActScopeDisposition.TypedGap,
                LuxembourgPublisherPdfActScopeGapReason.ActScopeUnproven);
        }

        var itemPath = item.AbsolutePath;
        if (itemPath.StartsWith(LegislativeMemorialPrefix, StringComparison.Ordinal)
            || itemPath.StartsWith(AdministrativeMemorialPrefix, StringComparison.Ordinal))
        {
            return Outcome(source, LuxembourgPublisherPdfActScopeDisposition.GazetteIssueScope);
        }

        return Outcome(
            source,
            LuxembourgPublisherPdfActScopeDisposition.TypedGap,
            LuxembourgPublisherPdfActScopeGapReason.ActScopeUnproven);
    }

    private static LuxembourgPublisherPdfActScopeOutcome Outcome(
        LuxembourgPublisherPdfTextLayerOutcome source,
        LuxembourgPublisherPdfActScopeDisposition disposition,
        LuxembourgPublisherPdfActScopeGapReason? gapReason = null) =>
        new(source, disposition, gapReason, RuleProfileSha256);

    private static string Digest(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
