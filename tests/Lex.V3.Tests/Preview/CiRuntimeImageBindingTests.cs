using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lex.V3.Tests.Preview;

[TestClass]
public sealed class CiRuntimeImageBindingTests
{
    [TestMethod]
    public void RuntimeImageUsesVerifierProducedDockerArchiveAndBoundConfig()
    {
        var workflowPath = FindRepositoryFile(".github", "workflows", "v3-ci.yml");
        var workflow = File.ReadAllText(workflowPath);

        Assert.IsFalse(
            workflow.Contains("docker load --input artifacts/s0-05/lex-v3-s0-05.tar", StringComparison.Ordinal),
            "Docker load cannot consume the verified OCI layout on the hosted runner.");

        var verification = workflow.IndexOf(
            "-ResultPath artifacts/s0-05/oci-verification.json",
            StringComparison.Ordinal);
        var runtimeArchive = workflow.IndexOf(
            "-DockerArchivePath artifacts/s0-05/lex-v3-s0-05-docker.tar",
            StringComparison.Ordinal);
        var expectedArchive = workflow.IndexOf(
            "expected_archive_digest=\"$(jq -er '.docker_archive_sha256",
            StringComparison.Ordinal);
        var actualArchive = workflow.IndexOf(
            "actual_archive_digest=\"$(sha256sum artifacts/s0-05/lex-v3-s0-05-docker.tar",
            StringComparison.Ordinal);
        var archiveBinding = workflow.IndexOf(
            "test \"$actual_archive_digest\" = \"$expected_archive_digest\"",
            StringComparison.Ordinal);
        var dockerLoad = workflow.IndexOf(
            "docker load --input artifacts/s0-05/lex-v3-s0-05-docker.tar",
            StringComparison.Ordinal);
        var expectedConfig = workflow.IndexOf(
            "expected_config_digest=\"$(jq -er '.config_digest | select(test(\"^sha256:[0-9a-f]{64}$\"))' artifacts/s0-05/oci-verification.json)\"",
            StringComparison.Ordinal);
        var actualImage = workflow.IndexOf(
            "actual_image_id=\"$(docker image inspect --format '{{.Id}}' \"$IMAGE_REFERENCE\")\"",
            StringComparison.Ordinal);
        var binding = workflow.IndexOf(
            "test \"$actual_image_id\" = \"$expected_config_digest\"",
            StringComparison.Ordinal);
        var run = workflow.IndexOf(
            "docker run --detach --name \"$CONTAINER_NAME\"",
            StringComparison.Ordinal);

        Assert.IsTrue(verification >= 0, "The OCI verification result must be materialized.");
        Assert.IsTrue(runtimeArchive > verification, "The runtime archive must be emitted by OCI verification.");
        Assert.IsTrue(expectedArchive > runtimeArchive, "The expected runtime archive digest must come from verified evidence.");
        Assert.IsTrue(actualArchive > expectedArchive, "The runtime archive digest must be measured after verification.");
        Assert.IsTrue(archiveBinding > actualArchive, "The runtime archive bytes must be bound before loading.");
        Assert.IsTrue(dockerLoad > archiveBinding, "Docker must load only the bound verifier-produced runtime archive.");
        Assert.IsTrue(expectedConfig > dockerLoad, "The expected config digest must come from verified evidence.");
        Assert.IsTrue(actualImage > expectedConfig, "The runtime image identity must be measured after publication.");
        Assert.IsTrue(binding > actualImage, "The runtime image must be bound to the verified OCI config digest.");
        Assert.IsTrue(run > binding, "The container must not start before the image identity is bound.");
    }

    private static string FindRepositoryFile(params string[] segments)
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            var path = segments.Aggregate(current.FullName, Path.Combine);
            if (File.Exists(path))
            {
                return path;
            }
        }

        Assert.Fail($"Could not locate repository file {Path.Combine(segments)}.");
        return string.Empty;
    }
}
