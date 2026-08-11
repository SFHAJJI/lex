using System.Text.RegularExpressions;

namespace Lex.Tests;

public sealed class ReleaseWorkflowTests
{
    [Fact]
    public void Candidate_deployment_is_zero_traffic_unless_promotion_is_explicit()
    {
        var workflow = File.ReadAllText(Path.Combine(RepoRoot(), ".github", "workflows", "deploy.yml"));

        var promoteStart = workflow.IndexOf("\n      promote:", StringComparison.Ordinal);
        var promoteEnd = workflow.IndexOf("\n  repository_dispatch:", promoteStart,
            StringComparison.Ordinal);
        Assert.True(promoteStart >= 0 && promoteEnd > promoteStart);
        Assert.Contains("default: false", workflow[promoteStart..promoteEnd]);
        Assert.Contains("Promote only with revision-traffic.yml", workflow);
        Assert.DoesNotContain("- name: Promote candidate", workflow);
        Assert.Contains("$candidate=0", workflow);
        Assert.Contains("AppRequests AppDependencies AppTraces", workflow);
        Assert.Contains("APPLICATION_INSIGHTS_NAME: ai-lex-web", workflow);
        Assert.Contains("-a \"$APPLICATION_INSIGHTS_NAME\"", workflow);
        Assert.Contains("--app \"$APPLICATION_INSIGHTS_NAME\"", workflow);
        Assert.DoesNotContain("--app ai-lex-web", workflow);
        Assert.Contains("mapfile -t retention", workflow);
        Assert.Contains("${retention[0]}", workflow);
        Assert.Contains("${retention[1]}", workflow);
        Assert.DoesNotContain("$'90\\t90'", workflow);
        Assert.Contains("retention is not the published 90 days", workflow);
        Assert.Contains("LEXTRACE${GITHUB_RUN_ID}${GITHUB_RUN_ATTEMPT}", workflow);
        Assert.Contains("candidate request telemetry was not exported", workflow);
        Assert.Contains("raw query or client address reached telemetry", workflow);
        Assert.Contains("evals/run-mcp-load.mjs", workflow);
        Assert.Contains("candidate exceeded the 75 percent memory budget", workflow);
        Assert.Contains("candidate load used more than one replica", workflow);
        Assert.Contains("revision_get() {", workflow);
        Assert.Contains("for attempt in $(seq 1 24)", workflow);
        Assert.Contains("--silent --show-error --connect-timeout 5 --max-time 10 \"$url\"", workflow);
        Assert.Contains("revision endpoint did not return a successful response", workflow);
        Assert.Equal(2, Regex.Matches(workflow, Regex.Escape(
            "revision_get \"https://$rollback_fqdn/")).Count);
        Assert.Equal(2, Regex.Matches(workflow, Regex.Escape(
            "revision_get \"https://$fqdn/")).Count);
        Assert.Equal(2, Regex.Matches(workflow, Regex.Escape(
            "{ [ \"$rollback_state\" = \"Running\" ] || [ \"$rollback_state\" = \"RunningAtMaxScale\" ]; }")).Count);
        Assert.Equal(2, Regex.Matches(workflow, Regex.Escape(
            "{ [ \"$state\" = \"Running\" ] || [ \"$state\" = \"RunningAtMaxScale\" ]; }")).Count);
        Assert.Contains("current-user injection canary reached the reply", workflow);
        Assert.Contains("restored-transcript injection canary reached the reply", workflow);
        Assert.Contains("revision deactivate", workflow);
        Assert.Contains("Enforce one active public quota authority", workflow);
        Assert.Contains("failed to deactivate non-authoritative revision", workflow);
        Assert.Contains("always() && steps.candidate.outcome != 'skipped'", workflow);
        Assert.Contains("expected exactly one active public quota authority", workflow);
        Assert.True(workflow.IndexOf("deactivate_revision()", StringComparison.Ordinal)
                    < workflow.IndexOf("trap finish_cleanup EXIT", StringComparison.Ordinal));
    }

    [Fact]
    public void Promotion_and_rollback_use_an_exact_revision_and_compare_current_traffic()
    {
        var workflow = File.ReadAllText(
            Path.Combine(RepoRoot(), ".github", "workflows", "revision-traffic.yml"));

        Assert.Contains("options:", workflow);
        Assert.Contains("- promote", workflow);
        Assert.Contains("- rollback", workflow);
        Assert.Contains("expected_current_revision", workflow);
        Assert.Contains("rollback_revision", workflow);
        Assert.Contains("target revision did not become ready", workflow);
        Assert.Single(Regex.Matches(workflow, Regex.Escape(
            "{ [ \"$state\" = \"Running\" ] || [ \"$state\" = \"RunningAtMaxScale\" ]; }")));
        Assert.Equal(2, Regex.Matches(workflow, Regex.Escape(
            "{ [ \"$target_state\" = \"Running\" ] || [ \"$target_state\" = \"RunningAtMaxScale\" ]; }")).Count);
        Assert.Contains("Activate the exact candidate for bounded verification", workflow);
        Assert.Contains("assistant-browser-evidence.json", workflow);
        Assert.Contains("properties.fqdn", workflow);
        Assert.Contains("TARGET_FQDN", workflow);
        Assert.Contains("target must have exactly one pinned replica", workflow);
        Assert.Contains("rollback must have exactly one pinned replica", workflow);
        Assert.True(workflow.IndexOf("current revision changed", StringComparison.Ordinal)
                    < workflow.IndexOf("cleanup_target()", StringComparison.Ordinal));
        Assert.True(workflow.IndexOf("target must have exactly one pinned replica", StringComparison.Ordinal)
                    < workflow.IndexOf("revision activate", StringComparison.Ordinal));
        Assert.True(workflow.IndexOf("echo \"target=$TARGET_REVISION\"", StringComparison.Ordinal)
                    < workflow.IndexOf("revision activate", StringComparison.Ordinal));
        Assert.True(workflow.IndexOf("current revision changed before verification", StringComparison.Ordinal)
                    < workflow.IndexOf("echo \"target=$TARGET_REVISION\"", StringComparison.Ordinal));
        Assert.Contains("PREVIOUS_REVISION: ${{ steps.traffic.outputs.previous }}", workflow);
        Assert.Contains("steps.traffic.outputs.target || steps.candidate.outputs.target", workflow);
        Assert.Contains("failed to deactivate the candidate without changing traffic", workflow);
        Assert.DoesNotContain("steps.candidate.outputs.previous", workflow);
        Assert.Contains("refusing recovery with identical revisions", workflow);
        Assert.Contains("expected exactly one active public quota authority", workflow);
        Assert.Contains("name != '$ROLLBACK_REVISION' && properties.active", workflow);
        Assert.Contains("revision-weight", workflow);
        Assert.Contains("trap restore_previous ERR", workflow);
        Assert.Contains("trap restore_previous ERR TERM INT", workflow);
        Assert.Contains("for attempt in $(seq 1 5)", workflow);
        Assert.Contains("failed to deactivate and verify the target revision", workflow);
        Assert.Contains("target_active", workflow);
        Assert.Contains("failed to restore and verify the previous revision", workflow);
        Assert.Contains("prior_promotion_deployment", workflow);
        Assert.Contains("lex-revision-promotion", workflow);
        Assert.Contains("previous promotion receipt does not bind the target revision", workflow);
        Assert.Contains("previous promotion receipt does not bind the evaluation release", workflow);
        Assert.Contains("previous promotion receipt is not successful", workflow);
        Assert.Contains("--allow-older-previously-promoted-evidence", workflow);
        Assert.Contains("Record successful promotion receipt", workflow);
    }

    [Fact]
    public void Evaluation_lifecycle_accepts_Azure_healthy_max_scale_state()
    {
        var script = File.ReadAllText(Path.Combine(RepoRoot(), "evals", "run-assistant-eval.ps1"));

        Assert.Contains("$running -in @(\"Running\", \"RunningAtMaxScale\")", script);
    }

    [Fact]
    public void Public_workflows_pin_every_external_action_to_a_commit()
    {
        var workflows = Directory.GetFiles(Path.Combine(RepoRoot(), ".github", "workflows"), "*.yml");
        var unpinned = workflows
            .SelectMany(path => File.ReadLines(path).Select((line, index) => (path, line, index)))
            .Where(item => Regex.IsMatch(item.line,
                @"^\s*-?\s*uses:\s*[^./\s][^@\s]*@(?![0-9a-f]{40}(?:\s|$))",
                RegexOptions.IgnoreCase))
            .Select(item => $"{Path.GetFileName(item.path)}:{item.index + 1}: {item.line.Trim()}")
            .ToArray();

        Assert.Empty(unpinned);
    }

    [Fact]
    public void Publisher_can_only_toggle_candidate_revision_lifecycle_not_runtime_configuration()
    {
        var terraform = File.ReadAllText(Path.Combine(RepoRoot(), "infra", "main.tf"));
        var start = terraform.IndexOf(
            "resource \"azurerm_role_definition\" \"publisher_revision_lifecycle\"",
            StringComparison.Ordinal);
        var end = terraform.IndexOf(
            "resource \"azurerm_role_assignment\" \"publisher_revision_lifecycle\"",
            start, StringComparison.Ordinal);

        Assert.True(start >= 0 && end > start);
        var role = terraform[start..end];
        Assert.Contains("Microsoft.App/containerApps/revisions/activate/action", role);
        Assert.Contains("Microsoft.App/containerApps/revisions/deactivate/action", role);
        Assert.DoesNotContain("Microsoft.App/containerApps/write", role);
        Assert.DoesNotContain("Microsoft.App/containerApps/*", role);
        Assert.DoesNotContain("Microsoft.App/*", role);
        Assert.Contains("scope              = local.container_app_id", terraform[end..]);
    }

    [Fact]
    public void Deployment_can_verify_retention_without_broad_telemetry_access()
    {
        var terraform = File.ReadAllText(Path.Combine(RepoRoot(), "infra", "main.tf"));
        var start = terraform.IndexOf(
            "resource \"azurerm_role_definition\" \"deploy_application_insights_metadata_reader\"",
            StringComparison.Ordinal);
        Assert.True(start >= 0, "Application Insights metadata role is missing.");
        var end = terraform.IndexOf(
            "resource \"azurerm_role_assignment\" \"deploy_application_insights_reader\"",
            start, StringComparison.Ordinal);

        Assert.True(end > start, "Application Insights metadata assignment is missing.");
        var role = terraform[start..end];
        var actionsStart = role.IndexOf("actions = [", StringComparison.Ordinal);
        Assert.True(actionsStart >= 0, "Telemetry-retention role actions are missing.");
        var actionsEnd = role.IndexOf(']', actionsStart);
        Assert.True(actionsEnd > actionsStart, "Telemetry-retention role actions are incomplete.");
        var actions = role[actionsStart..actionsEnd];
        var actionEntries = Regex.Matches(actions, "\"([^\"]+)\"")
            .Select(match => match.Groups[1].Value)
            .ToArray();
        Assert.Equal(new[] { "Microsoft.Insights/components/read" }, actionEntries);
        Assert.Contains("scope       = data.azurerm_resource_group.platform.id", role);
        Assert.Contains("assignable_scopes = [data.azurerm_resource_group.platform.id]", role);
        Assert.Contains("scope              = data.azurerm_application_insights.web.id", terraform[end..]);

        start = terraform.IndexOf(
            "resource \"azurerm_role_definition\" \"deploy_log_analytics_table_policy_reader\"",
            StringComparison.Ordinal);
        Assert.True(start >= 0, "Log Analytics table-policy role is missing.");
        end = terraform.IndexOf(
            "resource \"azurerm_role_assignment\" \"deploy_log_analytics_table_reader\"",
            start, StringComparison.Ordinal);
        Assert.True(end > start, "Log Analytics table-policy assignment is missing.");
        role = terraform[start..end];
        actionsStart = role.IndexOf("actions = [", StringComparison.Ordinal);
        Assert.True(actionsStart >= 0, "Log Analytics role actions are missing.");
        actionsEnd = role.IndexOf(']', actionsStart);
        Assert.True(actionsEnd > actionsStart, "Log Analytics role actions are incomplete.");
        actions = role[actionsStart..actionsEnd];
        actionEntries = Regex.Matches(actions, "\"([^\"]+)\"")
            .Select(match => match.Groups[1].Value)
            .ToArray();
        Assert.Equal(new[] { "Microsoft.OperationalInsights/workspaces/tables/read" }, actionEntries);
        Assert.Contains("scope       = \"/subscriptions/${var.subscription_id}\"", role);
        Assert.Contains("assignable_scopes = [\"/subscriptions/${var.subscription_id}\"]", role);
        Assert.DoesNotContain("log_analytics_resource_group_id", terraform);
        Assert.Contains("scope              = data.azurerm_application_insights.web.workspace_id", terraform[end..]);
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
