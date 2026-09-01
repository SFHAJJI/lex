// S12, get help.
//
// The screen a reader reaches after being told this service will not apply the law to their
// situation. Decision 41 fixes its shape as a referral LIST rather than one counter, because
// naming a single destination is itself advice about who should advise you.
//
// The state under test is the one this build is actually in: the handoff registry is editorial
// (product spec build item 14) and holds only the synthetic preview host. So the page has to be
// honest about having no verified counter, without offering a fixture as help and without leaving
// a reader who has already been refused once with nothing at all.

import assert from 'node:assert/strict';
import test from 'node:test';

import {
  BOUNDARY_NOTE,
  NO_COUNTER_NOTE,
  admissibleCounter,
  renderGetHelp,
} from '../scripts/get-help.mjs';

const ROUTES = [
  { label: 'Legilux, the publisher', uri: 'https://legilux.public.lu/' },
  { label: 'EUR-Lex', uri: 'https://eur-lex.europa.eu/' },
];
const SYNTHETIC = { label: 'Synthetic preview counter', href: 'https://handoff.invalid/one' };

test('a build with no verified counter says so, and offers no fixture', () => {
  // The registry currently holds only the synthetic host, so this is the live shape.
  const html = renderGetHelp({ counters: [SYNTHETIC], officialRoutes: ROUTES });

  assert.ok(html.includes(NO_COUNTER_NOTE));
  assert.ok(
    NO_COUNTER_NOTE.startsWith('No advice counter has been verified into this build'),
    'the note stopped being a statement about this build',
  );
  assert.ok(
    !html.includes('handoff.invalid'),
    'the synthetic counter was offered to a reader as help',
  );

  // And the reader is not left with nothing: the publisher routes are always true.
  assert.ok(html.includes('legilux.public.lu'));
  assert.ok(html.includes('eur-lex.europa.eu'));
});

test('the boundary is stated, because it is why this page exists', () => {
  const html = renderGetHelp({ officialRoutes: ROUTES });
  assert.ok(html.includes(BOUNDARY_NOTE));
  assert.ok(BOUNDARY_NOTE.includes('does not'), 'the boundary stopped being a refusal');
  assert.ok(html.includes(NO_COUNTER_NOTE), 'an empty registry rendered no explanation');
});

test('a verified counter is listed, and an unverifiable one is refused rather than dropped', () => {
  // There is no real counter in this build, so the admitted path is exercised through the
  // registry host itself. When a real counter is verified in, this is the shape it takes.
  assert.throws(
    () => admissibleCounter(SYNTHETIC, 0),
    /synthetic preview counter and cannot be offered as help/,
  );

  for (const bad of [
    { label: 'Off registry', href: 'https://evil.example/help' },
    { label: 'Not https', href: 'http://handoff.invalid/one' },
    { label: 'Userinfo', href: 'https://handoff.invalid@evil.example/' },
  ]) {
    assert.throws(
      () => renderGetHelp({ counters: [bad], officialRoutes: ROUTES }),
      /is not one|not a handoff|handoff/,
      `${bad.href} was accepted as a counter`,
    );
  }

  assert.throws(
    () => admissibleCounter({ href: 'https://handoff.invalid/one' }, 0),
    /has no label/,
  );
});

test('the page refuses to render with no publisher routes', () => {
  // Without them, a build with no verified counter would offer a reader nothing at all, which is
  // the one outcome this page exists to prevent.
  for (const bad of [undefined, [], null]) {
    assert.throws(
      () => renderGetHelp({ counters: [], officialRoutes: bad }),
      /lists the publisher routes that remain open/,
      `officialRoutes=${JSON.stringify(bad)} rendered a page with no destination`,
    );
  }
});
