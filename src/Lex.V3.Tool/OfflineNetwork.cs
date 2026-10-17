using Lex.V3.Ingest;

/// <summary>
/// What the offline commands (<c>derive</c>, <c>rederive-inputs</c>, <c>rederive-seal</c>) hold about the network: they enter
/// the process-wide guard (<see cref="V3OfflineProcess"/>) before any work, so no publisher session can be built in their
/// process, and they measure every network operation the process starts (<see cref="V3NetworkActivityListener"/>). The
/// count they print as <c>publisher_requests</c> is that measurement, and a command that measured any refuses to finish
/// (exit 5): an offline process starts none, so every operation counts against it.
/// </summary>
internal static class OfflineNetwork
{
    /// <summary>The exit code of an offline command that measured network activity.</summary>
    internal const int NetworkActivityMeasured = 5;

    /// <summary>Enters the guard and starts measuring. Call before any work.</summary>
    internal static V3NetworkActivityListener Begin()
    {
        V3OfflineProcess.Enter();
        return new V3NetworkActivityListener();
    }

    /// <summary>The guard's state and the measurement, as the command prints them.</summary>
    internal static string Line(V3NetworkActivityListener network) =>
        $"offline_guard={(V3OfflineProcess.IsEntered ? "on" : "off")}; network measured: {network.Describe()}; "
        + $"publisher_requests={network.Total}";

    /// <summary>
    /// Null when nothing was measured; otherwise prints the refusal and returns <see cref="NetworkActivityMeasured"/>.
    /// </summary>
    internal static int? Refusal(V3NetworkActivityListener network, string command)
    {
        if (network.Total == 0 && V3OfflineProcess.IsEntered)
        {
            return null;
        }

        Console.Error.WriteLine($"{command} refused: an offline process measured network activity ({Line(network)}); "
            + "nothing it wrote is to be used");
        return NetworkActivityMeasured;
    }
}
