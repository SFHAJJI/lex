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
        'LAUNCH-CONTRACT.md',
        'Lex.V3.slnx',
        'LICENSE',
        'README.md',
        'SECURITY.md',
        'STATUS.md',
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
        $normalized -ceq 'eng/test-fast.ps1' -or
        $normalized -ceq 'eng/verify-s0-05-preview.ps1' -or
        $normalized -cmatch '^schemas/v3-[a-z0-9-]+/[a-z0-9-]+\.schema\.json$' -or
        # The two censuses: real payloads and real answers the platform sends, not schemas, so each is
        # admitted by its exact path and nothing else in that directory is.
        $normalized -ceq 'schemas/v3-platform/refusal-payload-samples.json' -or
        $normalized -ceq 'schemas/v3-platform/answer-samples.json' -or
        $normalized -cmatch '^schemas/v3-source/core/[a-z0-9-]+\.schema\.json$' -or
        $normalized -cmatch '^schemas/v3-source/http/[a-z0-9-]+\.json$' -or
        $normalized -cmatch '^src/Lex\.V3\.[A-Za-z0-9.]+/.+$' -or
        $normalized -cmatch '^tests/Lex\.V3\.[A-Za-z0-9.]+/.+$' -or
        $normalized -ceq 'web/.gitignore' -or
        $normalized -cmatch '^web/acceptance/issue-368/(?:README\.md|sources\.sha256|[a-z0-9-]+\.(?:log|mjs))$' -or
        $normalized -cmatch '^web/app/[A-Za-z0-9.-]+\.(?:jsx|mjs)$' -or
        $normalized -cmatch '^web/package(?:-lock)?\.json$' -or
        $normalized -cmatch '^web/scripts/[a-z0-9.-]+\.mjs$' -or
        $normalized -cmatch '^web/src/fonts/[a-z0-9-]+\.woff2$' -or
        $normalized -cmatch '^web/src/[a-z0-9.-]+\.(?:css|html|svg)$' -or
        $normalized -cmatch '^web/test/[a-z0-9.-]+\.test\.mjs$'
    )
}

function Test-BootDocument {
    param([Parameter(Mandatory)][AllowEmptyString()][string]$Text)

    # The boot names the two files a session reads first and the baseline branch, and carries no
    # pointer to the retired out-of-repository bundles or the retired seat protocol.
    $required = @('STATUS.md', 'LAUNCH-CONTRACT.md', 'v3/integration')
    $forbidden = @(
        '12C302017CE9B48750115FB638A217B4D562581216AB0E3B5557A6E659C4EF0F',
        'out-of-repository authority bundle',
        'pass its quiz',
        'REVIEW REQUEST'
    )

    return (
        -not $required.Where({ -not $Text.Contains($_, [StringComparison]::Ordinal) }) -and
        -not $forbidden.Where({ $Text.Contains($_, [StringComparison]::OrdinalIgnoreCase) })
    )
}

function Test-CanonicalInstruction {
    param([Parameter(Mandatory)][string]$Text)

    $required = @(
        'STATUS.md',
        'LAUNCH-CONTRACT.md',
        'v3/integration',
        'legacy line',
        'implemented and accepted',
        'implemented but unaccepted',
        'incorrect',
        'missing'
    )
    $forbidden = @(
        '12C302017CE9B48750115FB638A217B4D562581216AB0E3B5557A6E659C4EF0F',
        'd43366e73d22b80f2ad2b9c08767806778354b5362f895bfc77068e298326020',
        '9AC4F7787C55D7B7E8104DB754A728F8C9979EDC98A886CD3A8CC7965D714A5F',
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
    'schemas/v3-source/core/source-profile-topology.schema.json',
    'schemas/v3-source/http/http-acquisition-reason-registry.json',
    'web/.gitignore',
    'web/acceptance/issue-368/README.md',
    'web/acceptance/issue-368/sources.sha256',
    'web/acceptance/issue-368/mutations.mjs',
    'web/acceptance/issue-368/red.log',
    'web/app/index.jsx',
    'web/app/render-document.mjs',
    'web/src/fonts/inter-400-latin.woff2'
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
    'schemas/v3-source/http/nested/http-acquisition-reason-registry.json',
    'schemas/v3-source/http/http-acquisition-reason-registry.yaml',
    'web/src/App.tsx',
    'web/.env',
    'web/acceptance/issue-368/.env',
    'web/acceptance/issue-368/nested/red.log',
    'web/acceptance/issue-369/red.log',
    'web/acceptance/issue-368/red.log.bak',
    'web/acceptance/issue-368/app.jsx',
    'web/acceptance/issue-368/../red.log',
    'web/app/App.tsx',
    'web/app/nested/App.jsx',
    'web/src/fonts/font.ttf',
    'web/src/fonts/nested/font.woff2'
)
if ($pathMutations.Where({ Test-V3TrackedPath -Path $_ })) {
    throw 'A legacy path mutation escaped the V3 structural allowlist.'
}

$instructionPath = Join-Path $repositoryRoot 'V3-INSTRUCTIONS.md'
$instructionText = Get-Content -LiteralPath $instructionPath -Raw
$agentsText = Get-Content -LiteralPath (Join-Path $repositoryRoot 'AGENTS.md') -Raw
$claudeText = Get-Content -LiteralPath (Join-Path $repositoryRoot 'CLAUDE.md') -Raw
foreach ($name in @('STATUS.md', 'LAUNCH-CONTRACT.md')) {
    if (-not (Test-Path -LiteralPath (Join-Path $repositoryRoot $name) -PathType Leaf)) {
        throw "$name is missing; the boot points a session at it."
    }
}

if (-not (Test-CanonicalInstruction -Text $instructionText)) {
    throw 'V3-INSTRUCTIONS.md is missing a required pointer or contains a stale authority.'
}
if (-not (Test-BootDocument -Text $agentsText)) {
    throw 'AGENTS.md does not point at STATUS.md and LAUNCH-CONTRACT.md, or carries a retired pointer.'
}
if (-not (Test-BootDocument -Text $claudeText)) {
    throw 'CLAUDE.md does not point at STATUS.md and LAUNCH-CONTRACT.md, or carries a retired pointer.'
}

# The checks above can fail: each mutation below must be rejected.
if (Test-BootDocument -Text '') {
    throw 'The empty-boot mutation did not fail.'
}
if (Test-BootDocument -Text ($agentsText + "`n12C302017CE9B48750115FB638A217B4D562581216AB0E3B5557A6E659C4EF0F")) {
    throw 'The stale-authority-pointer mutation did not fail.'
}
if (Test-BootDocument -Text ($agentsText + "`nPost a REVIEW REQUEST on the issue.")) {
    throw 'The retired-protocol mutation did not fail.'
}
if (Test-CanonicalInstruction -Text ($instructionText + "`ndeploy/indexes")) {
    throw 'The stale-path mutation did not fail.'
}
if (Test-CanonicalInstruction -Text ($instructionText + "`nSchema authority: lex-index/2")) {
    throw 'The old-schema mutation did not fail.'
}

Write-Host "V3 tree and boot documents verified across $($trackedPaths.Count) tracked paths."
