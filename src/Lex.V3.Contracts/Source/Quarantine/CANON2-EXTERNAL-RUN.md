# canon/2 quarantined external-run and input specification

**Status: dependency preparation. Not an execution authorization.**

This document specifies the external run that produces the one input `canon/2` needs, and the exact
form in which V3 will accept its output. It authorizes nothing: no external run, no access to a
retired index, no publisher traffic, no signing, no publication, no promotion. Execution is
separately authorized, and the evidence such a run must return is specified in section 8.

It is written as an **admission contract** — what V3 accepts — never as a reader of another tool's
format. Nobody has pinned the external tool's output shape, and writing a parser against an unpinned
format is inventing that format. Where the two could differ, this document states V3's requirement
and marks the external side as the thing an authorized run must satisfy.

---

## 1. Why an external run exists at all

`canon/2` is not a V3-internal naming scheme. Decision 45 fixes it as **the migration namespace**:
every provision identity from the exact previously promoted signed indexes is either byte-identical
unchanged, or carries exactly one permanent `canonicalized_to` target at the same version key and
anchor. So canon/2's first input is a complete, faithful record of what the **previous** indexes
resolved public permalinks at.

Backlog Candidate 2 section 7.3, quoted verbatim in `QuarantinePriorCoordinateReproduction.cs:32`:

> "the V3 repository cannot contain or execute a V2 index reader" … "Remove the verifier from active
> worktrees and credentials after proof. It is absent from the final V3 tree."

Both constraints hold simultaneously, and that is the whole shape of the problem:

- canon/2 **requires** the prior coordinates;
- V3 **must not** contain or execute the reader that obtains them.

The resolution is not to waive canon/2 and not to smuggle a reader in. It is that the reading happens
**outside V3, twice, independently**, and V3 receives only a bounded coordinate-only artifact which it
verifies through machinery it already has.

Everything in sections 2–7 below already exists in this directory. This document does not propose
changing any of it. The one thing that does not exist is the wire format in section 5, and
`QuarantineInventoryCanonicalizer.cs:143-150` names its absence as a carried condition for D3-05.

---

## 2. The two external roles, and what makes them independent

Section 7.3 step 5 requires the inventory reproduced independently twice, *including one reviewer
run*. `QuarantineReproducerRole` pins exactly two members:

| role | meaning |
| --- | --- |
| `Primary` | the production walk |
| `IndependentReviewer` | a separate reviewer walk, never the same run counted twice |

`QuarantinedPriorCoordinateInventory.TryReconcile` enforces independence on **two axes**, and both
are refusals, not conventions:

- `ReproductionRolesNotDistinct` — the two reproductions must not share a role;
- `ReproducerIdentitiesNotDistinct` — they must not share a `ReproducerIdentity`, compared ordinally.

That second axis is what makes "independent" mean more than "called twice". An authorized run must
therefore carry **two distinct operator or tool identities**, and must not default them to a shared
constant.

### The trust boundary, stated exactly

**There are three actors, not two, and conflating the first two is the error this section exists to
prevent.** The tool that reads the retired index never signs; the identity that signs never reads it.

| actor | may do | must never do |
| --- | --- | --- |
| **Reproducer** — the quarantined read-only tool, run twice under two identities | pin the exact previously promoted signed index pair; read it; enumerate public coordinates; emit its own coordinate list | **sign anything**; write to any network; mutate its source; publish; promote; touch production credentials |
| **Reviewer signing identity** — the pinned `quarantine_reviewer` | reconcile the two coordinate lists; sign the reconciled inventory | read, open, pin or execute against the retired index at all |
| **V3** | parse the coordinate-only artifact; re-derive every digest; re-reconcile the two reproductions; verify the signature against a trusted key | contain, execute, vendor or link a reader of the retired index; read its schema; read its serializer; accept any byte payload of law text |

`QuarantineVerifierReceipt` is the **reproducer's** receipt, not the signer's, and it makes that row a
construction-time refusal rather than a promise: `operatedReadOnly = false` throws, and its own
message names section 7.3's prohibition on network writes, source mutation, **signing**, publication
and production access "from the tool it describes". A document whose receipt claimed the reproducer
signed would therefore be refused by the receipt's own constructor.

`QuarantineAttestation.Issuer`, whose role must be exactly `quarantine_reviewer`, is the **signer**.
Section 7.3 step 4 requires that dedicated review identity to sign; nothing gives it index access.

### How the reproductions reach the signer without widening the boundary

Each reproducer emits **only its own coordinate list** — the four bounded strings per coordinate
described in section 3, which by construction cannot carry bytes, a stream or a path. The signer
receives two such lists and nothing else: no index handle, no credentials, no serializer, no file
from the retired generation.

That is sufficient for it to do its job, because reconciliation is a pure function of the two lists
(section 4.1's ordering, then a digest comparison). The signer reconciles in order to know what it is
signing, and signs the result. **It never needs to see the index, so its not seeing it costs
nothing.**

The boundary therefore narrows at each hop rather than widening: the index is read only by the
reproducer, only coordinates leave it, and only a signature is added downstream.

---

## 3. What a coordinate is, and why it cannot carry law text

`PriorPublicCoordinate` is four bounded strings and nothing else:

| field | contract |
| --- | --- |
| `WorkKey` | the opaque prior `lex_id`. Bounded opaque token (below) |
| `Language` | validated by `RoutedHttpValidation.RequireLanguage` |
| `ValidFrom` | exact `yyyy-MM-dd`, parsed with `DateOnly.TryParseExact` under the invariant culture |
| `Anchor` | the publisher-minted fragment for a provision-level coordinate; **null** for a version-level one |

These four are exactly what the prior index keyed rows by: a version row key is
`"key|language|valid_from"`, and a provision row key is `"lex_id#anchor"`.

**A bounded opaque token** (`QuarantineCoordinateValidation.RequireOpaqueKey`) is non-blank, at most
512 characters, printable ASCII only (`!`–`~`), and rejects anything containing `..` or `://`,
starting with `works/` or containing `/works/`, or ending in `.xml`, `.html`, `.htm` or `.json`.

The structural guarantee is stronger than that validation and worth stating separately, because it is
the part that cannot be weakened by a future edit to a rule list: **the type has no field whose type
is `byte[]`, `Stream`, or a path.** There is no parameter, property or producer anywhere on it
through which content could travel. The rejection list is defence in depth on top of that absence,
not the guarantee itself.

An authorized run therefore returns **locations, never content**. The provision text that used to
live behind a permalink is not part of this artifact and must not be transported with it.

---

## 4. Ordering, domain separation and digests

All three are already fixed in code. An external producer must match them exactly or V3 refuses.

### 4.1 Canonical ordering

`PriorPublicCoordinateSet.Ordered` sorts by, in order: `WorkKey` (ordinal), `Language` (ordinal),
`ValidFrom` (ordinal), **anchor presence** (`null` sorts before non-null), then `Anchor` (ordinal).

This is why two independent walks that enumerated rows in different orders still reconcile: both
normalize to identical bytes. Two walks that disagree on *content* do not.

### 4.2 Coordinate-set canonical bytes

`PriorPublicCoordinateSet.CanonicalBytes` writes, for each coordinate in canonical order, four
length-prefixed fields and a newline:

```
<byteCount>:<value>|<byteCount>:<value>|<byteCount>:<value>|<byteCount>:<value>|\n
      work_key            language           valid_from          anchor-or-empty
```

The length prefix is the **UTF-8 byte count**, not the character count. The whole is UTF-8.

Two details that must not be "improved" by a producer:

- **No anchor-presence flag byte exists.** A version-level coordinate encodes its anchor as `0:|`
  and a provision-level one as `N:value|` with `N >= 1`; the anchor's own length prefix already
  distinguishes them, and an extra flag would encode the same fact twice and change the bytes.
- **Length prefixes are what prevent re-cutting.** No pair of adjacent fields can be re-split into a
  different pair that hashes identically.

`CanonicalSha256Hex` is the **lowercase** hex SHA-256 of those bytes.

### 4.3 Signature domain separation

`QuarantineInventoryCanonicalizer` prefixes its signable form with the domain line

```
lex-v3-quarantine-prior-coordinate-inventory-signature/1
```

followed by `name=value` records, each side length-prefixed by UTF-8 byte count:

```
<byteCount>:<name>=<byteCount>:<value>\n
```

The covered fields, **in the exact order the canonicalizer writes them**:

1. `schema` — `lex-v3-quarantined-prior-coordinate-inventory/1`
2. `count`
3. `coordinate_set_sha256`
4. `prior_index_pair_sha256`
5. `source_index_identity_ref.resource_id`
6. `source_index_identity_ref.sha256`
7. `verifier_receipt.verifier_identity`
8. `verifier_receipt.operated_read_only` — literally `true` or `false`
9. `verifier_receipt.produced_at_utc`
10. `attestation.issuer.issuer_id`
11. `attestation.issuer.key_id`
12. `primary.role`, `primary.reproducer_identity`
13. `independent_reviewer.role`, `independent_reviewer.reproducer_identity`
14. then, for each coordinate in canonical order, `coordinates[i].work_key`, `.language`,
    `.valid_from`, `.anchor` (empty string when null)

`count` and `coordinate_set_sha256` are **re-derived from the coordinates on every call**, never read
from the stored field. The issuer id and key id are covered so a valid signature cannot be detached
and re-attached to an attestation naming a different issuer or key. The signature itself is excluded,
because a signature cannot be part of the bytes it signs.

### 4.4 Attestation shape

| field | required value |
| --- | --- |
| `Purpose` | `quarantine_prior_coordinate_inventory` |
| `Algorithm` | `ECDSA-P256-SHA256` |
| `SignatureFormat` | `ieee-p1363` |
| `Signature` | exactly 86 characters, unpadded base64url (`A–Z a–z 0–9 - _`), decoding to 64 P1363 bytes |
| `Issuer.Role` | exactly `quarantine_reviewer` |

There is deliberately **no encode helper in V3**. Producing a signature is the external tool's job;
only checking one is V3's.

---

## 5. The wire format V3 will accept — the part that does not exist yet

`QuarantineInventoryCanonicalizer.cs:143-150` states that no canonical-bytes form of a quarantined
inventory exists anywhere in V3, and that the external tool's output shape and the parser for it are
the carried condition for D3-05.

This section specifies **what V3 will accept**. It is an admission contract. An authorized external
run must emit this; V3's future parser reads this and nothing else.

### 5.1 The decisive design rule: V3 receives two reproductions, never a finished inventory

**The artifact carries both reproductions and V3 reconciles them itself.**

If the external tool handed V3 an already-reconciled inventory, V3 would be trusting the
reconciliation — the exact step section 7.3 requires to be independent. `TryReconcile` is documented
as *"the only path to a quarantined prior-coordinate inventory"*, and that must remain true on the
receiving side.

This has a property worth stating, because it is what makes the design self-checking: the signature
is over the **reconciled** inventory, whose signing bytes include both reproducers' roles and
identities and the coordinate list. So the external tool must reconcile in order to know what to
sign, and V3 re-reconciles from the two reproductions and re-derives the signing bytes itself. **If
the tool's reconciliation differed from V3's in any covered field, V3's re-derived bytes differ and
the signature fails.** V3 never has to trust the tool's reconciliation; it only has to be able to
repeat it.

### 5.2 Shape

UTF-8 JSON, unmapped members disallowed, matching the convention of every other wire family here.

```
{
  "schema": "lex-v3-quarantined-prior-coordinate-inventory-wire/1",
  "prior_index_pair_sha256": "<64 lowercase hex>",
  "source_index_identity_ref": { "resource_id": "<urn:uuid:...>", "sha256": "<64 lowercase hex>" },
  "verifier_receipt": {
    "verifier_identity": "<identifier>",
    "operated_read_only": true,
    "produced_at_utc": "<yyyy-MM-ddTHH:mm:ssZ>"
  },
  "reproductions": [
    {
      "role": "Primary",
      "reproducer_identity": "<printable ASCII, 1..256>",
      "coordinates": [
        { "work_key": "...", "language": "...", "valid_from": "yyyy-MM-dd", "anchor": null }
      ]
    },
    {
      "role": "IndependentReviewer",
      "reproducer_identity": "<distinct printable ASCII, 1..256>",
      "coordinates": [ ... ]
    }
  ],
  "attestation": {
    "purpose": "quarantine_prior_coordinate_inventory",
    "algorithm": "ECDSA-P256-SHA256",
    "signature_format": "ieee-p1363",
    "signature": "<86 chars unpadded base64url>",
    "issuer": { "role": "quarantine_reviewer", "issuer_id": "<identifier>", "key_id": "<identifier>" }
  }
}
```

Note the schema id differs from the inventory's own `SchemaId` by the `-wire/1` suffix: the wire form
and the reconciled object are different artifacts and must not share an identifier.

### 5.3 What the wire form must NOT contain

- no coordinate-set digest for either reproduction — V3 derives both, and accepting a supplied digest
  would let a producer make two disagreeing walks "agree" by supplying matching strings;
- no reconciled coordinate list beside the two reproductions — the reconciled set **is** the primary's
  list once reconciliation passes, and a third copy could disagree with both;
- no byte payload, stream, path or law text under any key;
- **no locator URI** — nothing that names where content may be fetched from. This is narrower than
  "no URI", deliberately: `source_index_identity_ref.resource_id` must be a `urn:uuid`, which
  `SourceArtifactRef` requires and which names an artifact *identity* rather than a retrievable
  location. The enforceable rule a parser applies is the one already in code:
  `QuarantineCoordinateValidation.RequireOpaqueKey` rejects `://` in every coordinate field, so no
  coordinate can name a location, while the artifact reference is validated as a UUID URN by
  `SourceCoreValidation.RequireUuidUrn`. Those two rules do not overlap and neither is a judgement
  call;
- no field naming the retired index's schema, serializer, row type or file layout.

### 5.4 The admission sequence, in order

1. parse the wire form; reject unmapped members;
2. construct each `PriorPublicCoordinate` — per-field validation applies here, including the opaque
   token rules;
3. `QuarantinePriorCoordinateReproduction.TryCreate` per reproduction — this derives each canonical
   digest **inside the factory, from its own coordinates alone**;
4. `QuarantinedPriorCoordinateInventory.TryReconcile(primary, independentReviewer, …)`;
5. `QuarantineInventoryCanonicalizer.VerifySignature(inventory, publicKey)` with a key resolved from
   a trust store — see section 7.

No step may be skipped or reordered. Step 3 before step 4 is what makes step 4's digest comparison a
comparison of two independently derived values rather than of two supplied strings.

---

## 6. Every refusal, and what each one catches

### 6.1 Per-coordinate, at construction

| condition | result |
| --- | --- |
| blank, over 512 chars, non printable-ASCII, contains `..` or `://`, `works/` prefix or `/works/` segment, `.xml`/`.html`/`.htm`/`.json` suffix | throws — a path or locator smuggled into a coordinate field |
| `valid_from` not exact `yyyy-MM-dd` | throws |
| language not accepted by `RoutedHttpValidation.RequireLanguage` | throws |

### 6.2 Per-reproduction — `QuarantineReproductionRefusal`

| refusal | catches |
| --- | --- |
| `RoleUndefined` | a role outside the closed two-member set |
| `ReproducerIdentityInvalid` | blank, over 256 chars, or non printable-ASCII identity |
| `CoordinatesEmpty` | **totality failure**: a walk that returned nothing cannot be a complete record |
| `CoordinatesTooMany` | over 2,000,000 — a bound against an adversarial or malformed input forcing an unbounded sort and hash |
| `DuplicateCoordinate` | the exact `(work_key, language, valid_from, anchor)` tuple twice in one walk |

### 6.3 Reconciliation — `QuarantineInventoryRefusal`

| refusal | catches |
| --- | --- |
| `ReproductionRolesNotDistinct` | one run submitted twice under one role |
| `ReproducerIdentitiesNotDistinct` | two runs by one identity — "called twice", not independent |
| `ReproductionCountMismatch` | **partial input**: one walk saw fewer coordinates than the other |
| `ReproductionsDisagree` | the two canonical digests differ, compared in fixed time. This is the collision and content-disagreement refusal: two walks of the same true index normalize identically, so a difference means at least one is wrong |
| `PriorIndexPairHashInvalid` | the prior index pair digest is not 64 lowercase hex characters |

### 6.4 Signature — `VerifySignature`

| condition | result |
| --- | --- |
| coordinates empty, or the stored `CoordinateSetSha256` disagrees with the digest re-derived from the coordinates | throws — **stale or mismatched digest** |
| signature not well-formed unpadded base64url for 64 P1363 bytes | throws |
| signature does not verify over `GetSigningBytes` under the supplied key | throws |

### 6.5 The refusal this specification says is still missing

`VerifySignature` checks only that the supplied key produced the signature. It says nothing about
whether that key **ought to be trusted** — its own remarks state it is "deliberately not the
trust-store-backed verifier section 7.3 step 6 hands to the canon/2 alias builder".

So an untrusted issuer with a syntactically valid key currently verifies. Section 7 specifies the
missing piece. Until it exists, a verified signature means *this key signed this inventory*, never
*this inventory is admissible*.

---

## 7. How V3 consumes the result without reading a retired index

The consuming path touches nothing from the previous generation. It reads **this document's wire
form** and nothing else — not the old schema, not its serializer, not its row types, not its files.

What remains to be built in V3, in dependency order, and none of it is authorized by this document:

1. **A parser** for section 5's wire form, producing exactly the constructor arguments in 5.4. It
   must be written against this specification, never against a sample of the external tool's output,
   or it becomes a reader of an unpinned format.
2. **A trust-store-backed verifier** — the section 6.5 gap. The pattern already exists in this
   repository: `IPreviewTrustStore` / `PreviewArtifactVerifier` resolve an issuer and a key before
   accepting an artifact. The quarantine counterpart must require the attestation's issuer role to be
   the pinned `quarantine_reviewer`, that issuer id to be present in a pinned store, the key id to
   resolve **under exactly that issuer**, and `VerifySignature` to pass against exactly that resolved
   key.
3. **Only then** the canon/2 alias builder (D3-05), which is out of scope here.

The ordering matters: a parser without the trust store admits a correctly-shaped artifact from an
unknown signer, and a trust store without the parser has nothing to verify.

---

## 8. What an authorized run must return

A future authorization would cover the external execution. This section specifies the evidence such a
run must produce, so that the authorization can name it and a reviewer can check it.

**Command evidence.** The exact tool identity and version; the exact previously promoted signed index
pair it pinned, by digest; that it ran read-only; and that its worktree and credentials were removed
afterwards, per section 7.3.

**Two reproduction receipts**, one per role, each naming its distinct reproducer identity and its
coordinate count.

**The artifact**, in section 5's wire form, retained under custody with its own content digest.

**Independent verification**, which a reviewer must be able to perform *without* re-reading the
retired index:

- re-derive both coordinate-set digests from the artifact's own coordinate lists and confirm they
  agree with each other;
- re-run steps 3–5 of section 5.4 and confirm the inventory reconciles and the signature verifies;
- confirm `prior_index_pair_sha256` matches the index pair the command evidence names.

**What the run does not establish.** That the coordinates are a *complete* record of the retired
index is attested by the two independent walks agreeing — not proven by V3, which never sees the
index. `ReproductionsDisagree` catches disagreement; it cannot catch two walks that were wrong
identically, for instance by both being pointed at the same wrong index pair. That is what
`prior_index_pair_sha256` and the command evidence are for, and it is a review obligation rather than
a computation.

---

## 9. Scope boundary of this document

This specifies an input and the terms of its admission. It does not authorize the external run,
implement the parser, implement the trust store, build the alias builder, mint an alias, publish
anything, sign anything, or touch a retired index. Claude's blocking READY and the owner gate that
Decision 45 already requires for **any** canon/2 alias publication remain in force and are unaffected
by anything written here.
