# Lex V3: the owner's brief

Updated 2026-11-15 at 09:15 UTC by the driver. The driver updates this file in the first pull request of each
day and whenever a run starts, ends or stops. At most 40 lines, plain words, no hashes. The detail stays in
STATUS-WEB.md and STATUS-DATA.md; the acceptance list is LAUNCH-CONTRACT.md.

## Where V3 stands

- Release 12 is live since 14 November, about 18:50 UTC. Your 7 checks pass on the live site.
- Release 13 is being built on your order of today: the three things the Foyer offer asks for, shown on the site.
  Nothing changes in search, the model's plan, the checks or any gate. It goes live only on your yes to its screenshots.
- What Release 13 adds:
  - "How it works" replaces "Preuves" in the menu: what Lex is for and not for, how a question travels, the API, the
    MLflow model registry, the nightly check, how it improved, quality, limits, and links to the two public repositories.
  - Each answer's "Show the work" names the model version that planned it and links to the registry.
  - The API is described in a standard file anyone can load: law.soufien.lu/openapi.json.
  - The menu: Ask, Texts, Changes, Decisions, How it works.
  - The Changes page names each law in words ("Loi du 29/10/2025"), counts the rows, links "Compare the two versions",
    and its warning is in French on French pages.
- MLflow: today's check of the live site is version 7 of the model, marked champion. Version 6 failed: the model timed
  out once on one question. Both stay recorded; that is the point of a registry. Every promotion now registers itself.
- The nightly check runs every night at 03:17 UTC on GitHub against the live site, and opens an issue if a gate fails.

## Known limits (honest, and fine to say in the interview)

- One insurance question ("Quand la garantie… prend-elle effet ?") can still get article 57. Do not use it in the demo.
- Questions phrased "l'assuré doit-il…" are declined as personal advice, on purpose.
- English questions about a Luxembourg law held in French find nothing: the search does not translate.

## For the Foyer interview on 17 November

- The demo: C:\lex-v3\foyer-interview-2026-11-17\DEMO-SCRIPT.md. To show MLflow itself on this PC:
  open a terminal in C:\lex-v3\lex-eval and run .venv\Scripts\mlflow.exe ui --backend-store-uri sqlite:///mlflow.db,
  then open http://127.0.0.1:5000 (Models, lex-assistant).

## What waits on you

- Read the "How it works" page in the screenshots: it makes public claims in your name. Tell me any line to change.
- Your yes to put Release 13 live, after the screenshots.
