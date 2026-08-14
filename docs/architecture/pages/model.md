# Legal model

The core modeling choice is to keep a law, a dated legal state, a language expression and the
publisher file as different things. Collapsing them into one document would make a date lookup
ambiguous and make provenance impossible to state precisely.

![A legal work contains dated consolidations, language expressions and publisher manifestations, with separate applicability and observation clocks.](/built/diagrams/legal-model.svg)

[Open the legal model diagram at full size](/built/diagrams/legal-model.svg)

## FRBR and JOLux shape

| Level | Meaning in Lex | Responsibility | Implementation |
|---|---|---|---|
| Work | The legal instrument across its life | Stable publisher identity and title | `Lex.Law` corpus records and index work catalog |
| Dated consolidation | One publisher-asserted state | `valid_from`, `valid_to` and stable v4 version key | corpus states and `Lex.Temporal` interval selection |
| Expression | One language of that state | Language-specific title and availability | corpus expression records and index version rows |
| Manifestation | The file the publisher served | Media type, URL, bytes and SHA-256 | evidence repositories and manifestation records |

The v4 version key is based on the full publisher version identifier, not its position in a list.
The key combines `valid_from` with SHA-256 of that full identifier. A same-day reordering can no
longer silently move text from one state to another.

## Two clocks, two questions

| Clock | It answers | It does not answer |
|---|---|---|
| Legal or consolidation time | Which publisher state covers the reader's requested date | When Lex first learned about it |
| Observation time | When a record or change entered the evidence history | Whether the rule was legally applicable then |

For Legilux, the date is publisher-asserted legal applicability. For EUR-Lex, it is the state date
of the consolidated wording. The UI and API keep those meanings distinct rather than claiming one
cross-publisher definition they do not share.

## Empty is a modeled outcome

A publisher can announce a state before supplying exploitable wording, or provide a manifestation
that the current deterministic profile cannot safely segment. Lex keeps that state and returns a
typed availability reason with the official link. It does not synthesize a consolidation or copy
text from another date. In this product, an honest gap is useful data.

## What stays outside the model

Human-authored aliases, model-inferred classifications and generated legal text do not become
corpus authority. Official short titles may identify a work only when the source literal is an
exact unique match. Official taxonomies remain discovery metadata and never prove historical
applicability or legal wording.
