// The live page a request for an unreviewed interface language gets: `localization_unavailable`.
//
// Built into `dist-live/` as `locale-<code>.html` for every chrome locale without reviewed copy (French,
// German and Luxembourgish today; Decision 41). It says what the preview's page says
// (`localeUnavailableCopy`), in English and labelled English, under the live banner, since a live page
// never says "synthetic" (Decision 95, ruling 3). It has no script and asks nothing.

import { Document } from './Document.jsx';
import { renderDocument } from './render-document.mjs';
import { skinFor } from '../scripts/shells.mjs';
import { localeUnavailableCopy } from '../scripts/locale-unavailable.mjs';

export function renderLiveLocaleUnavailablePage(requested) {
  const copy = localeUnavailableCopy(requested);
  return renderDocument(
    <Document
      state="live-localization-unavailable"
      title={copy.title}
      shell="dev"
      density={skinFor('dev').density}
      banner="live"
    >
      <h1>{copy.heading}</h1>
      <p className="locale-code">
        <code>{copy.code}</code>
      </p>
      {copy.paragraphs.map((paragraph) => (
        <p key={paragraph}>{paragraph}</p>
      ))}
    </Document>,
  );
}
