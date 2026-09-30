using System.Security.Cryptography;
using System.Text;

namespace Lex.V3.Contracts.Source.Core;

/// <summary>
/// Mints an artifact resource identity from the bytes it names, so that identical content under
/// identical terms is always given the same name.
/// </summary>
/// <remarks>
/// <para>
/// Decision 77. A retained request policy names the render receipt by resource id and digest. The
/// binder used to mint that id with <c>Guid.NewGuid</c>, so two binds of identical inputs produced
/// policies differing on two lines, and the policy digest differed with them. That digest is a
/// member of the R3.3 absence key tuple, which means the absence key changed at every cut and an
/// absence history could never advance: three consecutive absent cuts would never share a key.
/// The observable symptom would have been law that is genuinely gone never being reported gone,
/// with nothing failing anywhere.
/// </para>
/// <para>
/// So an identifier that enters a retained policy, receipt or send closure is derived from content
/// or is not there at all. What is genuinely per-run keeps a fresh identity and lives in the
/// observation, which is where a run says when it happened: the acquisition run identity binds its
/// own start instant, and an observation binds its own, and neither belongs in the terms a request
/// was sent under.
/// </para>
/// <para>
/// The derivation is a name-based UUID in the sense of RFC 9562, with SHA-256 rather than the
/// SHA-1 of version 5, emitted as version 8 which that RFC reserves for exactly this. Using SHA-1
/// here would have introduced the one hash this repository does not otherwise use, to name
/// artifacts whose whole identity is a SHA-256. The scope string is part of the hashed input, so
/// two artifact kinds that happen to share a content digest do not collide onto one identity.
/// </para>
/// </remarks>
public static class ContentDerivedIdentity
{
    /// <summary>The scope for a machine-query render receipt.</summary>
    public const string RenderReceiptScope = "lex-v3/machine-query-render-receipt/1";

    /// <summary>
    /// Derives the UUID URN naming <paramref name="canonicalBytes"/> within
    /// <paramref name="scope"/>. Pure: the same scope and bytes always give the same URN, on any
    /// machine and in any process.
    /// </summary>
    public static string DeriveUuidUrn(string scope, ReadOnlySpan<byte> canonicalBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);

        // The scope and a zero separator precede the content, so that no pair of scope and content
        // can be re-cut to make a different pair with the same hashed input. A zero byte cannot
        // occur in the UTF-8 of a scope, which is what makes the separator sufficient rather than
        // decorative.
        using var hash = StartHash(scope);
        hash.AppendData(canonicalBytes);
        return CompleteIdentity(hash);
    }

    /// <summary>
    /// Derives the same identity from the stream's remaining bytes without buffering the content.
    /// Consumes to EOF and leaves the caller's stream open. Callers identifying a complete artifact
    /// must position its stream at the beginning before this call.
    /// </summary>
    public static string DeriveUuidUrnFromStream(
        string scope, Stream canonicalStream, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentNullException.ThrowIfNull(canonicalStream);
        if (!canonicalStream.CanRead)
            throw new ArgumentException("The content stream must be readable.", nameof(canonicalStream));
        cancellationToken.ThrowIfCancellationRequested();
        using var hash = StartHash(scope);
        Span<byte> buffer = stackalloc byte[4096];
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = canonicalStream.Read(buffer);
            if (read == 0) break;
            hash.AppendData(buffer[..read]);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return CompleteIdentity(hash);
    }

    private static IncrementalHash StartHash(string scope)
    {
        var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(scope));
        hash.AppendData("\0"u8);
        return hash;
    }

    private static string CompleteIdentity(IncrementalHash hash)
    {
        Span<byte> digest = stackalloc byte[32];
        hash.GetHashAndReset(digest);
        Span<byte> identifier = stackalloc byte[16];
        digest[..16].CopyTo(identifier);
        identifier[6] = (byte)((identifier[6] & 0x0F) | 0x80);
        identifier[8] = (byte)((identifier[8] & 0x3F) | 0x80);

        // Big-endian, explicitly. The Guid(byte[]) constructor reads the first three fields in the
        // platform's byte order, so the default would give one identity on a little-endian machine
        // and another elsewhere: a reproducibility fix that is not itself reproducible.
        return "urn:uuid:" + new Guid(identifier, bigEndian: true).ToString("D");
    }
}
