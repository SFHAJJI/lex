using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Lex.V3.Ingest;

/// <summary>
/// Produces the SQLite build-options identity that can affect a persisted index while excluding
/// the compiler and synchronization backend selected for the current operating system.
/// </summary>
internal static class SqlitePortableProvenance
{
    internal static string CompileOptionsSha256(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA compile_options";
        using var reader = command.ExecuteReader();
        var values = new List<string>();
        while (reader.Read()) values.Add(reader.GetString(0));
        return CompileOptionsSha256(values);
    }

    internal static string CompileOptionsSha256(IEnumerable<string> values)
    {
        var portable = values
            .Where(static value =>
                !value.StartsWith("ATOMIC_INTRINSICS=", StringComparison.Ordinal) &&
                !value.StartsWith("COMPILER=", StringComparison.Ordinal) &&
                !value.StartsWith("MUTEX_", StringComparison.Ordinal))
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();
        return Convert.ToHexStringLower(
            SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(portable)));
    }
}
