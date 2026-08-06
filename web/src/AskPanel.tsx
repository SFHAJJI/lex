import { useEffect, useRef, useState } from "react";
import type { Step } from "./api";

/**
 * The assistant, out of the page and into a corner.
 *
 * It used to sit at the top with the browse controls directly underneath, which asked every
 * visitor to choose between two doors before knowing what either led to, and made the filters
 * look like a fallback for people the AI had failed. They are not: reading a law by name or by
 * date is the ordinary path, and the assistant is the shortcut for people who would rather ask.
 *
 * So the page is the corpus, and this is a thing you summon. Same pattern as the energy co-pilot,
 * for the same reason: a launcher costs nothing until it is wanted, and once open it is plainly a
 * conversation with a machine rather than a search box that happens to reply in prose.
 */
export interface AskPanelProps {
  q: string;
  setQ: (v: string) => void;
  busy: boolean;
  steps: Step[];
  said?: string;
  onSubmit: (text: string) => void;
  onOpenStep: (s: Step) => void;
  // Next steps for the answer on screen. Lex's prompt forbids asking permission, so
  // these are things to do rather than a question to answer: compare, read the current
  // text, widen the window.
  followUps?: { label: string; run: () => void }[];
}

const SUGGESTIONS = [
  "What did the Covid rules say on 1 February 2021?",
  "How has Article 92 of the CRR changed?",
  "Which Luxembourg laws changed most during the pandemic?",
  "Que disait le Code du travail en 2019 ?",
];

export default function AskPanel(p: AskPanelProps) {
  const [open, setOpen] = useState(false);
  const [min, setMin] = useState(false);
  const box = useRef<HTMLDivElement>(null);
  const input = useRef<HTMLInputElement>(null);
  const started = p.steps.length > 0 || !!p.said || p.busy;

  // An answer arriving behind a closed panel is an answer nobody reads.
  useEffect(() => { if (started) { setOpen(true); setMin(false); } }, [started]);
  useEffect(() => { if (open) input.current?.focus(); }, [open]);
  useEffect(() => {
    const esc = (e: KeyboardEvent) => { if (e.key === "Escape" && !started) setOpen(false); };
    addEventListener("keydown", esc);
    return () => removeEventListener("keydown", esc);
  }, [started]);
  useEffect(() => { box.current?.scrollTo({ top: box.current.scrollHeight, behavior: "smooth" }); },
            [p.steps.length, p.said]);

  if (!open) {
    return (
      <button className="asklaunch" onClick={() => setOpen(true)} aria-label="Ask about any law">
        <span className="al-ic" aria-hidden="true">✦</span>
        <span className="al-t"><b>Ask about any law</b><small>on any date, in plain language</small></span>
      </button>
    );
  }

  return (
    <aside className={"askpanel" + (min ? " min" : "")} role="dialog" aria-label="Assistant">
      <header className="ap-head">
        <span className="ap-title"><span className="al-ic" aria-hidden="true">✦</span> Assistant</span>
        <button className="ap-x" onClick={() => setMin(!min)} aria-label={min ? "Expand" : "Minimise"}>
          {min ? "▴" : "▾"}
        </button>
        <button className="ap-x" onClick={() => setOpen(false)} aria-label="Close">✕</button>
      </header>

      {!min && (
        <>
          {/* Said once, at the top, before anything it writes: this is a machine, it can be
              wrong, and it answers from the corpus rather than from the world. */}
          <p className="ap-notice">
            You are talking to an <b>AI assistant</b>. It answers only from the laws Lex holds,
            with the date and the source for every claim, or it declines. It can still be wrong,
            and it is not legal advice.
          </p>

          <div className="ap-body" ref={box}>
            {!started && (
              <div className="ap-sugg">
                <p className="ap-sugg-h">What can it do?</p>
                {SUGGESTIONS.map((s) => (
                  <button key={s} className="ap-chip" onClick={() => p.onSubmit(s)}>{s}</button>
                ))}
              </div>
            )}

            {p.steps.length > 0 && (
              <ol className="steps" aria-live="polite" aria-label="What the assistant is finding">
                {p.steps.map((st, i) => (
                  <li key={i} className={st.kind}>
                    <span>{st.text}</span>
                    {st.work ? <button className="chipmini" onClick={() => p.onOpenStep(st)}>open →</button> : null}
                  </li>
                ))}
                {p.busy ? <li className="pending"><span>working…</span></li> : null}
              </ol>
            )}

            {p.said ? <div className="said"><b>what I found</b>{p.said}</div> : null}

            {p.said && (p.followUps?.length ?? 0) > 0 && (
              <div className="ap-next">
                {p.followUps!.map((f) => (
                  <button key={f.label} className="ap-chip next" onClick={f.run}>{f.label}</button>
                ))}
              </div>
            )}
          </div>

          <form className="ap-form" onSubmit={(e) => { e.preventDefault(); p.onSubmit(p.q); p.setQ(""); }}>
            <input ref={input} value={p.q} onChange={(e) => p.setQ(e.target.value)} disabled={p.busy}
                   placeholder="What did a law say on a date?" aria-label="Ask" />
            <button type="submit" disabled={p.busy}>{p.busy ? "…" : "Ask"}</button>
          </form>
        </>
      )}
    </aside>
  );
}
