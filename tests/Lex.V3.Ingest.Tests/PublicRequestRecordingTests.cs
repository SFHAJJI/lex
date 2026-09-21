using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Lex.V3.Api;
using Lex.V3.Contracts.Platform;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using static Lex.V3.Ingest.Tests.V3CorpusResolveMountTests;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// S4-A11: a request's query text, the caller's address and the caller's user agent are not recorded. The
/// tests here hold that on the served handler, where it can be observed, and on the source of the public
/// process, where a new place to record would have to appear.
/// </summary>
/// <remarks>
/// Not claimed: hosting and ingress logs (the platform sees the client address and the URL before this
/// process does, and there is no deploy configuration for the Api in this repository), and what the libraries
/// the Api calls do outside the request path these tests drive. The response may carry the caller's own query
/// text back to the caller, and does: that is an answer, and it is not a record.
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class PublicRequestRecordingTests
{
    private sealed record Sentinels(string UserText, string Address, string Agent, string Forwarded, string Referer);

    private static readonly Sentinels First = new(
        "zqx-user-text-7731", "203.0.113.77", "SentinelBrowser/9.9 zqx-agent-4419", "198.51.100.23", "https://referer.example/zqx-5521");

    private static readonly Sentinels Second = new(
        "wvk-other-text-2086", "203.0.113.190", "OtherBrowser/1.0 wvk-agent-9052", "198.51.100.201", "https://other.example/wvk-8814");

    private sealed record Observation(int Status, string Headers, string Body, string StandardOut, string StandardError, string? RequestRef);

    private sealed record Scenario(string Name, Func<Sentinels, (string Target, string Method, string Body)> Build, bool AnswersWithAnEnvelope);

    private static string Operation(string id, string parameters) =>
        "{\"operation_id\":\"" + id + "\",\"parameters\":" + parameters + "}";

    private static readonly Scenario[] Scenarios =
    [
        new("search", s => ("/api/v3/search", "POST", Operation("search", "{\"query\":\"" + s.UserText + "\",\"language\":\"fra\"}")), true),
        new("resolve", s => ("/api/v3/resolve", "POST", Operation("resolve", "{\"identifier\":\"" + s.UserText + "\"}")), true),
        new("as_of", s => ("/api/v3/as_of", "POST", Operation("as_of", "{\"identifier\":\"" + s.UserText + "\",\"date\":\"2024-02-01\",\"language\":\"fra\"}")), true),
        new("a body cut off mid-value", s => ("/api/v3/search", "POST", "{\"operation_id\":\"search\",\"parameters\":{\"query\":\"" + s.UserText), false),
        new("another operation's body on the route", s => ("/api/v3/search", "POST", Operation("resolve", "{\"query\":\"" + s.UserText + "\"}")), false),
        new("a query string on the target", s => ("/api/v3/search?q=" + s.UserText, "POST", Operation("search", "{\"query\":\"x\"}")), false),
        new("a route that does not exist", s => ("/api/v3/" + s.UserText, "POST", "{}"), false),
        new("the wrong method", s => ("/api/v3/search", "GET", string.Empty), false),
        new("the preview route", s => ("/api/v3-preview/resolve?x=" + s.UserText, "GET", string.Empty), false),
    ];

    private static DefaultHttpContext Context(Scenario scenario, Sentinels who, string traceIdentifier)
    {
        var (target, method, body) = scenario.Build(who);
        var bytes = Encoding.UTF8.GetBytes(body);
        var context = new DefaultHttpContext { TraceIdentifier = traceIdentifier };
        context.Request.Method = method;
        context.Request.Body = new MemoryStream(bytes);
        context.Request.ContentLength = bytes.Length;
        context.Request.Headers.UserAgent = who.Agent;
        context.Request.Headers["X-Forwarded-For"] = who.Forwarded;
        context.Request.Headers.Referer = who.Referer;
        context.Connection.RemoteIpAddress = IPAddress.Parse(who.Address);
        context.Connection.RemotePort = 51234;
        context.Features.Get<IHttpRequestFeature>()!.RawTarget = target;
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static async Task<Observation> ObserveAsync(V3ApiHandler handler, Scenario scenario, Sentinels who, string traceIdentifier)
    {
        var context = Context(scenario, who, traceIdentifier);
        var previousOut = Console.Out;
        var previousError = Console.Error;
        using var standardOut = new StringWriter();
        using var standardError = new StringWriter();
        Console.SetOut(standardOut);
        Console.SetError(standardError);
        try
        {
            await handler.HandleAsync(context, CancellationToken.None);
        }
        finally
        {
            Console.SetOut(previousOut);
            Console.SetError(previousError);
        }

        var headers = string.Join("\n", context.Response.Headers.Select(static pair => pair.Key + ": " + pair.Value));
        var body = Encoding.UTF8.GetString(ResponseBytes(context));
        return new Observation(
            context.Response.StatusCode, headers, body, standardOut.ToString(), standardError.ToString(), RequestRefOf(body));
    }

    private static string? RequestRefOf(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return Find(document.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }

        static string? Find(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in element.EnumerateObject())
                {
                    if (property.NameEquals("request_ref") && property.Value.ValueKind == JsonValueKind.String)
                    {
                        return property.Value.GetString();
                    }

                    if (Find(property.Value) is { } nested)
                    {
                        return nested;
                    }
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                {
                    if (Find(item) is { } nested)
                    {
                        return nested;
                    }
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Where a sentinel turned up that it must not. The caller's address, agent, forwarded address and referrer
    /// appear nowhere. The caller's own text may be echoed in the body, where the answer says what was asked, and
    /// appears nowhere else.
    /// </summary>
    private static IReadOnlyList<string> Leaks(Observation seen, Sentinels who)
    {
        var found = new List<string>();
        var places = new[]
        {
            ("standard output", seen.StandardOut),
            ("standard error", seen.StandardError),
            ("response headers", seen.Headers),
            ("response body", seen.Body),
        };
        foreach (var (kind, value) in new[] { ("address", who.Address), ("agent", who.Agent), ("forwarded address", who.Forwarded), ("referrer", who.Referer) })
        {
            found.AddRange(places.Where(place => place.Item2.Contains(value, StringComparison.Ordinal))
                .Select(place => $"{kind} in {place.Item1}"));
        }

        found.AddRange(places.Where(place => place.Item1 != "response body" && place.Item2.Contains(who.UserText, StringComparison.Ordinal))
            .Select(place => $"query text in {place.Item1}"));
        return found;
    }

    private static V3ApiHandler HandlerOver(V3CorpusMount? mount) =>
        new(SyntheticApiState.Unavailable, new V3PlatformHost(), static () => ObservedAt, mount);

    private static string[] Snapshot(string directory) =>
        Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .OrderBy(static path => path, StringComparer.Ordinal)
            .Select(path => Path.GetRelativePath(directory, path) + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))))
            .ToArray();

    [TestMethod]
    public async Task NothingARequestCarriesReachesAnyOutputOfTheRealHandlerOnAnyOfItsPaths()
    {
        var fixture = await V3CorpusResolveMountTests.MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var before = Snapshot(fixture.Directory);

        foreach (var withCorpus in new[] { true, false })
        {
            var handler = HandlerOver(withCorpus ? mount : null);
            foreach (var scenario in Scenarios)
            {
                var seen = await ObserveAsync(handler, scenario, First, "trace-" + scenario.Name.Length);
                var leaks = Leaks(seen, First);
                Assert.IsEmpty(leaks, $"{scenario.Name} ({(withCorpus ? "corpus mounted" : "no corpus")}): {string.Join("; ", leaks)}");
            }
        }

        CollectionAssert.AreEqual(before, Snapshot(fixture.Directory), "The handler wrote to, or changed, a file in the mount directory.");
    }

    [TestMethod]
    public async Task TheSentinelsReallyReachTheHandlerSoTheAbsencesAboveAreNotVacuous()
    {
        var fixture = await V3CorpusResolveMountTests.MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var handler = HandlerOver(mount);

        var search = await ObserveAsync(handler, Scenarios[0], First, "trace-control");
        Assert.AreEqual(StatusCodes.Status200OK, search.Status, search.Body);
        Assert.Contains(First.UserText, search.Body, "The query text never reached the search, so a check that it is not recorded proves nothing.");
        Assert.IsNotNull(search.RequestRef);

        var context = Context(Scenarios[0], First, "trace-control");
        Assert.AreEqual(First.Agent, context.Request.Headers.UserAgent.ToString());
        Assert.AreEqual(First.Address, context.Connection.RemoteIpAddress!.ToString());
        Assert.AreEqual(First.Forwarded, context.Request.Headers["X-Forwarded-For"].ToString());
    }

    [TestMethod]
    public async Task TheRequestReferenceIsAFunctionOfTheTraceIdentifierAndNothingTheCallerSent()
    {
        var fixture = await V3CorpusResolveMountTests.MountedFixture.CreateAsync();
        await using var cleanup = fixture;
        using var mount = await V3CorpusMount.OpenAsync(fixture.Directory, CancellationToken.None);
        Assert.IsNotNull(mount);
        var handler = HandlerOver(mount);

        foreach (var scenario in Scenarios.Where(static value => value.AnswersWithAnEnvelope))
        {
            var one = await ObserveAsync(handler, scenario, First, "trace-alpha");
            var other = await ObserveAsync(handler, scenario, Second, "trace-alpha");
            var elsewhere = await ObserveAsync(handler, scenario, First, "trace-beta");

            Assert.IsNotNull(one.RequestRef, scenario.Name);
            Assert.AreEqual(
                one.RequestRef, other.RequestRef,
                $"{scenario.Name}: the same trace identifier gave a different reference for a different query, address and agent, so the reference is derived from something the caller sent. A hash of it would pass a search for the value and not this.");
            Assert.AreNotEqual(one.RequestRef, elsewhere.RequestRef, $"{scenario.Name}: the reference does not move with the trace identifier, so it is a constant and the equality above proves nothing.");
        }
    }

    [TestMethod]
    public void TheLeakCheckSeesASentinelInEachPlaceItIsMeantToWatch()
    {
        var clean = new Observation(200, "Cache-Control: no-store", "{\"requested_query\":\"" + First.UserText + "\"}", string.Empty, string.Empty, "http_a");
        Assert.IsEmpty(Leaks(clean, First), "The caller's text echoed in the body is an answer, not a leak.");

        CollectionAssert.AreEquivalent(new[] { "agent in standard error" }, Leaks(clean with { StandardError = "ua=" + First.Agent }, First).ToArray());
        CollectionAssert.AreEquivalent(new[] { "address in standard output" }, Leaks(clean with { StandardOut = First.Address }, First).ToArray());
        CollectionAssert.AreEquivalent(new[] { "forwarded address in response headers" }, Leaks(clean with { Headers = "X-Echo: " + First.Forwarded }, First).ToArray());
        CollectionAssert.AreEquivalent(new[] { "referrer in response body" }, Leaks(clean with { Body = First.Referer }, First).ToArray());
        CollectionAssert.AreEquivalent(new[] { "query text in standard error" }, Leaks(clean with { StandardError = First.UserText }, First).ToArray());
        CollectionAssert.AreEquivalent(new[] { "query text in response headers" }, Leaks(clean with { Headers = "Location: /" + First.UserText }, First).ToArray());
    }

    // The source of the public process. Comments and literals are blanked first, so a word in a comment or a
    // message is not code, and each rule names what a place to record or a source of client identity looks like.
    private static readonly (string Rule, Regex Pattern)[] Rules =
    [
        ("the caller's address", new Regex(@"\b(RemoteIpAddress|LocalIpAddress|RemotePort|LocalPort|ClientCertificate)\b")),
        ("the caller's headers", new Regex(@"(?<![Rr]esponse)\.Headers\b|\bHeaderNames\b|\bIHeaderDictionary\b")),
        ("the agent, the referrer or a cookie", new Regex(@"\b(UserAgent|Referer|Cookies?|Authorization)\b")),
        ("forwarded-header handling", new Regex(@"\b(UseForwardedHeaders|ForwardedHeaders\w*)\b")),
        ("the connection", new Regex(@"\.Connection\b")),
        ("the path or query of the request", new Regex(@"[Rr]equest\.(Query|QueryString|Path|PathBase|Host|Scheme|Protocol|Form|ContentType)\b")),
        ("a logger", new Regex(@"\b(ILogger\w*|LoggerFactory|LoggerMessage|LogInformation|LogWarning|LogError|LogDebug|LogTrace|LogCritical)\b")),
        ("a logging or telemetry provider", new Regex(@"\b(AddConsole|AddSimpleConsole|AddJsonConsole|AddDebug|AddEventLog|AddEventSourceLogger|AddOpenTelemetry|UseHttpLogging|AddHttpLogging|W3CLogging\w*|ActivitySource|Meter|EventSource|TelemetryClient)\b")),
        ("a trace or debug write", new Regex(@"\b(Trace|Debug)\.(Write\w*|Print\w*|Assert|Fail)\b")),
        ("a file or stream writer", new Regex(@"\b(StreamWriter|BinaryWriter)\b|\bFile\.(OpenWrite|Write\w*|Append\w*|Create\w*|Move|Copy|Replace)\b")),
        ("a database opened for writing", new Regex(@"\bSqliteOpenMode\.ReadWrite\w*")),
        ("a feature other than the request's raw target", new Regex(@"Features\.(Get|Set)<(?!IHttpRequestFeature>)")),
    ];

    private static IReadOnlyList<string> Findings(string code)
    {
        var found = Rules.Where(rule => rule.Pattern.IsMatch(code)).Select(static rule => rule.Rule).ToList();
        var members = Regex.Matches(code, @"IHttpRequestFeature>\(\)\??\.(\w+)").Select(static match => match.Groups[1].Value).Distinct().ToArray();
        if (members.Except(["RawTarget"], StringComparer.Ordinal).Any())
        {
            found.Add("a member of the request feature other than the raw target");
        }

        return found;
    }

    /// <summary>The code of a C# source with its comments removed and every string and character literal emptied.</summary>
    private static string Blank(string source)
    {
        var code = new StringBuilder(source.Length);
        var index = 0;
        while (index < source.Length)
        {
            var current = source[index];
            var next = index + 1 < source.Length ? source[index + 1] : '\0';
            if (current == '/' && next == '/')
            {
                while (index < source.Length && source[index] != '\n')
                {
                    index++;
                }
            }
            else if (current == '/' && next == '*')
            {
                var end = source.IndexOf("*/", index + 2, StringComparison.Ordinal);
                index = end < 0 ? source.Length : end + 2;
            }
            else if (current == '"' && source.AsSpan(index).StartsWith("\"\"\"", StringComparison.Ordinal))
            {
                var end = source.IndexOf("\"\"\"", index + 3, StringComparison.Ordinal);
                index = end < 0 ? source.Length : end + 3;
                code.Append("\"\"");
            }
            else if (current == '"')
            {
                var verbatim = index > 0 && (source[index - 1] == '@' || (index > 1 && source[index - 1] == '$' && source[index - 2] == '@'));
                index++;
                while (index < source.Length)
                {
                    if (verbatim && source[index] == '"' && index + 1 < source.Length && source[index + 1] == '"')
                    {
                        index += 2;
                    }
                    else if (!verbatim && source[index] == '\\')
                    {
                        index += 2;
                    }
                    else if (source[index] == '"')
                    {
                        break;
                    }
                    else
                    {
                        index++;
                    }
                }

                index++;
                code.Append("\"\"");
            }
            else if (current == '\'')
            {
                index++;
                while (index < source.Length && source[index] != '\'')
                {
                    index += source[index] == '\\' ? 2 : 1;
                }

                index++;
                code.Append("''");
            }
            else
            {
                code.Append(current);
                index++;
            }
        }

        return code.ToString();
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Lex.V3.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static (string File, string Code)[] ApiSources()
    {
        var api = Path.Combine(RepositoryRoot(), "src", "Lex.V3.Api");
        var separator = Path.DirectorySeparatorChar;
        return Directory.EnumerateFiles(api, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{separator}obj{separator}") && !path.Contains($"{separator}bin{separator}"))
            .Select(path => (Path.GetRelativePath(api, path).Replace('\\', '/'), Blank(File.ReadAllText(path, Encoding.UTF8))))
            .OrderBy(static value => value.Item1, StringComparer.Ordinal)
            .ToArray();
    }

    [TestMethod]
    public void TheApiSourceNamesNoSourceOfClientIdentityAndNoPlaceToRecordIt()
    {
        var sources = ApiSources();
        Assert.IsGreaterThan(10, sources.Length, "The census found too few source files to be looking at the Api project.");
        foreach (var (file, code) in sources)
        {
            var findings = Findings(code);
            Assert.IsEmpty(findings, $"{file} now contains {string.Join("; ", findings)}. Each is a way for a request's query text, address or agent to be read or to be put somewhere, and S4-A11 says none of them is recorded.");
        }
    }

    private static IReadOnlyList<string> WriteCapableOpens(string code) =>
        Regex.Matches(code, @"\bFileAccess\.(Write|ReadWrite)\b|\bFileMode\.(Create|CreateNew|OpenOrCreate|Append|Truncate)\b")
            .Select(static match => match.Value)
            .ToArray();

    [TestMethod]
    public void TheOnlyFilesTheApiProcessOpensForWritingAreTheStartupProbesThatItsGraphDirectoryCannotBeWrittenTo()
    {
        var opens = ApiSources()
            .Select(static source => (source.File, Opens: WriteCapableOpens(source.Code)))
            .Where(static value => value.Opens.Count > 0)
            .ToArray();

        // SyntheticImmutableCustody proves at startup that the runtime user cannot change the graph directory:
        // it tries to open a member for write and to create a probe file that deletes itself on close, and
        // refuses to start if either succeeds. Neither writes a byte, and neither is on a request's path.
        Assert.HasCount(1, opens, string.Join("; ", opens.Select(static value => value.File)));
        Assert.AreEqual("SyntheticImmutableCustody.cs", opens[0].File);
        CollectionAssert.AreEqual(new[] { "FileAccess.Write", "FileMode.CreateNew", "FileAccess.Write" }.OrderBy(static value => value, StringComparer.Ordinal).ToArray(),
            opens[0].Opens.OrderBy(static value => value, StringComparer.Ordinal).ToArray());

        CollectionAssert.AreEqual(
            new[] { "FileMode.Truncate", "FileAccess.Write" },
            WriteCapableOpens(Blank("new FileStream(p, FileMode.Truncate, FileAccess.Write); var a = FileAccess.Read; var b = FileMode.Open;")).ToArray(),
            "The scanner must see a write-capable open and must not see a read.");
    }

    [TestMethod]
    public void TheOnlyOutputTheApiProcessWritesIsTwoStartupMessagesAndItsLoggingProvidersAreCleared()
    {
        var sources = ApiSources();
        var console = sources
            .SelectMany(source => Regex.Matches(source.Code, @"\bConsole\b[^;]*").Select(match => (source.File, Text: Regex.Replace(match.Value, @"\s+", " ").Trim())))
            .ToArray();

        CollectionAssert.AreEqual(
            new[] { "Program.cs", "Program.cs" },
            console.Select(static value => value.File).ToArray(),
            "A Console write appeared outside Program.cs, or one of the two startup writes moved.");
        Assert.IsTrue(console.All(static value => value.Text.StartsWith("Console.Error.WriteLine(", StringComparison.Ordinal)), string.Join(" | ", console.Select(static value => value.Text)));

        var program = sources.Single(static source => source.File == "Program.cs").Code;
        Assert.IsTrue(program.Contains("builder.Logging.ClearProviders()", StringComparison.Ordinal),
            "Program.cs no longer clears the logging providers, so the host's own request logging is back.");
    }

    [TestMethod]
    public void TheApiProcessCarriesExactlyTheseThirdPartyPackages()
    {
        var root = RepositoryRoot();
        var project = XDocument.Load(Path.Combine(root, "src", "Lex.V3.Api", "Lex.V3.Api.csproj"));
        CollectionAssert.AreEqual(
            new[] { "JsonSchema.Net", "Microsoft.Data.Sqlite" },
            project.Descendants("PackageReference").Select(static element => (string)element.Attribute("Include")!).OrderBy(static name => name, StringComparer.Ordinal).ToArray(),
            "A package was added to or removed from the public process. A logging, telemetry or diagnostics package would be a place to record a request.");

        using var lockFile = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "src", "Lex.V3.Api", "packages.lock.json")));
        var resolved = lockFile.RootElement.GetProperty("dependencies").EnumerateObject()
            .SelectMany(static target => target.Value.EnumerateObject())
            .Select(static package => package.Name)
            .Where(static name => !name.StartsWith("lex.v3.", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();
        CollectionAssert.AreEqual(
            new[]
            {
                "Humanizer.Core", "Json.More.Net", "JsonPointer.Net", "JsonSchema.Net", "Microsoft.Data.Sqlite",
                "Microsoft.Data.Sqlite.Core", "PdfPig", "SQLitePCLRaw.bundle_e_sqlite3", "SQLitePCLRaw.core",
                "SQLitePCLRaw.lib.e_sqlite3", "SQLitePCLRaw.provider.e_sqlite3",
            },
            resolved,
            "The resolved package set of the public process changed.");
    }

    [TestMethod]
    public void TheSourceCensusSeesEachKindOfPlaceItIsMeantToWatchAndNothingInACommentOrAMessage()
    {
        var harmless = Blank(""""
            // context.Request.Headers.UserAgent, Console.WriteLine, ILogger
            /* RemoteIpAddress and Trace.WriteLine */
            var message = "Console.WriteLine(RemoteIpAddress) and UseHttpLogging";
            var verbatim = @"ILogger ""quoted"" File.WriteAllText";
            context.Response.Headers.CacheControl = "no-store";
            var raw = """ Request.Query and AddConsole """;
            """");
        Assert.IsEmpty(Findings(harmless), string.Join("; ", Findings(harmless)));

        var planted = new (string Code, string Rule)[]
        {
            ("var a = context.Connection.RemoteIpAddress;", "the caller's address"),
            ("var a = context.Request.Headers.UserAgent;", "the caller's headers"),
            ("var a = request.Headers[\"x\"];", "the caller's headers"),
            ("var a = HeaderNames.Referer;", "the caller's headers"),
            ("builder.Services.AddHttpLogging();", "a logging or telemetry provider"),
            ("app.UseForwardedHeaders();", "forwarded-header handling"),
            ("private readonly ILogger<X> log;", "a logger"),
            ("var a = context.Request.QueryString;", "the path or query of the request"),
            ("Trace.WriteLine(x);", "a trace or debug write"),
            ("File.AppendAllText(p, x);", "a file or stream writer"),
            ("using var w = new StreamWriter(p);", "a file or stream writer"),
            ("Open(p, SqliteOpenMode.ReadWriteCreate);", "a database opened for writing"),
            ("var f = context.Features.Get<IHttpConnectionFeature>();", "a feature other than the request's raw target"),
            ("var f = context.Features.Get<IHttpRequestFeature>()?.Headers;", "a member of the request feature other than the raw target"),
        };
        foreach (var (code, rule) in planted)
        {
            CollectionAssert.Contains(Findings(Blank(code)).ToArray(), rule, code);
        }

        Assert.IsEmpty(Findings(Blank("var raw = context.Features.Get<IHttpRequestFeature>()?.RawTarget ?? string.Empty;")));
        Assert.IsEmpty(Findings(Blank("context.Response.Headers.Allow = HttpMethods.Get;")));
    }
}
