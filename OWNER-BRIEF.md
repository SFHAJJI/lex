# Lex V3: the owner's brief

Updated 2026-11-14 at 19:10 UTC by the driver. The driver updates this file in the first pull request of each
day and whenever a run starts, ends or stops. At most 40 lines, plain words, no hashes. The detail stays in
STATUS-WEB.md and STATUS-DATA.md; the acceptance list is LAUNCH-CONTRACT.md.

## Where V3 stands

- Release 12 is live since about 18:50 UTC: the final look and the last wording. Your 7 checks pass on the live site.
  Its quality gate passed with nothing wrong: 17 right articles of 56, 0 wrong, every quotation verified.
- The code is frozen until the interview, unless something breaks.
- Releases 10 and 11 are switched off and kept for rollback. Every release's backup copy is made and checked.
- Release 11 went live on your override of the gate: one insurance question ("Quand la garantie… prend-elle effet ?")
  got article 57 instead of 37 in one run. The model sometimes asks for detail on it, sometimes searches a few words and
  quotes article 57. Release 12's run asked for detail. Fixing it means changing search: after the interview.

## Known limits (honest, and fine to say in the interview)

- The question above can still get article 57, depending on the model's plan. Do not use it in the demo.
- Questions phrased "l'assuré doit-il…" or "l'assureur peut-il…" are declined as personal advice, on purpose.
- The 15 unseen test questions: 0 right, 0 wrong. Six are declined by that advice rule; eight use everyday words that
  are not the law's words, so the word search finds no single article and Lex says so; one got no plan from the model.
- English questions about a Luxembourg law held in French find nothing: the search does not translate.
- "Not found" offers a word search, not "the three closest acts".

## For the Foyer interview on 17 November

- The demo: C:\lex-v3\foyer-interview-2026-11-17\DEMO-SCRIPT.md. The three home examples are test questions.
- What changed and why, to say aloud: docs/CHANGES.md. The system on one page: docs/ARCHITECTURE.md.

## What waits on you

- A decision: should a general question phrased "l'assuré doit-il…" be answered? Relaxing the advice rule is yours.
- Read the Decisions page: it speaks in your name. Tell me any line to change.
