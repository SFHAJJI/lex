using Microsoft.ClearScript;
using Microsoft.ClearScript.V8;

namespace Lex.V3.Api;

/// <summary>
/// Renders the live pages at a law's address from the API's own answer (the specification of record's R67: the dossier and
/// reading pages are complete HTML from the server, readable without JavaScript, then enhanced), with the pages' own server
/// renderer (<c>v3-web/ssr/{language}.js</c>, built from <c>web/app/ssr-entry.jsx</c>), in V8.
/// </summary>
/// <remarks>
/// <para>
/// The renderer is the React tree the browser hydrates, so the page the server sends and the page the browser keeps are one.
/// Each interface language has an engine of its own, holding the renderer and nothing of this process: no host object, no
/// file, no network. It is handed the answer and the path and returns a page, one render at a time.
/// </para>
/// <para>
/// A render is bounded in time, in heap and in stack. One that runs over its time, or whose heap passes the soft limit
/// ClearScript samples (V8's own hard limit would end the process), is stopped, and its engine is not used again: the next
/// render makes a fresh one. A render that waits too long for its engine is not attempted. A render that fails, is stopped,
/// waits too long or returns anything but a whole document is no page, and the page is then served as built, asking for
/// itself. Nothing about a request is logged.
/// </para>
/// </remarks>
internal sealed class V3PageRenderer : IDisposable
{
    /// <summary>A bare engine has no <c>TextEncoder</c>; React's server renderer makes one when it loads, and needs no other.</summary>
    private const string Prelude = """
        globalThis.TextEncoder = class TextEncoder {
          get encoding() { return 'utf-8'; }
          encode(text) {
            const bytes = unescape(encodeURIComponent(String(text ?? '')));
            const out = new Uint8Array(bytes.length);
            for (let i = 0; i < bytes.length; i++) out[i] = bytes.charCodeAt(i);
            return out;
          }
        };
        """;

    /// <summary>
    /// An engine's old space, in megabytes: V8's hard limit, at which it ends the process. The soft limit below stops a render
    /// before it, and should a render outrun the sampling, V8 doubles the limit (on-demand heap expansion) rather than end the
    /// process, until the next sample stops the render.
    /// </summary>
    private const int HardHeapMegabytes = 320;

    /// <summary>
    /// The heap a render may reach before it is stopped, which ClearScript samples. A state of 3,000 articles, as large as the
    /// largest codes, renders (a page of 13 MB) within 64 MB.
    /// </summary>
    private const ulong SoftHeapBytes = 224UL * 1024 * 1024;

    /// <summary>The heap above which an engine is collected after a render, so one large page's garbage is not the next one's.</summary>
    private const ulong CollectAboveBytes = 96UL * 1024 * 1024;

    /// <summary>The stack a render may use, which leaves the thread it runs on room to spare: recursion past it is a script error.</summary>
    private const ulong StackBytes = 512UL * 1024;

    private readonly IReadOnlyDictionary<string, string> _scripts;
    private readonly IReadOnlyDictionary<string, SemaphoreSlim> _gates;
    private readonly TimeSpan _budget;
    private readonly TimeSpan _wait;
    private readonly Func<V8ScriptEngine> _newEngine;
    private readonly Dictionary<string, Engine> _engines = new(StringComparer.Ordinal);
    private readonly HashSet<string> _unloadable = new(StringComparer.Ordinal);
    private readonly Lock _state = new();
    private bool _disposed;

    private V3PageRenderer(IReadOnlyDictionary<string, string> scripts, TimeSpan budget, TimeSpan wait, Func<V8ScriptEngine> newEngine)
    {
        _scripts = scripts;
        _gates = scripts.Keys.ToDictionary(language => language, _ => new SemaphoreSlim(1, 1), StringComparer.Ordinal);
        _budget = budget;
        _wait = wait;
        _newEngine = newEngine;
    }

    /// <summary>The interface languages a renderer was found for.</summary>
    internal IReadOnlyCollection<string> Languages => (IReadOnlyCollection<string>)_scripts.Keys;

    /// <summary>
    /// The renderers in <paramref name="directory"/> (<c>{language}.js</c> for each interface language), or null when it holds
    /// none. Each render may take <paramref name="budget"/> (five seconds when not given) before it is stopped, and wait
    /// <paramref name="wait"/> (two seconds when not given) for the render before it to end. <paramref name="newEngine"/>
    /// makes an engine (the bounded V8 engine when not given; a test hands one that fails).
    /// </summary>
    internal static V3PageRenderer? Open(string directory, TimeSpan? budget = null, TimeSpan? wait = null, Func<V8ScriptEngine>? newEngine = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var scripts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var language in V3WebRoot.InterfaceLanguages)
        {
            var file = Path.Combine(directory, $"{language}.js");
            if (File.Exists(file))
            {
                scripts[language] = File.ReadAllText(file);
            }
        }

        return scripts.Count == 0
            ? null
            : new V3PageRenderer(scripts, budget ?? TimeSpan.FromSeconds(5), wait ?? TimeSpan.FromSeconds(2), newEngine ?? NewEngine);
    }

    /// <summary>An engine bounded in heap and stack, holding nothing of this process.</summary>
    private static V8ScriptEngine NewEngine() =>
        new(
            new V8RuntimeConstraints { MaxOldSpaceSize = HardHeapMegabytes, MaxNewSpaceSize = 32, HeapExpansionMultiplier = 2 },
            V8ScriptEngineFlags.None)
        {
            MaxRuntimeHeapSize = (nuint)SoftHeapBytes,
            MaxRuntimeStackUsage = (nuint)StackBytes,
        };

    /// <summary>
    /// The page <paramref name="kind"/> (<c>reading</c>, <c>dossier</c>, <c>home</c>) at <paramref name="path"/> in
    /// <paramref name="language"/>, rendered from <paramref name="envelopeJson"/> (the API's answer to the request the page
    /// itself would send); null when there is no renderer for the language, or the render waits too long, fails, is stopped
    /// or returns anything but a whole document.
    /// </summary>
    internal async Task<string?> RenderAsync(string language, string kind, string envelopeJson, string path, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(language);
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(envelopeJson);
        ArgumentNullException.ThrowIfNull(path);
        if (!_gates.TryGetValue(language, out var gate) || !await gate.WaitAsync(_wait, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        try
        {
            return RenderAlone(language, kind, envelopeJson, path);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Whatever else fails in a render, the page is served as built and asks for itself (review of #959).
            return null;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>A render, its language's gate held: no other render uses the engine meanwhile.</summary>
    private string? RenderAlone(string language, string kind, string envelopeJson, string path)
    {
        if (EngineFor(language) is not { } engine)
        {
            return null;
        }

        object? result = null;
        var stopped = false;
        using (var deadline = new CancellationTokenSource(_budget))
        {
            var interrupt = deadline.Token.Register(static runtime => ((V8ScriptEngine)runtime!).Interrupt(), engine.Runtime);
            try
            {
                result = engine.Renderer.InvokeMethod("render", kind, envelopeJson, path);
            }
            catch (ScriptInterruptedException)
            {
                // Its time, or its heap's soft limit.
                stopped = true;
            }
            catch (Exception exception) when (exception is ScriptEngineException or InvalidOperationException)
            {
                // The renderer threw (the engine stays sound unless the error is fatal), or the renderer was disposed.
                stopped = exception is ScriptEngineException { IsFatal: true };
            }
            finally
            {
                // Waits for an interrupt under way, so that none reaches a later render.
                interrupt.Dispose();
            }

            // An engine interrupted after its render ended may stop the next: it is not used again either.
            stopped |= deadline.IsCancellationRequested;
        }

        if (stopped)
        {
            Retire(language, engine);
            return null;
        }

        try
        {
            if (engine.Runtime.GetRuntimeHeapInfo().UsedHeapSize > CollectAboveBytes)
            {
                engine.Runtime.CollectGarbage(true);
            }
        }
        catch (InvalidOperationException)
        {
            // Disposed meanwhile: the page stands.
        }

        return result is string html && html.StartsWith("<!doctype html>", StringComparison.Ordinal) ? html : null;
    }

    /// <summary>
    /// The language's engine, made on its first render and after one was retired; null when V8 cannot be made here or the
    /// renderer does not load in its time.
    /// </summary>
    private Engine? EngineFor(string language)
    {
        lock (_state)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_engines.TryGetValue(language, out var made))
            {
                return made;
            }

            if (_unloadable.Contains(language))
            {
                return null;
            }
        }

        // Made with the language's gate held, outside the lock: no second render of the language makes one meanwhile, and
        // another language's render does not wait.
        V8ScriptEngine? runtime = null;
        try
        {
            runtime = _newEngine();
            bool interrupted;
            using (var deadline = new CancellationTokenSource(_budget))
            {
                // The renderer loads within a render's time, so a load that never ends holds no gate for ever.
                var interrupt = deadline.Token.Register(static engine => ((V8ScriptEngine)engine!).Interrupt(), runtime);
                try
                {
                    runtime.Execute(Prelude);
                    runtime.Execute(_scripts[language]);
                }
                finally
                {
                    interrupt.Dispose();
                }

                interrupted = deadline.IsCancellationRequested;
            }

            if (!interrupted && runtime.Global.GetProperty("lexSsr") is ScriptObject renderer)
            {
                lock (_state)
                {
                    if (!_disposed)
                    {
                        var engine = new Engine(runtime, renderer);
                        _engines[language] = engine;
                        return engine;
                    }
                }

                runtime.Dispose();
                return null;
            }
        }
        catch (Exception exception) when (exception is ScriptInterruptedException || exception is not OperationCanceledException)
        {
            // The renderer does not load, or not in its time, or V8 itself cannot be made (its native library is not there for
            // this platform; review of #959: every law page answered 500). None of it changes while the process runs.
        }

        runtime?.Dispose();
        lock (_state)
        {
            _unloadable.Add(language);
        }

        return null;
    }

    /// <summary>Puts an engine out of use: the language's next render makes a fresh one.</summary>
    private void Retire(string language, Engine engine)
    {
        lock (_state)
        {
            if (_engines.TryGetValue(language, out var current) && ReferenceEquals(current, engine))
            {
                _engines.Remove(language);
            }
        }

        engine.Runtime.Dispose();
    }

    public void Dispose()
    {
        lock (_state)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (var engine in _engines.Values)
            {
                engine.Runtime.Dispose();
            }

            _engines.Clear();
        }
    }

    private sealed record Engine(V8ScriptEngine Runtime, ScriptObject Renderer);
}
