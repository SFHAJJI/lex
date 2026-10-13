# Lex V3: the owner's brief

Updated 2026-10-13 at 22:45 UTC by the driver. The driver updates this file in the first pull request of each
day and whenever a run starts, ends or stops. At most 40 lines, plain words, no hashes. The detail stays in
STATUS-WEB.md and STATUS-DATA.md; the acceptance list is LAUNCH-CONTRACT.md.

## What a user can do today

Nothing in public yet. V3 is not deployed, and V2 at law.soufien.lu stays down (its Azure subscription is still
disabled). On this machine the API and the eight screens work over test data and over one real bounded mount.

## What is running

- Both populations are fetched: the EU (5 October) and Luxembourg (12 October, 260,235 records, 11,145 documents).
- The combined data set was rebuilt with the Luxembourg search fix and passes every quality check (all at 100 per
  cent). The browser walk-throughs over the real data pass (29 of 29), and the server opens it in under 1 GB of memory.
- Built twice from the stored copies, the data set came out identical both times (the launch's stability check).
- The release is done: the server package, signed with your release key, built twice identically, and all 29
  walk-throughs pass on this machine, the privacy check included. Two faults were found and fixed on the way: the test
  harness left old copies running, and the server briefly wrote scratch files while answering (pull request 953).
- The hidden test copy on Azure (reachable only from this machine, no public traffic) is ready to deploy, but the
  tool that runs the driver refused the deploy command as an action outside this machine. It needs your yes in the
  tool, or you run the command kept in the releases folder beside the release (C:\lex-v3\releases).
- Azure is ready: pay-as-you-go, with the 30 a month budget alerts, the registry, the hosting environment and the
  key vault holding the release signing key.
- The second small canary ran tonight (414 requests to the live sites, within its limit) with a Luxembourg law
  whose dates are published: the history checks hold on live data.

## What waits on the owner

- At go-live: point soufien.lu's name servers to the new address book the driver will create (the old one is in
  V2's disabled subscription). Optional: Azure Support can clear the Gmail account's 6.10 EUR invoice.
- Allow the hidden Azure deploy (see above), then the word to go live once the hidden copy passes its probes.
- Optional, money: after go-live the previous version is switched off (about 26 EUR a month in all; going back to it then
  takes about a minute). Keeping it running for an instant switch back would be about 49 EUR a month.

## Launch

Target 7 November 2026. It holds: the data and the release pass their checks; the hidden Azure copy is next.
