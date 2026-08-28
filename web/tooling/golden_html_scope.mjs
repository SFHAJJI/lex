import { readSync } from "node:fs";
import { TextDecoder } from "node:util";

import { parse, Tokenizer, TokenizerMode } from "parse5";


const MAX_HTML_BYTES = 8 * 1024 * 1024;
const HTML_NAMESPACE = "http://www.w3.org/1999/xhtml";
const VOID_ELEMENTS = new Set([
  "area", "base", "br", "col", "embed", "hr", "img", "input",
  "link", "meta", "param", "source", "track", "wbr",
]);
const ID_SELECTOR = /^#([A-Za-z][A-Za-z0-9_-]{0,63})$/;
const TEST_ID_SELECTOR =
  /^\[data-testid="([A-Za-z][A-Za-z0-9_-]{0,63})"\]$/;
const SELECT_START_TAGS = new Set([
  "hr", "optgroup", "option", "script", "template",
]);
const SELECT_END_TAGS = new Set([
  "optgroup", "option", "script", "template",
]);


class Rejection extends Error {}


function requireCondition(condition) {
  if (!condition) {
    throw new Rejection();
  }
}


function readBoundedStdin() {
  const input = Buffer.alloc(MAX_HTML_BYTES + 1);
  let length = 0;
  while (length < input.length) {
    const count = readSync(0, input, length, input.length - length, null);
    if (count === 0) {
      break;
    }
    length += count;
  }
  requireCondition(length <= MAX_HTML_BYTES);
  return input.subarray(0, length);
}


function selectorParts(selector) {
  const id = ID_SELECTOR.exec(selector);
  if (id !== null) {
    return ["id", id[1]];
  }
  const testId = TEST_ID_SELECTOR.exec(selector);
  requireCondition(testId !== null);
  return ["data-testid", testId[1]];
}


function asciiLower(value) {
  return value.replace(/[A-Z]/gu, (character) => character.toLowerCase());
}


function children(node) {
  return Array.isArray(node.childNodes) ? node.childNodes : [];
}


function visitDom(node, visitor) {
  visitor(node);
  for (const child of children(node)) {
    visitDom(child, visitor);
  }
}


function visitAllParsedNodes(node, visitor) {
  visitor(node);
  for (const child of children(node)) {
    visitAllParsedNodes(child, visitor);
  }
  if (node.tagName === "template" && node.content !== undefined) {
    visitAllParsedNodes(node.content, visitor);
  }
}


function sourceInterval(node) {
  const location = node.sourceCodeLocation;
  if (location === undefined || location === null) {
    return null;
  }
  if (!Number.isSafeInteger(location.startOffset)
      || !Number.isSafeInteger(location.endOffset)
      || location.startOffset < 0
      || location.endOffset <= location.startOffset) {
    throw new Rejection();
  }
  return [location.startOffset, location.endOffset];
}


function targetInterval(target, source) {
  const location = target.sourceCodeLocation;
  requireCondition(location !== undefined && location !== null);
  const startTag = location.startTag;
  requireCondition(startTag !== undefined
    && Number.isSafeInteger(startTag.startOffset)
    && Number.isSafeInteger(startTag.endOffset)
    && startTag.startOffset >= 0
    && startTag.endOffset > startTag.startOffset);

  const rawStartTag = source.slice(startTag.startOffset, startTag.endOffset);
  const isHtmlElement = target.namespaceURI === HTML_NAMESPACE;
  const isHtmlVoid = isHtmlElement && VOID_ELEMENTS.has(target.tagName);
  if (isHtmlElement && !isHtmlVoid) {
    requireCondition(!/\/\s*>$/u.test(rawStartTag));
    const endTag = location.endTag;
    requireCondition(endTag !== undefined
      && Number.isSafeInteger(endTag.endOffset)
      && endTag.endOffset > startTag.endOffset);
    return [startTag.startOffset, endTag.endOffset];
  }
  if (isHtmlVoid) {
    return [startTag.startOffset, startTag.endOffset];
  }

  if (location.endTag !== undefined) {
    requireCondition(Number.isSafeInteger(location.endTag.endOffset)
      && location.endTag.endOffset > startTag.endOffset);
    return [startTag.startOffset, location.endTag.endOffset];
  }
  requireCondition(/\/\s*>$/u.test(rawStartTag));
  return [startTag.startOffset, startTag.endOffset];
}


function requireLegacySelectTokens(select, source) {
  targetInterval(select, source);
  const location = select.sourceCodeLocation;
  requireCondition(location.startTag !== undefined
    && location.endTag !== undefined
    && Number.isSafeInteger(location.startTag.endOffset)
    && Number.isSafeInteger(location.endTag.startOffset)
    && location.startTag.endOffset <= location.endTag.startOffset);
  const interior = source.slice(
    location.startTag.endOffset, location.endTag.startOffset);

  let valid = true;
  let tokenizer;
  const handler = {
    onComment() {},
    onDoctype() {
      valid = false;
    },
    onStartTag(token) {
      if (!SELECT_START_TAGS.has(token.tagName)) {
        valid = false;
      }
      if (token.tagName === "script") {
        tokenizer.state = TokenizerMode.SCRIPT_DATA;
      }
    },
    onEndTag(token) {
      if (!SELECT_END_TAGS.has(token.tagName)) {
        valid = false;
      }
      if (token.tagName === "script") {
        tokenizer.state = TokenizerMode.DATA;
      }
    },
    onEof() {},
    onCharacter() {},
    onNullCharacter() {
      valid = false;
    },
    onWhitespaceCharacter() {},
    onParseError() {
      valid = false;
    },
  };
  tokenizer = new Tokenizer({ sourceCodeLocationInfo: false }, handler);
  tokenizer.write(interior, true);
  requireCondition(valid);
}


function requireExactDomScope(document, target, source) {
  const [targetStart, targetEnd] = targetInterval(target, source);
  const descendants = new Set();
  visitDom(target, (node) => descendants.add(node));

  const ancestors = new Set();
  for (let node = target.parentNode; node !== undefined && node !== null;
       node = node.parentNode) {
    ancestors.add(node);
  }

  const seenStartTags = new Set();
  for (const descendant of descendants) {
    const interval = sourceInterval(descendant);
    if (interval !== null) {
      requireCondition(interval[0] >= targetStart && interval[1] <= targetEnd);
      if (descendant.tagName !== undefined) {
        targetInterval(descendant, source);
      }
    }
    const startTag = descendant.sourceCodeLocation?.startTag;
    if (startTag !== undefined) {
      const key = `${startTag.startOffset}:${startTag.endOffset}`;
      requireCondition(!seenStartTags.has(key));
      seenStartTags.add(key);
    }
  }

  visitAllParsedNodes(document, (node) => {
    if (descendants.has(node) || ancestors.has(node)) {
      return;
    }
    const interval = sourceInterval(node);
    if (interval !== null) {
      requireCondition(interval[1] <= targetStart || interval[0] >= targetEnd);
    }
  });

  const byteStart = Buffer.byteLength(source.slice(0, targetStart), "utf8");
  const byteEnd = Buffer.byteLength(source.slice(0, targetEnd), "utf8");
  requireCondition(byteEnd > byteStart);
  return [byteStart, byteEnd];
}


function main() {
  requireCondition(process.argv.length === 3);
  const selector = process.argv[2];
  const [attribute, value] = selectorParts(selector);
  const input = readBoundedStdin();
  const source = new TextDecoder("utf-8", {
    fatal: true,
    ignoreBOM: true,
  }).decode(input);

  let invalidHtml = false;
  const document = parse(source, {
    sourceCodeLocationInfo: true,
    onParseError(error) {
      if (error.code !== "missing-doctype") {
        invalidHtml = true;
      }
    },
  });
  requireCondition(!invalidHtml);

  let hasDeclarativeShadowDom = false;
  visitAllParsedNodes(document, (node) => {
    if (node.namespaceURI === HTML_NAMESPACE
        && node.tagName === "template"
        && Array.isArray(node.attrs)
        && node.attrs.some((item) => item.namespace === undefined
          && item.name === "shadowrootmode")) {
      hasDeclarativeShadowDom = true;
    }
    if (node.namespaceURI === HTML_NAMESPACE && node.tagName === "select") {
      requireLegacySelectTokens(node, source);
    }
  });
  requireCondition(!hasDeclarativeShadowDom);

  const foldId = attribute === "id" && document.mode === "quirks";
  const expectedValue = foldId ? asciiLower(value) : value;
  const matches = [];
  visitDom(document, (node) => {
    if (matches.length >= 2 || !Array.isArray(node.attrs)) {
      return;
    }
    if (node.attrs.some((item) => {
      const candidateValue = foldId ? asciiLower(item.value) : item.value;
      return item.namespace === undefined
        && item.name === attribute && candidateValue === expectedValue;
    })) {
      matches.push(node);
    }
  });

  const result = { schema: "lex-html-scope/1", count: matches.length };
  if (matches.length === 1) {
    const [start, end] = requireExactDomScope(document, matches[0], source);
    result.start = start;
    result.end = end;
  }
  process.stdout.write(`${JSON.stringify(result)}\n`);
}


try {
  main();
} catch (_error) {
  process.stderr.write("HTML scope rejected\n");
  process.exitCode = 1;
}
