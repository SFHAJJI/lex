import { useEffect, useState } from "react";
import { first, tool } from "./api";
import { diffWords, changed, type Piece } from "./diff";
import { Empty } from "./views";

/**
 * Two dated versions of one law, and what actually moved between them.
 *
 * Three things this had to stop doing.
 *
 * It downloaded both versions IN FULL to find out what changed, which for a 317-article code is
 * two megabytes and seven hundred word-diffs on the main thread. That is why there was a
 * 120-article ceiling and why a large code answered "too large to compare". Unnecessary:
 * `as_of mode=outline` already returns text_sha256 per article and no text at all, at a quarter
 * of the size. Compare the hashes, then fetch text for ONLY the articles whose hash moved. The
 * ceiling is gone because the expensive step is gone.
 *
 * It reported typesetting as legislation. Comparing the 2015 remuneration law across two dates
 * showed 56 changed articles, and 52 of them differed only in apostrophes, the publisher having
 * moved from ' to the typographic form. Telling a reader a law changed 56 times when it changed
 * 4 times is the confident wrongness this product exists to avoid. Those are now set aside and
 * counted separately. Note what is NOT done: stored text is never normalised and no hash is
 * touched. The publisher's bytes stay verbatim; normalising happens here, to classify a
 * difference that has already been computed.
 *
 * And it hid every unchanged article behind a number. They are now a band you can open, between
 * the changes, which is what a diff tool does and what reading an amended law needs.
 */
type Row = {
  label: string; anchor: string; pieces: Piece[];
  kind: "changed" | "added" | "removed";
};

export function Compare({ work, from, to, anchor }: {
  work: string; from: string; to: string; anchor?: string;
}) {
  const [state, setState] = useState<{
    loading: boolean; error?: string; rows?: Row[];
    unchanged?: string[]; punctuation?: string[]; added?: number; removed?: number;
  }>({ loading: true });
  const [wide, setWide] = useState(() => typeof window !== "undefined" && window.innerWidth >= 900);
  const [showPunct, setShowPunct] = useState(false);
  const [showCtx, setShowCtx] = useState(false);

  useEffect(() => {
    const onResize = () => setWide(window.innerWidth >= 900);
    addEventListener("resize", onResize);
    return () => removeEventListener("resize", onResize);
  }, []);

  useEffect(() => {
    let live = true;
    setState({ loading: true });
    setShowPunct(false);
    setShowCtx(false);

    const outline = (date: string) =>
      tool<any>("as_of", { work, date, mode: "outline", ...(anchor ? { anchors: anchor } : {}) });

    const textOf = async (date: string, anchors: string[]) => {
      const map = new Map<string, string>();
      // Chunked so a very large amendment cannot build an unreasonable query string.
      for (let i = 0; i < anchors.length; i += 60) {
        const res = await tool<any>("as_of",
          { work, date, mode: "select", anchors: anchors.slice(i, i + 60).join(",") });
        const one = first<any>(res, (x) => Array.isArray(x?.provisions) && x.provisions.length > 0);
        for (const p of one?.provisions ?? []) map.set(p.anchor, p.text ?? "");
      }
      return map;
    };

    (async () => {
      const [oa, ob] = await Promise.all([outline(from), outline(to)]);
      const pa = first<any>(oa, (x) => Array.isArray(x?.provisions) && x.provisions.length > 0);
      const pb = first<any>(ob, (x) => Array.isArray(x?.provisions) && x.provisions.length > 0);
      const A = new Map<string, any>((pa?.provisions ?? []).map((p: any) => [p.anchor, p]));
      const B = new Map<string, any>((pb?.provisions ?? []).map((p: any) => [p.anchor, p]));
      if (A.size === 0 && B.size === 0) {
        if (live) setState({ loading: false, error: "NO_TEXT" });
        return;
      }

      // The hashes decide what to fetch. No article text has been downloaded at this point.
      const keys = [...new Set([...A.keys(), ...B.keys()])].sort();
      const moved: string[] = [];
      const untouched: string[] = [];
      for (const k of keys) {
        const a = A.get(k);
        const b = B.get(k);
        if (!a || !b || a.text_sha256 !== b.text_sha256) moved.push(k);
        else untouched.push(a.num ?? k);
      }

      const [ta, tb] = await Promise.all([
        textOf(from, moved.filter((k) => A.has(k))),
        textOf(to, moved.filter((k) => B.has(k))),
      ]);
      if (!live) return;

      const rows: Row[] = [];
      const punctuation: string[] = [];
      let added = 0;
      let removed = 0;
      for (const k of moved) {
        const before = ta.get(k) ?? "";
        const after = tb.get(k) ?? "";
        const label = (A.get(k) ?? B.get(k))?.num ?? k;
        const kind: Row["kind"] = !A.has(k) ? "added" : !B.has(k) ? "removed" : "changed";
        if (kind === "added") added++;
        if (kind === "removed") removed++;
        // diffWords now matches tokens on their normalised form, so a purely typographic reset
        // yields no changed pieces at all and needs no separate test here. These are still counted
        // and named rather than silently dropped, because the bytes really did move: the hash
        // changed, the law did not, and both halves of that are worth saying.
        const pieces = diffWords(before, after);
        if (!changed(pieces) && kind === "changed") { punctuation.push(label); continue; }
        rows.push({ label, anchor: k, pieces, kind });
      }
      setState({ loading: false, rows, unchanged: untouched, punctuation, added, removed });
    })().catch((e) => live && setState({ loading: false, error: String(e?.message ?? e) }));

    return () => { live = false; };
  }, [work, from, to, anchor]);

  if (state.loading) return <Empty>Comparing {from} with {to}…</Empty>;
  if (state.error === "NO_TEXT")
    return (
      <Empty>
        Neither version carries text, so there is nothing to compare. What Lex holds for this law
        is the amendment record: when each version applied, where it came from, and its hash.
      </Empty>
    );
  if (state.error) return <Empty>Could not compare these versions: {state.error}</Empty>;

  const rows = state.rows ?? [];
  const punct = state.punctuation ?? [];
  const untouched = state.unchanged ?? [];

  // Side by side needs width that a phone does not have, so it is offered rather than imposed,
  // and defaults off below 900px. Unlike a code diff there is no alignment guesswork: an article
  // is matched to its counterpart by anchor, so the two columns are the same article by identity.
  const side = (r: Row) => (
    <div className="sbs">
      <div className="sbs-col">
        <div className="sbs-h mono">{from}</div>
        <div className="lawtxt">
          {r.pieces.filter((p) => p.k !== "+").map((p, i) =>
            p.k === "-" ? <del key={i}>{p.t}</del> : <span key={i}>{p.t}</span>)}
        </div>
      </div>
      <div className="sbs-col">
        <div className="sbs-h mono">{to}</div>
        <div className="lawtxt">
          {r.pieces.filter((p) => p.k !== "-").map((p, i) =>
            p.k === "+" ? <ins key={i}>{p.t}</ins> : <span key={i}>{p.t}</span>)}
        </div>
      </div>
    </div>
  );

  const inline = (r: Row) => (
    <div className="lawtxt">
      {r.pieces.map((p, i) =>
        p.k === "+" ? <ins key={i}>{p.t}</ins> :
        p.k === "-" ? <del key={i}>{p.t}</del> :
        <span key={i}>{p.t}</span>)}
    </div>
  );

  return (
    <>
      <div className="cnt">
        <span className="tag">{rows.length} changed</span>
        <span className="tag">{state.added ?? 0} added</span>
        <span className="tag">{state.removed ?? 0} removed</span>
        <span className="tag">{untouched.length} identical</span>
        <span className="tag mono">{from} → {to}</span>
        {rows.length > 0 ? (
          <button className={"tag act" + (wide ? " on" : "")} onClick={() => setWide((v) => !v)}>
            {wide ? "one column" : "side by side"}
          </button>
        ) : null}
      </div>

      {punct.length > 0 ? (
        <div className="punct">
          <button className="punct-h" aria-expanded={showPunct} onClick={() => setShowPunct((v) => !v)}>
            {showPunct ? "▾" : "▸"} {punct.length} article{punct.length === 1 ? "" : "s"} differ only
            in punctuation, not in wording
          </button>
          {showPunct ? (
            <p className="punct-b">
              The publisher reset these between the two dates, typically swapping the typewriter
              apostrophe for the typographic one. The stored text and its hash are untouched and are
              still the publisher's bytes; only this comparison sets the difference aside.{" "}
              <span className="mono">{punct.join(" · ")}</span>
            </p>
          ) : null}
        </div>
      ) : null}

      {rows.length === 0 ? (
        <Empty>
          {punct.length > 0
            ? "No article changed its wording between these two dates."
            : "Nothing changed in the text between these two dates."}
        </Empty>
      ) : (
        rows.map((r) => (
          <article key={r.anchor} className={"art diffrow " + r.kind}>
            <h4>
              {r.label}
              {r.kind !== "changed" ? <span className="sub"> · {r.kind}</span> : null}
            </h4>
            {wide && r.kind === "changed" ? side(r) : inline(r)}
          </article>
        ))
      )}

      {untouched.length > 0 ? (
        <div className="ctx">
          <button className="ctx-h" aria-expanded={showCtx} onClick={() => setShowCtx((v) => !v)}>
            ⋯ {untouched.length} article{untouched.length === 1 ? "" : "s"} unchanged
          </button>
          {showCtx ? <p className="ctx-b mono">{untouched.join(" · ")}</p> : null}
        </div>
      ) : null}
    </>
  );
}
