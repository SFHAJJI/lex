using System.Security.Cryptography;
using Lex.V3.Api;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.WebHost.ConfigureKestrel(static options => options.AddServerHeader = false);
var app = builder.Build();

// The synthetic preview and its key are for tests and the S0-05 image only: the one-server image, which carries the law,
// ships neither (Lex.V3.Api.csproj; plan 0.7), so its server starts no preview and answers no preview route.
SyntheticApiState? state = null;
if (Directory.Exists(Path.Combine(AppContext.BaseDirectory, "preview-graph")))
{
    try
    {
        var assemblyPath = typeof(Program).Assembly.Location;
        await using var assembly = new FileStream(
            assemblyPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var runtimeSourceSha256 = Convert.ToHexStringLower(
            await SHA256.HashDataAsync(assembly, app.Lifetime.ApplicationStopping));
        state = await SyntheticApiBootstrap.OpenAsync(
            Path.Combine(AppContext.BaseDirectory, "preview-graph"),
            Path.Combine(AppContext.BaseDirectory, "preview-trust", "public-key.spki"),
            SyntheticPreviewTrustConfiguration.EnvironmentBinding,
            SyntheticPreviewTrustConfiguration.IssuerId,
            SyntheticPreviewTrustConfiguration.KeyId,
            SyntheticPreviewTrustConfiguration.PublicKeySha256,
            runtimeSourceSha256,
            immutableCustody: true,
            new CryptographicRequestEntropySource(),
            app.Lifetime.ApplicationStopping);
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine(SyntheticBootstrapDiagnostic.Describe(exception));
        state = SyntheticApiState.Unavailable;
    }
}

// A deployment whose temporary folder is this process's alone has the copies an earlier process left there removed first.
if (Environment.GetEnvironmentVariable(V3StaleIndexCopies.PrivateTemporaryFolderVariable) == "1")
{
    V3StaleIndexCopies.Remove(Path.GetTempPath());
}

V3CorpusMount? corpusMount = null;
try
{
    // The server's one door to a mount (index 9, 8 or 6).
    corpusMount = V3CorpusMount.Served(
        await V3CorpusMount.OpenAsync(
            Path.Combine(AppContext.BaseDirectory, "v3-corpus"),
            app.Lifetime.ApplicationStopping),
        Path.Combine(AppContext.BaseDirectory, "v3-corpus"));

    // What search's exact references read on their first use, read once here, so no request pays it or waits behind it.
    corpusMount?.Warm();
}
catch (Exception exception)
{
    Console.Error.WriteLine($"V3 corpus mount refused: {exception.GetType().Name}: {exception.Message}");
}

// The live pages beside the API, one origin for both (Decision 95, ruling 3). A directory this server
// refuses to serve (a page without its policy, a file of a type it does not serve) leaves the API
// running without pages rather than serving them wrong.
V3WebRoot? webRoot = null;
try
{
    // What the pages say to search engines is the release's switch (R67, O27): kept out unless LEX_V3_SEARCH_ENGINES is
    // `indexed`; a law's page is opened only for a work the indexing record beside the mount lists (R92, O28) and the mount
    // holds. With the site indexed, the mount is asked once, here, its build time and which listed works it holds.
    var searchEngines = Environment.GetEnvironmentVariable(V3WebRoot.SearchEnginesVariable);
    var (indexingRecord, lastmod) = string.Equals(V3WebRoot.SearchEnginesState(searchEngines), V3WebRoot.Indexed, StringComparison.Ordinal) && corpusMount is not null
        ? V3WebRoot.SearchEngineFacts(
            V3WebRoot.ReadIndexingRecordFile(Path.Combine(AppContext.BaseDirectory, "v3-corpus", V3WebRoot.IndexingRecordFile)),
            (operationId, parameters) => V3ApiHandler.AnswerAtStartup(corpusMount, static () => DateTimeOffset.UtcNow, operationId, parameters))
        : ((V3WebRoot.IndexingRecord?)null, (string?)null);
    webRoot = V3WebRoot.Open(Path.Combine(AppContext.BaseDirectory, V3WebRoot.DirectoryName), searchEngines, indexingRecord, lastmod);
}
catch (Exception exception)
{
    Console.Error.WriteLine($"V3 web pages refused: {exception.GetType().Name}: {exception.Message}");
}

app.Lifetime.ApplicationStopped.Register(() =>
{
    corpusMount?.Dispose();
    state?.Dispose();
});
app.Run(V3ApiHandler.CreateRequestDelegate(
    state,
    static () => DateTimeOffset.UtcNow,
    corpusMount,
    webRoot));
await app.RunAsync();

public partial class Program;
