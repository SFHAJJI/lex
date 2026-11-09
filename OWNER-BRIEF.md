# Lex V3: the owner's brief

Updated 2026-11-09 at 19:15 UTC by the driver. The driver updates this file in the first pull request of each
day and whenever a run starts, ends or stops. At most 40 lines, plain words, no hashes. The detail stays in
STATUS-WEB.md and STATUS-DATA.md; the acceptance list is LAUNCH-CONTRACT.md.

## Where V3 stands

- law.soufien.lu still serves the second release, live since 31 October 01:15 UTC and backed up.
- Its EU data was collected on 5 October, so since 4 November the pages warn that it is more than 30 days old. Its
  Luxembourg data was collected on 12 October and turns 30 days old on 11 November at 18:17 UTC.
- The third release's code is merged: which acts amend or repeal which, the licence rulings L1, L1b and L2 (about 85,000
  more Luxembourg articles), EU act titles and Official Journal references, and the privacy sentence on named people.
  Its data was rebuilt and passed the checks, but its image build ran out of disk on 31 October.
- A release is only promoted when both halves of its data are under 30 days old. The third release's EU half is not, so
  it cannot go live as it is: the EU and Luxembourg data have to be collected again first.
- The assistant is built and switched off. Azure's AI service is now enabled on the subscription (it was turned on
  for the guide demo), so after the release the driver can give Lex its own EU-only model and switch the assistant on
  as a labelled preview, once its checks pass on the real model.

## The plan

- Run 17 started on this PC today at 18:54 UTC: the EU collection, about 18,500 requests to the Publications Office
  over 16 to 18 hours, so it should end on 10 November around midday. The code it needed was merged just before.
- Run 18 started on Azure today at 19:07 UTC: the full Luxembourg collection with the Constitution, about 25,000
  requests to Legilux over about 34 hours, so it should end on 11 November in the early morning. It does not wait for
  run 17: the two fresh halves are put together afterwards.
- Then the third release's code over both fresh halves: rebuilt on Azure, built here, probed, promoted and backed up,
  aiming to go live before the Luxembourg data turns stale on 11 November at 18:17 UTC.
- Azure has cost 24.50 EUR so far this month (11.20 for this site). The two runs and the rebuild add about 40 EUR, so
  the month should end near 110 EUR, under the 150 EUR ceiling.
- Your standing yes of 27 October covers the publisher traffic, the Azure cost and the promotion; the driver records it.

## What waits on you

- Four DNS records for soufien.lu (the commands are in the brief of 31 October); the driver was refused that change.
- Your choice, not done: a lawyer's read, an accessibility audit, user tests.
