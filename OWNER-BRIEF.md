# Lex V3: the owner's brief

Updated 2026-10-13 at 10:10 UTC by the driver. The driver updates this file in the first pull request of each
day and whenever a run starts, ends or stops. At most 40 lines, plain words, no hashes. The detail stays in
STATUS-WEB.md and STATUS-DATA.md; the acceptance list is LAUNCH-CONTRACT.md.

## What a user can do today

Nothing in public yet. V3 is not deployed, and V2 at law.soufien.lu stays down (its Azure subscription is still
disabled). On this machine the API and the eight screens work over test data and over one real bounded mount.

## What is running

- Both populations are fetched: the EU (5 October) and Luxembourg (12 October, 260,235 records, 11,145 documents).
- The combined EU and Luxembourg data set was built this morning (2.5 GB), from the stored copies only.
- The quality checks over it passed every date test (EU and Luxembourg) but found one real bug: Luxembourg search
  found nothing. The build recorded which Luxembourg text is searchable from a date on each article, and the
  publisher dates the whole document instead. The fix (pull request 948) also dates the law titles, and corrects
  one check that wrongly assumed Word files are never published. EU search was fine.
- Now running: the data set is being rebuilt with the fix (about 1 hour 45, no request to any publisher). Then the
  checks again, the browser walk-throughs over the real data, and the release rehearsal (built twice and compared).
- The canary (two small builds against the live EU and Luxembourg sites, 207 requests each) passed this morning.
- Found for the hosting: with the real data the server needs at least one full processor, not half of one (Azure
  gives a half-processor server too little scratch disk for the data). Its memory is measured before deploying.

## What waits on the owner

- Azure: your new account is set up (budget 30 a month with alerts, the registry, the hosting environment and the
  key vault holding the release signing key). It is a free trial: click "Upgrade to pay-as-you-go" before its credit
  or 30 days run out, or the hosting stops.
- At go-live: point soufien.lu's name servers to the new address book the driver will create (the old one is in
  V2's disabled subscription). Optional: Azure Support can clear the Gmail account's 6.10 EUR invoice.
- The word to go live, once the real data passes its checks and a hidden test copy runs on Azure.

## Launch

Target 7 November 2026. It holds if the rebuild and checks pass this week and the trial is upgraded.
