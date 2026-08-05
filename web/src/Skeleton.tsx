/**
 * Skeletons: the shape of what is coming, not the word "Loading".
 *
 * A law can take a second or two to arrive, because a code is thousands of provisions and the
 * request is a real one. During that time the page said "Loading…" on an otherwise empty screen,
 * which tells a reader nothing about whether they are in the right place, whether the thing is
 * large, or whether it has hung. A shape answers all three before the content does, and it makes
 * the arrival feel like filling in rather than jumping.
 *
 * Each variant deliberately mirrors the real layout it stands in for, including the proportions,
 * so nothing moves when the content lands.
 */

/** A single shimmering block. Width is a percentage so rows look ragged like real text. */
function Bar({ w = 100, h = 14, mt = 0 }: { w?: number; h?: number; mt?: number }) {
  return <span className="sk" style={{ width: `${w}%`, height: h, marginTop: mt }} />;
}

/** Pseudo-random but stable widths, so a skeleton does not shimmer differently on every render. */
const widths = (seed: number, n: number) =>
  Array.from({ length: n }, (_, i) => 72 + ((seed * 37 + i * 53) % 26));

/**
 * A law: the contents rail beside the article stack. Matches Provision's two-column layout so
 * the text lands exactly where its outline promised.
 */
export function LawSkeleton() {
  return (
    <div className="sk-law" aria-busy="true" aria-label="Loading the law">
      <aside className="sk-toc">
        <Bar w={52} h={17} />
        {widths(3, 7).map((w, i) => <Bar key={i} w={w} mt={13} />)}
      </aside>
      <div className="sk-body">
        {[0, 1, 2].map((a) => (
          <div className="sk-art" key={a}>
            <Bar w={22} h={16} />
            {widths(a + 1, 4).map((w, i) => <Bar key={i} w={w} mt={9} />)}
          </div>
        ))}
      </div>
    </div>
  );
}

/** The changes report: layer tabs, the counts row, then the bars in descending order. */
export function ReportSkeleton() {
  return (
    <div aria-busy="true" aria-label="Loading the report">
      <div className="sk-row">{[64, 78, 58, 86, 70].map((w, i) => <Bar key={i} w={w / 8} h={26} />)}</div>
      <div className="sk-row" style={{ marginTop: 12 }}>
        {[92, 74, 88].map((w, i) => <Bar key={i} w={w / 9} h={20} />)}
      </div>
      <div className="sk-bars">
        {[100, 88, 76, 68, 61, 55, 50, 46].map((w, i) => (
          <span className="sk-bar" key={i}><Bar w={w} h={30} /></span>
        ))}
      </div>
    </div>
  );
}

/** Search results: the two groups this page always shows, laws then articles. */
export function ResultsSkeleton() {
  return (
    <div aria-busy="true" aria-label="Searching">
      <Bar w={14} h={13} />
      {widths(5, 3).map((w, i) => (
        <div className="sk-hit" key={i}><Bar w={w} h={15} /><Bar w={w - 34} h={11} mt={7} /></div>
      ))}
      <div style={{ height: 18 }} />
      <Bar w={22} h={13} />
      {widths(9, 4).map((w, i) => (
        <div className="sk-hit" key={i}><Bar w={w} h={15} /><Bar w={w - 22} h={11} mt={7} /></div>
      ))}
    </div>
  );
}

/** A comparison: the stats row, then two columns of text with edits scattered through. */
export function CompareSkeleton() {
  return (
    <div aria-busy="true" aria-label="Loading the comparison">
      <div className="sk-row">{[1, 2, 3, 4].map((i) => <Bar key={i} w={9} h={20} />)}</div>
      <div className="sk-cols">
        {[0, 1].map((c) => (
          <div key={c}>{widths(c + 7, 9).map((w, i) => <Bar key={i} w={w} mt={10} />)}</div>
        ))}
      </div>
    </div>
  );
}
