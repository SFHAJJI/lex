# Lex V3: the owner's brief

Updated 2026-11-19 at 10:35 UTC by the driver. The driver updates this file in the first pull request of each day and
whenever a run starts, ends or stops. At most 40 lines, plain words, no hashes. The detail stays in STATUS-WEB.md and
STATUS-DATA.md; the acceptance list is LAUNCH-CONTRACT.md.

## Where Lex stands

- Live: Release 16 (V4), since 06:47 UTC. Nothing of your brief "make it work" is live yet: it is built, tested and
  prepared as the next release, and waits for your word.
- Your 80 checked questions: this morning the live site gave 27 right, 5 wrong, 29 not found, 19 refused. The new
  version gives 59 right, 3 wrong, 13 not found, 4 refused, 1 too slow (measured on my PC with the release's data and
  model). An article asked in ordinary words: 49 right of 54.
- How: when the dictionary does not know the words, an AI model picks articles in the laws' tables of contents, then
  points at lines; code quotes them. The model writes no text. About half a cent an answer, 3 seconds, 9 at worst.
- "Mon assureur peut-il…" now gets the general rule quoted, with a line saying the text does not say how it applies
  to the reader; refused only when no rule is found.
- The motor insurance law answers again, with the "Dernière version détenue" notice; an article Lex does not hold is
  said so ("L'article 16 existe mais Lex n'en détient pas le texte", with the official link).

## Wrong answers still there (your brief: tell you first)

- "Qui doit prouver quoi en cas de sinistre ?" quotes articles 26, 27 (your label: 15, 16). "L'assureur peut-il modifier
  les conditions générales en cours de contrat ?" quotes 60 (label: 42, 16, 17). The small model's choice; see below.

## What I decided for you (each can be undone)

- One word alone ("garantie") asks for a full question. Foreign law, "Remplaces-tu un avocat ?", labour or housing
  questions are no longer answered from the insurance law (found by your other 102 questions, now closed).
- The test cases on a reader's own case now expect the general rule and the caution line, as you directed.

## What waits on you

- Go live with this version: yes or no.
- The stronger model: I was not allowed to create it. Create a "gpt-4.1" deployment on the same Azure resource, or
  tell me to; it costs about five times more an answer, so the per-answer ceiling (2 cents) must rise too.
- A summary sentence written by the model above the quotations: yes or no. Five examples are in the folder
  kezhan-brief-2026-11-19 (file summary-sentence-examples.md). Not built: today no sentence comes from a model.
- The monthly cap is 20 EUR (about 3,700 answers): keep or change. The hold-out (77 questions) is not opened: say when.
- The Developers page still says personal questions "are refused": reword or leave.
