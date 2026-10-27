using System.Security.Cryptography;
using Lex.V3.Artifacts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// Run 15's publisher PDFs laid out on this host, compared byte for byte with the layout artifacts custody holds. Never a
/// CI test.
/// </summary>
/// <remarks>
/// OPT-IN AND READ-ONLY. It runs only with <c>LEX_V3_PDF_LAYOUT_PROBE=1</c>; <c>LEX_V3_PDF_LAYOUT_PROBE_CUSTODY</c> names
/// a custody root (its brotli store, read in place by digest and never written) and <c>LEX_V3_PDF_LAYOUT_PROBE_CASES</c> a
/// file of lines <c>pdf-sha256 source-identity-sha256 expected-layout-sha256</c>. Each retained PDF is laid out by the
/// producer's own <see cref="LuxembourgPdfLayoutEvidenceProducer.ReadLayout(byte[], string, long, CancellationToken)"/> for
/// its eligibility member, and the bytes must be the expected artifact's, which custody must hold. The PDFs are publisher
/// bytes: they stay in custody and nothing of them is committed. Nothing goes over the network.
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class LuxembourgPdfLayoutHostFontProbe
{
    private const string EnableVariable = "LEX_V3_PDF_LAYOUT_PROBE";
    private const string CustodyVariable = "LEX_V3_PDF_LAYOUT_PROBE_CUSTODY";
    private const string CasesVariable = "LEX_V3_PDF_LAYOUT_PROBE_CASES";

    [TestMethod]
    public async Task EachRetainedPdfIsLaidOutToTheArtifactCustodyHolds()
    {
        if (Environment.GetEnvironmentVariable(EnableVariable) != "1")
        {
            Assert.Inconclusive("Set LEX_V3_PDF_LAYOUT_PROBE=1, LEX_V3_PDF_LAYOUT_PROBE_CUSTODY and "
                + "LEX_V3_PDF_LAYOUT_PROBE_CASES for the read-only layout comparison.");
        }

        var root = Environment.GetEnvironmentVariable(CustodyVariable);
        var casesPath = Environment.GetEnvironmentVariable(CasesVariable);
        Assert.IsTrue(Directory.Exists(root), CustodyVariable + " names a custody root.");
        Assert.IsTrue(File.Exists(casesPath), CasesVariable + " names the cases.");
        var store = FileSystemCustodyStore.WithBrotliCompression(root!);
        var cases = File.ReadAllLines(casesPath!)
            .Where(static line => line.Length > 0 && !line.StartsWith('#'))
            .Select(static line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .ToArray();
        Assert.IsNotEmpty(cases);

        var differences = new List<string>();
        foreach (var fields in cases)
        {
            Assert.HasCount(3, fields, string.Join(' ', fields));
            var (pdfSha256, sourceIdentity, expectedSha256) = (fields[0], fields[1], fields[2]);
            var pdf = await store.ReadByDigestAsync(pdfSha256, CancellationToken.None);
            var artifact = LuxembourgPdfLayoutEvidenceProducer.ReadLayout(
                pdf.ToArray(), sourceIdentity, CustodyBounds.MaxObjectBytes, CancellationToken.None);
            var actual = Convert.ToHexStringLower(SHA256.HashData(artifact.Bytes.Span));
            if (!string.Equals(actual, expectedSha256, StringComparison.Ordinal))
            {
                differences.Add($"{pdfSha256}: laid out to {actual}, custody holds {expectedSha256}");
                continue;
            }

            var held = await store.ReadByDigestAsync(expectedSha256, CancellationToken.None);
            Assert.IsTrue(held.Span.SequenceEqual(artifact.Bytes.Span), expectedSha256);
        }

        Assert.IsEmpty(differences, string.Join('\n', differences));
    }
}
