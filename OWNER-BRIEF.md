# Lex V3: the owner's brief

Updated 2026-10-28 at 14:10 UTC by the driver. The driver updates this file in the first pull request of each
day and whenever a run starts, ends or stops. At most 40 lines, plain words, no hashes. The detail stays in
STATUS-WEB.md and STATUS-DATA.md; the acceptance list is LAUNCH-CONTRACT.md.

## Where V3 stands

- law.soufien.lu is open to everyone since 27 October 01:08 UTC and indexed by search engines since 04:35 UTC. It
  serves the first release (26 October). Down 3 minutes at 01:10 UTC; the deployment script is fixed.
- Merged on 27 October, waiting for the next release: the EU Treaties, the Charter and 17,850 more EU articles; the
  Luxembourg consolidations (451 files, about 14,400 articles) that our own reader wrongly refused; the text yield per
  class on Coverage; typed answers for everything the programming interface refuses; acts that name private people
  kept out (974 old Luxembourg acts, mostly naturalisations: Lex points to the publisher); checks that compare each
  release with the one serving and refuse an unexplained loss; backups of each release.
- The new data was rebuilt on Azure on 27 October (08:48 to 11:55 UTC): both builds identical, 29 of 30 quality
  checks pass. The one that fails is the Luxembourg date check, whose rewrite you stopped at 02:00 UTC.
- In review: records for 22,049 Luxembourg acts Lex holds no text for (dates, status, official link, why), and
  Compare showing the changed words. In progress: six sentences on the site that said more than Lex does.

## The plan

- Decided by you on 18 October: build on Azure; open once finished; search engines may index it.
- Decided by you on 19 October: texts may be 30 days old before pages warn; the site names no one and gives no
  contact; accessibility "non-conformant" until an audit. The EU texts date from 5 October, so from 4 November
  every page warns and no new release can be promoted until they are collected again.
- On 27 October you asked for V3 finished to the specification within 72 hours (to 30 October 01:35 UTC).
- Next: the second release. Its rehearsal on 28 October passed the quality checks, the rebuild and the signing,
  then failed two of its own probes (one a real fault, now fixed); in review, then the rehearsal runs again.

## What waits on you

- The Luxembourg date check: "continue the gates". Without it the new data cannot be released.
- Two collection runs from the publishers: an EU refresh (needed before 4 November) and the Constitution.
- Rulings L2, L1 and L1b on the Luxembourg licences (each adds texts; all three add about 85,000 articles).
- One sentence for the privacy notice: "An act the check finds to name private persons is not reproduced: Lex
  points to its publisher."
- The monthly cost ceiling (one block in docs/INCIDENT-RUNBOOK.md) and making v3 the default branch, so the outside
  check of the site runs.
- Your choice, not done: a lawyer's read, an accessibility audit, user tests.
