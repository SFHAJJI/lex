using System.Text.RegularExpressions;

namespace Lex.Ask;

internal enum AgentEvidenceKind
{
    Pointer,
    LegalText,
    Timeline,
    Change,
    Ranking,
    Coverage,
    Provenance,
}

internal enum AgentClaimKind
{
    LegalText,
    Timeline,
    Change,
    Ranking,
    Coverage,
    Provenance,
}

internal enum AgentAnswerStatus
{
    Answer,
    Gap,
    Clarify,
}

internal sealed record AgentEvidence(
    string Id,
    AgentEvidenceKind Kind,
    string? Work,
    string? Anchor,
    string? Date,
    string? TextSha256,
    string? Permalink,
    bool RequiresCoverageDisclosure = false);

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

internal static class AgentAnswerContract
{
    private static readonly Regex Url = new(@"https://[^\s<>()]+", RegexOptions.CultureInvariant);

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
            if (options.Length is < 2 or > 4)
                throw new InvalidDataException("A clarification requires two to four distinct options.");
            return draft with
            {
                Answer = answer,
                Clarification = new AgentClarification(question, options),
            };
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
            return new AgentClaim(text, claim.Kind, ids);
        }).ToArray();

        if (draft.Status == AgentAnswerStatus.Gap && claims.Any(claim => claim.Kind != AgentClaimKind.Coverage))
            throw new InvalidDataException("A corpus gap may make coverage claims only.");

        var allowedLinks = used.Select(item => item.Permalink)
            .Where(link => link is not null).Select(link => link!).ToHashSet(StringComparer.Ordinal);
        var links = draft.Permalinks.Select(link => Bounded(link, 2_000, "permalink"))
            .Distinct(StringComparer.Ordinal).ToArray();
        if (links.Length != draft.Permalinks.Count
            || links.Any(link => !Uri.TryCreate(link, UriKind.Absolute, out var uri)
                                 || uri.Scheme != Uri.UriSchemeHttps || !allowedLinks.Contains(link))
            || Url.Matches(answer).Select(match => match.Value.TrimEnd('.', ',', ';', ':'))
                .Any(link => !allowedLinks.Contains(link)))
            throw new InvalidDataException("Every answer link must exactly match returned evidence.");

        var disclosureRequired = claims.Any(claim => claim.Kind == AgentClaimKind.Coverage)
                                 || used.Any(item => item.RequiresCoverageDisclosure);
        var disclosure = draft.CoverageDisclosure?.Trim();
        if (disclosureRequired)
            disclosure = Bounded(disclosure, 1_000, "coverage disclosure");
        else if (disclosure is not null)
            disclosure = Bounded(disclosure, 1_000, "coverage disclosure");

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

    private static string Bounded(string? value, int maximum, string field)
    {
        var bounded = value?.Trim() ?? "";
        if (bounded.Length is 0 || bounded.Length > maximum)
            throw new InvalidDataException($"{field} must contain 1 to {maximum} characters.");
        return bounded;
    }
}
