using Lex.Ask;
using Lex.Mcp;

namespace Lex.Web;

/// <summary>
/// What every endpoint module needs, passed once instead of captured twenty-five times.
///
/// The routes used to be lambdas in a single file closing over local variables, which is why
/// they could not be moved: each one silently depended on whatever happened to be in scope. Made
/// explicit, the dependency list turns out to be short, and short enough to read is the point.
/// </summary>
public sealed record WebContext(
    IndexRegistry Registry,
    LexOptions Options,
    McpCore Mcp,
    AskService Ask,
    AskRequestRegistry AskRequests,
    AskThreadRegistry AskThreads,
    TimeProvider Clock)
{
    /// <summary>Absolute base for permalinks and social metadata, without a trailing slash.</summary>
    public string PublicBase => Options.PublicBase;

    /// <summary>The one application clock used by every rendered current-date claim.</summary>
    public DateOnly Today => DateOnly.FromDateTime(Clock.GetUtcNow().UtcDateTime);
}
