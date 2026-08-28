using Lex.Ingest;
using Lex.Law;

namespace Lex.Tests;

public sealed partial class CorpusWriterTests
{
    [Theory]
    [InlineData("versions-root")]
    [InlineData("version-directory")]
    [InlineData("version-meta")]
    [InlineData("observation")]
    public async Task V3_append_refuses_external_links_before_body_fetch(string targetKind)
    {
        if (!CanCreateSymbolicLinks()) return;

        var corpusRoot = Path.Combine(_dir, "linked-v4-" + targetKind);
        await new CorpusWriter(corpusRoot,
                DateTimeOffset.Parse("2026-08-14T00:00:00Z"), CodeCommit)
            .WriteAsync(new SameDateAdapter(
                reverse: false, includeSecond: false), default);
        var versionsRoot = Path.Combine(corpusRoot, "works", "w1", "versions");
        var versionDirectory = Assert.Single(Directory.EnumerateDirectories(versionsRoot));
        var metaPath = Path.Combine(versionDirectory, "meta.json");
        var meta = System.Text.Json.JsonSerializer.Deserialize<VersionMeta>(
            await File.ReadAllTextAsync(metaPath), CorpusJson.Options)!;
        var target = targetKind switch
        {
            "versions-root" => versionsRoot,
            "version-directory" => versionDirectory,
            "version-meta" => metaPath,
            "observation" => Path.Combine(versionDirectory,
                Assert.Single(Assert.Single(meta.Expressions).Observations).File!),
            _ => throw new ArgumentOutOfRangeException(nameof(targetKind)),
        };
        ReplaceWithExternalLink(target, Path.Combine(_dir, "external-v4-" + targetKind));
        var before = LinkAwareInventory(corpusRoot);
        var replacement = new SameDateAdapter(reverse: false, includeFirst: false);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new CorpusWriter(corpusRoot,
                    DateTimeOffset.Parse("2026-08-15T00:00:00Z"), CodeCommit)
                .WriteAsync(replacement, default));

        Assert.Contains("reparse point or symbolic link", error.Message,
            StringComparison.Ordinal);
        Assert.Equal(0, replacement.BodyFetchCount);
        Assert.Equal(before, LinkAwareInventory(corpusRoot));
    }

    [Fact]
    public async Task V3_append_rechecks_a_new_version_destination_after_body_fetch()
    {
        if (!CanCreateSymbolicLinks()) return;

        var corpusRoot = Path.Combine(_dir, "linked-v4-destination-after-fetch");
        await new CorpusWriter(corpusRoot,
                DateTimeOffset.Parse("2026-08-14T00:00:00Z"), CodeCommit)
            .WriteAsync(new SameDateAdapter(
                reverse: false, includeSecond: false), default);
        var replacementKey = "2025-07-28--" + Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes("official:v-b")));
        var destination = Path.Combine(
            corpusRoot, "works", "w1", "versions", replacementKey);
        var external = Path.Combine(_dir, "external-v4-destination-after-fetch");
        Directory.CreateDirectory(external);
        var replacement = new SameDateAdapter(
            reverse: false, includeFirst: false,
            beforeFirstBodyFetch: () => Directory.CreateSymbolicLink(destination, external),
            omitSource: true);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new CorpusWriter(corpusRoot,
                    DateTimeOffset.Parse("2026-08-15T00:00:00Z"), CodeCommit)
                .WriteAsync(replacement, default));

        Assert.Contains("reparse point or symbolic link", error.Message,
            StringComparison.Ordinal);
        Assert.Equal(1, replacement.BodyFetchCount);
        Assert.Empty(Directory.EnumerateFileSystemEntries(external));
    }

    [Fact]
    public async Task V3_append_refuses_a_versions_root_junction_before_body_fetch()
    {
        if (!OperatingSystem.IsWindows()) return;

        var corpusRoot = Path.Combine(_dir, "junction-v4-versions-root");
        await new CorpusWriter(corpusRoot,
                DateTimeOffset.Parse("2026-08-14T00:00:00Z"), CodeCommit)
            .WriteAsync(new SameDateAdapter(
                reverse: false, includeSecond: false), default);
        var versionsRoot = Path.Combine(corpusRoot, "works", "w1", "versions");
        var external = Path.Combine(_dir, "external-v4-versions-junction");
        Directory.Move(versionsRoot, external);
        Assert.True(TryCreateJunction(versionsRoot, external),
            "The Windows test host could not create a directory junction.");
        var before = LinkAwareInventory(corpusRoot);
        var replacement = new SameDateAdapter(reverse: false, includeFirst: false);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new CorpusWriter(corpusRoot,
                    DateTimeOffset.Parse("2026-08-15T00:00:00Z"), CodeCommit)
                .WriteAsync(replacement, default));

        Assert.Contains("reparse point or symbolic link", error.Message,
            StringComparison.Ordinal);
        Assert.Equal(0, replacement.BodyFetchCount);
        Assert.Equal(before, LinkAwareInventory(corpusRoot));
    }

    [Fact]
    public async Task V3_append_rechecks_a_new_version_destination_junction_after_body_fetch()
    {
        if (!OperatingSystem.IsWindows()) return;

        var corpusRoot = Path.Combine(_dir, "junction-v4-destination-after-fetch");
        await new CorpusWriter(corpusRoot,
                DateTimeOffset.Parse("2026-08-14T00:00:00Z"), CodeCommit)
            .WriteAsync(new SameDateAdapter(
                reverse: false, includeSecond: false), default);
        var replacementKey = "2025-07-28--" + Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes("official:v-b")));
        var destination = Path.Combine(
            corpusRoot, "works", "w1", "versions", replacementKey);
        var external = Path.Combine(_dir, "external-v4-destination-junction-after-fetch");
        Directory.CreateDirectory(external);
        var replacement = new SameDateAdapter(
            reverse: false, includeFirst: false, omitSource: true,
            beforeFirstBodyFetch: () => Assert.True(
                TryCreateJunction(destination, external),
                "The Windows test host could not create a directory junction."));

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new CorpusWriter(corpusRoot,
                    DateTimeOffset.Parse("2026-08-15T00:00:00Z"), CodeCommit)
                .WriteAsync(replacement, default));

        Assert.Contains("reparse point or symbolic link", error.Message,
            StringComparison.Ordinal);
        Assert.Equal(1, replacement.BodyFetchCount);
        Assert.Empty(Directory.EnumerateFileSystemEntries(external));
    }

    [Theory]
    [InlineData("work-directory")]
    [InlineData("work-meta")]
    [InlineData("version-directory")]
    [InlineData("version-meta")]
    [InlineData("observation")]
    public async Task Fresh_migration_refuses_external_links_without_changing_the_protected_root(
        string targetKind)
    {
        if (!CanCreateSymbolicLinks()) return;

        var corpusRoot = Path.Combine(_dir, "linked-baseline-" + targetKind);
        var baseline = await WriteLegacyWithdrawalBaselineAsync(corpusRoot);
        var workDirectory = Path.Combine(corpusRoot, "works", "code-civil");
        var target = targetKind switch
        {
            "work-directory" => workDirectory,
            "work-meta" => Path.Combine(workDirectory, "meta.json"),
            "version-directory" => Path.GetDirectoryName(baseline.MetaPath)!,
            "version-meta" => baseline.MetaPath,
            "observation" => baseline.BodyPath,
            _ => throw new ArgumentOutOfRangeException(nameof(targetKind)),
        };
        var external = Path.Combine(_dir, "external-" + targetKind);
        ReplaceWithExternalLink(target, external);
        var before = LinkAwareInventory(corpusRoot);
        var current = new LegiluxReplacementAdapter(includeWithdrawn: false);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            FreshCorpusMigration.RunAsync(
                corpusRoot, "lu-legilux", current,
                DateTimeOffset.Parse("2026-08-14T00:00:00Z"), CodeCommit, default));

        Assert.Contains("reparse point or symbolic link", error.Message,
            StringComparison.Ordinal);
        Assert.Equal(0, current.EnumerateCount);
        Assert.Equal(0, current.BodyFetchCount);
        Assert.Equal(before, LinkAwareInventory(corpusRoot));
    }

    [Fact]
    public async Task Fresh_migration_rechecks_ancestor_links_after_live_body_fetch()
    {
        if (!CanCreateSymbolicLinks()) return;

        var corpusRoot = Path.Combine(_dir, "ancestor-swapped-after-inventory");
        await WriteLegacyBaselineAsync(corpusRoot,
            new LegacyPublisherIdentityAdapter("official:v1", ["en"]));
        var versions = Directory.GetParent(
            Path.GetDirectoryName(VersionMetaPath(corpusRoot))!)!.FullName;
        var external = Path.Combine(_dir, "external-versions-after-inventory");
        var current = new LegacyPublisherIdentityAdapter(
            "official:v1", ["en", "fr"],
            beforeFirstBodyFetch: () => ReplaceWithExternalLink(versions, external));

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            FreshCorpusMigration.RunAsync(
                corpusRoot, "test", current,
                DateTimeOffset.Parse("2026-08-14T00:00:00Z"), CodeCommit, default));

        Assert.Contains("reparse point or symbolic link", error.Message,
            StringComparison.Ordinal);
        Assert.Equal(1, current.BodyFetchCount);
        Assert.True((File.GetAttributes(versions) & FileAttributes.ReparsePoint) != 0);
    }

    [Fact]
    public async Task Fresh_migration_refuses_a_fileless_metadata_destination_link()
    {
        if (!CanCreateSymbolicLinks()) return;

        var corpusRoot = Path.Combine(_dir, "linked-fileless-stage-meta");
        var adapter = new OneVersionAdapter("in_force", "finance");
        await new CorpusWriter(corpusRoot,
                DateTimeOffset.Parse("2026-08-13T00:00:00Z"), CodeCommit)
            .WriteAsync(adapter, default);
        var before = LinkAwareInventory(corpusRoot);
        var external = Path.Combine(_dir, "external-fileless-stage-meta");
        Directory.CreateDirectory(external);
        var swapped = false;

        void LinkMetadataDestination(string destination)
        {
            if (swapped
                || !destination.EndsWith("meta.json", StringComparison.Ordinal)) return;
            var versionDirectory = Path.GetDirectoryName(destination)!;
            Directory.CreateDirectory(Path.GetDirectoryName(versionDirectory)!);
            Directory.CreateSymbolicLink(versionDirectory, external);
            swapped = true;
        }

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            RunFreshWithStageWriteHook(
                corpusRoot, new OneVersionAdapter("in_force", "finance"),
                LinkMetadataDestination));

        Assert.True(swapped);
        Assert.Contains("reparse point or symbolic link", error.Message,
            StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(external, "meta.json")));
        Assert.Equal(before, LinkAwareInventory(corpusRoot));
    }

    [Fact]
    public async Task Fresh_migration_refuses_a_nested_observation_destination_link()
    {
        if (!CanCreateSymbolicLinks()) return;

        const string member = "CL2012R0648FR0200010.0001.doc.xml";
        var body = SourceBodyFetch.Retrieved("<html>publisher text</html>");
        var corpusRoot = Path.Combine(_dir, "linked-nested-stage-observation");
        await new CorpusWriter(corpusRoot,
                DateTimeOffset.Parse("2026-08-13T00:00:00Z"), CodeCommit)
            .WriteAsync(new AltThenPrimaryAdapter(body, member), default);
        var before = LinkAwareInventory(corpusRoot);
        var external = Path.Combine(_dir, "external-nested-stage-observation");
        Directory.CreateDirectory(external);
        var swapped = false;

        void LinkObservationDestination(string destination)
        {
            if (swapped || !destination.EndsWith(member, StringComparison.Ordinal)) return;
            var nestedDirectory = Path.GetDirectoryName(destination)!;
            Directory.CreateDirectory(Path.GetDirectoryName(nestedDirectory)!);
            Directory.CreateSymbolicLink(nestedDirectory, external);
            swapped = true;
        }

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            RunFreshWithStageWriteHook(
                corpusRoot, new AltThenPrimaryAdapter(body, member),
                LinkObservationDestination));

        Assert.True(swapped);
        Assert.Contains("reparse point or symbolic link", error.Message,
            StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(external, member)));
        Assert.Equal(before, LinkAwareInventory(corpusRoot));
    }

    [Fact]
    public async Task Fresh_migration_rechecks_the_source_after_the_stage_write_hook()
    {
        if (!CanCreateSymbolicLinks()) return;

        var body = SourceBodyFetch.Retrieved("<html>publisher text</html>");
        var corpusRoot = Path.Combine(_dir, "source-swapped-before-stage-copy");
        await new CorpusWriter(corpusRoot,
                DateTimeOffset.Parse("2026-08-13T00:00:00Z"), CodeCommit)
            .WriteAsync(new OneVersionAdapter(
                "in_force", "finance", bodyFetch: body), default);
        var versions = Directory.GetParent(
            Path.GetDirectoryName(VersionMetaPath(corpusRoot))!)!.FullName;
        var external = Path.Combine(_dir, "external-source-before-stage-copy");
        var swapped = false;

        void LinkSourceAncestor(string destination)
        {
            if (swapped || !destination.EndsWith(".html", StringComparison.Ordinal)) return;
            ReplaceWithExternalLink(versions, external);
            swapped = true;
        }

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            RunFreshWithStageWriteHook(
                corpusRoot, new OneVersionAdapter(
                    "in_force", "finance", bodyFetch: body),
                LinkSourceAncestor));

        Assert.True(swapped);
        Assert.Contains("reparse point or symbolic link", error.Message,
            StringComparison.Ordinal);
        Assert.True((File.GetAttributes(versions) & FileAttributes.ReparsePoint) != 0);
    }

    private static void ReplaceWithExternalLink(string target, string external)
    {
        var directory = Directory.Exists(target);
        if (directory)
        {
            Directory.Move(target, external);
        }
        else
        {
            File.Move(target, external);
        }
        if (directory) Directory.CreateSymbolicLink(target, external);
        else File.CreateSymbolicLink(target, external);
    }

    private bool CanCreateSymbolicLinks()
    {
        var probe = Path.Combine(_dir, "link-probe-" + Guid.NewGuid().ToString("N"));
        var target = probe + ".target";
        Directory.CreateDirectory(target);
        try
        {
            Directory.CreateSymbolicLink(probe, target);
            return true;
        }
        catch (Exception error) when (error is IOException
                                      or UnauthorizedAccessException
                                      or PlatformNotSupportedException)
        {
            return false;
        }
        finally
        {
            try { if (Directory.Exists(probe)) Directory.Delete(probe); } catch { }
            try { if (Directory.Exists(target)) Directory.Delete(target); } catch { }
        }
    }

    private static bool TryCreateJunction(string junction, string target)
    {
        var start = new System.Diagnostics.ProcessStartInfo("cmd.exe")
        {
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("/d");
        start.ArgumentList.Add("/c");
        start.ArgumentList.Add("mklink");
        start.ArgumentList.Add("/J");
        start.ArgumentList.Add(junction);
        start.ArgumentList.Add(target);
        using var process = System.Diagnostics.Process.Start(start);
        if (process is null) return false;
        _ = process.StandardOutput.ReadToEnd();
        _ = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode == 0
            && Directory.Exists(junction)
            && (File.GetAttributes(junction) & FileAttributes.ReparsePoint) != 0;
    }

    private static SortedDictionary<string, string> LinkAwareInventory(string root)
    {
        var inventory = new SortedDictionary<string, string>(StringComparer.Ordinal);
        Visit(root);
        return inventory;

        void Visit(string directory)
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory)
                         .Order(StringComparer.Ordinal))
            {
                var relative = Path.GetRelativePath(root, entry).Replace('\\', '/');
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    var info = (attributes & FileAttributes.Directory) != 0
                        ? (FileSystemInfo)new DirectoryInfo(entry)
                        : new FileInfo(entry);
                    inventory[relative] = "link:" + info.LinkTarget;
                }
                else if ((attributes & FileAttributes.Directory) != 0)
                {
                    inventory[relative] = "directory";
                    Visit(entry);
                }
                else
                {
                    inventory[relative] = "file:" + Convert.ToHexStringLower(
                        System.Security.Cryptography.SHA256.HashData(
                            File.ReadAllBytes(entry)));
                }
            }
        }
    }
}
