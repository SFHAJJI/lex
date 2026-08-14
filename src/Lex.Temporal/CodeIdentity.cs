namespace Lex.Temporal;

public static class CodeIdentity
{
    public static string RequireFullCommit(string? value, string fieldName)
        => RequireLowerHex(value, 40, fieldName,
            "a full lowercase 40-character Git commit SHA");

    public static string RequireFullGitObjectId(string? value, string fieldName)
        => RequireLowerHex(value, 40, fieldName,
            "a full lowercase 40-character Git object ID");

    public static string RequireSha256(string? value, string fieldName)
        => RequireLowerHex(value, 64, fieldName,
            "a lowercase 64-character SHA-256 digest");

    private static string RequireLowerHex(
        string? value, int length, string fieldName, string description)
    {
        if (value is null || value.Length != length
            || value.Any(character =>
                character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new InvalidDataException(
                $"{fieldName} must be {description}");
        return value;
    }
}
