# Lex V3: the owner's brief

Updated 2026-11-20 at 07:40 UTC by the driver. The driver updates this file in the first pull request of each day and
whenever a run starts, ends or stops. At most 40 lines, plain words, no hashes. The detail stays in STATUS-WEB.md and
STATUS-DATA.md; the acceptance list is LAUNCH-CONTRACT.md.

## Where Lex stands

- Live: Release 19 since 07:24 UTC on 20 November, on your go-live word of the evening before. Release 18 is kept
  ready: one command brings it back in about a minute.
- Later than promised: the build was ready at 01:15 UTC; the driver's session had stopped until 06:53 UTC.

## What is new in Release 19

- Lex reads all its 2,033 Luxembourg laws, not only insurance. Outside insurance it shows the passages it found and
  does not say "this is the answer".
- Before Lex asserts an answer, one more model call asks "does this article answer the question?". On a no, the
  article is shown as a candidate.
- A stronger model (gpt-4.1). Fewer needless refusals on general questions.

## How it was measured: 70 questions nobody had seen, asked once on the final version

| | Result |
|---|---|
| Wrong answers asserted, all 70 | 0 |
| Other laws (45): right article among the passages shown | 36 |
| Insurance (15): answered right / right article shown as a candidate | 8 / 6 |
| Questions that must get no article (10): handled as expected | 6 |
| The release gate on the candidate (56 cases): right articles / wrong | 36 / 0 |

## What I decided for you (each can be undone)

- Limits: 0.25 EUR an answer, 5000 EUR a month, 20 questions a minute for one visitor.
- No second-family review of that pull request, on your order to skip ceremonies that night.

## Weak spots, and what waits on you

- A greeting or nonsense gets "not found" instead of "please ask a question". The European texts are not read the
  new way. The quality numbers shown on the site are still Release 18's.
- The two emails to Kezhan (`C:\lex-v3\kezhan-plan-2026-11-20\email\EMAIL-FINAL.md`): you send them yourself.
