namespace Lex.V3.Contracts;

internal static class PreviewDocumentCanonicalizer
{
    public const string Identity = "lex-v3-preview-document-canonical-json/1";

    public static byte[] Canonicalize(PreviewOperationCatalog value) =>
        ContractCanonicalizer.Canonicalize(value, Identity, PreviewContractLimits.MaximumPayloadDepth);

    public static byte[] Canonicalize(PreviewRefusalRegistry value) =>
        ContractCanonicalizer.Canonicalize(value, Identity, PreviewContractLimits.MaximumPayloadDepth);

    public static byte[] Canonicalize(PreviewObjectSet value) =>
        ContractCanonicalizer.Canonicalize(value, Identity, PreviewContractLimits.MaximumPayloadDepth);

    internal static byte[] CanonicalizeJsonForEvidence(string json) =>
        ContractCanonicalizer.CanonicalizeJson(json, Identity, PreviewContractLimits.MaximumPayloadDepth);
}
