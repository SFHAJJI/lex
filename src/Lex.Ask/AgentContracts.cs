using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;

namespace Lex.Ask;

[JsonConverter(typeof(JsonStringEnumConverter))]
internal enum AgentEvidenceKind
{
    [JsonStringEnumMemberName("pointer")]
    Pointer,
    [JsonStringEnumMemberName("legal_text")]
    LegalText,
    [JsonStringEnumMemberName("timeline")]
    Timeline,
    [JsonStringEnumMemberName("change")]
    Change,
    [JsonStringEnumMemberName("ranking")]
    Ranking,
    [JsonStringEnumMemberName("coverage")]
    Coverage,
    [JsonStringEnumMemberName("provenance")]
    Provenance,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
internal enum AgentClaimKind
{
    [JsonStringEnumMemberName("legal_text")]
    LegalText,
    [JsonStringEnumMemberName("timeline")]
    Timeline,
    [JsonStringEnumMemberName("change")]
    Change,
    [JsonStringEnumMemberName("ranking")]
    Ranking,
    [JsonStringEnumMemberName("coverage")]
    Coverage,
    [JsonStringEnumMemberName("provenance")]
    Provenance,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
internal enum AgentAnswerStatus
{
    [JsonStringEnumMemberName("answer")]
    Answer,
    [JsonStringEnumMemberName("gap")]
    Gap,
    [JsonStringEnumMemberName("clarify")]
    Clarify,
    [JsonStringEnumMemberName("refusal")]
    Refusal,
}

internal sealed record AgentEvidence(
    string Id,
    AgentEvidenceKind Kind,
    string? Work,
    string? Anchor,
    string? Date,
    string? TextSha256,
    string? Permalink,
    bool RequiresCoverageDisclosure = false,
    string? Title = null,
    string? Excerpt = null);

internal sealed record AgentClaim(
    string Text,
    AgentClaimKind Kind,
    IReadOnlyList<string> EvidenceIds);

internal sealed record AgentClarification(string Question, IReadOnlyList<string> Options);

internal sealed record AgentAnswerDraft(
    AgentAnswerStatus Status,
    string Answer,
    IReadOnlyList<AgentClaim> Claims,
    IReadOnlyList<string> Permalinks,
    string? CoverageDisclosure,
    AgentClarification? Clarification);

[JsonConverter(typeof(JsonStringEnumConverter))]
internal enum AgentJudgmentDisposition
{
    [JsonStringEnumMemberName("pass")]
    Pass,
    [JsonStringEnumMemberName("repair")]
    Repair,
    [JsonStringEnumMemberName("refuse")]
    Refuse,
}

internal sealed record AgentGroundingJudgment(
    AgentJudgmentDisposition Disposition,
    IReadOnlyList<string> Issues,
    AgentAnswerDraft? Replacement);

internal static class AgentAnswerContract
{
    private static readonly Regex Url = new(@"https?://[^\s<>()]+",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex NumericFact = new(
        @"(?<![\p{L}\p{N}])\d{2,}(?:[-./]\d+)*(?![\p{L}\p{N}])",
        RegexOptions.CultureInvariant);
    private static readonly Regex ArticleFact = new(
        @"\b(?:article|articles|art)\s*[._-]?\s*(?<number>\d+[a-z]?)\b",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex SamePolarity = new(
        @"\b(?:same wording|unchanged|has not changed|have not changed|did not change|no change|identical|meme libelle|inchangee?s?|n a pas change|n ont pas change|aucun changement|identique)\b",
        RegexOptions.CultureInvariant);
    private static readonly Regex AddedPolarity = new(
        @"\b(?:added|ajoutee?s?|present only on the later date|present uniquement a la date ulterieure)\b",
        RegexOptions.CultureInvariant);
    private static readonly Regex RemovedPolarity = new(
        @"\b(?:removed|supprimee?s?|present only on the earlier date|present uniquement a la date anterieure)\b",
        RegexOptions.CultureInvariant);
    private static readonly Regex ChangedPolarity = new(
        @"\b(?:changed|has changed|have changed|different wording|wording changed|a change|ont change|modifiee?s?|libelle different)\b",
        RegexOptions.CultureInvariant);

    public static AgentAnswerDraft Validate(
        AgentAnswerDraft draft,
        IReadOnlyList<AgentEvidence> evidence)
    {
        var answer = Bounded(draft.Answer, 6_000, "answer");
        var evidenceById = evidence.ToDictionary(
            item => Bounded(item.Id, 200, "evidence id"), StringComparer.Ordinal);

        if (draft.Status == AgentAnswerStatus.Clarify)
        {
            if (draft.Clarification is null || draft.Claims.Count != 0 || draft.Permalinks.Count != 0
                || draft.CoverageDisclosure is not null)
                throw new InvalidDataException("A clarification contains one question and no claims or citations.");
            var question = Bounded(draft.Clarification.Question, 280, "clarification question");
            var options = draft.Clarification.Options.Select(option => Bounded(option, 100, "clarification option"))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (options.Length is 1 or > 4)
                throw new InvalidDataException(
                    "A clarification has no options for free-text input, or two to four choices.");
            if (ContainsUrl(answer) || ContainsUrl(question) || options.Any(ContainsUrl))
                throw new InvalidDataException("A clarification cannot contain links because it has no evidence.");
            return draft with
            {
                Answer = answer,
                Clarification = new AgentClarification(question, options),
            };
        }

        if (draft.Status == AgentAnswerStatus.Refusal)
        {
            if (draft.Clarification is not null || draft.Claims.Count != 0 || draft.Permalinks.Count != 0
                || draft.CoverageDisclosure is not null)
                throw new InvalidDataException("An evidence-limited refusal contains no claims or citations.");
            if (ContainsUrl(answer))
                throw new InvalidDataException("An evidence-limited refusal cannot contain links.");
            return draft with { Answer = answer };
        }

        if (draft.Clarification is not null || draft.Claims.Count == 0)
            throw new InvalidDataException("An answer or gap requires typed claims and no clarification.");

        var used = new HashSet<AgentEvidence>();
        var claims = draft.Claims.Select(claim =>
        {
            var text = Bounded(claim.Text, 2_000, "claim");
            var ids = claim.EvidenceIds.Select(id => Bounded(id, 200, "claim evidence id"))
                .Distinct(StringComparer.Ordinal).ToArray();
            if (ids.Length == 0)
                throw new InvalidDataException("Every claim requires evidence.");
            foreach (var id in ids)
            {
                if (!evidenceById.TryGetValue(id, out var item) || !Supports(claim.Kind, item.Kind))
                    throw new InvalidDataException("A claim is not supported by evidence of the required type.");
                used.Add(item);
            }
            ValidateClaimContent(text, claim.Kind, ids.Select(id => evidenceById[id]).ToArray());
            return new AgentClaim(text, claim.Kind, ids);
        }).ToArray();

        if (draft.Status == AgentAnswerStatus.Gap && claims.Any(claim => claim.Kind != AgentClaimKind.Coverage))
            throw new InvalidDataException("A corpus gap may make coverage claims only.");

        var disclosureRequired = claims.Any(claim => claim.Kind == AgentClaimKind.Coverage)
                                 || used.Any(item => item.RequiresCoverageDisclosure);
        var disclosure = draft.CoverageDisclosure?.Trim();
        if (disclosureRequired)
            disclosure = Bounded(disclosure, 1_000, "coverage disclosure");
        else if (disclosure is not null)
            disclosure = Bounded(disclosure, 1_000, "coverage disclosure");

        var allowedLinks = used.Select(item => item.Permalink)
            .Where(link => link is not null && IsHttpsAbsolute(link)).Select(link => link!)
            .ToHashSet(StringComparer.Ordinal);
        var links = draft.Permalinks.Select(link => Bounded(link, 2_000, "permalink"))
            .Distinct(StringComparer.Ordinal).ToArray();
        if (links.Length != draft.Permalinks.Count
            || links.Any(link => !IsHttpsAbsolute(link) || !allowedLinks.Contains(link))
            || LinksIn(answer).Concat(LinksIn(disclosure)).Any(link => !allowedLinks.Contains(link)))
            throw new InvalidDataException("Every rendered link must exactly match returned evidence.");

        return draft with
        {
            Answer = answer,
            Claims = claims,
            Permalinks = links,
            CoverageDisclosure = disclosure,
        };
    }

    private static bool Supports(AgentClaimKind claim, AgentEvidenceKind evidence) => claim switch
    {
        AgentClaimKind.LegalText => evidence == AgentEvidenceKind.LegalText,
        AgentClaimKind.Timeline => evidence == AgentEvidenceKind.Timeline,
        AgentClaimKind.Change => evidence == AgentEvidenceKind.Change,
        AgentClaimKind.Ranking => evidence == AgentEvidenceKind.Ranking,
        AgentClaimKind.Coverage => evidence == AgentEvidenceKind.Coverage,
        AgentClaimKind.Provenance => evidence == AgentEvidenceKind.Provenance,
        _ => false,
    };

    private static void ValidateClaimContent(
        string text,
        AgentClaimKind kind,
        IReadOnlyList<AgentEvidence> evidence)
    {
        var evidenceText = string.Join(" ", evidence.Select(item => string.Join(" ",
            new[] { item.Work, item.Anchor, item.Date, item.TextSha256, item.Title, item.Excerpt }
                .Where(value => !string.IsNullOrWhiteSpace(value)))));
        foreach (Match fact in NumericFact.Matches(text))
            if (!evidenceText.Contains(fact.Value, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    "A claim contains a numeric or dated fact absent from its cited evidence.");
        foreach (Match article in ArticleFact.Matches(text))
        {
            var number = article.Groups["number"].Value;
            if (!ArticleFact.Matches(evidenceText).Any(candidate =>
                    string.Equals(candidate.Groups["number"].Value, number,
                        StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException(
                    "A claim names an article absent from its cited evidence.");
        }

        if (kind != AgentClaimKind.Change) return;
        var facts = evidence.Select(ChangeFacts).Where(value => value is not null)
            .Select(value => value!.Value).ToArray();
        if (facts.Length == 0) return;
        var normalized = Fold(text);
        var same = SamePolarity.IsMatch(normalized);
        var added = AddedPolarity.IsMatch(normalized);
        var removed = RemovedPolarity.IsMatch(normalized);
        var withoutSame = SamePolarity.Replace(normalized, " ");
        var changed = ChangedPolarity.IsMatch(withoutSame);
        if (new[] { same, added, removed, changed }.Count(value => value) != 1)
            throw new InvalidDataException(
                "A change claim must use one canonical, unambiguous change polarity.");
        var supported = same ? facts.Any(fact => fact.Changed == false)
            : added ? facts.Any(fact => fact.FromPresent == false && fact.ToPresent == true)
            : removed ? facts.Any(fact => fact.FromPresent == true && fact.ToPresent == false)
            : changed ? facts.Any(fact => fact.Changed == true)
            : true;
        if (!supported)
            throw new InvalidDataException(
                "A change claim contradicts the polarity of its cited evidence.");
    }

    private static string Fold(string value)
    {
        var decomposed = value.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var result = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                result.Append(character);
        return Regex.Replace(result.ToString().Normalize(NormalizationForm.FormC),
            @"[^\p{L}\p{N}]+", " ").Trim();
    }

    private static (bool? Changed, bool? FromPresent, bool? ToPresent)? ChangeFacts(
        AgentEvidence evidence)
    {
        if (string.IsNullOrWhiteSpace(evidence.Excerpt)) return null;
        try
        {
            if (JsonNode.Parse(evidence.Excerpt) is not JsonObject value) return null;
            var equal = value["anchor_text_equal"]?.GetValue<bool?>();
            return (
                value["changed"]?.GetValue<bool?>() ?? (equal is null ? null : !equal),
                value["anchor_from_present"]?.GetValue<bool?>(),
                value["anchor_to_present"]?.GetValue<bool?>());
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static bool ContainsUrl(string value) => Url.IsMatch(value);

    private static bool IsHttpsAbsolute(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> LinksIn(string? value) => value is null
        ? []
        : Url.Matches(value).Select(match => match.Value.TrimEnd('.', ',', ';', ':'));

    private static string Bounded(string? value, int maximum, string field)
    {
        var bounded = value?.Trim() ?? "";
        if (bounded.Length is 0 || bounded.Length > maximum)
            throw new InvalidDataException($"{field} must contain 1 to {maximum} characters.");
        return bounded;
    }
}

internal static class AgentGroundingJudgmentContract
{
    public static AgentGroundingJudgment Validate(
        AgentGroundingJudgment judgment,
        AgentAnswerDraft draft,
        IReadOnlyList<AgentEvidence> evidence)
    {
        AgentAnswerContract.Validate(draft, evidence);
        var issues = judgment.Issues.Select(issue => issue.Trim())
            .Where(issue => issue.Length > 0).Distinct(StringComparer.Ordinal).Take(5).ToArray();
        return judgment.Disposition switch
        {
            AgentJudgmentDisposition.Pass when issues.Length == 0 && judgment.Replacement is null =>
                judgment with { Issues = [] },
            AgentJudgmentDisposition.Repair when issues.Length > 0 && judgment.Replacement is not null =>
                judgment with
                {
                    Issues = issues,
                    Replacement = AgentAnswerContract.Validate(judgment.Replacement, evidence),
                },
            AgentJudgmentDisposition.Refuse when issues.Length > 0 && judgment.Replacement is null =>
                judgment with { Issues = issues },
            _ => throw new InvalidDataException("Grounding judgment violates the pass, repair, or refusal contract."),
        };
    }
}
