# Lex V3: the owner's brief

Updated 2026-11-12 at 22:45 UTC by the driver. The driver updates this file in the first pull request of each
day and whenever a run starts, ends or stops. At most 40 lines, plain words, no hashes. The detail stays in
STATUS-WEB.md and STATUS-DATA.md; the acceptance list is LAUNCH-CONTRACT.md.

## Where V3 stands

- law.soufien.lu serves the final release since 12 November 22:05 UTC: the third release's code, Luxembourg data
  collected on 12 November and EU data of 11 November (no page warns about old data), the assistant as a labelled
  preview. New: the Luxembourg Constitution and 29 more laws.
- The assistant now answers EU questions ("article 5 du RGPD", GDPR article 22, AI Act article 50) with the official
  text. Measured by the Python evaluation tool: 14 of 15 right (60% before), every quotation verified, about 2 seconds.
- Before it went live, the release was checked against the previous one; the check found three changes the publisher
  made, and each is now explained in the release's record. The previous release is kept, switched off, for rollback.
- The backup copy is made; its last check stopped on a bookkeeping detail, fixed tonight, and is run again tomorrow.

## Known limits (honest, and fine to say in the interview)

- The three core Luxembourg insurance laws (1997 contract law, 2015 sector law, 2003 motor law) are collected but not
  shown: the publisher's own licence statements for them disagree, and Lex never republishes a text whose rights are
  unclear. The site points to the official source. Changing this is a legal decision, yours; recommended: not before
  Tuesday.
- DORA questions: in French the assistant asks which version you mean; in English it points to the official source.
- One French text of an EU banking rule stays missing, as before; the release record says so.

## For the Foyer interview on 17 November

- Start with C:\lex-v3\foyer-interview-2026-11-17\AI-LIFECYCLE-AND-CHALLENGES.md. Say "mock" for a practice interview.
- Tomorrow: a tested demo script on the live site, with what to say at each step.

## What waits on you

- To publish the Python evaluation tool on your GitHub (private first): your yes, or create the repository yourself.
- Reply to Foyer's e-mail to confirm the interview, if not done.
- Optional: ask Microsoft for modified abuse monitoring, which removes the 30 days the privacy page states.
- Four DNS records for soufien.lu (the commands are in the brief of 31 October).
