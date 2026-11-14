# What changed for the demo (14 November 2026)

Each change: what was wrong, what changed, and how it is tested. In plain words, to say aloud.

## A. The assistant finds the right article

**1. Search matched pieces of words.**
- Wrong: searching "délai de prescription contrat d'assurance" listed four Labour Code articles first, because the word "de"
  was found inside "décide" and "demandeur". The search even said it had no ranking.
- Changed: search now matches whole words only, ignores small words (de, la, the, der, ...), accepts a plural for a singular,
  and ranks: an article whose heading holds the rarest words of the question comes first. Article numbers like "121-6" or
  "1er" are kept whole.
- Tested: unit tests on the word rules (the "décide" case among them); a ranking test where a Labour Code article holding
  every word still comes after art. 44; the search's quality gate, rerun with the new rule, still at 100%.

**2. The assistant quoted a weak match as if it were sure.**
- Wrong: the prescription question was answered with art. 70, labelled "complete".
- Changed: the search now says whether its first result is clearly ahead of the next. The assistant quotes one article only
  then; otherwise it says it is not sure and offers the three best candidates.
- Tested: assistant tests for both cases; the live check below.

**3. A question with a date took another path and quoted nothing.**
- Wrong: the same question "au 1er janvier 2020" returned an act-level step with no quotation, still called an answer.
- Changed: a step that quotes nothing can no longer be called an answer; the model's instructions say a dated question is
  planned exactly like the undated one, with the date. The model is also told to keep the legal concept ("délai de
  prescription") in what it searches.
- Tested: a test on the real model (gpt-4.1-mini) that the dated and undated plans are the same but for the date.

**4. A very long query could skip a word** (found by the independent review): refused now, with a test.

**5. One common word in a heading was taken as proof** (found by the new release gate, see D).
- Wrong: on the next release, "Quand la garantie d'un contrat d'assurance prend-elle effet ?" quoted art. 9, whose heading
  shares only the word "assurance" with the question. The right article, 37 ("Prise d'effet de la garantie"), never repeats
  "contrat d'assurance" in its text.
- Changed: a result counts as "clearly ahead" only if its heading holds two words of the question, or its rarest word.
  Otherwise the assistant says it is not sure and offers the three best.
- Tested: the same case rebuilt on test articles; the decision rule tested on its own. The release that had the bug was
  stopped by the gate and never went live.

## B. The law page

- Wrong: an act's page showed "Version consolidée applicable au ..." as its title and a table for experts.
- Changed: one page per act with its real title, its status ("En vigueur"), its article and version counts, its versions as
  points on a line (click one to read it, a second to compare), a "Date du sinistre" field that picks the version in force
  on that date, and the comparison: changed articles first, added words underlined, removed words struck through, unchanged
  articles folded away. Each article has its official source, a citation to copy and its history.
- Tested: on the 1997 insurance law, 10 versions from 2006-12-25 to 2025-11-25; 3 March 2019 picks the version of
  1 November 2018; 2021 against 2025 shows 1 added article (62-11), 0 changed, 0 removed, 146 unchanged. The status rule was
  corrected after the review (a future version no longer makes the law "not yet in force").
- Found on the way: since 10 November, no law page was prepared by the server (its script engine lacks a web function a
  link used), so pages without JavaScript showed an empty form. Fixed, with a test that runs the server's own renderer.

## C. Home, answer card, menu

- Home: one question box with a date chip ("À la date du : aujourd'hui"); the second search box and the citation examples
  are gone.
- Answer card, in this order: Trouvé / Pas trouvé / Hors périmètre; the article and its heading; the act's real title; the
  quotation; "Version applicable depuis le ..."; three buttons: another date, what changed (both open the law page at that
  article), official source. "Comment Lex a trouvé" (the plan and the trace) is one click away. One disclaimer line under it.
- Menu: Assistant, Textes, Modifications, Qualité, Comment ça marche; the rest under Développeurs.
- Modifications opens on the last twelve months.

## D. Measuring quality

- The test set grew from 16 to 56 questions, each with the expected act and article: 25 on the 1997 insurance law, 21 on
  GDPR, the AI Act and DORA, in French and English. 15 more questions are held out: never shown, never tuned against, scored
  apart. Held-out file SHA-256: `5ab0d20f1699fb6316053ffb6f9ce05750d20b2dec7db9373c31ae5fa8ed2e7d` (fixed before any run).
- Each result is one of: right article, wrong article, not found, wrongly refused. "Wrong article" is the headline; target 0.
- Every run is logged in MLflow: the code commit, the data snapshot, the prompt version, the model, latency p50/p95, an
  estimated cost per question, and one trace per question (plan, search, hits, chosen article).
- Gate: a release does not get traffic if one answer on the insurance set quotes a wrong article, or if a question that
  passed before now fails. It worked on its first use: the release with fixes A to C answered 17 questions with the right
  article (13 before) but quoted one wrong article, so it was stopped and fixed (A5 above) before going live.
- Also listed on the Quality page: the known failures, the held-out score next to the test score, the cost per question.

## Results

The 56 test questions (each with the article its answer must quote) and the 15 held-out questions, on each release:

| | Before (Release 5, live on 13 Nov) | Release 6 (refused by the gate) | Release 7 (live) |
|---|---|---|---|
| Right article | 13 | 17 | 17 |
| **Wrong article** | **3** (2 on insurance) | **1** (insurance) | **0** |
| Not found (says so, or asks which) | 23 | 24 | 25 |
| Refused as advice, though general | 8 | 5 | 5 |
| Right behaviour (refusal, question back, pointer) | 9 | 9 | 9 |
| Held-out: right article, of 15 | 0 | 0 | 0 |
| Answer time, median / 95% | 1.5 s / 2.0 s | 1.5 s / 2.5 s | 1.6 s / 5.7 s |
| Estimated model cost per question | 0.0011 € | 0.0011 € | 0.0011 € |

Every quotation was checked against the published text on every run (100%).

The owner's acceptance checks on the live site, after the deployment:

On law.soufien.lu, 14 November 2026, 05:33 UTC (Release 7):

```text
PASS  A1 ask (REST): "Quel est le délai de prescription des actions dérivant d'un contrat d'assurance ?" -> art. 44, quoted
      verdict answer; cited /lu-legilux/loi-1997-07-27-n10 art_44; "Article 44Délai de prescription1.Le délai de prescription de toute action dérivant du contrat d'assurance est de trois a"
PASS  A2 ask (MCP tools/call): "Quel est le délai de prescription des actions dérivant d'un contrat d'assurance au 1er janvier 2020 ?" -> art. 44, quoted in the version of that date
      verdict answer; cited /lu-legilux/loi-1997-07-27-n10 art_44; version from 2020-01-01
PASS  A3 search "délai de prescription contrat d'assurance" (fra) -> art. 44 first
      first hits: loi-1997-07-27-n10 art_44 | code-travail art_l_551-5 | code-travail art_l_551-5 | code-travail art_l_551-5; best_hit clearly_ahead; ranking starts "strict lane before relaxed lane; within a lane, by the rarit"
PASS  B1 the 1997 law has 10 versions, 2006-12-25 to 2025-11-25
      10 versions: 2006-12-25 ... 2025-11-25
PASS  B2 a claim of 03/03/2019 selects the version of 2018-11-01
      applying on 2019-03-03: 2018-11-01
PASS  B3 2021-01-01 against 2025-11-25: 1 added (art. 62-11), 0 changed, 0 removed, 146 unchanged
      {"added":1,"changed":0,"removed":0,"unchanged":146}; added art_62-11
PASS  B4 the law page /fr/lu-legilux/loi-1997-07-27-n10: real title, 10 points, latest against previous, the claim date, nothing to type
      status 200; title "Loi du 27 juillet 1997 sur le contrat d&#x27;assurance."; 10 points; chosen 2021-01-01/2025-11-25; status badge in_force

every check passed
```

The same checks on the site before (Release 5) failed A1 (art. 70 quoted), A2 (art. 70), A3 (the Labour Code first, "ranking: none") and B4 (no law page).
