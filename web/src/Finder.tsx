import type { Space, State } from "./state";
import { LawPicker, PeriodPicker, TopicSearch, type WorkHit } from "./pickers";

/**
 * The three ways into the corpus, given one shape.
 *
 * They were three different objects sitting in a row of body text: a dropdown that opened a
 * popover, a pair of bare date inputs with a select, and a text field with its own button. Three
 * silhouettes, three alignments, no frame, and the only thing telling you they were alternatives
 * was the word "or". That is what made this look unfinished rather than plain.
 *
 * One card now holds all three. The tabs say what you are looking for, the card body holds
 * whatever that needs, and the shape of the page does not move when you switch. A law, a period
 * and a topic are the three questions this corpus can answer, so they deserve to look like a
 * deliberate set rather than leftovers.
 */
export interface FinderProps {
  space: Space;
  state: State;
  today: string;
  onSpace: (s: Space) => void;
  onPickLaw: (h: WorkHit) => void;
  onPeriod: (next: Partial<State>) => void;
  onQuery: (q: string, asOf?: string) => void;
  onOpen: (work: string, date: string, anchor?: string) => void;
}

const TABS: { id: Space; label: string; hint: string }[] = [
  { id: "law", label: "A law", hint: "by name, subject or identifier" },
  { id: "time", label: "A period", hint: "what changed between two dates" },
  { id: "topic", label: "A topic", hint: "words as they appear in the text" },
];

export default function Finder(p: FinderProps) {
  const tab = TABS.find((t) => t.id === p.space) ?? TABS[0];
  return (
    <section className="finder" aria-label="Find something in the corpus">
      <div className="fin-tabs" role="tablist">
        {TABS.map((t) => (
          <button key={t.id} role="tab" aria-selected={p.space === t.id}
                  className={"fin-tab" + (p.space === t.id ? " on" : "")}
                  onClick={() => p.onSpace(t.id)}>
            {t.label}
          </button>
        ))}
        <span className="fin-hint">{tab.hint}</span>
      </div>

      <div className="fin-body">
        {p.space === "law" && <LawPicker current={undefined} onPick={p.onPickLaw} inline />}
        {p.space === "time" && (
          <PeriodPicker from={p.state.from ?? p.today} until={p.state.until ?? p.today}
                        order={p.state.order ?? "by_churn"}
                        onChange={(next) => p.onPeriod({ ...next, work: undefined })} />
        )}
        {p.space === "topic" && (
          <TopicSearch q={p.state.q ?? ""} asOf={p.state.asOf} onQuery={p.onQuery} onOpen={p.onOpen} />
        )}
      </div>
    </section>
  );
}
