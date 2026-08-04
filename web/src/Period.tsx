import { LAYERS, type LayerId, type State } from "./state";

type Order = NonNullable<State["order"]>;

/**
 * The header of the "what changed" report.
 *
 * This is the half of the corpus a search box cannot reach. A search answers "where is this
 * said"; this answers "what moved, and how much", which is a question about a body of law rather
 * than about a document. So it is shaped like a report: a window, an ordering, counts. Those
 * controls used to be a tab of the finder, which stood them beside three search boxes and made a
 * report look like a fourth kind of search.
 *
 * The presets are here because almost nobody wants an arbitrary window. They want the last year,
 * or this year, or a period they can name. Typing two ISO dates to get that is a tax.
 */
export interface PeriodProps {
  from: string;
  until: string;
  order: Order;
  layer: LayerId;
  today: string;
  onWindow: (from: string, until: string) => void;
  onOrder: (o: Order) => void;
  onLayer: (l: LayerId) => void;
}

const iso = (d: Date) => d.toISOString().slice(0, 10);
const shift = (from: string, days: number) => {
  const d = new Date(`${from}T00:00:00Z`);
  d.setUTCDate(d.getUTCDate() + days);
  return iso(d);
};

const PRESETS: { label: string; of: (t: string) => [string, string] }[] = [
  { label: "last 30 days", of: (t) => [shift(t, -30), t] },
  { label: "last 12 months", of: (t) => [shift(t, -365), t] },
  { label: "this year", of: (t) => [`${t.slice(0, 4)}-01-01`, t] },
  { label: "last 5 years", of: (t) => [shift(t, -1826), t] },
  // Kept because it shows in one click what the tool is actually for: a body of law under
  // pressure, which is invisible in any view that shows one document at a time.
  { label: "the pandemic", of: () => ["2020-03-01", "2021-07-01"] },
];

export default function Period(p: PeriodProps) {
  const active = PRESETS.find((x) => {
    const [f, u] = x.of(p.today);
    return f === p.from && u === p.until;
  });

  return (
    <section className="period" aria-label="The window to report on">
      <div className="period-row">
        <h2>What changed</h2>
        <span className="grow" />
        <label className="pick"><i>from</i>
          <input type="date" value={p.from} max={p.until} aria-label="Start of the window"
                 onChange={(e) => e.target.value && p.onWindow(e.target.value, p.until)} />
        </label>
        <label className="pick"><i>to</i>
          <input type="date" value={p.until} min={p.from} aria-label="End of the window"
                 onChange={(e) => e.target.value && p.onWindow(p.from, e.target.value)} />
        </label>
      </div>

      <div className="period-row wrap">
        {PRESETS.map((x) => (
          <button key={x.label} className={"chip" + (active === x ? " on" : "")}
                  aria-pressed={active === x}
                  onClick={() => { const [f, u] = x.of(p.today); p.onWindow(f, u); }}>
            {x.label}
          </button>
        ))}
        <span className="grow" />
        {/* Two orderings, because "what changed" splits into two real questions: which laws are
            being worked on hardest, and what was touched most recently. */}
        <div className="seg" role="group" aria-label="How to order the report">
          <button className={p.order === "by_churn" ? "on" : ""} aria-pressed={p.order === "by_churn"}
                  onClick={() => p.onOrder("by_churn")}>most changed</button>
          <button className={p.order === "by_date" ? "on" : ""} aria-pressed={p.order === "by_date"}
                  onClick={() => p.onOrder("by_date")}>most recent</button>
        </div>
      </div>

      {/* Which layer of the law, asked once, here with the other filters. It lived above the rows
          before, so filtering down to nothing removed the control that would undo it. */}
      <div className="layers" role="tablist" aria-label="Which layer of the law">
        {LAYERS.map((l) => (
          <button key={l.id} role="tab" aria-selected={l.id === p.layer}
                  className={"layer" + (l.id === p.layer ? " on" : "")}
                  title={l.hint} onClick={() => p.onLayer(l.id)}>
            {l.label}
          </button>
        ))}
      </div>
    </section>
  );
}
