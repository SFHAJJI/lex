using System.Text.Json.Serialization;

namespace Lex.V3.Contracts.Source.Europe;

/// <summary>
/// Why a rights matrix is not the accounted legal-policy position. Closed.
/// </summary>
public enum EuRightsMatrixRefusal
{
    /// <summary>No refusal: the matrix was admitted.</summary>
    [JsonStringEnumMemberName("none")]
    None = 0,

    /// <summary>Two dispositions decide the same content class.</summary>
    [JsonStringEnumMemberName("duplicate_content_class")]
    DuplicateContentClass = 1,

    /// <summary>
    /// A content class in the closed set has no disposition. A class with no decision does not
    /// inherit one from a class it resembles, so a gap is a missing reading rather than a default.
    /// </summary>
    [JsonStringEnumMemberName("content_class_undecided")]
    ContentClassUndecided = 2,

    /// <summary>Two dispositions decide the same exception channel.</summary>
    [JsonStringEnumMemberName("duplicate_exception_channel")]
    DuplicateExceptionChannel = 3,

    /// <summary>
    /// An exception channel in the closed set has no disposition. Silence about a channel reads as
    /// "this exception cannot occur", which is the one thing a rights position must never say by
    /// omission.
    /// </summary>
    [JsonStringEnumMemberName("exception_channel_undecided")]
    ExceptionChannelUndecided = 4,
}

/// <summary>
/// The exact legal-policy rights matrix for the Union side: one reviewed reuse basis for every
/// content class, and one acknowledgement for every exception channel that can override it.
/// </summary>
/// <remarks>
/// <para>
/// The fact that shapes this type: <b>rights are not machine-readable at this publisher.</b>
/// Measured on 2026-09-02 across four surfaces, the work notice, the expression notice, the object
/// notice and the item itself, there is not one rights predicate. The basis for consolidated text
/// is stated in the EUR-Lex legal notice, in prose, citing Commission Decision 2011/833/EU. So this
/// matrix is a declared policy artifact bound to evidence of that notice, and it can never be
/// populated from Cellar metadata. A later reader who tries will find nothing and must not read
/// that nothing as permission.
/// </para>
/// <para>
/// <see cref="EuRightsDisposition"/> keeps one class honest by refusing a basis the reviewed notice
/// did not give it. It cannot speak about the classes that are absent, and absent is the dangerous
/// direction here in a way it is not elsewhere: a partial matrix reads as a settled legal position
/// while the classes it omits have no basis at all, and code downstream that finds no entry for a
/// class will either refuse everything or, worse, treat the silence as unrestricted.
/// </para>
/// <para>
/// Both halves are total against <see cref="Enum.GetValues{TEnum}"/> rather than a written count,
/// so a new content class or a newly reviewed exception channel makes every previously complete
/// matrix refuse until somebody decides it. That is the intended failure: unknown vocabulary fails
/// closed as scope drift.
/// </para>
/// <para>
/// What this is not, and the distinction is the whole reason the channels are separate. A basis is
/// a class-level reading of a notice. A channel is an acknowledgement that a per-document or
/// per-element condition can override that reading. Neither is clearance, authority to publish, or
/// a statement about any particular document. Holding a matrix establishes what must be sought, not
/// that it was found.
/// </para>
/// </remarks>
public sealed class EuRightsMatrix
{
    private readonly Dictionary<EuContentClass, EuRightsDisposition> _classes;
    private readonly Dictionary<EuRightsExceptionChannel, EuRightsExceptionDisposition> _channels;

    private EuRightsMatrix(
        Dictionary<EuContentClass, EuRightsDisposition> classes,
        Dictionary<EuRightsExceptionChannel, EuRightsExceptionDisposition> channels)
    {
        _classes = classes;
        _channels = channels;
    }

    /// <summary>Every content class, in the order the closed enum declares them.</summary>
    public IReadOnlyList<EuRightsDisposition> ContentClasses =>
        Enum.GetValues<EuContentClass>().Select(value => _classes[value]).ToArray();

    /// <summary>Every exception channel, in the order the closed enum declares them.</summary>
    public IReadOnlyList<EuRightsExceptionDisposition> ExceptionChannels =>
        Enum.GetValues<EuRightsExceptionChannel>().Select(value => _channels[value]).ToArray();

    /// <summary>The reviewed reuse basis for one class. Present for every member.</summary>
    public EuRightsDisposition For(EuContentClass contentClass) =>
        _classes[ContractValidation.RequireDefined(contentClass, nameof(contentClass))];

    /// <summary>The acknowledgement for one exception channel. Present for every member.</summary>
    public EuRightsExceptionDisposition For(EuRightsExceptionChannel channel) =>
        _channels[ContractValidation.RequireDefined(channel, nameof(channel))];

    /// <summary>
    /// The only path that mints a matrix. Returns null with a typed refusal, because an incomplete
    /// legal position is a reviewable state to record rather than a programming error.
    /// </summary>
    public static EuRightsMatrix? TryAdmit(
        IReadOnlyList<EuRightsDisposition> contentClasses,
        IReadOnlyList<EuRightsExceptionDisposition> exceptionChannels,
        out EuRightsMatrixRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(contentClasses);
        ArgumentNullException.ThrowIfNull(exceptionChannels);

        var classes = new Dictionary<EuContentClass, EuRightsDisposition>();
        foreach (var disposition in contentClasses)
        {
            ArgumentNullException.ThrowIfNull(disposition);
            if (!classes.TryAdd(disposition.ContentClass, disposition))
            {
                refusal = EuRightsMatrixRefusal.DuplicateContentClass;
                return null;
            }
        }

        var channels = new Dictionary<EuRightsExceptionChannel, EuRightsExceptionDisposition>();
        foreach (var disposition in exceptionChannels)
        {
            ArgumentNullException.ThrowIfNull(disposition);
            if (!channels.TryAdd(disposition.Channel, disposition))
            {
                refusal = EuRightsMatrixRefusal.DuplicateExceptionChannel;
                return null;
            }
        }

        foreach (var contentClass in Enum.GetValues<EuContentClass>())
        {
            if (!classes.ContainsKey(contentClass))
            {
                refusal = EuRightsMatrixRefusal.ContentClassUndecided;
                return null;
            }
        }

        foreach (var channel in Enum.GetValues<EuRightsExceptionChannel>())
        {
            if (!channels.ContainsKey(channel))
            {
                refusal = EuRightsMatrixRefusal.ExceptionChannelUndecided;
                return null;
            }
        }

        refusal = EuRightsMatrixRefusal.None;
        return new EuRightsMatrix(classes, channels);
    }
}
