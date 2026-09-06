$ErrorActionPreference = 'Stop'
$source = Join-Path $PWD 'src/Lex.V3.Custody.Probe/FactCustodyReplay.cs'
$original = [IO.File]::ReadAllText($source)
$utf8 = [Text.UTF8Encoding]::new($false)
$cases = @(
    @{Name='receipt-canonical'; Find='DurableBlobWriteReceiptDigest.Of(receipt) != hop.DurableWriteReceiptSha256'; Replace='false'; Filter='IndependentlyHeldReceiptMustAgreeWithHop'},
    @{Name='receipt-body-digest'; Find='receipt.Reference.ContentSha256 != hop.Sha256'; Replace='false'; Filter='IndependentlyHeldReceiptMustAgreeWithHop'},
    @{Name='receipt-body-length'; Find='checked((ulong)receipt.Reference.ByteLength) != hop.Length'; Replace='false'; Filter='IndependentlyHeldReceiptMustAgreeWithHop'},
    @{Name='derivation-refusal'; Find='if (!observation.DerivableTransport)'; Replace='if (false)'; Filter='ReplayRefusesBrokenPopulationLinks'},
    @{Name='inverse-ontology'; Find='provenance.Add(new(path + "authorizing_axiom.source_observation_id", inverse.AuthorizingAxiom.SourceObservationId));'; Replace=''; Filter='InverseRequiresBothForwardAndOntologyObservations'},
    @{Name='inbound-contributors'; Find='index < inbound.ContributingAssertions.Count'; Replace='index < Math.Min(1, inbound.ContributingAssertions.Count)'; Filter='InboundViewRequiresEveryContributorAndItsScopeBytes'},
    @{Name='scope-reopen'; Find='_ = await ReadMetadata(scope).ConfigureAwait(false);'; Replace=''; Filter='InboundViewRequiresEveryContributorAndItsScopeBytes'},
    @{Name='empty-population'; Find='(requireNonempty && digests.Length == 0)'; Replace='false'; Filter='ReplayRefusesBrokenPopulationLinks'}
)
$results = @()
foreach ($case in $cases) {
    if (!$original.Contains($case.Find)) { throw "Missing mutation anchor: $($case.Name)" }
    try {
        [IO.File]::WriteAllText($source, $original.Replace($case.Find, $case.Replace), $utf8)
        $log = Join-Path $PWD "artifacts/issue-459-runner/replay-mutation-$($case.Name).txt"
        dotnet test --project tests/Lex.V3.Tests/Lex.V3.Tests.csproj --configuration Debug --no-restore --filter "FullyQualifiedName~$($case.Filter)" --minimum-expected-tests 1 > $log 2>&1
        $testExit = $LASTEXITCODE
        $results += [pscustomobject]@{name=$case.Name;filter=$case.Filter;exit=$testExit;log_sha256=(Get-FileHash -LiteralPath $log -Algorithm SHA256).Hash.ToLowerInvariant()}
        $results[-1] | ConvertTo-Json -Compress
        if ($testExit -ne 2) { throw "Mutation did not produce test failure: $($case.Name) exit $testExit" }
    }
    finally { [IO.File]::WriteAllText($source, $original, $utf8) }
}
[IO.File]::WriteAllText((Join-Path $PWD 'artifacts/issue-459-runner/replay-mutations.json'), ($results | ConvertTo-Json), $utf8)