using System.Security.Cryptography;

namespace Lex.V3.Api;

internal static class SyntheticBootstrapDiagnostic
{
    public static string Describe(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var reason = exception switch
        {
            SyntheticImmutableCustodyException => "immutable_custody",
            FileNotFoundException or DirectoryNotFoundException => "required_file_missing",
            UnauthorizedAccessException => "required_file_unreadable",
            CryptographicException => "cryptographic_verification",
            OperationCanceledException => "startup_cancelled",
            InvalidDataException => "invalid_artifact_or_index",
            _ => "unexpected",
        };
        return $"lex_v3_preview_bootstrap_failed reason={reason}";
    }
}
