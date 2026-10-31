using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Lex.V3.Contracts.Platform.Assistant;

/// <summary>
/// One block of an answer part (docs/ANSWER-DOSSIER-1.md §5), a closed union. Three kinds are visible text (a claim, a
/// quotation, a span of the question); the four others carry their visible sentence as a claim beside typed data.
/// </summary>
public abstract class V3DossierBlock
{
    private protected V3DossierBlock()
    {
    }

    /// <summary>The block's kind on the wire: claim, quote, question_span, refusal, action, handoff or disclosure.</summary>
    public abstract string Kind { get; }

    internal abstract void Write(Utf8JsonWriter writer);

    internal static void WriteClaim(Utf8JsonWriter writer, V3AnswerClaim claim)
    {
        writer.WriteStartObject();
        writer.WriteString("kind", "claim");
        writer.WriteString("template_id", claim.TemplateId);
        writer.WriteString("locale", ContractWire.NameOf(claim.Locale));
        writer.WriteStartArray("facts");
        foreach (var fact in claim.Facts)
        {
            writer.WriteStartObject();
            writer.WriteString("name", fact.Name);
            writer.WriteString("kind", ContractWire.NameOf(fact.Kind));
            writer.WriteString("value", fact.Value);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteString("text", claim.Text);
        writer.WriteEndObject();
    }
}

/// <summary>A sentence from the closed template set, filled by code.</summary>
public sealed class V3ClaimBlock(V3AnswerClaim claim) : V3DossierBlock
{
    public V3AnswerClaim Claim { get; } = claim ?? throw new ArgumentNullException(nameof(claim));

    public override string Kind => "claim";

    internal override void Write(Utf8JsonWriter writer) => WriteClaim(writer, Claim);
}

/// <summary>
/// A verbatim quotation of held text (R31): the exact characters at <see cref="Offset"/> of an article's text, with that
/// text's SHA-256, the state's interval and permalink, and the official source.
/// </summary>
public sealed record V3Quote(
    string QuoteId,
    string StepId,
    string Publisher,
    string Work,
    string Anchor,
    string Language,
    string Text,
    int Offset,
    int Length,
    bool Shortened,
    string ArticleTextSha256,
    string StateSha256,
    string From,
    string? To,
    string DateRole,
    string StatePermalink,
    string ArticlePermalink,
    string OfficialSource,
    string? JournalOfficiel,
    string StateKind,
    bool Provisional,
    bool ValidityConflict,
    string? ArticleValidFrom,
    bool Corrigendum,
    bool Derogation,
    bool PreApplication);

/// <summary>A quotation block.</summary>
public sealed class V3QuoteBlock(V3Quote quote) : V3DossierBlock
{
    public V3Quote Quote { get; } = quote ?? throw new ArgumentNullException(nameof(quote));

    public override string Kind => "quote";

    internal override void Write(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString("kind", "quote");
        writer.WriteString("quote_id", Quote.QuoteId);
        writer.WriteString("step_id", Quote.StepId);
        writer.WriteString("publisher", Quote.Publisher);
        writer.WriteString("work", Quote.Work);
        writer.WriteString("anchor", Quote.Anchor);
        writer.WriteString("language", Quote.Language);
        writer.WriteString("text", Quote.Text);
        writer.WriteNumber("offset", Quote.Offset);
        writer.WriteNumber("length", Quote.Length);
        writer.WriteBoolean("shortened", Quote.Shortened);
        writer.WriteString("article_text_sha256", Quote.ArticleTextSha256);
        writer.WriteString("state_sha256", Quote.StateSha256);
        writer.WriteString("from", Quote.From);
        writer.WriteString("to", Quote.To);
        writer.WriteString("date_role", Quote.DateRole);
        writer.WriteString("state_kind", Quote.StateKind);
        writer.WriteString("state_permalink", Quote.StatePermalink);
        writer.WriteString("article_permalink", Quote.ArticlePermalink);
        writer.WriteString("official_source", Quote.OfficialSource);
        writer.WriteString("journal_officiel", Quote.JournalOfficiel);
        writer.WriteEndObject();
    }
}

/// <summary>A verbatim span [start, end) of the user's question.</summary>
public sealed class V3QuestionSpanBlock(int start, int end, string text) : V3DossierBlock
{
    public int Start { get; } = start;

    public int End { get; } = end;

    public string Text { get; } = text ?? throw new ArgumentNullException(nameof(text));

    public override string Kind => "question_span";

    internal override void Write(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString("kind", "question_span");
        writer.WriteNumber("start", Start);
        writer.WriteNumber("end", End);
        writer.WriteString("text", Text);
        writer.WriteEndObject();
    }
}

/// <summary>A typed refusal an operation answered, kept visible (R35), with its payload as data and its sentence as a claim.</summary>
public sealed class V3RefusalBlock(string stepId, string code, JsonElement payload, V3AnswerClaim claim) : V3DossierBlock
{
    public string StepId { get; } = stepId;

    public string Code { get; } = code;

    public JsonElement Payload { get; } = payload.Clone();

    public V3AnswerClaim Claim { get; } = claim ?? throw new ArgumentNullException(nameof(claim));

    public override string Kind => "refusal";

    internal override void Write(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString("kind", "refusal");
        writer.WriteString("step_id", StepId);
        writer.WriteString("code", Code);
        writer.WritePropertyName("payload");
        Payload.WriteTo(writer);
        writer.WritePropertyName("claim");
        WriteClaim(writer, Claim);
        writer.WriteEndObject();
    }
}

/// <summary>A next step offered as an action, never run by itself (R35: nearest states are offered as actions).</summary>
public sealed class V3ActionBlock(string operationId, IReadOnlyDictionary<string, string> parameters, V3AnswerClaim claim) : V3DossierBlock
{
    public string OperationId { get; } = operationId;

    public IReadOnlyDictionary<string, string> Parameters { get; } = new SortedDictionary<string, string>(
        parameters.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal), StringComparer.Ordinal);

    public V3AnswerClaim Claim { get; } = claim ?? throw new ArgumentNullException(nameof(claim));

    public override string Kind => "action";

    internal override void Write(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString("kind", "action");
        writer.WriteString("operation_id", OperationId);
        writer.WriteStartObject("parameters");
        foreach (var (name, value) in Parameters)
        {
            writer.WriteString(name, value);
        }

        writer.WriteEndObject();
        writer.WritePropertyName("claim");
        WriteClaim(writer, Claim);
        writer.WriteEndObject();
    }
}

/// <summary>A handoff to an entry of the registry (R42), with whether a person reviewed the entry.</summary>
public sealed class V3HandoffBlock(V3HandoffEntry entry, V3AnswerClaim claim) : V3DossierBlock
{
    public V3HandoffEntry Entry { get; } = entry ?? throw new ArgumentNullException(nameof(entry));

    public V3AnswerClaim Claim { get; } = claim ?? throw new ArgumentNullException(nameof(claim));

    public override string Kind => "handoff";

    internal override void Write(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString("kind", "handoff");
        writer.WriteString("entry_id", Entry.Id);
        writer.WriteString("role", ContractWire.NameOf(Entry.Role));
        writer.WriteString("link", Entry.Link);
        writer.WriteBoolean("reviewed_by_person", Entry.ReviewedByPerson);
        writer.WritePropertyName("claim");
        WriteClaim(writer, Claim);
        writer.WriteEndObject();
    }
}

/// <summary>A disclosure of a typed flag on a served object (R38), naming the object.</summary>
public sealed class V3DisclosureBlock(V3DisclosureFlag flag, string objectId, V3AnswerClaim claim) : V3DossierBlock
{
    public V3DisclosureFlag Flag { get; } = flag;

    public string ObjectId { get; } = objectId;

    public V3AnswerClaim Claim { get; } = claim ?? throw new ArgumentNullException(nameof(claim));

    public override string Kind => "disclosure";

    internal override void Write(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString("kind", "disclosure");
        writer.WriteString("flag", ContractWire.NameOf(Flag));
        writer.WriteString("object", ObjectId);
        writer.WritePropertyName("claim");
        WriteClaim(writer, Claim);
        writer.WriteEndObject();
    }
}

/// <summary>One part of the answer: its span, whether it asks for application and why, its verdict and its blocks.</summary>
public sealed record V3DossierPart(
    string PartId,
    V3PlanSpan Span,
    bool Application,
    IReadOnlyList<V3ApplicationCue> Cues,
    V3AssistantVerdict Verdict,
    IReadOnlyList<V3DossierBlock> Blocks);

/// <summary>
/// One step of the trace: the operation, the parameters code bound, and the digest of what it answered (its result or its
/// refusal, never its envelope's context, whose clock moves). A follow-up whose condition did not hold is recorded unfired.
/// </summary>
public sealed record V3TraceStep(
    string StepId,
    string OperationId,
    bool Fired,
    string? ParametersJson,
    string Outcome,
    string? ObjectType,
    string? Verdict,
    string? RefusalCode,
    string? ResultSha256);

/// <summary>
/// The trace (<c>lex-v3-answer-trace/1</c>, R32): everything a client needs to replay an answer against the pinned
/// snapshot, sent to the client and kept nowhere on the server.
/// </summary>
public sealed record V3AnswerTrace(
    string Question,
    V3AnswerLocale AnswerLocale,
    IReadOnlyList<V3ContextChip> Context,
    string PlannerId,
    V3AssistantPlan Plan,
    string Today,
    string SnapshotId,
    string SnapshotSha256,
    string RegistrySha256,
    IReadOnlyList<V3TraceStep> Steps)
{
    public const string Schema = "lex-v3-answer-trace/1";

    public string QuestionSha256 => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Question)));

    internal void Write(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString("schema", Schema);
        writer.WriteString("question", Question);
        writer.WriteString("question_sha256", QuestionSha256);
        writer.WriteString("answer_locale", ContractWire.NameOf(AnswerLocale));
        writer.WriteStartArray("context");
        foreach (var chip in Context)
        {
            writer.WriteStartObject();
            writer.WriteString("kind", chip.Kind);
            writer.WriteString("value", chip.Value);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteString("planner_id", PlannerId);
        writer.WritePropertyName("plan");
        using (var plan = JsonDocument.Parse(Plan.CanonicalUtf8))
        {
            plan.RootElement.WriteTo(writer);
        }

        writer.WriteString("plan_sha256", Plan.Sha256);
        writer.WriteString("today", Today);
        writer.WriteStartObject("snapshot");
        writer.WriteString("snapshot_id", SnapshotId);
        writer.WriteString("snapshot_sha256", SnapshotSha256);
        writer.WriteEndObject();
        writer.WriteString("registry_sha256", RegistrySha256);
        writer.WriteString("templates_sha256", V3AnswerTemplates.Sha256);
        writer.WriteString("handoff_registry_sha256", V3HandoffRegistry.Sha256);
        writer.WriteStartArray("steps");
        foreach (var step in Steps)
        {
            writer.WriteStartObject();
            writer.WriteString("step_id", step.StepId);
            writer.WriteString("operation_id", step.OperationId);
            writer.WriteBoolean("fired", step.Fired);
            writer.WritePropertyName("parameters");
            if (step.ParametersJson is null)
            {
                writer.WriteNullValue();
            }
            else
            {
                using var parameters = JsonDocument.Parse(step.ParametersJson);
                parameters.RootElement.WriteTo(writer);
            }

            writer.WriteString("outcome", step.Outcome);
            writer.WriteString("object_type", step.ObjectType);
            writer.WriteString("verdict", step.Verdict);
            writer.WriteString("refusal_code", step.RefusalCode);
            writer.WriteString("result_sha256", step.ResultSha256);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }
}

/// <summary>
/// An answer, <c>lex-v3-answer-dossier/1</c> (docs/ANSWER-DOSSIER-1.md): the verdict code computed, its label, the two
/// notices (R33, R50), the validated plan rendered first (R45(c)), the parts, the gates the code path checked and the
/// trace. Its canonical bytes (<see cref="ToUtf8"/>) are what a replay reproduces byte for byte.
/// </summary>
public sealed class V3AnswerDossier
{
    public const string Schema = "lex-v3-answer-dossier/1";

    /// <summary>The largest answer served, in UTF-8 bytes (R45(f)); a larger one is not served.</summary>
    public const int MaximumBytes = 256 * 1024;

    internal V3AnswerDossier(
        V3AssistantVerdict verdict,
        V3AnswerLocale locale,
        V3AnswerClaim label,
        IReadOnlyList<V3AnswerClaim> notices,
        IReadOnlyList<V3AnswerClaim> planLines,
        IReadOnlyList<V3DossierPart> parts,
        V3AnswerTrace trace)
    {
        Verdict = verdict;
        Locale = locale;
        Label = label;
        Notices = notices.ToList().AsReadOnly();
        PlanLines = planLines.ToList().AsReadOnly();
        Parts = parts.ToList().AsReadOnly();
        Trace = trace;
    }

    public V3AssistantVerdict Verdict { get; }

    public V3AnswerLocale Locale { get; }

    public V3AnswerClaim Label { get; }

    public ReadOnlyCollection<V3AnswerClaim> Notices { get; }

    public ReadOnlyCollection<V3AnswerClaim> PlanLines { get; }

    public ReadOnlyCollection<V3DossierPart> Parts { get; }

    public V3AnswerTrace Trace { get; }

    public IEnumerable<V3DossierBlock> Blocks => Parts.SelectMany(static part => part.Blocks);

    public IEnumerable<V3Quote> Quotes => Blocks.OfType<V3QuoteBlock>().Select(static block => block.Quote);

    /// <summary>The canonical bytes: UTF-8, members in the contract's order, no indentation, non-ASCII letters unescaped.</summary>
    public byte[] ToUtf8()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", Schema);
            writer.WriteString("verdict", ContractWire.NameOf(Verdict));
            writer.WriteString("answer_locale", ContractWire.NameOf(Locale));
            writer.WritePropertyName("label");
            V3DossierBlock.WriteClaim(writer, Label);
            writer.WriteStartArray("notices");
            foreach (var notice in Notices)
            {
                V3DossierBlock.WriteClaim(writer, notice);
            }

            writer.WriteEndArray();
            writer.WriteStartObject("plan");
            writer.WriteStartArray("rendered");
            foreach (var line in PlanLines)
            {
                V3DossierBlock.WriteClaim(writer, line);
            }

            writer.WriteEndArray();
            writer.WriteString("plan_sha256", Trace.Plan.Sha256);
            writer.WriteEndObject();
            writer.WriteStartArray("parts");
            foreach (var part in Parts)
            {
                writer.WriteStartObject();
                writer.WriteString("part_id", part.PartId);
                writer.WriteStartArray("span");
                writer.WriteNumberValue(part.Span.Start);
                writer.WriteNumberValue(part.Span.End);
                writer.WriteEndArray();
                writer.WriteString("class", part.Application ? "application" : "lookup");
                writer.WriteStartArray("cues");
                foreach (var cue in part.Cues)
                {
                    writer.WriteStartObject();
                    writer.WriteString("function", ContractWire.NameOf(cue.Function));
                    writer.WriteStartArray("span");
                    writer.WriteNumberValue(cue.Start);
                    writer.WriteNumberValue(cue.End);
                    writer.WriteEndArray();
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                writer.WriteString("verdict", ContractWire.NameOf(part.Verdict));
                writer.WriteStartArray("blocks");
                foreach (var block in part.Blocks)
                {
                    block.Write(writer);
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            var quotes = Quotes.Count();
            writer.WriteStartObject("gates");
            writer.WriteNumber("quotes", quotes);
            writer.WriteNumber("disclosures", Blocks.OfType<V3DisclosureBlock>().Count());
            writer.WriteNumber("non_template_strings", 0);
            writer.WriteBoolean("disclosure_complete", true);
            writer.WriteEndObject();
            writer.WritePropertyName("trace");
            Trace.Write(writer);
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }
}
