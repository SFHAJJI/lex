import assert from 'node:assert/strict';
import test from 'node:test';

import {
  AMBIGUOUS_NOTE,
  FORMS,
  OUT_OF_CORPUS_NOTE,
  UNRECOGNISED_NOTE,
  VERDICTS,
  checkCitation,
  checkQuote,
  outOfCorpusBody,
  parseCitation,
  renderVerdict,
} from '../scripts/citation-checker.mjs';

const ELI = 'http://data.legilux.public.lu/eli/etat/leg/loi/1993/04/05/n1';

function state(overrides = {}) {
  return {
    lex_id: 'lu-legilux:loi-1993-04-05-n1:2025-01-01',
    identifier: ELI,
    valid_from: '2025-01-01',
    valid_to: null,
    ...overrides,
  };
}

test('every citation form this build declares actually parses', () => {
  const cases = {
    lex_id: 'lu-legilux:loi-1993-04-05-n1:2025-01-01',
    permalink: `https://law.soufien.lu/lu-legilux/loi-1915-08-10-n1/2026-06-02--${'7f'.repeat(32)}`,
    lu_eli: ELI,
    lu_eli_dated: `${ELI}/consolide/20250101`,
    eu_celex: '32016R0679',
    eu_celex_consolidated: '02016R0679-20160504',
  };
  for (const [form, raw] of Object.entries(cases)) {
    assert.equal(parseCitation(raw)?.form, form, `${raw} did not parse as ${form}`);
  }
  // Both directions. FORMS is a closed public list, so a name in it that nothing parses is a
  // claim this build cannot keep, and that is how `permalink` sat in the list unimplemented.
  assert.deepEqual([...FORMS].sort(), Object.keys(cases).sort());
});

test('a permalink off the canonical host does not parse, userinfo included', () => {
  const good = `https://law.soufien.lu/lu-legilux/loi-1915-08-10-n1/2026-06-02--${'7f'.repeat(32)}`;
  assert.equal(parseCitation(good).digest, '7f'.repeat(32));
  for (const hostile of [
    good.replace('https://law.soufien.lu', 'https://law.soufien.lu@evil.example'),
    good.replace('https://law.soufien.lu', 'https://evil.example'),
    good.replace('https://', 'http://'),
  ]) {
    assert.equal(parseCitation(hostile), null, `${hostile} parsed as a permalink`);
  }
});

test('a citation embedded in prose is not extracted as if it had been cited alone', () => {
  // The patterns are anchored on purpose, and until this test existed nothing proved it: an
  // unanchored pattern survived a mutation. A checker that finds a CELEX inside a sentence would
  // report a verdict on a reference the writer never made, and the never-list forbids emitting a
  // citation this screen cannot resolve.
  for (const prose of [
    'see 32016R0679 for detail',
    'compare 32016R0679 with 32013R0575',
    'the GDPR (32016R0679) applies',
    '  32016R0679, cited above',
  ]) {
    assert.equal(parseCitation(prose), null, `${prose} was extracted from prose`);
  }
  // But the same identifier alone, with surrounding whitespace, is a citation.
  assert.equal(parseCitation('  32016R0679  ')?.form, 'eu_celex');
});

test('a CELEX cited the publisher way folds to the held key, and keeps the reader spelling', () => {
  // Held EU lex_ids carry the CELEX lowercased. A checker that did not fold would report every
  // correctly written citation as unheld.
  const parsed = parseCitation('32013R0575');
  assert.equal(parsed.celex, '32013R0575');
  assert.equal(parsed.key, '32013r0575');
});

test('a consolidated CELEX names a date and its parts, and invents no base act', () => {
  const parsed = parseCitation('02016R0679-20160504');
  assert.equal(parsed.at, '2016-05-04');
  assert.equal(parsed.sector, '0');
  assert.equal(parsed.year, '2016');
  assert.equal(parsed.type, 'R');
  assert.equal(parsed.number, '0679');
  // The corpus keys EU works by the base act's CELEX, which sits in a different sector. Deriving
  // it here by assuming sector 3 would be resolution dressed as parsing, so the parser must not
  // offer one at all.
  assert.equal(parsed.base, undefined, 'the parser fabricated a base CELEX');
});

test('a CELEX sector is one digit, so the whole identifier parses into its four parts', () => {
  const parsed = parseCitation('32016R0679');
  assert.deepEqual(
    { sector: parsed.sector, year: parsed.year, type: parsed.type, number: parsed.number },
    { sector: '3', year: '2016', type: 'R', number: '0679' },
  );
});

test('a dated LU ELI carries its applicability date', () => {
  assert.equal(parseCitation(`${ELI}/consolide/20250101`).at, '2025-01-01');
});

test('leg and adm produce one work key and two identifiers, which is the collision', () => {
  // The measured latent defect: the work key drops etat/leg and etat/adm, so these two differ
  // only in the segment the key throws away. Pinned here because this screen is where it would
  // reach a reader.
  const leg = parseCitation('http://data.legilux.public.lu/eli/etat/leg/agc/2023/10/06/b3399');
  const adm = parseCitation('http://data.legilux.public.lu/eli/etat/adm/agc/2023/10/06/b3399');
  assert.equal(leg.key, adm.key, 'the work keys were expected to collide');
  assert.notEqual(leg.identifier, adm.identifier, 'the identifiers must stay distinct');
  assert.equal(leg.branch, 'leg');
  assert.equal(adm.branch, 'adm');
});

test('a candidate from the other branch is refused, not rendered', () => {
  // A caller resolving on the work key would hand this back and every field would render
  // consistently around the wrong work.
  assert.throws(
    () =>
      checkCitation({
        raw: 'http://data.legilux.public.lu/eli/etat/leg/agc/2023/10/06/b3399',
        candidates: [
          state({
            identifier: 'http://data.legilux.public.lu/eli/etat/adm/agc/2023/10/06/b3399',
          }),
        ],
      }),
    /etat\/leg and etat\/adm/,
  );
});

test('unrecognised and out-of-corpus are different answers', () => {
  const nonsense = checkCitation({ raw: 'see the blue book, page 12' });
  assert.equal(nonsense.verdict, 'unrecognised');
  assert.equal(nonsense.note, UNRECOGNISED_NOTE);
  assert.equal(nonsense.body, undefined, 'an unrecognised citation must not be linked out');

  const circular = checkCitation({ raw: 'CSSF 20/747' });
  assert.equal(circular.verdict, 'out_of_corpus');
  assert.equal(circular.body.label, 'CSSF circular');
  assert.ok(circular.body.official.startsWith('https://'));
});

test('a recognised citation we hold nothing for is out of corpus, not unrecognised', () => {
  const result = checkCitation({ raw: ELI, candidates: [] });
  assert.equal(result.verdict, 'out_of_corpus');
  assert.equal(result.note, OUT_OF_CORPUS_NOTE);
  assert.equal(result.parsed.form, 'lu_eli');
});

test('more than one candidate lists every one and preselects none', () => {
  const result = checkCitation({
    raw: ELI,
    candidates: [state(), state({ lex_id: 'lu-legilux:loi-1993-04-05-n1:2026-01-01', valid_from: '2026-01-01' })],
  });
  assert.equal(result.verdict, 'ambiguous');
  assert.equal(result.candidates.length, 2);
  assert.equal(result.note, AMBIGUOUS_NOTE);
  assert.equal(result.state, undefined, 'an ambiguous result must not name a single state');

  const html = renderVerdict(result);
  assert.ok(html.includes('2025-01-01') && html.includes('2026-01-01'), 'a candidate was dropped');
  assert.ok(!html.includes('Resolved to'), 'an ambiguous result rendered as resolved');
});

test('one candidate resolves and names its state', () => {
  const result = checkCitation({ raw: ELI, candidates: [state()] });
  assert.equal(result.verdict, 'resolved');
  assert.ok(renderVerdict(result).includes('lu-legilux:loi-1993-04-05-n1:2025-01-01'));
});

test('every verdict this module can return is in the closed list', () => {
  const produced = [
    checkCitation({ raw: 'nonsense here' }).verdict,
    checkCitation({ raw: 'CSSF 20/747' }).verdict,
    checkCitation({ raw: ELI, candidates: [] }).verdict,
    checkCitation({ raw: ELI, candidates: [state(), state({ lex_id: 'x:y:2020-01-01' })] }).verdict,
    checkCitation({ raw: ELI, candidates: [state()] }).verdict,
  ];
  for (const verdict of produced) {
    assert.ok(VERDICTS.includes(verdict), `${verdict} is not a declared verdict`);
  }
  assert.equal(new Set(produced).size, 4, 'the five cases were expected to span four verdicts');
});

test('a candidate a reader cannot identify is refused', () => {
  for (const field of ['lex_id', 'identifier', 'valid_from']) {
    const bad = state();
    bad[field] = '';
    assert.throws(() => checkCitation({ raw: ELI, candidates: [bad] }), new RegExp(field));
  }
});

test('a quote that matches is identical, and one that does not names where', () => {
  assert.deepEqual(checkQuote({ quoted: 'abc', held: 'abc' }), { identical: true, at: null });
  assert.deepEqual(checkQuote({ quoted: 'abXc', held: 'abYc' }), { identical: false, at: 2 });
  // A prefix is not a match, and the offset is where the shorter one ran out.
  assert.deepEqual(checkQuote({ quoted: 'ab', held: 'abc' }), { identical: false, at: 2 });
});

test('a quote comparison against nothing is refused rather than reported identical', () => {
  for (const input of [{ quoted: '', held: 'a' }, { quoted: 'a', held: '' }, { quoted: 'a' }]) {
    assert.throws(() => checkQuote(input), /comparing against nothing|needs a/);
  }
});

test('the verdict card escapes a hostile citation rather than emitting it', () => {
  const html = renderVerdict(checkCitation({ raw: '<script>alert(1)</script>' }));
  assert.ok(!html.includes('<script>alert(1)</script>'));
  assert.ok(html.includes('&lt;script&gt;'));
});

test('an out-of-corpus body is recognised case-insensitively but not over-eagerly', () => {
  assert.ok(outOfCorpusBody('cssf 20/747'));
  assert.ok(outOfCorpusBody('CSSF circular 20/747'));
  assert.equal(outOfCorpusBody('CSSF'), null, 'a bare body name is not a citation');
  assert.equal(outOfCorpusBody('see CSSF 20/747 for detail'), null, 'matched inside prose');
});
