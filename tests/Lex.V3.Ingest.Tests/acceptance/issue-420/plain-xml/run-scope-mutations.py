from pathlib import Path
import subprocess, json, hashlib, sys

root = Path.cwd()
out = root / 'artifacts/issue-420-xml'
scope = 'src/Lex.V3.Ingest/Luxembourg/LuxembourgScopePartitionFamilies.cs'
adapter = 'src/Lex.V3.Ingest/Luxembourg/LuxembourgQueryExecutionAdapter.cs'
holder = 'src/Lex.V3.Contracts/Source/Luxembourg/LuxembourgProvenResourceObservations.cs'
invalid = 'FullyQualifiedName~ADeclaredScopeMustHaveAlignedDistinctMembersBeforeAnyTraffic'
positive = 'FullyQualifiedName~TwoDisjointDeclaredScopesContributeTheirOwnRowsAndAllRelationProofs'
mutants = [
 ('scope-count', scope, 'if (members.Count == 0 || families.Count != checked(members.Count * 3))', 'if (false)', invalid),
 ('scope-set', scope, 'if (request.SetId != set)', 'if (false)', invalid),
 ('scope-plan', scope, 'if (plan != commonPlan)', 'if (false)', invalid),
 ('scope-alignment', scope, 'if (range.StartInclusive != request.Partition.StartInclusive || range.EndExclusive != request.Partition.EndExclusive)', 'if (false)', invalid),
 ('scope-overlap', scope, 'if (ranges[index - 1].EndExclusive.CompareTo(ranges[index].StartInclusive) > 0)', 'if (false)', invalid),
 ('scope-whole-subject', scope, 'if (cursor.Key2.Length != 0 || cursor.Key3.Length != 0 || cursor.Key4.Length != 0 ||\n                        cursor.Key5.Length != 0 || cursor.Key6.Length != 0)', 'if (false)', invalid),
 ('scope-cover-root', scope, 'if (cover.RootRange != request.Partition)', 'if (false)', invalid),
 ('scope-cover-key', scope, 'if (leaf != cover.RootRange && !proofKeys.Add(leaf.PartitionId))', 'if (false)', invalid),
 ('assertion-profile', holder, 'if (proofs.Any(proof => proof.SourceProfileRef.Sha256 != proofs[0].SourceProfileRef.Sha256 ||\n            proof.InterpretationProfileRef.Sha256 != proofs[0].InterpretationProfileRef.Sha256))', 'if (false)', positive),
 ('relation-profile', adapter, 'if (copy.Any(proof => proof.SourceProfileRef.Sha256 != copy[0].SourceProfileRef.Sha256 ||\n            proof.InterpretationProfileRef.Sha256 != copy[0].InterpretationProfileRef.Sha256))', 'if (false)', positive),
 ('relation-reopen', adapter, 'if (relationRows is null)', 'if (relationRows is null && relationLegs.Count == -1)', 'FullyQualifiedName~ProvenRelationMembersWhoseCustodyFailsReopeningCannotRemainComplete'),
]
originals = {name: (root/name).read_bytes() for name in (scope, adapter, holder)}
results=[]
try:
 for name, path, old, new, test_filter in mutants:
  if len(sys.argv)>1 and name not in sys.argv[1:]: continue
  source = originals[path].decode('utf-8-sig').replace('\r\n','\n')
  assert source.count(old)==1, (name, source.count(old))
  (root/path).write_text(source.replace(old,new),encoding='utf-8')
  command=['dotnet','test','tests/Lex.V3.Ingest.Tests/Lex.V3.Ingest.Tests.csproj','-c','Release','--no-restore','--filter',test_filter]
  with (out/(name+'-mutation.log')).open('wb') as log:
   run=subprocess.run(command,stdout=log,stderr=subprocess.STDOUT)
  (root/path).write_bytes(originals[path])
  # Exit 2 is a test failure; compilation failures do not count as a caught mutant.
  result={'mutant':name,'command':command,'exitCode':run.returncode,'caughtByExecutedTest':run.returncode==2}
  results.append(result); print(json.dumps(result),flush=True)
  if run.returncode!=2: raise RuntimeError('Mutant survived or did not execute: '+name)
finally:
 for path,data in originals.items(): (root/path).write_bytes(data)
 (out/('scope-mutations'+('-selected' if len(sys.argv)>1 else '')+'.json')).write_text(json.dumps({'results':results,'restored':{path:hashlib.sha256((root/path).read_bytes()).hexdigest() for path in originals}},indent=2),encoding='utf-8')
