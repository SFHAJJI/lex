using System.Text.Json;
using Lex.V3.Ingest.Names;

namespace Lex.V3.Ingest.Releases;

/// <summary>
/// One consolidation file R86(2) lists: an admitted publisher file of a consolidated version (a Luxembourg consolidation or
/// code state, an EU consolidated wording) from which the mount serves no article, by identity, with the typed outcome the
/// data carries and what that outcome is: a typed textless outcome or a PDF's text-layer profile (explained by the data), or
/// a failure, which only an entry of the release's explanations can explain.
/// </summary>
public sealed record V3ConsolidationTextItem(
    string Publisher,
    string Family,
    string Kind,
    string? Identity,
    string ObjectRefSha256,
    string BodySha256,
    string? Format,
    string Outcome,
    string? TextStatus,
    string? TextStatusReason,
    string Classification,
    int? Entry)
{
    /// <summary>Whether the item is a typed, explained outcome: by the data, or by an entry of the release's explanations.</summary>
    public bool Explained => Classification != V3ConsolidationText.Failure || Entry is not null;
}

/// <summary>One publisher's consolidation files: admitted, with text, and listed.</summary>
public sealed record V3ConsolidationTextCount(string Publisher, long Admitted, long WithText, long Listed, long Unexplained);

/// <summary>
/// R86(2) (the audit's gap 6): every consolidation with an admitted XML manifestation yields text, and each failure is listed
/// and blocking; PDF-only ones get a labelled text-layer profile or a typed textless outcome. Over one mount, read-only
/// (<see cref="V3MountInventory"/>): every admitted file of a consolidated version (a Luxembourg state of kind
/// <c>consolidation</c> or <c>code</c>, an EU file of content class <c>consolidation</c>) from which no article is served is
/// listed by identity with the outcome its corpus member and index carry. It is explained when that outcome is typed and
/// textless (a Luxembourg file the index shows is another state's, <c>not_available_misfiled</c>; a file whose every unit is
/// marker-only evidence; an EU file with no Formex main body, PDF or HTML only; an EU expression whose package was never
/// acquired, so no XML manifestation is held), or a PDF with a labelled text-layer profile outcome; any other is a failure,
/// and the release refuses unless its explanations name that file with exactly that outcome.
/// </summary>
public sealed record V3ConsolidationText(
    IReadOnlyList<V3ConsolidationTextCount> Counts,
    IReadOnlyList<V3ConsolidationTextItem> Listed,
    IReadOnlyList<V3TextlessExplanation> UnusedExplanations)
{
    /// <summary>The report's schema.</summary>
    public const string SchemaId = "lex-v3-consolidation-text/1";

    /// <summary>The report's file name, as the release publishes it.</summary>
    public const string FileName = "consolidation-text.json";

    /// <summary>An admitted consolidation file with no article and no typed textless outcome: R86(2)'s failure.</summary>
    public const string Failure = "failure";

    /// <summary>A Luxembourg file the index's content consistency check shows holds another state's text.</summary>
    public const string TypedTextlessMisfiled = "typed_textless_misfiled";

    /// <summary>A Luxembourg file whose every legal-content unit is marker-only evidence (the publisher's text holds markers only).</summary>
    public const string TypedTextlessMarkerOnly = "typed_textless_marker_only";

    /// <summary>An EU file with no Formex main body (the publisher serves it as PDF or HTML only).</summary>
    public const string TypedTextlessNoXmlMainBody = "typed_textless_no_xml_main_body";

    /// <summary>An EU expression whose Formex package was never acquired: no XML manifestation is held.</summary>
    public const string TypedTextlessNoPackageHeld = "typed_textless_no_package_held";

    /// <summary>A PDF with a labelled text-layer profile outcome (the publisher PDF act-scope profile's disposition).</summary>
    public const string PdfTextLayerProfile = "pdf_text_layer_profile";

    /// <summary>What the check holds, as the report states it.</summary>
    public const string Rule =
        "R86(2): every admitted file of a consolidated version (Luxembourg consolidation and code states, EU consolidated wordings) " +
        "from which no article is served is listed by identity with its typed outcome. A typed textless outcome (misfiled, " +
        "marker-only, no XML main body, no package held) or a PDF's labelled text-layer profile is explained by the data; any " +
        "other outcome is a failure, and refuses the release unless the release's explanations name the file with exactly that outcome.";

    /// <summary>How many listed files no typed outcome or explanation accounts for.</summary>
    public long Unexplained => Listed.LongCount(static item => !item.Explained);

    /// <summary>Whether the release is refused.</summary>
    public bool Refuses => Unexplained > 0;

    /// <summary>The check over one mount's inventory, under <paramref name="explanations"/> (none: only the data explains).</summary>
    public static V3ConsolidationText Measure(V3MountInventory inventory, V3ReleaseExplanations? explanations)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        var entries = explanations?.Textless ?? [];
        var used = new HashSet<int>();
        // The index's text status of each Luxembourg state, by the state's IRI (index 9; none before it).
        var statuses = inventory.Versions.Values
            .Where(static version => version.Publisher == V3MountInventory.Luxembourg)
            .GroupBy(static version => version.Identity, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.OrderBy(static version => version.Key, StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);
        var counts = new SortedDictionary<string, long[]>(StringComparer.Ordinal);
        var listed = new List<V3ConsolidationTextItem>();
        foreach (var file in inventory.Files)
        {
            if (file.Outcome != "acquired" || !IsConsolidation(file))
            {
                continue;
            }

            if (!counts.TryGetValue(file.Publisher, out var count))
            {
                count = new long[2];
                counts.Add(file.Publisher, count);
            }

            count[0]++;
            if (file.ArticlesServed > 0)
            {
                count[1]++;
                continue;
            }

            var outcome = file.Dispositions.Count == 0 ? V3TextYield.NoLegalContentOutcome : string.Join("+", file.Dispositions);
            var state = file.Identity is { } identity && statuses.TryGetValue(identity, out var versions)
                ? versions.FirstOrDefault(static version => !version.HasText)
                : null;
            var classification = Classify(file, state);
            int? entry = null;
            if (classification == Failure)
            {
                var match = entries.FirstOrDefault(candidate => candidate.Publisher == file.Publisher &&
                    candidate.ObjectRefSha256 == file.ObjectRefSha256 && candidate.Outcome == outcome);
                if (match is not null)
                {
                    entry = match.Index;
                    used.Add(match.Index);
                }
            }

            listed.Add(new V3ConsolidationTextItem(
                file.Publisher, file.Family, file.Kind, file.Identity, file.ObjectRefSha256, file.BodySha256, file.Format, outcome,
                state?.TextStatus, state?.TextStatusReason, classification, entry));
        }

        listed.Sort(static (left, right) =>
            string.CompareOrdinal(left.Publisher, right.Publisher) is var publisher and not 0 ? publisher
            : string.CompareOrdinal(left.Family + "/" + left.Kind, right.Family + "/" + right.Kind) is var kind and not 0 ? kind
            : string.CompareOrdinal(left.Identity ?? "", right.Identity ?? "") is var identity and not 0 ? identity
            : string.CompareOrdinal(left.ObjectRefSha256, right.ObjectRefSha256));
        return new V3ConsolidationText(
            counts.Select(pair => new V3ConsolidationTextCount(
                pair.Key, pair.Value[0], pair.Value[1],
                listed.LongCount(item => item.Publisher == pair.Key),
                listed.LongCount(item => item.Publisher == pair.Key && !item.Explained))).ToArray(),
            listed,
            entries.Where(candidate => !used.Contains(candidate.Index)).ToArray());
    }

    /// <summary>Whether a file is of a consolidated version: a Luxembourg consolidation or code state, an EU consolidated wording.</summary>
    internal static bool IsConsolidation(V3HeldFile file) => file.Publisher == V3MountInventory.Luxembourg
        ? file.Kind is "consolidation" or "code"
        : file.Kind == "consolidation";

    private static string Classify(V3HeldFile file, V3ServedVersion? state)
    {
        if (file.Publisher == V3MountInventory.Luxembourg)
        {
            if (file.Format is { } format && format.StartsWith("pdf", StringComparison.Ordinal))
            {
                // A labelled outcome of the publisher PDF act-scope profile; pdf_not_applicable says the profile did not apply.
                return file.Dispositions.Any(static disposition => disposition.StartsWith("pdf_", StringComparison.Ordinal) && disposition != "pdf_not_applicable")
                    ? PdfTextLayerProfile
                    : Failure;
            }

            if (state is { TextStatus: "not_available_misfiled" })
            {
                return TypedTextlessMisfiled;
            }

            var legalContent = file.Dispositions.Where(static disposition => disposition.StartsWith("akn_", StringComparison.Ordinal)).ToArray();
            return legalContent.Length > 0 && legalContent.All(static disposition => disposition == "akn_marker_only_evidence")
                ? TypedTextlessMarkerOnly
                : Failure;
        }

        if (file.Dispositions.Count > 0 && file.Dispositions.All(static disposition => disposition == "formex_main_body_not_eligible"))
        {
            return TypedTextlessNoXmlMainBody;
        }

        return file.Dispositions.Count > 0 && file.Dispositions.All(static disposition => disposition == "formex_main_body_package_not_acquired")
            ? TypedTextlessNoPackageHeld
            : Failure;
    }

    /// <summary>The report, one form for one mount: indented, LF line ends, every list in ordinal order.</summary>
    public byte[] Write(V3ComparedMount mount, V3ReleaseExplanations? explanations)
    {
        ArgumentNullException.ThrowIfNull(mount);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, NamesRecord.WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", SchemaId);
            writer.WriteString("rule", Rule);
            V3ReleaseComparison.WriteMount(writer, "mount", mount);
            NamesRecord.WriteNullable(writer, "explanations_sha256", explanations?.Sha256);
            writer.WriteString("verdict", Refuses ? "refuse" : "pass");
            writer.WriteNumber("unexplained", Unexplained);
            writer.WriteStartArray("publishers");
            foreach (var count in Counts)
            {
                writer.WriteStartObject();
                writer.WriteString("publisher", count.Publisher);
                writer.WriteNumber("consolidation_files_admitted", count.Admitted);
                writer.WriteNumber("with_text", count.WithText);
                writer.WriteNumber("listed", count.Listed);
                writer.WriteNumber("unexplained", count.Unexplained);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteStartArray("listed");
            foreach (var item in Listed)
            {
                writer.WriteStartObject();
                writer.WriteString("publisher", item.Publisher);
                writer.WriteString("family", item.Family);
                writer.WriteString("kind", item.Kind);
                NamesRecord.WriteNullable(writer, "identity", item.Identity);
                writer.WriteString("object_ref_sha256", item.ObjectRefSha256);
                writer.WriteString("body_sha256", item.BodySha256);
                NamesRecord.WriteNullable(writer, "format", item.Format);
                writer.WriteString("outcome", item.Outcome);
                NamesRecord.WriteNullable(writer, "text_status", item.TextStatus);
                NamesRecord.WriteNullable(writer, "text_status_reason", item.TextStatusReason);
                writer.WriteString("classification", item.Classification);
                if (item.Entry is { } entry)
                {
                    writer.WriteNumber("explained_by_entry", entry);
                }
                else
                {
                    writer.WriteNull("explained_by_entry");
                }

                writer.WriteBoolean("blocking", !item.Explained);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteStartArray("unused_explanations");
            foreach (var entry in UnusedExplanations)
            {
                writer.WriteStartObject();
                writer.WriteNumber("entry", entry.Index);
                writer.WriteString("publisher", entry.Publisher);
                writer.WriteString("object_ref_sha256", entry.ObjectRefSha256);
                writer.WriteString("outcome", entry.Outcome);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        stream.WriteByte((byte)'\n');
        return stream.ToArray();
    }
}
