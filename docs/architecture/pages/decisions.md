# Trade-offs

An architecture decision is useful only when it names the alternative, the reason and the cost.
The complete machine-readable register is rendered at [Architecture decisions](/decisions). This
page is the interview path through the choices with the largest product consequences.

| ID | Choice | Rejected alternative | Cost admitted |
|---|---|---|---|
| D53 | Store repeated wording once and map every dated occurrence to its content hash | Duplicate every unchanged provision in every state | Readers reconstruct a version through occurrence mappings |
| D54 | Package a pinned local encoder and gate hybrid retrieval by holdout evidence | Buy managed search before corpus or traffic requires it | Lex owns model packaging, vectors and relevance measurement |
| D57 | Return an explicit publisher gap | Synthesize a consolidated text | Some states remain metadata-only |
| D58 | Pin trust outside the artifact and sign the complete manifest | Trust a public key carried beside the data it authenticates | Rotation is an explicit dual-trust release |
| D75 | Admit only source-backed official identity and discovery metadata | Manual aliases or model-generated legal identity | Publisher vocabularies and collisions become build concerns |
| D76 | Resolve subject first, freeze a typed plan, execute once, compose only on request | A ReAct loop with model observation and retries | More explicit application contracts |
| D83 | Keep Legilux and EUR-Lex classifications as distinct weak discovery lanes | Invent one cross-publisher taxonomy | Facets and multilingual alignment remain deferred |

## Three decisions to challenge

**Why no ReAct loop?** The measured dangerous failures were identity and evidence failures. Letting a
model observe a deterministic refusal and try a different law can convert an honest gap into a
plausible answer. One correction before execution fixes contract syntax without changing authority.

**Why build semantic retrieval and leave it off?** Because architecture is a reversible hypothesis.
The vectors and encoder prove the path can run; the frozen holdout decides whether it should serve.
The next activation can be category-specific rather than a global switch.

**Why not unify taxonomies?** EuroVoc and Legilux classifications have different authorities,
languages and semantics. Preserving the source scheme makes a match explainable. A unified label
would be a new assertion owned by Lex and would need its own governance and evaluation.

Status vocabulary records implementation maturity, not current traffic: `shipped` means included in
the release line, `gated` means activation still depends on evidence, and `planned` means no product
claim yet. Mounted identities and signed promotion receipts separately establish what a running
revision serves.
