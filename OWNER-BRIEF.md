# Lex V3: the owner's brief

Updated 2026-10-12 at 18:50 UTC by the driver. The driver updates this file in the first pull request of each
day and whenever a run starts, ends or stops. At most 40 lines, plain words, no hashes. The detail stays in
STATUS-WEB.md and STATUS-DATA.md; the acceptance list is LAUNCH-CONTRACT.md.

## What a user can do today

Nothing in public yet. V3 is not deployed, and V2 at law.soufien.lu is down: the Azure hosting environment is
still suspended after the subscription was disabled on 2 October, and the soufien.lu name still does not resolve
(checked again on 9 October). On this machine the API and the eight screens work over test data and over one real
bounded mount, the GDPR in English: search, dossier, reading, provision history, compare, radar, trust, export.

## What is running

- The EU population is complete: fetched by 5 October and derived twice with identical results.
- The Luxembourg population is complete too, as of 12 October at 18:28 UTC: all nine families, 260,235 records and
  11,145 document bodies (about 3 GB), in 21,972 requests. This week's fixes (old titles, items the publisher added
  during the run, one accented address, a checksum at full scale) all held on the live data.
- Nothing is running now. The last step, building the combined EU and Luxembourg data set on this machine, stopped
  when the disk was almost full: it needs about 20 GB of memory beyond what open apps use, which Windows takes from
  the disk, and the game install left too little. Nothing was lost; that step sends no request to any publisher.
- To finish it: free about 35 to 40 GB of disk (or close most apps, or restart the laptop), then tell the driver to
  derive. It runs twice and compares; the canary, journeys, gates and release rehearsal follow, then serving here.

## What waits on the owner

- Azure: the subscription is disabled and read-only; only you can re-enable it, by settling the billing. Then
  the hosting environment can resume and the soufien.lu name be checked. It blocks V2 today and V3 later.
- The release signing key (P-256, in your key vault); the release command is ready to take it.
- The word to go live, once the real mount passes its gates.

## Launch

Target 7 November 2026. It holds if the combined data set is built this week and Azure is usable soon.
