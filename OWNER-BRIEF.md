# Lex V3: the owner's brief

Updated 2026-11-21 at 00:20 UTC by the driver. The driver updates this file in the first pull request of each day and
whenever a run starts, ends or stops. At most 40 lines, plain words, no hashes. The detail stays in STATUS-WEB.md and
STATUS-DATA.md; the acceptance list is LAUNCH-CONTRACT.md.

## Where Lex stands

- Live since 20 November about 23:55 UTC, on your word: the new Lex. Same layout as before (reading and comparing in
  the middle, the assistant on the side), a new engine and a new data store behind it. Its code is in
  `C:\lex-v3\lex-next` (runbook: its README.md), outside this repository for now.
- The previous Lex (Release 19) keeps running on its own Azure address: the new one reads and compares through it, and
  one command brings it back on law.soufien.lu (`C:\lex-v3\lex-next\switch-domain.ps1 -To old -Apply`).

## What the new Lex does

- Finds the article three independent ways (meaning, words, the law's table of contents) and says how sure it is.
- The model only points at lines; the law's words are copied by code. Exceptions written in another article, the law's
  own definitions and the exact paragraph a reference names are shown beside the rule.
- Reads the law as it stood on the date asked, and says when it does not hold that version.
- Reads European texts too (GDPR, AI Act, DORA and 74 others held), with their amounts intact.
- When it finds no answer, it says so and shows what it searched.

## How it was measured: 130 fresh questions written by another session, asked once

| | Result |
|---|---|
| Right answer asserted, of 120 that have an answer | 99 |
| Wrong answer asserted | 4 |
| Questions with no answer in the law, rightly not answered | 10 of 10 |
| The same 61 earlier questions: new Lex / previous Lex | 57 / 14 |

## What I decided for you (each can be undone)

- Newer models in the EU data zone for the new Lex; the previous Lex keeps gpt-4.1.
- Old images deleted from the registry; Releases 18, 19 and 20 kept.

## Weak spots, and what waits on you

- 16,000 versions never downloaded (a licence flag: yours to decide); no expert-checked dictionary; slow when busy.
