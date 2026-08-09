import { useCallback, useEffect, useRef, useState } from "react";
import {
  actionableClarificationChoices,
  askQuestionError,
  askStreaming,
  boundedAskHistory,
  clarificationFollowUp,
  type AskMessage,
  type AskReply,
  type ClarificationChoice,
  type Step,
} from "./api";
import AskPanel from "./AskPanel";
import { assistantWorkspaceUrl, stepWorkspaceUrl } from "./assistantShell";

const ASK_HISTORY_KEY = "lex.ask.history.v1";

function restoredAskHistory(): AskMessage[] {
  try {
    return boundedAskHistory(JSON.parse(sessionStorage.getItem(ASK_HISTORY_KEY) ?? "[]"));
  } catch {
    return [];
  }
}

export interface AssistantControllerProps {
  onReply?: (reply: AskReply) => void;
  onOpenStep?: (step: Step) => void;
  followUps?: { label: string; run: () => void }[];
  standalone?: boolean;
}

/** One conversation implementation for the workspace and every server-rendered research page. */
export default function AssistantController({
  onReply,
  onOpenStep,
  followUps: contextualFollowUps,
  standalone = false,
}: AssistantControllerProps) {
  const [q, setQ] = useState("");
  const [busy, setBusy] = useState(false);
  const [steps, setSteps] = useState<Step[]>([]);
  const [said, setSaid] = useState<string>();
  const [resultUrl, setResultUrl] = useState<string>();
  const restored = useRef(restoredAskHistory()).current;
  const [conversation, setConversation] = useState<AskMessage[]>(restored);
  const [activeQuestion, setActiveQuestion] = useState<string>();
  const [clarification, setClarification] = useState<{
    context: string; choices: ClarificationChoice[];
  }>();
  const abort = useRef<AbortController>();
  const history = useRef<AskMessage[]>(restored);

  useEffect(() => () => abort.current?.abort(), []);

  const submit = useCallback(async (text: string) => {
    const question = text.trim();
    if (!question || busy) return;
    const questionError = askQuestionError(question);
    if (questionError) {
      setConversation(history.current);
      setActiveQuestion(question);
      setSaid(questionError);
      setSteps([]);
      setClarification(undefined);
      return;
    }
    setConversation(history.current);
    setActiveQuestion(question);
    setBusy(true);
    setSaid(undefined);
    setResultUrl(undefined);
    setSteps([]);
    setClarification(undefined);
    abort.current?.abort();
    const controller = new AbortController();
    abort.current = controller;
    try {
      const messages = boundedAskHistory([
        ...history.current,
        { role: "user", content: question } as AskMessage,
      ]);
      const reply = await askStreaming(
        messages,
        (step) => {
          if (abort.current === controller)
            setSteps((previous) => [...previous, step]);
        },
        controller.signal,
      );
      if (abort.current !== controller) return;
      const visibleReply = reply.clarification?.question ?? reply.reply;
      setSaid(reply.error ?? visibleReply);
      const choices = reply.clarification
        ? actionableClarificationChoices(reply.clarification)
        : undefined;
      setClarification(reply.clarification && choices
        ? { context: question, choices }
        : undefined);
      if (!reply.error) {
        history.current = boundedAskHistory([
          ...messages,
          { role: "assistant", content: visibleReply } as AskMessage,
        ]);
        try {
          sessionStorage.setItem(ASK_HISTORY_KEY, JSON.stringify(history.current));
        } catch { /* Conversation memory is optional in restricted browsing modes. */ }
      }
      if (reply.narrated === false) setSteps([]);
      if (standalone) setResultUrl(assistantWorkspaceUrl(reply.ui));
      onReply?.(reply);
    } catch {
      if (!controller.signal.aborted) setSaid("The request failed, try again.");
    } finally {
      if (abort.current === controller) setBusy(false);
    }
  }, [busy, onReply, standalone]);

  const resetConversation = useCallback(() => {
    abort.current?.abort();
    history.current = [];
    setConversation([]);
    setActiveQuestion(undefined);
    setQ("");
    setSaid(undefined);
    setResultUrl(undefined);
    setSteps([]);
    setClarification(undefined);
    setBusy(false);
    try { sessionStorage.removeItem(ASK_HISTORY_KEY); } catch { /* Optional tab memory. */ }
  }, []);

  const followUps = clarification
    ? clarification.choices.map((choice) => ({
        label: choice.label,
        run: () => submit(clarificationFollowUp(clarification.context, choice)),
      }))
    : [
        ...(resultUrl ? [{ label: "Open the structured result", run: () => location.assign(resultUrl) }] : []),
        ...(contextualFollowUps ?? []),
      ];

  return <AskPanel
    q={q}
    setQ={setQ}
    busy={busy}
    steps={steps}
    said={said}
    conversation={conversation}
    activeQuestion={activeQuestion}
    onSubmit={submit}
    onReset={resetConversation}
    followUps={followUps}
    onOpenStep={onOpenStep ?? ((step) => {
      const url = stepWorkspaceUrl(step);
      if (url) location.assign(url);
    })}
  />;
}
