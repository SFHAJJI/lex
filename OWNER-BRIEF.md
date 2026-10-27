# Lex V3: the owner's brief

Updated 2026-10-27 at 07:00 UTC by the driver. The driver updates this file in the first pull request of each
day and whenever a run starts, ends or stops. At most 40 lines, plain words, no hashes. The detail stays in
STATUS-WEB.md and STATUS-DATA.md; the acceptance list is LAUNCH-CONTRACT.md.

## Where V3 stands

- Released on 26 October: a signed release, rebuilt identically, every quality gate passed, no cookie, nothing kept
  about readers. It serves law.soufien.lu, open to everyone since 27 October 01:08 UTC, when you removed the address
  rules from your phone. The legal pages were checked first and again after.
- Down 3 minutes on 27 October (01:10 to 01:13 UTC): a redeployment switched off the live version by mistake. The
  deployment script is fixed so it never switches off a version it did not make.
- Today Luxembourg has 1,730 acts as first published, no codes; EU text covers 511 of 1,170 versions. The codes were
  rebuilt on Azure on 26 October; their date check is being rewritten before a second release carries them.
- Fixed on 27 October: reading a PDF no longer depends on the machine's fonts, so this PC and Azure build the same files.
- Merged on 27 October: Get help, How to read Lex and Developers; a limit of 60 questions a minute per visitor on
  the programming interface, kept only in memory; security headers; the published signing key; an outside check of the
  site every five minutes; and two operating documents, docs/OPERATING.md and docs/INCIDENT-RUNBOOK.md.
- In review on 27 October: the last screen gaps: example searches on the home page, breadcrumbs, a clean printout of a
  text, and a maintenance page for deployments.

## The plan

- Decided by you on 18 October: build on Azure, paid after the credits end; open to the public once V3 is finished;
  search engines may index it; the EU wording as recommended.
- Decided by you on 19 October: texts may be 30 days old before pages warn and a release is refused; the site names
  no one and gives no contact, so no e-mail is kept; accessibility is "non-conformant" until an audit. The EU texts
  date from 5 October, so from 4 November the site warns until they are collected again.
- On 27 October you asked for V3 finished to the specification within 72 hours (C:\lex-v3\PLAN-2026-10-14.md).

## What waits on you

- Now: your yes for two collection runs from the publishers (an EU refresh, and the Constitution from Legilux).
- The monthly cost ceiling: one PowerShell block in docs/INCIDENT-RUNBOOK.md (150 EUR a month, alerts at 50, 80 and
  100 per cent to the subscription's owner role, no e-mail address). The outside check runs once on the main branch.
- Before 12 November: your 175.70 EUR of credits end; then the site (about 26 EUR a month), the storage (under 0.50
  EUR a month) and each rebuild (5 to 8 EUR) are billed to your payment method, which must stay valid.
- Your choice, not done before the opening: a lawyer's read of the legal texts, an accessibility audit, user tests.
