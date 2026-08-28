using Lex.Ingest;
using Lex.Law;

namespace Lex.Tests;

public sealed partial class CorpusWriterTests
{
    [Fact]
    public void Handle_bound_move_honors_replace_and_no_replace_modes()
    {
        var rootPath = Path.Combine(_dir, "handle-bound-replace-modes");
        Directory.CreateDirectory(rootPath);
        File.WriteAllText(Path.Combine(rootPath, "source.txt"), "new");
        File.WriteAllText(Path.Combine(rootPath, "target.txt"), "old");
        using var root = OpenHandleBoundRoot(rootPath);

        Assert.Throws<IOException>(() =>
            MoveHandleBound(root, "source.txt", "target.txt", replace: false));
        Assert.Equal("new", File.ReadAllText(Path.Combine(rootPath, "source.txt")));
        Assert.Equal("old", File.ReadAllText(Path.Combine(rootPath, "target.txt")));

        MoveHandleBound(root, "source.txt", "target.txt", replace: true);
        Assert.False(File.Exists(Path.Combine(rootPath, "source.txt")));
        Assert.Equal("new", File.ReadAllText(Path.Combine(rootPath, "target.txt")));
    }

    [Fact]
    public void Trusted_root_handle_does_not_reopen_a_swapped_absolute_ancestor()
    {
        if (!OperatingSystem.IsWindows()) return;
        var rootPath = Path.Combine(_dir, "trusted-root-anchor");
        var displaced = Path.Combine(_dir, "trusted-root-displaced");
        var external = Path.Combine(_dir, "trusted-root-external");
        Directory.CreateDirectory(Path.Combine(rootPath, "nested"));
        Directory.CreateDirectory(Path.Combine(external, "nested"));
        File.WriteAllText(Path.Combine(rootPath, "source.txt"), "trusted");
        File.WriteAllText(Path.Combine(rootPath, "nested", "target.txt"), "old");
        File.WriteAllText(Path.Combine(external, "nested", "target.txt"), "sentinel");

        using (var root = OpenHandleBoundRoot(rootPath))
        {
            Directory.Move(rootPath, displaced);
            Assert.True(TryCreateJunction(rootPath, external));
            MoveHandleBound(root, "source.txt", "nested/target.txt", replace: true);
        }

        Directory.Delete(rootPath);
        Directory.Move(displaced, rootPath);
        Assert.Equal("trusted", File.ReadAllText(
            Path.Combine(rootPath, "nested", "target.txt")));
        Assert.Equal("sentinel", File.ReadAllText(
            Path.Combine(external, "nested", "target.txt")));
    }

    [Fact]
    public void Trusted_creation_anchor_opens_a_new_root_relative_to_its_handle()
    {
        if (!OperatingSystem.IsWindows()) return;
        var anchorPath = Path.Combine(_dir, "trusted-creation-anchor");
        var displaced = Path.Combine(_dir, "trusted-creation-displaced");
        var external = Path.Combine(_dir, "trusted-creation-external");
        Directory.CreateDirectory(anchorPath);
        Directory.CreateDirectory(external);

        using (var anchor = OpenHandleBoundRoot(anchorPath))
        {
            Directory.Move(anchorPath, displaced);
            Assert.True(TryCreateJunction(anchorPath, external));
            using var root = OpenRelativeHandleBoundRoot(
                anchor, "corpus", Path.Combine(anchorPath, "corpus"));
            EnsureHandleBoundDirectory(root, "nested");
        }

        Directory.Delete(anchorPath);
        Directory.Move(displaced, anchorPath);
        Assert.True(Directory.Exists(Path.Combine(
            anchorPath, "corpus", "nested")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(external));
    }

    [Fact]
    public async Task Writer_lock_identity_is_stable_across_an_ancestor_alias()
    {
        if (!OperatingSystem.IsWindows()) return;
        var actualParent = Path.Combine(_dir, "lock-actual-parent");
        var corpusRoot = Path.Combine(actualParent, "corpus");
        var aliasParent = Path.Combine(_dir, "lock-alias-parent");
        Directory.CreateDirectory(actualParent);
        Assert.True(TryCreateJunction(aliasParent, actualParent));
        var aliasRoot = Path.Combine(aliasParent, "corpus");
        await new CorpusWriter(corpusRoot,
                DateTimeOffset.Parse("2026-08-14T00:00:00Z"), CodeCommit)
            .WriteAsync(new SameDateAdapter(
                reverse: false, includeSecond: false), default);

        using var entered = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        var first = Task.Run(() => new CorpusWriter(corpusRoot,
                DateTimeOffset.Parse("2026-08-15T00:00:00Z"), CodeCommit,
                runIdentity: "nightly-alias-lock-1")
            .WriteAsync(new SameDateAdapter(
                reverse: false, includeFirst: false,
                beforeFirstBodyFetch: () =>
                {
                    entered.Set();
                    Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
                }), default));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));

        try
        {
            var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
                new CorpusWriter(aliasRoot,
                        DateTimeOffset.Parse("2026-08-15T00:05:00Z"), CodeCommit,
                        runIdentity: "nightly-alias-lock-2")
                    .WriteAsync(new EmptyAdapter(), default));
            Assert.Contains("writer lock", error.Message,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            release.Set();
            await first;
        }
    }

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
    public async Task V3_append_rejects_an_existing_body_link_swap_after_fetch_without_touching_the_sentinel()
    {
        if (!CanCreateSymbolicLinks()) return;

        var corpusRoot = Path.Combine(_dir, "linked-v4-body-swap-after-fetch");
        await new CorpusWriter(corpusRoot,
                DateTimeOffset.Parse("2026-08-14T00:00:00Z"), CodeCommit)
            .WriteAsync(new SameDateAdapter(
                reverse: false, includeSecond: false), default);
        var versionDirectory = Assert.Single(Directory.EnumerateDirectories(
            Path.Combine(corpusRoot, "works", "w1", "versions")));
        var meta = System.Text.Json.JsonSerializer.Deserialize<VersionMeta>(
            await File.ReadAllTextAsync(Path.Combine(versionDirectory, "meta.json")),
            CorpusJson.Options)!;
        var bodyPath = Path.Combine(versionDirectory,
            Assert.Single(Assert.Single(meta.Expressions).Observations).File!);
        var displacedBody = Path.Combine(_dir, "displaced-v4-body");
        var externalSentinel = Path.Combine(_dir, "external-v4-body-sentinel");
        var sentinelBytes = System.Text.Encoding.UTF8.GetBytes(
            "external sentinel must remain byte-identical");
        await File.WriteAllBytesAsync(externalSentinel, sentinelBytes);
        var manifestBefore = await File.ReadAllBytesAsync(
            Path.Combine(corpusRoot, "manifest.json"));
        var replacement = new SameDateAdapter(
            reverse: false, includeFirst: false, omitSource: true,
            beforeFirstBodyFetch: () =>
            {
                File.Move(bodyPath, displacedBody);
                File.CreateSymbolicLink(bodyPath, externalSentinel);
            });

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new CorpusWriter(corpusRoot,
                    DateTimeOffset.Parse("2026-08-15T00:00:00Z"), CodeCommit,
                    runIdentity: "nightly-link-swap-1")
                .WriteAsync(replacement, default));

        Assert.Contains("reparse point or symbolic link", error.Message,
            StringComparison.Ordinal);
        Assert.Equal(sentinelBytes, await File.ReadAllBytesAsync(externalSentinel));
        Assert.Equal(manifestBefore, await File.ReadAllBytesAsync(
            Path.Combine(corpusRoot, "manifest.json")));
        Assert.DoesNotContain("official:v-b",
            (await SameDateInventory(corpusRoot)).Values);
    }

    [Fact]
    public async Task Candidate_handle_bound_commit_rejects_target_swap_at_the_mutation_boundary()
    {
        if (!CanCreateSymbolicLinks()) return;
        var corpusRoot = Path.Combine(_dir, "candidate-boundary-target-swap");
        await new CorpusWriter(corpusRoot,
                DateTimeOffset.Parse("2026-08-14T00:00:00Z"), CodeCommit)
            .WriteAsync(new SameDateAdapter(reverse: false, includeSecond: false), default);
        var notice = Path.Combine(corpusRoot, "NOTICE");
        var displaced = Path.Combine(_dir, "candidate-displaced-notice");
        var sentinel = Path.Combine(_dir, "candidate-external-sentinel");
        var sentinelBytes = System.Text.Encoding.UTF8.GetBytes("candidate sentinel");
        await File.WriteAllBytesAsync(sentinel, sentinelBytes);
        var writer = new CorpusWriter(corpusRoot,
            DateTimeOffset.Parse("2026-08-15T00:00:00Z"), CodeCommit,
            runIdentity: "nightly-boundary-target-1");

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            WriteWithCommitHook(writer,
                new SameDateAdapter(reverse: false, includeFirst: false), () =>
                {
                    File.Move(notice, displaced);
                    File.CreateSymbolicLink(notice, sentinel);
                }));

        Assert.Contains("reparse point or symbolic link", error.Message,
            StringComparison.Ordinal);
        Assert.Equal(sentinelBytes, await File.ReadAllBytesAsync(sentinel));
    }

    [Fact]
    public async Task Candidate_handle_bound_commit_rejects_root_ancestor_swap_at_the_mutation_boundary()
    {
        if (!OperatingSystem.IsWindows()) return;
        var corpusRoot = Path.Combine(_dir, "candidate-boundary-root-swap");
        await new CorpusWriter(corpusRoot,
                DateTimeOffset.Parse("2026-08-14T00:00:00Z"), CodeCommit)
            .WriteAsync(new SameDateAdapter(reverse: false, includeSecond: false), default);
        var displaced = Path.Combine(_dir, "candidate-displaced-root");
        var external = Path.Combine(_dir, "candidate-external-root-sentinel");
        Directory.CreateDirectory(external);
        var sentinel = Path.Combine(external, "sentinel.txt");
        var sentinelBytes = System.Text.Encoding.UTF8.GetBytes("root sentinel");
        await File.WriteAllBytesAsync(sentinel, sentinelBytes);
        var writer = new CorpusWriter(corpusRoot,
            DateTimeOffset.Parse("2026-08-15T00:00:00Z"), CodeCommit,
            runIdentity: "nightly-boundary-root-1");

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            WriteWithCommitHook(writer,
                new SameDateAdapter(reverse: false, includeFirst: false), () =>
                {
                    Directory.Move(corpusRoot, displaced);
                    Assert.True(TryCreateJunction(corpusRoot, external));
                }));

        Assert.Contains("reparse point or symbolic link", error.Message,
            StringComparison.Ordinal);
        Assert.Equal(["sentinel.txt"], Directory.EnumerateFiles(external)
            .Select(path => Path.GetFileName(path)!).ToArray());
        Assert.Equal(sentinelBytes, await File.ReadAllBytesAsync(sentinel));
    }

    [Fact]
    public async Task Fresh_final_handle_bound_swap_rejects_works_junction_at_the_mutation_boundary()
    {
        if (!OperatingSystem.IsWindows()) return;
        var corpusRoot = Path.Combine(_dir, "fresh-boundary-junction-swap");
        await WriteLegacyBaselineAsync(corpusRoot,
            new LegacyPublisherIdentityAdapter("official:v1", ["en"]));
        var works = Path.Combine(corpusRoot, "works");
        var displaced = Path.Combine(_dir, "fresh-displaced-works");
        var external = Path.Combine(_dir, "fresh-external-sentinel");
        Directory.CreateDirectory(external);
        var sentinel = Path.Combine(external, "sentinel.txt");
        var sentinelBytes = System.Text.Encoding.UTF8.GetBytes("fresh sentinel");
        await File.WriteAllBytesAsync(sentinel, sentinelBytes);
        var attacked = false;
        void Attack(string source, string destination)
        {
            if (attacked || !string.Equals(source, works,
                    StringComparison.OrdinalIgnoreCase)) return;
            Directory.Move(works, displaced);
            Assert.True(TryCreateJunction(works, external));
            attacked = true;
        }
        var run = typeof(FreshCorpusMigration).GetMethods(
                System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Static)
            .Single(method => method.Name == "RunAsync"
                && method.GetParameters().Length == 7);
        var task = Assert.IsAssignableFrom<Task<CorpusIntegrityReport>>(run.Invoke(null,
        [
            corpusRoot, "test", new LegacyPublisherIdentityAdapter("official:v1", ["en"]),
            DateTimeOffset.Parse("2026-08-15T00:00:00Z"), CodeCommit,
            (Action<string, string>)Attack, CancellationToken.None,
        ]));

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => task);

        Assert.True(attacked);
        Assert.Contains("reparse point or symbolic link", error.Message,
            StringComparison.Ordinal);
        Assert.Equal(["sentinel.txt"], Directory.EnumerateFiles(external)
            .Select(path => Path.GetFileName(path)!).ToArray());
        Assert.Equal(sentinelBytes, await File.ReadAllBytesAsync(sentinel));
    }

    [Fact]
    public async Task V3_append_rejects_an_existing_directory_junction_swap_after_fetch_without_touching_the_sentinel()
    {
        if (!OperatingSystem.IsWindows()) return;

        var corpusRoot = Path.Combine(_dir, "junction-v4-existing-swap-after-fetch");
        await new CorpusWriter(corpusRoot,
                DateTimeOffset.Parse("2026-08-14T00:00:00Z"), CodeCommit)
            .WriteAsync(new SameDateAdapter(
                reverse: false, includeSecond: false), default);
        var versionDirectory = Assert.Single(Directory.EnumerateDirectories(
            Path.Combine(corpusRoot, "works", "w1", "versions")));
        var displaced = Path.Combine(_dir, "displaced-v4-version-directory");
        var external = Path.Combine(_dir, "external-v4-directory-sentinel");
        Directory.CreateDirectory(external);
        var sentinel = Path.Combine(external, "sentinel.txt");
        var sentinelBytes = System.Text.Encoding.UTF8.GetBytes(
            "external directory must remain byte-identical");
        await File.WriteAllBytesAsync(sentinel, sentinelBytes);
        var manifestBefore = await File.ReadAllBytesAsync(
            Path.Combine(corpusRoot, "manifest.json"));
        var replacement = new SameDateAdapter(
            reverse: false, includeFirst: false, omitSource: true,
            beforeFirstBodyFetch: () =>
            {
                Directory.Move(versionDirectory, displaced);
                Assert.True(TryCreateJunction(versionDirectory, external),
                    "The Windows test host could not create a directory junction.");
            });

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new CorpusWriter(corpusRoot,
                    DateTimeOffset.Parse("2026-08-15T00:00:00Z"), CodeCommit,
                    runIdentity: "nightly-junction-swap-1")
                .WriteAsync(replacement, default));

        Assert.Contains("reparse point or symbolic link", error.Message,
            StringComparison.Ordinal);
        Assert.Equal(["sentinel.txt"], Directory.EnumerateFiles(external)
            .Select(path => Path.GetFileName(path)!).ToArray());
        Assert.Equal(sentinelBytes, await File.ReadAllBytesAsync(sentinel));
        Assert.Equal(manifestBefore, await File.ReadAllBytesAsync(
            Path.Combine(corpusRoot, "manifest.json")));
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

    private static IDisposable OpenHandleBoundRoot(string path)
    {
        var type = typeof(CorpusWriter).Assembly.GetType(
            "Lex.Ingest.HandleBoundRename", throwOnError: true)!;
        return Assert.IsAssignableFrom<IDisposable>(type.GetMethod(
            "OpenRoot", System.Reflection.BindingFlags.Public
                        | System.Reflection.BindingFlags.Static)!.Invoke(null, [path]));
    }

    private static IDisposable OpenRelativeHandleBoundRoot(
        IDisposable anchor, string relative, string absolutePath) =>
        Assert.IsAssignableFrom<IDisposable>(anchor.GetType().GetMethod(
            "OpenRelativeRoot",
            System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.Instance)!.Invoke(
            anchor, [relative, absolutePath, true]));

    private static void EnsureHandleBoundDirectory(
        IDisposable root, string relative) => root.GetType().GetMethod(
            "EnsureDirectory",
            System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.Instance)!.Invoke(root, [relative]);

    private static void MoveHandleBound(
        IDisposable root, string source, string destination, bool replace)
    {
        try
        {
            root.GetType().GetMethod("Move",
                System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.Instance)!.Invoke(root,
                [source, destination, replace, null, null]);
        }
        catch (System.Reflection.TargetInvocationException error)
            when (error.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo
                .Capture(error.InnerException).Throw();
        }
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
