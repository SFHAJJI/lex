import { useEffect, useState } from "react";
import { copyText, downloadMarkdown } from "./export";

export function EvidenceActions({ citation, markdown, filename }: {
  citation: string;
  markdown: () => string;
  filename: string;
}) {
  const [copyState, setCopyState] = useState<"idle" | "copied" | "failed">("idle");

  useEffect(() => {
    if (copyState === "idle") return;
    const timer = window.setTimeout(() => setCopyState("idle"), 2400);
    return () => window.clearTimeout(timer);
  }, [copyState]);

  const copy = async () => {
    try {
      await copyText(citation);
      setCopyState("copied");
    } catch {
      setCopyState("failed");
    }
  };

  return (
    <>
      <button type="button" className="tag act" aria-live="polite" onClick={copy}>
        {copyState === "copied" ? "citation copied" : copyState === "failed" ? "copy unavailable" : "copy citation"}
      </button>
      <button type="button" className="tag act" onClick={() => downloadMarkdown(filename, markdown())}>
        download evidence (.md)
      </button>
    </>
  );
}
