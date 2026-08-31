using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;

namespace Lex.V3.Contracts;

public sealed record EuSeedResolutionRow
{
    internal EuSeedResolutionRow(string requestedCelex, string datatypeIri, bool isControl)
    {
        ArgumentNullException.ThrowIfNull(requestedCelex);
        if (!string.Equals(datatypeIri, EuSeedResolutionPlan.XsdStringDatatypeIri, StringComparison.Ordinal))
        {
            throw new ArgumentException("EU seed resolution rows must use the exact xsd:string datatype IRI.", nameof(datatypeIri));
        }

        RequestedCelex = requestedCelex;
        DatatypeIri = datatypeIri;
        IsControl = isControl;
    }

    public string RequestedCelex { get; }

    public string DatatypeIri { get; }

    public bool IsControl { get; }
}

public sealed record EuSeedResolutionBatch
{
    internal EuSeedResolutionBatch(
        int ordinal,
        IReadOnlyCollection<EuSeedResolutionRow> rows,
        int expectedControlCardinality)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var copy = rows.ToArray();
        if (ordinal < 1 || copy.Length > 50 || copy.Length == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ordinal));
        }

        if (copy.Any(static row => row is null) ||
            !copy[0].IsControl ||
            copy.Count(static row => row.IsControl) != 1 ||
            expectedControlCardinality != 1)
        {
            throw new ArgumentException("Each resolution batch must carry its one expected positive control.", nameof(rows));
        }

        Ordinal = ordinal;
        Rows = Array.AsReadOnly(copy);
        ExpectedControlCardinality = expectedControlCardinality;
    }

    public int Ordinal { get; }

    public IReadOnlyList<EuSeedResolutionRow> Rows { get; }

    public int DataRowCount => Rows.Count - 1;

    public int ExpectedControlCardinality { get; }
}

public sealed record EuPlainLiteralDriftProbePlan
{
    internal EuPlainLiteralDriftProbePlan(
        string queryFormLabel,
        string requestedCelex,
        int baselineHttpStatus,
        long baselineRowCount,
        DateOnly baselineDate)
    {
        QueryFormLabel = queryFormLabel;
        RequestedCelex = requestedCelex;
        BaselineHttpStatus = baselineHttpStatus;
        BaselineRowCount = baselineRowCount;
        BaselineDate = baselineDate;
    }

    public string QueryFormLabel { get; }

    public string RequestedCelex { get; }

    public int BaselineHttpStatus { get; }

    public long BaselineRowCount { get; }

    public DateOnly BaselineDate { get; }
}

public static class EuSeedResolutionPlan
{
    public const string SeedListSha256 =
        "ea1b4f276406a8bede5223459b92d7a94321de5b9a38de63397f2e22688d50c0";
    public static string XsdStringDatatypeIri { get; } = "http://www.w3.org/2001/XMLSchema#string";
    public static string PositiveControlCelex { get; } = "32000L0031";

    private const int FirstBatchDataCount = 49;

    static EuSeedResolutionPlan()
    {
        var seeds = new[]
        {
            "12012E/TXT",
            "12012M/TXT",
            "12012P/TXT",
            "12016E/TXT",
            "12016M/TXT",
            "12016P/TXT",
            "32003L0087",
            "32003L0088",
            "32003R0001",
            "32004L0048",
            "32004R0139",
            "32005L0029",
            "32006L0054",
            "32006L0112",
            "32006L0116",
            "32007L0036",
            "32007R0864",
            "32008R0593",
            "32011L0016",
            "32011L0083",
            "32012R1215",
            "32012R1257",
            "32013L0036",
            "32013R0575",
            "32014L0023",
            "32014L0024",
            "32014L0025",
            "32014L0041",
            "32014L0065",
            "32014L0067",
            "32014R0596",
            "32014R0600",
            "32014R0651",
            "32014R0910",
            "32015L0849",
            "32015L2366",
            "32015R0848",
            "32016L0680",
            "32016L1164",
            "32016R0679",
            "32016R1011",
            "32017L1132",
            "32017R1001",
            "32017R1129",
            "32018L0843",
            "32018L0957",
            "32018L2001",
            "32018R1999",
            "32019L0001",
            "32019L0770",
            "32019L0771",
            "32019L0790",
            "32019L0944",
            "32019L1151",
            "32019L1152",
            "32019L2121",
            "32019R1111",
            "32019R2088",
            "32020R0852",
            "32020R1783",
            "32020R1784",
            "32021R1119",
            "32022L0542",
            "32022L2041",
            "32022L2523",
            "32022L2555",
            "32022R1925",
            "32022R2065",
            "32022R2554",
            "32023L0970",
            "32023R1114",
            "32023R1115",
            "32023R1543",
            "32023R2831",
            "32023R2854",
            "32024L1640",
            "32024L1760",
            "32024R1620",
            "32024R1624",
            "32024R1689",
            "32024R1781",
            "32024R2847",
        };

        ValidateSeeds(seeds);
        Seeds = Array.AsReadOnly(seeds);
        Batches = Array.AsReadOnly(new[]
        {
            CreateBatch(1, seeds.Take(FirstBatchDataCount)),
            CreateBatch(2, seeds.Skip(FirstBatchDataCount)),
        });
        PlainLiteralDriftProbe = new EuPlainLiteralDriftProbePlan(
            "plain_literal",
            "32016R0679",
            200,
            0,
            new DateOnly(2026, 8, 31));
    }

    public static IReadOnlyList<string> Seeds { get; }

    public static IReadOnlyList<EuSeedResolutionBatch> Batches { get; }

    public static EuPlainLiteralDriftProbePlan PlainLiteralDriftProbe { get; }

    private static EuSeedResolutionBatch CreateBatch(int ordinal, IEnumerable<string> seeds)
    {
        var rows = new List<EuSeedResolutionRow>
        {
            new(PositiveControlCelex, XsdStringDatatypeIri, isControl: true),
        };
        rows.AddRange(seeds.Select(static seed =>
            new EuSeedResolutionRow(seed, XsdStringDatatypeIri, isControl: false)));
        return new EuSeedResolutionBatch(ordinal, rows, expectedControlCardinality: 1);
    }

    private static void ValidateSeeds(IReadOnlyList<string> seeds)
    {
        if (seeds.Count != 82 ||
            seeds.Distinct(StringComparer.Ordinal).Count() != seeds.Count ||
            !seeds.SequenceEqual(seeds.OrderBy(static seed => seed, StringComparer.Ordinal)) ||
            seeds.Contains(PositiveControlCelex, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("The accepted EU seed inventory is not exact.");
        }

        var bytes = Encoding.UTF8.GetBytes(string.Join('\n', seeds) + "\n");
        var digest = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (bytes.Length != 902 || !string.Equals(digest, SeedListSha256, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The accepted EU seed inventory digest does not match.");
        }
    }
}
