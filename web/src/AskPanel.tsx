import { useEffect, useRef, useState } from "react";
import type { AskMessage, Step } from "./api";
import { STARTER_PROMPTS, parseAssistantPanelState } from "./assistantShell";

export interface AskPanelProps {
  q: string;
  setQ: (value: string) => void;
  busy: boolean;
  steps: Step[];
  said?: string;
  conversation: AskMessage[];
  activeQuestion?: string;
  onSubmit: (text: string) => void;
  onReset: () => void;
  onOpenStep: (step: Step) => void;
  followUps?: { label: string; run: () => void }[];
}

const PANEL_KEY = "lex.ask.panel.v1";
const MODAL_QUERY = "(max-width: 1099px)";
const modalViewport = () => typeof matchMedia === "function" && matchMedia(MODAL_QUERY).matches;

function initialPanelState() {
  try { return parseAssistantPanelState(sessionStorage.getItem(PANEL_KEY)); }
  catch { return parseAssistantPanelState(null); }
}

export default function AskPanel(p: AskPanelProps) {
  const initial = useRef(initialPanelState()).current;
  const [open, setOpen] = useState(initial.open);
  const [minimized, setMinimized] = useState(initial.minimized);
  const [modal, setModal] = useState(modalViewport);
  const body = useRef<HTMLDivElement>(null);
  const panel = useRef<HTMLElement>(null);
  const input = useRef<HTMLInputElement>(null);
  const launcher = useRef<HTMLButtonElement>(null);
  const started = p.conversation.length > 0 || !!p.activeQuestion
    || p.steps.length > 0 || !!p.said || p.busy;

  useEffect(() => {
    if (typeof matchMedia !== "function") return;
    const media = matchMedia(MODAL_QUERY);
    const changed = () => setModal(media.matches);
    media.addEventListener("change", changed);
    return () => media.removeEventListener("change", changed);
  }, []);

  useEffect(() => {
    try { sessionStorage.setItem(PANEL_KEY, JSON.stringify({ open, minimized })); }
    catch { /* Tab-scoped state is optional in restricted browsing modes. */ }
    document.body.classList.toggle("assistant-open", open && !minimized && !modal);
    document.body.classList.toggle("assistant-modal", open && !minimized && modal);
    return () => document.body.classList.remove("assistant-open", "assistant-modal");
  }, [open, minimized, modal]);

  // Never let an answer arrive behind a closed panel.
  useEffect(() => {
    if (p.busy || p.said) { setOpen(true); setMinimized(false); }
  }, [p.busy, p.said]);

  useEffect(() => {
    if (open && !minimized) input.current?.focus();
  }, [open, minimized]);

  useEffect(() => {
    if (!open || minimized) return;
    const keydown = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        event.preventDefault();
        close();
        return;
      }
      if (event.key !== "Tab" || !modal || !panel.current) return;
      const controls = [...panel.current.querySelectorAll<HTMLElement>(
        "button:not([disabled]), input:not([disabled]), a[href], textarea:not([disabled]), [tabindex]:not([tabindex='-1'])",
      )];
      if (controls.length === 0) return;
      const first = controls[0];
      const last = controls.at(-1)!;
      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault(); last.focus();
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault(); first.focus();
      }
    };
    addEventListener("keydown", keydown);
    return () => removeEventListener("keydown", keydown);
  }, [open, minimized, modal]);

  useEffect(() => {
    body.current?.scrollTo({ top: body.current.scrollHeight, behavior: "smooth" });
  }, [p.conversation.length, p.activeQuestion, p.steps.length, p.said]);

  const show = () => { setOpen(true); setMinimized(false); };
  const close = () => {
    setOpen(false);
    setMinimized(false);
    requestAnimationFrame(() => launcher.current?.focus());
  };

  if (!open) return (
    <div className="askslot">
      <button ref={launcher} className="asklaunch" onClick={show}
        aria-label="Open Ask Lex legal research assistant">
        <span className="al-ic" aria-hidden="true">✦</span>
        <span>Ask Lex</span>
      </button>
    </div>
  );

  return (
    <div className="askslot">
      {!minimized && modal ? <div className="askbackdrop" aria-hidden="true" onMouseDown={close} /> : null}
      <aside ref={panel} className={`askpanel${minimized ? " min" : ""}`} role="dialog"
             aria-modal={!minimized && modal ? "true" : undefined} aria-label="Lex legal research assistant">
        <div className="ap-head">
          <span className="ap-title"><span className="al-ic" aria-hidden="true">✦</span> Ask Lex</span>
          {started ? <button className="ap-reset" onClick={p.onReset}
            aria-label="New conversation">New</button> : null}
          <button className="ap-x ap-min" onClick={() => setMinimized(!minimized)}
                  aria-label={minimized ? "Expand assistant" : "Minimise assistant"}>
            {minimized ? "▴" : "▾"}
          </button>
          <button className="ap-x" onClick={close} aria-label="Close assistant">✕</button>
        </div>

        {!minimized ? <>
          <p className="ap-notice">
            You are talking to an <b>AI assistant</b>. It answers only from the laws Lex holds,
            with the date and source for every claim, or it declines. It can still be wrong and
            it is not legal advice. Up to six turns stay in this browser tab; submitted text is
            processed by this server and Azure OpenAI. Do not submit confidential client facts.
            <a href="/developers#assistant-data">Data handling</a>.
          </p>

          <div className="ap-body" ref={body}>
            {!started ? <div className="ap-sugg">
              <p className="ap-sugg-h">Try a research task</p>
              {STARTER_PROMPTS.map((suggestion) =>
                <button key={suggestion} className="ap-chip" onClick={() => p.onSubmit(suggestion)}>
                  {suggestion}
                </button>)}
            </div> : null}

            {p.conversation.length > 0 ? <ol className="ap-conversation"
              aria-label="Conversation history">
              {p.conversation.map((message, index) => <li key={index} className={message.role}>
                <b>{message.role === "user" ? "You" : "Lex"}</b>
                <span>{message.content}</span>
              </li>)}
            </ol> : null}

            {p.activeQuestion ? <div className="ap-current user">
              <b>You</b><span>{p.activeQuestion}</span>
            </div> : null}

            {p.steps.length > 0 ? <ol className="steps" aria-live="polite"
              aria-label="What the assistant is finding">
              {p.steps.map((step, index) => <li key={index} className={step.kind}>
                <span>{step.text}</span>
                {step.work ? <button className="chipmini" onClick={() => p.onOpenStep(step)}>open →</button> : null}
              </li>)}
              {p.busy ? <li className="pending"><span>working…</span></li> : null}
            </ol> : null}

            {p.said ? <div className="said"><b>what I found</b>{p.said}</div> : null}
            {p.said && (p.followUps?.length ?? 0) > 0 ? <div className="ap-next">
              {p.followUps!.map((followUp) => <button key={followUp.label} className="ap-chip next"
                onClick={followUp.run}>{followUp.label}</button>)}
            </div> : null}
          </div>

          <form className="ap-form" onSubmit={(event) => {
            event.preventDefault(); p.onSubmit(p.q); p.setQ("");
          }}>
            <input ref={input} name="assistant-question" value={p.q}
              onChange={(event) => p.setQ(event.target.value)} disabled={p.busy}
              placeholder="Ask about a law, article or date" aria-label="Ask Lex" />
            <button type="submit" disabled={p.busy}>{p.busy ? "…" : "Ask"}</button>
          </form>
        </> : null}
      </aside>
    </div>
  );
}
