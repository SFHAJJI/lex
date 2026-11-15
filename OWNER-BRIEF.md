# Lex V3: the owner's brief

Updated 2026-11-15 at 12:00 UTC by the driver. The driver updates this file in the first pull request of each
day and whenever a run starts, ends or stops. At most 40 lines, plain words, no hashes. The detail stays in
STATUS-WEB.md and STATUS-DATA.md; the acceptance list is LAUNCH-CONTRACT.md.

## Where V3 stands

- Release 13 is live since about 11:50 UTC on your yes. Your 7 checks pass on the live site. Its quality gate passed:
  17 right articles of 56, 0 wrong, every quotation verified. Release 12 is switched off and kept for rollback.
- The code is frozen again until the interview, unless something breaks.
- What Release 13 added: "How it works" in the menu (the system card, the architecture, the API, the MLflow registry,
  supervision, improvement, quality, limits, the two public repositories); the API file at law.soufien.lu/openapi.json;
  the menu Ask, Texts, Changes, Decisions, How it works; a readable Changes page.
- MLflow: Release 13 is version 8 of the model, the champion. Version 6 failed (once, the model gave no usable plan). Every promotion
  now registers itself. The site's table shows versions 1 to 7: it was built before version 8 existed. That is why an
  answer's "Show the work" says this build is registered when it goes live, rather than naming a number.
- The nightly check runs every night at 03:17 UTC on GitHub against the live site; its first run passed.

## Known limits (honest, and fine to say in the interview)

- One insurance question ("Quand la garantie… prend-elle effet ?") can still get article 57. Do not use it in the demo.
- The test question with an e-mail address in it failed 3 of 7 runs today: the model gives no usable plan that
  time, and Lex says it could not answer. Do not use a question with an e-mail address in the demo.
- Questions phrased "l'assuré doit-il…" are declined as personal advice, on purpose.
- English questions about a Luxembourg law held in French find nothing: the search does not translate.

## For the Foyer interview on 17 November

- The demo: C:\lex-v3\foyer-interview-2026-11-17\DEMO-SCRIPT.md.
- To show MLflow itself on this PC: open a terminal in C:\lex-v3\lex-eval and run
  .venv\Scripts\mlflow.exe ui --backend-store-uri sqlite:///mlflow.db, then open http://127.0.0.1:5000
  (Models, lex-assistant: versions 1 to 8, the champion on 8).

## What waits on you

- Tell me any line of "How it works" to change after you read it; a change means one more release.
