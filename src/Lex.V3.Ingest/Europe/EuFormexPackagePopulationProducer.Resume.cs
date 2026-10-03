using System.Text.Json;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Ingest.Europe;

public sealed partial class EuFormexPackagePopulationProducer
{
    /// <summary>
    /// Reopens, before any request, every Formex unit an interrupted run journaled for <paramref name="run"/>, and hands
    /// them to the walk through <paramref name="progress"/>: each enumeration through
    /// <see cref="EuFormexManifestationEnumerationProducer.ReopenAsync"/> and each package through
    /// <see cref="EuFormexPackageAcquisitionProducer.ReopenAsync"/>, the checked readers a full replay uses.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This walks the population's own order (families by key, served-language expressions in production order), so a
    /// unit the walk would never reach is found here rather than after traffic: an expression outside the run, a
    /// package for an expression that is not Formex-eligible or whose identity is not admitted, a package without its
    /// enumeration. Each refuses, as does a unit journaled for another run's input (its digest is the population
    /// checkpoint's input digest) or under another family.
    /// </para>
    /// <para>
    /// Only the combined form is resumed, the one the first-mount acquisition runs: no caller CELEX, and the run's own
    /// identities admitting each work, exactly as <see cref="TryAdmitCelex"/> admits them for the live walk.
    /// </para>
    /// </remarks>
    internal static async Task PrepareResumeAsync(ICustodyStore store, EuQueryExecutionResult run,
        MachineQueryRendererSource documentFetchRendererSource, AcquisitionResume resume, EuFormexPopulationProgress progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(documentFetchRendererSource);
        ArgumentNullException.ThrowIfNull(resume);
        ArgumentNullException.ThrowIfNull(progress);
        if (resume.Count(AcquisitionJournal.EuropeFormexEnumerationPhase) == 0 &&
            resume.Count(AcquisitionJournal.EuropeFormexPackagePhase) == 0)
            return;
        if (run.Refusal is not null || run.Completion != EuQueryExecutionCompletion.AllFamiliesProven || run.CorrigendumTripwires is null)
            throw new CustodyIntegrityException("The journal names Formex units of an adapter run that is not complete.");

        var input = PopulationInputDigest(run);
        try
        {
            foreach (var (familyKey, production) in run.CorrigendumTripwires.ProductionsByFamilyKey
                         .OrderBy(static pair => pair.Key, StringComparer.Ordinal))
            {
                var expressions = production.Expressions?.Derivation?.Expressions
                    ?? throw new CustodyIntegrityException($"Family {familyKey} carries no delivered expression production.");
                foreach (var expression in expressions)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!EuFormexEligibilityPopulation.IsServedLanguage(expression)) continue;

                    // A package is journaled after its enumeration, so a journaled package whose enumeration is not
                    // journaled is never taken here and refuses below with every other unit the walk does not reach.
                    var key = expression.CanonicalContentSha256;
                    if (!resume.TryTake(AcquisitionJournal.EuropeFormexEnumerationPhase, key, out var enumerationPayload)) continue;
                    var journaledEnumeration = ContractJson.Deserialize<JournaledEnumeration>(enumerationPayload.GetRawText());
                    if (journaledEnumeration.InputSha256 != input || journaledEnumeration.Enumeration is null ||
                        journaledEnumeration.Enumeration.Family != familyKey || journaledEnumeration.Enumeration.ExpressionSha256 != key)
                        throw new CustodyIntegrityException(
                            $"The journaled Formex enumeration of {expression.Identity.PublisherExpressionId} belongs to another run or family.");
                    var enumeration = await EuFormexManifestationEnumerationProducer.ReopenAsync(store,
                        journaledEnumeration.Enumeration.Checkpoint, expression, journaledEnumeration.Enumeration.Run,
                        journaledEnumeration.Enumeration.Profile, cancellationToken).ConfigureAwait(false);
                    progress.Enumerations.Add(key, enumeration);

                    if (!resume.TryTake(AcquisitionJournal.EuropeFormexPackagePhase, key, out var packagePayload)) continue;
                    var journaledPackage = ContractJson.Deserialize<JournaledPackage>(packagePayload.GetRawText());
                    if (journaledPackage.InputSha256 != input || journaledPackage.Family != familyKey || journaledPackage.Package is null ||
                        journaledPackage.Package.ExpressionSha256 != key || journaledPackage.Package.Checkpoint is not { } checkpoint)
                        throw new CustodyIntegrityException(
                            $"The journaled Formex package of {expression.Identity.PublisherExpressionId} belongs to another run or family.");

                    // The walk requests a package only for an eligible expression whose identity it admits.
                    if (!enumeration.IsFormexEligible ||
                        !TryAdmitCelex(run, null, expression.Identity.PublisherWorkId, false, out var celex))
                        throw new CustodyIntegrityException(
                            $"The journal names a Formex package for {expression.Identity.PublisherExpressionId}, which this run does not acquire.");
                    progress.Packages.Add(key, await EuFormexPackageAcquisitionProducer.ReopenAsync(store, checkpoint, enumeration,
                        run.CorpusRecordSet, celex, documentFetchRendererSource, cancellationToken).ConfigureAwait(false));
                }
            }
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException)
        {
            throw new CustodyIntegrityException("A journaled Formex unit does not read.", exception);
        }

        resume.RequireTaken(AcquisitionJournal.EuropeFormexEnumerationPhase, AcquisitionJournal.EuropeFormexPackagePhase);
    }

    /// <summary>A journaled enumeration: the population checkpoint's own record, with the input digest of the run it belongs to.</summary>
    private sealed record JournaledEnumeration(string InputSha256, EnumerationCheckpoint Enumeration);

    /// <summary>A journaled package: the population checkpoint's own record, with its run's input digest and its family.</summary>
    private sealed record JournaledPackage(string InputSha256, string Family, PackageCheckpoint Package);
}

/// <summary>
/// A Formex population's part in a journaled acquisition: the journal each unit is written to once its checkpoint is
/// held and, when the acquisition resumes an interrupted run, the units that run finished, already reopened by their own
/// checked readers (<see cref="EuFormexPackagePopulationProducer.PrepareResumeAsync"/>), keyed by expression digest and
/// taken once by the walk. It counts what was replayed and what was acquired live, for the catalog the run retains.
/// </summary>
internal sealed class EuFormexPopulationProgress(IAcquisitionJournal? journal)
{
    internal IAcquisitionJournal? Journal { get; } = journal;

    internal Dictionary<string, EuFormexManifestationEnumerationResult> Enumerations { get; } = new(StringComparer.Ordinal);

    internal Dictionary<string, EuFormexPackageAcquisitionResult> Packages { get; } = new(StringComparer.Ordinal);

    internal int ReplayedEnumerations { get; set; }

    internal int LiveEnumerations { get; set; }

    internal int ReplayedPackages { get; set; }

    internal int LivePackages { get; set; }
}
