export interface OutlineProvision {
  anchor: string;
}

export interface AnchorPopulationAssessment {
  comparable: boolean;
  before: number;
  after: number;
  shared: number;
}

const MIN_SHARED_FRACTION = 0.5;

export function scopeOutline<T extends OutlineProvision>(items: T[], anchor?: string): T[] {
  return anchor ? items.filter((item) => item.anchor === anchor) : items;
}

/**
 * During a rolling deployment an older MCP server can return status=ok plus a full outline after
 * ignoring an anchor filter. Once the current client scopes that response, zero rows means the
 * requested anchor is absent, not that the publisher evidence is unavailable. Explicit evidence
 * failures such as no_version or text_withheld remain unavailable on both old and new servers.
 */
export function isUnavailableOutlineSide(
  provisionCount: number, status: string | undefined, anchorRequested: boolean,
): boolean {
  if (provisionCount !== 0 || !status || status === "anchor_not_in_version") return false;
  return !anchorRequested || status !== "ok";
}

/**
 * A shared extraction profile proves which parser ran, not that a publisher preserved its
 * internal identifiers. Large Legilux recueils can regenerate almost every art_N identifier
 * after one consolidation. Below this continuity floor, added/removed counts would describe
 * identifier churn rather than legislation, so the only honest whole-document result is refusal.
 *
 * The rule applies to small instruments too. If no anchor survives, Lex cannot distinguish a
 * genuine replacement from publisher re-anchoring and must not invent that distinction.
 */
export function assessAnchorPopulation(
  before: OutlineProvision[], after: OutlineProvision[],
): AnchorPopulationAssessment {
  const beforeAnchors = new Set(before.map((item) => item.anchor));
  const afterAnchors = new Set(after.map((item) => item.anchor));
  const smaller = Math.min(beforeAnchors.size, afterAnchors.size);
  const shared = [...beforeAnchors].filter((anchor) => afterAnchors.has(anchor)).length;
  const comparable = smaller > 0 && shared / smaller >= MIN_SHARED_FRACTION;
  return { comparable, before: beforeAnchors.size, after: afterAnchors.size, shared };
}
