using System.Security.Cryptography;
using System.Text;

namespace Lex.V3.Contracts.Source.Luxembourg;

/// <summary>
/// The cursor key this publisher actually computes for a lexical value. NOT SPARQL 1.1 SHA-256.
/// </summary>
/// <remarks>
/// <para>
/// <b>THIS IS A PUBLISHER-PROFILE CODEC, NOT A STANDARD.</b> SPARQL 1.1 §17.4.4.8 defines
/// <c>SHA256</c> over the UTF-8 representation of the lexical form. The Legilux endpoint does not
/// implement that. It hashes the value <b>double UTF-8 encoded</b>: the UTF-8 bytes of the lexical
/// form are reinterpreted as Latin-1 code points, and those code points are re-encoded as UTF-8
/// before hashing. For <c>é</c> (U+00E9) the conforming input is <c>c3 a9</c> and this endpoint's
/// input is <c>c3 83 c2 a9</c>.
/// </para>
/// <para>
/// ASCII IS A FIXED POINT of that transform, which is exactly what makes it dangerous: it agrees
/// with the standard on every ASCII value and silently diverges on every value carrying a non-ASCII
/// character - which, in a corpus of French legal titles, is most of the titles. Nothing here may
/// be described as SHA-256 of the value, and the name of this type exists to stop the next reader
/// making that substitution.
/// </para>
/// <para>
/// MEASURED, AND THE ALTERNATIVES REJECTED RATHER THAN THE WINNER PREFERRED. A governed canary was
/// aimed at five retained drafts chosen because each carries a byte in <c>0x80-0x9F</c>, the range
/// where single-byte codecs disagree; the earlier canary carried none and therefore could not
/// separate them. Twenty-two candidate algorithms were registered and twenty-one were rejected by
/// the delivered keys: conforming SPARQL 1.1, cp1252, iso8859-15, cp850, cp437, mac-roman, cp1250,
/// cp1251, koi8-r, iso8859-2, the narrow-store family, transcode-into-charset, UTF-16LE and BE,
/// NFC, NFD, an escaped serialization, and double reinterpretation. Latin-1 is the sole survivor
/// across all six discriminating rows, and it was not chosen for being total.
/// </para>
/// <para>
/// The two separations that did the work: <b>cp1252</b> fails on every discriminating row and
/// cannot process <c>pl/1989/60</c> at all, whose <c>0x81</c> is one of its five undefined slots;
/// <b>iso8859-15</b> survived five rows and is rejected by <c>pl/2004/158</c>, which carries
/// <c>0xA8</c> - one of the eight positions where Latin-9 diverges from Latin-1. Evidence:
/// <c>artifacts/e8-batch-8bc575b849c240e6800ab3318cc90c41</c>, 2026-09-11T11:46:28Z, five wire
/// requests. The codec reproduces all 115 delivered keys in that packet with no exception.
/// </para>
/// <para>
/// THE RAW VALUE IS UNTOUCHED. This computes a key FROM the value; it never replaces, normalises,
/// truncates or re-encodes what is projected, retained or decoded. The reinterpretation happens
/// inside this method and its result leaves as a hex digest and nothing else.
/// </para>
/// <para>
/// IF THE PUBLISHER IS EVER FIXED, this fails closed rather than silently changing meaning. A
/// corrected endpoint would deliver conforming digests, local recomputation would stop matching,
/// and the producer refuses the row as it does today for any key that does not describe its value.
/// That is drift surfacing as a refusal, which is what S2-A05 requires, and it is the reason the
/// comparison stays local rather than trusting whatever arrives.
/// </para>
/// </remarks>
public static class LuxembourgPublisherCursorCodec
{
    /// <summary>What this codec is, for receipts and refusals that must not say "SHA-256".</summary>
    public const string Identity = "legilux-virtuoso-latin1-rewiden-sha256/1";

    /// <summary>
    /// Strict UTF-8: a malformed value must throw rather than be silently replaced, because a
    /// substituted character would key a row the publisher never delivered.
    /// </summary>
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>
    /// The key this publisher computes for <paramref name="lexical"/>, lowercase hexadecimal.
    /// </summary>
    /// <remarks>
    /// Latin-1 is total - every byte from <c>0x00</c> to <c>0xFF</c> maps to a code point - so this
    /// is defined for every value the publisher can deliver and cannot throw on the reinterpretation
    /// step. That totality is a property of the measured codec, not the reason it was selected.
    /// </remarks>
    public static string ComputeKey(string lexical)
    {
        ArgumentNullException.ThrowIfNull(lexical);

        // The publisher's own input: UTF-8 bytes read back as Latin-1 code points, re-encoded UTF-8.
        var utf8 = StrictUtf8.GetBytes(lexical);
        var rewidened = new char[utf8.Length];
        for (var index = 0; index < utf8.Length; index++)
        {
            rewidened[index] = (char)utf8[index];
        }

        return Convert.ToHexStringLower(
            SHA256.HashData(StrictUtf8.GetBytes(new string(rewidened))));
    }

    /// <summary>
    /// What a conforming SPARQL 1.1 <c>SHA256</c> would produce, for diagnosis only.
    /// </summary>
    /// <remarks>
    /// Present so a refusal can say what the value WOULD have keyed to under the standard, which is
    /// how the divergence was found and how a future publisher correction will be recognised. It is
    /// never what a delivered key is compared against - <see cref="ComputeKey"/> is - and no caller
    /// may admit a row on this.
    /// </remarks>
    public static string ConformingKeyForDiagnosisOnly(string lexical)
    {
        ArgumentNullException.ThrowIfNull(lexical);
        return Convert.ToHexStringLower(SHA256.HashData(StrictUtf8.GetBytes(lexical)));
    }
}
