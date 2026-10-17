using System.Diagnostics;
using Lex.V3.Api;

namespace Lex.V3.Tests;

/// <summary>
/// The pages' server renderer run by the API (the specification of record's R67): a page is what the renderer returns
/// when it returns a whole document, in time and within its heap and stack; anything else is no page, and the page is then
/// served as built. A render stopped for its time or its heap leaves the process standing and its language a fresh engine.
/// </summary>
[TestClass]
public sealed class V3PageRendererTests
{
    private static string NewRendererDirectory(params (string Language, string Script)[] scripts)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"lex-v3-ssr-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        foreach (var (language, script) in scripts)
        {
            File.WriteAllText(Path.Combine(directory, $"{language}.js"), script);
        }

        return directory;
    }

    private const string Echo = """
        globalThis.lexSsr = { render(kind, envelope, path) {
          const value = JSON.parse(envelope);
          return '<!doctype html><p>' + kind + ' ' + value.word + ' ' + path + ' ' + new TextEncoder().encode('é').length + '</p>';
        } };
        """;

    /// <summary>
    /// A renderer that misbehaves as its path asks: <c>/loop</c> never ends, <c>/heap</c> fills its heap, <c>/deep</c>
    /// recurses without end, <c>/slow</c> works for a second; any other path is a page naming how many renders its engine
    /// has made, so a fresh engine says 1.
    /// </summary>
    private const string Misbehaving = """
        let renders = 0;
        globalThis.lexSsr = { render(kind, envelope, path) {
          renders += 1;
          if (path === '/en/loop') for (;;) {}
          if (path === '/en/heap') { const kept = []; for (;;) kept.push(new Array(1e5).fill(renders)); }
          if (path === '/en/deep') { const down = (n) => down(n + 1) + 1; return '<!doctype html>' + down(0); }
          if (path === '/en/slow') { const until = Date.now() + 1000; while (Date.now() < until) {} }
          return '<!doctype html><p>' + path + ' render ' + renders + '</p>';
        } };
        """;

    [TestMethod]
    public async Task AWholeDocumentTheRendererReturnsIsThePageAndNothingElseIs()
    {
        var directory = NewRendererDirectory(("en", Echo), ("fr", "globalThis.lexSsr = { render() { return 'a fragment, not a document'; } };"));
        try
        {
            using var renderer = V3PageRenderer.Open(directory)!;
            CollectionAssert.AreEquivalent(new[] { "en", "fr" }, renderer.Languages.ToArray());
            Assert.AreEqual("<!doctype html><p>reading answer /en/x 2</p>", await renderer.RenderAsync("en", "reading", "{\"word\":\"answer\"}", "/en/x", CancellationToken.None),
                "the renderer's page, its TextEncoder at hand");
            Assert.AreEqual("<!doctype html><p>dossier again /en/y 2</p>", await renderer.RenderAsync("en", "dossier", "{\"word\":\"again\"}", "/en/y", CancellationToken.None), "the engine is kept");
            Assert.IsNull(await renderer.RenderAsync("fr", "reading", "{}", "/fr/x", CancellationToken.None), "a fragment is no page");
            Assert.IsNull(await renderer.RenderAsync("de", "reading", "{}", "/de/x", CancellationToken.None), "no renderer for a language not reviewed");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task ARendererThatThrowsRunsOverOrDoesNotLoadIsNoPage()
    {
        var directory = NewRendererDirectory(
            ("en", "globalThis.lexSsr = { render() { throw new Error('broken'); } };"),
            ("fr", "globalThis.lexSsr = { render() { for (;;) {} } };"));
        var unloadable = NewRendererDirectory(("en", "this is not javascript ((("));
        try
        {
            using var renderer = V3PageRenderer.Open(directory, TimeSpan.FromMilliseconds(300))!;
            Assert.IsNull(await renderer.RenderAsync("en", "reading", "{}", "/en/x", CancellationToken.None), "a render that throws is no page");
            var clock = Stopwatch.StartNew();
            Assert.IsNull(await renderer.RenderAsync("fr", "reading", "{}", "/fr/x", CancellationToken.None), "a render that never ends is stopped and is no page");
            Assert.IsLessThan(10_000L, clock.ElapsedMilliseconds, "stopped at its budget, not left running");
            Assert.IsNull(await renderer.RenderAsync("en", "reading", "{}", "/en/x", CancellationToken.None), "and the engine still answers, with no page");

            using var broken = V3PageRenderer.Open(unloadable)!;
            Assert.IsNull(await broken.RenderAsync("en", "reading", "{}", "/en/x", CancellationToken.None), "a renderer that does not load is no page");
            Assert.IsNull(await broken.RenderAsync("en", "reading", "{}", "/en/x", CancellationToken.None), "nor on a second asking");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
            Directory.Delete(unloadable, recursive: true);
        }
    }

    [TestMethod]
    public async Task ARenderStoppedForItsTimeOrItsHeapLeavesTheProcessStandingAndItsLanguageAFreshEngine()
    {
        var directory = NewRendererDirectory(("en", Misbehaving));
        try
        {
            using var renderer = V3PageRenderer.Open(directory, TimeSpan.FromSeconds(3))!;
            Assert.AreEqual("<!doctype html><p>/en/a render 1</p>", await renderer.RenderAsync("en", "reading", "{}", "/en/a", CancellationToken.None));
            Assert.AreEqual("<!doctype html><p>/en/b render 2</p>", await renderer.RenderAsync("en", "reading", "{}", "/en/b", CancellationToken.None), "one engine, kept");

            Assert.IsNull(await renderer.RenderAsync("en", "reading", "{}", "/en/loop", CancellationToken.None), "a render that never ends is stopped");
            Assert.AreEqual("<!doctype html><p>/en/c render 1</p>", await renderer.RenderAsync("en", "reading", "{}", "/en/c", CancellationToken.None),
                "and the next render has a fresh engine, which no late interrupt reaches");

            // The heap is held apart from the time by a renderer with a long time: stopped well before that time, the render
            // was stopped by its heap. A 3-second time made the test a race with the runner's speed: filling the heap took
            // 3.5 seconds on a loaded runner (2026-10-17).
            using var roomy = V3PageRenderer.Open(directory, TimeSpan.FromSeconds(60))!;
            var clock = Stopwatch.StartNew();
            Assert.IsNull(await roomy.RenderAsync("en", "reading", "{}", "/en/heap", CancellationToken.None), "a render that fills its heap is stopped at the soft limit");
            Assert.IsLessThan(45_000L, clock.ElapsedMilliseconds, "by its heap, before its time");
            Assert.AreEqual("<!doctype html><p>/en/d render 1</p>", await roomy.RenderAsync("en", "reading", "{}", "/en/d", CancellationToken.None),
                "the process stands, and the next render has a fresh engine");

            Assert.IsNull(await roomy.RenderAsync("en", "reading", "{}", "/en/deep", CancellationToken.None), "recursion without end is a script error, not an overflow");
            Assert.AreEqual("<!doctype html><p>/en/e render 3</p>", await roomy.RenderAsync("en", "reading", "{}", "/en/e", CancellationToken.None),
                "after which the engine is sound and kept");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task ARenderThatWaitsTooLongForItsEngineIsNoPage()
    {
        var directory = NewRendererDirectory(("en", Misbehaving));
        try
        {
            using var renderer = V3PageRenderer.Open(directory, TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(100))!;

            // The engine is made first, so no round holds the engine while V8 loads (up to a quarter of a second for the first
            // engine in a process: the review of #962).
            StringAssert.StartsWith(await renderer.RenderAsync("en", "reading", "{}", "/en/warm", CancellationToken.None), "<!doctype html><p>/en/warm render");

            // The slow render holds the engine for a second, and a render that arrives meanwhile waits its 100 ms and gives
            // up. On a loaded machine either may take the engine first: the one that arrives second gives up, whichever it
            // is, and a round in which neither waited (the slow one done before the next arrived) is tried again, up to five
            // slow renders, so the test holds the behaviour and never the scheduler's timing (it failed once under a full
            // parallel run).
            var gaveUp = false;
            for (var round = 0; round < 5 && !gaveUp; round++)
            {
                var slow = Task.Run(() => renderer.RenderAsync("en", "reading", "{}", "/en/slow", CancellationToken.None));
                while (!slow.IsCompleted)
                {
                    if (await renderer.RenderAsync("en", "reading", "{}", "/en/behind", CancellationToken.None) is null)
                    {
                        gaveUp = true;
                        break;
                    }
                }

                if (await slow is { } page)
                {
                    StringAssert.StartsWith(page, "<!doctype html><p>/en/slow render", "a slow render that had the engine ends as a page");
                }
                else
                {
                    gaveUp = true;
                }
            }

            Assert.IsTrue(gaveUp, "a render that arrives while a slow one holds the engine gives up after its wait instead of queueing");
            StringAssert.StartsWith(await renderer.RenderAsync("en", "reading", "{}", "/en/after", CancellationToken.None), "<!doctype html><p>/en/after render",
                "and the engine is free again");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task AnEngineThatCannotBeMadeIsNoPageAndIsNotTriedAgain()
    {
        // As on a platform whose V8 native library is not there: ClearScript throws when the engine is made (review of #959).
        var directory = NewRendererDirectory(("en", Echo), ("fr", "globalThis.lexSsr = { render() { return '<!doctype html><p>fr</p>'; } };"));
        try
        {
            var made = 0;
            using var renderer = V3PageRenderer.Open(directory, newEngine: () =>
            {
                made++;
                throw new TypeLoadException("Cannot load ClearScript V8 library.");
            })!;
            Assert.IsNull(await renderer.RenderAsync("en", "reading", "{\"word\":\"a\"}", "/en/x", CancellationToken.None), "no V8: no page, never a failure");
            Assert.IsNull(await renderer.RenderAsync("en", "reading", "{\"word\":\"a\"}", "/en/y", CancellationToken.None), "nor on a second asking");
            Assert.AreEqual(1, made, "and the language is not tried again");
            Assert.IsNull(await renderer.RenderAsync("fr", "reading", "{}", "/fr/x", CancellationToken.None));
            Assert.AreEqual(2, made, "each language tries once");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task ARendererThatNeverFinishesLoadingIsStoppedAndNotTriedAgain()
    {
        var directory = NewRendererDirectory(("en", "for (;;) {}"));
        try
        {
            using var renderer = V3PageRenderer.Open(directory, TimeSpan.FromMilliseconds(300))!;
            var clock = Stopwatch.StartNew();
            Assert.IsNull(await renderer.RenderAsync("en", "reading", "{}", "/en/x", CancellationToken.None), "a load that never ends is stopped");
            Assert.IsLessThan(10_000L, clock.ElapsedMilliseconds, "at a render's time");
            clock.Restart();
            Assert.IsNull(await renderer.RenderAsync("en", "reading", "{}", "/en/x", CancellationToken.None));
            Assert.IsLessThan(250L, clock.ElapsedMilliseconds, "and not loaded again");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void ADirectoryWithNoRendererIsNoRenderer()
    {
        var directory = NewRendererDirectory();
        try
        {
            Assert.IsNull(V3PageRenderer.Open(directory));
            Assert.IsNull(V3PageRenderer.Open(Path.Combine(directory, "absent")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
