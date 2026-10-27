# Lex V3: the owner's brief

Updated 2026-10-27 at 03:00 UTC by the driver. The driver updates this file in the first pull request of each
day and whenever a run starts, ends or stops. At most 40 lines, plain words, no hashes. The detail stays in
STATUS-WEB.md and STATUS-DATA.md; the acceptance list is LAUNCH-CONTRACT.md.

## Where V3 stands

- The site is open to everyone since 27 October, 01:10 UTC, when you removed the address rules. It serves the
  release of 26 October: signed, rebuilt identically, every quality gate passed, nothing kept about readers.
- Just after, a redeployment of that same release, to let search engines in, switched the live version off by
  mistake; the site was down for about three minutes and was brought back. The deploy kit is being fixed so it cannot
  happen again; search engines stay kept out until the next release.
- Today Luxembourg has 1,730 acts as first published, no codes; EU text covers 511 of 1,170 versions. The codes were
  rebuilt on Azure on 26 October; their date check passes with a fix in review, and they come with the second release.
- In review today: a limit of 60 questions a minute per visitor on the programming interface (pages are not
  refused), kept only in memory as a fingerprint forgotten within two minutes, with the privacy notice saying so in
  French and English; the security headers on every answer; the published signing key at
  law.soufien.lu/.well-known/lex-keys.json; a check of the site every five minutes from outside Azure; and the two
  operating documents, docs/OPERATING.md and docs/INCIDENT-RUNBOOK.md.

## The plan

- Decided by you on 18 October: build on Azure, paid after the credits end; open to the public once V3 is finished;
  search engines may index it; the EU wording as recommended.
- Decided by you on 19 October: texts may be 30 days old before pages warn and a release is refused; the site names
  no one and gives no contact, so no e-mail is kept; accessibility is "non-conformant" until an audit. The EU texts
  date from 5 October, so from 4 November the site warns until they are collected again.
- On 27 October you asked for V3 finished to the specification within 72 hours (C:\lex-v3\PLAN-2026-10-14.md).

## What waits on you

- The monthly cost ceiling: one PowerShell block in docs/INCIDENT-RUNBOOK.md, section 6 (150 EUR a month over the
  Lex resources, alerts at 50, 80 and 100 per cent to the subscription's owner role, no e-mail address written).
- The outside check runs only once its file is on the repository's main branch, or once v3/integration is made the
  default branch: your choice which.
- Before 4 November: a new collection of the EU texts, which needs your yes for the run.
- Before 12 November: your 175.70 EUR of credits end. Then the site (about 26 EUR a month), the storage (under 0.50 EUR
  a month) and each rebuild (5 to 8 EUR) are billed to your payment method.
- Your choice, not done before the opening: a lawyer's read of the legal texts, an accessibility audit, user tests.
