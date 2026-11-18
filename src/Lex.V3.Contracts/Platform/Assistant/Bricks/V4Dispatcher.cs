using System.Text.RegularExpressions;

namespace Lex.V3.Contracts.Platform.Assistant.Bricks;

/// <summary>One activation of the dispatcher: a detector or a model step, on or off, and the rule that says so.</summary>
public sealed record V4Activation(string Id, bool On, string Reason);

/// <summary>What the dispatcher decided: the route, the kind of answer it will look for, the activations, and the rule that chose the route.</summary>
public sealed record V4Decision(string Route, string Kind, IReadOnlyList<V4Activation> Activations, string Reason);

/// <summary>What the dispatcher reads: the parsed row, and the facts of the corpus in scope. Nothing else decides.</summary>
/// <param name="Application">The question asks to apply the law to a case (R15's cues, as narrowed by the owner's decision of 2026-11-18).</param>
/// <param name="EuActNotHeld">The question names an EU act of the corpus index that Lex does not hold.</param>
/// <param name="ActHeld">The act in scope has a text Lex can show on the date asked.</param>
/// <param name="ActHasHeadings">The act's articles carry headings (its outline can be searched).</param>
/// <param name="TermDefined">A term the act defines is named in the question.</param>
/// <param name="ModelAvailable">A model is wired for the arbiter and the extraction.</param>
public sealed record V4DispatchInput(
    V4QuestionRow Row,
    bool Application,
    bool EuActNotHeld,
    bool ActHeld,
    bool ActHasHeadings,
    bool TermDefined,
    bool ModelAvailable);

/// <summary>
/// The dispatcher (brick E): one function of code, readable top to bottom. "a piece of code we can read: a dispatcher that
/// turns the parsed question and the document profile into an explicit plan" [13]. The same question on the same data takes
/// the same route, always; no model chooses it. "The series's position: Approach B as default" [6C].
/// </summary>
public static class V4Dispatcher
{
    private const RegexOptions Options = RegexOptions.CultureInvariant | RegexOptions.Compiled;
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(250);

    // A cited article ("l'article 39", "art. 62-3", "art. L. 121-6") or an identifier (a CELEX, an ELI): Lex's reference
    // reader answers these, as it did before the bricks.
    private static readonly Regex CitationPattern = new(
        @"(?<![\p{L}])art(?:icle|\.)?s?\s*(?:\d|1er|premier|l\.\s*\d)|(?<![\p{L}\d])[1-9]\d{4}[a-z]{1,2}\d{3,4}(?![\p{L}\d])|/eli/|eur-lex\.europa\.eu",
        Options, Timeout);

    // A question on how the law changed: Lex's own operations (diff, changes_in_period, timeline) answer these.
    private static readonly Regex ChangePattern = new(
        @"(?<![\p{L}])(?:modifi\w*|change\w*|changement\w*|evolu\w*|historique|versions?|difference\w*|compar\w*|avant et apres|amend\w*|changed|history|differ\w*|abroge\w*|repealed)(?![\p{L}])",
        Options, Timeout);

    /// <summary>Whether the question cites an article or an identifier (route 2: Lex's reference reader).</summary>
    public static bool CitesArticle(V4QuestionRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return CitationPattern.IsMatch(row.Folded);
    }

    /// <summary>Whether the question asks how the law changed over time (route 3: Lex's own operations).</summary>
    public static bool AsksChange(V4QuestionRow row, int dates)
    {
        ArgumentNullException.ThrowIfNull(row);
        return dates >= 2 || ChangePattern.IsMatch(row.Folded);
    }

    /// <summary>
    /// The kind of answer to look for, read from the row: the first kind the question's cues ask for that the scope can
    /// answer; else the usual kind of the first concept; else the passages. A definition is asked of a term the act defines:
    /// a cue for one on a concept the act does not define falls to the next kind.
    /// </summary>
    public static string KindOf(V4QuestionRow row, V4Dictionary dictionary, bool termDefined)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(dictionary);
        foreach (var kind in row.Kinds)
        {
            if (kind.Kind == "definition" && !termDefined && (row.Kinds.Count > 1 || row.Concepts.Count > 0))
            {
                continue;
            }

            return kind.Kind;
        }

        if (row.Kinds.Any(static kind => kind.Kind == "definition"))
        {
            return "definition";
        }

        // A term alone ("personne lésée ?", "note de couverture") asks what it is, when the act defines it.
        if (row.Kinds.Count == 0 && termDefined && row.Concepts.Count == 0 && row.Words.Count <= 5)
        {
            return "definition";
        }

        return row.Concepts.Count > 0 ? dictionary.Concept(row.Concepts[0].Concept)!.UsualAnswer.FirstOrDefault() ?? "text" : "text";
    }

    /// <summary>The route and the activations for a row. The routes are tried the cheapest first.</summary>
    public static V4Decision Decide(V4DispatchInput input, V4Dictionary dictionary)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(dictionary);
        var row = input.Row;
        var kind = KindOf(row, dictionary, input.TermDefined);
        if (row.Greeting)
        {
            return new V4Decision("greeting", kind, [], "question_is_a_greeting");
        }

        if (input.EuActNotHeld)
        {
            return new V4Decision("not_held", kind, [new V4Activation("retrieval", false, "text_not_held")], "eu_act_named_and_not_held");
        }

        if (!input.ActHeld)
        {
            return new V4Decision("absence", kind, [new V4Activation("retrieval", false, "text_not_held")], "text_not_held");
        }

        var hasConcept = row.Concepts.Count > 0 || row.Roles.Count > 0;
        var activations = new List<V4Activation>
        {
            input.ActHasHeadings ? new("title", true, "act_has_headings") : new("title", false, "act_has_no_headings"),
            hasConcept ? new("keyword", true, "row_has_concept") : new("keyword", false, "row_has_no_concept"),
            row.Roles.Count > 0 && row.Concepts.Count > 0 ? new("cooccurrence", true, "row_has_actor") : new("cooccurrence", false, "row_has_no_actor"),
            V4Patterns.HasPattern(kind) ? new("type_pattern", true, "kind_has_pattern") : new("type_pattern", false, "kind_has_no_pattern"),
            row.Roles.Count > 0 ? new("lexicon", true, "row_has_actor") : new("lexicon", false, "row_has_no_actor"),
            kind == "list" ? new("sweep", true, "kind_is_list") : new("sweep", true, "proof_of_absence"),
            new("outline_descent", false, "act_has_no_chapter_levels"),
            row.Language == "en" ? new("embeddings", false, "not_built_question_in_english") : new("embeddings", false, "off_by_default"),
        };

        if (input.Application)
        {
            // Route 1: the reader's own case. Lex's refusal, with the law found by code beside it; no model is asked.
            activations.Add(new("arbiter", false, "own_case"));
            activations.Add(new("extraction", false, "own_case"));
            return new V4Decision("own_case", kind, activations, "application_cue_and_personal_marker");
        }

        if (kind == "definition" && input.TermDefined)
        {
            // Route 4: a term the law defines. "the expert's dictionary decides the route, not the LLM." [9ter]
            activations.Add(new("arbiter", false, "defining_line_known"));
            activations.Add(new("extraction", false, "defining_line_known"));
            return new V4Decision("definition", kind, activations, "term_defined_and_definition_asked");
        }

        var model = input.ModelAvailable;
        activations.Add(model ? new("arbiter", true, "candidates_to_rank") : new("arbiter", false, "no_model_wired"));
        activations.Add(model ? new("extraction", true, kind == "list" ? "all_candidates_together" : "one_candidate_at_a_time") : new("extraction", false, "no_model_wired"));
        return kind switch
        {
            "list" => new V4Decision("list", kind, activations, "kind_is_list"),
            "rule" => new V4Decision("rule", kind, activations, "kind_is_rule"),
            _ => new V4Decision("open", kind, activations, "kind_is_" + kind),
        };
    }
}
