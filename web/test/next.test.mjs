// The side build ("Lex next"): the copy tables, the dock drawn from every sample answer of the new engine in both
// languages, the three counted pieces and the references panel (`scripts/next-check.mjs`, with no browser), and the
// addresses the pieces ask and open.

import assert from 'node:assert/strict';
import { test } from 'node:test';

import {
  copyKeyDifferences, copyPlaceDifferences, frenchFaultsIn, frenchTypographyFaults, inMarkup, renderAll, unexpectedDigits,
} from '../scripts/next-check.mjs';
import { countedForm, countWord, decimal, fillNext, groupedCount, nextCopy, numericDate } from '../scripts/next-copy.mjs';
import {
  ASK_ADDRESS, answerOpening, askRequest, blockView, footLine, funnelRows, noteLines, outcomeOf, referenceDay, replayHref, replayOf, stepRows, whenOf,
} from '../scripts/next-view.mjs';
import { textHref } from '../scripts/text-address.mjs';
import { loadRenderers, questionAddressFaults, readSamples, storageFaults, syntheticEuropeanAnswer } from '../scripts/next-check.mjs';
import { RESULTS_FIXTURE, freshFaults } from '../scripts/next-check.mjs';
import { readFile } from 'node:fs/promises';
import { CSP_DIRECTIVES } from '../scripts/csp.mjs';
import { buildLine, cleanTitle, confidenceOf, identifierOf, marksOf, searchedView, shortTitle, wordingLine } from '../scripts/next-view.mjs';

test('the French and the English tables hold the same keys, and their templates name the same places', () => {
  assert.deepEqual(copyKeyDifferences(), []);
  assert.deepEqual(copyPlaceDifferences(), []);
});

test('the French table keeps French typography: no-break spaces before : ; ! ? % » and after «, no straight apostrophe', () => {
  assert.deepEqual(frenchTypographyFaults(), []);
  // The check itself sees each fault it is there for.
  assert.equal(frenchFaultsIn('Lex a compris :').length, 1);
  assert.equal(frenchFaultsIn('Lex a compris :').length, 0);
  assert.equal(frenchFaultsIn("l'article").length, 1);
  assert.equal(frenchFaultsIn('« à vérifier »').length, 2);
  assert.equal(frenchFaultsIn('« à vérifier »').length, 0);
});

test('no count is typed in the copy: every digit in it is one of the known ones', () => {
  assert.deepEqual(unexpectedDigits(), []);
});

test('a counted sentence takes its form from the count, in each language', () => {
  const fr = nextCopy('fr').dock.calls;
  assert.equal(countedForm(fr, 0, 'fr'), 'aucun appel au modèle');
  assert.equal(countedForm(fr, 1, 'fr'), '1 appel au modèle');
  assert.equal(countedForm(fr, 3, 'fr'), '3 appels au modèle');
  assert.equal(countedForm(nextCopy('en').dock.funnel.read, 0, 'en'), 'articles read by the model');
  assert.equal(countedForm(nextCopy('fr').dock.funnel.read, 0, 'fr'), 'article lu par le modèle');
  assert.equal(groupedCount(85594, 'fr'), '85 594');
  assert.equal(groupedCount(85594, 'en'), '85,594');
  assert.equal(decimal(0.01086, 'fr'), '0,01086');
  assert.equal(numericDate('2025-11-25'), '25/11/2025');
  assert.equal(countWord(6, 'fr'), 'Six');
  assert.throws(() => fillNext('{from} to {to}', { from: 'a' }), /no value for \{to\}/u);
});

test('the date Lex understood is said as a year, a month, a day or today', () => {
  assert.equal(whenOf({ from: '2014-01-01', to: '2014-12-31', grain: 'year', text: '2014' }, 'fr'), 'année 2014');
  assert.equal(whenOf({ from: '2014-03-01', to: '2014-03-31', grain: 'month', text: 'mars 2014' }, 'fr'), 'mars 2014');
  assert.equal(whenOf({ from: '2014-03-05', to: '2014-03-05', grain: 'day', text: '5 mars 2014' }, 'fr'), '05/03/2014');
  assert.equal(whenOf({ from: '2026-11-20', to: '2026-11-20', grain: 'day', text: null }, 'fr'), 'aujourd’hui');
  // A day the reader chose is said as that day, never as today.
  assert.equal(whenOf({ from: '2015-06-01', to: '2015-06-01', grain: 'day', text: null }, 'fr', '2015-06-01'), '01/06/2015');
  assert.equal(whenOf({ from: '2014-01-01', to: '2014-12-31', grain: 'year', text: '2014' }, 'en'), 'year 2014');
});

test('a question travels in the body of a POST, never in an address; the replay link holds it after "#"', () => {
  // The privacy notice's rule: a reader's words go in the body of the request, never in the request's address.
  const request = askRequest('  Que dit la loi\u00A0? ', null);
  assert.equal(request.address, '/api/next/ask');
  assert.equal(request.address, ASK_ADDRESS);
  assert.equal(request.init.method, 'POST');
  assert.equal(request.init.headers['content-type'], 'application/json');
  assert.deepEqual(JSON.parse(request.init.body), { question: 'Que dit la loi ?', date: null });
  assert.deepEqual(JSON.parse(askRequest('Que dit la loi ?', '2015-06-01').init.body), { question: 'Que dit la loi ?', date: '2015-06-01' });
  assert.deepEqual(JSON.parse(askRequest('Que dit la loi ?', 'yesterday').init.body), { question: 'Que dit la loi ?', date: null });
  // The replay link: the question and the day after "#", the part of an address a browser never sends.
  assert.equal(replayHref('Que dit la loi ?', '2026-11-20', 'fr'), '/fr/#q=Que%20dit%20la%20loi%20%3F&on=2026-11-20');
  assert.deepEqual(replayOf('#q=Que%20dit%20la%20loi%20%3F&on=2026-11-20'), { question: 'Que dit la loi ?', day: '2026-11-20' });
  // Not a replay: no day, a day that is none, a search's fragment (the search page's own), a query, nothing.
  assert.equal(replayOf('#q=Bonjour'), null);
  assert.equal(replayOf('#q=Bonjour&on=tomorrow'), null);
  assert.equal(replayOf('#q=contrat%20d%27assurance&lang=fra'), null);
  assert.equal(replayOf('#q=Bonjour&on=2026-11-20&lang=fra'), null);
  assert.equal(replayOf('?q=Bonjour&on=2026-11-20'), null);
  assert.equal(replayOf('#article-art_21'), null);
  assert.equal(replayOf(''), null);
  // No source of the dock writes a question into a request's address or a page's query.
  assert.deepEqual(questionAddressFaults(), []);
  assert.equal(referenceDay({ row: { date: { from: '2026-11-20', grain: 'day', text: null } } }, null, '2026-11-21'), '2026-11-20');
  assert.equal(referenceDay({ row: { date: { from: '2014-01-01', grain: 'year', text: '2014' } } }, null, '2026-11-21'), '2026-11-21');
  assert.equal(referenceDay({ row: { date: { from: '2014-01-01', grain: 'year', text: '2014' } } }, '2015-06-01', '2026-11-21'), '2015-06-01');
});

test('a block opens its article on the reading page, by the names the current site gives the text and the article', () => {
  const view = blockView({ role: 'rule', work: 'loi-1997-07-27-n10', anchor: 'art_21', date: '2025-11-25', article: 'Article 21', heading: 'Défaut de paiement de la prime', act_title: 'Loi du 27 juillet 1997 sur le contrat d\'assurance.', path: 'TITRE I > Chapitre II', version_from: '2025-11-25', version_to: null, official: 'https://legilux.public.lu/eli/etat/leg/loi/1997/07/27/n10/jo', lines: [{ n: 1, text: 'A' }, { n: 3, text: 'C' }] }, 'fr');
  assert.deepEqual(view.opening, { identifier: '/lu-legilux/loi-1997-07-27-n10', article: 'art_21', date: '2025-11-25' });
  assert.equal(textHref(view.opening, 'fr'), '/fr/text.html?text=%2Flu-legilux%2Floi-1997-07-27-n10&date=2025-11-25&article=art_21');
  assert.equal(view.label, 'La règle');
  assert.equal(view.cite, 'Article 21, Défaut de paiement de la prime.');
  assert.equal(view.version, 'Version applicable depuis le 25/11/2025.');
  assert.equal(view.path, 'TITRE I › Chapitre II');
  // Lines 1 and 3: the line skipped between them is said as a gap.
  assert.deepEqual(view.lines.map((line) => (line.gap ? '…' : line.text)), ['A', '…', 'C']);
  assert.equal(blockView({ role: 'candidate', work: null, anchor: 'art_1', lines: [] }, 'fr').opening, null);
  assert.equal(blockView({ role: 'rule', article: 'Art. L. 124-3.', heading: null, version_from: '2011-04-16', version_to: '2013-01-01', lines: [] }, 'fr').cite, 'Art. L. 124-3.');
  assert.equal(blockView({ role: 'rule', version_from: '2011-04-16', version_to: '2013-01-01', lines: [] }, 'fr').version, 'Version applicable du 16/04/2011 au 01/01/2013.');
  // A title written over several lines, or with a written "\n", is read on one line.
  assert.equal(blockView({ role: 'rule', act_title: 'Loi modifiée du 30 mai 2005\\n - relative aux dispositions', lines: [] }, 'fr').title, 'Loi modifiée du 30 mai 2005 - relative aux dispositions.');
});

test('a European text opens on the reading page by its CELEX alone, its article by its three digits, at the date given', () => {
  const block = { role: 'candidate', act_id: 'celex:32016R0679', work: '32016R0679', anchor: '033', date: '2018-05-25', official: 'https://eur-lex.europa.eu/eli/reg/2016/679/oj', lines: [] };
  const view = blockView(block, 'fr');
  assert.deepEqual(view.opening, { identifier: '32016R0679', article: '033', date: '2018-05-25' });
  assert.equal(textHref(view.opening, 'fr'), '/fr/text.html?text=32016R0679&date=2018-05-25&article=033');
  assert.equal(view.official, 'https://eur-lex.europa.eu/eli/reg/2016/679/oj', 'the official text as the block gives it');
  assert.equal(identifierOf('32016R0679'), '32016R0679');
  assert.equal(identifierOf(null, 'celex:32022R2554'), '32022R2554');
  assert.equal(identifierOf('loi-1997-07-27-n10', '/eli/etat/leg/loi/1997/07/27/n10'), '/lu-legilux/loi-1997-07-27-n10');
  assert.equal(identifierOf('code-travail'), '/lu-legilux/code-travail');
  assert.equal(identifierOf(null), null);
  // The lines a European answer quotes are said to the page under the same name.
  assert.deepEqual(marksOf({ blocks: [{ ...block, lines: [{ n: 1, text: 'Article 33' }] }] }, '32016R0679'), [{ article: '033', text: 'Article 33' }]);
});

test('a title in a list of the dock is cut short at a word, about sixty characters, closed with an ellipsis', () => {
  assert.equal(shortTitle('Code du travail'), 'Code du travail');
  assert.equal(shortTitle('Loi du 10 août 1992 portant création de l’entreprise des postes et télécommunications.'), 'Loi du 10 août 1992 portant création de l’entreprise des…');
  assert.equal(shortTitle('Loi du 20 juin 2020 portant\\n1° dérogation temporaire à certaines dispositions en matière '), 'Loi du 20 juin 2020 portant 1° dérogation temporaire à…');
  assert.ok(shortTitle('x'.repeat(200)).length <= 61);
  assert.equal(cleanTitle('Loi du 23 juillet 2024 portant modification et modifiant certaines …'), 'Loi du 23 juillet 2024 portant modification et modifiant certaines…');
});

test('only an asserted answer opens its first article; a text not held is said before any outcome', () => {
  const block = { role: 'rule', work: 'code-travail', anchor: 'art_l_124-3', date: '2026-07-01', lines: [] };
  assert.deepEqual(answerOpening({ outcome: 'answer', blocks: [block] }, 'fr'), { identifier: '/lu-legilux/code-travail', article: 'art_l_124-3', date: '2026-07-01' });
  assert.equal(answerOpening({ outcome: 'candidates', blocks: [block] }, 'fr'), null);
  assert.equal(answerOpening({ outcome: 'partial', blocks: [block] }, 'fr'), null);
  const held = outcomeOf({ outcome: 'candidates', not_held: { act_title: 'Code du travail', earliest_version: '2020-11-01', period: ['2014-01-01', '2014-12-31'] } }, 'fr');
  assert.equal(held.key, 'not_held');
  assert.match(held.more, /« Code du travail » date du 01\/11\/2020/u);
  assert.equal(outcomeOf({ outcome: 'answer' }, 'fr').lead, 'D’après le texte officiel.');
  assert.equal(outcomeOf({ outcome: 'answer' }, 'fr').card, false);
  assert.equal(outcomeOf({ outcome: 'not_found', not_found: { searched_rows: 85594, period: ['2014-01-01', '2014-12-31'] } }, 'fr').more, 'Lex a lu les articles les plus proches parmi 85 594 passages, pour la période du 01/01/2014 au 31/12/2014. Cela ne veut pas dire que la loi ne prévoit rien.');
  assert.equal(outcomeOf({ outcome: 'something_new' }, 'fr'), null);
  assert.equal(outcomeOf({ outcome: 'not_found' }, 'fr').lead, 'Lex n’a pas trouvé de réponse dans les textes lus.');
  // Under an answer that found nothing, a passage is a close one, never a proposed one.
  assert.equal(blockView({ role: 'candidate', lines: [] }, 'fr', 'not_found').label, 'Passage proche');
  assert.equal(blockView({ role: 'candidate', lines: [] }, 'fr', 'candidates').label, 'Passage proposé');
});

test('the notes, the funnel, the steps and the foot line are read from the answer, and an unknown step is shown by its name', () => {
  const answer = {
    outcome: 'answer', notes: ['exception_not_this_case', 'personal_case', 'a_note_of_tomorrow'], row: { personal_case: true },
    blocks: [{ role: 'rule', lines: [{ n: 1, text: 'A' }, { n: 2, text: 'B' }] }],
    see_also: [{ article: 'Article 126', heading: 'Application', why: 'Article 126 déroge à Article 105' }],
    candidates: [{ label: 'A3', article: 'Article 105' }],
    trace: {
      ms: 3936, model_calls: 3, cost_usd_list_price: 0.01015,
      steps: [
        { step: 'find_meaning', who: 'code+embedding', ms: 164, detail: { index_rows: 85594, kinds_of_text_searched: ['general'], rows_in_scope: 63866, rows_kept: 40, articles_kept: 8, acts: 2 } },
        { step: 'read', who: 'model', ms: 1448, detail: { articles_read: 8, pointed: 'A3', lines: [2, 3], answer_found: true, complete_answer_found: true } },
        { step: 'a_step_of_tomorrow', who: 'code', ms: null, detail: { anything: 1 } },
      ],
    },
  };
  assert.deepEqual(noteLines(answer, 'fr').map((note) => note.key), ['personal_case', 'exception_not_this_case', 'see_also']);
  assert.equal(noteLines(answer, 'fr').at(-1).text, 'Voir aussi : Article 126 (Application), Article 126 déroge à Article 105');
  assert.deepEqual(funnelRows(answer, 'en').map((row) => `${row.count} ${row.label}`), [
    '85,594 passages of law in the search', '63,866 in the right kinds of text', '8 articles proposed by the three searches', '8 articles read by the model',
    '1 article quoted in the answer', '2 lines copied from the law',
  ]);
  const steps = stepRows(answer, 'fr');
  assert.deepEqual(steps.map((row) => [row.name, row.who, row.time]), [['Le sens', 'both', '164 ms'], ['Lecture', 'model', '1,4 s'], ['a step of tomorrow', 'code', null]]);
  // The reading names an article by its place in the list it was handed ("A3"): the row says the article's own name.
  assert.equal(steps[1].line, '8 articles lus → Article 105, lignes 2, 3 ; réponse trouvée : oui ; complète : oui');
  assert.equal(steps[2].line, '');
  assert.equal(footLine(answer, 'fr'), '3 appels au modèle · 3,9 s · coût au tarif public : 0,01015 USD (non encore comparé à la facture)');
  assert.deepEqual(funnelRows({ trace: { steps: [] } }, 'fr'), []);
  // Nothing found: the closest passages are not counted as quoted in an answer, nor their lines as copied.
  assert.deepEqual(funnelRows({ ...answer, outcome: 'not_found' }, 'en').map((row) => row.key), ['index', 'scope', 'proposed', 'read']);
});

test('the search by meaning is said in one sentence, and the absence of an answer has a row of its own', () => {
  const answer = {
    outcome: 'not_found', row: {}, blocks: [],
    trace: { steps: [
      { step: 'find_meaning', who: 'code+embedding', ms: 2017, detail: { index_rows: 97578, kinds_of_text_searched: ['general'], rows_in_scope: 71717, rows_kept: 40, articles_kept: 12, acts: 11 } },
      { step: 'find_meaning', who: 'code+embedding', ms: 2017, detail: { index_rows: 97578, kinds_of_text_searched: ['general', 'repealed'], rows_in_scope: 81637, rows_kept: 1, articles_kept: 1, acts: 1 } },
      { step: 'absence', who: 'code', ms: null, detail: { answer_seen_in_the_articles_read: false, articles_read: 12, texts_read: 11 } },
      { step: 'absence', who: 'code', ms: null, detail: { answer_seen_in_the_articles_read: false } },
    ] },
  };
  const fr = stepRows(answer, 'fr');
  assert.equal(fr[0].line, 'les 40 passages les plus proches par le sens, puis 12 articles de 11 textes avec les deux autres recherches');
  assert.equal(fr[1].line, 'le passage le plus proche par le sens, textes particuliers demandés compris, puis 1 article de 1 texte avec les deux autres recherches');
  assert.deepEqual([fr[2].name, fr[2].who, fr[2].time, fr[2].line], ['Absence', 'code', null, '12 articles de 11 textes lus ; aucune réponse']);
  assert.equal(fr[3].line, '', 'an absence that says no count says nothing');
  assert.equal(stepRows(answer, 'en')[2].line, '12 articles from 11 texts read; no answer');
  // The note of a text made for one group, and the note of no answer in the articles read (said by the outcome alone).
  assert.deepEqual(noteLines({ notes: ['text_for_a_special_group', 'no_answer_in_the_articles_read'] }, 'fr'), [
    { key: 'text_for_a_special_group', text: 'Ce texte ne vise qu’un groupe particulier ; il n’est pas présenté comme la réponse à une question générale.' },
  ]);
});

test('the dock, the counted pieces and the references panel render from their answers in both languages, with no fault', async () => {
  const { renderings, samples } = await renderAll({ tag: 'test' });
  assert.equal(samples, 14, 'the fourteen sample answers of the new engine');
  assert.deepEqual(renderings.filter((rendering) => rendering.faults.length > 0).map((rendering) => `${rendering.name} (${rendering.locale}): ${rendering.faults.join('; ')}`), []);
  const of = (name, locale) => renderings.find((rendering) => rendering.name === name && rendering.locale === locale).html;

  // The shell is the current dock's: the same root classes, head, body, field.
  const first = of('dock-state-first', 'fr');
  for (const mark of ['class="lx-dock-inner"', 'class="lx-dock-head"', 'class="lx-dock-title"', 'class="lx-dock-notice"', 'class="lx-dock-hide"', 'class="lx-dock-body"', 'class="lx-dock-ask"', 'class="lx-dock-field"', 'id="lx-dock-q"', 'class="lx-dock-submit"', 'class="lx-dock-example"']) {
    assert.ok(first.includes(mark), `the first screen holds ${mark}`);
  }
  assert.ok(of('dock-state-pending', 'fr').includes('class="lx-dock-card lx-dock-loading"'));
  assert.ok(of('dock-state-pending', 'fr').includes('class="lx-dock-skeleton"'));
  assert.ok(of('dock-state-rate', 'fr').includes(inMarkup(nextCopy('fr').dock.rateLimited)));
  assert.ok(of('dock-state-folded', 'fr').includes('class="lx-dock-tab"'));
  assert.ok(of('dock-state-kept', 'fr').includes('data-dock-reopen=""'));

  // An answer with an exception: the cards in the order given, each role named, the rule's lines marked.
  const answer = of('dock-answer_with_exception', 'fr');
  const roles = [...answer.matchAll(/data-dock-card="block" data-role="([a-z_]+)"/gu)].map((match) => match[1]);
  assert.deepEqual(roles, ['rule', 'related', 'related', 'exception', 'definition']);
  assert.ok(answer.includes('<span class="lx-dock-verdict">La règle</span>'));
  assert.ok(answer.includes('<span class="lx-dock-verdict">Exception</span>'));
  assert.ok(answer.includes('<mark class="lx-dock-mark">A défaut de paiement'));
  assert.ok(answer.includes('Version applicable depuis le 25/11/2025.'));
  assert.ok(answer.includes('href="/fr/#q=Mon%20assureur%20peut-il%20suspendre%20ma%20garantie%20si%20je%20ne%20paie%20pas%20ma%20prime%20%3F&amp;on=2026-11-20"'));
  assert.ok(first.includes(inMarkup('le serveur ne garde ni question ni réponse')), 'the notice in the head says where the conversation is kept');
  assert.ok(answer.includes('data-dock-note="personal_case"'));

  // A text not held for the date asked: said in a card of its own, the passage a proposed one, in English too.
  const held = of('dock-not_held_for_date', 'en');
  assert.ok(held.includes('data-dock-outcome="not_held"'));
  assert.ok(held.includes('date: year 2014'));
  assert.ok(held.includes('<span class="lx-dock-verdict">Proposed passage</span>'));
  // The law's own words are marked French on an English page.
  assert.ok(held.includes('class="lx-dock-extract" data-dock-extract="art_l_121-5" lang="fr"'));

  // Nothing found: the sentence first, then what Lex searched, then the closest passages under their heading; the steps on
  // two lines each, the absence of an answer among them.
  const nothing = of('dock-not_found_with_evidence', 'fr');
  const at = (mark) => nothing.indexOf(mark);
  assert.ok(nothing.includes(inMarkup('Lex n’a pas trouvé de réponse dans les textes lus.')));
  assert.ok(at('data-dock-card="outcome"') < at('data-dock-card="searched"'));
  assert.ok(at('data-dock-card="searched"') < at('data-dock-passages=""'));
  assert.ok(at('data-dock-passages=""') < at('data-dock-card="block"'), 'never a passage before the evidence');
  assert.ok(nothing.includes('<h3 class="lx-dock-passages-heading" data-dock-passages="">Les passages les plus proches, qui ne répondent pas</h3>'));
  assert.ok(nothing.includes('<span class="lx-dock-verdict">Passage proche</span>'));
  assert.ok(!nothing.includes('Passage proposé'));
  assert.ok(nothing.includes('<li class="lx-dock-row lx-dock-step" data-stage="absence"><span class="lx-dock-step-head"><span class="lx-dock-row-name">Absence</span><span class="lx-dock-tag" data-stage-by="code">code</span><span class="lx-dock-row-time"></span></span><span class="lx-dock-row-line">12 articles de 11 textes lus ; aucune réponse</span></li>'));
  assert.ok(nothing.includes('<li><span>Loi du 10 août 1992 portant création de l’entreprise des…</span> : aucune partie nommée</li>'));
  assert.ok(!nothing.includes('data-funnel="quoted"') && !nothing.includes('data-funnel="lines"'));
  // A step with its time on the first line, beside its tag.
  assert.ok(of('dock-answer_value', 'fr').includes('<span class="lx-dock-row-name">Le sens</span><span class="lx-dock-tag lx-dock-tag-model" data-stage-by="both">code + modèle</span><span class="lx-dock-row-time">5,0 s</span>'));
  // An amendment is always "amended by" its text, whatever the kind the publisher records; an ellipsis ends the sentence.
  const caution = of('dock-answer_caution_amended', 'fr');
  assert.ok(caution.includes('data-dock-wording="">Rédaction inchangée depuis le 01/08/2024 ; modifié par Loi du 23 juillet 2024 portant modification'));
  assert.ok(caution.includes('modifiant certaines…</p>'));
  assert.ok(!/inséré par|remplacé par/u.test(caution));
  // A note for a text made for one group; a European text's article opened by its CELEX.
  assert.ok(of('dock-synthetic-special-group', 'fr').includes(inMarkup('Ce texte ne vise qu’un groupe particulier ; il n’est pas présenté comme la réponse à une question générale.')));
  const europe = of('dock-synthetic-european-blocks', 'fr');
  assert.ok(europe.includes('href="/fr/text.html?text=32016R0679&amp;date=2018-05-25&amp;article=033"'));
  assert.ok(europe.includes('href="https://eur-lex.europa.eu/eli/reg/2016/679/oj"'));
  assert.ok(!europe.includes('lu-legilux'));

  // The counted pieces: the corpus section shows nothing when the counts could not be read.
  assert.equal(of('corpus-failed', 'fr'), '');
  assert.ok(of('how', 'fr').includes('Six choix, et pourquoi'));
  assert.equal((of('how', 'fr').match(/class="lx-how-stage"/gu) ?? []).length, 8);
  assert.equal((of('how-loading', 'fr').match(/data-brick-counts/gu) ?? []).length, 0, 'no count is shown before the engine answered');
  assert.ok(of('results-none', 'fr').includes(nextCopy('fr').results.none));

  // A question that names a European text Lex does not hold (a synthetic answer in the engine's shape): the Luxembourg answer
  // is kept whole, the engine's sentence stands above it, and its step says which text is not held (release-a).
  const european = of('dock-synthetic-eu-not-held', 'fr');
  assert.ok(european.includes('data-dock-eu-not-held=""'));
  assert.ok(european.includes(inMarkup('La directive Solvabilité II (2009/138/CE) n’est pas détenue par Lex ; voici la loi luxembourgeoise qui la transpose.')));
  assert.ok(european.includes('data-stage="european_text"'));
  assert.ok(european.includes(inMarkup('non détenu par Lex : 32009L0138')));
  assert.ok(!european.includes('ne lit pas encore'));
  assert.ok(!of('dock-european_not_read', 'fr').includes('ne lit pas encore'), 'an answer of the earlier engine is drawn without the false sentence');
  // A repealed text: its card says so, with the day, the repealing text and its article; and a law the question names.
  const repealed = of('dock-synthetic-repealed', 'fr');
  assert.ok(repealed.includes('data-dock-repeal="">Texte abrogé à partir du 01/01/2016 par Loi du 7 décembre 2015 sur le secteur des assurances (Art. 323).</p>'));
  assert.ok(repealed.includes(inMarkup('Le texte « Loi du 6 décembre 1991 sur le secteur des assurances » est abrogé à partir du 01/01/2016 par Loi du 7 décembre 2015 sur le secteur des assurances (Art. 323).')));
  assert.ok(of('dock-synthetic-repealed', 'en').includes('Text repealed from 01/01/2016 by Loi du 7 décembre 2015 sur le secteur des assurances (Art. 323).'));
  // Nothing found: what Lex does not hold and the codes' first dates, a name the copy does not know left out.
  const coverage = of('dock-synthetic-coverage', 'fr');
  assert.ok(coverage.includes(inMarkup('Non détenus par Lex : la directive Solvabilité II, la directive sur la distribution d’assurances (IDD), le règlement PRIIPs, les règlements du Commissariat aux assurances (CAA), le Code de la sécurité sociale, le Code de la route, et 16 284 des 24 644 lois et règlements grand-ducaux que liste le catalogue de Legilux.')) || coverage.includes('16 284 des 24 644'), coverage.slice(coverage.indexOf('data-dock-coverage'), coverage.indexOf('data-dock-coverage') + 600));
  assert.ok(coverage.includes(inMarkup('Les codes ne sont tenus qu’à partir d’une date : Code civil depuis le 01/09/2016 ; Code du travail depuis le 01/11/2020.')));
  assert.ok(!coverage.includes('a_text_of_tomorrow'));
  assert.equal(outcomeOf(syntheticEuropeanAnswer({ outcome: 'answer', notes: [], blocks: [], row: {}, trace: { steps: [] } }), 'en').key, 'answer');

  // Results: the fresh test first, then the same questions to the live Lex and to this version, then the bench; the groups
  // named in words; what each brick adds, one row with everything on and one a brick switched off.
  const results = of('results', 'fr');
  assert.ok(results.indexOf('data-results-fresh') < results.indexOf('data-results-independent'), 'the fresh test stands before the earlier questions');
  assert.ok(results.indexOf('data-results-independent') < results.indexOf('data-results-bench'), 'the independent test stands before the bench');
  assert.ok(results.includes(inMarkup(nextCopy('fr').results.independent.heading)));
  assert.ok(of('results-synthetic-independent', 'fr').includes('<th scope="row">bonne réponse affirmée</th><td>14</td><td>45</td>'));
  assert.ok(!of('results-no-independent', 'fr').includes('data-results-independent'));
  assert.ok(results.includes('Questions neuves, assurance'));
  assert.ok(!results.includes('G1 / insurance'));
  assert.ok(of('results-synthetic-independent', 'fr').includes('<th scope="row">Tout allumé</th>'));
  assert.ok(of('results-synthetic-independent', 'fr').includes('<th scope="row">Sans les deux garde-fous de décision</th>'));
  assert.ok(of('how', 'fr').includes('data-limit="european"'));

  // The references panel: a link where the article can be opened, none where it cannot.
  const references = of('references', 'fr');
  assert.ok(references.includes('href="/fr/text.html?text=%2Flu-legilux%2Floi-1997-07-27-n10&amp;date=2025-11-25&amp;article=art_24"'));
  assert.ok(references.includes('<span class="lx-refs-what" lang="fr">article 5 de la loi du 8 décembre 1994</span>'));
  assert.ok(references.includes('data-certainty="unsure">à vérifier'));
});

test('the dock asks once, by POST on its own origin with the question in the body, and reads the answer, too many questions, and a failure apart', async () => {
  const { fr } = await loadRenderers('test-ask');
  const [{ answer }] = await readSamples();
  const asked = [];
  const answering = (status, body) => async (address, init) => {
    asked.push([address, init.method, init.headers['content-type'], JSON.parse(init.body)]);
    return { status, ok: status >= 200 && status < 300, json: async () => body };
  };
  const done = await fr.runQuestion(answer.question, null, answering(200, answer));
  assert.equal(done.kind, 'done');
  assert.equal(done.result, answer);
  assert.deepEqual(asked, [['/api/next/ask', 'POST', 'application/json', { question: answer.question, date: null }]]);
  assert.equal((await fr.runQuestion('Bonjour', '2015-06-01', answering(200, answer))).day, '2015-06-01');
  assert.deepEqual(asked.at(-1), ['/api/next/ask', 'POST', 'application/json', { question: 'Bonjour', date: '2015-06-01' }]);
  // The dock opens an article by itself only from the home page or a text page.
  assert.deepEqual([...fr.OPENING_PAGES], ['live-home', 'live-text', 'live-dossier', 'live-reading']);
  assert.equal((await fr.runQuestion('Bonjour', null, answering(429, { error: 'too_many_questions', message: 'Trop de questions' }))).kind, 'rate');
  assert.equal((await fr.runQuestion('Bonjour', null, answering(500, {}))).kind, 'failure');
  assert.equal((await fr.runQuestion('Bonjour', null, answering(200, { anything: 'else' }))).kind, 'failure', 'an answer with no outcome is none');
  assert.equal((await fr.runQuestion('Bonjour', null, async () => { throw new Error('offline'); })).kind, 'failure');
  // A day is read as dd/mm/yyyy, and only a day that exists.
  assert.equal(fr.typedDay('01/06/2015'), '2015-06-01');
  assert.equal(fr.typedDay('1.6.2015'), '2015-06-01');
  assert.equal(fr.typedDay('31/02/2015'), null);
  assert.equal(fr.typedDay('2015-06-01'), null);
});

test('how sure an asserted answer is: the three searches, two or one (a caution), or the article the person named', () => {
  const answer = { outcome: 'answer', confidence: 'firm', found_by: ['keywords', 'meaning', 'outline'], notes: ['found_by_all_detectors'] };
  assert.deepEqual(confidenceOf(answer, 'fr'), { key: 'firm', text: 'Les trois recherches (le sens, les mots, la table des matières) ont trouvé cet article.', caution: false });
  assert.deepEqual(confidenceOf({ ...answer, confidence: 'supported', found_by: ['keywords', 'meaning'] }, 'fr'), { key: 'supported', text: 'Deux recherches sur trois ont trouvé cet article (les mots et le sens). À vérifier dans le texte.', caution: true });
  assert.deepEqual(confidenceOf({ ...answer, confidence: 'single', found_by: ['outline'] }, 'fr'), { key: 'single', text: 'Une seule recherche a trouvé cet article (la table des matières). À vérifier dans le texte.', caution: true });
  assert.equal(confidenceOf({ ...answer, confidence: 'single', found_by: [] }, 'fr').text, 'Une seule recherche a trouvé cet article. À vérifier dans le texte.');
  assert.equal(confidenceOf({ ...answer, found_by: ['named_by_the_person'], notes: ['article_named_by_the_person'] }, 'fr').text, 'Vous avez nommé cet article.');
  assert.equal(confidenceOf({ ...answer, outcome: 'candidates' }, 'fr'), null, 'nothing is said of an answer that asserts nothing');
  assert.equal(confidenceOf({ outcome: 'answer' }, 'fr'), null, 'an answer that carries no confidence says none');
  assert.equal(confidenceOf({ ...answer, confidence: 'supported', found_by: ['keywords', 'meaning'] }, 'en').text, 'Two searches out of three found this article (words and meaning). To be checked in the text.');
});

test('since when a wording is unchanged, and what changed it last', () => {
  const first = { wording_since: '1997-09-07', wording_until: null, wording_no: 1, wording_first_held: 1, last_change: null };
  assert.equal(wordingLine(first, 'fr'), 'Rédaction inchangée depuis au moins le 07/09/1997.');
  assert.equal(wordingLine({ ...first, wording_since: '2025-01-01', wording_no: 4, wording_first_held: 0 }, 'fr'), 'Rédaction inchangée depuis le 01/01/2025.');
  const changed = { ...first, wording_since: '2013-03-01', wording_no: 2, wording_first_held: 0, last_change: { kind: 'replacement', effective: '2013-03-01', title: 'Loi du 21 décembre 2012\nportant modification.' } };
  assert.equal(wordingLine(changed, 'fr'), 'Rédaction inchangée depuis le 01/03/2013\u00A0; modifié par Loi du 21 décembre 2012 portant modification.');
  assert.equal(wordingLine({ ...changed, last_change: { kind: 'insertion', title: null } }, 'fr'), 'Rédaction inchangée depuis le 01/03/2013\u00A0; modifié par un texte que Lex ne tient pas.');
  assert.equal(wordingLine({ ...changed, last_change: { kind: 'substitution', title: null } }, 'fr'), 'Rédaction inchangée depuis le 01/03/2013\u00A0; modifié par un texte que Lex ne tient pas.');
  assert.equal(wordingLine({ ...changed, last_change: { kind: 'repeal', title: 'Loi X' } }, 'en'), 'Wording unchanged since 01/03/2013; amended by Loi X.');
  assert.equal(wordingLine({ ...changed, last_change: { kind: 'insertion', title: 'Loi du 23 juillet 2024 portant modification de certaines …' } }, 'fr'), 'Rédaction inchangée depuis le 01/03/2013\u00A0; modifié par Loi du 23 juillet 2024 portant modification de certaines…');
  assert.equal(wordingLine({ ...changed, last_change: { kind: 'insertion', title: 'Loi du 21 décembre 2012 portant\\nmodification' } }, 'fr'), 'Rédaction inchangée depuis le 01/03/2013\u00A0; modifié par Loi du 21 décembre 2012 portant modification.');
  assert.equal(wordingLine({ ...first, wording_until: '2013-03-01' }, 'fr'), 'Rédaction en place du 07/09/1997 au 01/03/2013.');
  assert.equal(wordingLine({ ...first, wording_since: null }, 'fr'), null, 'a wording whose start is not known says nothing');
  assert.equal(wordingLine(null, 'fr'), null);
});

test('what Lex searched is shown only when nothing is asserted, the rarest word first, a word no article holds said so', () => {
  const searched = {
    kinds_of_text: ['general', 'repealed'], passages_in_those_kinds: 72702, period: ['2014-01-01', '2014-12-31'],
    words: { periode: 5395, durait: 0, essai: 460 }, texts_whose_table_of_contents_was_read: ['Code du travail', 'Loi du 20 juin 2020 portant\n1° dérogation'],
    closest: { article: 'Art. L. 121-5.', heading: null, act_title: 'Code du travail', holds: ['periode', 'essai'], lacks: ['durait'] },
  };
  const view = searchedView({ outcome: 'candidates', searched }, 'fr');
  assert.equal(view.scope, '72\u202F702 passages dans les textes généraux et les textes abrogés, pour la période du 01/01/2014 au 31/12/2014.');
  assert.deepEqual(view.words.map((row) => `${row.word}: ${row.said}`), ['durait: dans aucun article', 'essai: 460 articles', 'periode: 5\u202F395 articles']);
  assert.deepEqual(view.outlines, ['Code du travail', 'Loi du 20 juin 2020 portant 1° dérogation']);
  assert.equal(view.closest, 'Le passage le plus proche, Art. L. 121-5 (Code du travail), contient\u00A0: periode, essai\u00A0; ne contient pas\u00A0: durait.');
  assert.equal(searchedView({ outcome: 'partial', searched: { ...searched, period: ['2026-11-20', '2026-11-20'], kinds_of_text: ['general'] } }, 'fr').scope, '72\u202F702 passages dans les textes généraux, au 20/11/2026.');
  assert.equal(searchedView({ outcome: 'answer', searched }, 'fr'), null, 'an asserted answer shows no evidence card');
  assert.equal(searchedView({ outcome: 'not_found' }, 'fr'), null);
  assert.equal(buildLine({ build: { code: 'r19', data: 'd7', models: {} } }, 'fr'), 'Version du code r19, des données d7.');
  assert.equal(buildLine({}, 'fr'), null);
});

test('the new steps have a row each: the words, the table of contents (the law\'s own headings apart), and who found the article read', () => {
  const answer = {
    outcome: 'answer', row: {}, blocks: [], candidates: [{ label: 'A1', article: 'Article 41' }],
    trace: { steps: [
      { step: 'find_words', who: 'code', ms: 84, detail: { words_of_the_question: ['assureur', 'resilier'], terms_a_statute_would_use: ['resiliation'], articles_holding_each_word: { assureur: 8540 }, articles_kept: 10 } },
      { step: 'outline_walk', who: 'model', ms: 1655, detail: { texts_walked: 2, walks: [{ title: 'Loi du 27 juillet 1997 sur le contrat d\'assurance.', outline_rows: 53, parts_named: ['Section IX - Fin du contrat'], articles_in_parts: 5 }, { title: 'Loi du 7 décembre 2015', outline_rows: 170, parts_named: [], articles_in_parts: 0 }], whole_part_for_a_list: false } },
      { step: 'read', who: 'model', ms: 1472, detail: { articles_read: 10, pointed: 'A1', lines: [1, 3], answer_found: true, complete_answer_found: true, found_by: 'keywords+meaning+outline' } },
      { step: 'european_text', who: 'code', ms: null, detail: { named_in_question: 'RGPD', read_by_the_assistant: false } },
    ] },
  };
  const rows = stepRows(answer, 'fr');
  assert.deepEqual(rows.map((row) => row.name), ['Les mots', 'Table des matières', 'Lecture', 'Texte européen']);
  assert.equal(rows[0].line, '2 mots de la question et 1 terme de loi cherchés\u00A0; 10 articles retenus');
  // The table of contents: a short list, each text read by its title cut short, with the parts named in it (or none).
  assert.equal(rows[1].line, 'les plans de 2 textes lus\u00A0:');
  assert.deepEqual(rows[1].items, [{ title: 'Loi du 27 juillet 1997 sur le contrat d\'assurance', parts: 'Section IX - Fin du contrat' }, { title: 'Loi du 7 décembre 2015', parts: null }]);
  assert.equal(rows[2].line, '10 articles lus → Article 41, lignes 1, 3\u00A0; réponse trouvée\u00A0: oui\u00A0; complète\u00A0: oui\u00A0; trouvé par\u00A0: les mots, le sens, la table des matières');
  assert.deepEqual(rows[2].items, []);
  const whole = stepRows({ ...answer, trace: { steps: [{ ...answer.trace.steps[1], detail: { ...answer.trace.steps[1].detail, whole_part_for_a_list: true } }] } }, 'fr');
  assert.equal(whole[0].line, 'les plans de 2 textes lus (partie entière lue, pour une liste)\u00A0:');
  assert.equal(stepRows({ ...answer, trace: { steps: [{ step: 'outline_walk', who: 'model', ms: 1, detail: { texts_walked: 0, walks: [] } }] } }, 'fr')[0].line, 'aucun plan lu');
});

test('what the cookies page says is tested: no cookie, only the conversation in the tab\'s session storage, nothing from another site', () => {
  // "the code of a page sets a cookie, writes anything to the browser's storage but the assistant's conversation".
  assert.deepEqual(storageFaults(), []);
  // "puts a question in a request's address".
  assert.deepEqual(questionAddressFaults(), []);
  // "the pages' policy admits anything from another site": every directive names this origin or nothing.
  for (const [directive, sources] of Object.entries(CSP_DIRECTIVES)) {
    assert.ok(sources.every((source) => source === "'self'" || source === "'none'"), `${directive}: ${sources.join(' ')}`);
  }
});

test('the reading page asks the engine by the names the current site gives a text and an article, and draws what comes back', async () => {
  const { fr } = await loadRenderers('test-reading');
  assert.equal(fr.referencesAddress('/lu-legilux/loi-1997-07-27-n10', 'art_21', '2025-11-25'), '/api/next/article-by?work=loi-1997-07-27-n10&anchor=art_21&date=2025-11-25');
  assert.equal(fr.linksAddress('/lu-legilux/loi-1997-07-27-n10'), '/api/next/act-by?work=loi-1997-07-27-n10');
  assert.equal(fr.linksAddress('/eu-eurlex/32016R0679'), null, 'a European text asks nothing');
  const { renderings } = await renderAll({ tag: 'test-reading-all' });
  const of = (name) => renderings.find((rendering) => rendering.name === name && rendering.locale === 'fr').html;
  const references = of('references');
  assert.ok(references.includes('data-article-wording="">Rédaction inchangée depuis le 01/03/2013\u00A0; modifié par Loi du 21 décembre 2012 portant modification de la loi sur le contrat d&#x27;assurance.</span>'));
  assert.ok(references.includes('Modifications enregistrées par l’éditeur'));
  assert.ok(references.includes('<span class="lx-refs-type">01/06/2005</span><span class="lx-refs-type">modifié par</span><span class="lx-refs-what">un texte que Lex ne tient pas</span>'));
  const links = of('links');
  assert.ok(links.startsWith('<details class="lx-links" data-text-links=""><summary>Liens avec d’autres textes (4)</summary>'), 'folded: a details with no open attribute');
  for (const heading of ['Ce texte modifie', 'Ce texte transpose', 'Modifié par', 'Autres textes liés à celui-ci']) assert.ok(links.includes(`<h3 class="lx-refs-heading">${heading}</h3>`), heading);
  assert.ok(links.includes('<a href="/fr/text.html?text=%2Flu-legilux%2Floi-2012-12-21-n9">'));
  assert.ok(links.includes('<code>/eli/dir/1992/49/oj</code>'), 'a text with no title is named by its address, with no link');
  assert.equal(of('links-none'), '');
  // The counted pieces say a count only when the engine gives it.
  assert.ok(!of('how').includes('articles dans l’index par mots-clés.') && of('how-synthetic-counts').includes('78\u202F000 articles dans l’index par mots-clés.'));
  assert.ok(of('how-synthetic-counts').includes('Contrôle\u00A0: 10\u202F870 fichiers relus, 10\u202F868 conformes, 2 à revoir.'));
  assert.ok(of('corpus-synthetic-counts').includes('<th scope="row">Annexes</th><td>4\u202F000</td>'));
  // A reason or a kind is said in words; one the copy does not know is never shown as the engine's key.
  const limits = /<tr data-limit="left-out">[\s\S]*?<\/tr>/u.exec(of('how-synthetic-counts'))?.[0] ?? '';
  assert.ok(limits.includes('supprim\u00E9s par l\u2019\u00E9diteur\u00A0: 641'));
  assert.ok(!limits.includes('deleted_by_publisher') && !limits.includes('a_reason_of_tomorrow'));
  const units = of('corpus-synthetic-counts');
  assert.ok(units.includes('<th scope="row">Formules finales, dates et signatures</th><td>9\u202F770</td>'));
  assert.ok(units.includes('<th scope="row">Texte sous un titre, hors article</th><td>471</td>'));
  assert.ok(units.includes('<th scope="row">Autres unit\u00E9s</th><td>15\u202F007</td>'), 'a kind not known is counted with the other units');
  assert.ok(!/deleted_by_publisher|a_reason_of_tomorrow|a_kind_of_tomorrow|under_heading/u.test(units));
  assert.ok(of('dock-synthetic-supported').includes('class="lx-dock-remark lx-dock-caution" data-dock-confidence="supported"'));
  assert.ok(of('dock-synthetic-not-found').includes('<tr data-none=""><th scope="row">durait</th><td>dans aucun article</td></tr>'));
  assert.ok(of('dock-not_found_with_evidence').includes('data-searched="closest"'));
  assert.ok(of('results').includes('Selon le nombre de recherches qui ont trouvé l’article'));
});

test('Results: the fresh test comes first, drawn from the real results.json in both languages; the earlier questions and the bench after it', async () => {
  const fixture = JSON.parse(await readFile(RESULTS_FIXTURE, 'utf8'));
  assert.equal(typeof fixture.fresh, 'object', 'the fixture is the engine\'s file with its fresh test');
  const { renderings } = await renderAll({ tag: 'test-fresh' });
  assert.deepEqual(renderings.filter((rendering) => rendering.name.startsWith('results') && rendering.faults.length > 0).map((rendering) => `${rendering.name} (${rendering.locale}): ${rendering.faults.join('; ')}`), []);
  const of = (name, locale) => renderings.find((rendering) => rendering.name === name && rendering.locale === locale)?.html ?? '';
  const sectionOf = (html) => {
    const start = html.indexOf('<section aria-labelledby="results-fresh"');
    return start < 0 ? '' : html.slice(start, html.indexOf('</section>', start) + '</section>'.length);
  };
  const NB = ' ';

  // French: the order of the page, then each line read from the data.
  const fr = of('results', 'fr');
  const at = (mark) => fr.indexOf(mark);
  assert.ok(at('<h1>Résultats</h1>') < at('data-results-fresh=""'));
  assert.ok(at('data-results-fresh=""') < at('data-results-independent=""') && at('data-results-independent=""') < at('data-results-intro=""') && at('data-results-intro=""') < at('data-results-bench=""'));
  const frFresh = sectionOf(fr);
  assert.ok(frFresh.includes(`<h2 id="results-fresh">Le test qui compte${NB}: des questions neuves, écrites par quelqu’un d’autre</h2>`));
  assert.ok(frFresh.includes(`<p class="lx-muted" lang="fr" data-fresh-label="">${inMarkup(fixture.fresh.label)}.</p>`));
  assert.ok(frFresh.includes(`<p lang="fr" data-fresh-note="">${inMarkup(fixture.fresh.note)}</p>`));
  assert.ok(frFresh.includes('<h3 id="results-fresh-answer">Questions qui ont une réponse dans la loi</h3>'));
  assert.ok(frFresh.includes(`<th scope="col">Résultat</th><th scope="col">Sur 120 questions</th><th scope="col">Intervalle à 95${NB}%</th>`));
  assert.ok(frFresh.includes(`<tr data-fresh="right"><th scope="row">bonne réponse affirmée</th><td>99</td><td>de 74,7 à 88,3${NB}%</td></tr>`));
  assert.ok(frFresh.includes(`<tr data-fresh="wrong"><th scope="row">réponse fausse affirmée<span class="lx-how-decision-cost" data-fresh-split="">mauvais article${NB}: 3${NB}; mauvaise version${NB}: 0${NB}; bon article mais pas les bonnes lignes${NB}: 1</span></th><td>4</td><td>de 1,3 à 8,3${NB}%</td></tr>`));
  assert.ok(frFresh.includes('<tr data-fresh="shown_not_asserted"><th scope="row">bon article montré sans être affirmé</th><td>8</td><td></td></tr>'));
  assert.ok(frFresh.includes('<tr data-fresh="not_found"><th scope="row">non trouvé</th><td>9</td><td></td></tr>'));
  assert.ok(frFresh.includes('<p data-fresh-asserted=""><strong>Sur 103 réponses affirmées, 99 étaient justes.</strong></p>'));
  assert.ok(frFresh.includes(`<p data-fresh-line="no-answer">Questions sans réponse dans la loi${NB}: 10 sur 10 sans réponse affirmée, comme il se doit.</p>`));
  assert.ok(frFresh.includes(`<p data-fresh-line="exceptions">Exception écrite dans un autre article${NB}: 11 sur 13 justes${NB}; l’autre article montré dans 9 cas.</p>`));
  assert.ok(frFresh.includes(`<p data-fresh-line="dated">Questions sur une date passée${NB}: 7 sur 8 justes${NB}; version erronée affirmée${NB}: 0.</p>`));
  assert.ok(frFresh.includes('<h3 id="results-fresh-confidence">Selon le degré de certitude affiché</h3>'));
  assert.ok(frFresh.includes('<th scope="col">Certitude affichée</th><th scope="col">Réponses affirmées</th><th scope="col">dont fausses</th>'));
  const certainty = (html) => [...html.matchAll(/<tr data-confidence="([a-z-]+)"><th scope="row">([^<]*)<\/th><td>([0-9]*)<\/td><td>([0-9]*)<\/td><\/tr>/gu)].map((match) => match.slice(1).join(' | '));
  assert.deepEqual(certainty(frFresh), ['firm | les trois recherches ont trouvé l’article | 50 | 1', 'supported | deux sur trois | 36 | 1', 'single | une seule | 17 | 2']);
  // Folded: by style and by text, the most questions first, each named in words when the copy names it.
  assert.ok(frFresh.includes('<details class="lx-how-fold" data-fresh-detail=""><summary>Par style de question et par texte</summary>'), 'folded: a details with no open attribute');
  const styles = [...frFresh.matchAll(/<tr data-style="([^"]+)"><th scope="row">([^<]*)<\/th><td>([0-9]+)<\/td>/gu)].map((match) => match.slice(1));
  assert.equal(styles.length, fixture.fresh.by_style.length);
  assert.deepEqual(styles[0], ['lay', 'langage courant', '18']);
  for (const [key, name] of styles) assert.equal(name, nextCopy('fr').results.fresh.styles[key], `the style ${key} said in words`);
  assert.ok(frFresh.includes('<th scope="col">Style de question</th><th scope="col">Questions</th><th scope="col">Justes</th><th scope="col">Fausses</th>'));
  const laws = [...frFresh.matchAll(/<tr data-law="([^"]+)"><th scope="row">([^<]*)<\/th><td>([0-9]+)<\/td><td>([0-9]+)<\/td><td>([0-9]+)<\/td><\/tr>/gu)].map((match) => match.slice(1));
  assert.equal(laws.length, fixture.fresh.by_law.length);
  assert.deepEqual(laws[0], ['contrat-assurance', 'Loi sur le contrat d’assurance', '35', '31', '0']);
  const lawName = (key) => laws.find((row) => row[0] === key)?.[1];
  assert.equal(lawName('rgpd'), 'RGPD (règlement de l’Union)');
  assert.equal(lawName('rome-i'), 'Règlement Rome I');
  assert.equal(lawName('bruxelles-ibis'), 'Règlement Bruxelles I bis');
  assert.equal(lawName('desherence'), 'Loi sur les avoirs et contrats d’assurance en déshérence');
  assert.equal(lawName('societes'), 'Loi sur les sociétés commerciales');
  assert.equal(lawName('consommation'), 'Code de la consommation');
  // The less-known texts drawn at random are grouped under one name by the results data.
  assert.equal(lawName('peu-connus'), 'Textes peu connus tirés au hasard');
  const peu = fixture.fresh.by_law.find((row) => row.law === 'peu-connus');
  assert.deepEqual(laws.find((row) => row[0] === 'peu-connus').slice(2), [String(peu.n), String(peu.right), String(peu.wrong)]);
  assert.equal(laws.some((row) => row[0] === row[1]), false, 'no text is shown by its raw key');
  assert.ok(frFresh.includes(`<p class="lx-muted" data-fresh-time="">Durée médiane${NB}: 6,2${NB}s${NB}; neuf sur dix sous 33,9${NB}s. Coût moyen au tarif public${NB}: 0,0156${NB}USD par question.</p>`));
  // The earlier questions, retitled and otherwise as they were; the bench last, with its notice.
  assert.ok(fr.includes('<h2 id="results-independent">Les mêmes questions au Lex en ligne et à cette version</h2>'));
  assert.ok(fr.includes('<tr data-independent="right"><th scope="row">bonne réponse affirmée</th><td>14</td><td>57</td></tr>'));
  assert.ok(fr.includes('<tr data-law="contrat-assurance"><th scope="row">Loi sur le contrat d’assurance</th><td>27</td><td>25</td></tr>'));
  assert.ok(fr.includes('<p data-results-bench=""><strong>À lire comme une alarme, pas comme une preuve.</strong>'));

  // English: every word from the English table, the test's own label and note marked French.
  const en = of('results', 'en');
  assert.ok(en.indexOf('data-results-fresh=""') < en.indexOf('data-results-independent=""') && en.indexOf('data-results-independent=""') < en.indexOf('data-results-bench=""'));
  const enFresh = sectionOf(en);
  assert.ok(enFresh.includes('<h2 id="results-fresh">The test that counts: fresh questions written by someone else</h2>'));
  assert.ok(enFresh.includes(`<p class="lx-muted" lang="fr" data-fresh-label="">${inMarkup(fixture.fresh.label)}.</p>`));
  assert.ok(enFresh.includes(`<p lang="fr" data-fresh-note="">${inMarkup(fixture.fresh.note)}</p>`));
  assert.ok(enFresh.includes('<h3 id="results-fresh-answer">Questions that have an answer in the law</h3>'));
  assert.ok(enFresh.includes('<th scope="col">Result</th><th scope="col">Out of 120 questions</th><th scope="col">95% interval</th>'));
  assert.ok(enFresh.includes('<tr data-fresh="right"><th scope="row">right answer asserted</th><td>99</td><td>from 74.7 to 88.3%</td></tr>'));
  assert.ok(enFresh.includes('<tr data-fresh="wrong"><th scope="row">wrong answer asserted<span class="lx-how-decision-cost" data-fresh-split="">wrong article: 3; wrong version: 0; right article but not the right lines: 1</span></th><td>4</td><td>from 1.3 to 8.3%</td></tr>'));
  assert.ok(enFresh.includes('<tr data-fresh="shown_not_asserted"><th scope="row">right article shown without being asserted</th><td>8</td><td></td></tr>'));
  assert.ok(enFresh.includes('<p data-fresh-asserted=""><strong>Of 103 answers asserted, 99 were right.</strong></p>'));
  assert.ok(enFresh.includes('<p data-fresh-line="no-answer">Questions with no answer in the law: 10 of 10 with no answer asserted, as it should be.</p>'));
  assert.ok(enFresh.includes('<p data-fresh-line="exceptions">Exception written in another article: 11 of 13 right; the other article shown in 9 cases.</p>'));
  assert.ok(enFresh.includes('<p data-fresh-line="dated">Questions about a past date: 7 of 8 right; wrong version asserted: 0.</p>'));
  assert.deepEqual(certainty(enFresh), ['firm | all three searches found the article | 50 | 1', 'supported | two out of three | 36 | 1', 'single | only one | 17 | 2']);
  assert.ok(enFresh.includes('<summary>By style of question and by text</summary>'));
  assert.ok(enFresh.includes('<tr data-style="lay-personal"><th scope="row">personal situation</th><td>9</td><td>6</td><td>2</td></tr>'));
  assert.ok(enFresh.includes('<tr data-law="desherence"><th scope="row">Law on dormant assets and insurance contracts</th><td>4</td><td>2</td><td>0</td></tr>'));
  assert.ok(enFresh.includes(`<p class="lx-muted" data-fresh-time="">Median time: 6.2${NB}s; nine in ten under 33.9${NB}s. Average cost at list price: 0.0156${NB}USD per question.</p>`));
  assert.ok(en.includes('<h2 id="results-independent">The same questions to the live Lex and to this version</h2>'));
  // No word of the French table in the English section, but for the test's own label and note.
  const enWords = enFresh.replace(/<p[^>]*lang="fr"[^>]*>[\s\S]*?<\/p>/gu, '');
  for (const french of ['bonne réponse', 'Questions qui ont', 'Sur 120', 'justes', 'Durée médiane', 'Selon le degré']) assert.ok(!enWords.includes(french), `no French on the English page: ${french}`);

  // A file with keys missing or of the wrong type: each line whose counts are not all known is left out, never said with a hole.
  for (const locale of ['fr', 'en']) {
    const partial = sectionOf(of('results-fresh-partial', locale));
    assert.ok(partial.includes('data-fresh="right"') && partial.includes('data-fresh="wrong"'));
    assert.ok(!partial.includes('data-fresh="shown_not_asserted"') && !partial.includes('data-fresh="not_found"'));
    assert.ok(!partial.includes(`<th scope="col">${nextCopy(locale).results.fresh.columns.interval}</th>`), 'no column of intervals when no interval is given');
    for (const mark of ['data-fresh-label', 'data-fresh-note', 'data-fresh-asserted', 'data-fresh-line', 'data-fresh-time', 'data-fresh-laws']) assert.ok(!partial.includes(mark), `${mark} left out`);
    assert.ok(partial.includes(`<th scope="col">${nextCopy(locale).results.fresh.columns.count}</th>`), 'a number of questions that is not one is not said');
    assert.deepEqual(certainty(partial).map((row) => row.split(' | ')[0]), ['firm']);
    assert.ok(!/undefined|NaN|null|\[object Object\]/u.test(partial));
    // Unknown keys: a style and a text by their key, a degree of certainty the copy does not name left out.
    const unknown = sectionOf(of('results-fresh-unknown', locale));
    assert.ok(unknown.includes('<tr data-style="a-style-of-tomorrow"><th scope="row">a-style-of-tomorrow</th><td>2</td><td>1</td><td>1</td></tr>'));
    assert.ok(unknown.includes('<tr data-law="a-law-of-tomorrow"><th scope="row">a-law-of-tomorrow</th><td>3</td><td>3</td><td>0</td></tr>'));
    assert.ok(!unknown.includes('a-certainty-of-tomorrow'));
    assert.deepEqual(certainty(unknown).map((row) => row.split(' | ')[0]), ['firm', 'supported', 'single', 'named']);
    // Nothing known: no section at all. A file with the fresh test alone: the section, and no bench drawn empty.
    assert.ok(!of('results-fresh-empty', locale).includes('data-results-fresh'));
    const only = of('results-fresh-only', locale);
    assert.ok(only.includes('data-results-fresh=""') && !only.includes('data-results-intro') && !only.includes('data-results-overview') && !only.includes(nextCopy(locale).results.none));
    assert.ok(!of('results-no-fresh', locale).includes('data-results-fresh'));
  }
  // The check reads the page against the data: a count changed in the page is a fault.
  assert.ok(freshFaults(fr.replace('<td>99</td>', '<td>98</td>'), fixture, 'fr').length > 0);
  assert.ok(freshFaults(fr.replace(frFresh, ''), fixture, 'fr').includes('the fresh test is not drawn'));
});

// ---------------------------------------------------------------------------------------------------------------------------
// The answer in the engine's contract (TRACE-CONTRACT.md), the Results page in `lex-results/2`, the text's table of contents,
// How it works and the design choices (the pages builder, 21/11/2026).

import {
  actTitle, alineaOf, clarifiedQuestion, contractBlockView, contractOpening, contractStateOf, evidenceView, isContractAnswer, listStatement, quotedLines,
  replayRequest, replayTokenHref, replayTokenOf, traceLevels, versionLine, widened, workOf,
} from '../scripts/next-contract.mjs';
import { CONTRACT_FIXTURES, readContractFixtures } from '../scripts/next-check.mjs';

test('the contract: each state said as a state, never as a percentage, with what it names', async () => {
  const fixtures = new Map((await readContractFixtures()).map(({ name, answer }) => [name, answer]));
  assert.ok(fixtures.size >= 10, 'one fixture a state, and the lists');
  for (const [, answer] of fixtures) assert.ok(isContractAnswer(answer));
  assert.equal(isContractAnswer({ outcome: 'answer', row: {} }), false, 'the earlier shape is not the contract');
  assert.deepEqual(contractStateOf(fixtures.get('answer_with_exception'), 'fr'), { key: 'answer_with_exception', badge: 'Réponse, avec une exception', lead: 'D’après le texte officiel, avec une exception à lire.', more: null });
  assert.equal(contractStateOf(fixtures.get('partial'), 'fr').more, `Ce qui manque : ${fixtures.get('partial').missing}`);
  assert.equal(contractStateOf({ state: 'partial' }, 'en').more, 'The reading says this passage does not answer the whole question.');
  assert.equal(contractStateOf({ state: 'a_state_of_tomorrow' }, 'fr'), null);
  assert.equal(contractStateOf(fixtures.get('not_found'), 'fr').more, 'Cela ne veut pas dire que la loi ne prévoit rien.');
});

test('the contract: a version is dated from and until, or says that no later one is known, or that Lex does not know', () => {
  assert.equal(versionLine({ from: '2011-04-16', until: '2013-01-01', until_known: true }, 'fr'), 'Version applicable du 16/04/2011 au 01/01/2013.');
  assert.equal(versionLine({ from: '2025-11-25', until: null, until_known: true }, 'fr'), 'Version applicable depuis le 25/11/2025 ; aucune version plus récente n’est connue.');
  assert.equal(versionLine({ from: '2025-11-25', until: null, until_known: false }, 'en'), 'Version applicable since 25/11/2025; Lex does not know whether it still applies.');
  assert.equal(versionLine({ from: null }, 'fr'), 'La date de cette version n’est pas connue.', 'an undated version is never said to apply always');
  assert.equal(versionLine(null, 'fr'), 'La date de cette version n’est pas connue.');
});

test('the contract: the lines quoted, numbered, a gap where lines were skipped; the article opened by its names', () => {
  assert.deepEqual(quotedLines({ lines: [3, 4, 7], text: ['a', 'b', 'c'] }).map((line) => (line.gap ? '…' : `${line.n}:${line.text}`)), ['3:a', '4:b', '…', '7:c']);
  assert.deepEqual(quotedLines({ lines: [3], text: ['a', 'b'] }).map((line) => `${line.n}:${line.text}`), ['null:a', 'null:b'], 'lists of two lengths: the words, unnumbered');
  assert.equal(workOf('/eli/etat/leg/loi/1997/07/27/n10'), 'loi-1997-07-27-n10');
  assert.equal(workOf('/eli/etat/leg/code/travail'), 'code-travail');
  assert.equal(workOf('celex:32016R0679'), '32016R0679');
  assert.equal(workOf('blank:x'), null);
  assert.equal(actTitle('Version consolidée applicable au 21/12/2007: Loi du 27 juillet 1997 sur le contrat d’assurance.'), 'Loi du 27 juillet 1997 sur le contrat d’assurance.');
  const view = contractBlockView({ role: 'rule', article_pk: 1, act_id: '/eli/etat/leg/loi/1997/07/27/n10', anchor: 'art_21', label: 'Article 21', version: { from: '2025-11-25', until: null, until_known: true }, lines: [1], text: ['A'] }, 'fr', 'answer');
  assert.equal(view.label, 'La règle');
  assert.equal(view.identifier, '/lu-legilux/loi-1997-07-27-n10');
  assert.deepEqual(contractOpening({ state: 'answer', blocks: [{ act_id: '/eli/etat/leg/loi/1997/07/27/n10', anchor: 'art_21', version: { from: '2025-11-25' } }] }, 'fr'), { identifier: '/lu-legilux/loi-1997-07-27-n10', article: 'art_21', date: '2025-11-25' });
  assert.equal(contractOpening({ state: 'candidates', blocks: [{ act_id: '/eli/etat/leg/loi/1997/07/27/n10', anchor: 'art_21' }] }, 'fr'), null, 'nothing asserted, nothing opened');
  assert.equal(contractBlockView({ role: 'rule' }, 'fr', 'not_found').label, 'Passage proche');
  assert.equal(contractBlockView({ role: 'rule' }, 'fr', 'candidates').label, 'Passage proposé');
});

test('the contract: a list says it is complete, or may be incomplete, and checks a count the text states', () => {
  const complete = listStatement({ list: { heading: 'Il indique:', items_total: 7, items_shown: 7, count_stated_by_text: 7, complete: true } }, 'fr');
  assert.equal(complete.complete, true);
  assert.equal(complete.main, 'Liste complète : tous les points de « Il indique: » sont cités (7 points).');
  assert.equal(complete.stated, 'Le texte en annonce 7 points ; Lex en a trouvé 7.');
  const partial = listStatement({ list: { heading: null, items_total: 12, items_shown: 8, count_stated_by_text: null, complete: false } }, 'en');
  assert.equal(partial.main, 'The list may be incomplete: 8 quoted out of 12 items.');
  assert.equal(partial.stated, null);
  assert.equal(listStatement({ list: { items_total: 9, items_shown: 8, complete: true } }, 'fr').complete, false, 'a list said complete with fewer items shown is not complete');
  assert.equal(listStatement({}, 'fr'), null);
});

test('the contract: a "not found" shows what was checked, the rarest word first, and the closest passage by its name', async () => {
  const answer = (await readContractFixtures()).find(({ name }) => name === 'not_found').answer;
  const view = evidenceView(answer, 'fr');
  assert.equal(view.terms[0].term, 'boîte aux lettres');
  assert.equal(view.terms[0].said, 'dans aucun article');
  assert.equal(view.swept, '31 textes et 412 articles parcourus par les recherches.');
  assert.match(view.closest, /^Le passage le plus proche, Article 1er, ne répond pas : /u);
  assert.equal(evidenceView({}, 'fr'), null);
});

test('the contract: a quotation widens to its alinéa, its article and its section, the lines quoted marked', async () => {
  assert.equal(alineaOf('art_16/p2/a2/pt1'), 'art_16/p2/a2');
  assert.equal(alineaOf('art_21/a1'), 'art_21/a1');
  assert.equal(alineaOf('art_5/p1'), 'art_5/p1');
  assert.equal(alineaOf(''), '');
  const context = JSON.parse(await readFile(new URL('context-art16.json', CONTRACT_FIXTURES), 'utf8'));
  const alinea = widened(context, 'alinea', [6]);
  assert.equal(alinea.kind, 'alinea');
  assert.ok(alinea.lines.every((line) => alineaOf(context.article.lines.find((one) => one.n === line.n).unit) === 'art_16/p2/a2'));
  assert.deepEqual(alinea.lines.filter((line) => line.quoted).map((line) => line.n), [6]);
  assert.equal(widened(context, 'article', [6]).lines.length, context.article.lines.length);
  const section = widened(context, 'section', [6]);
  assert.equal(section.kind, 'section');
  assert.equal(section.articles.length, context.section.articles.length);
  assert.equal(widened(context, 'something', [6]), null);
});

test('the contract: the trace names each level, by code or by a model, with its counts and the reasons given', async () => {
  const answer = (await readContractFixtures()).find(({ name }) => name === 'answer_with_exception').answer;
  const levels = traceLevels(answer, 'fr');
  assert.deepEqual(levels.map((level) => level.level), ['corpus', 'document', 'detectors', 'arbiter', 'read', 'references', 'decision']);
  assert.equal(levels[0].rows[0].text, '8 535 textes examinés, 1 retenu');
  assert.equal(levels[2].by, 'both', 'the searches side by side: code and the vectors of a model');
  const table = levels[2].rows.find((row) => row.table !== null).table;
  assert.deepEqual(table.rows.map((row) => [row.head, ...row.cells]), [['Article 21', '—', 'n° 1', 'n° 1', 'n° 2'], ['Article 24', '—', 'n° 4', '—', '—'], ['Article 1er', '—', '—', '—', 'n° 1']]);
  assert.equal(table.rows[2].note, 'trouvé par le sens seul : à vérifier');
  assert.equal(levels[4].rows[0].text, '12 articles lus ; quantité fixée par la partie de la table des matières ; 2 lignes citées.');
});

test('the contract: an answer is shown again from its token, by POST, the token never in the address of a request', async () => {
  assert.equal(replayTokenHref('abc/def+==', 'fr'), '/fr/#r=abc%2Fdef%2B%3D%3D');
  assert.equal(replayTokenOf('#r=abc%2Fdef%2B%3D%3D'), 'abc/def+==');
  assert.equal(replayTokenOf('#r=short'), null);
  assert.equal(replayTokenOf('#q=Bonjour&on=2026-11-20'), null);
  const request = replayRequest('abcdefgh123');
  assert.equal(request.address, '/api/next/replay');
  assert.equal(request.init.method, 'POST');
  assert.deepEqual(JSON.parse(request.init.body), { token: 'abcdefgh123' });
  assert.equal(clarifiedQuestion('Quel est le délai pour résilier ?', 'un contrat d’assurance'), 'Quel est le délai pour résilier (un contrat d’assurance)');
  assert.equal(clarifiedQuestion('Quel délai ?', 'Quel délai pour résilier un bail ?'), 'Quel délai pour résilier un bail ?');
  const { fr } = await loadRenderers('test-replay');
  const answer = (await readContractFixtures()).find(({ name }) => name === 'answer').answer;
  const asked = [];
  const done = await fr.runReplay('abcdefgh123', async (address, init) => { asked.push([address, init.method, JSON.parse(init.body)]); return { ok: true, status: 200, json: async () => answer }; });
  assert.equal(done.kind, 'done');
  assert.equal(done.question, answer.question);
  assert.deepEqual(asked, [['/api/next/replay', 'POST', { token: 'abcdefgh123' }]]);
  assert.equal((await fr.runReplay('abcdefgh123', async () => ({ ok: false, status: 404, json: async () => ({}) }))).kind, 'failure');
  assert.equal((await fr.runQuestion('Bonjour', null, async () => ({ ok: true, status: 200, json: async () => answer }))).kind, 'done', 'an answer in the contract is an answer');
});
