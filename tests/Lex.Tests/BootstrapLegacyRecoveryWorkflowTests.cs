namespace Lex.Tests;

public sealed class BootstrapLegacyRecoveryWorkflowTests
{
    [Fact]
    public void Inventory_records_exact_route_readiness_and_a_reviewable_no_write_handoff()
    {
        var workflow = File.ReadAllText(Path.Combine(
            RepoRoot(), ".github", "workflows", "bootstrap-legacy-inventory.yml"));

        Assert.Contains("lex-bootstrap-legacy-recovery-inventory/2", workflow);
        Assert.Contains("runningState", workflow);
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
    public void Reviewed_a_plus_two_handoff_is_no_write_and_emits_an_exact_v2_receipt()
    {
        var workflow = Workflow();
        var recovery = RecoveryBlock(workflow);

        Assert.Contains("handoff-reviewed-inactive-for-first-official", recovery);
        Assert.Contains("cmp --silent plan/recovery_plan.json immediate-recovery-plan.json",
            recovery);
        Assert.Contains("lex-bootstrap-legacy-cleanup-receipt/2", recovery);
        Assert.Contains("first_pruned_inactive_revision", recovery);
        Assert.Contains("remaining_inactive_revision", recovery);
        Assert.Contains("handoff_latest_ready_revision", recovery);
        Assert.Contains("inactive_handoff", recovery);
        Assert.Contains("outcome=handoff", recovery);
        Assert.DoesNotContain("curl --request", recovery);
        Assert.DoesNotContain("az rest --method", recovery);
        Assert.DoesNotContain("revision deactivate", recovery);
        Assert.DoesNotContain("ingress traffic set", recovery);
        Assert.DoesNotContain("gh api --method POST", recovery);
        Assert.Contains("if: steps.cleanup.outputs.outcome == 'converged'", workflow);
    }

    [Fact]
    public void Bootstrap_deploy_consumes_the_two_exact_inactive_identities_in_order()
    {
        var deploy = File.ReadAllText(Path.Combine(
                RepoRoot(), ".github", "workflows", "deploy.yml"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains("bootstrap-legacy-cleanup-receipt/2", deploy);
        Assert.Contains("def plain_sha: type == \"string\" and length == 64",
            deploy);
        Assert.Contains(".recovery_plan_sha256 | plain_sha", deploy);
        Assert.Contains(".reviewed_inventory_sha256 | plain_sha", deploy);
        Assert.Contains(".post_plan_sha256 | plain_sha", deploy);
        Assert.Contains("and .post_plan_sha256 == null", deploy);
        Assert.Contains("first_pruned_inactive_revision", deploy);
        Assert.Contains("remaining_inactive_revision", deploy);
        Assert.Contains("cleanup_handoff_latest_ready", deploy);
        Assert.Contains(".latestReadyRevisionName == $ready", deploy);
        Assert.Contains("verify_bootstrap_handoff_identity", deploy);
        Assert.Contains("verify_bootstrap_forward_topology before-fallback", deploy);
        Assert.Contains("verify_bootstrap_forward_topology fallback-active", deploy);
        Assert.Contains("verify_bootstrap_forward_topology fallback-inactive", deploy);
        Assert.Contains("verify_bootstrap_forward_topology candidate-active", deploy);
        Assert.Contains("verify_bootstrap_forward_topology candidate-prepared", deploy);
        // Those stages used to be pinned by an exact revision set, which is what stalled run
        // 31926644551 waiting on a prune only Azure can schedule. The inventory is still closed
        // by name at every stage: the roles this run owns are required, and no other name is
        // admitted unless the reviewed receipt itself supplied it.
        Assert.Contains("verify_revision_set \"$previous\" \"$cleanup_survivor\" || return 1",
            deploy);
        Assert.Contains("verify_revision_set \"$previous\" \"$bootstrap_fallback\" || return 1",
            deploy);
        Assert.Contains("verify_revision_set \"$previous\" \"$bootstrap_fallback\" "
            + "\"$candidate\" || return 1", deploy);
        Assert.Contains("--arg predecessor \"$cleanup_predecessor\" "
            + "--arg survivor \"$cleanup_survivor\"", deploy);
        Assert.Contains("(($required - $observed) | length) == 0", deploy);
        Assert.Contains("((($observed - $required) - $tolerated) | length) == 0", deploy);
        Assert.DoesNotContain("([.[].name] | sort) ==", deploy);
        // Tolerating O or S is only safe while they are still the identities that were reviewed,
        // so wherever either survives it is re-proved inactive at zero traffic against the
        // receipt's own createdTime, image and canonical template digest.
        Assert.Contains("verify_reviewed_handoff_retention || return 1", deploy);
        Assert.Contains("verify_bootstrap_handoff_identity \"$cleanup_predecessor\" false 0",
            deploy);
        Assert.Contains("verify_bootstrap_handoff_identity \"$cleanup_survivor\" false 0",
            deploy);
        // O is never a required set member, so nothing else proves it is a live revision name.
        // It reaches $GITHUB_OUTPUT, where an embedded newline would inject a second key.
        Assert.Contains("[[ \"$cleanup_predecessor\" =~ ^([a-z0-9][a-z0-9-]*)?$ ]] \\\n"
            + "              && [[ \"$cleanup_survivor\" =~ ^[a-z0-9][a-z0-9-]*$ ]]", deploy);
        Assert.Contains("60); do", deploy);
    }

    [Fact]
    public void Bootstrap_handoff_never_mutates_a_or_transfers_traffic_before_promotion()
    {
        var deploy = File.ReadAllText(Path.Combine(
                RepoRoot(), ".github", "workflows", "deploy.yml"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        var start = deploy.IndexOf("if [ \"$bootstrap\" = \"true\" ]; then",
            StringComparison.Ordinal);
        var traffic = deploy.IndexOf("az_write az containerapp ingress traffic set", start,
            StringComparison.Ordinal);
        Assert.True(start >= 0 && traffic > start);
        var preparation = deploy[start..traffic];

        Assert.DoesNotContain("revision activate", preparation);
        Assert.DoesNotContain("deactivate_revision \"$previous\"", preparation);
        Assert.Contains("revision-weight \"$previous=100\" \"$candidate=0\"", deploy);
        Assert.Contains("bootstrap candidate routes did not converge to exact A100/C0", deploy);
        Assert.Contains("reconcile_candidate_retention", deploy);
        Assert.Contains("current A remained active at 100%", Workflow());
    }

    [Fact]
    public void Failure_recovery_waits_for_an_exact_created_topology_before_deactivation()
    {
        var deploy = File.ReadAllText(Path.Combine(
                RepoRoot(), ".github", "workflows", "deploy.yml"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        var recovery = deploy[deploy.IndexOf("reconcile_candidate_retention()",
            StringComparison.Ordinal)..deploy.IndexOf("finish_cleanup()",
            StringComparison.Ordinal)];

        Assert.Contains("for attempt in $(seq 1 60); do\n"
            + "                    verify_bootstrap_forward_topology candidate-recoverable",
            recovery);
        Assert.Contains("for attempt in $(seq 1 60); do\n"
            + "                  if verify_bootstrap_forward_topology fallback-inactive-recovery",
            recovery);
        Assert.True(recovery.IndexOf("verify_bootstrap_forward_topology candidate-recoverable",
                        StringComparison.Ordinal)
                    < recovery.IndexOf("deactivate_revision \"$candidate\"",
                        StringComparison.Ordinal));
        Assert.True(recovery.IndexOf("verify_bootstrap_forward_topology fallback-recoverable",
                        StringComparison.Ordinal)
                    < recovery.IndexOf("deactivate_revision \"$bootstrap_fallback\"",
                        StringComparison.Ordinal));
        Assert.Contains("if ! verify_bootstrap_prepared_state; then", deploy);
        Assert.True(System.Text.RegularExpressions.Regex.Matches(
            deploy, @"reconcile_candidate_retention \|\| status=1").Count >= 2);
        Assert.Contains("fallback_patch_attempted=false", deploy);
        Assert.Contains("candidate_patch_attempted=false", deploy);
        Assert.Contains("fallback_created=''", deploy);
        Assert.Contains("candidate_created=''", deploy);
        Assert.Contains("fallback_patch_attempted=true\n"
            + "            az_write az rest --method patch", deploy);
        Assert.Contains("candidate_patch_attempted=true\n"
            + "            az_write az rest --method patch", deploy);
        Assert.Contains("attempted R did not become observable", recovery);
        Assert.Contains("attempted C did not become observable", recovery);
        Assert.Contains("verify_bootstrap_forward_topology candidate-inactive",
            recovery);
        Assert.Contains("verify_bootstrap_forward_topology fallback-inactive-recovery",
            recovery);
        // fallback-pruning meant "the invariants already hold but Azure has not removed O or S
        // yet". The target states now tolerate a receipt-named leftover themselves, so that
        // wait state has no shape left of its own and is gone. What it guarded is asserted here.
        Assert.DoesNotContain("fallback-pruning", deploy);
        // It gated a wait loop, so recovery may still only leave the loop through a verified
        // topology, may only deactivate R after one reported R active, and may only report a
        // safe failure after re-verifying the settled topology unconditionally.
        Assert.Contains("[ \"$fallback_active\" = true ] || return 1\n"
            + "                deactivate_revision \"$bootstrap_fallback\" || return 1",
            recovery);
        Assert.Contains(
            "verify_bootstrap_forward_topology fallback-inactive-recovery || return 1",
            recovery);
        // On the forward path the guard survives literally: while R settles, the one tolerated
        // interim reading is R still active, and any other reading exits at once.
        Assert.Contains("verify_bootstrap_forward_topology fallback-recoverable \\\n"
            + "                >/dev/null 2>&1 \\\n"
            + "                || { echo \"::error::bootstrap topology drifted while "
            + "R deactivation settled\"; exit 1; }", deploy);
        var fallbackRecovery = recovery[recovery.IndexOf(
            "elif [ \"$fallback_patch_attempted\" = true ]; then",
            StringComparison.Ordinal)..];
        var finalFallback = fallbackRecovery.IndexOf(
            "verify_bootstrap_forward_topology fallback-inactive-recovery",
            StringComparison.Ordinal);
        var activeFallback = fallbackRecovery.IndexOf(
            "verify_bootstrap_forward_topology fallback-recoverable",
            StringComparison.Ordinal);
        var deactivateFallback = fallbackRecovery.IndexOf(
            "deactivate_revision \"$bootstrap_fallback\"", StringComparison.Ordinal);
        Assert.True(finalFallback >= 0 && activeFallback > finalFallback
            && deactivateFallback > activeFallback);
        var raiseRetention = recovery.IndexOf(
            "{properties:{configuration:{maxInactiveRevisions:2}}}",
            StringComparison.Ordinal);
        var deactivateCandidate = recovery.IndexOf("deactivate_revision \"$candidate\"",
            StringComparison.Ordinal);
        Assert.True(raiseRetention >= 0 && deactivateCandidate > raiseRetention);
        Assert.Contains("verify_bootstrap_forward_topology candidate-retained", recovery);
    }

    [Fact]
    public void Active_zero_hard_loss_has_a_separate_read_only_review_boundary()
    {
        var inventory = File.ReadAllText(Path.Combine(
            RepoRoot(), ".github", "workflows", "bootstrap-preparation-inventory.yml"));

        Assert.Contains("bootstrap-preparation-inventory", inventory);
        Assert.Contains("bootstrap_preparation_abandon_plan.py", inventory);
        Assert.Contains("lex-bootstrap-preparation-abandon-plan", inventory);
        Assert.Contains("mutations: \\`false\\`", inventory);
        Assert.DoesNotContain("revision deactivate", inventory);
        Assert.DoesNotContain("ingress traffic set", inventory);
        Assert.DoesNotContain("az rest --method", inventory);
    }

    private static string Workflow() => File.ReadAllText(Path.Combine(
            RepoRoot(), ".github", "workflows", "bootstrap-legacy-cleanup.yml"))
        .Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string RecoveryBlock(string workflow)
    {
        var start = workflow.IndexOf("if [ -n \"$RECOVERY_PLAN_SHA256\" ]; then",
            StringComparison.Ordinal);
        var end = workflow.IndexOf("\n          fresh_inventory immediate", start,
            StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        return workflow[start..end];
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
