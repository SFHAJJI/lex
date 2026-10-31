using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Lex.V3.Contracts.Platform.Assistant;

/// <summary>
/// The gates the code path checks before an answer is served (R30, R38, 36 §1.6): zero strings outside the template set,
/// quotations and question spans; every quotation bound to the text it was taken from; every disclosure its flags require;
/// the size cap. A failed gate is not served: ask answers the switched-off card. The machine gates' harness in the tests
/// checks the same properties again with code of its own.
/// </summary>
public static class V3AnswerGates
{
    /// <summary>The state kinds whose text is a consolidation, documentation without legal effect (R19; F30 §2).</summary>
    private static readonly HashSet<string> ConsolidatedKinds = new(StringComparer.Ordinal) { "consolidation", "code", "consolidated_version" };

    /// <summary>
    /// The disclosures a quotation's typed flags require (R38): its authenticity always; no legal effect for a
    /// consolidation; a provisional state; an article whose own date differs from its state's; a corrigendum, a derogation
    /// or a pre-application state where the operation's result marks one.
    /// </summary>
    public static IReadOnlyList<V3DisclosureFlag> RequiredDisclosures(V3Quote quote)
    {
        ArgumentNullException.ThrowIfNull(quote);
        var flags = new List<V3DisclosureFlag> { V3DisclosureFlag.Authenticity };
        if (ConsolidatedKinds.Contains(quote.StateKind))
        {
            flags.Add(V3DisclosureFlag.NoLegalEffect);
        }

        if (quote.Provisional && IsDate(quote.From))
        {
            flags.Add(V3DisclosureFlag.Provisional);
        }

        if (quote.ValidityConflict && IsDate(quote.ArticleValidFrom))
        {
            flags.Add(V3DisclosureFlag.ValidityConflict);
        }

        if (quote.Corrigendum)
        {
            flags.Add(V3DisclosureFlag.Corrigendum);
        }

        if (quote.Derogation)
        {
            flags.Add(V3DisclosureFlag.Derogation);
        }

        if (quote.PreApplication)
        {
            flags.Add(V3DisclosureFlag.PreApplication);
        }

        return flags;
    }

    /// <summary>Whether an answer passes every gate of the code path, given the outcomes its steps answered.</summary>
    public static bool Check(V3AnswerDossier dossier, IReadOnlyDictionary<string, V3StepOutcome> outcomes)
    {
        ArgumentNullException.ThrowIfNull(dossier);
        ArgumentNullException.ThrowIfNull(outcomes);
        var bytes = dossier.ToUtf8();
        return bytes.Length <= V3AnswerDossier.MaximumBytes
            && NonTemplateStrings(bytes) == 0
            && dossier.Quotes.All(quote => QuoteBound(quote, outcomes))
            && DisclosureComplete(dossier);
    }

    /// <summary>
    /// The visible strings of an answer that are not a template filled by code, a quotation or a span of the question
    /// (R31; 36 §1.6(d)): every member named <c>text</c> outside the trace and the refusal payloads, read back from the bytes.
    /// A claim counts unless its template, locale and facts render its text again exactly; a span counts unless it is the
    /// question's characters at its offsets; a quotation is checked by <see cref="QuoteBound"/>; any other holder counts.
    /// </summary>
    public static int NonTemplateStrings(ReadOnlySpan<byte> dossierUtf8)
    {
        using var document = JsonDocument.Parse(dossierUtf8.ToArray());
        var root = document.RootElement;
        var question = root.GetProperty("trace").GetProperty("question").GetString() ?? string.Empty;
        var count = 0;
        Walk(root, question, ref count);
        return count;
    }

    private static void Walk(JsonElement element, string question, ref int count)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (element.TryGetProperty("text", out var text))
                {
                    var kind = element.TryGetProperty("kind", out var kindElement) ? kindElement.GetString() : null;
                    var good = text.ValueKind == JsonValueKind.String && kind switch
                    {
                        "claim" => ClaimRendersAgain(element),
                        "question_span" => SpanIsQuestion(element, question),
                        "quote" => true,
                        _ => false,
                    };
                    if (!good)
                    {
                        count++;
                    }
                }

                foreach (var member in element.EnumerateObject())
                {
                    if (member.NameEquals("trace") || member.NameEquals("payload") || member.NameEquals("facts"))
                    {
                        continue;
                    }

                    Walk(member.Value, question, ref count);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    Walk(item, question, ref count);
                }

                break;
        }
    }

    private static bool ClaimRendersAgain(JsonElement claim)
    {
        try
        {
            var templateId = claim.GetProperty("template_id").GetString()!;
            var locale = claim.GetProperty("locale").GetString() switch
            {
                "fr" => V3AnswerLocale.Fr,
                "en" => V3AnswerLocale.En,
                _ => throw new ArgumentException("not a locale"),
            };
            var facts = claim.GetProperty("facts").EnumerateArray()
                .Select(static fact => new V3AnswerFact(
                    fact.GetProperty("name").GetString()!,
                    SlotKind(fact.GetProperty("kind").GetString()!),
                    fact.GetProperty("value").GetString()!))
                .ToArray();
            return string.Equals(V3AnswerClaim.Bind(templateId, locale, facts).Text, claim.GetProperty("text").GetString(), StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            return false;
        }
    }

    private static V3AnswerSlotKind SlotKind(string token) =>
        Enum.GetValues<V3AnswerSlotKind>().Single(kind => string.Equals(ContractWire.NameOf(kind), token, StringComparison.Ordinal));

    private static bool SpanIsQuestion(JsonElement span, string question) =>
        span.TryGetProperty("start", out var start) && span.TryGetProperty("end", out var end) &&
        start.TryGetInt32(out var from) && end.TryGetInt32(out var to) && from >= 0 && to <= question.Length && from < to &&
        string.Equals(question[from..to], span.GetProperty("text").GetString(), StringComparison.Ordinal);

    /// <summary>
    /// Whether a quotation is the exact characters at its offset of the article text its step answered, and that text's
    /// SHA-256 is the one it cites (36 §1.6(c), computed against the trace's own step and never judged).
    /// </summary>
    public static bool QuoteBound(V3Quote quote, IReadOnlyDictionary<string, V3StepOutcome> outcomes)
    {
        ArgumentNullException.ThrowIfNull(quote);
        ArgumentNullException.ThrowIfNull(outcomes);
        if (!outcomes.TryGetValue(quote.StepId, out var outcome) || outcome.Refused || outcome.Result is not { } result)
        {
            return false;
        }

        var containers = result.TryGetProperty("states", out var states) && states.ValueKind == JsonValueKind.Array ? states
            : result.TryGetProperty("wordings", out var wordings) && wordings.ValueKind == JsonValueKind.Array ? wordings
            : default;
        if (containers.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var container in containers.EnumerateArray())
        {
            if (!container.TryGetProperty("articles", out var articles) || articles.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var article in articles.EnumerateArray())
            {
                if (article.TryGetProperty("publisher_id", out var id) && id.GetString() == quote.Anchor &&
                    article.TryGetProperty("text", out var textElement) && textElement.GetString() is { } text &&
                    string.Equals(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text))), quote.ArticleTextSha256, StringComparison.Ordinal) &&
                    quote.Offset >= 0 && quote.Offset + quote.Length <= text.Length &&
                    string.Equals(text.Substring(quote.Offset, quote.Length), quote.Text, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Whether every quotation carries a disclosure block for each flag it requires (R38: 100 percent of the time).</summary>
    public static bool DisclosureComplete(V3AnswerDossier dossier)
    {
        ArgumentNullException.ThrowIfNull(dossier);
        var shown = dossier.Blocks.OfType<V3DisclosureBlock>().Select(static block => (block.ObjectId, block.Flag)).ToHashSet();
        return dossier.Quotes.All(quote => RequiredDisclosures(quote).All(flag => shown.Contains((quote.QuoteId, flag))));
    }

    private static bool IsDate(string? value) =>
        value is { Length: 10 } && DateOnly.TryParseExact(value, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _);
}
