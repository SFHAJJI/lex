using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lex.V3.Contracts.Platform.Assistant.Bricks;

/// <summary>One call to a model: the step it is for, the fixed instruction, the typed input as JSON, and the closed form of the reply.</summary>
public sealed record V4ModelCall(string Step, string System, string User, string SchemaName, string SchemaJson, int MaximumTokens);

/// <summary>What a model answered: its reply as text (JSON of the call's form, untrusted) and what the call cost.</summary>
public sealed record V4ModelReply(string Content, long CostMicroEuros, int InputTokens, int OutputTokens);

/// <summary>
/// The model seam of the bricks (ours, in the shape of their injected callable: "llm_parse is an injected callable."
/// [5octies]). A model fills a typed form and nothing else: "The LLM is a function, not an oracle." [8ter] The one
/// implementation in src is the Azure OpenAI client of Lex.V3.Api; a test hands in recorded replies.
/// </summary>
public interface IV4Model
{
    /// <summary>The model's identity, written into every trace.</summary>
    string ModelId { get; }

    V4ModelReply Complete(V4ModelCall call, CancellationToken cancellationToken);
}

/// <summary>What the arbiter said of one candidate it was given.</summary>
public sealed record V4ArbiterRow(string Id, string Role, string Reason);

/// <summary>One item of an extraction: the line cited, what it is in the answer, and the value's words as the model copied them.</summary>
public sealed record V4ExtractedItem(string Line, string Role, string Value);

/// <summary>The typed answer of the extraction call, before code checks it.</summary>
public sealed record V4Extraction(bool Found, bool Complete, IReadOnlyList<V4ExtractedItem> Items, IReadOnlyList<string> Caveats);

/// <summary>
/// The two model calls behind retrieval (brick I, the arbiter) and before composition (brick J, the extraction): their fixed
/// instructions, their closed forms, and the checks code runs on what comes back (brick G). "Structured output is the start
/// of validation, not the end." [8C] No sentence a model writes is kept: a reply holds identifiers of things it was given,
/// codes of closed lists, and words that code then finds again on the cited line or refuses.
/// </summary>
public static class V4Generation
{
    public static readonly IReadOnlyList<string> ArbiterRoles = ["primary", "supporting", "tangential", "discarded"];

    public static readonly IReadOnlyList<string> ArbiterReasons =
        ["states_rule", "states_exception", "states_case", "states_condition", "gives_value", "defines_term", "lists_items", "procedure_or_form", "other_party", "other_subject", "mentions_only"];

    /// <summary>The caveats a model may set; code may add others of <see cref="V4PlanRecord.Caveats"/>.</summary>
    public static readonly IReadOnlyList<string> ModelCaveats = ["conditions_apply", "left_to_contract", "general_and_special"];

    public const string ArbiterPrompt = """
        lex-v4-arbiter-prompt/1

        You rank passages; you never answer. Lex is a public website that shows Luxembourg and EU legislation exactly as the
        official publishers published it. A person asked the question you are given. Lex's code searched one act and found
        candidate lines. Your only output says, for each candidate you were given, what it is for this question. Nothing you
        write is shown to anyone: you return identifiers and codes, never a sentence.

        ROLES (one per candidate)
        - "primary": the line itself carries the answer asked (the rule, the value, the definition).
        - "supporting": the line states a case, an exception or a condition of the answer.
        - "tangential": the same subject, but it does not help to answer this question.
        - "discarded": another subject.

        REASONS (one per candidate, from this closed list)
        states_rule, states_exception, states_case, states_condition, gives_value, defines_term, lists_items,
        procedure_or_form, other_party, other_subject, mentions_only.

        RULES
        - Return every candidate id you were given exactly once, and no other id.
        - When the question names a party ("actor"), the answer is about that party. A line about another party's right or
          duty is "tangential" with reason "other_party", unless it is an exception to the rule asked.
        - "kind" says what form the answer takes. For kind "rule", the general rule is "primary" and each case in which the
          law allows or forbids it is "supporting". For a kind that is a value (duration, date, amount), a line is "primary"
          only if the value is written on it; if the text gives different values for different cases, each such line is
          "primary".
        - A line that only mentions the subject while regulating something else is "tangential" with reason "mentions_only".
        - Prefer few primaries. Never invent, complete or correct a line.
        """;

    /// <summary>The most articles the outline call may name.</summary>
    public const int MaximumOutlinePicks = 6;

    public const string OutlinePrompt = """
        lex-v4-outline-prompt/2

        You route a question to articles; you never answer it. Lex is a public website that shows legislation exactly as
        published. A person asked the question you are given. You are given the tables of contents of the acts Lex reads on
        this subject: each act with what it covers, and each of its articles by an "id" and its heading (or its first
        words when it has no heading). Your only output names the articles most likely to state the rule that answers the
        question. Nothing you write is shown to anyone: Lex's code then reads those articles.

        RULES
        - Name an article by its "id" (A1, A2, ...), never by its number. Use only ids given. At most six, the best first,
          all from the one act that governs the question.
        - A question worded for a person's own case ("mon assureur", "ma prime", "my claim") is routed like the general
          question behind it: which article states the rule. You never say what applies to that person.
        - If no article given can answer (another subject, another country's law, an act that is not listed, a question
          that is not about what these acts provide), return no article. Never guess.
        - Return no article either when the question is put to Lex itself (what it is, what it holds, what it can do)
          or when it leaves its subject unsaid ("ça", "cela", "this", "it").
        """;

    /// <summary>
    /// The outline call: the question and, for each act given, its summary line and its articles by a short id and their
    /// heading. The second value gives each id's article back. "Trust the TOC" [7C]: the author wrote those titles.
    /// </summary>
    public static (V4ModelCall Call, List<(V4ActTables Tables, V4Article Article)> Ids) OutlineCall(string question, IReadOnlyList<(V4IndexAct Act, V4ActTables Tables)> acts)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(acts);
        var ids = new List<(V4ActTables, V4Article)>();
        var given = new JsonArray();
        foreach (var (act, tables) in acts)
        {
            var articles = new JsonArray();
            foreach (var article in tables.Articles.Where(static article => article.Status != "empty"))
            {
                ids.Add((tables, article));
                var heading = article.Heading;
                if (string.IsNullOrWhiteSpace(heading))
                {
                    var first = tables.TextLinesOf(article).FirstOrDefault()?.Text ?? string.Empty;
                    heading = first.Length > 110 ? first[..110] : first;
                }

                articles.Add(new JsonObject { ["id"] = "A" + ids.Count.ToString(CultureInfo.InvariantCulture), ["heading"] = heading });
            }

            given.Add(new JsonObject { ["act"] = act.SummaryFr, ["articles"] = articles });
        }

        var user = new JsonObject { ["question"] = question, ["acts"] = given };
        return (new V4ModelCall("outline", OutlinePrompt, user.ToJsonString(Relaxed), "lex_v4_outline", OutlineSchema, 120), ids);
    }

    /// <summary>
    /// The outline call over acts a router kept (every law, 2026-11-19): the same instruction, form and bound as
    /// <see cref="OutlineCall"/>, each act by its title and by the articles a pre-filter kept of it, each with the line
    /// shown of it (its heading, else its text around a word of the question). The second value gives each id's article back.
    /// </summary>
    public static (V4ModelCall Call, List<(V4ActTables Tables, V4Article Article)> Ids) OutlineCallOf(string question, IReadOnlyList<(string Title, V4ActTables Tables, IReadOnlyList<V4OutlineArticle> Articles)> acts)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(acts);
        var ids = new List<(V4ActTables, V4Article)>();
        var given = new JsonArray();
        foreach (var (title, tables, kept) in acts)
        {
            var articles = new JsonArray();
            foreach (var entry in kept)
            {
                ids.Add((tables, entry.Article));
                articles.Add(new JsonObject { ["id"] = "A" + ids.Count.ToString(CultureInfo.InvariantCulture), ["heading"] = entry.Brief });
            }

            given.Add(new JsonObject { ["act"] = title, ["articles"] = articles });
        }

        var user = new JsonObject { ["question"] = question, ["acts"] = given };
        return (new V4ModelCall("outline", OutlinePrompt, user.ToJsonString(Relaxed), "lex_v4_outline", OutlineSchema, 120), ids);
    }

    private static string OutlineSchema { get; } = new JsonObject
    {
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["required"] = new JsonArray("articles"),
        ["properties"] = new JsonObject
        {
            ["articles"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } },
        },
    }.ToJsonString();

    /// <summary>
    /// Reads the outline call's reply: the places (from zero) of the articles it names among the <paramref name="given"/>,
    /// each once, in its order, at most <see cref="MaximumOutlinePicks"/>. Null when the reply is not of the form or names
    /// an id that was not given: nothing of it is kept.
    /// </summary>
    public static List<int>? ReadOutline(string content, int given)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("articles", out var articles) || articles.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var picks = new List<int>();
            foreach (var entry in articles.EnumerateArray())
            {
                var id = entry.ValueKind == JsonValueKind.String ? entry.GetString() : null;
                if (id is null || id.Length < 2 || id[0] != 'A' || !int.TryParse(id.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number < 1 || number > given)
                {
                    return null;
                }

                if (!picks.Contains(number - 1) && picks.Count < MaximumOutlinePicks)
                {
                    picks.Add(number - 1);
                }
            }

            return picks;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public const string ExtractionBase = """
        lex-v4-extraction-prompt/1

        You point at lines; you never answer in your own words. Lex is a public website that shows legislation exactly as
        published. A person asked the question you are given, and Lex's code selected lines of one act. Your only output
        says which of those lines answer, in the form you are held to. Nothing you write is shown to anyone: Lex's code
        quotes the lines you cite, after checking them.

        BASE RULES
        - Use only the lines given. Cite a line by its "id" (L1, L2, ...), never by its article or point number.
        - Every item cites exactly one line. An item with no line is refused.
        - If the lines do not answer the question, return found=false and no item. Never guess, never fill a gap.
        - The law is quoted in its own language. Never say what the law means for a person's own case. A condition is
          reported as a line that states it; never decide whether a condition is met.
        - "value" is copied from the cited line character for character, and is empty for an item that is not a value.
        - Set a caveat only from the closed list: "conditions_apply" (the answer holds only under conditions that the lines
          state), "left_to_contract" (the lines leave the matter to the contract), "general_and_special" (a general rule and
          a special rule both apply).
        """;

    private static readonly Dictionary<string, string> Fragments = new(StringComparer.Ordinal)
    {
        ["rule"] = """
            KIND: rule. Return one item with role "rule": the one line that states the general rule for the party the
            question names (may, may not, must). Then return, with role "case", the lines that state a case, an exception
            or a condition of that rule: at most four, the most general first, each from a different article when
            possible. Set complete=true only if no line given states a further case.
            """,
        ["rule_pinned"] = """
            KIND: rule. Lex's code has already identified the line that states the general rule: the line named in
            "rule_line". Return that line as the one item with role "rule". Then return, with role "case", the lines that
            state a case, an exception or a condition of that rule for the party the question names: at most four, the
            most general first, each from a different article when possible. Set complete=true only if no line given
            states a further case.
            """,
        ["duration"] = """
            KIND: duration. Return, with role "value", the line on which the period asked is written, and in "value" the
            exact words of the period as written on that line (for example "trois ans" or "30 jours calendrier"). If the
            text gives different periods for different cases, return one item per case, at most three. If the lines leave
            the period to the contract, return found=false and set "left_to_contract".
            """,
        ["date"] = """
            KIND: date or moment. Return, with role "value", the line that states the date or the moment asked, and in
            "value" the exact words of a date or period written on that line, or an empty string when the line states the
            moment in words only. At most three items. If the lines leave it to the contract, set "left_to_contract".
            """,
        ["amount"] = """
            KIND: amount or rate. Return, with role "value", the line on which the amount or rate is written, and in
            "value" its exact words as written. At most three items. If no line holds one, return found=false.
            """,
        ["list"] = """
            KIND: list. Return one item with role "item" per element of the list asked, each with its line, at most five,
            in the order of the text, each from a different article when possible. Set complete=true only if every element
            the lines hold is returned.
            """,
        ["text"] = """
            KIND: passages. Return, with role "passage", the lines that answer the question, at most three, the most
            direct first.
            """,
    };

    // ---- The answer check (V4Bricks.CheckAnswer): its instruction and its form. Their digest is apart from PromptsSha256,
    // so that the planner's identity is the one it was while the check is switched off.

    /// <summary>The check's instruction, as measured on 2026-11-19. Its digest is part of the planner's identity when the check is on.</summary>
    public const string AnswerCheckPrompt =
        "You check whether one article of law answers a reader's question. Use only the article given. answer_found is true only if the article states the rule that answers the question as asked: the same subject, the same situation, the same kind of person or contract. It is false if the article is about a neighbouring subject, another situation or another party, if it only shares words with the question, or if the answer would have to be guessed. complete_answer_found is true only if the question can be answered from this article alone. First write the reason in at most 25 words, then the two booleans.";

    internal static string AnswerCheckSchema { get; } = new JsonObject
    {
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["required"] = new JsonArray("reason", "answer_found", "complete_answer_found"),
        ["properties"] = new JsonObject
        {
            ["reason"] = new JsonObject { ["type"] = "string" },
            ["answer_found"] = new JsonObject { ["type"] = "boolean" },
            ["complete_answer_found"] = new JsonObject { ["type"] = "boolean" },
        },
    }.ToJsonString();

    /// <summary>The digest of the check's instruction and form.</summary>
    public static string AnswerCheckSha256 { get; } =
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(AnswerCheckPrompt + "\n" + AnswerCheckSchema)));

    /// <summary>The digest of the instructions this build sends, for the planner's identity: a changed prompt is a changed planner.</summary>
    public static string PromptsSha256 { get; } = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
        ArbiterPrompt + "\n" + OutlinePrompt + "\n" + ExtractionBase + "\n" + string.Join("\n", Fragments.OrderBy(static pair => pair.Key, StringComparer.Ordinal).Select(static pair => pair.Key + ":" + pair.Value)))));

    /// <summary>The fragment of the extraction prompt for a kind of answer: "a fixed base that every question shares, plus only the rules this particular question needs." [8B]</summary>
    public static string FragmentFor(string kind) => Fragments.TryGetValue(kind, out var fragment) ? fragment : Fragments["text"];

    private static string Cut(string text, int most) => text.Length <= most ? text : text[..(most - (char.IsHighSurrogate(text[most - 1]) ? 1 : 0))];

    private static JsonObject Describe(V4QuestionRow row, string kind, V4Dictionary dictionary)
    {
        var description = new JsonObject
        {
            ["question"] = row.Question,
            ["kind"] = kind,
        };
        if (row.Roles.Count > 0)
        {
            description["actor"] = dictionary.Concept(row.Roles[0].Concept)!.LabelFr;
        }

        description["concepts"] = new JsonArray(row.Concepts.Select(read =>
        {
            var concept = dictionary.Concept(read.Concept)!;
            return (JsonNode)new JsonObject { ["term"] = concept.LabelFr, ["meaning"] = concept.Definition };
        }).ToArray());
        return description;
    }

    /// <summary>The arbiter's call (brick I, step 3): one row per candidate, with its place in the outline, its detectors and the lines around it.</summary>
    public static V4ModelCall ArbiterCall(V4QuestionRow row, string kind, V4ActTables tables, IReadOnlyList<V4Candidate> candidates, V4Dictionary dictionary)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(tables);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(dictionary);
        var user = Describe(row, kind, dictionary);
        user["candidates"] = new JsonArray(candidates.Select((candidate, at) =>
        {
            var around = V4Retrieval.ContextOf(tables, candidate.Line, "rule", 3).Where(line => line.Id != candidate.Line.Id).Select(static line => (JsonNode)Cut(line.Text, 300)).ToArray();
            return (JsonNode)new JsonObject
            {
                ["id"] = "c" + (at + 1).ToString(CultureInfo.InvariantCulture),
                ["article"] = candidate.Article.Label ?? candidate.Article.Id,
                ["heading"] = candidate.Article.Heading ?? string.Empty,
                ["point"] = candidate.Line.Point ?? string.Empty,
                ["found_by"] = new JsonArray(candidate.Detectors.Select(static detector => (JsonNode)detector).ToArray()),
                ["line"] = Cut(candidate.Line.Text, 700),
                ["around"] = new JsonArray(around),
            };
        }).ToArray());
        return new V4ModelCall("arbiter", ArbiterPrompt, user.ToJsonString(Relaxed), "lex_v4_arbiter", ArbiterSchema, 900);
    }

    private static readonly JsonSerializerOptions Relaxed = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static string ArbiterSchema { get; } = new JsonObject
    {
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["required"] = new JsonArray("candidates"),
        ["properties"] = new JsonObject
        {
            ["candidates"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject
                {
                    ["type"] = "object",
                    ["additionalProperties"] = false,
                    ["required"] = new JsonArray("id", "role", "reason"),
                    ["properties"] = new JsonObject
                    {
                        ["id"] = new JsonObject { ["type"] = "string" },
                        ["role"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray(ArbiterRoles.Select(static role => (JsonNode)role).ToArray()) },
                        ["reason"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray(ArbiterReasons.Select(static reason => (JsonNode)reason).ToArray()) },
                    },
                },
            },
        },
    }.ToJsonString();

    /// <summary>
    /// Reads the arbiter's reply, or null when it does not fit its form: every candidate given named exactly once, no other
    /// id, a role and a reason of the closed lists (brick G: "the candidates were sent").
    /// </summary>
    public static List<V4ArbiterRow>? ReadArbiter(string content, int given)
    {
        try
        {
            using var document = JsonDocument.Parse(content, new JsonDocumentOptions { MaxDepth = 8 });
            var rows = new List<V4ArbiterRow>();
            foreach (var item in document.RootElement.GetProperty("candidates").EnumerateArray())
            {
                var id = item.GetProperty("id").GetString()!;
                var role = item.GetProperty("role").GetString()!;
                var reason = item.GetProperty("reason").GetString()!;
                if (!ArbiterRoles.Contains(role) || !ArbiterReasons.Contains(reason) || rows.Any(row => row.Id == id))
                {
                    return null;
                }

                rows.Add(new V4ArbiterRow(id, role, reason));
            }

            var expected = Enumerable.Range(1, given).Select(static number => "c" + number.ToString(CultureInfo.InvariantCulture)).ToHashSet(StringComparer.Ordinal);
            return rows.Count == given && rows.All(row => expected.Contains(row.Id)) ? rows : null;
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// The extraction's call (brick J): the generation brief, and the lines of the context as a table with named columns,
    /// the column to cite said in the instruction and again above the lines. The law's own definitions of the terms that
    /// appear travel with them: "This prevents the LLM from importing a generic notion when the document defines the term
    /// specifically." [11]
    /// </summary>
    public static V4ModelCall ExtractionCall(V4QuestionRow row, string kind, V4ActTables tables, IReadOnlyList<V4Line> lines, IReadOnlyList<V4Definition> definitions, V4Dictionary dictionary, V4Line? pinnedRule = null)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(tables);
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(dictionary);
        var user = Describe(row, kind, dictionary);
        user["cite"] = "the column id of lines (L1, L2, ...)";
        var pinned = pinnedRule is null ? -1 : lines.ToList().FindIndex(line => line.Id == pinnedRule.Id);
        if (pinned >= 0)
        {
            user["rule_line"] = "L" + (pinned + 1).ToString(CultureInfo.InvariantCulture);
        }

        user["lines"] = new JsonArray(lines.Select((line, at) =>
        {
            var article = tables.Article(line.Article)!;
            return (JsonNode)new JsonObject
            {
                ["id"] = "L" + (at + 1).ToString(CultureInfo.InvariantCulture),
                ["article"] = article.Label ?? article.Id,
                ["heading"] = article.Heading ?? string.Empty,
                ["point"] = line.Point ?? string.Empty,
                ["text"] = Cut(line.Text, 900),
            };
        }).ToArray());
        user["definitions_of_the_act"] = new JsonArray(definitions.Select(definition => (JsonNode)new JsonObject
        {
            ["term"] = definition.Term,
            ["text"] = Cut(string.Join(' ', Enumerable.Range(definition.FirstLine, definition.LastLine - definition.FirstLine + 1)
                .Select(index => tables.Line(definition.Article + "#" + index.ToString(CultureInfo.InvariantCulture))?.Text ?? string.Empty)), 400),
        }).ToArray());
        return new V4ModelCall("extraction", ExtractionBase + "\n" + FragmentFor(pinned >= 0 && kind == "rule" ? "rule_pinned" : kind), user.ToJsonString(Relaxed), "lex_v4_extraction", ExtractionSchema, 700);
    }

    private static string ExtractionSchema { get; } = new JsonObject
    {
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["required"] = new JsonArray("found", "complete", "items", "caveats"),
        ["properties"] = new JsonObject
        {
            ["found"] = new JsonObject { ["type"] = "boolean" },
            ["complete"] = new JsonObject { ["type"] = "boolean" },
            ["items"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject
                {
                    ["type"] = "object",
                    ["additionalProperties"] = false,
                    ["required"] = new JsonArray("line", "role", "value"),
                    ["properties"] = new JsonObject
                    {
                        ["line"] = new JsonObject { ["type"] = "string" },
                        ["role"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("rule", "case", "value", "item", "passage") },
                        ["value"] = new JsonObject { ["type"] = "string" },
                    },
                },
            },
            ["caveats"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray(ModelCaveats.Select(static caveat => (JsonNode)caveat).ToArray()) },
            },
        },
    }.ToJsonString();

    /// <summary>Reads the extraction's reply into its typed form, or null when it is not of that form.</summary>
    public static V4Extraction? ReadExtraction(string content)
    {
        try
        {
            using var document = JsonDocument.Parse(content, new JsonDocumentOptions { MaxDepth = 8 });
            var root = document.RootElement;
            var items = root.GetProperty("items").EnumerateArray()
                .Select(static item => new V4ExtractedItem(item.GetProperty("line").GetString()!, item.GetProperty("role").GetString()!, item.GetProperty("value").GetString() ?? string.Empty))
                .ToList();
            var caveats = root.GetProperty("caveats").EnumerateArray().Select(static item => item.GetString()!).Distinct(StringComparer.Ordinal).ToList();
            return new V4Extraction(root.GetProperty("found").GetBoolean(), root.GetProperty("complete").GetBoolean(), items, caveats);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>One item of an answer that passed the checks: the line, its role, and the value's exact position on the line when it restates one.</summary>
    public sealed record Checked(V4Line Line, string Role, V4Value? Value);

    /// <summary>
    /// The answer validator (brick G), all code. It answers the checked items, or null with the check that failed: the shape
    /// is of the kind asked; found and items agree; every item cites a line that was sent; a value is on the cited line and
    /// reads with the pattern of its type (ours); a rule's line has the form of a rule. "The LLM extracts; Python compares."
    /// </summary>
    public static List<Checked>? Validate(V4Extraction extraction, string kind, IReadOnlyList<V4Line> sent, out string? failed, V4Line? pinnedRule = null)
    {
        ArgumentNullException.ThrowIfNull(extraction);
        ArgumentNullException.ThrowIfNull(sent);
        failed = null;
        if (extraction.Found != (extraction.Items.Count > 0))
        {
            failed = "found_and_items_disagree";
            return null;
        }

        if (extraction.Caveats.Any(caveat => !ModelCaveats.Contains(caveat)))
        {
            failed = "caveat_outside_list";
            return null;
        }

        var valueKind = kind is "duration" or "date" or "amount";
        var allowed = kind switch
        {
            "rule" => new[] { "rule", "case" },
            "duration" or "date" or "amount" => ["value"],
            "list" => ["item"],
            _ => ["passage"],
        };
        var most = kind switch { "rule" => 5, "list" => 5, _ => 3 };
        if (extraction.Items.Count > most || extraction.Items.Any(item => !allowed.Contains(item.Role)) ||
            (kind == "rule" && extraction.Items.Count > 0 && extraction.Items.Count(static item => item.Role == "rule") != 1))
        {
            failed = "shape_not_of_the_kind";
            return null;
        }

        var items = new List<Checked>();
        foreach (var item in extraction.Items)
        {
            if (item.Line.Length < 2 || item.Line[0] != 'L' || !int.TryParse(item.Line.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number < 1 || number > sent.Count)
            {
                failed = "line_was_not_sent";
                return null;
            }

            var line = sent[number - 1];
            if (items.Any(existing => existing.Line.Id == line.Id))
            {
                failed = "line_cited_twice";
                return null;
            }

            V4Value? value = null;
            if (valueKind)
            {
                var written = V4Patterns.Values(kind == "date" && V4Patterns.Values("date", line.Folded).Count == 0 ? "duration" : kind, line.Folded);
                if (item.Value.Trim().Length > 0)
                {
                    // The value the model copied must be on the cited line, and code keeps the pattern's own span of it.
                    var folded = string.Join(' ', V4Text.Words(V4Text.Fold(item.Value)).Select(static word => word.Text));
                    var match = written.Cast<V4Value?>().FirstOrDefault(candidate =>
                    {
                        var onLine = string.Join(' ', V4Text.Words(line.Folded.Substring(candidate!.Value.Start, candidate.Value.Length)).Select(static word => word.Text));
                        return onLine == folded || onLine.Contains(folded, StringComparison.Ordinal) || folded.Contains(onLine, StringComparison.Ordinal);
                    });
                    if (match is null && kind != "date")
                    {
                        failed = "value_not_on_the_line";
                        return null;
                    }

                    // A moment the law states in words ("aux date et heure indiquées dans le contrat") is quoted, and not restated.
                    value = match;
                }
                else if (kind != "date")
                {
                    failed = "value_missing";
                    return null;
                }
            }
            else if (item.Role == "rule" && pinnedRule is not null && line.Id != pinnedRule.Id)
            {
                failed = "rule_line_is_not_the_one_code_identified";
                return null;
            }
            else if (item.Role == "rule" && !V4Patterns.Confirms("rule", line.Folded))
            {
                failed = "rule_line_has_no_rule_form";
                return null;
            }

            items.Add(new Checked(line, item.Role, value));
        }

        return items;
    }
}
