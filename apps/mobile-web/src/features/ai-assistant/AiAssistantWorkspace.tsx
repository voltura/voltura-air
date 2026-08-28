import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from "react";
import { ArrowLeft, Bot, Mic, RotateCcw, SendHorizontal, Square } from "lucide-react";
import Markdown from "react-markdown";
import type { PcProfile } from "../../foundation/connection/pcProfiles";
import { signClientPayload } from "../../foundation/connection/pairingCredentials";
import { subscribeAiAssistantResults } from "../../foundation/connection/aiAssistantResultBus";
import type { AiAssistantCapability, ClientMessage } from "../../foundation/protocol/messages";
import type { ConnectionState } from "../../foundation/connection/connectionTypes";
import {
  assistantAskTranscript,
  assistantOpenTranscript,
  assistantResetTranscript,
} from "../../foundation/ai-assistant/assistantTranscripts";
import { useSpeechDictation } from "../../foundation/input/useSpeechDictation";
import "./ai-assistant.css";

interface Props {
  activePc: PcProfile;
  capability: AiAssistantCapability;
  clientId: string;
  onBack: () => void;
  send: (message: ClientMessage) => void;
  state: ConnectionState;
}

interface ConversationMessage {
  sequence: number;
  messageId: string;
  chunks: string[];
  sender: "user" | "assistant";
  text: string;
}

const workingPhrases = [
  "Working…",
  "Checking…",
  "Looking it up…",
  "Thinking…",
  "Investigating…",
  "Putting the answer together…",
];
const maximumConversationMessages = 32;
const maximumQuestionLength = 16 * 1024;
const newOperationId = () => crypto.randomUUID().replaceAll("_", "-");
const truncateQuestion = (value: string) => {
  if (value.length <= maximumQuestionLength) {
    return value;
  }
  let end = maximumQuestionLength;
  const finalCodeUnit = value.charCodeAt(end - 1);
  if (finalCodeUnit >= 0xd800 && finalCodeUnit <= 0xdbff) {
    end -= 1;
  }
  return value.slice(0, end);
};

export default function AiAssistantWorkspace({
  activePc,
  capability,
  clientId,
  onBack,
  send,
  state,
}: Props) {
  const [messages, setMessages] = useState<ConversationMessage[]>([]);
  const [question, setQuestion] = useState("");
  const [status, setStatus] = useState("Opening the Assistant…");
  const [opened, setOpened] = useState(false);
  const [working, setWorking] = useState(capability.ownedByClient && capability.working);
  const [workingPhrase, setWorkingPhrase] = useState(0);
  const [pending, setPending] = useState(false);
  const newestRef = useRef<HTMLDivElement | null>(null);
  const questionRef = useRef<HTMLTextAreaElement | null>(null);
  const openedRef = useRef(false);
  const openAttemptedRef = useRef(false);
  const openOperationRef = useRef<string | null>(null);
  const askOperationRef = useRef<string | null>(null);
  const resetOperationRef = useRef<string | null>(null);
  const appendDictationText = useCallback((text: string) => {
    setQuestion((current) => truncateQuestion(`${current}${text}`));
  }, []);
  const dictationEnabled = opened && state === "paired" && !working && !pending;
  const { canUseSpeech, isListening, speechError, startSpeech, stopSpeech } = useSpeechDictation(
    appendDictationText,
    dictationEnabled,
  );

  const resizeQuestion = useCallback(() => {
    const input = questionRef.current;
    if (!input) {
      return;
    }
    input.style.height = "auto";
    input.style.height = `${input.scrollHeight}px`;
  }, []);

  useLayoutEffect(resizeQuestion, [question, resizeQuestion]);

  useEffect(() => {
    window.addEventListener("resize", resizeQuestion);
    return () => window.removeEventListener("resize", resizeQuestion);
  }, [resizeQuestion]);

  const sign = useCallback(
    (transcript: string) => signClientPayload(clientId, activePc.id, transcript),
    [activePc.id, clientId],
  );

  useEffect(() => {
    openedRef.current = opened;
  }, [opened]);

  useEffect(() => {
    const unsubscribe = subscribeAiAssistantResults((message) => {
      if (message.type === "ai.assistant.snapshot.start") {
        setMessages([]);
      } else if (message.type === "ai.assistant.message") {
        setMessages((current) => {
          if (current.some((item) => item.sequence === message.sequence)) {
            return current;
          }
          const existing = current.find((item) => item.messageId === message.messageId);
          if (existing) {
            const chunks = [...existing.chunks];
            chunks[message.chunkIndex] = message.text;
            return current
              .map((item) =>
                item.messageId === message.messageId
                  ? { ...item, chunks, text: chunks.join("") }
                  : item,
              )
              .sort((left, right) => left.sequence - right.sequence)
              .slice(-maximumConversationMessages);
          }
          const chunks: string[] = [];
          chunks[message.chunkIndex] = message.text;
          return [...current, { ...message, chunks, text: chunks.join("") }]
            .sort((left, right) => left.sequence - right.sequence)
            .slice(-maximumConversationMessages);
        });
      } else if (message.type === "ai.assistant.state") {
        setWorking(message.state === "working");
        setStatus(
          message.message ??
            (message.state === "failed" ? "The answer did not complete." : "Ready"),
        );
      } else if (message.type === "ai.assistant.open.result") {
        if (message.operationId !== openOperationRef.current) {
          return;
        }
        openOperationRef.current = null;
        setPending(false);
        setOpened(message.succeeded);
        if (!message.succeeded) {
          openAttemptedRef.current = false;
          setWorking(false);
        }
        setStatus(message.message);
      } else if (message.type === "ai.assistant.ask.result") {
        if (message.operationId !== askOperationRef.current) {
          return;
        }
        askOperationRef.current = null;
        setPending(false);
        if (message.succeeded) {
          setQuestion("");
        } else {
          setStatus(message.message);
        }
      } else if (message.type === "ai.assistant.reset.result") {
        if (message.operationId !== resetOperationRef.current) {
          return;
        }
        resetOperationRef.current = null;
        setPending(false);
        setStatus(message.message);
        if (message.succeeded) {
          setMessages([]);
        }
      } else if (message.type === "ai.assistant.closed") {
        openAttemptedRef.current = false;
        setOpened(false);
        setWorking(false);
        setPending(false);
        setStatus("The Assistant connection closed. Reopen the tool to continue.");
      }
    });
    return unsubscribe;
  }, []);

  useEffect(
    () => () => {
      if (openAttemptedRef.current) {
        send({ type: "ai.assistant.close", operationId: newOperationId() });
      }
    },
    [send],
  );

  useEffect(() => {
    if (!working) {
      return undefined;
    }
    const timer = window.setInterval(() => {
      setWorkingPhrase((current) => (current + 1) % workingPhrases.length);
    }, 2400);
    return () => window.clearInterval(timer);
  }, [working]);

  useEffect(() => {
    const reduceMotion = window.matchMedia?.("(prefers-reduced-motion: reduce)").matches ?? false;
    newestRef.current?.scrollIntoView({ behavior: reduceMotion ? "auto" : "smooth", block: "end" });
  }, [messages, working]);

  const open = useCallback(() => {
    if (
      state !== "paired" ||
      openedRef.current ||
      !capability.canUse ||
      !activePc.hostIdentityPublicKey
    ) {
      return;
    }
    const operationId = newOperationId();
    const signature = sign(
      assistantOpenTranscript(clientId, activePc.hostIdentityPublicKey, operationId),
    );
    if (!signature) {
      setStatus("Scan the PC pairing QR again before using the AI Assistant.");
      return;
    }
    setPending(true);
    setStatus("Opening the Assistant…");
    openAttemptedRef.current = true;
    openOperationRef.current = operationId;
    send({ type: "ai.assistant.open", operationId, clientSignature: signature });
  }, [activePc.hostIdentityPublicKey, capability.canUse, clientId, send, sign, state]);

  useEffect(() => {
    const timer = window.setTimeout(open, 0);
    return () => window.clearTimeout(timer);
  }, [open]);

  const submit = () => {
    const normalized = question.trim();
    if (!normalized || !opened || working || pending || !activePc.hostIdentityPublicKey) {
      return;
    }
    stopSpeech();
    const operationId = newOperationId();
    const signature = sign(
      assistantAskTranscript(clientId, activePc.hostIdentityPublicKey, operationId, normalized),
    );
    if (!signature) {
      setStatus("Scan the PC pairing QR again before asking a question.");
      return;
    }
    setPending(true);
    askOperationRef.current = operationId;
    send({
      type: "ai.assistant.ask",
      operationId,
      question: normalized,
      clientSignature: signature,
    });
  };

  const reset = () => {
    if (!opened || working || pending || !activePc.hostIdentityPublicKey) {
      return;
    }
    const operationId = newOperationId();
    const signature = sign(
      assistantResetTranscript(clientId, activePc.hostIdentityPublicKey, operationId),
    );
    if (!signature) {
      return;
    }
    setPending(true);
    resetOperationRef.current = operationId;
    send({ type: "ai.assistant.reset", operationId, clientSignature: signature });
  };

  const canAsk = opened && state === "paired" && !working && !pending && question.trim().length > 0;
  const welcome = useMemo(() => messages.length === 0 && !working, [messages.length, working]);

  return (
    <section className="ai-assistant-workspace" aria-label="Voltura Air AI Assistant">
      <header className="ai-assistant-header">
        <button type="button" className="icon-button" aria-label="Back" onClick={onBack}>
          <ArrowLeft aria-hidden="true" />
        </button>
        <div>
          <h2>AI Assistant</h2>
          <p>AI help from your PC</p>
        </div>
        <button
          type="button"
          className="icon-button"
          aria-label="New conversation"
          disabled={!opened || working || pending}
          onClick={reset}
        >
          <RotateCcw aria-hidden="true" />
        </button>
      </header>

      <div className="ai-assistant-conversation" aria-live="polite">
        {welcome && (
          <div className="ai-assistant-welcome">
            <Bot aria-hidden="true" />
            <h3>Ask the Voltura Air Assistant</h3>
            <p>Get help with Voltura Air and information available on your PC.</p>
            <p className="ai-assistant-disclosure">
              This is a powerful, read-only tool. It can read information with the same Windows-user
              access available when you use Codex locally on this PC. The conversation is stored by
              Codex on your PC.
            </p>
            <div className="ai-assistant-suggestions">
              <button
                type="button"
                onClick={() => setQuestion("What are the top features of Voltura Air?")}
              >
                Top features
              </button>
              <button
                type="button"
                onClick={() => setQuestion("How do I test Phone webcam before first use?")}
              >
                Test Phone webcam
              </button>
              <button
                type="button"
                onClick={() => setQuestion("What is the difference between Direct and Relay?")}
              >
                Direct or Relay?
              </button>
            </div>
          </div>
        )}
        {messages.map((message) => (
          <article key={message.messageId} className={`ai-assistant-message ${message.sender}`}>
            <strong>{message.sender === "user" ? "You" : "Assistant"}</strong>
            {message.sender === "assistant" ? (
              <Markdown
                skipHtml
                components={{
                  a: ({ children, ...properties }) => (
                    <a {...properties} target="_blank" rel="noreferrer">
                      {children}
                    </a>
                  ),
                  img: ({ alt }) => (
                    <span className="ai-assistant-omitted-media">
                      Image omitted{alt ? `: ${alt}` : ""}
                    </span>
                  ),
                }}
              >
                {message.text}
              </Markdown>
            ) : (
              <p>{message.text}</p>
            )}
          </article>
        ))}
        {working && (
          <div className="ai-assistant-working" role="status">
            <span aria-hidden="true" className="ai-assistant-working-dot" />
            {workingPhrases[workingPhrase]}
          </div>
        )}
        <div ref={newestRef} />
      </div>

      <form
        className={`ai-assistant-composer ${canUseSpeech ? "has-speech" : ""}`}
        onSubmit={(event) => {
          event.preventDefault();
          submit();
        }}
      >
        <label className="visually-hidden" htmlFor="ai-assistant-question">
          Ask the Voltura Air Assistant
        </label>
        <textarea
          ref={questionRef}
          id="ai-assistant-question"
          rows={1}
          value={question}
          maxLength={maximumQuestionLength}
          placeholder="Ask a question…"
          disabled={!opened || working || pending}
          onChange={(event) => setQuestion(event.target.value)}
          onKeyDown={(event) => {
            if (event.key === "Enter" && !event.shiftKey) {
              event.preventDefault();
              submit();
            }
          }}
        />
        {canUseSpeech && (
          <button
            type="button"
            className="ai-assistant-dictation-button"
            aria-label={isListening ? "Stop dictation" : "Start dictation"}
            aria-pressed={isListening}
            disabled={!dictationEnabled}
            onClick={isListening ? stopSpeech : startSpeech}
          >
            {isListening ? <Square aria-hidden="true" /> : <Mic aria-hidden="true" />}
          </button>
        )}
        <button
          type="submit"
          className="primary-button"
          aria-label="Send question"
          disabled={!canAsk}
        >
          <SendHorizontal aria-hidden="true" />
        </button>
      </form>
      <p className={`ai-assistant-status ${speechError ? "error" : ""}`} aria-live="polite">
        {speechError ?? (isListening ? "Listening…" : status)}
      </p>
    </section>
  );
}
