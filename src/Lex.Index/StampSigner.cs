using System.Security.Cryptography;
using System.Text;

namespace Lex.Index;

/// <summary>
/// D40 — signed index stamp. ECDSA P-256 over the canonical stamp text; the algorithm is
/// recorded in the stamp itself. Makes every served hash attributable to a keyholder.
/// </summary>
public static class StampSigner
{
    public const string Algorithm = "ECDSA-P256-SHA256";

    public static string CanonicalText(IReadOnlyDictionary<string, string> stamp) =>
        string.Join("\n", stamp
            .Where(kv => kv.Key is not ("signature" or "public_key"))
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => $"{kv.Key}={kv.Value}"));

    public static (string SignatureB64, string PublicKeyPem) Sign(IReadOnlyDictionary<string, string> stamp, string privateKeyPem)
    {
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(privateKeyPem);
        var sig = ecdsa.SignData(Encoding.UTF8.GetBytes(CanonicalText(stamp)), HashAlgorithmName.SHA256);
        return (Convert.ToBase64String(sig), ecdsa.ExportSubjectPublicKeyInfoPem());
    }

    public static bool Verify(IReadOnlyDictionary<string, string> stamp)
    {
        if (!stamp.TryGetValue("signature", out var sigB64) || !stamp.TryGetValue("public_key", out var pubPem))
            return false;
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(pubPem);
        return ecdsa.VerifyData(
            Encoding.UTF8.GetBytes(CanonicalText(stamp)),
            Convert.FromBase64String(sigB64),
            HashAlgorithmName.SHA256);
    }

    public static string CreateKeyPem()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return ecdsa.ExportPkcs8PrivateKeyPem();
    }
}
