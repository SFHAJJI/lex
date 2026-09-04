using System.Text.Json.Serialization;

namespace Lex.V3.Contracts.Source.Http;

/// <summary>
/// The format a selected Legilux manifestation is served as. Only xml and pdf/a participate in
/// D1-06c-LU item 3's selection order (Decision 22 admits xml, pdf and html generally for
/// filestore fetches; html and docx and svg never participate in THIS selection, which picks the
/// one manifestation this route fetches for one expression).
/// </summary>
public enum LuxembourgManifestationFormat
{
    [JsonStringEnumMemberName("xml")]
    Xml = 1,

    [JsonStringEnumMemberName("pdf_a")]
    PdfA = 2,
}

public enum LuxembourgManifestationSelectionOutcome
{
    [JsonStringEnumMemberName("selected")]
    Selected = 1,

    [JsonStringEnumMemberName("no_manifestation_available")]
    NoManifestationAvailable = 2,
}

/// <summary>
/// D1-06c-LU item 5: pure manifestation-selection decision logic for one expression, ready for a
/// later slice's adapter wiring (this type is deliberately not wired into any
/// <c>ScopeManifestRow</c> or LU adapter here). XML wins when the publisher lists it; otherwise
/// PDF/A when the publisher lists that; otherwise a typed absence. Per the ruling's own words, "the
/// record names the format it holds": <see cref="Format"/> is populated on every selected outcome,
/// never reduced to a bare boolean.
/// </summary>
public sealed record LuxembourgManifestationSelection
{
    private LuxembourgManifestationSelection(
        LuxembourgManifestationSelectionOutcome outcome,
        LuxembourgManifestationFormat? format,
        string? fileUri)
    {
        Outcome = outcome;
        Format = format;
        FileUri = fileUri;
    }

    public LuxembourgManifestationSelectionOutcome Outcome { get; }

    /// <summary>The selected format, or null exactly when <see cref="Outcome"/> is the absence.</summary>
    public LuxembourgManifestationFormat? Format { get; }

    /// <summary>The selected manifestation's store file URI, or null exactly when absent.</summary>
    public string? FileUri { get; }

    /// <summary>
    /// Selects among the manifestations the publisher's own SPARQL store lists for one expression.
    /// Each parameter is that format's file URI when the publisher enumerates one, else null;
    /// XML strictly precedes PDF/A in the selection order.
    /// </summary>
    public static LuxembourgManifestationSelection Select(string? xmlFileUri, string? pdfAFileUri)
    {
        if (!string.IsNullOrEmpty(xmlFileUri))
        {
            return new LuxembourgManifestationSelection(
                LuxembourgManifestationSelectionOutcome.Selected,
                LuxembourgManifestationFormat.Xml,
                xmlFileUri);
        }

        if (!string.IsNullOrEmpty(pdfAFileUri))
        {
            return new LuxembourgManifestationSelection(
                LuxembourgManifestationSelectionOutcome.Selected,
                LuxembourgManifestationFormat.PdfA,
                pdfAFileUri);
        }

        return new LuxembourgManifestationSelection(
            LuxembourgManifestationSelectionOutcome.NoManifestationAvailable,
            null,
            null);
    }
}
