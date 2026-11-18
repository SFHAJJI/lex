using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lex.V3.Contracts.Platform.Assistant.Bricks;

/// <summary>One part of a question as the model described it: what it asks, and which of the reader's words name what.</summary>
/// <param name="Asks">One of <see cref="V4QuestionCall.Asks"/>.</param>
/// <param name="Concept">A concept of the dictionary the part is about, or empty.</param>
/// <param name="ActRef">The context chip that is the act the part is about, when its words name none; or empty.</param>
public sealed record V4DescribedPart(
    string Asks,
    bool OwnCase,
    V4Span Span,
    V4Span? Citation,
    V4Span? Act,
    V4Span? Words,
    string Language,
    string Date,
    string Range,
    string Concept,
    string ActRef = "");

/// <summary>
/// The question call (brick C, step 6): the one model call that describes a question the tables did not read, in place of
/// the call that planned it. "The model call takes over from the planner call, one for one": the same model, a different
/// job. It says what a part asks, from a closed list, and points at spans of the reader's own words; it names no operation
/// and writes no value. Code maps the description to operations by a fixed table (<see cref="PlanJson"/>), as the
/// dispatcher's routes 2, 3 and the search do.
/// </summary>
public static class V4QuestionCall
{
    /// <summary>What a part can ask, as the model may say it. A closed list: adding one is a row here and a row in <see cref="PlanJson"/>.</summary>
    public static readonly IReadOnlyList<string> Asks =
    [
        "article_or_act_cited", "words_on_a_subject", "versions_of_an_act", "record_of_an_act", "act_on_a_date",
        "two_dates_compared", "changes_in_a_period", "exact_identifier", "nothing_to_look_up",
    ];

    public const string Prompt = """
        lex-v4-question-prompt/1

        You describe a question; you never answer it, and you choose no operation. Lex is a public website that shows
        Luxembourg and EU legislation exactly as the official publishers published it. A person asked the question you are
        given. Your only output is a description of that question, in the JSON form you are held to. Lex's code reads your
        description, decides what to run, runs it and writes every sentence the person reads from fixed wording. Nothing
        you write is shown to anyone: write no sentence, no explanation and no advice.

        THE QUESTION
        - You receive the question with obvious identifiers (e-mail addresses, phone numbers, bank account and identity
          numbers) replaced by the character "•". Never refer to a masked run.
        - Positions are counted in UTF-16 code units of the question as given, from 0; a span [start, end] includes start
          and excludes end. You also receive "words": every word of the question with its exact start and end, and
          "question_length". Take every span from the words: it starts at one word's start and ends at the same or a later
          word's end. Never count positions yourself, and no span ends after question_length.
        - You also receive references: dates and ranges the question writes (d1, d2, r1) and context chips from the
          person's last answer (c1, c2). Use a reference only by its name, exactly as given. Never invent one.

        PARTS
        - Describe one part, or two when the question asks two different things, in order, not overlapping. "span" is the
          part itself; for one part it is the whole question.
        - "own_case" is true when the part asks to apply the law to someone's situation: choosing among legal options,
          concluding on a person's situation, predicting what a court or an authority will decide, or computing deadlines
          or amounts from someone's facts. This does not depend on whether the question says "I" or "an employee".

        WHAT THE PART ASKS ("asks", exactly one)
        - "article_or_act_cited": it cites an article of an act, or an act, and asks what it says or said. Give
          "citation": the citation as the person wrote it, for example "article 1er de la loi du 10 août 1991" or "article 5
          du RGPD". When the act is named by its date and then by words of its title ("loi du 27 juillet 1997 sur le
          contrat d'assurance", "loi du 10 août 1915 concernant les sociétés commerciales", "règlement grand-ducal du 15
          mars 2008 relatif à ..."), the citation runs to the end of those title words: several acts share a date, and
          only the title words tell them apart. A date inside an act's name never ends the citation. Stop before the
          question's own end ("?").
        - "words_on_a_subject": it asks what the law says on a subject without citing an article. Give "words": the
          concept with its qualifiers, in the person's own words; for "Quelle est la durée maximale de la période d'essai
          d'un contrat de travail ?" it is "durée maximale de la période d'essai d'un contrat de travail", never only its
          last noun phrase. When the question names the one act to look in ("RGPD", "AI Act", "DORA", a CELEX number, a
          code), give "act" too: an EU act's words are searched only within the act named.
        - "versions_of_an_act": it asks which versions of an act exist or are held. Give "act".
        - "record_of_an_act": it asks about the act itself (what it is, its status, what it amends). Give "act".
        - "act_on_a_date": it asks for an act as a whole as it stood on a date. Give "act" and "date". Never use it for what
          the law says on a subject or in an article.
        - "two_dates_compared": it asks how an act changed between two dates. Give "act" and "range".
        - "changes_in_a_period": it asks what changed between two dates, naming no act. Give "range".
        - "exact_identifier": the part is an identifier alone (an ELI, a CELEX number, an address of Lex). Give "citation".
        - "nothing_to_look_up": none of these.

        THE OTHER FIELDS
        - A span is [start, end]; [-1, -1] when the part gives none.
        - "language": "fra" for Luxembourg law and French texts; "eng" or "deu" for an EU text asked in that language.
        - "date": "today" when the question gives no date to read the law on; else the name of a date reference or of a
          context chip. A date that is part of an act's name ("loi du 10 août 1991") is not the date to read it on.
        - "range": "" or the name of a range ("r1": the references "r1.from" and "r1.to" are its two ends).
        - "act_ref": "" or the name of a context chip of kind "context_work" ("c1"), when the part is about the act of
          the person's last answer and its own words name no act.
        - "concept": when the part asks about private insurance in words of its own, the one concept of "concepts" it is
          about, by its id; "" when none fits or the question is not about private insurance.
        """;

    /// <summary>The digest of the instruction, for the planner's identity.</summary>
    public static string PromptSha256 { get; } = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Prompt)));

    private static readonly JsonSerializerOptions Relaxed = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>The character the intake writes over an identifier.</summary>
    private const char MaskCharacter = '•';

    /// <summary>
    /// The question's words as code finds them, each a run of characters between white space with its start and end, so
    /// the model takes every span from them rather than counting positions. A run holding a mask character is left out.
    /// </summary>
    public static IReadOnlyList<(string Text, int Start, int End)> WordsOf(string question)
    {
        ArgumentNullException.ThrowIfNull(question);
        var words = new List<(string Text, int Start, int End)>();
        var at = 0;
        while (at < question.Length)
        {
            while (at < question.Length && char.IsWhiteSpace(question[at]))
            {
                at++;
            }

            var start = at;
            while (at < question.Length && !char.IsWhiteSpace(question[at]))
            {
                at++;
            }

            if (at > start && question.AsSpan(start, at - start).IndexOf(MaskCharacter) < 0)
            {
                words.Add((question[start..at], start, at));
            }
        }

        return words;
    }

    /// <summary>The call: the fixed instruction, the masked question with its words and references, and the concepts of the dictionary by id.</summary>
    public static V4ModelCall Call(V3PlannerInput input, V4Dictionary dictionary)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(dictionary);
        var user = new JsonObject
        {
            ["question"] = input.MaskedQuestion,
            ["question_length"] = input.MaskedQuestion.Length,
            ["words"] = new JsonArray(WordsOf(input.MaskedQuestion).Select(static word => (JsonNode)new JsonObject { ["w"] = word.Text, ["start"] = word.Start, ["end"] = word.End }).ToArray()),
            ["references"] = new JsonArray(input.References.Select(static reference => (JsonNode)new JsonObject
            {
                ["ref"] = reference.Ref,
                ["kind"] = reference.Kind,
                ["start"] = reference.Start,
                ["end"] = reference.End,
            }).ToArray()),
            ["search_languages"] = new JsonArray(input.SearchLanguages.Select(static language => (JsonNode)language).ToArray()),
            ["concepts"] = new JsonArray(dictionary.Concepts.Where(static concept => concept.Kind != "role").Select(static concept => (JsonNode)new JsonObject
            {
                ["id"] = concept.Id,
                ["term"] = concept.LabelFr,
                ["meaning"] = concept.Definition,
            }).ToArray()),
        };
        return new V4ModelCall("question", Prompt, user.ToJsonString(Relaxed), "lex_v4_question", Schema(dictionary), 500);
    }

    private static string Schema(V4Dictionary dictionary)
    {
        static JsonObject Span() => new() { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "integer" } };
        var part = new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new JsonArray("asks", "own_case", "span", "citation", "act", "words", "language", "date", "range", "act_ref", "concept"),
            ["properties"] = new JsonObject
            {
                ["asks"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray(Asks.Select(static value => (JsonNode)value).ToArray()) },
                ["own_case"] = new JsonObject { ["type"] = "boolean" },
                ["span"] = Span(),
                ["citation"] = Span(),
                ["act"] = Span(),
                ["words"] = Span(),
                ["language"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("fra", "eng", "deu") },
                ["date"] = new JsonObject { ["type"] = "string" },
                ["range"] = new JsonObject { ["type"] = "string" },
                ["act_ref"] = new JsonObject { ["type"] = "string" },
                ["concept"] = new JsonObject
                {
                    ["type"] = "string",
                    ["enum"] = new JsonArray(new[] { string.Empty }.Concat(dictionary.Concepts.Where(static concept => concept.Kind != "role").Select(static concept => concept.Id)).Select(static value => (JsonNode)value).ToArray()),
                },
            },
        };
        return new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new JsonArray("parts"),
            ["properties"] = new JsonObject { ["parts"] = new JsonObject { ["type"] = "array", ["items"] = part } },
        }.ToJsonString();
    }

    /// <summary>
    /// Reads the model's description, or null when it does not fit its closed form: one or two parts in order inside the
    /// question, a kind of the list, spans that are spans of the question (or none), a language of the three, tokens for
    /// the date and the range. What a span or a reference names is checked again by the plan's validator.
    /// </summary>
    public static List<V4DescribedPart>? Read(string content, string question, V4Dictionary dictionary)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(dictionary);
        try
        {
            using var document = JsonDocument.Parse(content, new JsonDocumentOptions { MaxDepth = 8 });
            var parts = new List<V4DescribedPart>();
            foreach (var item in document.RootElement.GetProperty("parts").EnumerateArray())
            {
                V4Span? Span(string name)
                {
                    var span = item.GetProperty(name);
                    if (span.GetArrayLength() != 2)
                    {
                        throw new FormatException(name);
                    }

                    var start = span[0].GetInt32();
                    var end = span[1].GetInt32();
                    if (start < 0 || end < 0)
                    {
                        return null;
                    }

                    return start < end && end <= question.Length ? new V4Span(start, end) : throw new FormatException(name);
                }

                static string Token(string value, int most) =>
                    value.Length <= most && value.All(static character => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character is '.' or '_') ? value : throw new FormatException("token");

                var asks = item.GetProperty("asks").GetString()!;
                var language = item.GetProperty("language").GetString()!;
                var concept = item.GetProperty("concept").GetString() ?? string.Empty;
                if (!Asks.Contains(asks) || language is not ("fra" or "eng" or "deu") || (concept.Length > 0 && dictionary.Concept(concept) is null))
                {
                    return null;
                }

                parts.Add(new V4DescribedPart(
                    asks,
                    item.GetProperty("own_case").GetBoolean(),
                    Span("span") ?? new V4Span(0, question.Length),
                    Span("citation"),
                    Span("act"),
                    Span("words"),
                    language,
                    Token(item.GetProperty("date").GetString() ?? "today", 16),
                    Token(item.GetProperty("range").GetString() ?? string.Empty, 16),
                    concept,
                    Token(item.GetProperty("act_ref").GetString() ?? string.Empty, 16)));
            }

            if (parts.Count is < 1 or > 2 || (parts.Count == 2 && parts[1].Span.Start < parts[0].Span.End))
            {
                return null;
            }

            if (parts.Count == 1)
            {
                // One part is the whole question, whatever span the model gave it.
                parts[0] = parts[0] with { Span = new V4Span(0, question.Length) };
            }

            return parts;
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or IndexOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>
    /// The description code gives when no model does (none is wired, the call failed, or its reply left its closed form):
    /// the question's own words, to search as written, on the one date the question writes or today. Ours: "deterministic
    /// first". The answer is then a search, and says that no model took part in it.
    /// </summary>
    public static List<V4DescribedPart> ByCode(V3PlannerInput input, V4QuestionRow row)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(row);
        var dates = input.References.Where(static reference => reference.Kind == "date").ToList();
        return
        [
            new V4DescribedPart(
                "words_on_a_subject",
                OwnCase: false,
                new V4Span(0, input.MaskedQuestion.Length),
                Citation: null,
                Act: null,
                Words: null,
                row.Language == "en" ? "eng" : "fra",
                dates.Count == 1 ? dates[0].Ref : "today",
                Range: string.Empty,
                Concept: string.Empty),
        ];
    }

    /// <summary>What became of the question call, as the record says it: a model described the question, or code did and why.</summary>
    public static readonly IReadOnlyList<string> Outcomes = ["described", "not_wired", "failed", "refused"];

    /// <summary>
    /// The plan code writes from a description: per part, the operations its kind maps to, with every argument bound to a
    /// span of the question, a reference, a choice, today or an earlier result. The table is the whole of it; the model
    /// chose none of the operations, their order, their follow-ups or where an argument comes from. A span the plan cannot
    /// carry (outside its part, over a mask, too long) is dropped, and the kind falls to what it has left.
    /// </summary>
    /// <param name="outcome">One of <see cref="Outcomes"/>; "described" exactly when <paramref name="asked"/> is given.</param>
    /// <param name="asked">The call a model answered and its reply, or null when code described the question.</param>
    public static string PlanJson(V3PlannerInput input, IReadOnlyList<V4DescribedPart> parts, string outcome, (V4ModelCall Call, V4ModelReply Reply, string ModelId)? asked)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(parts);
        if (!Outcomes.Contains(outcome) || (outcome == "described") != asked.HasValue)
        {
            throw new ArgumentException("Not an outcome of the question call.", nameof(outcome));
        }

        var question = input.MaskedQuestion;
        var references = input.References.ToDictionary(static reference => reference.Ref, static reference => reference.Kind, StringComparer.Ordinal);
        var steps = new List<JsonObject>();
        var followUps = new List<JsonObject>();
        var planParts = new JsonArray();
        for (var at = 0; at < parts.Count; at++)
        {
            var part = parts[at];
            var partId = "p" + (at + 1).ToString(CultureInfo.InvariantCulture);

            JsonObject SpanSource(V4Span span) => new() { ["span"] = new JsonArray(span.Start, span.End) };

            // A span is an argument only when it is the reader's own words, inside the part and off the masks.
            V4Span? Usable(V4Span? span, int most) =>
                span is { } given && given.Start >= part.Span.Start && given.End <= part.Span.End && given.End - given.Start <= most
                    && !string.IsNullOrWhiteSpace(question[given.Start..given.End]) && question.AsSpan(given.Start, given.End - given.Start).IndexOf(MaskCharacter) < 0
                    ? given
                    : null;

            // The words to look for when the part gives none: the part's longest run off the masks, without the marks that end it.
            V4Span? Own()
            {
                var best = new V4Span(part.Span.Start, part.Span.Start);
                var run = part.Span.Start;
                for (var index = part.Span.Start; index <= part.Span.End; index++)
                {
                    if (index == part.Span.End || question[index] == MaskCharacter)
                    {
                        if (index - run > best.End - best.Start)
                        {
                            best = new V4Span(run, index);
                        }

                        run = index + 1;
                    }
                }

                var (start, end) = (best.Start, best.End);
                while (end > start && (char.IsWhiteSpace(question[end - 1]) || question[end - 1] is '?' or '!' or '.'))
                {
                    end--;
                }

                while (start < end && char.IsWhiteSpace(question[start]))
                {
                    start++;
                }

                return start < end ? new V4Span(start, Math.Min(end, start + V3AssistantPlanValidator.MaximumQueryLength)) : null;
            }

            var citation = Usable(part.Citation, V3AssistantPlanValidator.MaximumQueryLength);
            var words = Usable(part.Words, V3AssistantPlanValidator.MaximumQueryLength);
            var act = Usable(part.Act, V3AssistantPlanValidator.MaximumIdentifierLength);
            var named = act ?? Usable(part.Citation, V3AssistantPlanValidator.MaximumIdentifierLength);

            // The language is one this mount searches: the one described when it is, else the mount's first.
            var language = input.SearchLanguages.Contains(part.Language, StringComparer.Ordinal) ? part.Language : input.SearchLanguages.Count > 0 ? input.SearchLanguages[0] : "fra";

            JsonObject DateSource() =>
                part.Date != "today" && references.TryGetValue(part.Date, out var kind) && kind is "date" or "context_date"
                    ? new JsonObject { ["ref"] = part.Date }
                    : new JsonObject { ["today"] = true };
            var rangeName = part.Range.EndsWith(".from", StringComparison.Ordinal) ? part.Range[..^5] : part.Range.EndsWith(".to", StringComparison.Ordinal) ? part.Range[..^3] : part.Range;
            var range = rangeName.Length > 0 && references.ContainsKey(rangeName + ".from") && references.ContainsKey(rangeName + ".to") ? rangeName : null;

            // The act of the person's last answer, when the part's own words name none and the description points at its chip.
            var chip = part.ActRef.Length > 0 && references.TryGetValue(part.ActRef, out var chipKind) && chipKind == "context_work" ? part.ActRef : null;
            JsonObject? ActSource() => named is { } inWords ? SpanSource(inWords) : chip is not null ? new JsonObject { ["ref"] = chip } : null;
            var stepsBefore = steps.Count;

            // Arguments in the order the canonical plan writes them (by name), so the bytes written are the bytes kept.
            static JsonObject InOrder(JsonObject arguments)
            {
                var ordered = new JsonObject();
                foreach (var (name, value) in arguments.OrderBy(static pair => pair.Key, StringComparer.Ordinal).ToList())
                {
                    arguments.Remove(name);
                    ordered[name] = value;
                }

                return ordered;
            }

            JsonObject Step(string operation, JsonObject arguments) => new()
            {
                ["step_id"] = "s" + (steps.Count + 1).ToString(CultureInfo.InvariantCulture),
                ["part_id"] = partId,
                ["operation_id"] = operation,
                ["arguments"] = InOrder(arguments),
            };

            void SearchThenRead(V4Span? query, JsonObject? within)
            {
                if (query is not { } sought || steps.Count >= V3AssistantPlanValidator.MaximumSteps)
                {
                    return;
                }

                var date = DateSource();
                var arguments = new JsonObject { ["query"] = SpanSource(sought), ["language"] = new JsonObject { ["choice"] = language }, ["date"] = date.DeepClone() };
                if (within is not null)
                {
                    arguments["identifier"] = within;
                }

                var step = Step("search", arguments);
                steps.Add(step);
                if (followUps.Count < V3AssistantPlanValidator.MaximumFollowUps)
                {
                    var id = step["step_id"]!.GetValue<string>();
                    followUps.Add(new JsonObject
                    {
                        ["step_id"] = "f" + (followUps.Count + 1).ToString(CultureInfo.InvariantCulture),
                        ["after"] = id,
                        ["condition"] = "on_single_article",
                        ["operation_id"] = "evidence_bundle",
                        ["arguments"] = InOrder(new JsonObject
                        {
                            ["identifier"] = new JsonObject { ["result"] = id, ["pick"] = "sole_work" },
                            ["date"] = date.DeepClone(),
                            ["article"] = new JsonObject { ["result"] = id, ["pick"] = "sole_article" },
                        }),
                    });
                }
            }

            switch (part.Asks)
            {
                case "nothing_to_look_up":
                    break;
                case "words_on_a_subject":
                    SearchThenRead(words ?? citation ?? Own(), act is { } inside ? SpanSource(inside) : chip is not null ? new JsonObject { ["ref"] = chip } : null);
                    break;
                case "versions_of_an_act" when ActSource() is { } versioned:
                    steps.Add(Step("timeline", new JsonObject { ["identifier"] = versioned }));
                    break;
                case "record_of_an_act" when ActSource() is { } recorded:
                    steps.Add(Step("dossier", new JsonObject { ["identifier"] = recorded }));
                    break;
                case "act_on_a_date" when ActSource() is { } dated:
                    steps.Add(Step("as_of", new JsonObject { ["identifier"] = dated, ["date"] = DateSource() }));
                    break;
                case "two_dates_compared" when ActSource() is { } compared && range is not null:
                    steps.Add(Step("diff", new JsonObject { ["identifier"] = compared, ["date_from"] = new JsonObject { ["ref"] = range + ".from" }, ["date_to"] = new JsonObject { ["ref"] = range + ".to" } }));
                    break;
                case "changes_in_a_period" when range is not null:
                    steps.Add(Step("changes_in_period", new JsonObject { ["date_from"] = new JsonObject { ["ref"] = range + ".from" }, ["date_to"] = new JsonObject { ["ref"] = range + ".to" } }));
                    break;
                case "exact_identifier" when Usable(part.Citation, V3AssistantPlanValidator.MaximumIdentifierLength) is { } identifier:
                    steps.Add(Step("resolve", new JsonObject { ["identifier"] = SpanSource(identifier) }));
                    break;
                default:
                    // A cited article or act, and every part whose kind lacks what it needs: the citation, else the words,
                    // else the part's own words, searched, and the one article they name read on the date asked. A citation
                    // that names no act ("et l'article 40 ?") is searched within the act of the person's last answer.
                    SearchThenRead(citation ?? words ?? act ?? Own(), citation is not null && act is null && chip is not null ? new JsonObject { ["ref"] = chip } : null);
                    break;
            }

            planParts.Add(new JsonObject
            {
                ["part_id"] = partId,
                ["span"] = new JsonArray(part.Span.Start, part.Span.End),
                ["proposed_verdict"] = part.OwnCase ? "refuse" : steps.Count == stepsBefore ? "point" : "answer",
            });
        }

        var bricks = new JsonObject
        {
            ["version"] = V4PlanRecord.Version,
            ["dictionary"] = V4Dictionary.Insurance.Sha256,
            ["corpus_index"] = V4CorpusIndex.Insurance.Sha256,
            ["route"] = "operations",
            ["date"] = "today",
            ["concepts"] = new JsonArray(),
            ["roles"] = new JsonArray(),
            ["rule_question"] = false,
            ["question_call"] = outcome,
            ["described"] = new JsonArray(parts.Select(static part => (JsonNode)new JsonObject { ["asks"] = part.Asks, ["own_case"] = part.OwnCase, ["language"] = part.Language }).ToArray()),
            ["detectors"] = new JsonArray(),
            ["candidates"] = new JsonArray(),
            ["sweep"] = new JsonArray(),
            ["passes"] = new JsonArray(new JsonObject { ["n"] = 1, ["trigger"] = "first_pass", ["changed"] = "none", ["outcome"] = asked.HasValue ? "question_described" : "question_read_by_code" }),
            ["model_calls"] = asked is { } answered
                ? new JsonArray(new JsonObject
                {
                    ["step"] = "question",
                    ["model"] = new string(answered.ModelId.Select(static character => char.IsAsciiLetterOrDigit(character) || character is '_' or '.' or '-' ? character : '-').ToArray()),
                    ["prompt"] = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(answered.Call.System + "\n" + answered.Call.User)))[..16],
                    ["reply"] = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(answered.Reply.Content))),
                    ["input_tokens"] = Math.Max(0, answered.Reply.InputTokens),
                    ["output_tokens"] = Math.Max(0, answered.Reply.OutputTokens),
                })
                : new JsonArray(),
            ["answer"] = new JsonObject
            {
                ["kind"] = "operations",
                ["found"] = false,
                ["complete"] = false,
                ["items"] = new JsonArray(),
                ["values"] = new JsonArray(),
                ["caveats"] = new JsonArray(),
                ["notes"] = new JsonArray(),
                ["more"] = new JsonArray(),
            },
        };
        return new JsonObject
        {
            ["schema"] = V3AssistantPlanValidator.Schema2,
            ["parts"] = planParts,
            ["steps"] = new JsonArray(steps.Select(static step => (JsonNode)step).ToArray()),
            ["follow_ups"] = new JsonArray(followUps.Select(static step => (JsonNode)step).ToArray()),
            ["bricks"] = bricks,
        }.ToJsonString();
    }
}
