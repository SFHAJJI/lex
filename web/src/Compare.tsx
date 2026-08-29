import { useEffect, useState } from "react";
import { CompareSkeleton } from "./Skeleton";
import { asOfResult, first, hasTypedProvisionGaps, tool } from "./api";
import { diffWords, changed } from "./diff";
import { assessAnchorPopulation, isUnavailableOutlineSide, scopeOutline } from "./comparison";
import { Empty } from "./views";
import { EvidenceActions } from "./EvidenceActions";
import {
  comparisonCitationText, comparisonEvidenceMarkdown, evidenceFilename, type ComparisonRow,
} from "./export";
import { legalDisplayText } from "./legalText.ts";

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
type Row = ComparisonRow;

export function Compare({ work, title, from, to, anchor }: {
  work: string; title: string; from: string; to: string; anchor?: string;
}) {
  const [state, setState] = useState<{
    loading: boolean; error?: string; rows?: Row[];
    unchanged?: string[]; punctuation?: string[]; added?: number; removed?: number;
    profiles?: [string, string];
    sources?: [string | undefined, string | undefined];
    permalinks?: [string | undefined, string | undefined];
    documents?: [{ lexId?: string; validFrom?: string; validTo?: string; recordSha256?: string },
                 { lexId?: string; validFrom?: string; validTo?: string; recordSha256?: string }];
    unavailable?: [string | undefined, string | undefined];
    anchorPopulation?: { before: number; after: number; shared: number };
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
      const pa = asOfResult<any>(oa);
      const pb = asOfResult<any>(ob);
      if (hasTypedProvisionGaps(pa, anchor) || hasTypedProvisionGaps(pb, anchor)) {
        if (live) setState({
          loading: false,
          error: "TYPED_GAP",
          sources: [pa?.document?.source_uri, pb?.document?.source_uri],
        });
        return;
      }
      // Keep the client honest during rolling deployment too: an older MCP build ignored
      // anchors in outline mode and returned the whole document, which made an article-scoped
      // permalink display hundreds of unrelated additions and removals.
      const provisionsA = scopeOutline<any>(pa?.provisions ?? [], anchor);
      const provisionsB = scopeOutline<any>(pb?.provisions ?? [], anchor);
      const A = new Map<string, any>(provisionsA.map((p: any) => [p.anchor, p]));
      const B = new Map<string, any>(provisionsB.map((p: any) => [p.anchor, p]));
      const statusA: string | undefined = pa?.envelope?.status ?? pa?.status;
      const statusB: string | undefined = pb?.envelope?.status ?? pb?.status;
      // An empty selected anchor can honestly mean "this article did not exist yet". An empty
      // whole document with text_withheld/no_version cannot: treating unavailable evidence as
      // an insertion or deletion would manufacture a legislative change from a data gap.
      const unavailableA = isUnavailableOutlineSide(A.size, statusA, Boolean(anchor));
      const unavailableB = isUnavailableOutlineSide(B.size, statusB, Boolean(anchor));
      if (unavailableA || unavailableB) {
        if (live) setState({ loading: false, error: "UNAVAILABLE_SIDE", unavailable: [statusA, statusB] });
        return;
      }
      if (A.size === 0 && B.size === 0) {
        if (live) setState({ loading: false, error: "NO_TEXT" });
        return;
      }

      // Two versions are only comparable article by article when the same extraction profile
      // produced both. The Code du travail is the case that proves it: 2020 came from pdf-lu/1
      // with 13 provisions anchored art_541-8, 2026 from akn-lu/1 with 1,197 anchored
      // art_l_010-1. Pairing those by anchor yielded "1,196 added, 12 removed" — a confident,
      // detailed, entirely false account of what the legislator did, produced by two parsers
      // disagreeing. Refusing is the only honest option, and it is cheap: both profiles are
      // already in the outline payloads we just fetched.
      const profA: string | undefined = pa?.document?.extraction_profile;
      const profB: string | undefined = pb?.document?.extraction_profile;
      if (profA && profB && profA !== profB) {
        if (live) setState({ loading: false, error: "PROFILE_MISMATCH", profiles: [profA, profB] });
        return;
      }

      const anchorPopulation = assessAnchorPopulation(provisionsA, provisionsB);
      if (!anchor && !anchorPopulation.comparable) {
        if (live) setState({
          loading: false,
          error: "ANCHOR_POPULATION_MISMATCH",
          anchorPopulation,
          sources: [pa?.document?.source_uri, pb?.document?.source_uri],
          permalinks: [pa?.document?.permalink, pb?.document?.permalink],
        });
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
        // The reader renders Markdown, so compare the same visible legal text. This avoids
        // presenting profile-generated ** markers as legislation while retaining the exact
        // stored Markdown on the evidence row for download and hash verification.
        const pieces = diffWords(legalDisplayText(before), legalDisplayText(after));
        if (!changed(pieces) && kind === "changed") { punctuation.push(label); continue; }
        rows.push({
          label, anchor: k, pieces, kind,
          fromSha: A.get(k)?.text_sha256,
          toSha: B.get(k)?.text_sha256,
          fromText: before,
          toText: after,
        });
      }
      setState({
        loading: false, rows, unchanged: untouched, punctuation, added, removed,
        profiles: [profA ?? "not recorded", profB ?? "not recorded"],
        sources: [pa?.document?.source_uri, pb?.document?.source_uri],
        permalinks: [pa?.document?.permalink, pb?.document?.permalink],
        documents: [
          // Each side carries its own version-metadata digest, so the citation identifies the two
          // compared version records separately. Neither digests the compared wording.
          { lexId: pa?.document?.lex_id, validFrom: pa?.document?.valid_from, validTo: pa?.document?.valid_to,
            recordSha256: pa?.document?.record_sha256 },
          { lexId: pb?.document?.lex_id, validFrom: pb?.document?.valid_from, validTo: pb?.document?.valid_to,
            recordSha256: pb?.document?.record_sha256 },
        ],
      });
    })().catch((e) => live && setState({ loading: false, error: String(e?.message ?? e) }));

    return () => { live = false; };
  }, [work, from, to, anchor]);

  // The dates stay in view while the shape of the comparison fills in below them, so a reader
  // can tell they asked for the right pair before the answer arrives.
  if (state.loading) return (
    <>
      <p className="sub">Comparing {from} with {to}…</p>
      <CompareSkeleton />
    </>
  );
  if (state.error === "NO_TEXT")
    return (
      <Empty>
        Neither version carries text, so there is nothing to compare. What Lex holds for this law
        is the amendment record: when each version applied, where it came from, and its hash.
      </Empty>
    );
  if (state.error === "UNAVAILABLE_SIDE") {
    const [a, b] = state.unavailable ?? [];
    return (
      <Empty>
        This comparison is unavailable because the publisher evidence is incomplete for one of
        the requested dates ({from}: <span className="mono">{a ?? "no text"}</span>; {to}:{" "}
        <span className="mono">{b ?? "no text"}</span>). Lex will not turn missing text into
        apparent additions or removals. Open the <a href={`/${work.replace(":", "/")}/${from}`}>{from} record</a>
        {" and "}<a href={`/${work.replace(":", "/")}/${to}`}>{to} record</a> to inspect each source.
      </Empty>
    );
  }
  if (state.error === "TYPED_GAP")
    return (
      <Empty>
        This comparison is unavailable because one or both versions contain a typed text gap.
        Lex will not compare a partial body as if it were complete. Open the{" "}
        <a href={`/${work.replace(":", "/")}/${from}`}>{from} record</a>{" and "}
        <a href={`/${work.replace(":", "/")}/${to}`}>{to} record</a> to inspect the retained
        coordinates and official publisher sources.
      </Empty>
    );
  if (state.error === "PROFILE_MISMATCH") {
    const [pA, pB] = state.profiles ?? ["", ""];
    return (
      <Empty>
        These two versions were read by different extraction profiles, <span className="mono">{pA}</span> on
        {" "}{from} and <span className="mono">{pB}</span> on {to}, so their articles carry different
        anchor schemes and cannot be lined up. A comparison here would report differences invented by
        the two parsers rather than made by the legislator, so Lex will not draw one. Open each
        version on its own, or compare the publisher's own texts:{" "}
        <a href={`/${work.replace(":", "/")}/${from}`}>{from}</a>{" and "}
        <a href={`/${work.replace(":", "/")}/${to}`}>{to}</a>.
      </Empty>
    );
  }
  if (state.error === "ANCHOR_POPULATION_MISMATCH") {
    const population = state.anchorPopulation;
    return (
      <Empty>
        These versions use the same extraction profile, but only {population?.shared ?? 0} of the
        {" "}{Math.min(population?.before ?? 0, population?.after ?? 0)} provision anchors remain
        continuous. The publisher appears to have regenerated its internal identifiers, so a
        whole-document diff would mislabel unchanged articles as removed and added. Lex will not
        present that as a legal change. Open an article from either version to compare an anchor
        that is present on both dates, or inspect the publisher records:{" "}
        <a href={`/${work.replace(":", "/")}/${from}`}>{from}</a>{" and "}
        <a href={`/${work.replace(":", "/")}/${to}`}>{to}</a>.
      </Empty>
    );
  }
  if (state.error) return <Empty>Could not compare these versions: {state.error}</Empty>;

  const rows = state.rows ?? [];
  const punct = state.punctuation ?? [];
  const untouched = state.unchanged ?? [];
  const comparisonUrl = typeof window === "undefined" ? "" : window.location.href;
  const evidence = () => ({
    title, work, from, to, permalink: comparisonUrl,
    fromSource: state.sources?.[0], toSource: state.sources?.[1],
    fromPermalink: state.permalinks?.[0], toPermalink: state.permalinks?.[1],
    fromLexId: state.documents?.[0].lexId, toLexId: state.documents?.[1].lexId,
    fromVersionValidFrom: state.documents?.[0].validFrom, fromVersionValidTo: state.documents?.[0].validTo,
    toVersionValidFrom: state.documents?.[1].validFrom, toVersionValidTo: state.documents?.[1].validTo,
    fromExtractionProfile: state.profiles?.[0], toExtractionProfile: state.profiles?.[1],
    fromRecordSha256: state.documents?.[0].recordSha256,
    toRecordSha256: state.documents?.[1].recordSha256,
    rows, unchanged: untouched, punctuationOnly: punct, exportedAt: new Date().toISOString(),
  });
  const comparisonCitation = comparisonCitationText(evidence());

  // Side by side needs width that a phone does not have, so it is offered rather than imposed,
  // and defaults off below 900px. Unlike a code diff there is no alignment guesswork: an article
  // is matched to its counterpart by anchor, so the two columns are the same article by identity.
  // One column per date, including for articles that exist on only one side: an article that
  // was inserted genuinely has nothing on the left, and saying so beside its text is clearer
  // than the empty space that saying nothing leaves. This used to render only for kind
  // "changed", so on a comparison made mostly of insertions the toggle appeared to do nothing
  // at all — the button flipped its own label and the page below it did not move.
  const col = (r: Row, date: string, drop: "+" | "-", mark: "+" | "-") => {
    const pieces = r.pieces.filter((p) => p.k !== drop);
    const absent = (drop === "+" && r.kind === "added") || (drop === "-" && r.kind === "removed");
    return (
      <div className="sbs-col">
        <div className="sbs-h mono">{date}</div>
        {absent
          ? <p className="sub">not in this version</p>
          : <div className="lawtxt">
              {pieces.map((p, i) =>
                p.k === mark
                  ? (mark === "+" ? <ins key={i}>{p.t}</ins> : <del key={i}>{p.t}</del>)
                  : <span key={i}>{p.t}</span>)}
            </div>}
      </div>
    );
  };

  const side = (r: Row) => (
    <div className="sbs">
      {col(r, from, "+", "-")}
      {col(r, to, "-", "+")}
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
        <EvidenceActions citation={comparisonCitation} markdown={() => comparisonEvidenceMarkdown(evidence())}
                         filename={evidenceFilename(work, `${from}-to-${to}`)} />
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
            {wide ? side(r) : inline(r)}
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
