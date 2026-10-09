# Lex V3: the owner's brief

Updated 2026-10-09 at 14:50 UTC by the driver. The driver updates this file in the first pull request of each
day and whenever a run starts, ends or stops. At most 40 lines, plain words, no hashes. The detail stays in
STATUS-WEB.md and STATUS-DATA.md; the acceptance list is LAUNCH-CONTRACT.md.

## What a user can do today

Nothing in public yet. V3 is not deployed, and V2 at law.soufien.lu is down: the Azure hosting environment is
still suspended after the subscription was disabled on 2 October, and the soufien.lu name still does not resolve
(checked again on 9 October). On this machine the API and the eight screens work over test data and over one real
bounded mount, the GDPR in English: search, dossier, reading, provision history, compare, radar, trust, export.

## What is running

- The EU population is complete: fetched by 5 October and derived twice with identical results.
- The Luxembourg run read all nine families for the first time, then stopped at its final cross-check on 9 October
  at 02:00 UTC. In the two days it ran, the publisher added 28 new items, including a July 2026 regulation and three
  new consolidated versions, after our list of laws was taken, and the check treated them as an error.
- Before restarting, the driver replayed that run on this machine with the fix. The replays found two more problems
  the run would have hit later: one Code du travail article whose address has an accented letter, and a checksum
  step that tried to hold the whole population in one piece of text. All three are fixed: items added late wait for
  the next run, that one article is left out and recorded, and the checksum now reads the data as it streams.
- A replay with all three fixes then ran through to the first document downloads, where it stopped as planned.
- The fix is merged, and the Luxembourg run restarted fresh on 9 October at 14:35 UTC. It needs about three days
  with the machine awake, so it should end about 12 to 13 October. Please keep the laptop on its charger; on 7
  October its battery ran out and it slept for 8 hours.
- Then the combined mount is derived twice, its journeys, gates and release rehearsal run, and the first real mount
  is served on this machine about 13 to 15 October. Those derives need a lot of memory; closing unused apps helps.

## What waits on the owner

- Azure: the subscription is disabled and read-only; only you can re-enable it, by settling the billing. Then
  the hosting environment can resume and the soufien.lu name be checked. It blocks V2 today and V3 later.
- The release signing key (P-256, in your key vault); the release command is ready to take it.
- The word to go live, once the real mount passes its gates.

## Launch

Target 7 November 2026. It holds if the Luxembourg run finishes by about 13 October and Azure is usable soon.
