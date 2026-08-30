namespace Lex.V3.Preview;

public sealed record SyntheticPreviewBuildResult(
    string SourcePath,
    string SourceSha256,
    long SourceBytes,
    string DerivedPath,
    string DerivedSha256,
    long DerivedBytes,
    string SqlitePath,
    string SqliteSha256,
    long SqliteBytes,
    string NormalizationProfileIdentity,
    string NormalizationProfileSha256,
    string DdlSha256,
    string ScopeCanonicalJson,
    string ScopeSha256,
    string LogicalRowsCanonicalJson,
    string LogicalRowsSha256,
    string BuildIdentity,
    SyntheticSqliteProvenance SqliteProvenance);

public static class SyntheticPreviewBuilder
{
    public static SyntheticPreviewBuildResult BuildCanonical(string buildRoot) =>
        Build(buildRoot, SyntheticPreviewBuildContract.CanonicalSourceUtf8);

    internal static SyntheticPreviewBuildResult Build(
        string buildRoot,
        ReadOnlySpan<byte> source,
        bool includeCandidate = true)
    {
        var transport = SyntheticSourceStore.PersistAndNormalize(buildRoot, source);
        var index = SyntheticSqliteIndex.Build(buildRoot, transport, includeCandidate);
        return new SyntheticPreviewBuildResult(
            transport.SourcePath,
            transport.SourceSha256,
            transport.SourceBytes,
            transport.DerivedPath,
            transport.DerivedSha256,
            transport.DerivedBytes,
            index.SqlitePath,
            index.SqliteSha256,
            index.SqliteBytes,
            SyntheticPreviewBuildContract.NormalizationProfileIdentity,
            SyntheticPreviewBuildContract.NormalizationProfileSha256,
            index.DdlSha256,
            index.ScopeCanonicalJson,
            index.ScopeSha256,
            index.LogicalRowsCanonicalJson,
            index.LogicalRowsSha256,
            index.BuildIdentity,
            index.Provenance);
    }
}
