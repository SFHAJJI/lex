# Lex V3: the owner's brief

Updated 2026-10-06 at 20:20 UTC by the driver. The driver updates this file in the first pull request of each
day and whenever a run starts, ends or stops. At most 40 lines, plain words, no hashes. The detail stays in
STATUS-WEB.md and STATUS-DATA.md; the acceptance list is LAUNCH-CONTRACT.md.

## What a user can do today

Nothing in public yet. V3 is not deployed, and V2 at law.soufien.lu is down: the Azure hosting environment
is still suspended after the subscription was disabled on 2 October, and the soufien.lu name does not resolve.
On this machine the API and the eight screens work over test data and over one real bounded mount, the GDPR
in English: search, dossier, reading, provision history, compare, radar, trust and coverage, export.

## What is running

- The EU population is complete: fetched by 5 October and derived twice with identical results.
- The Luxembourg run restarted on 5 October at 17:45 UTC with a fix for titles carrying a language tag. This
  machine slept 06:03-17:56 UTC on 6 October; a helper now holds off idle sleep while runs are active.
- At 19:04 UTC on 6 October a family was refused again: one 2005 law's title is 2,678 bytes, longer than the
  2,047 our page cursor carries. A second one-line fix keys such rows by a short digest. The run reads its last
  families first, so every page can be checked for any other such shape; then it restarts once, fresh, on the
  fixed tool, for about 64 hours, ending about 10 October. A waiter then builds the canary.
- Then the combined mount is derived twice, its journeys, gates and release rehearsal run, and the first real
  mount is served on this machine about 10 to 12 October. The acquisition code does not change until then.

## What waits on the owner

- Azure: the subscription is disabled and read-only; only you can re-enable it, by settling the billing. Then
  the hosting environment can resume and the soufien.lu name be checked. It blocks V2 today and V3 later.
- The release signing key (P-256, in your key vault); the release command is ready to take it.
- The word to go live, once the real mount passes its gates.

## Launch

Target 7 November 2026. It holds if the Luxembourg run finishes by about 10 October and Azure is usable soon.

## Done this week

EU text with its rights notice, the EU time view, exports and the annex control, the French interface (accepted
4 October), browser journeys on every pull request, the release path from custody in CI, and resume from a journal.
