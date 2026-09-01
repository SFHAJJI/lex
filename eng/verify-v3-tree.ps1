[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path

function Test-V3TrackedPath {
    param([Parameter(Mandatory)][string]$Path)

    $normalized = $Path.Replace('\', '/')
    $rootFiles = @(
        '.dockerignore',
        '.gitattributes',
        '.gitignore',
        'AGENTS.md',
        'CLAUDE.md',
        'Directory.Build.props',
        'global.json',
        'Lex.V3.slnx',
        'LICENSE',
        'README.md',
        'SECURITY.md',
        'V3-INSTRUCTIONS.md'
    )

    if ($rootFiles -ccontains $normalized) {
        return $true
    }

    return (
        $normalized -ceq '.github/workflows/v3-ci.yml' -or
        $normalized -ceq '.github/workflows/dual-review.yml' -or
        $normalized -ceq '.github/scripts/dual_review.py' -or
        $normalized -ceq '.github/scripts/test_dual_review.py' -or
        $normalized -cmatch '^eng/verify-v3-[a-z0-9-]+\.ps1$' -or
        $normalized -ceq 'eng/verify-s0-05-preview.ps1' -or
        $normalized -cmatch '^schemas/v3-[a-z0-9-]+/[a-z0-9-]+\.schema\.json$' -or
        $normalized -cmatch '^schemas/v3-source/core/[a-z0-9-]+\.schema\.json$' -or
        $normalized -cmatch '^src/Lex\.V3\.[A-Za-z0-9.]+/.+$' -or
        $normalized -cmatch '^tests/Lex\.V3\.[A-Za-z0-9.]+/.+$' -or
        $normalized -cmatch '^web/package(?:-lock)?\.json$' -or
        $normalized -cmatch '^web/scripts/[a-z0-9.-]+\.mjs$' -or
        $normalized -cmatch '^web/src/[a-z0-9.-]+\.(?:css|html|svg)$' -or
        $normalized -cmatch '^web/test/[a-z0-9.-]+\.test\.mjs$'
    )
}

function Test-InstructionPointer {
    param(
        [Parameter(Mandatory)][AllowEmptyString()][string]$Text,
        [Parameter(Mandatory)][string]$ExpectedDigest,
        [Parameter(Mandatory)][ValidateSet('Codex', 'Claude')][string]$Agent
    )

    return (
        $Text.Contains('/V3-INSTRUCTIONS.md', [StringComparison]::Ordinal) -and
        $Text.Contains($ExpectedDigest, [StringComparison]::OrdinalIgnoreCase) -and
        $Text.Contains('fresh or compacted session', [StringComparison]::OrdinalIgnoreCase) -and
        $Text.Contains('boot sequence', [StringComparison]::OrdinalIgnoreCase) -and
        $Text.Contains('quiz', [StringComparison]::OrdinalIgnoreCase) -and
        $Text.Contains($Agent, [StringComparison]::Ordinal)
    )
}

function Test-CanonicalInstruction {
    param([Parameter(Mandatory)][string]$Text)

    $required = @(
        '12C302017CE9B48750115FB638A217B4D562581216AB0E3B5557A6E659C4EF0F',
        'd43366e73d22b80f2ad2b9c08767806778354b5362f895bfc77068e298326020',
        'Decision 55',
        '9AC4F7787C55D7B7E8104DB754A728F8C9979EDC98A886CD3A8CC7965D714A5F',
        'V3 product, source, integration, and release repository',
        'no accepted production data manifest exists',
        'one accountable writer',
        'Decision 43'
    )
    $forbidden = @(
        'deploy/indexes',
        'lex-index/2',
        'corpus/5',
        'canon/1'
    )

    return (
        -not $required.Where({ -not $Text.Contains($_, [StringComparison]::OrdinalIgnoreCase) }) -and
        -not $forbidden.Where({ $Text.Contains($_, [StringComparison]::OrdinalIgnoreCase) })
    )
}

$trackedPaths = @(git -C $repositoryRoot ls-files)
if ($LASTEXITCODE -ne 0) {
    throw 'Unable to enumerate tracked files.'
}

$violations = @($trackedPaths.Where({ -not (Test-V3TrackedPath -Path $_) }))
if ($violations.Count -gt 0) {
    throw "Paths outside the V3 structural allowlist remain:`n$($violations -join "`n")"
}

$requiredV3Paths = @(
    '.github/scripts/dual_review.py',
    '.github/scripts/test_dual_review.py',
    '.github/workflows/dual-review.yml',
    'schemas/v3-facts/facts-common.schema.json',
    'schemas/v3-facts/publisher-relation.schema.json',
    'schemas/v3-facts/derived-inverse-relation.schema.json',
    'schemas/v3-facts/local-inbound-view.schema.json',
    'schemas/v3-facts/relation-fact.schema.json',
    'schemas/v3-facts/publisher-date.schema.json',
    'schemas/v3-facts/publisher-date-fact.schema.json',
    'schemas/v3-facts/vocabulary-drift.schema.json',
    'schemas/v3-source/core/source-common.schema.json',
    'schemas/v3-source/core/source-object-ref.schema.json',
    'schemas/v3-source/core/source-profile-topology.schema.json'
)
if ($requiredV3Paths.Where({ -not (Test-V3TrackedPath -Path $_) })) {
    throw 'A required bounded V3 path was rejected by the structural allowlist.'
}

$pathMutations = @(
    'src/Lex.Ingest/Legacy.cs',
    'tests/Lex.Tests/LegacyTests.cs',
    '.github/workflows/deploy.yml',
    '.github/workflows/dual-review-copy.yml',
    '.github/scripts/dual-review.py',
    '.github/scripts/nested/dual_review.py',
    '.github/scripts/dual_review.ps1',
    'schemas/v2-facts/facts-common.schema.json',
    'schemas/v3-Facts/facts-common.schema.json',
    'schemas/v3-facts/Nested.schema.json',
    'schemas/v3-facts/nested/facts-common.schema.json',
    'schemas/v3-facts/facts-common.json',
    'schemas/v3-facts/facts-common.schema.json.bak',
    'schemas/v3-Source/core/source-common.schema.json',
    'schemas/v3-source/core/nested/source-common.schema.json',
    'schemas/v3-facts/core/source-common.schema.json',
    'schemas/v3-source/other/source-common.schema.json',
    'schemas/v3-source/core/source-common.schema.yaml',
    'web/src/App.tsx'
)
if ($pathMutations.Where({ Test-V3TrackedPath -Path $_ })) {
    throw 'A legacy path mutation escaped the V3 structural allowlist.'
}

$instructionPath = Join-Path $repositoryRoot 'V3-INSTRUCTIONS.md'
$instructionText = Get-Content -LiteralPath $instructionPath -Raw
$instructionDigest = (Get-FileHash -LiteralPath $instructionPath -Algorithm SHA256).Hash
$agentsText = Get-Content -LiteralPath (Join-Path $repositoryRoot 'AGENTS.md') -Raw
$claudeText = Get-Content -LiteralPath (Join-Path $repositoryRoot 'CLAUDE.md') -Raw

if (-not (Test-CanonicalInstruction -Text $instructionText)) {
    throw 'The canonical V3 instruction is missing an authority binding or contains a stale authority.'
}
if (-not (Test-InstructionPointer -Text $agentsText -ExpectedDigest $instructionDigest -Agent Codex)) {
    throw 'AGENTS.md does not bind the canonical V3 instruction and Codex boot action.'
}
if (-not (Test-InstructionPointer -Text $claudeText -ExpectedDigest $instructionDigest -Agent Claude)) {
    throw 'CLAUDE.md does not bind the canonical V3 instruction and Claude boot action.'
}

$wrongDigest = '0' * 64
if (Test-InstructionPointer -Text $agentsText -ExpectedDigest $wrongDigest -Agent Codex) {
    throw 'The wrong-digest mutation did not fail.'
}
if (Test-InstructionPointer -Text '' -ExpectedDigest $instructionDigest -Agent Codex) {
    throw 'The missing-pointer mutation did not fail.'
}
if (Test-CanonicalInstruction -Text ($instructionText + "`ndeploy/indexes")) {
    throw 'The stale-path mutation did not fail.'
}
if (Test-CanonicalInstruction -Text ($instructionText + "`nSchema authority: lex-index/2")) {
    throw 'The old-schema mutation did not fail.'
}

Write-Host "V3 tree and instruction boundary verified across $($trackedPaths.Count) tracked paths."
