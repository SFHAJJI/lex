# Security

Lex serves public legislation and holds no user accounts, no personal data and no secrets
belonging to anyone but its operator. The realistic concerns are therefore integrity and
availability rather than confidentiality.

## Reporting

Open a [private security advisory](https://github.com/SFHAJJI/lex/security/advisories/new),
or email haji.soufien@gmail.com. Please allow a few days for a first reply; this is a
personal project maintained outside working hours.

## What is worth reporting

- A way to make the API return text that does not match the publisher's bytes, or to serve a
  provision under the wrong date, work or language.
- A signed index that verifies while its content has been altered, or any way to produce one.
- A path traversal, injection or SSRF in the web layer or the MCP endpoint.
- A way to make an ingest run write outside its corpus, or to poison a corpus with content
  the publisher never served.

## What is not

- Rate limits on the free assistant. It is capped on purpose.
- The absence of authentication on `/mcp`. It is read-only and public by design.
- Reports that a law is missing or has no text. That is coverage, not security, and it is
  documented at [law.soufien.lu/coverage](https://law.soufien.lu/coverage).

## Integrity, if you want to check it yourself

Every index carries an ECDSA P-256 signature over a digest of its own content, and every
provision carries a SHA-256 chained to the verbatim publisher file it came from. The
procedure is at [law.soufien.lu/verify](https://law.soufien.lu/verify).
