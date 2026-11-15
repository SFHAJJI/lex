# Lex V3: the owner's brief

Updated 2026-11-15 at 15:30 UTC by the driver. The driver updates this file in the first pull request of each
day and whenever a run starts, ends or stops. At most 40 lines, plain words, no hashes. The detail stays in
STATUS-WEB.md and STATUS-DATA.md; the acceptance list is LAUNCH-CONTRACT.md.

## Where V3 stands

- Release 13 is live and stays live until you say yes to Release 14. Nothing has changed for readers yet.
- Release 14, the new front end from your brief, is building on Azure since 15:14 UTC (about 50 minutes). It then goes
  to a test address with no traffic, with screenshots and the ten checks of your brief, for your yes.
- What Release 14 changes, front end only (the engine, the search, the model and the quality gates are untouched):
  - the law page: the text itself, its versions on a time line with a bar per version, the article list with the years
    each changed, two versions compared side by side with the changed words in red and green;
  - the assistant beside every main page, the five steps of each answer, and honest "not found" and "which text?" cards;
  - the new Home, Changes, How it works and Decisions, the phone layout, and the same header on every page.

## What waits on you

- Your yes to put Release 14 live, after the screenshots. Release 13 comes back in minutes if anything is wrong.
- Two public wordings: the privacy notice and the cookies page now say the assistant keeps your conversation in the
  browser tab until you close it (it is sent nowhere); and How it works's heading is "How an answer is built".

## Known limits (honest, and fine to say in the interview)

- One insurance question ("Quand la garantie… prend-elle effet ?") can still get article 57. Do not use it in the demo.
- A question with an e-mail address in it sometimes gets no usable plan. Do not use one in the demo.
- Questions phrased "l'assuré doit-il…" are declined as personal advice, on purpose.
- English questions about a Luxembourg law held in French find nothing: the search does not translate.

## For the Foyer interview on 17 November

- The demo: C:\lex-v3\foyer-interview-2026-11-17\DEMO-SCRIPT.md (to be updated for the new pages once live).
- To show MLflow itself on this PC: open a terminal in C:\lex-v3\lex-eval and run
  .venv\Scripts\mlflow.exe ui --backend-store-uri sqlite:///mlflow.db, then open http://127.0.0.1:5000
