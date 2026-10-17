// Captured bundles changed as a test needs them, their pages kept true (the plan's item 3.7): the tests of the screens that
// read an evidence bundle take the census's answer and change one thing, and a bundle's page members (each article's
// position, the page, the index of pages, the contents) must still hold to the rules `reading-pages.mjs` reads them by,
// or the reader refuses the answer for the wrong reason. It lives here because `web/test` admits only test files
// (`eng/verify-v3-tree.ps1`); no page imports it.

/**
 * Moves the article at `index` of a version's quoted articles (a state's or an EU wording's, as the answer writes it) to its
 * articles held without text, at the same position, with the page's counts and the contents' flag changed with it, as the
 * platform would send a version whose article carries no text. Returns the article moved.
 */
export function withoutTextAt(version, index, { reason = 'no_text_tokens' } = {}) {
  const [moved] = version.articles.splice(index, 1);
  const entry = { position: moved.position, article_identity_sha256: moved.article_identity_sha256, publisher_id: moved.publisher_id };
  if (Object.hasOwn(moved, 'validity_conflict')) entry.reason = reason;
  version.articles_without_text = [...version.articles_without_text, entry].sort((left, right) => left.position - right.position);
  version.page.quoted_in_version -= 1;
  version.page.without_text_in_version += 1;
  if (moved.validity_conflict === true) {
    version.validity_conflict_count -= 1;
    version.page.validity_conflicts_in_version -= 1;
  }
  for (const row of version.contents ?? []) {
    if (row.position === moved.position) row.text_held = false;
  }
  return moved;
}

/**
 * A version's articles, quoted and without text, in the version's order, as `{ kind, row }`.
 */
function inOrder(version) {
  return [
    ...version.articles.map((row) => ({ kind: 'articles', row })),
    ...version.articles_without_text.map((row) => ({ kind: 'articles_without_text', row })),
  ].sort((left, right) => left.row.position - right.row.position);
}

/**
 * The answers of a bundle's pages had its versions been cut every `size` articles: one answer per page, page 1 first,
 * each version's page, index and articles as the platform sends them (a version shorter than the others has no page past
 * its last, so every version must have as many pages). For tests of what is put together from pages (`reading-pages.mjs`),
 * which must equal the bundle it was cut from.
 */
export function cutIntoPages(answer, size) {
  const versions = answer.publisher === 'eu-eurlex' ? answer.wordings : answer.states;
  const cuts = versions.map((version) => {
    const all = inOrder(version);
    const pages = [];
    for (let at = 0; at < all.length; at += size) pages.push(all.slice(at, at + size));
    return { version, pages };
  });
  const count = cuts[0].pages.length;
  if (cuts.some((cut) => cut.pages.length !== count)) throw new Error('every version must have as many pages');
  const headings = answer.publisher === 'eu-eurlex';
  return Array.from({ length: count }, (_, index) => {
    const copy = structuredClone(answer);
    const list = copy.publisher === 'eu-eurlex' ? copy.wordings : copy.states;
    copy.requested_page = index === 0 ? null : index + 1;
    copy.requested_article = null;
    list.forEach((version, at) => {
      const { pages } = cuts[at];
      const page = pages[index];
      version.articles = page.filter((item) => item.kind === 'articles').map((item) => structuredClone(item.row));
      version.articles_without_text = page.filter((item) => item.kind === 'articles_without_text').map((item) => structuredClone(item.row));
      if (Object.hasOwn(version, 'validity_conflict_count')) {
        version.validity_conflict_count = version.articles.filter((article) => article.validity_conflict).length;
      }
      version.page = {
        ...version.page,
        number: index + 1,
        pages: pages.length,
        first_position: page[0].row.position,
        last_position: page[page.length - 1].row.position,
      };
      version.pages = pages.map((rows, number) => {
        const row = {
          number: number + 1,
          first_publisher_id: rows[0].row.publisher_id,
          last_publisher_id: rows[rows.length - 1].row.publisher_id,
          articles: rows.length,
        };
        if (headings) {
          row.first_heading = rows[0].row.heading ?? '';
          row.last_heading = rows[rows.length - 1].row.heading ?? '';
        }
        return row;
      });
      version.contents = null;
    });
    return copy;
  });
}

/**
 * A hand-built version made one page again after a test changed its articles: its articles, quoted and without text, put
 * in the order of their publisher ids (an EU wording's order; a hand-built sample's ids are written to sort), numbered from
 * 1, and its page and index of pages those of a version of one page, its contents none. `validity` (a Luxembourg state)
 * counts its articles whose own date differs from the state's.
 */
export function asOnePage(version, { headings = false } = {}) {
  const byId = (left, right) => (left.publisher_id < right.publisher_id ? -1 : left.publisher_id > right.publisher_id ? 1 : 0);
  version.articles.sort(byId);
  version.articles_without_text.sort(byId);
  const all = [...version.articles.map((row) => ({ quoted: true, row })), ...version.articles_without_text.map((row) => ({ quoted: false, row }))]
    .sort((left, right) => byId(left.row, right.row));
  all.forEach((item, index) => { item.row.position = index + 1; });
  version.page = {
    number: 1,
    pages: 1,
    first_position: 1,
    last_position: all.length,
    articles_in_version: all.length,
    quoted_in_version: version.articles.length,
    without_text_in_version: version.articles_without_text.length,
    ...(headings ? {} : { validity_conflicts_in_version: version.articles.filter((article) => article.validity_conflict).length }),
  };
  const first = all[0].row;
  const last = all[all.length - 1].row;
  version.pages = [{
    number: 1,
    first_publisher_id: first.publisher_id,
    last_publisher_id: last.publisher_id,
    articles: all.length,
    ...(headings ? { first_heading: first.heading ?? '', last_heading: last.heading ?? '' } : {}),
  }];
  version.contents = null;
  return version;
}
