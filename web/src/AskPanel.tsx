import { useEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";
import type { AskExecutionDetails, AskMessage, Step } from "./api";
import { STARTER_PROMPTS, parseAssistantPanelState } from "./assistantShell";

export interface AskPanelProps {
  q: string;
  setQ: (value: string) => void;
  busy: boolean;
  steps: Step[];
  said?: string;
  conversation: AskMessage[];
  activeQuestion?: string;
  execution?: AskExecutionDetails;
  onSubmit: (text: string) => void;
  onReset: () => void;
  onOpenStep: (step: Step) => void;
  followUps?: { label: string; run: () => void }[];
}

function ExecutionDetails({ value }: { value?: AskExecutionDetails }) {
  return value ? <details className="ap-execution">
    <summary>Plan and execution details</summary>
    <pre tabIndex={0} aria-label="Plan and execution details JSON">{JSON.stringify(value, null, 2)}</pre>
  </details> : null;
}

const PANEL_KEY = "lex.ask.panel.v1";
const MODAL_QUERY = "(width < 1100px)";
const REDUCED_MOTION_QUERY = "(prefers-reduced-motion: reduce)";
// Kept in step with the .askpanel transition in styles.css.
const PANEL_MOTION_MS = 220;
const prefersReducedMotion = () =>
  typeof matchMedia === "function" && matchMedia(REDUCED_MOTION_QUERY).matches;
const modalViewport = () => typeof matchMedia === "function" && matchMedia(MODAL_QUERY).matches;

export default function AskPanel(p: AskPanelProps) {
  // Default-open applies only where the panel docks beside the content. Below the modal boundary it
  // would cover the whole page before a first-time reader has seen anything, so there it waits to be
  // asked. A stored choice always wins over both.
  const initial = useRef((() => {
    let raw: string | null = null;
    try { raw = sessionStorage.getItem(PANEL_KEY); } catch { raw = null; }
    const stored = parseAssistantPanelState(raw);
    return raw === null && modalViewport() ? { open: false, minimized: false } : stored;
  })()).current;
  const [open, setOpen] = useState(initial.open);
  const [minimized, setMinimized] = useState(initial.minimized);
  const [closing, setClosing] = useState(false);
  const [entered, setEntered] = useState(false);
  const reducedMotion = useRef(prefersReducedMotion()).current;
  const [modal, setModal] = useState(modalViewport);
  const body = useRef<HTMLDivElement>(null);
  const panel = useRef<HTMLElement | null>(null);
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

  useEffect(() => {
    if (!open || minimized || !modal) return;
    const background = [...document.querySelectorAll<HTMLElement>(
      "body > header, body > main, body > footer",
    )];
    const previous = background.map((element) => element.inert);
    background.forEach((element) => { element.inert = true; });
    return () => background.forEach((element, index) => { element.inert = previous[index]; });
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
    if (!open || entered) return;
    // No animation frame outside a browser, and none wanted when motion is reduced. Both land on
    // the open state directly rather than leaving the panel stuck in its entering transform.
    if (reducedMotion || typeof requestAnimationFrame !== "function") {
      setEntered(true);
      return;
    }
    const frame = requestAnimationFrame(() => setEntered(true));
    return () => cancelAnimationFrame(frame);
  }, [open, entered, reducedMotion]);

  useEffect(() => {
    body.current?.scrollTo({ top: body.current.scrollHeight, behavior: "smooth" });
  }, [p.conversation.length, p.activeQuestion, p.steps.length, p.said]);

  const show = () => { setOpen(true); setMinimized(false); };
  // The panel used to appear and vanish on the same frame it mounted and unmounted, so opening the
  // assistant read as a jump-cut. It now stays mounted for the length of its own exit transition and
  // enters from the closed state on the first frame, which is also what makes the default-open panel
  // animate in on arrival instead of being there already.
  const close = () => {
    if (reducedMotion) {
      setOpen(false);
      setMinimized(false);
      requestAnimationFrame(() => launcher.current?.focus());
      return;
    }
    setClosing(true);
    window.setTimeout(() => {
      setClosing(false);
      setOpen(false);
      setMinimized(false);
      requestAnimationFrame(() => launcher.current?.focus());
    }, PANEL_MOTION_MS);
  };

  const rememberPanel = (element: HTMLElement | null) => { panel.current = element; };

  if (!open && !closing) return (
    <div className="askslot">
      <button ref={launcher} className="asklaunch" onClick={show}
        aria-label="Open Ask Lex legal research assistant">
        <span className="al-ic" aria-hidden="true">✦</span>
        <span>Ask Lex</span>
      </button>
    </div>
  );

  const panelContent = <>
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
                {message.role === "assistant" ? <ExecutionDetails value={message.execution} /> : null}
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
            {p.said ? <ExecutionDetails value={p.execution} /> : null}
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
  </>;

  if (!minimized && modal) return createPortal(
    <div className="askslot">
      <div className="askbackdrop" aria-hidden="true" onMouseDown={close} />
      <div ref={rememberPanel} className={`askpanel${closing ? " is-closing" : entered ? " is-open" : " is-entering"}`} role="dialog" aria-modal="true"
           aria-label="Lex legal research assistant">
        {panelContent}
      </div>
    </div>,
    document.body,
  );

  return (
    <div className="askslot">
      <aside ref={rememberPanel} className={`askpanel${minimized ? " min" : ""}${closing ? " is-closing" : entered ? " is-open" : " is-entering"}`}
             aria-label="Lex legal research assistant">
        {panelContent}
      </aside>
    </div>
  );
}
