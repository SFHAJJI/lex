using System.Security.Cryptography;
using System.Text;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Contracts.Source.Europe;

/// <summary>
/// Why the embedded seed lines could not be admitted as Appendix A. Closed.
/// </summary>
/// <remarks>
/// This is a data-integrity self-check on a compiled-in constant, never a runtime input contract:
/// nothing in this assembly can construct a <see cref="EuAppendixASeedMap"/> from caller-supplied
/// seed lines. The typed vocabulary still exists, and every member is still driven by a test,
/// because the whole point of <see cref="EuAppendixASeedMap.TryValidateAndCanonicalize"/> being a
/// pure function over an explicit list is that a test can hand it a deliberately corrupted list and
/// watch each branch fire, rather than trusting that the 82 lines below are correct because they
/// look right.
/// </remarks>
public enum EuAppendixASeedMapRefusal
{
    /// <summary>No refusal.</summary>
    None = 0,

    /// <summary>
    /// R7: "exactly 82 canonical seed lines". A different count is not this appendix.
    /// </summary>
    SeedCountNotEightyTwo = 1,

    /// <summary>
    /// The appendix's own fence text is explicit: "sorted by CELEX". A traversal that is not
    /// strictly ascending could still contain the right 82 rows in the wrong order, and the
    /// canonical serialization this type reconstructs would then not be Appendix A's bytes.
    /// </summary>
    CelexNotStrictlyAscending = 2,

    /// <summary>A CELEX identity occurs twice. The map is keyed by CELEX and must be a function.</summary>
    CelexRepeated = 3,

    /// <summary>
    /// A public Work root occurs twice. R7: "no duplicate Work target". Two seeds naming the same
    /// Work would let one Work satisfy two rows of the terminal accounting this pack backs.
    /// </summary>
    WorkRootRepeated = 4,

    /// <summary>
    /// A seed's Work root does not reduce to Appendix A's exact lexical form under
    /// <see cref="EuPackRootCanonicalForm"/>. Every root this pack ever hands out is canonicalized
    /// before it is retained, so nothing downstream that compares against the pack can be handed a
    /// spelling <see cref="EuFeedRootIntersection"/> and <see cref="EuPackRootCanonicalForm"/> would
    /// not themselves have canonicalized first.
    /// </summary>
    WorkRootNotCanonical = 5,

    /// <summary>
    /// The reconstructed canonical bytes do not hash to the digest the caller expected. This is the
    /// actual proof that a seed list is Appendix A rather than 82 rows that merely look like it: the
    /// fence in <c>D1-01-OFFICIAL-SOURCE-BOUNDARY-CANDIDATE-5-2026-08-31.md</c> names
    /// <c>14fb3c685d341244de30d0306d1dca0169945a19772412a2b4ffc85034267b9a</c> over the exact ASCII,
    /// one-tab, LF, final-LF, CELEX-sorted, no-header serialization, and this type recomputes that
    /// same digest from its own in-memory list rather than asserting the constant.
    /// </summary>
    CanonicalBytesDigestMismatch = 6,
}

/// <summary>
/// Appendix A of D1-01 Candidate 5: the exact, closed, 82-seed CELEX-to-public-Work resolution map
/// that bounds the entire EU root universe (R1, R7).
/// </summary>
/// <remarks>
/// <para>
/// R1 is explicit that "the root universe is exactly the 82 canonical seed lines whose SHA-256 is
/// <c>ea1b4f27...</c>, resolved by the frozen CELEX predicate to the 82 unique public Work URIs
/// whose map SHA-256 is <c>14fb3c68...</c>. It is finite and explicit." R7 restates the same map as
/// "six treaty seeds and 76 sector-3 seeds" with "no duplicate Work target". This type is that map,
/// held as data rather than derived, because no bounded observation this repository has taken
/// re-resolves CELEX to Work root at runtime; re-resolving it here would be inventing a second
/// resolution the ruling never asked D1-05 to perform. What D1-05 owns is holding the frozen result
/// under a digest a test can recompute, and canonicalizing every root it hands out so nothing that
/// compares against this pack can be handed a spelling <see cref="EuFeedRootIntersection"/> would
/// not itself have canonicalized to the identical string first.
/// </para>
/// <para>
/// The reviewer's carried condition for D1-05 (RULING event, and the item-16 REVIEW_RESULT at
/// <c>lex-event-20260903T200525425Z-85f4c2e5207e42739121779adb94ec51</c>): "Appendix A is not
/// pinned in the tree (no seed map artifact, the seed appears only in a test constant); D1-05 binds
/// the seed map by its digest 14fb3c68 as a declared input and the canonical form is then checked
/// against it." <see cref="SeedMapRef"/> is that binding, and <see cref="PackRoots"/> is the
/// canonical form the check runs against.
/// </para>
/// </remarks>
public static class EuAppendixASeedMap
{
    /// <summary>
    /// Appendix A's own digest over the canonical ASCII, one-tab, LF, final-LF, CELEX-sorted,
    /// no-header serialization of the 82 seed lines. Named identically to the candidate text's own
    /// fence so a reader can grep one string across both.
    /// </summary>
    public const string AppendixASha256 =
        "14fb3c685d341244de30d0306d1dca0169945a19772412a2b4ffc85034267b9a";

    /// <summary>Appendix A's own stated byte count for that same serialization.</summary>
    public const int AppendixAByteCount = 7708;

    /// <summary>Appendix A's own stated seed count: six treaty seeds plus 76 sector-3 seeds.</summary>
    public const int SeedCount = 82;

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>
    /// The 82 seed lines exactly as Appendix A's fence gives them, in the fence's own order.
    /// Transcribed mechanically from the frozen candidate text (a byte-exact extraction was
    /// SHA-256-verified against <see cref="AppendixASha256"/> before this array was written), never
    /// retyped by hand from the rendered document.
    /// </summary>
    private static readonly (string Celex, string WorkRoot)[] SeedLines =
    {
        ("12012E/TXT", "http://publications.europa.eu/resource/cellar/ccccda77-8ac2-4a25-8e66-a5827ecd3459"),
        ("12012M/TXT", "http://publications.europa.eu/resource/cellar/2bf140bf-a3f8-4ab2-b506-fd71826e6da6"),
        ("12012P/TXT", "http://publications.europa.eu/resource/cellar/20e2519e-c2d9-4b23-86ed-7b65a7dd81ec"),
        ("12016E/TXT", "http://publications.europa.eu/resource/cellar/f1bcba61-0d85-4e7f-b41e-a72c42eb5c49"),
        ("12016M/TXT", "http://publications.europa.eu/resource/cellar/2021d50a-3468-11e6-969e-01aa75ed71a1"),
        ("12016P/TXT", "http://publications.europa.eu/resource/cellar/c483a582-2c70-11e6-b497-01aa75ed71a1"),
        ("32003L0087", "http://publications.europa.eu/resource/cellar/518d47fe-bb29-4eac-a25e-42d0499f4e19"),
        ("32003L0088", "http://publications.europa.eu/resource/cellar/050dd964-4f94-4c61-ab50-89217a0d90e2"),
        ("32003R0001", "http://publications.europa.eu/resource/cellar/9191c590-efda-4786-8179-001ea608103e"),
        ("32004L0048", "http://publications.europa.eu/resource/cellar/c5da2d9b-495e-4618-a6c8-c6400c00f1f9"),
        ("32004R0139", "http://publications.europa.eu/resource/cellar/982a6d0d-5767-4a00-9065-a7954f57de58"),
        ("32005L0029", "http://publications.europa.eu/resource/cellar/7fecf696-5eda-47b3-b838-0202aca4f2dd"),
        ("32006L0054", "http://publications.europa.eu/resource/cellar/bed4fa3f-d7f2-480b-b244-75d98e4efe14"),
        ("32006L0112", "http://publications.europa.eu/resource/cellar/ded2ee9c-f30e-4ed1-ab74-b2b7d7a7a6b6"),
        ("32006L0116", "http://publications.europa.eu/resource/cellar/55214a8a-6268-409c-ae5e-59c8241cf445"),
        ("32007L0036", "http://publications.europa.eu/resource/cellar/2624447f-8c17-4208-8889-a98c49d39339"),
        ("32007R0864", "http://publications.europa.eu/resource/cellar/0e830168-524b-43dd-b73d-a87d367c14ad"),
        ("32008R0593", "http://publications.europa.eu/resource/cellar/3db0a06f-cae9-433d-a229-dde3e68d6dc7"),
        ("32011L0016", "http://publications.europa.eu/resource/cellar/9c0b4723-7e53-4595-9fa7-1b78e5ddf337"),
        ("32011L0083", "http://publications.europa.eu/resource/cellar/6c8e7593-7a75-4b58-b709-542f06ee7a28"),
        ("32012R1215", "http://publications.europa.eu/resource/cellar/387e4da5-e292-496d-a9bc-c3ebface5413"),
        ("32012R1257", "http://publications.europa.eu/resource/cellar/46f2048f-54d1-11e2-9294-01aa75ed71a1"),
        ("32013L0036", "http://publications.europa.eu/resource/cellar/2ff66fd7-df0b-11e2-9165-01aa75ed71a1"),
        ("32013R0575", "http://publications.europa.eu/resource/cellar/ccd31733-df06-11e2-9165-01aa75ed71a1"),
        ("32014L0023", "http://publications.europa.eu/resource/cellar/7ebc3110-b653-11e3-86f9-01aa75ed71a1"),
        ("32014L0024", "http://publications.europa.eu/resource/cellar/aa61f069-b654-11e3-86f9-01aa75ed71a1"),
        ("32014L0025", "http://publications.europa.eu/resource/cellar/3c100e84-b654-11e3-86f9-01aa75ed71a1"),
        ("32014L0041", "http://publications.europa.eu/resource/cellar/e1440a9e-d0fe-11e3-8cd4-01aa75ed71a1"),
        ("32014L0065", "http://publications.europa.eu/resource/cellar/056587cf-f1f8-11e3-8cd4-01aa75ed71a1"),
        ("32014L0067", "http://publications.europa.eu/resource/cellar/03be754c-e637-11e3-8cd4-01aa75ed71a1"),
        ("32014R0596", "http://publications.europa.eu/resource/cellar/329793ac-f1f6-11e3-8cd4-01aa75ed71a1"),
        ("32014R0600", "http://publications.europa.eu/resource/cellar/3b729ddf-f1f7-11e3-8cd4-01aa75ed71a1"),
        ("32014R0651", "http://publications.europa.eu/resource/cellar/1291bb4c-fcfe-11e3-831f-01aa75ed71a1"),
        ("32014R0910", "http://publications.europa.eu/resource/cellar/23b61856-2e82-11e4-8c3c-01aa75ed71a1"),
        ("32015L0849", "http://publications.europa.eu/resource/cellar/0bff31ef-0b49-11e5-8817-01aa75ed71a1"),
        ("32015L2366", "http://publications.europa.eu/resource/cellar/dd85ef2e-a953-11e5-b528-01aa75ed71a1"),
        ("32015R0848", "http://publications.europa.eu/resource/cellar/db164a5d-0b48-11e5-8817-01aa75ed71a1"),
        ("32016L0680", "http://publications.europa.eu/resource/cellar/182703d1-11bd-11e6-ba9a-01aa75ed71a1"),
        ("32016L1164", "http://publications.europa.eu/resource/cellar/029ea67e-4d76-11e6-89bd-01aa75ed71a1"),
        ("32016R0679", "http://publications.europa.eu/resource/cellar/3e485e15-11bd-11e6-ba9a-01aa75ed71a1"),
        ("32016R1011", "http://publications.europa.eu/resource/cellar/5f55dd2e-3dbb-11e6-a825-01aa75ed71a1"),
        ("32017L1132", "http://publications.europa.eu/resource/cellar/eba5bed3-5d59-11e7-954d-01aa75ed71a1"),
        ("32017R1001", "http://publications.europa.eu/resource/cellar/2d2217a2-525b-11e7-a5ca-01aa75ed71a1"),
        ("32017R1129", "http://publications.europa.eu/resource/cellar/1495c976-5d5a-11e7-954d-01aa75ed71a1"),
        ("32018L0843", "http://publications.europa.eu/resource/cellar/6ea73588-7380-11e8-9483-01aa75ed71a1"),
        ("32018L0957", "http://publications.europa.eu/resource/cellar/fe8beacc-8343-11e8-ac6a-01aa75ed71a1"),
        ("32018L2001", "http://publications.europa.eu/resource/cellar/7aa60dea-04e7-11e9-adde-01aa75ed71a1"),
        ("32018R1999", "http://publications.europa.eu/resource/cellar/c6980a60-04e7-11e9-adde-01aa75ed71a1"),
        ("32019L0001", "http://publications.europa.eu/resource/cellar/1609a9fc-17cc-11e9-8d04-01aa75ed71a1"),
        ("32019L0770", "http://publications.europa.eu/resource/cellar/c426ed23-7c64-11e9-9f05-01aa75ed71a1"),
        ("32019L0771", "http://publications.europa.eu/resource/cellar/28907e25-7c65-11e9-9f05-01aa75ed71a1"),
        ("32019L0790", "http://publications.europa.eu/resource/cellar/214471fe-786e-11e9-9f05-01aa75ed71a1"),
        ("32019L0944", "http://publications.europa.eu/resource/cellar/8594f013-8e7c-11e9-9369-01aa75ed71a1"),
        ("32019L1151", "http://publications.europa.eu/resource/cellar/37ce8467-a3b5-11e9-9d01-01aa75ed71a1"),
        ("32019L1152", "http://publications.europa.eu/resource/cellar/aaba87d8-a3b5-11e9-9d01-01aa75ed71a1"),
        ("32019L2121", "http://publications.europa.eu/resource/cellar/0bce5c5c-1d3f-11ea-95ab-01aa75ed71a1"),
        ("32019R1111", "http://publications.europa.eu/resource/cellar/524570fa-9c9a-11e9-9d01-01aa75ed71a1"),
        ("32019R2088", "http://publications.europa.eu/resource/cellar/4f50e277-1a53-11ea-8c1f-01aa75ed71a1"),
        ("32020R0852", "http://publications.europa.eu/resource/cellar/e5ba36a8-b454-11ea-bb7a-01aa75ed71a1"),
        ("32020R1783", "http://publications.europa.eu/resource/cellar/4fc86880-3464-11eb-b27b-01aa75ed71a1"),
        ("32020R1784", "http://publications.europa.eu/resource/cellar/9d4f5cd2-3464-11eb-b27b-01aa75ed71a1"),
        ("32021R1119", "http://publications.europa.eu/resource/cellar/365a2e8e-e04f-11eb-895a-01aa75ed71a1"),
        ("32022L0542", "http://publications.europa.eu/resource/cellar/f09670e0-b543-11ec-b6f4-01aa75ed71a1"),
        ("32022L2041", "http://publications.europa.eu/resource/cellar/97ee19ca-543f-11ed-92ed-01aa75ed71a1"),
        ("32022L2523", "http://publications.europa.eu/resource/cellar/c0f3ca3d-81cb-11ed-9887-01aa75ed71a1"),
        ("32022L2555", "http://publications.europa.eu/resource/cellar/9b84d482-85bd-11ed-9887-01aa75ed71a1"),
        ("32022R1925", "http://publications.europa.eu/resource/cellar/b4326c28-49cd-11ed-92ed-01aa75ed71a1"),
        ("32022R2065", "http://publications.europa.eu/resource/cellar/3ff67256-55c4-11ed-92ed-01aa75ed71a1"),
        ("32022R2554", "http://publications.europa.eu/resource/cellar/0caf473a-85bd-11ed-9887-01aa75ed71a1"),
        ("32023L0970", "http://publications.europa.eu/resource/cellar/5bbb9daf-f470-11ed-a05c-01aa75ed71a1"),
        ("32023R1114", "http://publications.europa.eu/resource/cellar/01d55833-0660-11ee-b12e-01aa75ed71a1"),
        ("32023R1115", "http://publications.europa.eu/resource/cellar/d80446fe-0660-11ee-b12e-01aa75ed71a1"),
        ("32023R1543", "http://publications.europa.eu/resource/cellar/1b9e472a-2ce3-11ee-95a2-01aa75ed71a1"),
        ("32023R2831", "http://publications.europa.eu/resource/cellar/75417f3b-9aec-11ee-b164-01aa75ed71a1"),
        ("32023R2854", "http://publications.europa.eu/resource/cellar/ef51c6ab-a06c-11ee-b164-01aa75ed71a1"),
        ("32024L1640", "http://publications.europa.eu/resource/cellar/698476cd-2dd4-11ef-a61b-01aa75ed71a1"),
        ("32024L1760", "http://publications.europa.eu/resource/cellar/a416fd73-3a66-11ef-a1cb-01aa75ed71a1"),
        ("32024R1620", "http://publications.europa.eu/resource/cellar/8e78c1d2-2dd4-11ef-a61b-01aa75ed71a1"),
        ("32024R1624", "http://publications.europa.eu/resource/cellar/868bf3cf-2dd4-11ef-a61b-01aa75ed71a1"),
        ("32024R1689", "http://publications.europa.eu/resource/cellar/dc8116a1-3fe6-11ef-865a-01aa75ed71a1"),
        ("32024R1781", "http://publications.europa.eu/resource/cellar/66b9f8da-34ea-11ef-b441-01aa75ed71a1"),
        ("32024R2847", "http://publications.europa.eu/resource/cellar/21b7d4eb-a6e2-11ef-85f0-01aa75ed71a1"),
    };

    /// <summary>
    /// The seed-map artifact identity D1-05 declares for <see cref="SourceArtifactRef"/> binding
    /// purposes (the reviewer's carried condition). The resource id is this contract's own minted
    /// <c>urn:uuid</c>, never a value scraped from the candidate text, which names the map only by
    /// digest and byte count. The digest half is <see cref="AppendixASha256"/> itself, recomputed at
    /// static initialization time from <see cref="SeedLines"/> rather than asserted.
    /// </summary>
    public static SourceArtifactRef SeedMapRef { get; } = BuildSeedMapRef();

    /// <summary>
    /// <c>P</c>'s ground truth: the 82 public Work roots, each already reduced to Appendix A's exact
    /// lexical form, sorted ordinally (this is a set; CELEX order is a different, also-exposed
    /// arrangement). This is the list a caller passes as <c>discoveredPackRoots</c> when the closure
    /// query has not (yet, or ever, for a seed with no further closure) discovered anything beyond
    /// the seed itself, and it is the list <see cref="EuPrimaryEnumerationRootBinding"/> checks every
    /// resolved root against.
    /// </summary>
    public static IReadOnlyList<string> PackRoots { get; } = BuildPackRoots();

    /// <summary>
    /// The map itself, CELEX to canonical Work root, in the fence's own CELEX-ascending order. Used
    /// to name which of the 82 seeds a discovered root traces back to; never used to re-derive
    /// membership, which is <see cref="PackRoots"/>'s job.
    /// </summary>
    public static IReadOnlyList<(string Celex, string WorkRoot)> SeedsInCelexOrder { get; } =
        Array.AsReadOnly(((string, string)[])SeedLines.Clone());

    private static SourceArtifactRef BuildSeedMapRef()
    {
        var canonical = TryValidateAndCanonicalize(SeedLines, AppendixASha256, out var refusal);
        if (canonical is null)
        {
            throw new InvalidOperationException(
                $"The embedded Appendix A seed lines failed self-validation with {refusal}. " +
                "This is a defect in this file's own transcription, never a runtime input.");
        }

        return new SourceArtifactRef(
            "urn:uuid:618963c7-0c91-4a23-a17f-3a723f5ee74e",
            AppendixASha256);
    }

    private static IReadOnlyList<string> BuildPackRoots()
    {
        var canonical = TryValidateAndCanonicalize(SeedLines, AppendixASha256, out var refusal);
        if (canonical is null)
        {
            throw new InvalidOperationException(
                $"The embedded Appendix A seed lines failed self-validation with {refusal}. " +
                "This is a defect in this file's own transcription, never a runtime input.");
        }

        return canonical;
    }

    /// <summary>
    /// Validate a candidate seed list against every rule Appendix A's own fence states, and return
    /// the canonicalized, ordinally sorted Work-root set on success. A pure function over an
    /// explicit list rather than a method on <see cref="SeedLines"/> directly, so a test can hand it
    /// a deliberately corrupted list and drive each refusal, and so the one production call (against
    /// the real 82 lines) and every hostile test call go through the identical path.
    /// </summary>
    /// <param name="seedLines">The candidate seed list, in whatever order the caller holds it.</param>
    /// <param name="expectedSha256">
    /// The digest the canonical serialization must reduce to. Production always passes
    /// <see cref="AppendixASha256"/>; a test passes a different expectation to prove the mismatch
    /// branch fires on an otherwise-valid list.
    /// </param>
    /// <param name="refusal">Why no canonical pack exists, when none does.</param>
    internal static IReadOnlyList<string>? TryValidateAndCanonicalize(
        IReadOnlyList<(string Celex, string WorkRoot)> seedLines,
        string expectedSha256,
        out EuAppendixASeedMapRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(seedLines);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedSha256);

        var rows = seedLines.ToArray();
        if (rows.Length != SeedCount)
        {
            refusal = EuAppendixASeedMapRefusal.SeedCountNotEightyTwo;
            return null;
        }

        // Distinctness is checked before order, not after. A strictly-ascending pairwise check
        // already implies distinctness by transitivity (a < b < c leaves no room for a repeat
        // anywhere in the sequence, adjacent or not), so checking order first would make the
        // repeat check below unreachable: nothing could ever fail it once ordering had already
        // passed. Checking the repeat independently first, over the whole set rather than adjacent
        // pairs, keeps both refusals live: a repeat anywhere is caught here regardless of where the
        // two copies sit, and an all-distinct-but-shuffled list still reaches the ordering check
        // below.
        var celexSeen = new HashSet<string>(rows.Select(static row => row.Celex), StringComparer.Ordinal);
        if (celexSeen.Count != rows.Length)
        {
            refusal = EuAppendixASeedMapRefusal.CelexRepeated;
            return null;
        }

        for (var i = 1; i < rows.Length; i++)
        {
            if (string.CompareOrdinal(rows[i - 1].Celex, rows[i].Celex) >= 0)
            {
                refusal = EuAppendixASeedMapRefusal.CelexNotStrictlyAscending;
                return null;
            }
        }

        var canonicalRoots = new string[rows.Length];
        for (var i = 0; i < rows.Length; i++)
        {
            var canonical = EuPackRootCanonicalForm.TryCanonicalize(rows[i].WorkRoot, out _);
            if (canonical is null)
            {
                refusal = EuAppendixASeedMapRefusal.WorkRootNotCanonical;
                return null;
            }

            canonicalRoots[i] = canonical;
        }

        var rootSeen = new HashSet<string>(canonicalRoots, StringComparer.Ordinal);
        if (rootSeen.Count != canonicalRoots.Length)
        {
            refusal = EuAppendixASeedMapRefusal.WorkRootRepeated;
            return null;
        }

        var actualSha256 = ComputeCanonicalSerializationSha256(rows);
        if (!string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            refusal = EuAppendixASeedMapRefusal.CanonicalBytesDigestMismatch;
            return null;
        }

        refusal = EuAppendixASeedMapRefusal.None;
        Array.Sort(canonicalRoots, StringComparer.Ordinal);
        return Array.AsReadOnly(canonicalRoots);
    }

    /// <summary>
    /// Reconstructs Appendix A's exact canonical byte serialization from an explicit seed list
    /// (ASCII, one literal tab between columns, LF line endings, final LF, CELEX order, no header)
    /// and returns its lowercase-hex SHA-256. Internal so a test can vary the input and observe the
    /// digest change, the same sensitivity-testing pattern <see cref="EuScopeProfile"/> uses for its
    /// own computed digests.
    /// </summary>
    internal static string ComputeCanonicalSerializationSha256(
        IReadOnlyList<(string Celex, string WorkRoot)> seedLines)
    {
        ArgumentNullException.ThrowIfNull(seedLines);
        var builder = new StringBuilder();
        foreach (var (celex, workRoot) in seedLines)
        {
            builder.Append(celex).Append('\t').Append(workRoot).Append('\n');
        }

        return Convert.ToHexStringLower(SHA256.HashData(StrictUtf8.GetBytes(builder.ToString())));
    }
}
