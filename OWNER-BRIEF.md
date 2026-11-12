# Lex V3: the owner's brief

Updated 2026-11-12 at 15:50 UTC by the driver. The driver updates this file in the first pull request of each
day and whenever a run starts, ends or stops. At most 40 lines, plain words, no hashes. The detail stays in
STATUS-WEB.md and STATUS-DATA.md; the acceptance list is LAUNCH-CONTRACT.md.

## Where V3 stands

- law.soufien.lu serves release A since 11 November 18:07 UTC: the third release's code, EU data of 10 November, the
  assistant as a labelled preview. Its Luxembourg data is from 12 October, so the pages warn it is over 30 days old.
- The fresh Luxembourg collection finished today at 15:28 UTC: every law family, the documents and the official
  gazette, 11,316 documents held (11,205 in October). One document is refused by the publisher, as in October.
- Today's fixes, merged: EU questions ("article 5 of the GDPR", RGPD, DORA, AI Act), a request with a badly encoded
  character now gets "bad request" instead of a server error, and a test that failed by chance is now exact.

## The plan

- The final data is being rebuilt on Azure since 15:34 UTC (about four and a half hours). Then the release is built
  and checked against release A on Azure (about three hours), and goes live tonight, around 23:00 to 23:30 UTC, with
  Luxembourg and EU data both under 30 days old and the EU questions fixed.
- One French text of an EU banking rule stays missing, as in release A; the release record says so.
- For the Foyer interview on 17 November: start with
  C:\lex-v3\foyer-interview-2026-11-17\AI-LIFECYCLE-AND-CHALLENGES.md (the whole project lifecycle, a fraud
  assistant worked through, what they can ask). Say "mock" for a practice interview.

## What waits on you

- To publish the Python evaluation tool on your GitHub (private first): your yes, or create the repository yourself.
- Reply to Foyer's e-mail to confirm the interview, if not done.
- Optional: allow the driver to delete the old third-release data folder (1.6 GB, also kept in Azure).
- Optional: ask Microsoft for modified abuse monitoring, which removes the 30 days the privacy page states.
- Four DNS records for soufien.lu (the commands are in the brief of 31 October).
