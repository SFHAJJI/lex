using System.Security.Cryptography;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The three real Luxembourg publisher responses this lane's acquisition tests drive, retained as
/// files rather than described in prose. REVIEW_RESULT
/// lex-event-20260904T200339509Z-8a3db602c17c41389408981d2fb26535 defect three: they existed only
/// as digests in comments, so no test could actually serve one and nothing checked that the digests
/// still meant anything.
/// </summary>
/// <remarks>
/// Every load re-hashes and refuses a mismatch, so a fixture that is truncated, re-encoded or
/// swapped fails at the point of use rather than silently changing what a test proves.
/// </remarks>
internal static class LuxembourgDocumentFetchFixtures
{
    /// <summary>
    /// The real Akoma Ntoso body of loi 2017/03/14/a439, fetched from the www host with
    /// User-Agent Lex/0.1. Independently re-fetched on 2026-09-04 and byte for byte identical to
    /// the digest D1-06c-LU-1 pinned, which is fetch evidence: two independent fetches of the
    /// publisher agreeing. The publisher's bytes are the authority.
    /// </summary>
    internal const string XmlBodySha256 =
        "9e43a99e4b9735e383d989989d4005fc9e1676f4094c2633f30b2f056d5e476d";

    internal const int XmlBodyLength = 19_986;

    /// <summary>
    /// The real PDF of rgd 1977/11/16/n3, the PDF-only act: one pdf manifestation, no XML at all,
    /// which is the arm the format ladder falls through to.
    /// </summary>
    internal const string PdfBodySha256 =
        "13a73ea1a4eeb71f2530398e85192716abcaed0fffc34d7a7f6948adba48e699";

    internal const int PdfBodyLength = 124_932;

    /// <summary>
    /// The office's real 404, application/json.
    /// </summary>
    /// <remarks>
    /// THIS DIGEST IS PER FETCH AND IS NOT REPRODUCIBLE. The body carries a live timestamp and
    /// echoes the requested path, so every fetch differs. Three observations now exist for this one
    /// endpoint shape: 209 bytes, then 234 bytes (efd7f3ff..), then this one, 204 bytes, taken
    /// 2026-09-04T22:20Z. The digest below names THESE RETAINED BYTES so the fixture cannot be
    /// swapped; it is never a claim that a fresh fetch would reproduce it, and no test asserts that.
    /// </remarks>
    internal const string NotFoundBodySha256 =
        "b4e140344eddc8e62e8500c6479fb9b5a2807d47f16fe904e5d0c08204580bab";

    internal const int NotFoundBodyLength = 204;

    internal static byte[] XmlBody() => Load("lu-xml-200-body.bin", XmlBodySha256, XmlBodyLength);

    internal static byte[] PdfBody() => Load("lu-pdf-200-body.bin", PdfBodySha256, PdfBodyLength);

    internal static byte[] NotFoundBody() =>
        Load("lu-xml-404-body.bin", NotFoundBodySha256, NotFoundBodyLength);

    private static byte[] Load(string name, string expectedSha256, int expectedLength)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "LuDocumentFetch", name);
        var bytes = File.ReadAllBytes(path);
        var actual = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (bytes.Length != expectedLength || !string.Equals(actual, expectedSha256, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The retained fixture '{name}' is {bytes.Length} bytes with digest {actual}, not "
                + $"{expectedLength} bytes with {expectedSha256}. A fixture that no longer carries "
                + "the publisher bytes it names cannot stand in for them.");
        }

        return bytes;
    }
}
