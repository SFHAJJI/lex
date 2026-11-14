// The answer screen's reading of an answer (the owner's order of 2026-11-14, evening): the sentence that answers is a span of
// the quotation's own characters, the words that answer are marked by the question's own words, and the work boxes read the
// trace and the site's own operations; the Decisions and Evidence pages print the recorded runs' and the coverage answer's
// figures, never a number typed by hand.

import assert from 'node:assert/strict';
import test from 'node:test';

import { answerSentence, contentKeys, dateFor, quoteVerifies, searchCount, searchStepOf, sentencesOf, stepsRun, wordKey } from '../scripts/answer-view.mjs';
import { EVALUATION_RECORD } from '../scripts/evaluation-record.mjs';
import { COVERAGE_RECORD } from '../scripts/coverage-record.mjs';
import { renderLiveReferencePage } from '../.react-build/app.mjs';

const ART_44 = "Article 44Délai de prescription1.Le délai de prescription de toute action dérivant du contrat d'assurance est de trois ans.Le délai court à partir du jour de l'événement qui donne ouverture à l'action. Toutefois, lorsque celui à qui appartient l'action prouve qu'il n'a eu connaissance de cet événement qu'à une date ultérieure, le délai ne commence à courir qu'à cette date.";
const QUESTION = "Quel est le délai de prescription des actions dérivant d'un contrat d'assurance ?";
const joined = (sentence) => sentence.parts.map((part) => part.text).join('');

test('the sentence that answers shares the most of the question\'s words, and the words it adds are marked', () => {
  const sentence = answerSentence(ART_44, QUESTION);
  assert.equal(joined(sentence), "Le délai de prescription de toute action dérivant du contrat d'assurance est de trois ans.");
  assert.deepEqual(sentence.parts.filter((part) => part.mark).map((part) => part.text), ['trois ans']);
  assert.ok(ART_44.includes(joined(sentence)), 'a span of the quotation, every character kept');
  // The dated question reads the same sentence: its date is no word of the law's.
  assert.equal(joined(answerSentence(ART_44, "Quel était le délai de prescription des actions dérivant d'un contrat d'assurance au 1er janvier 2020 ?")), joined(sentence));
});

test('with no sentence sharing the question\'s words, the first full sentence is shown unmarked, one ending on a colon with the next', () => {
  const gdpr = 'Article 5Principles relating to processing of personal data1.Personal data shall be: (a) processed lawfully, fairly and in a transparent manner in relation to the data subject;';
  const sentence = answerSentence(gdpr, 'What does article 5 of the GDPR say?');
  assert.ok(sentence.parts.every((part) => !part.mark), 'nothing marked');
  assert.ok(joined(sentence).startsWith('Personal data shall be:'), joined(sentence));
  for (const part of sentence.parts) assert.ok(gdpr.includes(part.text.trim()), `${part.text} is the quotation's`);
  assert.equal(answerSentence('Article 1er', 'Que dit l’article 1er ?'), null, 'no sentence, nothing shown');
});

test('a word is compared without its accents, its case and its elided article; the words that say nothing are left out', () => {
  assert.equal(wordKey("d'Assurance"), 'assurance');
  assert.equal(wordKey('l’assuré'), 'assure');
  assert.deepEqual([...contentKeys('Le délai de la prescription')], ['delai', 'presc']);
  assert.deepEqual(sentencesOf('Article 2Objet1.Le texte dit ceci.Puis cela.'), ['Article 2', 'Objet', 'Le texte dit ceci.', 'Puis cela.']);
});

test('the work boxes read the trace, the search asked again and the quotation\'s check', () => {
  const trace = { steps: [
    { operation_id: 'search', fired: true, parameters: { query: 'délai de prescription', language: 'fra', limit: 10 } },
    { operation_id: 'evidence_bundle', fired: true, parameters: { article: 'art_44', date: '2026-11-14' } },
    { operation_id: 'dossier', fired: false, parameters: {} },
  ] };
  assert.deepEqual(searchStepOf(trace), { query: 'délai de prescription', language: 'fra', limit: 10 });
  assert.equal(searchStepOf({ steps: [] }), null);
  assert.deepEqual(stepsRun(trace).map((step) => step.operation), ['search', 'evidence_bundle'], 'a step that did not fire is not said');
  assert.deepEqual(searchCount({ result: { value: { hits: [1, 2, 3], truncated: false } } }), { matched: 3, more: false });
  assert.deepEqual(searchCount({ result: { value: { hits: new Array(10).fill(0), truncated: true } } }), { matched: 10, more: true });
  assert.equal(searchCount(null), null);
  const quote = { article_text_sha256: 'a'.repeat(64) };
  assert.equal(quoteVerifies(quote, { result: { value: { verdict: 'digest_matches', article_text_sha256: 'a'.repeat(64) } } }), true);
  assert.equal(quoteVerifies(quote, { result: { value: { verdict: 'digest_matches', article_text_sha256: 'b'.repeat(64) } } }), false);
  assert.equal(quoteVerifies(quote, { result: { value: { verdict: 'not_held' } } }), false);
  assert.equal(dateFor('2025-11-25', 'fr'), '25/11/2025');
  assert.equal(dateFor('2025-11-25', 'en'), '2025-11-25');
});

test('Decisions and Evidence print the recorded runs\' and the coverage answer\'s figures', () => {
  const dev = EVALUATION_RECORD.dev;
  const heldout = EVALUATION_RECORD.heldout;
  const decisions = renderLiveReferencePage('decisions');
  assert.ok(decisions.includes(`${dev.wrongArticle} wrong articles in ${dev.cases} test questions.`));
  assert.ok(decisions.includes(`${dev.notFound + dev.wronglyRefused} of the ${dev.rightArticle + dev.wrongArticle + dev.notFound + dev.wronglyRefused} questions that call for an article get none.`));
  assert.ok(decisions.includes(`Open problem: ${heldout.rightArticle} of ${heldout.cases} unseen questions get the right article.`));
  assert.equal((decisions.match(/class="decision-card"/g) ?? []).length, 7, 'seven cards');
  const evidence = renderLiveReferencePage('evidence');
  for (const [key, value] of [['right', dev.rightArticle], ['wrong', dev.wrongArticle], ['notFound', dev.notFound], ['wronglyRefused', dev.wronglyRefused], ['rightBehaviour', dev.rightBehaviour]]) {
    assert.match(evidence, new RegExp(`data-count="${key}"[^>]*><strong>${value}</strong>`), key);
  }
  assert.equal((evidence.match(/<tr data-outcome=/g) ?? []).length, dev.cases, 'every question, one by one');
  assert.ok(evidence.includes(`Model: ${dev.modelDeployment}`));
  assert.ok(evidence.includes(`Held: ${COVERAGE_RECORD.works.toLocaleString('en-GB')} acts`));
  assert.equal((evidence.match(/class="flow-accent"/g) ?? []).length, 2, 'the model and the owner, each in the accent colour');
  assert.ok(!/@example\.com/.test(evidence), 'no e-mail address on the page');
});
