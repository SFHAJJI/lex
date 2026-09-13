using System.Text.Json.Serialization;

namespace Lex.V3.Contracts.Source.Luxembourg;

/// <summary>
/// Where one recognized Luxembourg act legal-type sits relative to E10's counted population. Closed.
/// </summary>
/// <remarks>
/// E10 counts the never-consolidated LOI and RGD population and nothing else, so only those two are
/// in scope. Every other recognized legal-type is named here rather than left absent, because the
/// owner's ruling is that the manifest must classify the FULL recognized vocabulary: a code the
/// build knows is out of scope by decision, distinguishable from a code the build has never seen,
/// which is unrecognized and fails closed (<see cref="LuxembourgActClassManifest.TryClassify"/>).
/// </remarks>
public enum LuxembourgActClassScope
{
    /// <summary>The LOI legal-type — in E10's counted population.</summary>
    [JsonStringEnumMemberName("in_scope_loi")]
    InScopeLoi = 1,

    /// <summary>The RGD legal-type — in E10's counted population.</summary>
    [JsonStringEnumMemberName("in_scope_rgd")]
    InScopeRgd = 2,

    /// <summary>
    /// A legal-type the publisher emits and this build recognizes, but outside E10's counted
    /// population. Named, not absent: an out-of-scope decision, not an unrecognized code.
    /// </summary>
    [JsonStringEnumMemberName("recognized_out_of_scope")]
    RecognizedOutOfScope = 3,
}

/// <summary>
/// The closed classification of every recognized Luxembourg act legal-type against E10's counted
/// population, over the publisher's own exact authoritative resource-type IRIs.
/// </summary>
/// <remarks>
/// <para>
/// WHAT E10 COUNTS, AND WHAT IT DOES NOT. The owner's ruling of 2026-09-13 fixes the counted
/// population at exactly LOI and RGD, using the publisher's exact authoritative IRIs. "Maximum-scope
/// class manifest" means this artifact must classify the whole recognized legal-type vocabulary, not
/// that the counted population widens: every other recognized code is <see cref="LuxembourgActClassScope.RecognizedOutOfScope"/>,
/// named on purpose so a later count cannot silently include one, and cannot silently exclude a code
/// it never placed either.
/// </para>
/// <para>
/// AN UNKNOWN CODE FAILS CLOSED. <see cref="Classify"/> throws and <see cref="TryClassify"/> returns
/// false for any IRI not in the recognized vocabulary. A newly minted or misspelled legal-type is an
/// unrecognized code, and treating it as out-of-scope would be a silent scope decision the manifest
/// has no evidence for. It refuses, and a caller counting the population stops rather than guessing.
/// </para>
/// <para>
/// THE PUBLISHER'S REAL AUTHORITY, NOT A STAND-IN. A Legilux act's legal-type is its
/// <c>jolux:typeDocument</c> value, an IRI under the resource-type authority
/// (<see cref="VerifiedLuxembourgSourceProfile.TypeDocumentPrefix"/>); LOI and RGD are
/// <c>resource-type/LOI</c> and <c>resource-type/RGD</c>. The recognized vocabulary is drawn from
/// this build's own resolver rather than restated here - <c>LuxembourgActClassManifestTests</c>
/// reflects over <see cref="LuxembourgScopeResolver"/>'s resource-type code buckets and fails if any
/// code it recognizes is not classified here, so the two cannot drift into disagreement about what
/// "recognized" means.
/// </para>
/// </remarks>
public static class LuxembourgActClassManifest
{
    private const string Authority = VerifiedLuxembourgSourceProfile.TypeDocumentPrefix;

    /// <summary>The LOI legal-type class, as the publisher's resource-type authority states it.</summary>
    public static readonly string LoiClassIri = Authority + "LOI";

    /// <summary>The RGD legal-type class, as the publisher's resource-type authority states it.</summary>
    public static readonly string RgdClassIri = Authority + "RGD";

    /// <summary>
    /// Every recognized legal-type, classified. The two in-scope codes are LOI and RGD; the rest are
    /// the resource-type codes this build's resolver recognizes, each out of scope by decision.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, LuxembourgActClassScope> Recognized =
        BuildRecognized();

    private static Dictionary<string, LuxembourgActClassScope> BuildRecognized()
    {
        // OUT-OF-SCOPE CODES THE BUILD RECOGNIZES, by their resource-type suffix. This is the full
        // recognized act legal-type vocabulary minus LOI and RGD, and the drift guard test pins it
        // against the resolver's own buckets so a publisher addition cannot slip through unclassified.
        var outOfScope = new[]
        {
            "A", "AGC", "AGD", "AMIN", "ARGD", "CODE", "Constitution", "CONV", "ORD", "PROT", "REG",
            "RGC", "RI", "RMIN", "ST",
            "TC", "RECT", "ACC",
            "RCSF", "RBCL", "RILR",
            "ACCA", "RC",
            "DIV", "PA",
            "RECUEIL", "CODE_RECUEIL",
        };

        var recognized = new Dictionary<string, LuxembourgActClassScope>(StringComparer.Ordinal)
        {
            [LoiClassIri] = LuxembourgActClassScope.InScopeLoi,
            [RgdClassIri] = LuxembourgActClassScope.InScopeRgd,
        };
        foreach (var suffix in outOfScope)
        {
            recognized.Add(Authority + suffix, LuxembourgActClassScope.RecognizedOutOfScope);
        }

        return recognized;
    }

    /// <summary>Every recognized legal-type class IRI, in-scope and out-of-scope alike.</summary>
    public static IReadOnlyCollection<string> RecognizedClassIris => (IReadOnlyCollection<string>)Recognized.Keys;

    /// <summary>
    /// Classifies one publisher legal-type class IRI, or throws when the code is unrecognized.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The IRI is not a recognized legal-type. A caller contract violation, and the fail-closed
    /// direction the owner's ruling requires: an unrecognized code is not silently out of scope.
    /// </exception>
    public static LuxembourgActClassScope Classify(string publisherClassIri) =>
        TryClassify(publisherClassIri, out var scope)
            ? scope
            : throw new ArgumentException(
                $"'{publisherClassIri}' is not a recognized Luxembourg act legal-type; an unrecognized "
                + "code fails closed rather than being treated as out of scope.",
                nameof(publisherClassIri));

    /// <summary>
    /// Classifies one publisher legal-type class IRI, returning false for an unrecognized code.
    /// </summary>
    /// <remarks>
    /// The false is the fail-closed signal: <paramref name="scope"/> is only meaningful when this
    /// returns true, and a caller must not read an unrecognized code as out of scope.
    /// </remarks>
    public static bool TryClassify(string publisherClassIri, out LuxembourgActClassScope scope)
    {
        ArgumentNullException.ThrowIfNull(publisherClassIri);
        return Recognized.TryGetValue(publisherClassIri, out scope);
    }

    /// <summary>Whether a classified scope is one E10 counts. Exactly LOI and RGD.</summary>
    /// <remarks>
    /// FAILS CLOSED ON AN UNDEFINED SCOPE. This is a public counting boundary, separate from
    /// <see cref="TryClassify"/>, and its whole promise is that an unrecognized code cannot be
    /// counted. <c>TryClassify</c> leaves <c>scope</c> at <c>default</c> (an unnamed 0) on a miss, so
    /// a caller that passed that default straight here - ignoring the <c>bool</c> - would otherwise
    /// get the same "not counted" answer as a genuine <see cref="LuxembourgActClassScope.RecognizedOutOfScope"/>,
    /// collapsing the distinction the manifest exists to keep. An undefined scope is refused rather
    /// than answered, so a miss cannot be silently read as "not counted".
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The scope is not a declared member.</exception>
    public static bool IsCounted(LuxembourgActClassScope scope)
    {
        if (!Enum.IsDefined(scope))
        {
            throw new ArgumentOutOfRangeException(
                nameof(scope),
                scope,
                "An undefined scope is not a counting answer; an unrecognized code must be caught at "
                + "Classify/TryClassify and fail closed, not read as not-counted here.");
        }

        return scope is LuxembourgActClassScope.InScopeLoi or LuxembourgActClassScope.InScopeRgd;
    }
}
