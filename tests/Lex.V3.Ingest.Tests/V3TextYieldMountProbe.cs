using System.Diagnostics;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// The text yield per class of an installed mount, measured by the production count over the mount's own files. Never a CI
/// test.
/// </summary>
/// <remarks>
/// OPT-IN AND READ-ONLY. It runs only with <c>LEX_TEXT_YIELD_PROBE=1</c>; <c>LEX_TEXT_YIELD_PROBE_MOUNT</c> names a mount
/// directory (derive-r1's), whose corpus is read as the API's mount reads it (<see cref="LexCorpus6MountView"/>, every member
/// verified, only the kept ones held) and whose two indexes are opened read-only (journal mode <c>delete</c>, so no file is
/// written beside them); <c>LEX_TEXT_YIELD_PROBE_OUT</c> names the file the yield is written to, in the build report's own
/// block. The count is <see cref="V3TextYield.FromMount"/>, the one the mount writer runs, so these are the figures a derive at
/// this head writes into the report of that mount's inputs. Nothing goes over the network.
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class V3TextYieldMountProbe
{
    private const string EnableVariable = "LEX_TEXT_YIELD_PROBE";
    private const string MountVariable = "LEX_TEXT_YIELD_PROBE_MOUNT";
    private const string OutVariable = "LEX_TEXT_YIELD_PROBE_OUT";

    [TestMethod]
    public async Task TheTextYieldOfAnInstalledMountIsMeasured()
    {
        if (Environment.GetEnvironmentVariable(EnableVariable) != "1")
        {
            Assert.Inconclusive("Set LEX_TEXT_YIELD_PROBE=1, LEX_TEXT_YIELD_PROBE_MOUNT and LEX_TEXT_YIELD_PROBE_OUT for the read-only measurement of an installed mount.");
        }

        var mount = Environment.GetEnvironmentVariable(MountVariable);
        var output = Environment.GetEnvironmentVariable(OutVariable);
        Assert.IsTrue(Directory.Exists(mount), MountVariable + " names a mount directory.");
        Assert.IsFalse(string.IsNullOrWhiteSpace(output), OutVariable + " names the output file.");
        var clock = Stopwatch.StartNew();
        var corpus = LexCorpus6MountView.ParseCanonicalAndVerify(
            await File.ReadAllBytesAsync(Path.Combine(mount!, V3FirstMountBuildResult.CorpusFileName)));
        var luxembourg = Path.Combine(mount!, V3FirstMountBuildResult.LuxembourgIndexFileName);
        var europe = Path.Combine(mount!, V3FirstMountBuildResult.EuropeIndexFileName);
        var before = Directory.GetFiles(mount!).Order(StringComparer.Ordinal).ToArray();
        var textYield = V3TextYield.FromMount(corpus.HeldMembers, luxembourg, europe);
        CollectionAssert.AreEqual(before, Directory.GetFiles(mount!).Order(StringComparer.Ordinal).ToArray(), "The probe writes nothing beside the mount.");

        // The figures hold to the mount as the API holds a report's: every article each index holds, and every file the corpus holds.
        var lu = textYield.Publishers.Single(static row => row.Publisher == "lu-legilux");
        var eu = textYield.Publishers.Single(static row => row.Publisher == "eu-eurlex");
        Assert.AreEqual(ArticlesIn(luxembourg), lu.Articles);
        Assert.AreEqual(ArticlesIn(europe), eu.Articles);
        Assert.AreEqual(corpus.HeldMembers.LongCount(static member => member.LuxembourgRights is not null), lu.Files);
        await File.WriteAllTextAsync(output!, JsonSerializer.Serialize(textYield, V3TextYield.JsonOptions));
        Console.WriteLine($"measured in {clock.Elapsed}, peak working set {Process.GetCurrentProcess().PeakWorkingSet64 / (1024 * 1024)} MiB, corpus members {corpus.MemberCount}");
    }

    private static long ArticlesIn(string indexPath)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = indexPath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM articles";
        return (long)command.ExecuteScalar()!;
    }
}
