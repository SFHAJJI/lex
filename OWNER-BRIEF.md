# Lex V3: the owner's brief

Updated 2026-11-14 at 14:00 UTC by the driver. The driver updates this file in the first pull request of each
day and whenever a run starts, ends or stops. At most 40 lines, plain words, no hashes. The detail stays in
STATUS-WEB.md and STATUS-DATA.md; the acceptance list is LAUNCH-CONTRACT.md.

## Where V3 stands

- Release 10 is live since about 13:10 UTC, on your yes. Its quality gate stopped it once: the model planned one test
  question differently (it fetched the whole law instead of the article). The rerun passed every check: 17 right
  articles of 56, 0 wrong, every quotation verified. Your 7 checks pass on the live site.
- Release 7 is switched off and kept for rollback. Release 10's backup is copied; checking the copy is pending, because
  this PC runs short of disk while it reads the 4 GB image.
- The final release (your order of this evening) is built and tested: a white page, one green, one font, Decisions and
  Evidence in the menu, the answer in one sentence with "Voir le travail", the law page with one summary sentence.
  Screenshots come to you before 20:00 for your yes; nothing goes live without it.

## Known limits (honest, and fine to say in the interview)

- Questions phrased "l'assuré doit-il…" or "l'assureur peut-il…" are declined as personal advice, on purpose.
- The 15 unseen test questions: 0 right, 0 wrong. Six are declined by that advice rule; nine use everyday words that
  are not the law's words, so the word search finds no single article and Lex says so.
- English questions about a Luxembourg law held in French find nothing: the search does not translate.
- "Not found" offers a word search, not "the three closest acts": no operation finds those without a new search rule.
- Evidence shows the cost per question, not the month's spend: the server keeps that in memory and does not publish it.

## For the Foyer interview on 17 November

- The demo: C:\lex-v3\foyer-interview-2026-11-17\DEMO-SCRIPT.md. The three home examples are test questions.
- What changed and why, to say aloud: docs/CHANGES.md. The system on one page: docs/ARCHITECTURE.md.

## What waits on you

- Your yes on the final release's screenshots, then the freeze.
- A decision: should a general question phrased "l'assuré doit-il…" be answered? Relaxing the advice rule is yours.
- Read the Decisions page: it speaks in your name. Tell me any line to change.
