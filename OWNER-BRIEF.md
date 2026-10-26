# Lex V3: the owner's brief

Updated 2026-10-26 at 23:10 UTC by the driver. The driver updates this file in the first pull request of each
day and whenever a run starts, ends or stops. At most 40 lines, plain words, no hashes. The detail stays in
STATUS-WEB.md and STATUS-DATA.md; the acceptance list is LAUNCH-CONTRACT.md.

## Where V3 stands

- Merged on 26 October, in three pull requests, each reviewed and green: the web (today's date, the search kept in
  its link, the legal pages naming no one, the EU authenticity line, how current the law is on every page); the data
  (which version answers a date, each date said for what it is, the gaps between versions, an EU act's own dates).
- Released on 26 October: a signed release of that code, rebuilt identically, every quality gate passed, no cookie,
  nothing kept about readers. It serves law.soufien.lu since 21:00 UTC, still open only to your addresses.
- The site opens to everyone with one command that you run (below): the driver's safety system will not let it
  remove the address rules itself. The legal pages were checked first and are ready.
- Today Luxembourg has 1,730 acts as first published, no codes; EU text covers 511 of 1,170 versions.
- The codes (121,000 articles) were rebuilt on Azure on 26 October after two fixes found on real data (a date written
  20220101, and a code's title given each article's date): two builds agreed byte for byte. Their quality gates pass
  for the EU and for search, but the Luxembourg date check does not yet: the check itself still assumes one version
  per act. It is being rewritten to the publisher's own start and end dates, and any reader fault it finds fixed,
  before a second release carries the codes. The index step now runs on this PC first, in about ten minutes.

## The plan

- Decided by you on 18 October: build on Azure, paid after the credits end; open to the public once V3 is finished;
  search engines may index it; the EU wording as recommended.
- Decided by you on 19 October: texts may be 30 days old before pages warn and a release is refused; the site names
  no one and gives no contact, so no e-mail is kept; accessibility is "non-conformant" until an audit. The EU texts
  date from 5 October, so from 4 November the site warns until they are collected again.
- The domain's name servers are Azure's since your switch on 18 October: keep the payment method valid. The full plan
  is C:\lex-v3\PLAN-2026-10-14.md.

## What waits on you

- Now: open the site. In PowerShell on this PC:
  pwsh -File C:\lex-v3\worktrees\claude-release\deploy\deploy.ps1 -Subscription <your subscription> -ResourceGroup
  rg-lex-v3 -AppName ca-lex-v3 -Open   (the full line is in the driver's message of 26 October)
- Before 4 November: a new collection of the EU texts, which the driver runs once the codes are in.
- Before 12 November: your 175.70 EUR of credits end. Then the site (about 26 EUR a month), the storage (under 0.50 EUR
  a month) and each rebuild (5 to 8 EUR) are billed to your payment method.
- Your choice, not done before the opening: a lawyer's read of the legal texts, an accessibility audit, user tests.
