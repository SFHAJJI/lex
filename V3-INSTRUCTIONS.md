# Lex V3 working rules

## Authority binding

This is the V3 product, source, integration, and release repository. Its execution contract is the accepted six-member clean-room bundle with manifest SHA256 `12C302017CE9B48750115FB638A217B4D562581216AB0E3B5557A6E659C4EF0F` and bundle-set SHA256 `d43366e73d22b80f2ad2b9c08767806778354b5362f895bfc77068e298326020`, activated by Decision 55 in the Decisions register whose post-append SHA256 is `9AC4F7787C55D7B7E8104DB754A728F8C9979EDC98A886CD3A8CC7965D714A5F`.

The architect specification and settled Decisions remain the quality floor. If this file conflicts with either, the specification or later settled Decision wins. At this activation checkpoint, no accepted production data manifest exists. A local database, directory, image, generated artifact, or mutable path is never data authority. Data becomes authoritative only through an exact signed V3 artifact manifest accepted at its required gate.

## Product and reuse boundary

1. Build only the V3 product and the infrastructure V3 needs on its own merits.
2. Earlier implementation is an opt-in parts bin, never a foundation or compatibility constraint. Reuse requires a named V3 reason, a recorded ledger entry, and an independent check.
3. The activation commit reuses only repository identity, Git history, and the existing `LICENSE`. It reuses no product implementation.
4. Keep legal facts, evidence, deterministic operations, refusals, exports, and supported journeys available without a model.
5. Never commit private coordination locations, credentials, publisher payloads, legal text, or generated release artifacts.
6. A preview artifact is synthetic and must remain incapable of entering a production corpus, index, or release path.

## Work and review boundary

1. Every active item has one accountable writer, declared paths, a checkpoint, and a non-writer reviewer.
2. Add behavior in small, reviewed vertical slices. Tests describe V3 behavior only.
3. A moved candidate head invalidates review and checks affected by the move. Integration is serialized and records the reviewed head and resulting tree.
4. Claude receives major milestone reviews. Decision 43 requires Claude's personal independent READY before accepting or publishing the full release corpus and index, publishing aliases, or promoting production.
5. The only required continuous-integration checks are `dotnet`, `web`, and `canon-windows`. Golden and snapshot diffs are review evidence, never merge gates.
