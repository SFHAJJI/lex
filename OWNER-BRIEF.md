# Lex V3: the owner's brief

Updated 2026-10-04 at 16:45 UTC by the driver. The driver updates this file in the first pull request of each
day and whenever a run starts, ends or stops. At most 40 lines, plain words, no hashes. The detail stays in
STATUS-WEB.md and STATUS-DATA.md; the acceptance list is LAUNCH-CONTRACT.md.

## What a user can do today

Nothing in public yet. V3 is not deployed, and V2 at law.soufien.lu is down: the Azure hosting environment
is still suspended after the subscription was disabled on 2 October, and the soufien.lu name does not resolve.
On this machine the API and the eight screens work over test data and over one real bounded mount, the GDPR
in English: search, dossier, reading, provision history, compare, radar, trust and coverage, export.

## What is running

- The EU population run stopped twice on 4 October (14:20 and 15:26 UTC): the network to the EU publisher
  failed, and one failed request stops the whole run. A supervisor now resumes it after each such stop, waiting
  30 to 120 minutes, with the same tool and nothing fetched lost. Run 16 has fetched since 16:12 UTC; with no
  more stops it ends about midday on 5 October.
- The supervisor starts the Luxembourg run when the EU run ends; it takes about 64 hours, so it should end
  about 8 October. A waiter then builds a small chained canary for the replay gates.
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
4 October), browser journeys on every pull request, the release path from custody in CI, and resume from a journal.
