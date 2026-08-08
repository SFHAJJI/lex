export type SearchSubmission = {
  query: string;
  asOf?: string;
};

/** Build the complete state change for one search-form submission. */
export function searchSubmission(text: string): SearchSubmission {
  const date = /^\s*(\d{4}-\d{2}-\d{2})\s*$/.exec(text);
  return date ? { query: "", asOf: date[1] } : { query: text };
}
