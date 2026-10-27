using System.Text.Json;
using System.Text.Json.Nodes;
using Lex.V3.Contracts;
using Lex.V3.Ingest;
using Lex.V3.Ingest.Europe;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Api;

/// <summary>
/// The text yield per class the coverage answer serves (the specification of record's R86(4) and R26): the build's own count,
/// read from the build report beside the mount (<c>textYield</c>, report /2) and held to the mount when it opens, so Coverage
/// and R26's sentence say the figures the build reported and nothing else. A mount whose report states no yield (Release 1's
/// mounts, written under report /1, or a mount with no report) answers its coverage as before, with <c>text_yield</c> null and
/// a note saying why: the live release keeps working, and no figure is computed by the server that the build did not report.
/// </summary>
internal sealed partial class V3CorpusMount
{
    // The yield the build report states, held to the mount at open; null when the report states none.
    private readonly V3TextYield? _textYield;

    /// <summary>What the coverage answer's <c>text_yield</c> is and how its counts relate, when the build reported one.</summary>
    internal const string TextYieldNote =
        "the build's report of the text yield per class, read from the build report beside this mount and held to it when the mount opened: " +
        "per publisher and per class of act (a Luxembourg family and state kind; an EU content class), versions counts the versions the catalogue lists " +
        "(Luxembourg: the catalogue states of the class, as the index holds the catalogue's facts, and every state of which a file is held; EU: the census's " +
        "versions, one per original wording and one per consolidation date of each seed) and versions_with_text those with an article served here; " +
        "versions_not_held counts each catalogue state of which this mount holds no file, by what the catalogue says of it, and is null where the index cannot " +
        "tell (EU); files counts the publisher files this mount holds, each in exactly one of files_with_text, files_withheld (by the rights disposition, " +
        "verbatim; pdf counts the PDF files among them), files_pdf_only (an admitted PDF from which no text is served; null where no held file is a PDF: EU) " +
        "and files_parse_failed (an admitted file that is not a PDF and from which no article is served, by the derivation's own dispositions, verbatim, " +
        "joined with +); articles counts the articles served; works counts distinct acts, so a publisher's works are not the sum of its classes', while its " +
        "versions, files and articles are; annexes counts the annex outcomes the corpus recorded on the class's files: image_only those of " +
        "annex_text_not_available, with_text those served as text (none: no annex is served as an article) and failed every other, by disposition, and is " +
        "null where the corpus records no annex outcome (Luxembourg)";

    /// <summary>Why the coverage answer's <c>text_yield</c> is null.</summary>
    internal const string TextYieldAbsentNote =
        "this mount's build report states no text yield (it was written under lex-v3-first-mount-report/1, or no report is beside the mount), so this " +
        "answer gives none and computes none; every other count here is unchanged by its absence";

    /// <summary>The coverage scope of a mount whose build reported its text yield: the catalogue's versions are then counted too.</summary>
    internal const string CoverageScopeWithTextYield =
        "the mounted Luxembourg corpus and index: what this mount holds and recorded as missing, and of what the publisher holds nothing but " +
        "text_yield's count of the versions its catalogue lists, as this mount holds the catalogue's facts";

    /// <summary>The <c>publisher_universe</c> row of a mount whose build reported its text yield.</summary>
    internal static readonly string[] CoveragePublisherUniverseWithTextYield =
        ["publisher_universe", "how many acts the publisher holds outside the families whose catalogue facts this mount holds: text_yield counts the versions and acts the catalogue lists in those families, and the EU versions the census found for the seeds acquired, and nothing here counts any other"];

    /// <summary>
    /// Holds a reported yield to the mount it is beside: a publisher is reported exactly when its index is mounted, its
    /// articles are the articles its index holds, and its files are the files the corpus holds for it. A yield that does
    /// not hold refuses the mount, as a report naming other artifacts does.
    /// </summary>
    internal static void HoldTextYield(
        V3TextYield? textYield,
        LuxembourgIndexReader? luxembourg,
        EuropeIndexReader? europe,
        LexCorpus6MountView corpus)
    {
        if (textYield is null)
        {
            return;
        }

        var luxembourgRow = textYield.Publishers.SingleOrDefault(static row => row.Publisher == "lu-legilux");
        var europeRow = textYield.Publishers.SingleOrDefault(static row => row.Publisher == "eu-eurlex");
        if ((luxembourgRow is null) != (luxembourg is null) || (europeRow is null) != (europe is null))
        {
            throw new InvalidDataException("The build report's text yield does not report exactly the publishers whose indexes are mounted.");
        }

        if (luxembourgRow is not null &&
            (luxembourgRow.Articles != luxembourg!.ArticleCount ||
             luxembourgRow.Files != corpus.HeldMembers.LongCount(static member =>
                 member.Publisher == PublisherId.LuLegilux && member.LuxembourgRights is not null)))
        {
            throw new InvalidDataException("The build report's Luxembourg text yield does not count the mounted index's articles and the corpus's Luxembourg files.");
        }

        if (europeRow is not null &&
            (europeRow.Articles != europe!.ArticleCount ||
             europeRow.Files != corpus.HeldMembers.LongCount(static member =>
                 member.Publisher == PublisherId.EuEurLex && member.Outcome == LexCorpus6OutcomeKind.Acquired && member.EuropeContentClass is not null)))
        {
            throw new InvalidDataException("The build report's EU text yield does not count the mounted index's articles and the corpus's EU files.");
        }
    }

    /// <summary>The coverage answer's <c>text_yield</c>: the reported block in its own schema, or null.</summary>
    private JsonNode? TextYieldBlock() =>
        _textYield is null ? null : JsonSerializer.SerializeToNode(_textYield, V3TextYield.JsonOptions);
}
