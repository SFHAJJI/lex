# Lex V3: the owner's brief

Updated 2026-11-09 at 17:20 UTC by the driver. The driver updates this file in the first pull request of each
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
- The assistant is built and switched off. It needs Azure's AI service enabled on the subscription (yours to do).

## The plan

- Today: the code that makes the refresh runs possible goes through review and merges (written on 28 October).
- Then run 17 on this PC: the EU collection, about 18,500 requests to the Publications Office over 16 to 18 hours.
- Then run 18 on Azure: the full Luxembourg collection with the Constitution, about 25,000 requests to Legilux over
  about 34 hours, at about 36 to 40 node-hours, inside the 150 EUR ceiling.
- While run 18 runs: the fresh EU data with the October Luxembourg data, rebuilt on Azure and released with the third
  release's code, if it can go live before Luxembourg turns stale on 11 November.
- When run 18 ends: the release with both halves fresh and the Constitution, probed, promoted and backed up.
- Your standing yes of 27 October covers the publisher traffic, the Azure cost and the promotion; the driver records it.

## What waits on you

- Enable Azure's AI service on the subscription (one command), so the assistant can be switched on.
- Four DNS records for soufien.lu (the commands are in the brief of 31 October); the driver was refused that change.
- Your choice, not done: a lawyer's read, an accessibility audit, user tests.
