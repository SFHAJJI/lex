# Lex V3: the owner's brief

Updated 2026-11-11 at 14:30 UTC by the driver. The driver updates this file in the first pull request of each
day and whenever a run starts, ends or stops. At most 40 lines, plain words, no hashes. The detail stays in
STATUS-WEB.md and STATUS-DATA.md; the acceptance list is LAUNCH-CONTRACT.md.

## Where V3 stands

- law.soufien.lu still serves the second release, live since 31 October and backed up. Its EU data is from 5 October
  and its Luxembourg data from 12 October, so from 18:17 UTC today the pages warn that both are over 30 days old.
- Release A (yesterday's EU data, the third release's code, the assistant and the plainer law page) passed all its
  checks but could not be built: this PC's disk ran out three times at the image step. After 18:17 UTC it could no
  longer go live anyway, so the driver stopped it.
- The fresh Luxembourg collection runs on Azure. It stopped twice on network errors and EUR-Lex refusals this morning;
  the driver found and fixed a fault that made each restart redo hours of work. It is running again.

## The plan

- The Luxembourg collection ends in about a day. Its data, with yesterday's EU data, is then rebuilt on Azure and
  released: both halves fresh, the Constitution, the assistant and the plainer law page.
- That release must be built somewhere with about 20 GB of free disk. This PC has 12 GB.

## What waits on you

- To free disk on this PC (an administrator terminal): "powercfg /h off" frees 6.3 GB; "vssadmin list shadowstorage"
  shows whether restore points hold space.
- Or your yes to build and upload releases on Azure: the build job would read the release signing key and push to
  the registry. This also removes the 2 to 5 hour upload over this PC's Wi-Fi.
- Optional: allow the driver to delete the old third-release data folder (1.6 GB, also kept in Azure).
- Optional: ask Microsoft for modified abuse monitoring, which removes the 30 days the privacy page states.
- Four DNS records for soufien.lu (the commands are in the brief of 31 October).
- Your choice, not done: a lawyer's read, an accessibility audit, user tests.
