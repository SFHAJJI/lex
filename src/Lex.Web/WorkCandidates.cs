namespace Lex.Web;

/// <summary>
/// Publisher entry points, kept in one place so no page invents a URL for a publisher.
///
/// This is a PARTIAL copy of the canonical WorkCandidates from the B2 lane branch. That file
/// also carries Nearest and NoticeHtml, which depend on WorkCandidateFinder and belong to the
/// nearest-work slice that has not reached this branch. Only the member MatchLanes needs is
/// here, so MatchLanes itself stays byte-identical to the agreed implementation rather than
/// being forked to work around a missing dependency. When the two branches meet, the fuller
/// canonical file replaces this one.
/// </summary>
public static class WorkCandidates
{
    /// <summary>
    /// The official publisher search entry per collection, exact hosts only. Unknown
    /// collections fall back to the internal search page rather than guessing a URL.
    /// </summary>
    public static string OfficialSearchHref(string collection) => collection switch
    {
        "lu-legilux" => "https://legilux.public.lu",
        "eu-eurlex" => "https://eur-lex.europa.eu",
        _ => "/search",
    };
}
