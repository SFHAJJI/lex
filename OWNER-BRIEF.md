# Lex V3: the owner's brief

Updated 2026-11-10 at 18:40 UTC by the driver. The driver updates this file in the first pull request of each
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
- The assistant works on a real model and, with your yes of today, goes live with the next release as a labelled
  preview. The privacy page now names Microsoft as the processor of typed questions, kept up to 30 days for abuse checks.
- How a law changes over time is shown as a table for experts; the driver is making it plainer (a clear "in force today",
  read or compare any version, read the law on a date) for the final release.

## The plan

- The EU collection on this PC ends tonight. Its data with October's Luxembourg data then goes live, with the third
  release and the assistant, around 07:00 UTC on 11 November (the backup release).
- The full Luxembourg collection on Azure takes about 51 hours, not 34: it ends around 05:00 UTC on 12 November. From
  18:17 UTC on 11 November until then, the pages warn that the Luxembourg data is over 30 days old.
- The final release, with both halves fresh and the Constitution, goes live around midday on 12 November.
- Azure has cost about 25 EUR this month; the runs, rebuilds and assistant add about 45 EUR, under the 150 EUR ceiling.

## What waits on you

- Optional: ask Microsoft for modified abuse monitoring, which removes the 30 days the privacy page now states.
- Optional: allow the driver to delete the old third-release data folder (3.5 GB, also kept in Azure) if disk runs short.
- Four DNS records for soufien.lu (the commands are in the brief of 31 October); the driver was refused that change.
- Your choice, not done: a lawyer's read, an accessibility audit, user tests.
