# Lex V3: the owner's brief

Updated 2026-10-31 at 01:40 UTC by the driver. The driver updates this file in the first pull request of each
day and whenever a run starts, ends or stops. At most 40 lines, plain words, no hashes. The detail stays in
STATUS-WEB.md and STATUS-DATA.md; the acceptance list is LAUNCH-CONTRACT.md.

## Where V3 stands

- law.soufien.lu serves the second release since 31 October 01:15 UTC. New since the first: the Developers, Help and
  Changes pages; records for 22,000 Luxembourg acts Lex holds no text for; Compare showing the changed words; the EU
  Treaties, the Charter and 17,850 more EU articles; the Luxembourg consolidations our reader wrongly refused; acts that
  name private people kept out; checks that compare each release with the one serving. Backed up and verified.
- The second release's first attempt never started on Azure: the server opens its 3 GB of indexes before it listens,
  and one processor needed more than the 4.5 minutes allowed. The server now has two processors and twenty minutes;
  it started in three. Hosting costs about twice what it did, within the 150 EUR ceiling (54 EUR spent this month).
- Merged today for the third release: which acts amend or repeal which, with a notice on acts amended after their
  last held consolidation; the licence rulings L1, L1b and L2 switched on (about 85,000 more Luxembourg articles once
  the data is rebuilt); the privacy sentence on named people; EU act titles and Official Journal references (in review).
- The assistant is built to its contract and switched off: the model only proposes a plan, code checks and runs it,
  every sentence shown is a reviewed template or the law's own words. It can talk to Azure OpenAI once that service
  is enabled on the subscription (yours to do) and ships labelled "preview" until a lawyer and annotators review it.
- Your portfolio is back at https://sfhajji.github.io/ (Lex case study, a guide demo, Energy Copilot, your CV); soufien.lu
  itself answers again once you add four DNS records (the driver may not). Your GitHub profile links are fixed.

## The plan

- Saturday: the data rebuilt on Azure with the third release's rules and indexes, then the third release built, probed
  and promoted. Then the assistant's release, switched on as a preview if the gates pass on the real model.
- After Monday: the EU refresh run (the EU data is 30 days old on 4 November, when pages start to warn) and the full
  Luxembourg refresh with the Constitution; the code for both is written.

## What waits on you

- Enable Azure's AI service on the subscription (one command), so the assistant can be switched on.
- Four DNS records for soufien.lu (the commands are in the brief); the driver was refused that change.
- Your choice, not done: a lawyer's read, an accessibility audit, user tests.
