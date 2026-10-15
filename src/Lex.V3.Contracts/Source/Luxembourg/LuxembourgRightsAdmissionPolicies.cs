using System.Security.Cryptography;
using System.Text;

namespace Lex.V3.Contracts.Source.Luxembourg;

/// <summary>
/// The closed registry of Luxembourg rights admission policies: each entry is a canonical text, pinned in code, and the
/// SHA-256 of that text. A derivation names the policy it was made under by that digest, and a corpus header carries it.
/// </summary>
/// <remarks>
/// <para>
/// One entry is registered: policy /1, which admits exactly <c>agreed_same_run_cc_by</c>, the two channels of one run
/// agreeing on CC BY 4.0. It is the policy every <c>lex-lu-first-mount-acquisition</c> catalog and every corpus built so
/// far implies: <see cref="Lex.V3.Contracts.Source.Luxembourg.LuxembourgRightsChannelResolution.ChannelsAgreeOnAdmittingLicence"/>
/// is the corpus builder's admission rule, and nothing else admits. Release 1 of the Luxembourg design re-derives the
/// profiles only, so its re-derivation names this policy and no other: its admission is today's.
/// </para>
/// <para>
/// What is not here yet, stated so this is not cited for more than it holds: the registry's type checks (a policy naming a
/// disposition that can never admit cannot be built), policy A and every owner's ruling come with the rights slices (S1,
/// S9). A digest this registry does not hold names no policy, and every reader of one refuses it.
/// </para>
/// </remarks>
public static class LuxembourgRightsAdmissionPolicies
{
    /// <summary>The first line of policy /1's canonical text: its name.</summary>
    public const string Policy1Name = "lex-lu-rights-admission-policy/1";

    private const string Policy1Text =
        Policy1Name + "\n" +
        "admits=agreed_same_run_cc_by\n" +
        "single-channel-admission=none\n" +
        "single-channel-evidence-window=none\n" +
        "ruling=none;the-two-channel-agreement-every-lex-lu-first-mount-acquisition-catalog-and-every-corpus-before-it-implies\n";

    /// <summary>The SHA-256 of policy /1's canonical text, lowercase hex.</summary>
    public static string Policy1Sha256 { get; } = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Policy1Text)));

    /// <summary>Policy /1's canonical text, as UTF-8 bytes (a copy).</summary>
    public static byte[] CopyPolicy1Bytes() => Encoding.UTF8.GetBytes(Policy1Text);

    /// <summary>The digests of every registered policy, sorted ordinally.</summary>
    public static IReadOnlyList<string> RegisteredSha256s { get; } = Array.AsReadOnly(new[] { Policy1Sha256 });

    /// <summary>Whether <paramref name="sha256"/> is the digest of a registered policy.</summary>
    public static bool IsRegistered(string? sha256) =>
        sha256 is not null && RegisteredSha256s.Contains(sha256, StringComparer.Ordinal);
}
