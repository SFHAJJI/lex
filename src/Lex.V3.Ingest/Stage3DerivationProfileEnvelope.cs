using System.Text.Json.Serialization;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest;

/// <summary>Why the proof-complete Stage 3 derivation-profile chain could not be composed.</summary>
public enum Stage3DerivationProfileEnvelopeRefusal
{
    [JsonStringEnumMemberName("none")]
    None = 0,

    [JsonStringEnumMemberName("publisher_pdf_act_scope_missing")]
    PublisherPdfActScopeMissing = 1,
}

/// <summary>
    /// Construction-only envelope over the exact post-body profile chain represented by one
    /// terminal act-scope population.
/// </summary>
public sealed class Stage3DerivationProfileEnvelope
{
    private Stage3DerivationProfileEnvelope(
        Stage3BodyComposition bodyComposition,
        LuxembourgPdfProfileEligibilityPopulation pdfEligibility,
        LuxembourgPdfLayoutEvidencePopulation pdfLayoutEvidence,
        LuxembourgPublisherPdfTextLayerPopulation publisherPdfTextLayer,
        LuxembourgPublisherPdfActScopePopulation publisherPdfActScope)
    {
        BodyComposition = bodyComposition;
        PdfEligibility = pdfEligibility;
        PdfLayoutEvidence = pdfLayoutEvidence;
        PublisherPdfTextLayer = publisherPdfTextLayer;
        PublisherPdfActScope = publisherPdfActScope;
    }

    public Stage3BodyComposition BodyComposition { get; }

    public LuxembourgPdfProfileEligibilityPopulation PdfEligibility { get; }

    public LuxembourgPdfLayoutEvidencePopulation PdfLayoutEvidence { get; }

    public LuxembourgPublisherPdfTextLayerPopulation PublisherPdfTextLayer { get; }

    public LuxembourgPublisherPdfActScopePopulation PublisherPdfActScope { get; }

    public static Stage3DerivationProfileEnvelope? TryCreate(
        LuxembourgPublisherPdfActScopePopulation? publisherPdfActScope,
        out Stage3DerivationProfileEnvelopeRefusal refusal,
        out string? detail)
    {
        refusal = Stage3DerivationProfileEnvelopeRefusal.None;
        detail = null;

        if (publisherPdfActScope is null)
        {
            refusal = Stage3DerivationProfileEnvelopeRefusal.PublisherPdfActScopeMissing;
            detail = "The publisher PDF act-scope population is missing.";
            return null;
        }

        var publisherPdfTextLayer = publisherPdfActScope.SourceTextLayerPopulation;
        var pdfLayoutEvidence = publisherPdfTextLayer.SourceLayoutEvidencePopulation;
        var pdfEligibility = pdfLayoutEvidence.SourceEligibilityPopulation;
        var bodyComposition = pdfEligibility.SourceComposition;

        return new Stage3DerivationProfileEnvelope(
            bodyComposition,
            pdfEligibility,
            pdfLayoutEvidence,
            publisherPdfTextLayer,
            publisherPdfActScope);
    }
}
