# Bounded custody replay

Run `Lex.V3.Custody.Probe replay <input-sha256>` with the same managed-identity
environment as the existing probe. The argument must be one lowercase SHA-256.
The command is read-only and opens the input and every named artifact through the
configured custody store. It accepts no storage secret, URL, local path or alternate
authentication mode.

The retained input is strict UTF-8 JSON with exactly these fields:

```json
{"schema":"lex-v3-custody-replay-input/1","facts":["<fact-content-sha256>"],"routes":["<canonical-http-evidence-content-sha256>"]}
```

The angle-bracket strings above are placeholders, not accepted digest syntax. Each
list must contain 1–1,000 distinct digests. Producers retain these input bytes and
the named documents using the existing custody API before invoking replay.

Facts are exact retained JSON documents read through the existing contracts:
`PublisherDateFact`, `PublisherRelation`, `DerivedInverseRelation`,
`LocalInboundView`, or `RelationFact`. Inverses require both their forward
assertion and observed ontology axiom. Inbound views require every contributor
and reopen their scope descriptor bytes. A scope descriptor's successful byte
reopen does not establish its semantic validity or completeness. A bare
`PublisherDate` has no provenance; vocabulary drift remains diagnostic evidence.
Neither is accepted as a Fact input.

Routes use the existing canonical `lex-license-http-evidence/4` reader. Every
observation identity must be unique across the supplied routes. For every hop,
replay opens the retained write receipt, checks its canonical digest and its
agreement with the hop's body digest and length, then restores those exact bytes
through the receipt's durable reference. Extra rejected and partial observations
are reopened too. A Fact reference to non-derivable transport or an incomplete
route fails. Reopening a response never decodes it into legal text.

The result records the input digest, each Fact's member-to-observation links,
route/receipt coordinates, HTTP disposition, body completion and the **recorded
write receipt**. It always reports `acceptance_established: false`,
`population_completeness_established: false`, and `current_retention_established:
false`. This checks the supplied byte/provenance graph; it does not authenticate
publisher claims, validate extracted semantics, establish that the supplied set
is the whole accepted population, or re-attest historical policy evidence.

No result is written until the entire input succeeds. Existing custody exceptions
remain typed internally, including their provider causes; the console preserves
the existing fixed failure message and nonzero exit behavior. Cancellation is
passed through every reopen.

Metadata decoding is bounded to 8 MiB per artifact, with at most 10,000 route
observations and 512 MiB cumulative reopened bytes. The cumulative count includes
repeat opens, not only distinct content. The whole-object custody API retains its
own 256 MiB object bound; the metadata limit does not cap the provider's first
allocation. These are operational probe limits, not a definition of the accepted
Fact population.

Production acceptance still requires a real, explicitly authorized accepted
population and real managed-identity retention/restore evidence. Synthetic tests
cannot establish it. The separately prepared deployment at `010eab6b` pins an
image built from unchanged `e305791e` probe code; **that image does not include
this replay command**. Adding replay to an Azure operation needs its own inspected
image digest and exact-head review. Do not substitute a rebuilt image into the
pending deployment proposal.
