# Lex V3: the owner's brief

Updated 2026-11-12 at 08:00 UTC by the driver. The driver updates this file in the first pull request of each
day and whenever a run starts, ends or stops. At most 40 lines, plain words, no hashes. The detail stays in
STATUS-WEB.md and STATUS-DATA.md; the acceptance list is LAUNCH-CONTRACT.md.

## Where V3 stands

- law.soufien.lu serves release A since yesterday 18:07 UTC: the third release's code, EU data of 10 November, the
  assistant as a labelled preview, and the plainer law page. Its Luxembourg data is from 12 October, so the pages warn
  that it is over 30 days old until the final release.
- The assistant quotes Luxembourg law correctly. EU questions ("article 5 of the GDPR") failed on the live site; the
  fix, with short names such as RGPD, DORA and AI Act, is merged and goes live with the final release.
- One French text of an EU banking rule stays missing in the final release: the EU collection could not re-fetch it,
  and the release lists it openly.

## The plan

- The fresh Luxembourg collection has collected every law family and is now downloading the documents; it ends around
  14:00 to 15:00 UTC today. The data is then rebuilt and built on Azure; the final release goes live this evening.
- For the Foyer interview on 17 November: your study guide and preparation file are in
  C:\lex-v3\foyer-interview-2026-11-17. A Python evaluation tool with MLflow (C:\lex-v3\lex-eval) measures the
  assistant: today 60% overall (EU 0 of 5, everything else right, every quotation verified); the final release should
  lift it.

## What waits on you

- To publish the Python evaluation tool on your GitHub (private first): your yes, or create the repository yourself.
- Reply to Foyer's e-mail to confirm the interview; fill in your own background in the study guide's pitch.
- Optional: allow the driver to delete the old third-release data folder (1.6 GB, also kept in Azure).
- Optional: ask Microsoft for modified abuse monitoring, which removes the 30 days the privacy page states.
- Four DNS records for soufien.lu (the commands are in the brief of 31 October).
