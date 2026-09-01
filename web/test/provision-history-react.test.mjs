// The React forms of S4 and S12, measured against their string renderers.
//
// The claim under test is not "React works". It is that the two surfaces apply one implementation
// and produce one page, because a framework that quietly becomes a second home for legal rules is
// the worst available outcome of adopting one.

import assert from 'node:assert/strict';
import test from 'node:test';
import { createElement as h } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';

import { GetHelp, ProvisionHistory } from '../.react-build/app.mjs';
import { renderProvisionHistory } from '../scripts/provision-history.mjs';
import { renderGetHelp } from '../scripts/get-help.mjs';

const link = (date, seed) =>
  `https://law.soufien.lu/lu-legilux/loi-2020-07-17-a624/${date}--${seed.repeat(64)}`;

const HISTORY = {
  work: 'lu-legilux:loi-2020-07-17-a624',
  anchor: 'art_18',
  truncated: false,
  distinctTexts: 2,
  states: [
    {
      valid_from: '2023-07-01',
      valid_to: '2024-06-30',
      text_sha256: 'a'.repeat(64),
      article_valid_from: '2023-04-01',
      validity_conflict: true,
      permalink: link('2023-07-01', 'e'),
    },
    {
      valid_from: '2024-07-01',
      valid_to: null,
      text_sha256: 'b'.repeat(64),
      article_valid_from: '2024-07-01',
      permalink: link('2024-07-01', '3'),
    },
  ],
};

const ROUTES = [
  { label: 'Legilux, the publisher', uri: 'https://legilux.public.lu/' },
  { label: 'EUR-Lex', uri: 'https://eur-lex.europa.eu/' },
];

/** React writes `&#x27;` where the string renderer writes `&#39;`; both parse identically. */
const normalise = (html) => html.replaceAll('&#x27;', '&#39;');

test('both surfaces draw the same provision history, in every shape that makes one lie', () => {
  const shapes = {
    'two wordings, one with a validity conflict': HISTORY,
    'nothing held': { ...HISTORY, states: [], distinctTexts: 0 },
    truncated: { ...HISTORY, truncated: true, distinctTexts: 9 },
    renumbered: {
      ...HISTORY,
      anchorEvents: [{ kind: 'renumbered', from: 'art_18', to: 'art_18bis' }],
    },
    'a Union provision': {
      ...HISTORY,
      work: 'eu-eurlex:32016R0679',
      states: HISTORY.states.map((state) => ({
        ...state,
        permalink: state.permalink.replace(
          'lu-legilux/loi-2020-07-17-a624',
          'eu-eurlex/32016R0679',
        ),
      })),
    },
  };

  for (const [name, input] of Object.entries(shapes)) {
    assert.equal(
      normalise(renderToStaticMarkup(h(ProvisionHistory, input))),
      normalise(renderProvisionHistory(input)),
      `the two renderings of "${name}" differ`,
    );
  }
});

test('both surfaces draw the same get-help page, empty and populated', () => {
  const shapes = {
    'the registry as it stands, synthetic only': {
      counters: [{ label: 'Synthetic preview counter', href: 'https://handoff.invalid/one' }],
      officialRoutes: ROUTES,
    },
    'no counters at all': { officialRoutes: ROUTES },
  };

  for (const [name, input] of Object.entries(shapes)) {
    assert.equal(
      normalise(renderToStaticMarkup(h(GetHelp, input))),
      normalise(renderGetHelp(input)),
      `the two renderings of "${name}" differ`,
    );
  }
});

test('every guard refuses the same input in both surfaces', () => {
  // The two implementations share their rules through the model functions. This is what holds
  // them together: if either stops delegating, one of these stops throwing.
  const cases = [
    [HISTORY, { ...HISTORY, truncated: 'no' }, /says whether it was cut/],
    [HISTORY, { ...HISTORY, distinctTexts: 9 }, /one of those two numbers is wrong/],
    [
      HISTORY,
      // distinctTexts follows the state count, or the reconciliation guard fires first and this
      // case never reaches the digest check it was written for.
      { ...HISTORY, distinctTexts: 1, states: [{ ...HISTORY.states[0], text_sha256: 'x' }] },
      /has no text digest/,
    ],
    [HISTORY, { ...HISTORY, work: 'nobody:some-work' }, /not a publisher this interface/],
  ];
  for (const [, bad, pattern] of cases) {
    assert.throws(() => renderProvisionHistory(bad), pattern, 'the string renderer accepted it');
    assert.throws(
      () => renderToStaticMarkup(h(ProvisionHistory, bad)),
      pattern,
      'the React port accepted it',
    );
  }

  const noRoutes = { counters: [], officialRoutes: [] };
  assert.throws(() => renderGetHelp(noRoutes), /lists the publisher routes/);
  assert.throws(() => renderToStaticMarkup(h(GetHelp, noRoutes)), /lists the publisher routes/);
});
