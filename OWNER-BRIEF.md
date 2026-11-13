# Lex V3: the owner's brief

Updated 2026-11-13 at 15:45 UTC by the driver. The driver updates this file in the first pull request of each
day and whenever a run starts, ends or stops. At most 40 lines, plain words, no hashes. The detail stays in
STATUS-WEB.md and STATUS-DATA.md; the acceptance list is LAUNCH-CONTRACT.md.

## Where V3 stands

- law.soufien.lu serves a new release since 13 November 15:18 UTC, made on your "fix it now": 283 more Luxembourg laws
  and regulations now show their text. Among them: the insurance contract law of 1997 (its current version, 147
  articles), the insurance sector law of 2015, the motor insurance law of 2003 and the company law of 1915.
- Why they were hidden: a licence rule withheld a file whose own licence is open because the file also carried notes
  about other files (the original act). The rule is now narrower, versioned and recorded with your decision.
- Before it went live: the data was built twice and came out identical, every check passed, the assistant was asked
  real questions at the new version's private address. The previous release is kept, switched off, for rollback.
- The assistant's automatic test: 14 of 16 right, every quotation checked against the official text, every gate passed.

## Known limits (honest, and fine to say in the interview)

- Asked about "la loi du 27 juillet 1997 sur le contrat d'assurance", the assistant asks "which law?": it searches by
  the date alone, which several laws share. The search bar finds it. Fix in Sunday's release.
- Typing "AI Act" or "RGPD" in the search bar finds nothing today. Fixed and merged; live with Sunday's release.
- The pages show too much technical detail, which the specification says belongs behind "Details". A design pass,
  checked on screenshots, ships Sunday.
- About 3,400 Luxembourg files stay hidden because the publisher's own note on the file names its restrictive licence.
- DORA questions in English point to the official source.

## For the Foyer interview on 17 November

- Start with C:\lex-v3\foyer-interview-2026-11-17\AI-LIFECYCLE-AND-CHALLENGES.md. Say "mock" for a practice interview.
- Sunday's release puts it all on the site: a clearer home page with the assistant up front, a "How Lex works" page
  (for Kezhan and his manager alike) and plain pages. You see before-and-after screenshots before it goes live.
- The Python evaluation tool is public on your GitHub (github.com/SFHAJJI/lex-eval).

## What waits on you

- Read the wording of the "How Lex works" page before it goes live on Sunday (a public statement).
- Optional: ask Microsoft for modified abuse monitoring, which removes the 30 days the privacy page states.
- Four DNS records for soufien.lu (the commands are in the brief of 31 October).
