using Lex.V3.Contracts;

namespace Lex.V3.Api;

internal readonly record struct SyntheticResolveRequest(
    bool Accepted,
    string? Family,
    string? Coordinate)
{
    public static SyntheticResolveRequest Rejected { get; } = new(false, null, null);
}

internal static class SyntheticRawTarget
{
    public static bool IsWithinApplicationBoundary(string? rawTarget)
    {
        if (rawTarget is null ||
            rawTarget.Length > SyntheticResolveRequestContract.MaximumApplicationRawTargetByteCount)
        {
            return false;
        }

        foreach (var value in rawTarget)
        {
            if (value is < ' ' or > '~')
            {
                return false;
            }
        }

        return true;
    }

    public static SyntheticResolveRequest Parse(string? rawTarget) =>
        IsWithinApplicationBoundary(rawTarget) ? rawTarget switch
        {
            SyntheticResolveRequestContract.HeldRawTarget => new(true, "eli", "eli/synthetic-preview"),
            SyntheticResolveRequestContract.CandidateRawTarget => new(
                true,
                "historical_legal_id",
                "historical_legal_id:synthetic-preview"),
            _ => SyntheticResolveRequest.Rejected,
        }
        : SyntheticResolveRequest.Rejected;
}
