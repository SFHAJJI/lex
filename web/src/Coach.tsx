import { useEffect } from "react";
import type { State } from "./state";

/**
 * Three steps, once, for the one idea that cannot be shown by layout alone.
 *
 * Almost all of this redesign was about deleting explanation. This is the exception. That a law is
 * a different document on every date it was amended is the premise the whole product rests on, and
 * nobody arrives holding it. A visitor who never moves the rail sees a slow way to read a statute
 * and leaves, having used none of it.
 *
 * So it teaches by doing rather than describing, and it reads progress off the URL rather than
 * tracking anything of its own: a law is open, a date has been chosen, a comparison is on screen.
 * Arrive by permalink with all three already true and it never appears, which is correct, because
 * someone who is deep-linked into a comparison has nothing left to learn from it.
 */
export const COACH_KEY = "lex.coached";

const STEPS = [
  { t: "Pick a law", d: "by name, subject or identifier" },
  { t: "Move along the rail", d: "the text changes under you, date by date" },
  { t: "Compare two dates", d: "press Compare, then pick the second one" },
];

export default function Coach({ state, onDone }: { state: State; onDone: () => void }) {
  const done = [!!state.work, !!state.work && !!state.date, state.mode === "compare"];
  const at = done.findIndex((d) => !d);
  const finished = at < 0;

  // Let the comparison land and be looked at before the coach clears itself away.
  useEffect(() => {
    if (!finished) return;
    const t = setTimeout(onDone, 2600);
    return () => clearTimeout(t);
  }, [finished, onDone]);

  return (
    <div className={"coach" + (finished ? " done" : "")} role="note">
      <div className="co-h">
        <b>{finished ? "That is the whole idea." : "A law is not one document"}</b>
        <button className="co-x" onClick={onDone} aria-label="Dismiss">✕</button>
      </div>
      <ol className="co-steps">
        {STEPS.map((s, i) => (
          <li key={s.t} className={done[i] ? "ok" : i === at ? "at" : ""}>
            <span className="co-n" aria-hidden="true">{done[i] ? "✓" : i + 1}</span>
            <span><b>{s.t}</b><span className="co-d">{s.d}</span></span>
          </li>
        ))}
      </ol>
    </div>
  );
}
