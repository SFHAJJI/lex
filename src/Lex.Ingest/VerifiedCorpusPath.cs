namespace Lex.Ingest;

internal static class VerifiedCorpusPath
{
    public static string RequireExisting(string root, string candidate, string description)
        => Lex.Temporal.ProtectedPath.RequireExisting(
            root, candidate, $"Corpus {description}");
}
