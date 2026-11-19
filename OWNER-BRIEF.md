# Lex V3: the owner's brief

Updated 2026-11-19 at 04:50 UTC by the driver. The driver updates this file in the first pull request of each day and
whenever a run starts, ends or stops. At most 40 lines, plain words, no hashes. The detail stays in STATUS-WEB.md and
STATUS-DATA.md; the acceptance list is LAUNCH-CONTRACT.md.

## Where Lex stands

- V4 is live: Release 15 serves law.soufien.lu since 04:27 UTC, through the usual release and its quality gate (36
  right articles of 56, 0 wrong). Release 14 is switched off and kept: it comes back in minutes.
- The two questions that failed on 18 November answer on the live site: "c'est quoi une personne lésé" gives the line
  of article 1 that defines the term (no AI model, under a second); "l'assureur peut résilier le contrat de façon
  unilatérale ?" gives the rule in the law's own sentence (article 39) and four cases, in about 3 s, and says that
  "unilatérale" is nowhere in the law.
- What changed: code reads the question with a dictionary of insurance terms, picks the route, finds the lines and
  quotes them. No AI model plans an answer: one only describes a question, or points at lines code found.
- Measured: on the 56 test questions, 36 right articles and 0 wrong (Release 14: 17 right); on 79 insurance questions
  asked of the live site, 64 right articles, 0 wrong, and 15 rightly handled without an article (refusals, words
  the law does not use); on the 15 unseen questions, 10 right (none before) and 1 wrong.

## What I decided for you (each can be undone)

- The new parts are built inside the present app, not as a separate Python service as the plan recommended: one
  thing to release, sign and back up. Same shape: one file per part, the route chosen by code.
- The advice line (your decision 4): "L'assureur peut-il résilier le contrat ?" is now answered by quoting the rule.
  A question on the reader's own case ("mon assureur…", "suis-je couvert ?") is still refused.
- Public sentences changed: the privacy notice's line on Microsoft (what is sent, and that a question code answers
  alone is not sent), How it works, Decisions, the Developers page. French checked by an independent AI reviewer.

## Known limits (fine to say)

- The dictionary covers private insurance only. Elsewhere, as before: a cited article opens, words are searched.
- A search with a date takes about 28 s on the live site (Release 14 too). Not corrected here.
- "Peut-on…", "faut-il…" with no role of the law named are still refused as advice.

## What waits on you

- Read the first rows of the insurance dictionary; later, name a person who knows insurance law.
- A new set of unseen questions: the present one is used up (a rule was written after its result; the site says so).
- The publisher run for the EU insurance texts not held yet (Solvency II, IDD, PRIIPs, the motor directive).
