# Lex V3: the owner's brief

Updated 2026-10-26 at 14:20 UTC by the driver. The driver updates this file in the first pull request of each
day and whenever a run starts, ends or stops. At most 40 lines, plain words, no hashes. The detail stays in
STATUS-WEB.md and STATUS-DATA.md; the acceptance list is LAUNCH-CONTRACT.md.

## Where V3 stands

- The engine underneath is solid: every quote checkable against its official source, and a signed release that
  rebuilds identically. For Luxembourg the date checks prove little yet: each act has one version so far.
- Landed on 17 October: the new home page; a link opens on the article it cites; readable addresses; one
  citation in three forms for copy, export and links; and the search box opens a law from its exact reference (an
  ELI, a CELEX number, a Mémorial issue, "loi du 10 août 1991", "Article 6 of Regulation (EU) 2016/679").
- Your private copy runs today's code since 18 October (a signed release, checked over HTTP), now at law.soufien.lu
  over HTTPS, still open only to this PC and your 14 October address. Built on 18 October: search results by act, long
  codes page by page, the reader of dated versions, quoted and struck-out wording, a lighter rebuild.
- Still thin: Luxembourg has 1,730 acts as first published, no codes; EU text covers 511 of 1,170 versions. The codes
  (121,000 articles) and about 28,000 more articles wait for the rebuild on an Azure machine.
- That rebuild ran on Azure on 19 October and stopped after 72 minutes: one article of the consumer code writes its
  date 20220101, not 2022-01-01, and the build refused it. Fixed on 26 October: both ways of writing a full date are
  read, and no article's date can stop a build again. The rebuild can run again from the same inputs.

## The plan

- Decided by you on 18 October: build on Azure, paid after the credits end; open to the public once V3 is finished,
  within 24 hours, without the lawyer's read or the audits; search engines may index it; the EU wording as recommended.
- The domain's name servers are Azure's since your switch on 18 October, not the registrar's as decided on 14
  October: the name now depends on the subscription staying paid, as V2's did. Keep the payment method valid.
- The full plan is your private page, and C:\lex-v3\PLAN-2026-10-14.md.

## What waits on you

- Now: turn on Azure Compute, with the one Cloud Shell line the driver gave you, so the codes can be rebuilt on an
  Azure machine (about 5 EUR a rebuild).
- Now: the legal notice's details: the publisher's name, a postal address and a contact email to publish.
- Before 12 November: your 175.70 EUR of credits end. Then the site (about 26 EUR a month), the storage (under 0.50 EUR
  a month) and each rebuild are billed to your payment method.
- Your choice, not done before the opening: a lawyer's read of the legal texts, an accessibility audit, user tests.
