# Evals for the /ask agent (spec: the AI constructs MCP tool arguments from natural questions).
# Usage:  .\run-evals.ps1 [-BaseUrl http://localhost:5099]
# Requires the target Lex.Web to run with AOAI_* and LEX_PUBLIC_BASE_URL configured.
param([string]$BaseUrl = "http://localhost:5099")

$cases = (Get-Content "$PSScriptRoot\cases.json" -Raw -Encoding UTF8 | ConvertFrom-Json).cases
$failures = 0
$results = @()

foreach ($case in $cases) {
    $body = @{ messages = @(@{ role = "user"; content = $case.question }) } | ConvertTo-Json -Depth 5
    try {
        $r = Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/ask" -ContentType "application/json" -Body ([System.Text.Encoding]::UTF8.GetBytes($body)) -TimeoutSec 300
    } catch {
        $results += [pscustomobject]@{ id = $case.id; ok = $false; detail = "HTTP error: $($_.Exception.Message)" }
        $failures++; continue
    }
    $problems = @()
    $trace = @($r.trace)
    $argsFlat = ($trace | ForEach-Object { "$($_.tool) $((($_.args | ConvertTo-Json -Compress -Depth 5)))" }) -join " `n"

    # 1. Expected tool was called
    $tools = @($trace | ForEach-Object tool)
    if ($case.expect_tool -and $tools -notcontains $case.expect_tool) { $problems += "tool '$($case.expect_tool)' not called (called: $($tools -join ','))" }
    if ($case.expect_tool_any -and -not (@($case.expect_tool_any) | Where-Object { $tools -contains $_ })) { $problems += "none of [$($case.expect_tool_any -join ',')] called (called: $($tools -join ','))" }

    # 2. Expected argument fragments appear in some call's args
    foreach ($frag in @($case.expect_args_contain)) {
        if ($argsFlat -notmatch [regex]::Escape($frag)) { $problems += "arg fragment '$frag' not found in any tool call" }
    }

    # 3. Reply content sanity
    if ($case.expect_reply_contains_any) {
        $hit = @($case.expect_reply_contains_any) | Where-Object { $r.reply -match [regex]::Escape($_) }
        if (-not $hit) { $problems += "reply contains none of the expected markers" }
    }

    # 4. Refusal case: no evidence doc may predate the bound (the model must not fabricate early versions)
    if ($case.forbid_evidence_valid_from_before) {
        $bad = $trace | ForEach-Object { $_.docs } | Where-Object { $_.valid_from -and $_.valid_from -lt $case.forbid_evidence_valid_from_before }
        if ($bad) { $problems += "evidence doc predates $($case.forbid_evidence_valid_from_before)" }
    }

    # 5. Grounding: every URL in the reply must come from tool evidence (permalink) or an official publisher host
    $evidenceLinks = @($trace | ForEach-Object { $_.docs } | ForEach-Object { $_.permalink } | Where-Object { $_ })
    $officialHosts = @("legilux.public.lu", "data.legilux.public.lu", "eur-lex.europa.eu", "publications.europa.eu")
    $urls = [regex]::Matches($r.reply, 'https?://[^\s)"''<>\]]+') | ForEach-Object { $_.Value.TrimEnd('.', ',', ';') }
    foreach ($u in $urls) {
        $uHost = ([uri]$u).Host
        $grounded = ($evidenceLinks | Where-Object { $u.StartsWith($_) -or $_.StartsWith($u) }) -or ($officialHosts -contains $uHost)
        if (-not $grounded) { $problems += "ungrounded URL in reply: $u" }
    }

    $ok = $problems.Count -eq 0
    if (-not $ok) { $failures++ }
    $results += [pscustomobject]@{ id = $case.id; ok = $ok; detail = if ($ok) { "tools: $($tools -join '->')" } else { $problems -join " | " } }
}

$results | Format-Table -AutoSize -Wrap
Write-Host "$($cases.Count - $failures)/$($cases.Count) passed"
exit $failures
