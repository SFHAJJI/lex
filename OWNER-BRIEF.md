# Lex V3: the owner's brief

Updated 2026-11-14 at 15:45 UTC by the driver. The driver updates this file in the first pull request of each
day and whenever a run starts, ends or stops. At most 40 lines, plain words, no hashes. The detail stays in
STATUS-WEB.md and STATUS-DATA.md; the acceptance list is LAUNCH-CONTRACT.md.

## Where V3 stands

- Release 10 is live since about 13:10 UTC, on your yes. Its quality gate stopped it once: the model planned one test
  question differently (it fetched the whole law instead of the article). The rerun passed every check: 17 right
  articles of 56, 0 wrong, every quotation verified. Your 7 checks pass on the live site.
- Release 7 is switched off and kept for rollback. Release 10's backup copy is made and checked file by file.
- Release 11, the final release of your order of this evening, waits at its own address with no traffic: your 7 checks
  pass there, the quality gate passes (17 right, 0 wrong), the two-address check passes. Its screenshots, French and
  English, are with you. Nothing goes live without your yes; then the freeze.

## Known limits (honest, and fine to say in the interview)

- Questions phrased "l'assuré doit-il…" or "l'assureur peut-il…" are declined as personal advice, on purpose.
- The 15 unseen test questions: 0 right, 0 wrong. Six are declined by that advice rule; eight use everyday words that
  are not the law's words, so the word search finds no single article and Lex says so; one got no plan from the model.
- English questions about a Luxembourg law held in French find nothing: the search does not translate.
- "Not found" offers a word search, not "the three closest acts": no operation finds those without a new search rule.
- Evidence shows the cost per question, not the month's spend: the server keeps that in memory and does not publish it.

## For the Foyer interview on 17 November

- The demo: C:\lex-v3\foyer-interview-2026-11-17\DEMO-SCRIPT.md. The three home examples are test questions.
- What changed and why, to say aloud: docs/CHANGES.md. The system on one page: docs/ARCHITECTURE.md.

## What waits on you

- Your yes on Release 11's screenshots; then it goes live and the code is frozen until the interview.
- A decision: should a general question phrased "l'assuré doit-il…" be answered? Relaxing the advice rule is yours.
- Read the Decisions page: it speaks in your name. Tell me any line to change.
