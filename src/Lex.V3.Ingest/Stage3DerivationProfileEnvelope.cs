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
        LuxembourgPublisherPdfActScopePopulation publisherPdfActScope,
        LuxembourgRederivationRecord? luxembourgRederivation = null)
    {
        BodyComposition = bodyComposition;
        PdfEligibility = pdfEligibility;
        PdfLayoutEvidence = pdfLayoutEvidence;
        PublisherPdfTextLayer = publisherPdfTextLayer;
        PublisherPdfActScope = publisherPdfActScope;
        LuxembourgRederivation = luxembourgRederivation;
    }

    /// <summary>
    /// The record of the re-derivation that made the Luxembourg inventory and legal content this chain carries; null when
    /// they are the acquisition's own. Every artefact built from this envelope names it the same way (the corpus header).
    /// </summary>
    public LuxembourgRederivationRecord? LuxembourgRederivation { get; }

    /// <summary>
    /// This envelope with the record of the re-derivation that made its Luxembourg inventory and legal content, or null with
    /// the reason when the record names other populations than the ones this chain carries, or the envelope has one already.
    /// </summary>
    internal Stage3DerivationProfileEnvelope? WithLuxembourgRederivation(LuxembourgRederivationRecord record, out string? detail)
    {
        ArgumentNullException.ThrowIfNull(record);
        var envelope = BodyComposition.Envelope;
        if (LuxembourgRederivation is not null)
        {
            detail = "the profile envelope already names a re-derivation";
            return null;
        }

        if (!string.Equals(envelope.LuxembourgAknArticleInventoryPopulation.IdentitySha256, record.InventoryIdentitySha256, StringComparison.Ordinal) ||
            !string.Equals(envelope.LuxembourgAknLegalContentPopulation.IdentitySha256, record.ContentIdentitySha256, StringComparison.Ordinal))
        {
            detail = $"the re-derivation record {record.Sha256} names other Luxembourg derivations than the envelope carries";
            return null;
        }

        detail = null;
        return new(BodyComposition, PdfEligibility, PdfLayoutEvidence, PublisherPdfTextLayer, PublisherPdfActScope, record);
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
