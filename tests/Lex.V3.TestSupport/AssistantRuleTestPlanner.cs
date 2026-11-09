using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Lex.V3.Contracts.Platform.Assistant;

namespace Lex.V3.TestSupport;

/// <summary>
/// A deterministic planner for the tests, and nothing else: it reads the masked question with a few fixed rules and writes
/// a plan of the grammar (docs/ANSWER-DOSSIER-1.md §5). It stands where a model would plan, so the pipeline, the validator,
/// the gates and the replay can be exercised without one. It lives in the tests only: src holds no planner (R30, R33).
/// </summary>
/// <remarks>
/// The rules: split once at "; ", ", et " or ", and " when both halves hold a word; in each part, search a citation of an
/// article of an act (or an address of this site) when one is written, else the words inside quotation marks, else the
/// part's own words; read the date a reference names outside the citation, else today; and follow a search that names
/// exactly one article with that article's text. It proposes <c>answer</c> for every part and leaves the verdict to code.
/// </remarks>
public sealed class AssistantRuleTestPlanner : IAssistantPlanner
{
    private static readonly Regex Citation = new(
        @"(?:/lu-legilux/[^\s?]+)|(?:\b(?:article|art\.)\s+(?:1er|premier|[0-9A-Z][\w.\-]*)\s+(?:de\s+la\s+|du\s+|of\s+the\s+|of\s+)?(?:loi|law|règlement)\b[^,?;]*?\b\d{4}\b)",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex Quoted = new("[«\"“]\\s*([^»\"”]{2,})\\s*[»\"”]", RegexOptions.CultureInvariant);

    public string PlannerId => "rule-test-planner/1";

    /// <summary>The cost each plan reports, in micro-euros, so the budget can be exercised.</summary>
    public long CostMicroEuros { get; init; } = 1_000;

    /// <summary>How many plans this planner wrote.</summary>
    public int Calls { get; private set; }

    public V3PlannerOutput Plan(V3PlannerInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        Calls++;
        var question = input.MaskedQuestion;
        var parts = SplitParts(question);
        var json = new StringBuilder();
        json.Append("{\"schema\":\"lex-v3-assistant-plan/1\",\"parts\":[");
        json.AppendJoin(',', parts.Select((part, at) =>
            $"{{\"part_id\":\"p{(at + 1).ToString(CultureInfo.InvariantCulture)}\",\"span\":[{part.Start.ToString(CultureInfo.InvariantCulture)},{part.End.ToString(CultureInfo.InvariantCulture)}],\"proposed_verdict\":\"answer\"}}"));
        json.Append("],\"steps\":[");
        var steps = new List<string>();
        var followUps = new List<string>();
        var language = input.SearchLanguages.Contains("fra") ? "fra" : input.SearchLanguages.FirstOrDefault() ?? "fra";
        for (var at = 0; at < parts.Count; at++)
        {
            var (start, end) = parts[at];
            var (queryStart, queryEnd) = QueryOf(question, start, end);
            var date = DateOf(input, start, end, queryStart, queryEnd);
            var stepId = "s" + (steps.Count + 1).ToString(CultureInfo.InvariantCulture);
            steps.Add($"{{\"step_id\":\"{stepId}\",\"part_id\":\"p{(at + 1).ToString(CultureInfo.InvariantCulture)}\",\"operation_id\":\"search\",\"arguments\":{{\"query\":{{\"span\":[{queryStart.ToString(CultureInfo.InvariantCulture)},{queryEnd.ToString(CultureInfo.InvariantCulture)}]}},\"language\":{{\"choice\":\"{language}\"}},\"date\":{date}}}}}");
            followUps.Add($"{{\"step_id\":\"f{(followUps.Count + 1).ToString(CultureInfo.InvariantCulture)}\",\"after\":\"{stepId}\",\"condition\":\"on_single_article\",\"operation_id\":\"evidence_bundle\",\"arguments\":{{\"identifier\":{{\"result\":\"{stepId}\",\"pick\":\"sole_work\"}},\"article\":{{\"result\":\"{stepId}\",\"pick\":\"sole_article\"}},\"date\":{date}}}}}");
        }

        json.AppendJoin(',', steps);
        json.Append("],\"follow_ups\":[");
        json.AppendJoin(',', followUps);
        json.Append("]}");
        return new V3PlannerOutput(json.ToString(), CostMicroEuros);
    }

    private static List<(int Start, int End)> SplitParts(string question)
    {
        foreach (var separator in new[] { "; ", ", et ", ", and " })
        {
            var at = question.IndexOf(separator, StringComparison.OrdinalIgnoreCase);
            if (at > 0 && question[..at].Any(char.IsLetter) && question[(at + separator.Length)..].Any(char.IsLetter))
            {
                return [Trim(question, 0, at), Trim(question, at + separator.Length, question.Length)];
            }
        }

        return [Trim(question, 0, question.Length)];
    }

    private static (int Start, int End) Trim(string text, int start, int end)
    {
        while (start < end && (char.IsWhiteSpace(text[start]) || text[start] == '•'))
        {
            start++;
        }

        while (end > start && (char.IsWhiteSpace(text[end - 1]) || text[end - 1] is '?' or '.' or '!' or ' ' or '•'))
        {
            end--;
        }

        return (start, end);
    }

    private static (int Start, int End) QueryOf(string question, int start, int end)
    {
        var part = question[start..end];
        var citation = Citation.Match(part);
        if (citation.Success)
        {
            return (start + citation.Index, start + citation.Index + citation.Length);
        }

        var quoted = Quoted.Match(part);
        if (quoted.Success)
        {
            return Trim(question, start + quoted.Groups[1].Index, start + quoted.Groups[1].Index + quoted.Groups[1].Length);
        }

        // The part's own words, never past a masked run, and within the grammar's 400 characters.
        var masked = part.IndexOf('•', StringComparison.Ordinal);
        var stop = masked >= 0 ? start + masked : end;
        var (from, to) = Trim(question, start, stop);
        return (from, Math.Min(to, from + 400));
    }

    private static string DateOf(V3PlannerInput input, int start, int end, int queryStart, int queryEnd)
    {
        var reference = input.References.FirstOrDefault(candidate =>
            candidate.Kind == "date" && candidate.Start >= start && candidate.End <= end && (candidate.End <= queryStart || candidate.Start >= queryEnd));
        return reference is null ? "{\"today\":true}" : $"{{\"ref\":\"{reference.Ref}\"}}";
    }
}
