# Lex V3: the owner's brief

Updated 2026-10-06 at 19:20 UTC by the driver. The driver updates this file in the first pull request of each
day and whenever a run starts, ends or stops. At most 40 lines, plain words, no hashes. The detail stays in
STATUS-WEB.md and STATUS-DATA.md; the acceptance list is LAUNCH-CONTRACT.md.

## What a user can do today

Nothing in public yet. V3 is not deployed, and V2 at law.soufien.lu is down: the Azure hosting environment
is still suspended after the subscription was disabled on 2 October, and the soufien.lu name does not resolve.
On this machine the API and the eight screens work over test data and over one real bounded mount, the GDPR
in English: search, dossier, reading, provision history, compare, radar, trust and coverage, export.

## What is running

- The EU population is complete: fetched by 5 October 04:07 UTC (two network stops on 4 October were resumed
  by a supervisor, nothing lost), then derived twice with identical results at 05:11 UTC.
- The Luxembourg run restarted on 5 October at 17:45 UTC, under a supervisor, on a tool with a reviewed one-line
  fix (the frozen code's allowed exception): some old titles carry a language tag the publisher's engine does not
  type, our reader refused those pages, and 2 of its 9 families could never pass. The first of the two now passes.
  This machine slept 06:03-17:56 UTC on 6 October and the run paused; a helper now holds off idle sleep while runs
  are active (your own sleep still works). It should end late on 8 October; a waiter then builds the canary.
- Then the combined mount is derived twice, its journeys, gates and release rehearsal run, and the first real
  mount is served on this machine between 8 and 10 October. The acquisition code does not change until then.

## What waits on the owner

- Azure: the subscription is disabled and read-only; only you can re-enable it, by settling the billing. Then
  the hosting environment can resume and the soufien.lu name be checked. It blocks V2 today and V3 later.
- The release signing key (P-256, in your key vault); the release command takes it once pull request 931 merges.
- The word to go live, once the real mount passes its gates.

## Launch

Target 7 November 2026. It holds if both runs finish by 10 October and Azure is usable this week.

## Done this week

EU text with its rights notice, the EU time view, exports and the annex control, the French interface (accepted
4 October), browser journeys on every pull request, the release path from custody in CI, and resume from a journal.
