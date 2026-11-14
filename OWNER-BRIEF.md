# Lex V3: the owner's brief

Updated 2026-11-14 at 05:50 UTC by the driver. The driver updates this file in the first pull request of each
day and whenever a run starts, ends or stops. At most 40 lines, plain words, no hashes. The detail stays in
STATUS-WEB.md and STATUS-DATA.md; the acceptance list is LAUNCH-CONTRACT.md.

## Where V3 stands

- Your order of 13 November, sections A to D, is live since 05:30 UTC. Search matches whole words and puts first the
  article whose title fits; the assistant quotes one article only when it is clearly the best, else offers three; each
  law has its own page (versions on a line, "Date du sinistre", changes highlighted); the answer shows the article, the
  law's real title and three buttons; the menu has five entries.
- Your three test cases now pass on the live site, and so do the law page checks: 7 of 7 (docs/CHANGES.md has the output).
- The new quality gate stopped one release this morning: it quoted a wrong article for one insurance question. Fixed,
  measured again, then live. On 56 test questions: wrong articles 3 before, 0 now; right articles 13 before, 17 now.
- 15 held-out questions, never used for tuning: 0 right and 0 wrong. On questions it has never seen, Lex now says it did
  not find or asks, rather than quoting a wrong article; finding them is the next step.
- The previous version is kept, switched off, for rollback; the backup copy is made and verified file by file.

## Known limits (honest, and fine to say in the interview)

- Questions phrased "l'assuré doit-il…" or "l'assureur peut-il…" are declined as personal advice, on purpose (the rule
  ignores who is asking). Asking "quel est le délai de…" works. See the decision below.
- English questions about a Luxembourg law held in French find nothing: the search does not translate.
- Some EU questions asked by idea rather than by article number point to the official source.
- The Quality page ends on the refused release; this morning's live run is added with the next release, later today.

## For the Foyer interview on 17 November

- The demo: C:\lex-v3\foyer-interview-2026-11-17\DEMO-SCRIPT.md, rewritten for this release, its questions checked live.
- What changed and why, to say aloud: docs/CHANGES.md. The system on one page: docs/ARCHITECTURE.md.
- The Python test suite is public on your GitHub (github.com/SFHAJJI/lex-eval).

## What waits on you

- A decision: should a general question phrased "l'assuré doit-il…" be answered? Today the advice rule declines it, by
  design, on 5 of the 56 test questions and 6 of the 15 held-out ones. Relaxing it is a legal choice, so it is yours.
- Read "How Lex works" on the site: it speaks in your name. Tell me any sentence to change.
- Optional: ask Microsoft for modified abuse monitoring, which removes the 30 days the privacy page states.
