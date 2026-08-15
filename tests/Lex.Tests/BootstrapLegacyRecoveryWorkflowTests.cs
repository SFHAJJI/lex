namespace Lex.Tests;

public sealed class BootstrapLegacyRecoveryWorkflowTests
{
    [Fact]
    public void Inventory_records_exact_route_authority_and_a_reviewable_recovery_plan()
    {
        var workflow = File.ReadAllText(Path.Combine(
            RepoRoot(), ".github", "workflows", "bootstrap-legacy-inventory.yml"));

        Assert.Contains("lex-bootstrap-legacy-recovery-inventory/1", workflow);
        Assert.Contains("activeRevisionsMode", workflow);
        Assert.Contains("ingress.traffic", workflow);
        Assert.Contains("latestRevision", workflow);
        Assert.Contains("label", workflow);
        Assert.Contains("bootstrap_legacy_recovery_plan.py", workflow);
        Assert.Contains("recovery_plan_sha256", workflow);
        Assert.Contains("mutations performed: \\`false\\`", workflow);
        Assert.DoesNotContain("revision deactivate", workflow);
        Assert.DoesNotContain("/deactivate", workflow);
    }

    [Fact]
    public void Recovery_is_one_exact_post_without_retry_and_emits_receipt_only_on_convergence()
    {
        var workflow = File.ReadAllText(Path.Combine(
                RepoRoot(), ".github", "workflows", "bootstrap-legacy-cleanup.yml"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains("reconcile-reviewed-inactive-for-first-official", workflow);
        Assert.Contains("bootstrap_legacy_recovery_plan.py --classify", workflow);
        Assert.Contains("$GITHUB_RUN_ATTEMPT", workflow);
        Assert.Contains("deployments: write", workflow);
        Assert.Contains("lex-bootstrap-legacy-recovery-attempt/1", workflow);
        Assert.Contains("this exact reviewed A+2 POST authority was already consumed", workflow);
        Assert.Contains("/revisions/$target/deactivate?api-version=2025-01-01", workflow);
        Assert.Equal(1, Count(workflow, "curl --silent --show-error --request POST"));
        Assert.Contains("--connect-timeout 10 --max-time 60", workflow);
        Assert.DoesNotContain("az_retry az rest --method post", workflow);
        Assert.DoesNotContain("curl --retry", workflow);
        Assert.Contains("x-ms-client-request-id", workflow);
        Assert.Contains("x-ms-request-id", workflow);
        Assert.Contains("x-ms-correlation-request-id", workflow);
        Assert.Contains("state == \"unchanged\"", workflow);
        Assert.Contains("state == \"converged\"", workflow);
        Assert.Contains("consecutive_converged", workflow);
        Assert.Contains("[ \"$consecutive_converged\" = \"3\" ]", workflow);
        Assert.Contains("outcome=inconclusive", workflow);
        Assert.Contains("remaining_inactive_revision", workflow);
        Assert.Contains("lex-bootstrap-legacy-cleanup-receipt/1", workflow);
        Assert.Contains("if: steps.cleanup.outputs.outcome == 'converged'", workflow);
        Assert.DoesNotContain("ingress traffic set", workflow);
        Assert.DoesNotContain("revision activate", workflow);
    }

    [Fact]
    public void Recovery_revalidates_exact_reviewed_state_before_the_only_mutation()
    {
        var workflow = File.ReadAllText(Path.Combine(
                RepoRoot(), ".github", "workflows", "bootstrap-legacy-cleanup.yml"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        var recovery = workflow.IndexOf("if [ -n \"$RECOVERY_PLAN_SHA256\" ]; then",
            StringComparison.Ordinal);
        var post = workflow.IndexOf("curl --silent --show-error --request POST", recovery,
            StringComparison.Ordinal);
        Assert.True(recovery >= 0 && post > recovery);
        var preMutation = workflow[recovery..post];

        Assert.Contains("cmp --silent plan/recovery_plan.json immediate-recovery-plan.json",
            preMutation);
        Assert.Contains("bootstrap_legacy_recovery_plan.py --classify", preMutation);
        Assert.Contains("[ \"$entry_state\" = \"unchanged\" ]", preMutation);
        Assert.Contains("revision list", preMutation);
        Assert.Contains("--all", preMutation);
        var preparation = preMutation.IndexOf("az account get-access-token",
            StringComparison.Ordinal);
        var marker = preMutation.IndexOf(
            "gh api --method POST \"repos/$GITHUB_REPOSITORY/deployments\"",
            StringComparison.Ordinal);
        Assert.True(preparation >= 0 && marker > preparation && marker < post - recovery);
        Assert.Contains("survivor=\"\"", workflow[post..]);
    }

    private static int Count(string value, string needle)
    {
        var count = 0;
        for (var index = 0; (index = value.IndexOf(needle, index,
                 StringComparison.Ordinal)) >= 0; index += needle.Length)
            count++;
        return count;
    }

    private static string RepoRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(directory, "Lex.slnx")))
            directory = Directory.GetParent(directory)?.FullName
                        ?? throw new InvalidOperationException("Repository root not found.");
        return directory;
    }
}
