// The React form of S10, measured against its string renderer.
//
// The claim under test is not "React works". It is that the two surfaces apply one implementation
// of the rights rule and produce one page. A composer that told the reader one thing in React and
// another in the string renderer would be worse than having only one of them.

import assert from 'node:assert/strict';
import test from 'node:test';
import { createElement as h } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';

import { ExportComposer } from '../.react-build/app.mjs';
import { renderExportComposer } from '../scripts/export-composer.mjs';

/** React writes `&#x27;` where the string renderer writes `&#39;`; both parse identically. */
const normalise = (html) => html.replaceAll('&#x27;', '&#39;');

const MATTER = { reference: 'M-2026-014', author: 'S. Hajji' };

function item(overrides = {}) {
  return {
    kind: 'publisher_text',
    lex_id: 'lu-legilux:loi-1993-04-05-n1:2025-01-01',
    valid_from: '2025-01-01',
    valid_to: null,
    official_uri: 'https://legilux.public.lu/eli/etat/leg/loi/1993/04/05/n1/consolide/20250101',
    record_sha256: 'a'.repeat(64),
    licence: 'cc-by-4.0',
    attribution: 'Journal officiel du Grand-Duche de Luxembourg',
    text_public: true,
    ...overrides,
  };
}

test('both surfaces draw the same composer, in every shape that makes one lie', () => {
  const shapes = {
    'nothing pinned': { items: [], matter: MATTER },
    'one item that travels whole': { items: [item()], matter: MATTER },
    'withheld by licence': {
      items: [item({ licence: 'licence-scl', record_sha256: 'b'.repeat(64) })],
      matter: MATTER,
    },
    'withheld by rights, on a licence that would have embedded': {
      items: [item({ text_public: false, record_sha256: 'c'.repeat(64) })],
      matter: MATTER,
    },
    'a mixed cart spanning both publishers': {
      items: [
        item(),
        item({ licence: 'licence-scl', record_sha256: 'd'.repeat(64) }),
        item({
          lex_id: 'eu-eurlex:32016R0679:2016-05-04',
          record_sha256: 'e'.repeat(64),
          attribution: 'Publications Office of the European Union',
          official_uri: 'https://eur-lex.europa.eu/eli/reg/2016/679/oj',
        }),
      ],
      matter: MATTER,
    },
    'a closed interval': {
      items: [item({ valid_to: '2026-01-01', record_sha256: 'f'.repeat(64) })],
      matter: MATTER,
    },
  };

  for (const [name, input] of Object.entries(shapes)) {
    assert.equal(
      normalise(renderToStaticMarkup(h(ExportComposer, input))),
      normalise(renderExportComposer(input)),
      `the two renderings of "${name}" differ`,
    );
  }
});

test('every guard refuses the same input in both surfaces', () => {
  const hostile = {
    'a derived join pinned': { items: [item({ kind: 'derived' })], matter: MATTER },
    'an unofficial translation pinned': { items: [item({ kind: 'unofficial' })], matter: MATTER },
    'an unknown licence': { items: [item({ licence: 'cc-by-nc' })], matter: MATTER },
    'no matter reference': { items: [item()], matter: { ...MATTER, reference: '' } },
    'text without attribution': { items: [item({ attribution: '' })], matter: MATTER },
  };

  for (const [name, input] of Object.entries(hostile)) {
    assert.throws(() => renderExportComposer(input), undefined, `string renderer allowed "${name}"`);
    assert.throws(
      () => renderToStaticMarkup(h(ExportComposer, input)),
      undefined,
      `React allowed "${name}"`,
    );
  }
});

test('the rendered page is not trivially small, so parity is not parity between two blanks', () => {
  const html = renderExportComposer({ items: [item()], matter: MATTER });
  assert.ok(html.length > 600, `composer rendered only ${html.length} bytes`);
});
