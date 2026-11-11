# Lex V3: the owner's brief

Updated 2026-11-11 at 05:30 UTC by the driver. The driver updates this file in the first pull request of each
day and whenever a run starts, ends or stops. At most 40 lines, plain words, no hashes. The detail stays in
STATUS-WEB.md and STATUS-DATA.md; the acceptance list is LAUNCH-CONTRACT.md.

## Where V3 stands

- law.soufien.lu still serves the second release, live since 31 October 01:15 UTC and backed up. Its EU data is from
  5 October (the pages warn it is over 30 days old); its Luxembourg data turns 30 days old today at 18:17 UTC.
- The EU collection ended last night at 22:12 UTC. With October's Luxembourg data it was rebuilt on Azure overnight as
  release A, which also carries the third release's code, the assistant (a labelled preview) and the plainer law page.
- Release A's check against the live release found one loss: the French text of one version of the EU banking rules
  (Regulation 575/2013 as it stood on 30 September 2021). The EU still publishes it; the collection's one request for
  it was stopped by its own pre-check of the site's robots rules. The English text of that version stays.
- None of the check's four reasons was true of that loss, so the driver added a fifth, "the collection could not fetch
  it". The release lists the loss openly; the French text comes back in the final release.

## The plan

- Release A: the fix is reviewed and merged this morning, then the release is built (2 hours) and uploaded from this PC
  (about 2 hours). It goes live around 12:00 UTC today, before the Luxembourg data turns 30 days old.
- The EU collection is resumed once more to fetch that French text: about 2,100 more requests to the EU publisher, so
  that run makes about 20,700 instead of the 18,500 planned. The driver took this under your standing yes.
- The full Luxembourg collection on Azure ends around 05:00 UTC on 12 November. From 18:17 UTC today until the final
  release, the pages warn that the Luxembourg data is over 30 days old.
- The final release (both halves fresh, the Constitution, the French text back) goes live in the late afternoon or
  evening of 12 November.
- Azure has cost about 25 EUR this month; the runs, rebuilds and assistant add about 45 EUR, under the 150 EUR ceiling.

## What waits on you

- Optional: ask Microsoft for modified abuse monitoring, which removes the 30 days the privacy page now states.
- Optional: allow the driver to delete the old third-release data folder (3.5 GB, also kept in Azure) if disk runs short.
- Four DNS records for soufien.lu (the commands are in the brief of 31 October); the driver was refused that change.
- Your choice, not done: a lawyer's read, an accessibility audit, user tests.
