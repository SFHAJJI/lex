// The page the deploy kit can show while the site is being deployed (the specification of record's R60: "a maintenance
// page shows during deployments"; plan item 6.9: it never looks like a legal answer).
//
// It says that the site is being updated and cannot answer for the moment, that the page is not an answer about the law,
// and where the law this site holds is published: the publishers' official sites, as a failure panel names them
// (`OfficialSites`). It asks nothing and ships no script, so it reads the same whatever is or is not running behind it, and
// its footer's freshness strip says it carries no answer (`answers` false).
//
// Built into `dist-live/maintenance/{language}.html`, once per interface language, by `scripts/build-live.mjs`; neither
// server serves it (`scripts/not-found.mjs`, `V3WebRoot.NotFound.cs`). Wiring it in front of a deployment is the deploy
// kit's, and not yet done (STATUS-WEB.md).

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
