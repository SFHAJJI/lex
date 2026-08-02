# Evals for the /ask agent (spec: the AI constructs MCP tool arguments from natural questions).
# Usage:  .\run-evals.ps1 [-BaseUrl http://localhost:5099] [-Only case-id[,case-id]]
# Requires the target Lex.Web to run with AOAI_* and LEX_PUBLIC_BASE_URL configured.
param([string]$BaseUrl = "http://localhost:5099", [string[]]$Only)

# Optional LLM-graded assertion: replaces brittle keyword markers on cases that carry a
# `grade_llm` rubric, when AOAI_ENDPOINT + AOAI_KEY are set. Returns $null on any failure
# so the harness silently falls back to the keyword markers (keyless CI still works).
function Invoke-LlmGrade([string]$Question, [string]$Reply, [string]$Rubric) {
    $ep = $env:AOAI_ENDPOINT; if ($ep) { $ep = $ep.TrimEnd('/') }
    if (-not $ep -or -not $env:AOAI_KEY) { return $null }
    $dep = if ($env:AOAI_CHAT_DEPLOYMENT) { $env:AOAI_CHAT_DEPLOYMENT } else { "gpt-5-mini" }
    $req = @{
        model = $dep
        reasoning_effort = "low"
        max_completion_tokens = 2000
        messages = @(
            @{ role = "system"; content = 'You grade answers from a legal point-in-time retrieval assistant. Judge ONLY against the rubric. Output strict JSON: {"pass": true|false, "reason": "<one sentence>"} and nothing else.' },
            @{ role = "user"; content = "RUBRIC:`n$Rubric`n`nQUESTION:`n$Question`n`nREPLY TO GRADE:`n$Reply" }
        )
    } | ConvertTo-Json -Depth 6
    try {
        $resp = Invoke-RestMethod -Method Post -Uri "$ep/openai/v1/chat/completions" -Headers @{ "api-key" = $env:AOAI_KEY } -ContentType "application/json" -Body ([System.Text.Encoding]::UTF8.GetBytes($req)) -TimeoutSec 120
        $json = [regex]::Match($resp.choices[0].message.content, '\{[\s\S]*\}').Value
        if (-not $json) { return $null }
        return $json | ConvertFrom-Json
    } catch { return $null }
}

$cases = (Get-Content "$PSScriptRoot\cases.json" -Raw -Encoding UTF8 | ConvertFrom-Json).cases
if ($Only) { $cases = @($cases | Where-Object { $Only -contains $_.id }) }
$failures = 0
$results = @()
$export = @()   # per-case {query,response,context} JSONL for evals/groundedness.py (Azure AI Foundry evaluators)

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

    # 3. Reply content sanity — LLM-graded when the case has a rubric and AOAI creds exist,
    #    keyword markers otherwise (and as fallback when the grader call fails).
    $graded = $false
    if ($case.grade_llm) {
        $g = Invoke-LlmGrade -Question $case.question -Reply $r.reply -Rubric $case.grade_llm
        if ($null -ne $g) {
            $graded = $true
            if (-not $g.pass) { $problems += "LLM grade fail: $($g.reason)" }
        }
    }
    if (-not $graded -and $case.expect_reply_contains_any) {
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

    # Evidence context for groundedness scoring: what the tools actually returned
    $ctx = ($trace | ForEach-Object { $_.docs } | Where-Object { $_ } | ForEach-Object {
        $quotes = @($_.pinpoints | ForEach-Object { "$($_.anchor): $($_.quote)" }) -join " "
        "$($_.title) [$($_.valid_from) -> $($_.valid_to)] $($_.permalink) $quotes"
    }) -join "`n"
    $export += (@{ id = $case.id; query = $case.question; response = "$($r.reply)"; context = $ctx } | ConvertTo-Json -Compress -Depth 4)
}

New-Item -ItemType Directory -Force "$PSScriptRoot\out" | Out-Null
[IO.File]::WriteAllLines("$PSScriptRoot\out\results.jsonl", $export, (New-Object Text.UTF8Encoding $false))
$results | Format-Table -AutoSize -Wrap
Write-Host "$($cases.Count - $failures)/$($cases.Count) passed  (evidence export: out\results.jsonl - score with groundedness.py)"
exit $failures
