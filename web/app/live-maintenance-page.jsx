// The page the deploy kit can show while the site is being deployed (the specification of record's R60: "a maintenance
// page shows during deployments"; plan item 6.9: it never looks like a legal answer).
//
// It says that the site is being updated and cannot answer for the moment, that the page is not an answer about the law,
// and where the law this site holds is published: the publishers' official sites, as a failure panel names them
// (`OfficialSites`). It asks nothing and ships no script, so it reads the same whatever is or is not running behind it, and
// its footer's freshness strip says it carries no answer (`answers` false).
//
// Built into `dist-live/maintenance/{language}.html`, once per interface language, by `scripts/build-live.mjs`, and never
// served at its own address (`scripts/not-found.mjs`, `V3WebRoot.NotFound.cs`). The release's maintenance revision, the same
// image started with `LEX_V3_MAINTENANCE=on`, answers every page request with it, status 503 and `Retry-After`
// (`V3WebRoot.Maintenance.cs`); the deploy kit pre-builds that revision, probes it and keeps it inactive, and prints the
// traffic moves that switch it on and off (`deploy/deploy.ps1 -Maintenance`).

import { Document } from './Document.jsx';
import { liveChrome } from '../scripts/live-chrome.mjs';
import { OfficialSites } from './LiveAnswer.jsx';
import { renderDocument } from './render-document.mjs';
import { skinFor } from '../scripts/shells.mjs';

export function renderLiveMaintenancePage() {
  const copy = liveChrome().maintenance;
  return renderDocument(
    <Document
      state="live-maintenance"
      title={copy.title}
      shell="ask"
      density={skinFor('ask').density}
      banner="live"
      answers={false}
    >
      <p className="eyebrow">{copy.eyebrow}</p>
      <h1>{copy.heading}</h1>
      <p>{copy.intro}</p>
      <OfficialSites />
    </Document>,
  );
}
