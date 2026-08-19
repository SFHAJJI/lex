# Incidents

The useful story is not that a defect existed. It is which assumption failed, why ordinary tests
missed it and which permanent guard changed the system.

![A production symptom is traced through business impact and root cause into a code, contract, evaluation or operational guard that is then deliberately broken to prove the guard.](/built/diagrams/incident.svg)

[Open the incident-learning diagram at full size](/built/diagrams/incident.svg)

| Stage | Required evidence | Where it is recorded |
|---|---|---|
| Symptom and impact | What a reader or operator observed and which decision became unsafe | incident entry and sanitized operational evidence |
| Detection gap | Why ordinary build, unit or health checks remained green | postmortem and missing-case analysis |
| Root cause | The failed system assumption, not the last visible exception | source diff, trace or artifact inspection |
| Permanent guard | The smallest code, contract, evaluation or operational boundary that prevents recurrence | owning module plus focused regression |
| Proof | A deliberate break that turns the guard red | recorded per change in the commit that adds the guard; an automated break harness is planned, not built |

## Case studies

| Symptom and business impact | Root cause | Permanent guard | Lesson |
|---|---|---|---|
| The right Article 26 from the wrong EU regulation | An official amending title named two held instruments and residual text rank chose one | Deterministic subject preflight, amending-clause handling, ambiguity and instrument disclosure tests | Grounded prose can still answer the wrong document |
| Search index was valid and signed but contained no provisions | The build ran without the derived article layer | Required pinned article input plus end-to-end retrieval evaluation | Build integrity is not product behavior |
| Formex paragraphs appeared twice | Introductory text and list traversal overlapped | Immutable extraction-profile fingerprints | Plausible text corruption needs content tests |
| FTS snippets were always empty | Contentless FTS5 cannot supply stored text to `snippet()` | Snippets cut from the content-addressed text store and contract tested | A query that cannot fail loudly can fail invisibly |
| Deployment repeatedly failed although the candidate served | Several serial management-plane, OIDC, download and telemetry-arrival failures were hidden one behind another | Typed Azure retries, resumable acquisition, refreshed OIDC and bounded telemetry polling | Audit a serial pipeline end to end instead of paying one full cycle per cause |

## The retrieval lesson

The CRR/EMIR case adds a failure class to the usual RAG discussion: **right passage, wrong
instrument**. A groundedness judge cannot detect it because the generated sentence is faithful to
the evidence it received. The control must sit earlier, at subject identity, and the answer must
name the selected instrument so a reader can see the decision.

## Incident template

Every new entry records symptom, business effect, detection, why it survived, root cause, fix,
guard, deliberate-break proof and residual risk. That format prevents a postmortem from becoming a
timeline without an architectural consequence.
