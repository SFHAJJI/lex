using System.Text;
using System.Text.Json;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;

namespace Lex.V3.Ingest.Luxembourg;

public sealed partial class LuxembourgFirstMountAcquisition
{
    private const string ScopeKey = "scope";
    private const string VocabularyKey = "vocabulary";

    /// <summary>
    /// The scope reference and plan identity this run binds its requests to, journaled before the first request. A
    /// resumed run takes its interrupted run's pair when that run declared these exact scope bytes, so the families,
    /// vocabulary and GETs it journaled are bound to this run's plan; otherwise it keeps its own, and every journaled
    /// unit bound to the other plan is acquired again.
    /// </summary>
    private async Task<(SourceArtifactRef ScopeRef, string PlanId)> ResumeScopeAsync(SourceArtifactRef scopeRef, string planId,
        LuxembourgAcquisitionProgress? progress, CancellationToken cancellationToken)
    {
        var replayed = false;
        if (_resume is not null && _resume.TryTake(AcquisitionJournal.LuxembourgScopePhase, ScopeKey, out var payload))
        {
            try
            {
                var journaled = ContractJson.Deserialize<JournaledScope>(payload.GetRawText());
                if (journaled.Scope.Sha256 == scopeRef.Sha256 && IsUrn(journaled.Scope.ResourceId) && IsUrn(journaled.PlanId))
                {
                    scopeRef = journaled.Scope;
                    planId = journaled.PlanId;
                    replayed = true;
                }
            }
            catch (Exception exception) when (exception is ArgumentException or JsonException)
            {
                // A journaled scope that does not read is not this run's: the run keeps its own.
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Tally(AcquisitionJournal.LuxembourgScopePhase, replayed);
        if (_journal is not null)
            await _journal.AppendAsync(AcquisitionJournal.LuxembourgScopePhase, ScopeKey,
                AcquisitionJournal.Payload(new JournaledScope(scopeRef, planId)), [scopeRef.Sha256]).ConfigureAwait(false);
        return (scopeRef, planId);
    }

    /// <summary>
    /// The interrupted run's observed vocabulary, when it journaled a checkpoint for this run's plan: reopened through
    /// <see cref="ReopenVocabularyAsync"/> (all four enumeration proofs, this run's query renderer, the observation bytes
    /// reproduced and the source-profile gate). Null when the journal names none, or names one that does not reopen for
    /// this plan: the vocabulary is then enumerated again.
    /// </summary>
    private async Task<(VerifiedLuxembourgSourceProfile Profile, SourceArtifactRef Observation, SourceArtifactRef Checkpoint)?>
        ResumeVocabularyAsync(LuxembourgQueryPlan plan, string planId, MachineQueryRendererSource queryRenderer,
            CancellationToken cancellationToken)
    {
        if (_resume is null || !_resume.TryTake(AcquisitionJournal.LuxembourgVocabularyPhase, VocabularyKey, out var payload))
            return null;
        try
        {
            var journaled = ContractJson.Deserialize<JournaledVocabulary>(payload.GetRawText());
            var bytes = await CustodyRestore.ReadByDigestCheckedAsync(_custodyStore, journaled.Checkpoint.Sha256,
                cancellationToken).ConfigureAwait(false);
            var document = ContractJson.Deserialize<VocabularyCheckpoint>(new UTF8Encoding(false, true).GetString(bytes.Span));
            if (document.Plan != LuxembourgQueryPlanIdentity.Create(planId, plan) ||
                document.PlanWireSha256 != CustodyDigest.Of(LuxembourgQueryPlan.GetWireBytes(plan)))
                return null;
            var profile = await ReopenVocabularyAsync(_custodyStore, journaled.Checkpoint, journaled.Observation, queryRenderer,
                cancellationToken).ConfigureAwait(false);
            return (profile, journaled.Observation, journaled.Checkpoint);
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException or DecoderFallbackException
            or CustodyRequiredException or CustodyIntegrityException)
        {
            return null;
        }
    }

    /// <summary>Journals the vocabulary checkpoint and the observation it reproduces, both held.</summary>
    private async Task JournalVocabularyAsync(SourceArtifactRef checkpoint, SourceArtifactRef observation)
    {
        if (_journal is null) return;
        await _journal.AppendAsync(AcquisitionJournal.LuxembourgVocabularyPhase, VocabularyKey,
            AcquisitionJournal.Payload(new JournaledVocabulary(checkpoint, observation)), [checkpoint.Sha256, observation.Sha256])
            .ConfigureAwait(false);
    }

    private static bool IsUrn(string value) =>
        value.StartsWith("urn:uuid:", StringComparison.Ordinal) && Guid.TryParseExact(value["urn:uuid:".Length..], "D", out _);

    /// <summary>The scope reference and plan identity a run bound its requests to.</summary>
    private sealed record JournaledScope(SourceArtifactRef Scope, string PlanId);

    /// <summary>A retained vocabulary checkpoint and the observation document its rows reproduce.</summary>
    private sealed record JournaledVocabulary(SourceArtifactRef Checkpoint, SourceArtifactRef Observation);
}
