import { useCallback, useEffect, useRef, useState } from "react";
import {
  actionableClarificationChoices,
  assistantUnavailableActions,
  AssistantResponseError,
  askQuestionError,
  askStreaming,
  clarificationFollowUp,
  executionDetails,
  resetAskThread,
  retainsAssistantConversation,
  shouldOfferContextualFollowUps,
  type AskMessage,
  type AskReply,
  type AskExecutionDetails,
  type AssistantUnavailableAction,
  type ClarificationChoice,
  type Step,
} from "./api";
import AskPanel from "./AskPanel";
import { assistantWorkspaceUrl, stepWorkspaceUrl } from "./assistantShell";

const MAX_VISIBLE_MESSAGES = 12;

function boundedVisibleConversation(messages: AskMessage[]): AskMessage[] {
  const visible = messages.slice(-MAX_VISIBLE_MESSAGES);
  while (visible[0]?.role === "assistant") visible.shift();
  return visible;
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
  const [allowContextualFollowUps, setAllowContextualFollowUps] = useState(false);
  const [conversation, setConversation] = useState<AskMessage[]>([]);
  const [activeQuestion, setActiveQuestion] = useState<string>();
  const [execution, setExecution] = useState<AskExecutionDetails>();
  const [unavailableActions, setUnavailableActions] = useState<AssistantUnavailableAction[]>([]);
  const [clarification, setClarification] = useState<{
    context: string; choices: ClarificationChoice[];
  }>();
  const abort = useRef<AbortController>();
  const history = useRef<AskMessage[]>([]);
  const threadToken = useRef<string>();

  useEffect(() => () => abort.current?.abort(), []);

  const submit = useCallback(async (text: string) => {
    const question = text.trim();
    if (!question || busy) return;
    const questionError = askQuestionError(question);
    if (questionError) {
      setConversation(history.current);
      setActiveQuestion(question);
      setSaid(questionError);
      setResultUrl(undefined);
      setAllowContextualFollowUps(false);
      setSteps([]);
      setClarification(undefined);
      setExecution(undefined);
      setUnavailableActions([]);
      return;
    }
    setConversation(history.current);
    setActiveQuestion(question);
    setBusy(true);
    setSaid(undefined);
    setResultUrl(undefined);
    setAllowContextualFollowUps(false);
    setSteps([]);
    setClarification(undefined);
    setExecution(undefined);
    setUnavailableActions([]);
    abort.current?.abort();
    const controller = new AbortController();
    abort.current = controller;
    const idempotencyKey = crypto.randomUUID();
    const streamedOperations = new Map<string, NonNullable<AskReply["operations"]>[number]>();
    try {
      const reply = await askStreaming(
        question,
        {
          onStep: (step) => {
            if (abort.current === controller)
              setSteps((previous) => [...previous, step]);
          },
          onOperation: (operation) => {
            if (abort.current !== controller) return;
            streamedOperations.set(operation.operation_id, operation);
            const operations = [...streamedOperations.values()]
              .sort((left, right) => left.order - right.order);
            onReply?.({ reply: "", operations, ui: operation.ui });
          },
        },
        controller.signal,
        idempotencyKey,
        threadToken.current,
      );
      if (abort.current !== controller) return;
      const visibleReply = reply.clarification?.question ?? reply.reply;
      const details = executionDetails(reply);
      setSaid(reply.error ?? visibleReply);
      setExecution(details);
      setUnavailableActions(assistantUnavailableActions(reply.ui));
      const choices = reply.clarification
        ? actionableClarificationChoices(reply.clarification)
        : undefined;
      setClarification(reply.clarification && choices
        ? { context: question, choices }
        : undefined);
      setAllowContextualFollowUps(shouldOfferContextualFollowUps(reply));
      if (!retainsAssistantConversation(reply)) {
        threadToken.current = undefined;
        history.current = [];
        setConversation([]);
      } else if (!reply.error) {
        threadToken.current = reply.thread_token;
        history.current = boundedVisibleConversation([
          ...history.current,
          { role: "user", content: question } as AskMessage,
          { role: "assistant", content: visibleReply, execution: details } as AskMessage,
        ]);
      }
      if (reply.narrated === false) setSteps([]);
      if (standalone) setResultUrl(assistantWorkspaceUrl(reply.ui));
      onReply?.(reply);
    } catch (error) {
      if (!controller.signal.aborted) {
        if (error instanceof AssistantResponseError && error.status === 409) {
          threadToken.current = undefined;
          history.current = [];
          setConversation([]);
        }
        setSaid(error instanceof AssistantResponseError
          ? error.message : "The request failed, try again.");
        setExecution(undefined);
        setUnavailableActions([]);
      }
    } finally {
      if (abort.current === controller) setBusy(false);
    }
  }, [busy, onReply, standalone]);

  const resetConversation = useCallback(() => {
    abort.current?.abort();
    abort.current = undefined;
    const token = threadToken.current;
    threadToken.current = undefined;
    if (token) void resetAskThread(token);
    history.current = [];
    setConversation([]);
    setActiveQuestion(undefined);
    setQ("");
    setSaid(undefined);
    setResultUrl(undefined);
    setAllowContextualFollowUps(false);
    setSteps([]);
    setClarification(undefined);
    setExecution(undefined);
    setUnavailableActions([]);
    setBusy(false);
  }, []);

  const followUps = clarification
    ? clarification.choices.map((choice) => ({
        label: choice.label,
        run: () => submit(clarificationFollowUp(clarification.context, choice)),
      }))
    : [
        ...unavailableActions.map((action) => ({
          label: action.label,
          run: () => location.assign(action.href),
        })),
        ...(resultUrl ? [{ label: "Open the structured result", run: () => location.assign(resultUrl) }] : []),
        ...(allowContextualFollowUps ? contextualFollowUps ?? [] : []),
      ];

  return <AskPanel
    q={q}
    setQ={setQ}
    busy={busy}
    steps={steps}
    said={said}
    conversation={conversation}
    activeQuestion={activeQuestion}
    execution={execution}
    onSubmit={submit}
    onReset={resetConversation}
    followUps={followUps}
    onOpenStep={onOpenStep ?? ((step) => {
      const url = stepWorkspaceUrl(step);
      if (url) location.assign(url);
    })}
  />;
}
