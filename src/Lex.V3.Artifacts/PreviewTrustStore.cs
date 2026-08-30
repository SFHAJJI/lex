namespace Lex.V3.Artifacts;

public interface IPreviewTrustStore
{
    bool ContainsIssuer(string issuerId);

    bool TryGetSubjectPublicKeyInfo(
        string issuerId,
        string keyId,
        out ReadOnlyMemory<byte> subjectPublicKeyInfo);
}
