using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Contracts.Source.Europe;

/// <summary>
/// Why a request rate was chosen. Closed, because the difference between obeying guidance and
/// choosing a number matters and a free-text note would blur it.
/// </summary>
/// <remarks>
/// This exists because of an error worth not repeating. A measurement recorded that we call the
/// publisher 6.7 times faster than its stated crawl delay, and that comparison was against
/// <c>eur-lex.europa.eu</c>, a host the architecture forbids us to fetch from at all. The hosts we
/// do use redirect their robots to <c>op.europa.eu</c>, which publishes no crawl delay of any kind.
/// So our interval is not compliance with anything; it is a judgement. A profile that recorded only
/// the number would have carried that confusion forward silently.
/// </remarks>
public enum EuPacingBasis
{
    /// <summary>
    /// The served host publishes no rate guidance, and this interval is a conservative choice.
    /// </summary>
    [JsonStringEnumMemberName("chosen_absent_published_guidance")]
    ChosenAbsentPublishedGuidance = 1,

    /// <summary>The served host publishes a crawl delay and this interval respects it.</summary>
    [JsonStringEnumMemberName("published_crawl_delay")]
    PublishedCrawlDelay = 2,
}

/// <summary>
/// The request rate this profile was calibrated with, and why.
/// </summary>
/// <remarks>
/// The configured minimum interval only. The rate a run actually achieved varies with the endpoint
/// and belongs in that run's receipt: freezing an achieved rate into a profile would make the
/// profile wrong the moment conditions changed, while claiming to describe them.
/// </remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EuPacingPolicy
{
    /// <summary>An interval above this is a configuration error rather than caution.</summary>
    public const int MaximumIntervalMilliseconds = 600_000;

    [JsonConstructor]
    public EuPacingPolicy(
        int minimumIntervalMilliseconds,
        EuPacingBasis basis,
        SourceArtifactRef basisEvidenceRef)
    {
        if (minimumIntervalMilliseconds is < 1 or > MaximumIntervalMilliseconds)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumIntervalMilliseconds),
                minimumIntervalMilliseconds,
                "A pacing interval must be a bounded positive number of milliseconds.");
        }

        MinimumIntervalMilliseconds = minimumIntervalMilliseconds;
        Basis = ContractValidation.RequireDefined(basis, nameof(basis));
        // Required for both bases. A chosen interval needs the observation showing no guidance
        // exists as much as a respected one needs the guidance itself, because "we checked and
        // found none" and "we did not check" are the same number with different standing.
        BasisEvidenceRef = basisEvidenceRef ?? throw new ArgumentNullException(nameof(basisEvidenceRef));
    }

    public int MinimumIntervalMilliseconds { get; }

    public EuPacingBasis Basis { get; }

    /// <summary>The robots observation this basis was read from, content-bound.</summary>
    public SourceArtifactRef BasisEvidenceRef { get; }
}

/// <summary>
/// The delivery ceiling this profile binds, by threshold and detector identity.
/// </summary>
/// <remarks>
/// The assessment is not here and must not be. Whether a given page is at risk of silent
/// truncation is one shared pure function over a shared vector corpus, owned outside this profile
/// so that Luxembourg and the Union cannot drift into two answers about one endpoint property.
/// What is source-specific is the threshold and which detector version was bound, so those live
/// here.
/// </remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EuDeliveryCeilingBinding
{
    [JsonConstructor]
    public EuDeliveryCeilingBinding(
        long maxDeliverableRows,
        SourceRegistryMemberRef detectorRef)
    {
        if (maxDeliverableRows < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxDeliverableRows),
                maxDeliverableRows,
                "A delivery ceiling must be a positive row count.");
        }

        MaxDeliverableRows = maxDeliverableRows;
        DetectorRef = detectorRef ?? throw new ArgumentNullException(nameof(detectorRef));
    }

    /// <summary>
    /// The measured ceiling for this publisher. A delivered count equal to this value is
    /// ambiguous rather than complete, and forces repartitioning; that rule belongs to the shared
    /// detector, and this is the number it is given.
    /// </summary>
    public long MaxDeliverableRows { get; }

    /// <summary>The exact detector artifact and member this profile was bound against.</summary>
    public SourceRegistryMemberRef DetectorRef { get; }
}

/// <summary>
/// How Union data may be acquired: which channels, at what rate, against which ceiling.
/// </summary>
/// <remarks>
/// <para>
/// A recording contract, not an enforcing one. Robots and path behaviour are enforced by transport
/// against the live response; duplicating those rules in a second validator would produce two
/// places that must agree about one publisher and eventually will not.
/// </para>
/// <para>
/// What this profile owes a later reader is the ability to ask what the acquisition was configured
/// to do, and to check that answer against evidence. It carries no completion, no absence, and no
/// ceiling assessment.
/// </para>
/// </remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EuAcquisitionProfile
{
    [JsonConstructor]
    public EuAcquisitionProfile(
        IReadOnlyList<EuChannelDisposition> channels,
        EuPacingPolicy pacing,
        EuDeliveryCeilingBinding ceiling)
    {
        ArgumentNullException.ThrowIfNull(channels);
        Pacing = pacing ?? throw new ArgumentNullException(nameof(pacing));
        Ceiling = ceiling ?? throw new ArgumentNullException(nameof(ceiling));

        // Materialised once, then validated and exposed. `IReadOnlyList` is a view rather than a
        // guarantee: a caller can pass a List through it and mutate it after validation, so the
        // snapshot is what gets checked and what gets kept.
        var snapshot = channels.ToArray();

        // Exhaustive. A profile that simply omits a channel says nothing about it, and a consumer
        // cannot tell an unmentioned channel from a refused one. Every identity in the closed set
        // carries a disposition, exactly once.
        var seen = new HashSet<EuChannel>();
        foreach (var disposition in snapshot)
        {
            if (disposition is null)
            {
                throw new ArgumentException(
                    "A channel disposition cannot be null.",
                    nameof(channels));
            }

            if (!seen.Add(disposition.Channel))
            {
                throw new ArgumentException(
                    $"{disposition.Channel} carries more than one disposition; a channel with two " +
                    "answers has none.",
                    nameof(channels));
            }
        }

        foreach (var channel in EuScopeVocabulary.Channels)
        {
            if (!seen.Contains(channel))
            {
                throw new ArgumentException(
                    $"{channel} carries no disposition; an unmentioned channel is indistinguishable " +
                    "from a refused one.",
                    nameof(channels));
            }
        }

        // An acquisition profile that admits nothing cannot acquire. Expressing that state is
        // possible elsewhere; here it is a configuration error, and a loud one is better than a
        // run that fetches nothing and reports success.
        if (!snapshot.Any(static disposition => disposition.MayGraduate))
        {
            throw new ArgumentException(
                "No channel is admitted, so this profile can acquire nothing.",
                nameof(channels));
        }

        Channels = Array.AsReadOnly(snapshot);
    }

    public IReadOnlyList<EuChannelDisposition> Channels { get; }

    public EuPacingPolicy Pacing { get; }

    public EuDeliveryCeilingBinding Ceiling { get; }

    /// <summary>
    /// The channels a datum may arrive by and still graduate past POINT.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<EuChannel> AdmittedChannels =>
        new ReadOnlyCollection<EuChannel>(
            Channels.Where(static disposition => disposition.MayGraduate)
                .Select(static disposition => disposition.Channel)
                .ToArray());
}
