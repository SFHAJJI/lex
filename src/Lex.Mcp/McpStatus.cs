namespace Lex.Mcp;

/// <summary>
/// Closed set of legal result statuses emitted by the public MCP contract.
/// Transport failures are represented separately by the host and never added here.
/// </summary>
public static class McpStatus
{
    public const string Ok = "ok";
    public const string NoResult = "no_result";
    public const string NoChangesInPeriod = "no_changes_in_period";
    public const string ProfilesDiffer = "profiles_differ";
    public const string UnknownWork = "unknown_work";
    public const string UnknownAnchor = "unknown_anchor";
    public const string NoVersionForDate = "no_version_for_date";
    public const string AnchorNotInVersion = "anchor_not_in_version";
    public const string NoProvisionHistory = "no_provision_history";
    public const string TextNotAvailable = "text_not_available";
    public const string TextWithheld = "text_withheld";
    public const string NoCorpusMounted = "no_corpus_mounted";

    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[]
    {
        Ok,
        NoResult,
        NoChangesInPeriod,
        ProfilesDiffer,
        UnknownWork,
        UnknownAnchor,
        NoVersionForDate,
        AnchorNotInVersion,
        NoProvisionHistory,
        TextNotAvailable,
        TextWithheld,
        NoCorpusMounted,
    });
}
