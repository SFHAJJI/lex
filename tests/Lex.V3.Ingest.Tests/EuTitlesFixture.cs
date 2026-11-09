namespace Lex.V3.Ingest.Tests;

/// <summary>
/// Family X's title rows for an act's expressions (EU index /7, Release 3's item 1), in the shape EU run 16's custody holds
/// them: each <c>expression_title</c> a plain literal and each <c>expression_title_short</c> one typed <c>xsd:string</c>, the
/// datatype column <c>xsd:string</c> and no language tag. The GDPR's are the literals run 16 holds for 32016R0679's English
/// and French expressions (publisher metadata, no text), set on this fixture's own expression IRIs.
/// </summary>
internal static class EuTitlesFixture
{
    internal const string EnglishTitle =
        "Regulation (EU) 2016/679 of the European Parliament and of the Council of 27 April 2016 on the protection of natural persons " +
        "with regard to the processing of personal data and on the free movement of such data, and repealing Directive 95/46/EC " +
        "(General Data Protection Regulation) (Text with EEA relevance)";

    internal const string EnglishShortTitle = "gdpr, personal data, personal data protection";

    internal const string FrenchTitle =
        "Règlement (UE) 2016/679 du Parlement européen et du Conseil du 27 avril 2016 relatif à la protection des personnes physiques " +
        "à l'égard du traitement des données à caractère personnel et à la libre circulation de ces données, et abrogeant la directive " +
        "95/46/CE (règlement général sur la protection des données) (Texte présentant de l'intérêt pour l'EEE)";

    internal const string FrenchShortTitle = "rgdp, GDPR, Protection des données à caractère personnel";

    /// <summary>The GDPR's titles on <paramref name="english"/> and, when given, <paramref name="french"/>.</summary>
    internal static IReadOnlyList<EuAxiomWiringHarness.ExpressionTitle> Gdpr(string english, string? french) =>
    [
        new(english, EuAcquisitionTestFixture.ExpressionTitle, EnglishTitle),
        new(english, EuAcquisitionTestFixture.ExpressionTitleShort, EnglishShortTitle, Typed: true),
        .. french is null
            ? Array.Empty<EuAxiomWiringHarness.ExpressionTitle>()
            :
            [
                new(french, EuAcquisitionTestFixture.ExpressionTitle, FrenchTitle),
                new(french, EuAcquisitionTestFixture.ExpressionTitleShort, FrenchShortTitle, Typed: true),
            ],
    ];
}
