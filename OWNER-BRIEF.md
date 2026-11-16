# Lex V3: the owner's brief

Updated 2026-11-16 at 10:45 UTC by the driver. The driver updates this file in the first pull request of each
day and whenever a run starts, ends or stops. At most 40 lines, plain words, no hashes. The detail stays in
STATUS-WEB.md and STATUS-DATA.md; the acceptance list is LAUNCH-CONTRACT.md.

## Where V3 stands

- Release 14, the new front end from your brief, is live since about 10:30 UTC, on your explicit exception to the
  quality gate. Your 7 checks pass on the live site. Release 13 is switched off and kept: it comes back in minutes.
- What changed for readers: the law page (the text, its versions on a time line, the articles with the years they
  changed, two versions compared in red and green), the assistant beside every main page with the five steps of each
  answer, the new Home, Changes, How it works and Decisions, the phone layout, the same header on every page.
- The exception: the gate failed twice on one insurance question ("Quand la garantie d'un contrat d'assurance
  prend-elle effet ?" gets article 57, the right one is 37). Release 13 gave the same wrong answer at that moment, two
  times in three: the fault is the model's plan on that question, not the new website. MLflow says it plainly: version
  10 serves (tagged with your exception); "champion" stays on version 8, the last that passed every check.
- The privacy notice and cookies page now say the assistant keeps the conversation in the browser tab until it closes.

## Known limits (honest, and fine to say in the interview)

- The insurance question above can answer article 57. Do not use it in the demo; it is a good example of the gate
  doing its job, and of your decision to override it with the reason written down.
- A question with an e-mail address in it sometimes gets no usable plan.
- Questions phrased "l'assuré doit-il…" are declined as personal advice, on purpose.
- On a phone, the time line's year labels are cramped; old addresses still open the old pages.

## For the Foyer interview on 17 November

- The demo: C:\lex-v3\foyer-interview-2026-11-17\DEMO-SCRIPT.md (to be updated for the new pages).
- A safe path: Home, ask "Quel est le délai de prescription d'un contrat d'assurance ?", the law opens at article 44;
  on its time line click 01/03/2013 then 16/04/2011: article 38's notice went from three months to thirty days.
- To show MLflow on this PC: in C:\lex-v3\lex-eval run .venv\Scripts\mlflow.exe ui --backend-store-uri
  sqlite:///mlflow.db, then open http://127.0.0.1:5000 (lex-assistant: versions 1 to 10).

## What waits on you

- Whether to look at the engine for that one insurance question (the engine is frozen until you say so).
