# Lex V3: the owner's brief

Updated 2026-10-13 at 20:25 UTC by the driver. The driver updates this file in the first pull request of each
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
- The release (the server package, signed with your release key in the key vault) builds, verifies and rebuilds
  identically. Its last step, starting the package on this machine and walking through it, failed twice tonight.
  The cause was in the test harness, not the product: each test start left the previous copy running, and by the
  third start this machine's Linux memory was full. The fix (pull request 953) ends each copy before the next.
  The release runs again with it tonight; then the hidden test copy goes onto Azure (no public traffic).
- Azure is ready: pay-as-you-go, with the 30 a month budget alerts, the registry, the hosting environment and the
  key vault holding the release signing key.
- Next: a second small canary (about 400 requests to the live sites) with a Luxembourg law whose dates are
  published, so the history checks can be shown on live data.

## What waits on the owner

- At go-live: point soufien.lu's name servers to the new address book the driver will create (the old one is in
  V2's disabled subscription). Optional: Azure Support can clear the Gmail account's 6.10 EUR invoice.
- The word to go live, once the real data passes its checks and a hidden test copy runs on Azure.
- Optional, money: after go-live the previous version is switched off (about 26 EUR a month in all; going back to it then
  takes about a minute). Keeping it running for an instant switch back would be about 49 EUR a month.

## Launch

Target 7 November 2026. It holds: the data passes its checks; the release and the hidden Azure copy are next.
