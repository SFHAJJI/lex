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
  onOnDate: (date: string) => void;
  onDoor: (go: Partial<State>) => void;
}

// Labels are the QUESTION, not the noun. "A period" and "A date" sound like the same thing and
// are not: one asks what CHANGED between two days, the other what APPLIED on one. Naming the
// question is the difference between a reader guessing and a reader choosing.
const TABS: { id: Space; label: string; hint: string }[] = [
  { id: "law", label: "Find a law", hint: "by name, subject or identifier" },
  { id: "topic", label: "Search the text", hint: "words as they appear in the articles" },
  { id: "time", label: "What changed", hint: "between two dates, across the corpus" },
  { id: "onDate", label: "In force on a day", hint: "everything that applied on one date" },
];

/**
 * Three ways in and nothing to try.
 *
 * A visitor arriving at three empty inputs has to think of a question before the product does
 * anything at all, which is a poor trade for a corpus that can answer immediately. These are real
 * queries against real laws, not a tour.
 */
const DOORS: { label: string; go: Partial<State> }[] = [
  { label: "The Constitution", go: { work: "lu-legilux:constitution-1868-10-17-n1", space: "law", mode: "read" } },
  { label: "Code du travail", go: { work: "lu-legilux:loi-2006-07-31-n2", space: "law", mode: "read" } },
  { label: "What changed this month", go: { space: "time" } },
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
      </div>

      {/* The hint used to be pinned to the far right of the tab row, level with the tabs but
          nowhere near the one it described, so it read as an unrelated caption. It belongs under
          the tab it explains and above the control it explains. */}
      <div className="fin-body">
        <p className="fin-hint">{tab.hint}</p>
        {p.space === "law" && <LawPicker current={undefined} onPick={p.onPickLaw} inline />}
        {p.space === "time" && (
          <PeriodPicker from={p.state.from ?? p.today} until={p.state.until ?? p.today}
                        order={p.state.order ?? "by_churn"}
                        onChange={(next) => p.onPeriod({ ...next, work: undefined })} />
        )}
        {p.space === "topic" && (
          <TopicSearch q={p.state.q ?? ""} asOf={p.state.asOf} onQuery={p.onQuery} onOpen={p.onOpen} />
        )}
        {p.space === "onDate" && (
          <div className="sel">
            <label className="pick"><i>on</i>
              <input type="date" aria-label="Show what was in force on this date"
                     value={p.state.asOf ?? p.today}
                     onChange={(e) => p.onOnDate(e.target.value || p.today)} /></label>
            <button type="button" onClick={() => p.onOnDate(p.state.asOf ?? p.today)}>Show</button>
            <span className="grow" />
            <span className="quick">
              <button className="eg" onClick={() => p.onOnDate(p.today)}>today</button>
              <span className="or">·</span>
              <button className="eg" onClick={() => p.onOnDate("2020-03-18")}>18 March 2020</button>
            </span>
          </div>
        )}
      </div>

      {/* Only while nothing has been asked. They exist so a first arrival sees the corpus work
          without having to invent a question; once there IS a question they are just three more
          things competing with the answer. */}
      {/* Keyed off the CURRENT tab's own input, not off any state anywhere: a leftover date from
          an earlier tab was hiding the doors on a page that had asked nothing. */}
      {(p.space === "law" || (p.space === "topic" && !p.state.q)
        || (p.space === "onDate" && !p.state.asOf)) ? (
        <div className="doors">
          <span className="doors-h">or start here</span>
          {DOORS.map((d) => (
            <button key={d.label} className="door" onClick={() => p.onDoor(d.go)}>{d.label}</button>
          ))}
        </div>
      ) : null}
    </section>
  );
}
