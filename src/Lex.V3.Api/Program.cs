using System.Security.Cryptography;
using Lex.V3.Api;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.WebHost.ConfigureKestrel(static options => options.AddServerHeader = false);
var app = builder.Build();

SyntheticApiState state;
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

app.Lifetime.ApplicationStopped.Register(state.Dispose);
app.Run(context => SyntheticApiHandler.HandleAsync(context, state, context.RequestAborted));
await app.RunAsync();

public partial class Program;
