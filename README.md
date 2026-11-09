# Lex V3

Lex shows the published text of Luxembourg and European Union law as it stood on a date, article by article, with the
official source, the text's hash and a permalink. It is served at https://law.soufien.lu (open since 27 October 2026), in
French and English, over the pages, a REST API and an MCP server on one origin. It does not give advice, apply the law to a
situation, make its own consolidation or let a model choose an act or rank results.

How it is built, in short: the publishers' files (Formex XML from the Publications Office, Akoma Ntoso from Legilux) are
parsed into relational SQLite indexes (works, dated versions and their validity intervals, articles, hashes, publisher
relations), built twice and compared byte for byte; questions are parsed by deterministic parsers (ELI, CELEX, citations,
dates); retrieval goes by structure first (act, version on a date, article), then lexical search over the articles' text;
every answer is a typed envelope and every refusal a typed code with a mandatory payload. Releases are reproducible, signed
images, checked against the release serving before them. An assistant is built to a typed answer contract (the model only
plans; code validates and quotes) and is switched off until its gates pass.

Where to read more: [STATUS.md](STATUS.md) (what is served, what is next), [docs/OPERATING.md](docs/OPERATING.md) (how it
runs), [LAUNCH-CONTRACT.md](LAUNCH-CONTRACT.md) (the acceptance list), and on the site itself the Developers page and
"What this site holds". Work starts from `v3/integration`; `main` is the legacy line.

## Local verification

```powershell
dotnet test Lex.V3.slnx --configuration Release
npm test --prefix web
npm run build --prefix web
pwsh -File eng/verify-v3-tree.ps1
```
