using System.Security.Cryptography;

namespace Lex.V3.Api;

internal interface IRequestEntropySource
{
    void Fill(Span<byte> destination);
}

internal sealed class CryptographicRequestEntropySource : IRequestEntropySource
{
    public void Fill(Span<byte> destination) => RandomNumberGenerator.Fill(destination);
}

internal static class RequestReferenceFactory
{
    private const int EntropyBytes = 16;

    public static string Create(IRequestEntropySource entropySource)
    {
        ArgumentNullException.ThrowIfNull(entropySource);
        Span<byte> bytes = stackalloc byte[EntropyBytes];
        entropySource.Fill(bytes);
        return $"req_{Convert.ToHexStringLower(bytes)}";
    }
}
