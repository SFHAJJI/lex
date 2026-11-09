using System.Text;
using Lex.V3.Api;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests;

/// <summary>
/// The assistant's page and script are served only while the server reports the assistant switched on (R47, R52): while it
/// is off, <c>/{language}/assistant</c> and its script are paths the web root does not hold, answered as any unknown page.
/// </summary>
[TestClass]
public sealed class V3WebRootAssistantTests
{
    private const string PageCsp = "default-src &#x27;none&#x27;; script-src &#x27;self&#x27;; connect-src &#x27;self&#x27;";

    private static string NewWebDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"lex-v3-web-assistant-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(directory, "en"));
        Directory.CreateDirectory(Path.Combine(directory, "fr"));
        File.WriteAllText(Path.Combine(directory, "en", "index.html"),
            $"<!doctype html><html><head><meta http-equiv=\"Content-Security-Policy\" content=\"{PageCsp}\"></head><body>index</body></html>");
        File.WriteAllText(Path.Combine(directory, "fr", "index.html"), "<!doctype html><p>accueil</p>");
        foreach (var language in new[] { "en", "fr" })
        {
            File.WriteAllText(Path.Combine(directory, language, V3WebRoot.AssistantScriptName), "void 0;");
        }

        return directory;
    }

    private static DefaultHttpContext Request(string rawTarget)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Features.Get<IHttpRequestFeature>()!.RawTarget = rawTarget;
        context.Response.Body = new MemoryStream();
        return context;
    }

    [TestMethod]
    public async Task WhileTheAssistantIsOffItsPageAndScriptAreNoPathsOfTheSite()
    {
        var directory = NewWebDirectory();
        try
        {
            var root = V3WebRoot.Open(directory)!;
            foreach (var target in new[] { "/fr/assistant", "/en/assistant", "/fr/" + V3WebRoot.AssistantScriptName, "/en/" + V3WebRoot.AssistantScriptName })
            {
                foreach (var off in new Func<bool>?[] { null, () => false })
                {
                    var context = Request(target);
                    Assert.IsFalse(await root.TryServeAsync(context, target, CancellationToken.None, assistantOn: off), $"{target} is no path while the assistant is off");
                    Assert.AreEqual(0, ((MemoryStream)context.Response.Body).Length, $"{target}: nothing is written");
                }
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task WhileItIsOnItsScriptIsServedAndItsPageIsRenderedOrIsNoPath()
    {
        var directory = NewWebDirectory();
        try
        {
            var root = V3WebRoot.Open(directory)!;
            var script = Request("/fr/" + V3WebRoot.AssistantScriptName);
            Assert.IsTrue(await root.TryServeAsync(script, "/fr/" + V3WebRoot.AssistantScriptName, CancellationToken.None, assistantOn: () => true));
            Assert.AreEqual(StatusCodes.Status200OK, script.Response.StatusCode);
            Assert.AreEqual("void 0;", Encoding.UTF8.GetString(((MemoryStream)script.Response.Body).ToArray()));

            // With no server renderer (this directory ships none), the page cannot be rendered, and is no path either.
            var page = Request("/fr/assistant");
            Assert.IsFalse(await root.TryServeAsync(page, "/fr/assistant", CancellationToken.None, answer: (_, _) => [], assistantOn: () => true));
            Assert.AreEqual("fr", V3WebRoot.AssistantLanguage("/fr/assistant"));
            Assert.IsNull(V3WebRoot.AssistantLanguage("/fr/assistant.html"), "only the page's one address");
            Assert.IsNull(V3WebRoot.AssistantLanguage("/de/assistant"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
