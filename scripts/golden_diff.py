#!/usr/bin/env python3
import argparse
from dataclasses import dataclass
import hashlib
import json
import math
from pathlib import Path, PurePosixPath
import re
import subprocess
import sys


GOLDEN_ROOT = "tests/Lex.Tests/golden"
DOCUMENT_POINTER = "/result/content/0/text"
INTENT_SCHEMA = "lex-golden-diff-intent/1"
FULL_SHA = re.compile(r"^[0-9a-f]{40}$")
MAX_JSON_BYTES = 8 * 1024 * 1024
MAX_JSON_DEPTH = 128
MAX_JSON_NODES = 100_000
MAX_HTML_BYTES = MAX_JSON_BYTES
MAX_INTENT_BYTES = 64 * 1024
MAX_EVENT_BYTES = 2 * 1024 * 1024
MAX_PR_BODY_BYTES = 128 * 1024
MAX_ADDITIONS = 10_000
MAX_HTML_SELECTORS = 256
MAX_HTML_HELPER_OUTPUT_BYTES = 4 * 1024
MAX_WORKFLOW_FILES = 256
MAX_WORKFLOW_BYTES = 1024 * 1024
MAX_WORKFLOW_LIST_BYTES = 64 * 1024
TRUSTED_WORKFLOW = ".github/workflows/trusted-golden-diff.yml"
HTML_SCOPE_HELPER = (
    Path(__file__).resolve().parents[1] / "web" / "tooling" /
    "golden_html_scope.mjs")
HTML_SELECTOR = re.compile(
    r'^(?:#(?P<id>[A-Za-z][A-Za-z0-9_-]{0,63})|'
    r'\[data-testid="(?P<testid>[A-Za-z][A-Za-z0-9_-]{0,63})"\])$')
YAML_UNICODE_ESCAPE = re.compile(
    r"\\(?:x([0-9A-Fa-f]{2})|u([0-9A-Fa-f]{4})|U([0-9A-Fa-f]{8}))")


class Rejection(ValueError):
    pass


@dataclass(frozen=True, order=True)
class Addition:
    file: str
    pointer: str
    document_pointer: str | None = None


@dataclass(frozen=True, order=True)
class HtmlSelector:
    file: str
    selector: str


@dataclass(frozen=True)
class Intent:
    additions: frozenset[Addition] = frozenset()
    html_selectors: frozenset[HtmlSelector] = frozenset()


def require(condition, message):
    if not condition:
        raise Rejection(message)


def run_git(repo, *arguments):
    try:
        completed = subprocess.run(
            ["git", *arguments],
            cwd=repo,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            check=False,
        )
    except OSError as error:
        raise Rejection("git could not be executed") from error
    return completed


def git_output(repo, *arguments):
    completed = run_git(repo, *arguments)
    require(completed.returncode == 0, "git could not inspect the requested revision")
    return completed.stdout


def reject_constant(_value):
    raise Rejection("JSON contains a non-standard numeric constant")


def finite_float(value):
    parsed = float(value)
    require(math.isfinite(parsed), "JSON number exceeds the finite numeric range")
    return parsed


def reject_duplicate_keys(pairs):
    result = {}
    for key, value in pairs:
        require(key not in result, "JSON contains a duplicate object key")
        result[key] = value
    return result


def parse_json(raw, label):
    require(len(raw) <= MAX_JSON_BYTES, f"{label} exceeds the JSON byte limit")
    try:
        text = raw.decode("utf-8") if isinstance(raw, bytes) else raw
        parsed = json.loads(
            text,
            object_pairs_hook=reject_duplicate_keys,
            parse_constant=reject_constant,
            parse_float=finite_float,
        )
        pending = [(parsed, 0)]
        nodes = 0
        while pending:
            value, depth = pending.pop()
            nodes += 1
            require(nodes <= MAX_JSON_NODES,
                    f"{label} exceeds maximum JSON node count of "
                    f"{MAX_JSON_NODES:,}")
            if isinstance(value, str):
                value.encode("utf-8")
            elif isinstance(value, list):
                require(depth < MAX_JSON_DEPTH,
                        f"{label} exceeds maximum JSON depth of {MAX_JSON_DEPTH}")
                pending.extend((item, depth + 1) for item in value)
            elif isinstance(value, dict):
                require(depth < MAX_JSON_DEPTH,
                        f"{label} exceeds maximum JSON depth of {MAX_JSON_DEPTH}")
                pending.extend((key, depth + 1) for key in value)
                pending.extend((item, depth + 1) for item in value.values())
        return parsed
    except Rejection:
        raise
    except (UnicodeDecodeError, UnicodeEncodeError, json.JSONDecodeError,
            RecursionError, ValueError) as error:
        raise Rejection(f"{label} is not strict UTF-8 JSON") from error


def parse_pointer(pointer):
    require(isinstance(pointer, str) and len(pointer) <= 4096,
            "RFC 6901 pointer must be a bounded string")
    if pointer == "":
        return []
    require(pointer.startswith("/"), "RFC 6901 pointer must be empty or start with /")
    tokens = []
    for encoded in pointer[1:].split("/"):
        decoded = []
        index = 0
        while index < len(encoded):
            if encoded[index] != "~":
                decoded.append(encoded[index])
                index += 1
                continue
            require(index + 1 < len(encoded) and encoded[index + 1] in "01",
                    "RFC 6901 pointer contains an invalid escape")
            decoded.append("~" if encoded[index + 1] == "0" else "/")
            index += 2
        tokens.append("".join(decoded))
    return tokens


def pointer_child(pointer, token):
    escaped = str(token).replace("~", "~0").replace("/", "~1")
    return f"{pointer}/{escaped}"


class JsonLayoutScanner:
    def __init__(self, source, label, style):
        self.source = source
        self.label = label
        self.style = style
        self.index = 0
        self.decoder = json.JSONDecoder()
        self.leaves = {}
        self.keys = {}

    def reject(self):
        raise Rejection(
            f"{self.label} is not canonical JSON; "
            "format-only changes are not allowed")

    def take(self, expected):
        if not self.source.startswith(expected, self.index):
            self.reject()
        self.index += len(expected)

    def token(self):
        start = self.index
        try:
            value, self.index = self.decoder.raw_decode(self.source, self.index)
        except (json.JSONDecodeError, RecursionError):
            self.reject()
        return value, self.source[start:self.index]

    def key(self, expected, pointer):
        decoded, token = self.token()
        if not isinstance(decoded, str) or decoded != expected:
            self.reject()
        self.keys[pointer] = token

    def scan(self, value, pointer="", level=0):
        if isinstance(value, dict):
            self.scan_object(value, pointer, level)
        elif isinstance(value, list):
            self.scan_array(value, pointer, level)
        else:
            decoded, token = self.token()
            if type(decoded) is not type(value) or decoded != value:
                self.reject()
            self.leaves[pointer] = token

    def scan_object(self, value, pointer, level):
        self.take("{")
        entries = list(value.items())
        if not entries:
            self.take("}")
            return
        if self.style == "indented":
            self.take("\n")
        for index, (key, child_value) in enumerate(entries):
            if index:
                self.take(",")
                if self.style == "indented":
                    self.take("\n")
            if self.style == "indented":
                self.take("  " * (level + 1))
            child = pointer_child(pointer, key)
            self.key(key, child)
            if self.style == "indented":
                self.take(": ")
            else:
                self.take(":")
            self.scan(child_value, child, level + 1)
        if self.style == "indented":
            self.take("\n" + "  " * level)
        self.take("}")

    def scan_array(self, value, pointer, level):
        self.take("[")
        if not value:
            self.take("]")
            return
        if self.style == "indented":
            self.take("\n")
        for index, child_value in enumerate(value):
            if index:
                self.take(",")
                if self.style == "indented":
                    self.take("\n")
            if self.style == "indented":
                self.take("  " * (level + 1))
            self.scan(child_value, pointer_child(pointer, index), level + 1)
        if self.style == "indented":
            self.take("\n" + "  " * level)
        self.take("]")


def scan_json_layout(source, value, label, style, *, newline=False):
    if newline:
        require(source.endswith("\n"),
                f"{label} is not canonical JSON; "
                "format-only changes are not allowed")
        source = source[:-1]
    scanner = JsonLayoutScanner(source, label, style)
    scanner.scan(value)
    if scanner.index != len(source):
        scanner.reject()
    return scanner.leaves, scanner.keys


def compare_lexemes(old, new, old_lexemes, new_lexemes, pointer, label):
    old_leaves, old_keys = old_lexemes
    new_leaves, new_keys = new_lexemes
    if isinstance(old, dict) and isinstance(new, dict):
        for key, old_value in old.items():
            child = pointer_child(pointer, key)
            require(old_keys.get(child) == new_keys.get(child),
                    f"{label}: existing JSON key lexical representation changed at "
                    f"{safe_pointer(child)}")
            compare_lexemes(
                old_value, new[key], old_lexemes, new_lexemes, child, label)
        return
    if isinstance(old, list) and isinstance(new, list):
        for index, old_value in enumerate(old):
            child = pointer_child(pointer, index)
            compare_lexemes(
                old_value, new[index], old_lexemes, new_lexemes, child, label)
        return
    require(old_leaves.get(pointer) == new_leaves.get(pointer),
            f"{label}: existing JSON lexical representation changed at "
            f"{safe_pointer(pointer)}")


def safe_pointer(pointer):
    encoded = json.dumps(pointer, ensure_ascii=True)
    if len(encoded) <= 180:
        return encoded
    digest = hashlib.sha256(pointer.encode("utf-8")).hexdigest()
    return f"<pointer length={len(pointer)} sha256={digest}>"


def value_type(value):
    if value is None:
        return "null"
    if isinstance(value, bool):
        return "boolean"
    if isinstance(value, (int, float)):
        return "number"
    if isinstance(value, str):
        return "string"
    if isinstance(value, list):
        return "array"
    return "object"


def value_summary(value):
    canonical = json.dumps(
        value,
        ensure_ascii=False,
        sort_keys=True,
        separators=(",", ":"),
    ).encode("utf-8")
    return (f"type={value_type(value)} size={len(canonical)}B "
            f"sha256={hashlib.sha256(canonical).hexdigest()}")


def compare_additive(old, new, pointer, additions, label):
    if isinstance(old, dict) and isinstance(new, dict):
        for key, old_value in old.items():
            child = pointer_child(pointer, key)
            require(key in new,
                    f"{label}: removal at {safe_pointer(child)}; "
                    f"old {value_summary(old_value)}")
        require([key for key in new if key in old] == list(old),
                f"{label}: existing JSON key order changed at "
                f"{safe_pointer(pointer)}")
        for key, old_value in old.items():
            child = pointer_child(pointer, key)
            compare_additive(old_value, new[key], child, additions, label)
        for key in new.keys() - old.keys():
            additions.add(pointer_child(pointer, key))
            require(len(additions) <= MAX_ADDITIONS,
                    f"{label}: too many JSON additions")
        return

    if isinstance(old, list) and isinstance(new, list):
        if len(new) < len(old):
            child = pointer_child(pointer, len(new))
            raise Rejection(
                f"{label}: removal at {safe_pointer(child)}; "
                f"old {value_summary(old[len(new)])}")
        for index, old_value in enumerate(old):
            compare_additive(
                old_value, new[index], pointer_child(pointer, index), additions, label)
        for index in range(len(old), len(new)):
            additions.add(pointer_child(pointer, index))
            require(len(additions) <= MAX_ADDITIONS,
                    f"{label}: too many JSON additions")
        return

    require(type(old) is type(new) and old == new,
            f"{label}: replacement at {safe_pointer(pointer)}; "
            f"old {value_summary(old)}; new {value_summary(new)}")


def skip_space(source, index):
    while index < len(source) and source[index] in " \t\r\n":
        index += 1
    return index


def locate_pointer_span(source, pointer):
    tokens = parse_pointer(pointer)
    decoder = json.JSONDecoder()

    def locate(index, remaining):
        index = skip_space(source, index)
        if not remaining:
            try:
                _value, end = decoder.raw_decode(source, index)
            except (json.JSONDecodeError, RecursionError) as error:
                raise Rejection("MCP envelope could not be scanned") from error
            return index, end

        token = remaining[0]
        if index < len(source) and source[index] == "{":
            index = skip_space(source, index + 1)
            while index < len(source) and source[index] != "}":
                try:
                    key, index = decoder.raw_decode(source, index)
                except (json.JSONDecodeError, RecursionError) as error:
                    raise Rejection("MCP envelope could not be scanned") from error
                require(isinstance(key, str), "MCP envelope has a non-string object key")
                index = skip_space(source, index)
                require(index < len(source) and source[index] == ":",
                        "MCP envelope could not be scanned")
                value_start = skip_space(source, index + 1)
                if key == token:
                    return locate(value_start, remaining[1:])
                try:
                    _value, index = decoder.raw_decode(source, value_start)
                except (json.JSONDecodeError, RecursionError) as error:
                    raise Rejection("MCP envelope could not be scanned") from error
                index = skip_space(source, index)
                if index < len(source) and source[index] == ",":
                    index = skip_space(source, index + 1)
                else:
                    break
            raise Rejection(f"MCP envelope has no value at {safe_pointer(pointer)}")

        if index < len(source) and source[index] == "[":
            require(token == "0" or token.isdigit() and not token.startswith("0"),
                    f"MCP envelope has no value at {safe_pointer(pointer)}")
            wanted = int(token)
            index = skip_space(source, index + 1)
            current = 0
            while index < len(source) and source[index] != "]":
                if current == wanted:
                    return locate(index, remaining[1:])
                try:
                    _value, index = decoder.raw_decode(source, index)
                except (json.JSONDecodeError, RecursionError) as error:
                    raise Rejection("MCP envelope could not be scanned") from error
                index = skip_space(source, index)
                if index < len(source) and source[index] == ",":
                    index = skip_space(source, index + 1)
                    current += 1
                else:
                    break
            raise Rejection(f"MCP envelope has no value at {safe_pointer(pointer)}")

        raise Rejection(f"MCP envelope has no value at {safe_pointer(pointer)}")

    return locate(0, tokens)


def mcp_document(raw, label):
    envelope = parse_json(raw, label)
    try:
        text = envelope["result"]["content"][0]["text"]
    except (KeyError, IndexError, TypeError) as error:
        raise Rejection(f"{label} has no string at {DOCUMENT_POINTER}") from error
    require(isinstance(text, str), f"{label} has no string at {DOCUMENT_POINTER}")
    document = parse_json(text, f"{label} embedded document")
    return envelope, document, text


def mask_document(raw, label):
    try:
        source = raw.decode("utf-8")
    except UnicodeDecodeError as error:
        raise Rejection(f"{label} is not strict UTF-8 JSON") from error
    start, end = locate_pointer_span(source, DOCUMENT_POINTER)
    return source[:start] + '"<document>"' + source[end:]


def require_canonical_outer_text(raw, text, label):
    try:
        source = raw.decode("utf-8")
    except UnicodeDecodeError as error:
        raise Rejection(f"{label} is not strict UTF-8 JSON") from error
    start, end = locate_pointer_span(source, DOCUMENT_POINTER)
    require(source[start:end] == json.dumps(text, ensure_ascii=False),
            f"{label} has non-canonical outer JSON string encoding at "
            f"{DOCUMENT_POINTER}")


def validate_file_path(raw, expected_family):
    require(isinstance(raw, str) and len(raw) <= 512,
            "intent file path must be a bounded repository-relative string")
    require("\\" not in raw, "intent file path must use repository-relative POSIX syntax")
    path = PurePosixPath(raw)
    require(not path.is_absolute() and path.as_posix() == raw
            and "." not in path.parts and ".." not in path.parts,
            "intent file path contains path traversal or non-canonical segments")
    require(len(path.parts) == 4 and "/".join(path.parts[:-1]) == GOLDEN_ROOT,
            f"intent file path must be directly inside {GOLDEN_ROOT}")
    name = path.name
    actual_family = family_from_name(name)
    require(actual_family == expected_family,
            f"intent entry must name a {expected_family} golden file")
    return raw


def parse_intent(raw, supplied_base):
    require(len(raw) <= MAX_INTENT_BYTES, "intent exceeds its byte limit")
    payload = parse_json(raw, "intent")
    require(isinstance(payload, dict), "intent must be a JSON object")
    keys = set(payload)
    additions_keys = {"schema", "base_commit", "additions"}
    selector_keys = {"schema", "base_commit", "html_selectors"}
    require(keys in (additions_keys, selector_keys),
            "intent must contain exactly one of additions or html_selectors")
    require(payload["schema"] == INTENT_SCHEMA,
            f"intent schema must be {INTENT_SCHEMA}")
    intent_base = payload["base_commit"]
    require(isinstance(intent_base, str) and FULL_SHA.fullmatch(intent_base),
            "intent base_commit must be a full 40-character lowercase SHA")
    require(intent_base == supplied_base,
            "intent base_commit does not match the separately supplied base SHA")

    if "additions" in payload:
        raw_additions = payload["additions"]
        require(isinstance(raw_additions, list)
                and 0 < len(raw_additions) <= MAX_ADDITIONS,
                "intent additions must be a nonempty bounded array")
        additions = set()
        for entry in raw_additions:
            require(isinstance(entry, dict) and "file" in entry and "pointer" in entry,
                    "each intent addition must name an exact file and pointer")
            file = validate_file_path(entry["file"], "json")
            pointer = entry["pointer"]
            parse_pointer(pointer)
            if file.endswith("/tools-list.txt"):
                require(set(entry) == {"file", "pointer"},
                        "tools-list additions use a direct pointer without document_pointer")
                addition = Addition(file, pointer)
            else:
                require(set(entry) == {"file", "pointer", "document_pointer"}
                        and pointer == DOCUMENT_POINTER,
                        f"tool response additions require pointer {DOCUMENT_POINTER} "
                        "and an exact document_pointer")
                document_pointer = entry["document_pointer"]
                parse_pointer(document_pointer)
                addition = Addition(file, pointer, document_pointer)
            require(addition not in additions,
                    "intent contains a duplicate addition declaration")
            additions.add(addition)
        return Intent(additions=frozenset(additions))

    raw_selectors = payload["html_selectors"]
    require(isinstance(raw_selectors, list)
            and 0 < len(raw_selectors) <= MAX_HTML_SELECTORS,
            "intent html_selectors must be a nonempty bounded array")
    selectors = set()
    files = set()
    for entry in raw_selectors:
        require(isinstance(entry, dict) and set(entry) == {"file", "selector"},
                "each HTML selector must contain exactly file and selector")
        file = validate_file_path(entry["file"], "page")
        selector = entry["selector"]
        require(isinstance(selector, str) and HTML_SELECTOR.fullmatch(selector),
                "HTML selector must be an exact bounded id or data-testid selector")
        declared = HtmlSelector(file, selector)
        require(file not in files and declared not in selectors,
                "intent contains duplicate HTML selector scope")
        files.add(file)
        selectors.add(declared)
    return Intent(html_selectors=frozenset(selectors))


def read_bounded_file(path, limit, read_error, limit_error):
    try:
        with Path(path).open("rb") as stream:
            raw = stream.read(limit + 1)
    except OSError as error:
        raise Rejection(read_error) from error
    require(len(raw) <= limit, limit_error)
    return raw


def load_intent(path, supplied_base):
    if path is None:
        return None
    raw = read_bounded_file(
        path,
        MAX_INTENT_BYTES,
        "intent file could not be read",
        "intent exceeds its byte limit",
    )
    return parse_intent(raw, supplied_base)


def fenced_json(body):
    require(isinstance(body, str), "pull request body must be text")
    require(len(body.encode("utf-8")) <= MAX_PR_BODY_BYTES,
            "pull request body exceeds its byte limit")
    blocks = []
    current = None
    for line in body.splitlines(keepends=True):
        marker = line.strip()
        if current is None and marker == "```json":
            current = []
        elif current is not None and marker == "```":
            blocks.append("".join(current))
            current = None
        elif current is not None:
            current.append(line)
    require(current is None, "pull request has an unterminated fenced JSON intent")
    require(len(blocks) <= 1, "pull request must contain exactly one fenced JSON intent")
    return blocks[0] if blocks else None


def load_event_intent(path, supplied_base):
    if path is None:
        return None
    raw = read_bounded_file(
        path,
        MAX_EVENT_BYTES,
        "GitHub event file could not be read",
        "GitHub event file exceeds its byte limit",
    )
    event = parse_json(raw, "GitHub event")
    require(isinstance(event, dict), "GitHub event must be a JSON object")
    pull_request = event.get("pull_request")
    if pull_request is None:
        before = event.get("before")
        if before is not None and before != "0" * 40:
            require(isinstance(before, str) and FULL_SHA.fullmatch(before)
                    and before == supplied_base,
                    "GitHub push event base does not match --base")
        return None
    try:
        event_base = pull_request["base"]["sha"]
        body = pull_request["body"]
    except (KeyError, TypeError) as error:
        raise Rejection("GitHub pull request event is missing base SHA or body") from error
    require(isinstance(event_base, str) and FULL_SHA.fullmatch(event_base)
            and event_base == supplied_base,
            "GitHub pull request base does not match --base")
    if body is None:
        return None
    block = fenced_json(body)
    return None if block is None else parse_intent(block.encode("utf-8"), supplied_base)


def parse_name_status(raw):
    tokens = raw.split(b"\0")
    if tokens and tokens[-1] == b"":
        tokens.pop()
    changes = []
    index = 0
    try:
        while index < len(tokens):
            status_field = tokens[index].decode("ascii")
            index += 1
            if "\t" in status_field:
                status_field, first_path = status_field.split("\t", 1)
            else:
                first_path = tokens[index].decode("utf-8")
                index += 1
            paths = [first_path]
            if status_field[0] in "RC":
                paths.append(tokens[index].decode("utf-8"))
                index += 1
            changes.append((status_field, paths))
    except (IndexError, UnicodeDecodeError, ValueError) as error:
        raise Rejection("git returned an unreadable golden diff") from error
    return changes


def changed_files(repo, base):
    raw = git_output(
        repo,
        "diff",
        "--no-ext-diff",
        "--name-status",
        "-z",
        "--find-renames",
        base,
        "HEAD",
        "--",
        GOLDEN_ROOT,
    )
    changes = parse_name_status(raw)
    files = []
    for status, paths in changes:
        kind = status[0]
        if kind == "A":
            raise Rejection("golden file additions are not allowed")
        if kind == "D":
            raise Rejection("golden file deletions are not allowed")
        if kind in "RC":
            raise Rejection("golden file renames or copies are not allowed")
        require(kind == "M", "golden file type or merge changes are not allowed")
        files.append(paths[0])
    return files


def blob(repo, revision, path, *, byte_limit=MAX_JSON_BYTES, kind="JSON"):
    object_name = f"{revision}:{path}"
    size_raw = git_output(repo, "cat-file", "-s", object_name)
    try:
        size = int(size_raw)
    except ValueError as error:
        raise Rejection("git returned an unreadable golden object size") from error
    require(size <= byte_limit, f"golden {kind} file exceeds its byte limit")
    return git_output(repo, "show", object_name)


def blob_mode(repo, revision, path):
    raw = git_output(repo, "ls-tree", revision, "--", path)
    try:
        metadata, listed_path = raw.rstrip(b"\n").split(b"\t", 1)
        mode = metadata.split(b" ", 1)[0]
        listed_path.decode("utf-8")
    except (UnicodeDecodeError, ValueError) as error:
        raise Rejection("git returned unreadable golden file metadata") from error
    require(mode in (b"100644", b"100755"),
            "golden file must remain a regular file")
    return mode


def workflow_marker_count(raw):
    try:
        text = raw.decode("utf-8")
    except UnicodeDecodeError as error:
        raise Rejection("candidate workflow is not strict UTF-8") from error
    text = re.sub(r"\\(?:\r\n|[\r\n])[ \t]*", "", text)

    def decode_escape(match):
        digits = next(group for group in match.groups() if group is not None)
        try:
            return chr(int(digits, 16))
        except ValueError as error:
            raise Rejection("candidate workflow has an invalid Unicode escape") from error

    normalized = YAML_UNICODE_ESCAPE.sub(decode_escape, text)
    return normalized.count("trusted-golden-diff")


def require_unique_trusted_check_name(repo, base):
    base_has_trusted = run_git(
        repo, "cat-file", "-e", f"{base}:{TRUSTED_WORKFLOW}").returncode == 0
    head_has_trusted = run_git(
        repo, "cat-file", "-e", f"HEAD:{TRUSTED_WORKFLOW}").returncode == 0
    require(base_has_trusted == head_has_trusted,
            "trusted workflow must have the same existence as base")
    if base_has_trusted:
        require(blob_mode(repo, base, TRUSTED_WORKFLOW)
                == blob_mode(repo, "HEAD", TRUSTED_WORKFLOW),
                "trusted workflow mode changed from base")
        base_workflow = blob(
            repo,
            base,
            TRUSTED_WORKFLOW,
            byte_limit=MAX_WORKFLOW_BYTES,
            kind="workflow",
        )
        head_workflow = blob(
            repo,
            "HEAD",
            TRUSTED_WORKFLOW,
            byte_limit=MAX_WORKFLOW_BYTES,
            kind="workflow",
        )
        require(base_workflow == head_workflow,
                "trusted workflow bytes changed from base")

    raw_paths = git_output(
        repo,
        "ls-tree",
        "-r",
        "-z",
        "--name-only",
        "HEAD",
        "--",
        ".github/workflows",
    )
    require(len(raw_paths) <= MAX_WORKFLOW_LIST_BYTES,
            "candidate workflow path list exceeds its byte limit")
    encoded_paths = raw_paths.split(b"\0")
    require(encoded_paths[-1] == b"",
            "git returned malformed candidate workflow paths")
    encoded_paths.pop()
    require(len(encoded_paths) <= MAX_WORKFLOW_FILES,
            "candidate has too many workflow files")
    paths = []
    for encoded_path in encoded_paths:
        try:
            path = encoded_path.decode("utf-8")
        except UnicodeDecodeError as error:
            raise Rejection("candidate has a non-UTF-8 workflow path") from error
        parsed = PurePosixPath(path)
        require(parsed.as_posix() == path and not parsed.is_absolute(),
                "candidate has an invalid workflow path")
        if parsed.suffix not in (".yml", ".yaml"):
            continue
        paths.append(path)

    for path in paths:
        if path == TRUSTED_WORKFLOW:
            continue
        require(blob_mode(repo, "HEAD", path) in (b"100644", b"100755"),
                "candidate workflow must be a regular file")
        raw = blob(
            repo,
            "HEAD",
            path,
            byte_limit=MAX_WORKFLOW_BYTES,
            kind="workflow",
        )
        require(workflow_marker_count(raw) == 0,
                "candidate workflow duplicates trusted-golden-diff")


def require_unchanged_mode(repo, base, path):
    require(blob_mode(repo, base, path) == blob_mode(repo, "HEAD", path),
            f"{path}: golden file mode changed")


def classify_json_file(repo, base, path):
    require_unchanged_mode(repo, base, path)
    old_raw = blob(repo, base, path)
    new_raw = blob(repo, "HEAD", path)
    additions = set()
    if path.endswith("/tools-list.txt"):
        old = parse_json(old_raw, f"base {path}")
        new = parse_json(new_raw, f"head {path}")
        old_lexemes = scan_json_layout(
            old_raw.decode("utf-8"), old, f"base {path}",
            "compact", newline=True)
        new_lexemes = scan_json_layout(
            new_raw.decode("utf-8"), new, f"head {path}",
            "compact", newline=True)
        require(old != new, f"{path}: format-only JSON change")
        compare_additive(old, new, "", additions, path)
        compare_lexemes(old, new, old_lexemes, new_lexemes, "", path)
        return {Addition(path, pointer) for pointer in additions}

    old_envelope, old, old_text = mcp_document(old_raw, f"base {path}")
    new_envelope, new, new_text = mcp_document(new_raw, f"head {path}")
    scan_json_layout(
        old_raw.decode("utf-8"), old_envelope, f"base {path}",
        "compact", newline=True)
    scan_json_layout(
        new_raw.decode("utf-8"), new_envelope, f"head {path}",
        "compact", newline=True)
    require_canonical_outer_text(old_raw, old_text, f"base {path}")
    require_canonical_outer_text(new_raw, new_text, f"head {path}")
    old_lexemes = scan_json_layout(
        old_text, old, f"base {path} embedded document", "indented")
    new_lexemes = scan_json_layout(
        new_text, new, f"head {path} embedded document", "indented")
    require(mask_document(old_raw, f"base {path}")
            == mask_document(new_raw, f"head {path}"),
            f"{path}: outer MCP envelope changed outside {DOCUMENT_POINTER}")
    require(old != new, f"{path}: format-only JSON change")
    compare_additive(old, new, "", additions, path)
    compare_lexemes(old, new, old_lexemes, new_lexemes, "", path)
    return {Addition(path, DOCUMENT_POINTER, pointer) for pointer in additions}


def html_scope(raw, selector, label):
    require(isinstance(raw, bytes) and len(raw) <= MAX_HTML_BYTES,
            f"{label} exceeds the HTML byte limit")
    require(HTML_SELECTOR.fullmatch(selector) is not None,
            "HTML selector is invalid")
    try:
        raw.decode("utf-8")
    except UnicodeDecodeError as error:
        raise Rejection(f"{label} is not strict UTF-8 HTML") from error
    try:
        completed = subprocess.run(
            ["node", str(HTML_SCOPE_HELPER), selector],
            input=raw,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            check=False,
            timeout=10,
        )
    except (OSError, subprocess.TimeoutExpired) as error:
        raise Rejection(f"{label} HTML parser could not run") from error
    require(
        len(completed.stdout) <= MAX_HTML_HELPER_OUTPUT_BYTES
        and len(completed.stderr) <= MAX_HTML_HELPER_OUTPUT_BYTES,
        f"{label} HTML parser output exceeded its byte limit")
    require(completed.returncode == 0,
            f"{label} HTML parser rejected malformed structure")
    require(not completed.stderr,
            f"{label} HTML parser produced unexpected diagnostics")
    parsed = parse_json(completed.stdout, f"{label} HTML parser output")
    require(isinstance(parsed, dict),
            f"{label} HTML parser returned an invalid result")
    count = parsed.get("count")
    require(type(count) is int and 0 <= count <= 2,
            f"{label} HTML parser returned an invalid result")
    expected_keys = {"schema", "count"}
    if count == 1:
        expected_keys.update(("start", "end"))
    require(set(parsed) == expected_keys
            and parsed.get("schema") == "lex-html-scope/1",
            f"{label} HTML parser returned an invalid result")
    if count != 1:
        return count, None
    start = parsed["start"]
    end = parsed["end"]
    require(type(start) is int and type(end) is int
            and 0 <= start < end <= len(raw),
            f"{label} HTML parser returned invalid byte boundaries")
    return count, (start, end)


def classify_html_file(repo, base, path, selector):
    require_unchanged_mode(repo, base, path)
    old_raw = blob(repo, base, path, byte_limit=MAX_HTML_BYTES, kind="HTML")
    new_raw = blob(repo, "HEAD", path, byte_limit=MAX_HTML_BYTES, kind="HTML")
    require(old_raw != new_raw,
            f"{path}: HTML file bytes did not change")
    old_count, old_span = html_scope(old_raw, selector, f"base {path}")
    new_count, new_span = html_scope(new_raw, selector, f"head {path}")
    require(old_count <= 1,
            f"{path}: declared selector must occur at most once in base HTML")
    require(new_count == 1,
            f"{path}: declared selector must occur exactly once in head HTML")
    new_start, new_end = new_span
    if old_span is None:
        require(new_raw[:new_start] + new_raw[new_end:] == old_raw,
                f"{path}: selected subtree must be the only inserted HTML bytes")
        return
    old_start, old_end = old_span
    require(old_raw[:old_start] == new_raw[:new_start]
            and old_raw[old_end:] == new_raw[new_end:],
            f"{path}: bytes outside the selected subtree changed")


def family_from_name(name):
    if name.startswith("page-") and name.endswith(".txt"):
        return "page"
    if name == "tools-list.txt" or name.startswith("tool-") and name.endswith(".txt"):
        return "json"
    return "unknown"


def family(path):
    parsed = PurePosixPath(path)
    if (parsed.as_posix() != path or len(parsed.parts) != 4
            or "/".join(parsed.parts[:-1]) != GOLDEN_ROOT):
        return "unknown"
    return family_from_name(parsed.name)


def compare_declarations(actual, declared):
    undeclared = actual - declared
    stale = declared - actual
    if not undeclared and not stale:
        return
    parts = []
    if undeclared:
        parts.append(f"{len(undeclared)} undeclared addition(s)")
    if stale:
        parts.append(f"{len(stale)} stale declaration(s)")
    raise Rejection("intent mismatch: " + ", ".join(parts))


def compare_html_scope(files, selectors):
    changed = set(files)
    declared = {entry.file for entry in selectors}
    undeclared = changed - declared
    stale = declared - changed
    if not undeclared and not stale:
        return
    parts = []
    if undeclared:
        parts.append(f"{len(undeclared)} undeclared HTML file(s)")
    if stale:
        parts.append(f"{len(stale)} stale HTML selector file(s)")
    raise Rejection("intent mismatch: " + ", ".join(parts))


def supplied_intent(intent_path, event_path, base):
    if intent_path is not None:
        return load_intent(intent_path, base)
    return load_event_intent(event_path, base)


def classify(repo, base, intent_path=None, event_path=None):
    repo = Path(repo).resolve()
    require(repo.is_dir(), "repository path does not exist")
    require(FULL_SHA.fullmatch(base) is not None,
            "--base must be a full 40-character lowercase SHA")
    require(run_git(repo, "cat-file", "-e", f"{base}^{{commit}}").returncode == 0,
            "--base does not identify a fetched commit")
    require(run_git(repo, "merge-base", "--is-ancestor", base, "HEAD").returncode == 0,
            "--base must be an ancestor of HEAD")
    dirty = git_output(
        repo,
        "status",
        "--porcelain=v1",
        "-z",
        "--untracked-files=all",
        "--ignored=matching",
        "--",
        GOLDEN_ROOT,
    )
    require(not dirty, "golden state is dirty or untracked")
    require_unique_trusted_check_name(repo, base)

    files = changed_files(repo, base)
    families = {family(path) for path in files}
    require("unknown" not in families, "golden diff contains an unsupported file family")
    require(not ({"page", "json"} <= families),
            "golden diff must not mix JSON tool and HTML page families")
    intent = supplied_intent(intent_path, event_path, base)

    if not files:
        require(intent is None, "intent is stale because no golden files changed")
        return "golden diff: no golden changes"
    if families == {"page"}:
        require(intent is not None and intent.html_selectors and not intent.additions,
                "HTML page golden changes require exact external html_selectors intent, "
                "with one fenced PR-body JSON object in CI")
        compare_html_scope(files, intent.html_selectors)
        selectors_by_file = {entry.file: entry.selector
                             for entry in intent.html_selectors}
        for path in files:
            classify_html_file(repo, base, path, selectors_by_file[path])
        return (f"golden diff: {len(files)} HTML machine scope(s) verified; "
                "human diff review remains defense in depth")

    require(intent is not None and intent.additions and not intent.html_selectors,
            "JSON tool golden changes require exact external additions intent, "
            "with one fenced PR-body JSON object in CI")
    actual = set()
    for path in files:
        actual.update(classify_json_file(repo, base, path))
        require(len(actual) <= MAX_ADDITIONS, "golden diff has too many JSON additions")
    compare_declarations(actual, intent.additions)
    return f"golden diff: approved {len(actual)} declared JSON additions"


def main(arguments=None):
    parser = argparse.ArgumentParser(
        description="Classify golden changes against an externally supplied base and intent.")
    parser.add_argument("--base", required=True, help="trusted full base commit SHA")
    source = parser.add_mutually_exclusive_group()
    source.add_argument("--intent", help="external lex-golden-diff-intent/1 JSON file")
    source.add_argument("--event", help="GitHub event file containing the PR body intent")
    parser.add_argument("--repo", default=".", help=argparse.SUPPRESS)
    options = parser.parse_args(arguments)
    try:
        print(classify(options.repo, options.base, options.intent, options.event))
    except Rejection as error:
        print(f"golden diff rejected: {error}", file=sys.stderr)
        return 1
    except Exception:
        print("golden diff rejected: internal classifier failure", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
