# Lex V3: the owner's brief

Updated 2026-10-04 by the audit session. The driver updates this file in the first pull request of each day
and whenever a run starts, ends or stops. At most 40 lines, plain words, no hashes. The detail stays in
STATUS-WEB.md and STATUS-DATA.md; the acceptance list is LAUNCH-CONTRACT.md.

## What a user can do today

Nothing in public yet. V3 is not deployed, and V2 at law.soufien.lu is down: the Azure hosting environment
is still suspended after the subscription was disabled on 2 October, and the soufien.lu name does not resolve.
On this machine the API and the eight screens work over test data and over one real bounded mount, the GDPR
in English: search, dossier, reading, provision history, compare, radar, trust and coverage, export.

## What is running

- EU population run 14 started on 4 October at 08:55 UTC and should end about 5 October 09:00 UTC.
- The Luxembourg population run is armed: it starts by itself when the EU run ends and takes about 64 hours,
  so it should end about 8 October. A second waiter then builds a small chained canary (the GDPR and one
  Luxembourg act, at most 800 requests per build) for the replay gates.
- Then the combined mount is derived twice, the journeys and gates run over it, and the release rehearsal
  runs. The first real mount served on this machine is expected between 8 and 10 October.
- Nothing in the acquisition code changes until then, and a run is never restarted for a newer tool.

## What waits on the owner

- Azure: the subscription is enabled again, but the Container Apps environment must be resumed, and the
  registrar's name servers for soufien.lu must be checked. Both block V2 today and V3 later.
- The release signing key: made when the release command can take one; the driver is adding that option.
- The word to go live, once the real mount passes its gates.

## Launch

Target 7 November 2026. It holds if both runs finish by 10 October and Azure is usable this week.

## Done this week

EU text served with its rights notice, the EU time view, EU exports and the annex control, the French
interface (accepted by the owner on 4 October), the journeys in a real browser on every pull request, the
release path from custody end to end in CI, and the resume of an interrupted run from its journal.
