# Lex V3: the owner's brief

Updated 2026-10-19 at 07:30 UTC by the driver. The driver updates this file in the first pull request of each
day and whenever a run starts, ends or stops. At most 40 lines, plain words, no hashes. The detail stays in
STATUS-WEB.md and STATUS-DATA.md; the acceptance list is LAUNCH-CONTRACT.md.

## Where V3 stands

- The engine underneath is solid: every quote checkable against its official source, and a signed release that
  rebuilds identically. For Luxembourg the date checks prove little yet: each act has one version so far.
- Landed on 17 and 18 October: the new home page, links that open on the article they cite, readable addresses, one
  citation in three forms, a search box that opens a law from its exact reference, results by act, long codes by page.
- Ready on 19 October, in one pull request: today's date in Luxembourg on every page and kept in a shared link; a
  search kept in its link; the five legal pages in French and English; the EU authenticity line chosen by the act's
  date; and on every page how current the law is and which code built it, with a warning when a source is too old.
- Your private copy runs the code of 18 October (a signed release, checked over HTTP) at law.soufien.lu over HTTPS,
  still open only to this PC and your 14 October address.
- Still thin: Luxembourg has 1,730 acts as first published, no codes; EU text covers 511 of 1,170 versions. The codes
  (121,000 articles) and about 28,000 more articles wait for the rebuild on an Azure machine.

## The plan

- Decided by you on 18 October: build on Azure, paid after the credits end; open to the public once V3 is finished,
  within 24 hours, without the lawyer's read or the audits; search engines may index it; the EU wording as recommended.
- Decided by you on 19 October: texts may be 30 days old before every page warns and a new release is refused; e-mails
  are kept 12 months; the accessibility page says "non-conformant" until an audit. The EU texts were collected on 5
  October, so from 4 November the site warns until they are collected again, which needs your yes for that run.
- The domain's name servers are Azure's since your switch on 18 October: the name depends on the subscription staying
  paid, as V2's did. Keep the payment method valid. The full plan is C:\lex-v3\PLAN-2026-10-14.md.

## What waits on you

- Now: turn on Azure Compute, with the one Cloud Shell line the driver gave you, so the codes can be rebuilt on an
  Azure machine (about 5 EUR a rebuild).
- Now: three details for the legal pages, which only you can give: your name as publisher, a postal address and a
  contact e-mail. They go in one file, web/scripts/legal-publisher.mjs (or tell the driver). The site still cannot
  open until they are filled.
- Before 12 November: your 175.70 EUR of credits end. Then the site (about 26 EUR a month), the storage (under 0.50 EUR
  a month) and each rebuild are billed to your payment method.
- Your choice, not done before the opening: a lawyer's read of the legal texts, an accessibility audit, user tests.
