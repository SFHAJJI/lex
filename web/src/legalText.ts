import { toString } from "mdast-util-to-string";
import remarkGfm from "remark-gfm";
import remarkParse from "remark-parse";
import { unified } from "unified";

type MdNode = { type: string; value?: string; children?: MdNode[] };

// The frozen publisher profiles sometimes place an italic span directly between two text spans,
// producing `word:*formula*word`. CommonMark deliberately leaves that delimiter literal. Clean
// only delimiter pairs that survived parsing as text nodes; well-formed emphasis is already an
// AST node and literal arithmetic such as `A * B` has no pair to match.
const stripUnparsedEmphasis = (value: string) => value
  .replace(/\*\*([^*\s](?:[^*\n]*?[^*\s])?)\*\*/g, "$1")
  .replace(/\*([^*\s](?:[^*\n]*?[^*\s])?)\*/g, "$1");

function cleanUnparsedEmphasis(node: MdNode): void {
  if (node.type === "text" && node.value) node.value = stripUnparsedEmphasis(node.value);
  for (const child of node.children ?? []) cleanUnparsedEmphasis(child);
}

/** React-Markdown plugin: repair only profile emphasis CommonMark could not recognize. */
export function remarkLegalText() {
  return (tree: MdNode) => cleanUnparsedEmphasis(tree);
}

const inlineText = (node: MdNode) => toString(node as never).replace(/[ \t]+/g, " ").trim();

function blockText(node: MdNode): string {
  if (node.type === "table")
    return (node.children ?? []).map((row) =>
      (row.children ?? []).map(inlineText).join(" | ")).join("\n");
  if (node.type === "list")
    return (node.children ?? []).map(inlineText).filter(Boolean).join("\n\n");
  return inlineText(node);
}

/**
 * Project stored Markdown to the same legal words a reader sees before computing a visual diff.
 * A CommonMark parser, rather than delimiter replacement, ensures literal asterisks remain legal
 * text. The stored Markdown and its SHA-256 stay untouched and exports retain the exact source.
 */
export function legalDisplayText(markdown: string): string {
  const root = unified().use(remarkParse).use(remarkGfm).parse(markdown) as unknown as MdNode;
  cleanUnparsedEmphasis(root);
  return (root.children ?? []).map(blockText).filter(Boolean).join("\n\n");
}
