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
          numbers) replaced by the character "•". Never copy a masked run.
        - Where the form asks for words of the question ("part", "citation", "act", "words"), copy them from the question
          exactly, character for character, as one unbroken run: never translate, correct, reorder or complete them. Lex's
          code finds your copy in the question; words it does not find there are ignored. Give "" when there are none.
        - You also receive references: the dates and ranges the question writes (d1, d2, r1.from, r1.to), each with the
          words it stands for, and context chips from the person's last answer (c1, c2). Use a reference only by its
          name, exactly as given. Never invent one.

        PARTS
        - Describe one part, or two when the question asks two different things, in the order they are asked. For one
          part, "part" is "". For two, "part" is the words of the question each part is made of.
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
    /// The call: the fixed instruction, the masked question, its references each with the words it stands for, the
    /// languages the mount searches, and the concepts of the dictionary by id.
    /// </summary>
    public static V4ModelCall Call(V3PlannerInput input, V4Dictionary dictionary)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(dictionary);
        var question = input.MaskedQuestion;
        var user = new JsonObject
        {
            ["question"] = question,
            ["references"] = new JsonArray(input.References.Select(reference => (JsonNode)new JsonObject
            {
                ["ref"] = reference.Ref,
                ["kind"] = reference.Kind,
                ["words"] = reference.Start >= 0 && reference.Start < reference.End && reference.End <= question.Length ? question[reference.Start..reference.End] : string.Empty,
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
        static JsonObject Words() => new() { ["type"] = "string" };
        var part = new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new JsonArray("asks", "own_case", "part", "citation", "act", "words", "language", "date", "range", "act_ref", "concept"),
            ["properties"] = new JsonObject
            {
                ["asks"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray(Asks.Select(static value => (JsonNode)value).ToArray()) },
                ["own_case"] = new JsonObject { ["type"] = "boolean" },
                ["part"] = Words(),
                ["citation"] = Words(),
                ["act"] = Words(),
                ["words"] = Words(),
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
    /// Where the words a description copies stand in the question: their first place as written; else their first place
    /// under the matching fold (capitals, accents, apostrophes and spaces compared as the dictionary compares them, one
    /// unit for one unit, so the place is the same). Null when the question does not hold them, or holds a mask there:
    /// the model's words are never used, only the reader's own at the place found.
    /// </summary>
    internal static V4Span? Locate(string question, string? copied)
    {
        var words = copied?.Trim() ?? string.Empty;
        if (words.Length == 0 || words.Length > question.Length)
        {
            return null;
        }

        var at = question.IndexOf(words, StringComparison.Ordinal);
        if (at < 0)
        {
            at = V4Text.Fold(question).IndexOf(V4Text.Fold(words), StringComparison.Ordinal);
        }

        return at >= 0 && question.AsSpan(at, words.Length).IndexOf(MaskCharacter) < 0 ? new V4Span(at, at + words.Length) : null;
    }

    /// <summary>
    /// Reads the model's description, or null when it does not fit its closed form: an object with one or two parts, each
    /// with a kind of the list, a language of the three, a concept of the dictionary or none, and its members of their
    /// types. Inside that form, what cannot be used is read as absent and nothing is repaired: words the question does not
    /// hold name no span, and a date, a range or a chip that is no reference's name is none. Whether a name is a reference
    /// of this question is checked where the plan is written, and again by the plan's validator.
    /// </summary>
    public static List<V4DescribedPart>? Read(string content, string question, V4Dictionary dictionary)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(dictionary);
        try
        {
            using var document = JsonDocument.Parse(content, new JsonDocumentOptions { MaxDepth = 8 });
            var parts = new List<V4DescribedPart>();
            var spans = new List<V4Span?>();
            foreach (var item in document.RootElement.GetProperty("parts").EnumerateArray())
            {
                string Text(string name) => item.GetProperty(name).GetString() ?? string.Empty;
                static string Token(string value, string none) =>
                    value.Length is > 0 and <= 16 && value.All(static character => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character is '.' or '_') ? value : none;

                var asks = Text("asks");
                var language = Text("language");
                var concept = Text("concept");
                if (!Asks.Contains(asks) || language is not ("fra" or "eng" or "deu") || (concept.Length > 0 && dictionary.Concept(concept) is not { Kind: not "role" }))
                {
                    return null;
                }

                spans.Add(Locate(question, Text("part")));
                parts.Add(new V4DescribedPart(
                    asks,
                    item.GetProperty("own_case").GetBoolean(),
                    new V4Span(0, question.Length),
                    Locate(question, Text("citation")),
                    Locate(question, Text("act")),
                    Locate(question, Text("words")),
                    language,
                    Token(Text("date"), "today"),
                    Token(Text("range"), string.Empty),
                    concept,
                    Token(Text("act_ref"), string.Empty)));
            }

            if (parts.Count is < 1 or > 2)
            {
                return null;
            }

            if (parts.Count == 2)
            {
                // Two parts are two runs of the question, in order and apart; else the description is of no use.
                if (spans[0] is not { } first || spans[1] is not { } second || second.Start < first.End)
                {
                    return null;
                }

                parts[0] = parts[0] with { Span = first };
                parts[1] = parts[1] with { Span = second };
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
        var dates = V4Dispatcher.ReadingDates(row, V3AssistantParsers.DatesIn(input.MaskedQuestion));
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

    /// <summary>The words that open the title of an act after its date ("loi du 27 juillet 1997 sur le contrat d'assurance").</summary>
    private static readonly string[] TitleOpeners =
        ["sur ", "concernant ", "relative ", "relatif ", "relatives ", "relatifs ", "portant ", "modifiant ", "on ", "concerning ", "relating "];

    /// <summary>What became of the question call, as the record says it: a model described the question, or code did and why.</summary>
    public static readonly IReadOnlyList<string> Outcomes = ["described", "not_wired", "failed", "refused", "cited"];

    /// <summary>
    /// The plan of a citation the tables read themselves (ours: the fast path of a reference): the question cites one
    /// article of an act the corpus index names, so code opens that article on the date asked. No model and no search:
    /// the act's address and the article's identifier are the corpus's own.
    /// </summary>
    /// <param name="work">The act's address on this site.</param>
    /// <param name="article">The article's identifier in the publisher's text.</param>
    /// <param name="dateRef">"today", or the reference of the date the question asks to read the law on.</param>
    /// <param name="ownCase">The question is a person's own case: the part is proposed refused, and the article is shown beside the refusal.</param>
    public static string CitedPlanJson(V3PlannerInput input, string work, string article, string dateRef, string language, bool ownCase = false)
    {
        ArgumentNullException.ThrowIfNull(input);
        var part = new V4DescribedPart("article_or_act_cited", ownCase, new V4Span(0, input.MaskedQuestion.Length), null, null, null, language, dateRef, string.Empty, string.Empty);
        var step = new JsonObject
        {
            ["step_id"] = "s1",
            ["part_id"] = "p1",
            ["operation_id"] = "evidence_bundle",
            ["arguments"] = new JsonObject
            {
                ["article"] = new JsonObject { ["corpus"] = article },
                ["date"] = dateRef == "today" ? new JsonObject { ["today"] = true } : new JsonObject { ["ref"] = dateRef },
                ["identifier"] = new JsonObject { ["corpus"] = work },
            },
        };
        return new JsonObject
        {
            ["schema"] = V3AssistantPlanValidator.Schema2,
            ["parts"] = new JsonArray(new JsonObject { ["part_id"] = "p1", ["span"] = new JsonArray(0, input.MaskedQuestion.Length), ["proposed_verdict"] = ownCase ? "refuse" : "answer" }),
            ["steps"] = new JsonArray(step),
            ["follow_ups"] = new JsonArray(),
            ["bricks"] = Record([part], "cited", null),
        }.ToJsonString();
    }

    /// <summary>The bricks record of a plan of operations: who read the question, what it was read as, and the model call if one was made.</summary>
    private static JsonObject Record(IReadOnlyList<V4DescribedPart> parts, string outcome, (V4ModelCall Call, V4ModelReply Reply, string ModelId)? asked) => new()
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
        ["passes"] = new JsonArray(new JsonObject
        {
            ["n"] = 1,
            ["trigger"] = "first_pass",
            ["changed"] = "none",
            ["outcome"] = asked.HasValue ? "question_described" : outcome == "cited" ? "citation_read_by_code" : "question_read_by_code",
        }),
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

    /// <summary>
    /// The plan code writes from a description: per part, the operations its kind maps to, with every argument bound to a
    /// span of the question, a reference, a choice, today or an earlier result. The table is the whole of it; the model
    /// chose none of the operations, their order, their follow-ups or where an argument comes from. A span the plan cannot
    /// carry (outside its part, over a mask, too long) is dropped, and the kind falls to what it has left.
    /// </summary>
    /// <param name="outcome">One of <see cref="Outcomes"/>; "described" exactly when <paramref name="asked"/> is given.</param>
    /// <param name="asked">The call a model answered and its reply, or null when code described the question.</param>
    /// <param name="readingDates">The dates of the question that are dates to read the law on (<see cref="V4Dispatcher.ReadingDates"/>); null when every date it writes is one.</param>
    public static string PlanJson(
        V3PlannerInput input,
        IReadOnlyList<V4DescribedPart> parts,
        string outcome,
        (V4ModelCall Call, V4ModelReply Reply, string ModelId)? asked,
        IReadOnlyCollection<string>? readingDates = null,
        bool searchOnly = false,
        IReadOnlyList<V4ActMention>? acts = null,
        bool actAlone = false)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(parts);
        if (!Outcomes.Contains(outcome) || outcome == "cited" || (outcome == "described") != asked.HasValue)
        {
            throw new ArgumentException("Not an outcome of the question call.", nameof(outcome));
        }

        var question = input.MaskedQuestion;
        var references = input.References.ToDictionary(static reference => reference.Ref, static reference => reference.Kind, StringComparer.Ordinal);

        // How many days each range of the question spans: an operation over two dates has a cap (R45(f)), and a range wider
        // than the cap is not asked of it.
        var dates = V3AssistantParsers.DatesIn(question);
        var spans = V3AssistantParsers.RangesIn(question, dates).ToDictionary(
            static found => found.Ref,
            static found => DateOnly.ParseExact(found.To, "yyyy-MM-dd", CultureInfo.InvariantCulture).DayNumber - DateOnly.ParseExact(found.From, "yyyy-MM-dd", CultureInfo.InvariantCulture).DayNumber,
            StringComparer.Ordinal);
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

            // The date a part reads the law on: a date reference the question holds, unless code reads it as part of an
            // act's name ("law of 27 July 1997"), which is no date to read anything on; or the date chip of the last answer.
            JsonObject DateSource() =>
                part.Date != "today" && references.TryGetValue(part.Date, out var kind) &&
                    (kind == "context_date" || (kind == "date" && readingDates?.Contains(part.Date) != false))
                    ? new JsonObject { ["ref"] = part.Date }
                    : new JsonObject { ["today"] = true };
            var rangeName = part.Range.EndsWith(".from", StringComparison.Ordinal) ? part.Range[..^5] : part.Range.EndsWith(".to", StringComparison.Ordinal) ? part.Range[..^3] : part.Range;
            var range = rangeName.Length > 0 && references.ContainsKey(rangeName + ".from") && references.ContainsKey(rangeName + ".to") ? rangeName : null;
            bool Within(int cap) => range is not null && spans.TryGetValue(range, out var days) && days >= 0 && days <= cap;

            // The act of the person's last answer, when the part's own words name none and the description points at its chip.
            var chip = part.ActRef.Length > 0 && references.TryGetValue(part.ActRef, out var chipKind) && chipKind == "context_work" ? part.ActRef : null;
            // The act the corpus index reads in the part's words is named by its address: a reader's short name ("loi sur
            // le contrat d'assurance") is no identifier Lex's operations resolve (2026-11-19: "Lex ne reconnaît pas ...").
            JsonObject? Indexed()
            {
                var read = acts?.Where(static mention => mention.Work is not null).ToList();
                if (read is null || read.Count == 0)
                {
                    return null;
                }

                var hit = named is { } span
                    ? read.FirstOrDefault(mention => mention.Start < span.End && span.Start < mention.End)
                    : read.Select(static mention => mention.Work).Distinct(StringComparer.Ordinal).Count() == 1 ? read[0] : null;
                return hit is null || V4CorpusIndex.Insurance.Act(hit.Work!) is not { } indexed ? null : new JsonObject { ["corpus"] = indexed.Address };
            }

            JsonObject? ActSource() => Indexed() ?? (named is { } inWords ? SpanSource(inWords) : chip is not null ? new JsonObject { ["ref"] = chip } : null);
            var stepsBefore = steps.Count;

            // The citation code searches: the span the description points at, made whole by two rules of code. When the
            // description gives the article and the act apart ("l'article 39" and "loi du 27 juillet 1997 sur le contrat
            // d'assurance"), the citation is the reader's words from the first to the last of them. And a date inside an
            // act's name never ends a citation: when it stops at the end of a date the question writes and words of a
            // title follow ("sur le secteur des assurances"), it runs to the end of the part's own words. Several acts
            // share a date; only the title words tell them apart (2026-11-13: the search then asked which act was meant).
            V4Span? Cited()
            {
                if (citation is not { } given)
                {
                    return null;
                }

                if (act is { } beside)
                {
                    given = Usable(new V4Span(Math.Min(given.Start, beside.Start), Math.Max(given.End, beside.End)), V3AssistantPlanValidator.MaximumQueryLength) ?? given;
                }

                if (Own() is { } whole && given.Start >= whole.Start && given.End < whole.End &&
                    input.References.Any(reference => reference.Kind == "date" && reference.End == given.End))
                {
                    var rest = question[given.End..whole.End].TrimStart();
                    if (TitleOpeners.Any(opener => rest.StartsWith(opener, StringComparison.OrdinalIgnoreCase)))
                    {
                        given = Usable(new V4Span(given.Start, whole.End), V3AssistantPlanValidator.MaximumQueryLength) ?? given;
                    }
                }

                return given;
            }

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

                // A search reads a date only when the question asks one: the words are then matched in the versions of
                // that date. With none, they are matched in what Lex holds, and the article they name is read as of today.
                var date = DateSource();
                var arguments = new JsonObject { ["query"] = SpanSource(sought), ["language"] = new JsonObject { ["choice"] = language } };
                if (date.ContainsKey("ref"))
                {
                    arguments["date"] = date.DeepClone();
                }

                if (within is not null)
                {
                    arguments["identifier"] = within;
                }

                var step = Step("search", arguments);
                steps.Add(step);
                // An article cited by its number with no act named ("depuis quand l'article 62 existe-t-il ?") is found
                // in many acts: the search shows where, and nothing is read on its own (the reader's set of 2026-11-19:
                // the one hit of another act was quoted as the answer).
                if (!searchOnly && followUps.Count < V3AssistantPlanValidator.MaximumFollowUps)
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
                case "two_dates_compared" when ActSource() is { } compared && Within(V3AssistantPlanValidator.MaximumDiffDays):
                    steps.Add(Step("diff", new JsonObject { ["identifier"] = compared, ["date_from"] = new JsonObject { ["ref"] = range + ".from" }, ["date_to"] = new JsonObject { ["ref"] = range + ".to" } }));
                    break;
                case "changes_in_a_period" when Within(V3AssistantPlanValidator.MaximumChangesDays):
                    steps.Add(Step("changes_in_period", new JsonObject { ["date_from"] = new JsonObject { ["ref"] = range + ".from" }, ["date_to"] = new JsonObject { ["ref"] = range + ".to" } }));
                    break;
                case "exact_identifier" when Usable(part.Citation, V3AssistantPlanValidator.MaximumIdentifierLength) is { } identifier:
                    steps.Add(Step("resolve", new JsonObject { ["identifier"] = SpanSource(identifier) }));
                    break;
                case "article_or_act_cited" when actAlone && Indexed() is { } whole:
                    // An act of the index named alone ("loi assurance 1997"): its record is opened.
                    steps.Add(Step("dossier", new JsonObject { ["identifier"] = whole }));
                    break;
                default:
                    // A cited article or act, and every part whose kind lacks what it needs: the citation, else the words,
                    // else the part's own words, searched, and the one article they name read on the date asked. A citation
                    // that names no act ("et l'article 40 ?") is searched within the act of the person's last answer.
                    SearchThenRead(Cited() ?? words ?? act ?? Own(), citation is not null && act is null && chip is not null ? new JsonObject { ["ref"] = chip } : null);
                    break;
            }

            planParts.Add(new JsonObject
            {
                ["part_id"] = partId,
                ["span"] = new JsonArray(part.Span.Start, part.Span.End),
                ["proposed_verdict"] = part.OwnCase ? "refuse" : steps.Count == stepsBefore ? "point" : "answer",
            });
        }

        return new JsonObject
        {
            ["schema"] = V3AssistantPlanValidator.Schema2,
            ["parts"] = planParts,
            ["steps"] = new JsonArray(steps.Select(static step => (JsonNode)step).ToArray()),
            ["follow_ups"] = new JsonArray(followUps.Select(static step => (JsonNode)step).ToArray()),
            ["bricks"] = Record(parts, outcome, asked),
        }.ToJsonString();
    }
}
