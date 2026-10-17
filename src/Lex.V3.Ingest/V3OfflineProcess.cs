using System.Diagnostics.Tracing;
using System.Globalization;

namespace Lex.V3.Ingest;

/// <summary>
/// The process-wide guard an offline command enters before any work: once entered, no publisher session can be built in
/// this process. <see cref="RoutedHttpAcquisitionSession"/>'s one constructor, through which every session is built on
/// every transport (the pinned production handler and the test door alike), refuses first.
/// </summary>
/// <remarks>
/// The guard is an <see cref="AppContext"/> switch, so it is set once for the whole process and read where the session is
/// built. It closes the door the code base itself owns; it cannot stop code that builds its own HTTP client, which is what
/// <see cref="V3NetworkActivityListener"/> measures. An offline command does both: it enters the guard, measures, and
/// refuses to finish when anything was measured.
/// </remarks>
public static class V3OfflineProcess
{
    /// <summary>The <see cref="AppContext"/> switch the guard is.</summary>
    public const string SwitchName = "Lex.V3.Ingest.OfflineProcess";

    /// <summary>Enters the guard for the rest of this process's life.</summary>
    public static void Enter() => AppContext.SetSwitch(SwitchName, true);

    /// <summary>Whether this process has entered the guard.</summary>
    public static bool IsEntered => AppContext.TryGetSwitch(SwitchName, out var entered) && entered;

    /// <summary>Refuses, when the guard is entered, the building of a publisher session.</summary>
    internal static void RefuseSession()
    {
        if (IsEntered)
        {
            throw new InvalidOperationException(
                $"This process is offline ({SwitchName}): it builds no publisher session.");
        }
    }
}

/// <summary>
/// Counts every network operation this process starts while it lives: a socket connect (<c>System.Net.Sockets</c>
/// <c>ConnectStart</c>), a name resolution (<c>System.Net.NameResolution</c> <c>ResolutionStart</c>) and an HTTP request
/// (<c>System.Net.Http</c> <c>RequestStart</c>), whatever handler, proxy setting or client starts it.
/// </summary>
/// <remarks>
/// <para>
/// A proxy trap sees only clients that use the default proxy, and the code base's own session builds its handler with
/// <c>UseProxy = false</c>, so a trap cannot see the production transport; this listener reads the runtime's own
/// networking events, which every client raises. It counts the whole process, so a measurement means something only where
/// nothing else in the process does network work: the offline tool's own process, or a test that runs alone.
/// </para>
/// <para>
/// The runtime raises these events synchronously on the thread that starts the operation, so a count read after the
/// measured work has completed holds every operation that work started.
/// </para>
/// </remarks>
public sealed class V3NetworkActivityListener : EventListener
{
    private long _socketConnects;
    private long _nameResolutions;
    private long _httpRequests;

    /// <summary>Socket connects started (<c>System.Net.Sockets</c> <c>ConnectStart</c>).</summary>
    public long SocketConnects => Interlocked.Read(ref _socketConnects);

    /// <summary>Name resolutions started (<c>System.Net.NameResolution</c> <c>ResolutionStart</c>).</summary>
    public long NameResolutions => Interlocked.Read(ref _nameResolutions);

    /// <summary>HTTP requests started (<c>System.Net.Http</c> <c>RequestStart</c>).</summary>
    public long HttpRequests => Interlocked.Read(ref _httpRequests);

    /// <summary>Every network operation counted: what an offline process must keep at zero.</summary>
    public long Total => SocketConnects + NameResolutions + HttpRequests;

    /// <summary>The three counts, in one line.</summary>
    public string Describe() => string.Create(CultureInfo.InvariantCulture,
        $"socket connects {SocketConnects}, name resolutions {NameResolutions}, HTTP requests {HttpRequests}");

    protected override void OnEventSourceCreated(EventSource eventSource)
    {
        // Called for every source that exists when the listener is built and for each one created later; this runs
        // during the base constructor for the first ones, so it reads no field.
        if (eventSource.Name is "System.Net.Sockets" or "System.Net.NameResolution" or "System.Net.Http")
        {
            EnableEvents(eventSource, EventLevel.Verbose, EventKeywords.All);
        }
    }

    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        switch (eventData.EventSource.Name, eventData.EventName)
        {
            case ("System.Net.Sockets", "ConnectStart"):
                Interlocked.Increment(ref _socketConnects);
                break;
            case ("System.Net.NameResolution", "ResolutionStart"):
                Interlocked.Increment(ref _nameResolutions);
                break;
            case ("System.Net.Http", "RequestStart"):
                Interlocked.Increment(ref _httpRequests);
                break;
        }
    }
}
