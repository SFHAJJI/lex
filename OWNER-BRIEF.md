# Lex V3: the owner's brief

Updated 2026-10-04 at 14:45 UTC by the driver. The driver updates this file in the first pull request of each
day and whenever a run starts, ends or stops. At most 40 lines, plain words, no hashes. The detail stays in
STATUS-WEB.md and STATUS-DATA.md; the acceptance list is LAUNCH-CONTRACT.md.

## What a user can do today

Nothing in public yet. V3 is not deployed, and V2 at law.soufien.lu is down: the Azure hosting environment
is still suspended after the subscription was disabled on 2 October, and the soufien.lu name does not resolve.
On this machine the API and the eight screens work over test data and over one real bounded mount, the GDPR
in English: search, dossier, reading, provision history, compare, radar, trust and coverage, export.

## What is running

- EU population run 14 stopped on 4 October at 14:20 UTC, after 5 hours: the network to the EU publisher
  failed, and one failed request stops the whole run. It resumed at 14:34 UTC as run 15, with the same tool,
  keeping everything already fetched, and should end about 5 October 10:00 UTC.
- The Luxembourg run is armed again: it starts by itself when the EU run ends and takes about 64 hours, so it
  should end about 8 October. A waiter then builds a small chained canary for the replay gates.
- Then the combined mount is derived twice, its journeys, gates and release rehearsal run, and the first real
  mount is served on this machine between 8 and 10 October. The acquisition code does not change until then.

## What waits on the owner

- Azure: the subscription is disabled and read-only; only you can re-enable it, by settling the billing. Then
  the hosting environment can resume and the soufien.lu name be checked. It blocks V2 today and V3 later.
- The release signing key, a P-256 key in your key vault. The release command's option for it is written and
  green (pull request 931); it is reviewed and merged after the runs.
- The word to go live, once the real mount passes its gates.

## Launch

Target 7 November 2026. It holds if both runs finish by 10 October and Azure is usable this week.

## Done this week

EU text with its rights notice, the EU time view, exports and the annex control, the French interface (accepted
by the owner on 4 October), browser journeys on every pull request, the release path from custody in CI, and
the resume of an interrupted run from its journal, which saved run 14's five hours today.
