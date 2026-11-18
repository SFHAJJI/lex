using System.Globalization;
using System.Text.Json;
using Lex.V3.Contracts.Platform.Assistant.Bricks;

namespace Lex.V3.Contracts.Platform.Assistant;

/// <summary>
/// The composer of a bricks answer (plan /2, V4): the same fixed wordings, quotations bound to the stored text, notices,
/// handoffs and gates as every answer, laid out from the bricks record. A quotation here is one line of the law (an extract
/// at a position of the article's stored text) and no longer the whole article; a value shown is the exact words on a quoted
/// line. "The snippet the user reads is always the recovered one, never the model's." [8A]
/// </summary>
public static partial class V3AssistantPipeline
{
    /// <summary>The article a step read, in the text the plan's extracts were cut from: its row and its container, or null when that text is not the one answered.</summary>
    private static (JsonElement Container, JsonElement Item, JsonElement Result)? ExtractSource(Executed item)
    {
        if (item.Outcome is not { Refused: false, Result: { } result, OperationId: "evidence_bundle" } || !item.Bound.TryGetValue("article", out var article))
        {
            return null;
        }

        foreach (var container in Items(result, Text(result, "publisher") == "eu-eurlex" ? "wordings" : "states"))
        {
            foreach (var row in Items(container, "articles"))
            {
                if (Text(row, "publisher_id") == article.Value && Text(row, "text") is { Length: > 0 } && Text(row, "text_sha256") == item.Step.TextSha256)
                {
                    return (container, row, result);
                }
            }
        }

        return null;
    }

    /// <summary>One quotation per extract of a step, or null when an extract is not inside the text the step answered.</summary>
    private static List<V3Quote>? ExtractQuotes(Executed item, Func<string> nextId)
    {
        if (ExtractSource(item) is not { } source)
        {
            return null;
        }

        var (container, row, result) = source;
        var text = Text(row, "text")!;
        var publisher = Text(result, "publisher") ?? "lu-legilux";
        var work = WorkAddress(publisher, Text(result, "work_key") ?? "unknown");
        var journal = (Member(result, "as_published") is { } asPublished ? Text(asPublished, "journal_officiel_iri") : null)
            ?? (Member(result, "citation_facts") is { } facts ? Text(facts, "journal_officiel_iri") : null);
        var corrigenda = Items(container, "corrigenda").Length > 0 || Items(result, "corrigenda").Length > 0;
        var quotes = new List<V3Quote>();
        foreach (var extract in item.Step.Extracts!)
        {
            var end = extract.Offset + extract.Length;
            if (end > text.Length || char.IsLowSurrogate(text[extract.Offset]) || (end < text.Length && char.IsLowSurrogate(text[end])))
            {
                return null;
            }

            quotes.Add(new V3Quote(
                nextId(),
                item.Step.StepId,
                publisher,
                work,
                item.Bound["article"].Value,
                Text(row, "language") ?? Text(container, "language") ?? "fra",
                text.Substring(extract.Offset, extract.Length),
                extract.Offset,
                extract.Length,
                extract.Length < text.Length,
                Text(row, "text_sha256")!,
                Text(container, "state_sha256") ?? string.Empty,
                Text(container, "applicability_date") ?? string.Empty,
                Text(container, "applicable_to"),
                Text(container, "date_role") ?? "applicability",
                Text(container, "permalink") ?? string.Empty,
                Text(row, "article_permalink") ?? Text(container, "permalink") ?? string.Empty,
                Text(row, "official_source") ?? journal ?? string.Empty,
                journal,
                Text(container, "state_kind") ?? string.Empty,
                True(container, "provisional"),
                True(row, "validity_conflict"),
                Text(row, "article_valid_from"),
                corrigenda,
                Items(container, "derogations").Length > 0,
                True(container, "pre_application"),
                extract.Breaks.Count > 0 ? extract.Breaks : null));
        }

        return quotes;
    }

    private static V3AnswerFact Count(string name, int value) => new(name, V3AnswerSlotKind.Count, value.ToString(CultureInfo.InvariantCulture));

    private static V3AnswerDossier? ComposeBricks(
        V3AssistantPlan plan,
        V3PlanContext context,
        V3AnswerLocale locale,
        IReadOnlyList<Executed> executed,
        V3AssistantEnvironment environment,
        V3AnswerTrace trace)
    {
        var record = plan.Bricks!;
        if (plan.Parts.Count != 1 || plan.FollowUps.Count != 0)
        {
            return null;
        }

        var question = context.Intake.Text;
        var part = plan.Parts[0];
        var cues = V3AssistantParsers.ApplicationCues(question, part.Span.Start, part.Span.End);
        var application = AsksApplication(question, part, cues);
        var dictionary = V4Dictionary.Insurance;
        var date = record.DateRef == "today" ? environment.Today : context.Value(record.DateRef, "date") ?? environment.Today;

        var quoteNumber = 0;
        string NextQuoteId() => "q" + (++quoteNumber).ToString(CultureInfo.InvariantCulture);
        var quotes = new Dictionary<(string Step, int Extract), V3Quote>();
        var headings = new Dictionary<string, string?>(StringComparer.Ordinal);
        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        var publishers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in executed.Where(static item => item.Step.Extracts is not null))
        {
            if (ExtractQuotes(item, NextQuoteId) is not { } made)
            {
                return null;
            }

            for (var at = 0; at < made.Count; at++)
            {
                quotes[(item.Step.StepId, at)] = made[at];
                publishers.Add(made[at].Publisher);
            }

            var text = Text(ExtractSource(item)!.Value.Item, "text")!;
            texts[item.Step.StepId] = text;
            headings[item.Step.StepId] = item.Step.Heading is { } heading && heading.End <= text.Length && !string.IsNullOrWhiteSpace(text[heading.Start..heading.End])
                ? text[heading.Start..heading.End]
                : null;
        }

        if (quotes.Count > MaximumQuotes || record.Items.Any(item => !quotes.ContainsKey((item.Step, item.Extract))) ||
            record.Values.Any(value => !texts.TryGetValue(value.Step, out var text) || value.Offset + value.Length > text.Length))
        {
            return null;
        }

        IEnumerable<V3DossierBlock> Shown(V4AnswerItem item)
        {
            var quote = quotes[(item.Step, item.Extract)];
            foreach (var block in QuoteBlocks(quote, date, locale, headings[item.Step]))
            {
                yield return block;
            }

            // A value is restated only as the exact words written on the quoted line.
            foreach (var value in record.Values.Where(value => value.Step == item.Step && value.Offset >= quote.Offset && value.Offset + value.Length <= quote.Offset + quote.Length))
            {
                yield return Claim(V3AnswerTemplates.AnswerValueWords, locale,
                    Fact("value", V3AnswerSlotKind.LawWords, texts[value.Step].Substring(value.Offset, value.Length)));
            }
        }

        IEnumerable<V3DossierBlock> SweepLines()
        {
            if (record.Work is null)
            {
                yield break;
            }

            yield return Claim(V3AnswerTemplates.AbsenceSweepHeading, locale,
                Fact("work", V3AnswerSlotKind.WorkAddress, record.Work), Count("lines", record.Lines), Count("articles", record.Articles), Date("date", date));
            foreach (var row in record.Sweep.Take(12))
            {
                var keyword = dictionary.Keyword(row.Keyword)!;
                if (!keyword.Pattern)
                {
                    yield return Claim(V3AnswerTemplates.AbsenceTermCount, locale,
                        Fact("term", V3AnswerSlotKind.DictionaryTerm, keyword.Keyword), Count("lines", row.Lines), Count("articles", row.Articles));
                }
            }
        }

        var blocks = new List<V3DossierBlock>();
        V3AssistantVerdict verdict;
        if (application)
        {
            // The descriptive maximum (S4-A05): the law the bricks found, verbatim and dated, beside the refusal; nothing else.
            verdict = V3AssistantVerdict.Refuse;
            blocks.Add(record.Items.Count > 0
                ? Claim(V3AnswerTemplates.BoundaryWholeNoAdviser, locale, Date("date", date))
                : Claim(V3AnswerTemplates.BoundaryWholeNoText, locale));
            blocks.AddRange(record.Items.SelectMany(Shown));
            blocks.AddRange(Handoffs(V3HandoffRole.Information, locale));
        }
        else if (record.Kind == "greeting")
        {
            verdict = V3AssistantVerdict.Clarify;
            blocks.Add(Claim(V3AnswerTemplates.DirectGreeting, locale));
        }
        else if (record.Kind == "not_held")
        {
            if (record.EuAct is null || V4CorpusIndex.Insurance.EuAct(record.EuAct) is not { } act)
            {
                return null;
            }

            verdict = V3AssistantVerdict.Point;
            blocks.Add(Claim(V3AnswerTemplates.AbsenceActNotHeld, locale, Fact("name", V3AnswerSlotKind.DictionaryTerm, locale == V3AnswerLocale.Fr ? act.NameFr : act.NameEn)));
            blocks.Add(Claim(V3AnswerTemplates.LawOfficialSource, locale, Fact("uri", V3AnswerSlotKind.Uri, act.Official)));
            blocks.AddRange(OfficialSources(["eu-eurlex"], locale));
        }
        else if (record.Kind == "absence")
        {
            switch (record.Absence)
            {
                case "text_not_held" when record.Work is not null:
                    verdict = V3AssistantVerdict.Point;
                    blocks.Add(Claim(V3AnswerTemplates.AbsenceTextNotHeld, locale, Fact("work", V3AnswerSlotKind.WorkAddress, record.Work)));
                    blocks.AddRange(OfficialSources(["lu-legilux"], locale));
                    break;
                case "terms_nowhere":
                    verdict = V3AssistantVerdict.Answer;
                    blocks.AddRange(SweepLines());
                    blocks.Add(Claim(V3AnswerTemplates.AbsenceTermsNowhere, locale));
                    break;
                case "words_not_found":
                    verdict = V3AssistantVerdict.Clarify;
                    blocks.AddRange(SweepLines());
                    blocks.Add(Claim(V3AnswerTemplates.AbsenceWordsNotFound, locale));
                    break;
                case "no_answer_in_lines" when record.Items.Count > 0:
                    verdict = V3AssistantVerdict.Answer;
                    blocks.Add(Claim(V3AnswerTemplates.AbsenceNoAnswerInLines, locale));
                    blocks.AddRange(record.Items.SelectMany(Shown));
                    break;
                default:
                    return null;
            }
        }
        else
        {
            if (record.Items.Count == 0)
            {
                return null;
            }

            verdict = V3AssistantVerdict.Answer;
            var first = record.Items.Where(static item => item.Role is not "case").ToList();
            var cases = record.Items.Where(static item => item.Role == "case").ToList();
            if (record.AnswerCaveats.Contains("not_defined"))
            {
                blocks.Add(Claim(V3AnswerTemplates.CaveatNotDefined, locale));
            }
            else
            {
                blocks.Add(Claim(record.Kind switch
                {
                    "definition" => V3AnswerTemplates.AnswerDefinition,
                    "rule" => V3AnswerTemplates.AnswerRule,
                    "duration" or "date" or "amount" => first.Count > 1 ? V3AnswerTemplates.AnswerSeveral : V3AnswerTemplates.AnswerValue,
                    "list" => V3AnswerTemplates.AnswerList,
                    "candidates" => V3AnswerTemplates.AnswerCandidates,
                    _ => V3AnswerTemplates.AnswerPassages,
                }, locale));
            }

            blocks.AddRange(first.SelectMany(Shown));
            if (cases.Count > 0)
            {
                blocks.Add(Claim(V3AnswerTemplates.AnswerRuleCases, locale));
                blocks.AddRange(cases.SelectMany(Shown));
            }
        }

        if (!application)
        {
            if (record.Usage is { } usage)
            {
                blocks.Add(Claim(V3AnswerTemplates.AnswerTermUsage, locale, Count("lines", usage.Lines), Count("articles", usage.Articles)));
            }

            if (record.More.Count > 0 && record.Work is not null)
            {
                blocks.Add(Claim(V3AnswerTemplates.AnswerMoreArticles, locale));
                foreach (var article in record.More)
                {
                    blocks.Add(Action("evidence_bundle", new() { ["identifier"] = record.Work, ["date"] = date, ["article"] = article }, V3AnswerTemplates.ActionReadArticle, locale,
                        Fact("anchor", V3AnswerSlotKind.Anchor, article), Fact("work", V3AnswerSlotKind.WorkAddress, record.Work), Date("date", date)));
                }
            }

            foreach (var caveat in record.AnswerCaveats)
            {
                var template = caveat switch
                {
                    "conditions_apply" => V3AnswerTemplates.CaveatConditionsApply,
                    "left_to_contract" => V3AnswerTemplates.CaveatLeftToContract,
                    "cases_from_sweep" => V3AnswerTemplates.CaveatCasesFromSweep,
                    "list_from_sweep" => V3AnswerTemplates.CaveatListFromSweep,
                    "general_and_special" => V3AnswerTemplates.CaveatGeneralAndSpecial,
                    "vocabulary_incomplete" => V3AnswerTemplates.CaveatVocabularyIncomplete,
                    _ => null,
                };
                if (template is not null)
                {
                    blocks.Add(Claim(template, locale));
                }
            }

            foreach (var note in record.Notes)
            {
                var words = Fact("words", V3AnswerSlotKind.QuestionSpan, question[note.Span.Start..note.Span.End]);
                if (note.Code == "law_term")
                {
                    var concept = dictionary.Concept(note.Concept!)!;
                    blocks.Add(Claim(V3AnswerTemplates.NoteLawTerm, locale, words, Fact("term", V3AnswerSlotKind.DictionaryTerm, locale == V3AnswerLocale.Fr ? concept.LabelFr : concept.LabelEn)));
                }
                else
                {
                    blocks.Add(Claim(V3AnswerTemplates.NoteWordNotInAct, locale, words));
                }
            }
        }

        var label = Claim(verdict switch
        {
            V3AssistantVerdict.Answer => V3AnswerTemplates.VerdictAnswer,
            V3AssistantVerdict.Point => V3AnswerTemplates.VerdictPoint,
            V3AssistantVerdict.Clarify => V3AnswerTemplates.VerdictClarify,
            _ => V3AnswerTemplates.VerdictRefuse,
        }, locale);
        var notices = new List<V3AnswerClaim>();
        if (V3AnswerTemplates.Preview)
        {
            notices.Add(V3AnswerClaim.Bind(V3AnswerTemplates.NoticePreview, locale));
        }

        notices.Add(V3AnswerClaim.Bind(record.ModelSteps.Count == 0 ? V3AnswerTemplates.NoticeNoModel : V3AnswerTemplates.NoticeModelAssisted, locale));
        notices.Add(V3AnswerClaim.Bind(V3AnswerTemplates.NoticeNotLegalAdvice, locale));

        var parts = new[] { new V3DossierPart(part.PartId, part.Span, application, cues, verdict, blocks) };
        return new V3AnswerDossier(verdict, locale, label.Claim, notices, BricksPlanLines(record, question, locale, executed, environment.Today, date), parts, trace);
    }

    /// <summary>
    /// The plan the reader sees for a bricks answer (their audit block: "This isn't optional. It's what makes the system
    /// auditable." [6C]): how the question was read, the route and its reason, what each detector found, which model was
    /// asked for what, then the operations run.
    /// </summary>
    private static List<V3AnswerClaim> BricksPlanLines(V4PlanRecord record, string question, V3AnswerLocale locale, IReadOnlyList<Executed> executed, string today, string date)
    {
        var dictionary = V4Dictionary.Insurance;
        var lines = new List<V3AnswerClaim> { V3AnswerClaim.Bind(V3AnswerTemplates.PlanHeading, locale) };
        foreach (var read in record.Concepts.Concat(record.Roles).OrderBy(static read => read.Span.Start))
        {
            var concept = dictionary.Concept(read.Concept)!;
            var label = locale == V3AnswerLocale.Fr ? concept.LabelFr : concept.LabelEn;
            if (string.Equals(V4Text.Fold(question[read.Span.Start..read.Span.End]), V4Text.Fold(label), StringComparison.Ordinal))
            {
                // The reader wrote the term itself: there is nothing to say of how it was read.
                continue;
            }

            lines.Add(V3AnswerClaim.Bind(V3AnswerTemplates.PlanBricksTerm, locale,
                Fact("words", V3AnswerSlotKind.QuestionSpan, question[read.Span.Start..read.Span.End]),
                Fact("term", V3AnswerSlotKind.DictionaryTerm, locale == V3AnswerLocale.Fr ? concept.LabelFr : concept.LabelEn)));
        }

        if (record.RuleQuestion)
        {
            lines.Add(V3AnswerClaim.Bind(V3AnswerTemplates.PlanBricksRuleQuestion, locale));
        }

        var kindLine = record.Asked switch
        {
            "definition" => V3AnswerTemplates.PlanBricksKindDefinition,
            "rule" => V3AnswerTemplates.PlanBricksKindRule,
            "duration" => V3AnswerTemplates.PlanBricksKindDuration,
            "date" => V3AnswerTemplates.PlanBricksKindDate,
            "amount" => V3AnswerTemplates.PlanBricksKindAmount,
            "list" => V3AnswerTemplates.PlanBricksKindList,
            "reference" => V3AnswerTemplates.PlanBricksKindReference,
            "text" => V3AnswerTemplates.PlanBricksKindText,
            _ => null,
        };
        if (kindLine is not null)
        {
            lines.Add(V3AnswerClaim.Bind(kindLine, locale));
        }

        if (record.Work is not null)
        {
            if (record.DateRef == "today")
            {
                lines.Add(V3AnswerClaim.Bind(V3AnswerTemplates.PlanDateToday, locale, Date("date", today)));
            }

            if (record.Lines > 0)
            {
                lines.Add(V3AnswerClaim.Bind(V3AnswerTemplates.PlanBricksScope, locale,
                    Fact("work", V3AnswerSlotKind.WorkAddress, record.Work), Date("date", date), Count("articles", record.Articles), Count("lines", record.Lines)));
            }
        }

        foreach (var detector in record.Detectors)
        {
            switch (detector.Id)
            {
                case "title" when detector.Articles > 0:
                    lines.Add(V3AnswerClaim.Bind(V3AnswerTemplates.PlanBricksDetectorTitle, locale, Count("articles", detector.Articles)));
                    break;
                case "keyword":
                    lines.Add(V3AnswerClaim.Bind(V3AnswerTemplates.PlanBricksDetectorKeyword, locale, Count("lines", detector.Lines), Count("articles", detector.Articles)));
                    break;
                case "cooccurrence":
                    lines.Add(V3AnswerClaim.Bind(V3AnswerTemplates.PlanBricksDetectorCooccurrence, locale, Count("lines", detector.Lines), Count("articles", detector.Articles)));
                    break;
                case "type_pattern":
                    lines.Add(V3AnswerClaim.Bind(V3AnswerTemplates.PlanBricksDetectorTypePattern, locale, Count("lines", detector.Lines), Count("articles", detector.Articles)));
                    break;
            }
        }

        if (record.Route is "definition")
        {
            lines.Add(V3AnswerClaim.Bind(V3AnswerTemplates.PlanBricksRouteDefinition, locale));
        }
        else if (record.Route is "fact")
        {
            lines.Add(V3AnswerClaim.Bind(V3AnswerTemplates.PlanBricksRouteFact, locale));
        }

        foreach (var pass in record.Passes)
        {
            var template = pass.Trigger switch
            {
                "unnamed_cases" => V3AnswerTemplates.PlanBricksPassSweepCases,
                "partial_answer" => V3AnswerTemplates.PlanBricksPassWider,
                "validator_rejected" => V3AnswerTemplates.PlanBricksPassRetry,
                _ => null,
            };
            if (template is not null)
            {
                lines.Add(V3AnswerClaim.Bind(template, locale));
            }
        }

        if (record.ModelSteps.Count == 0)
        {
            lines.Add(V3AnswerClaim.Bind(V3AnswerTemplates.PlanBricksModelNone, locale));
        }
        else
        {
            foreach (var step in record.ModelSteps.Distinct(StringComparer.Ordinal))
            {
                var template = step switch
                {
                    "question" => V3AnswerTemplates.PlanBricksModelQuestion,
                    "arbiter" => V3AnswerTemplates.PlanBricksModelArbiter,
                    "extraction" => V3AnswerTemplates.PlanBricksModelExtraction,
                    _ => null,
                };
                if (template is not null)
                {
                    lines.Add(V3AnswerClaim.Bind(template, locale));
                }
            }
        }

        if (record.AnswerCaveats.Contains("model_unavailable"))
        {
            lines.Add(V3AnswerClaim.Bind(V3AnswerTemplates.PlanBricksModelUnavailable, locale));
        }

        lines.AddRange(executed.Where(static item => item.Outcome is not null).Select(item => StepLine(item, locale)));
        return lines;
    }
}
