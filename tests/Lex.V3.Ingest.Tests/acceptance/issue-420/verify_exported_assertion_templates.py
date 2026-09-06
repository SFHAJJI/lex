"""Execute exported product templates, not a reconstructed SPARQL traversal.

python artifacts/issue-420/verify_exported_assertion_templates.py
Requires sibling query-templates-{baseline,current}.json and sparql-semantic-deps.
All RDF is synthetic. No publisher request or performance claim is made.
"""
from pathlib import Path
import hashlib
import json
import re
import sys

ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT / "sparql-semantic-deps"))
import rdflib
from rdflib import Graph, URIRef, BNode, Literal
from rdflib.namespace import XSD


def load_export(name):
    raw = (ROOT / f"query-templates-{name}.json").read_bytes()
    rows = json.loads(raw.decode("utf-8-sig"))
    templates = {row["TemplateId"]: row for row in rows}
    assert len(templates) == len(rows), "Duplicate template ID"
    return templates, hashlib.sha256(raw).hexdigest()


baseline, baseline_sha256 = load_export("baseline")
current, current_sha256 = load_export("current")
ordered, ordered_sha256 = load_export("ordered")
# RDFLib executes SPARQL algebra, not Virtuoso's physical-plan directives.
# Verify that removing only the documented join-order directive recovers the
# exact prefilter templates executed below, including every other field.
directive = 'DEFINE sql:select-option "order"\n'
for template_id, template in ordered.items():
    normalized = dict(template)
    if template_id == "assertion-rows":
        for field in ("Utf8QueryTemplate", "Utf8CountTemplate"):
            assert normalized[field].startswith(directive)
            normalized[field] = normalized[field][len(directive):]
    assert normalized == current[template_id], template_id + ": directive normalization changed query algebra"
assert ordered.keys() == current.keys()
assert baseline.keys() == current.keys(), "Exported template IDs changed"
assert len(current) == 9, "Expected nine product templates"
identical = []
for template_id in baseline:
    if template_id == "assertion-rows":
        continue
    for field in ("Utf8QueryTemplate", "Utf8CountTemplate"):
        assert baseline[template_id][field].encode("utf-8") == current[template_id][field].encode("utf-8"), (
            f"Unrelated product template changed: {template_id}/{field}"
        )
    identical.append(template_id)
assert len(identical) == 8
assert baseline["assertion-rows"] != current["assertion-rows"], "Probe is comparing unchanged A templates"

# Use actual admitted publisher predicate IRIs. Values outside the template's own
# VALUES list must never become assertions merely because the subject scan saw them.
JOLUX = "http://data.legilux.public.lu/resource/ontology/jolux#"
P_A = URIRef(JOLUX + "basedOn")
P_Z = URIRef(JOLUX + "userFormat")
P_IGNORED = URIRef("urn:fixture:ignored-predicate")
for export in (baseline, current):
    for field in ("Utf8QueryTemplate", "Utf8CountTemplate"):
        predicates = re.search(r"VALUES \?predicate\s*\{([^}]+)\}", export["assertion-rows"][field]).group(1)
        assert f"<{P_A}>" in predicates and f"<{P_Z}>" in predicates
        assert f"<{P_IGNORED}>" not in predicates

SUBJECTS = [URIRef("urn:s:a"), URIRef("urn:s:b"), URIRef("urn:s:z"), URIRef("urn:s:é"), BNode("blank-subject")]
graph = Graph()
for i, subject in enumerate(SUBJECTS):
    graph.add((subject, P_A, Literal(f"literal-{i}")))
    graph.add((subject, P_A, URIRef(f"urn:object:{i}")))
    graph.add((subject, P_Z, Literal(f"français-{i}", lang="fr")))
    graph.add((subject, P_Z, Literal(i, datatype=XSD.integer)))
    graph.add((subject, P_Z, BNode(f"blank-object-{i}")))
    graph.add((subject, P_IGNORED, Literal("not an admitted assertion")))
graph.add((URIRef("urn:s:only-ignored"), P_IGNORED, Literal("ignored")))

PLACEHOLDER = re.compile(r"\{([a-z_]+[0-9]*):(sparql_string|uint)\}")


def render(export, start, end, cursor=None, limit=100_000, count=False):
    """The only non-mutant transformation is replacing typed placeholders."""
    args = {"pass_id": 1, "page_limit": limit, "has_cursor": int(cursor is not None)}
    for i in range(6):
        args[f"partition_start_{i + 1}"] = start[i]
        args[f"partition_end_{i + 1}"] = end[i]
        args[f"last_key_{i + 1}"] = cursor[i] if cursor is not None else ""

    def substitute(match):
        name, kind = match.groups()
        assert name in args, f"Unknown typed placeholder {name}:{kind}"
        value = args[name]
        if kind == "uint":
            assert isinstance(value, int) and value >= 0
            return str(value)
        assert isinstance(value, str)
        # JSON's short string escaping is valid SPARQL string literal syntax;
        # fixture boundaries include Unicode and contain no unpaired surrogates.
        return json.dumps(value, ensure_ascii=False)

    field = "Utf8CountTemplate" if count else "Utf8QueryTemplate"
    rendered = PLACEHOLDER.sub(substitute, export["assertion-rows"][field])
    assert PLACEHOLDER.search(rendered) is None
    return rendered


def replace_once(text, old, new):
    assert text.count(old) == 1, f"Mutation target not unique: {old}"
    return text.replace(old, new, 1)


def mutate(query, name):
    if name == "exclusive_first_component_end":
        return replace_once(query, "?subject_key <= ?partition_end_1", "?subject_key < ?partition_end_1")
    if name == "discard_blank_subjects":
        return replace_once(query, "?subject ?scan_predicate ?scan_object .",
                            "?subject ?scan_predicate ?scan_object . FILTER(isIRI(?subject))")
    projection = r"(\{\s*SELECT DISTINCT \?subject\s+)((?:\?partition_(?:start|end)_[1-6]\s+){12})(WHERE\s*\{)"
    if name == "omit_projected_bounds":
        result, substitutions = re.subn(projection, r"\1\3", query)
        assert substitutions == 1, "Inner projection mutation target not unique"
        return result
    if name == "outer_values_only":
        values_pattern = r"VALUES\s+\(\?partition_start_1\b.*?\)\s*\{\s*\(.*?\)\s*\}"
        matches = list(re.finditer(values_pattern, query, re.DOTALL))
        assert len(matches) == 1, "Bound VALUES mutation target not unique"
        match = matches[0]
        values_text = match.group()
        without = query[:match.start()] + query[match.end():]
        result, substitutions = re.subn(projection, lambda m: values_text + "\n" + m.group(), without)
        assert substitutions == 1, "Outer VALUES insertion target not unique"
        return result
    raise AssertionError("Unknown mutation " + name)


executions = 0


def execute(query):
    global executions
    executions += 1
    return [tuple(row) for row in graph.query(query)]


def same(left, right):
    canonical = lambda rows: sorted(tuple(term.n3() for term in row) for row in rows)
    return canonical(left) == canonical(right)


def key(first, *tail):
    return (first,) + tuple(str(value) for value in tail) + ("",) * (5 - len(tail))


cases = [
    ("whole_graph", key(""), key("\uffff")),
    ("bounded_iris", key("urn:s:a"), key("urn:s:z")),
    ("equal_first_component_end_tail", key("urn:s:b"), key("urn:s:b", P_Z)),
    ("same_subject_tail_slice", key("urn:s:b", P_A, "literal"), key("urn:s:b", P_Z, "literal")),
    ("blank_subject_only", key(""), key("", P_Z)),
    ("blank_subject_lower_tail", key("", P_Z, "literal"), key("urn:s:a")),
    ("unicode_subject", key("urn:s:é"), key("urn:s:é", str(P_Z) + "z")),
    ("unsupported_object_slice", key("urn:s:a", P_Z, "unsupported_blank_node"), key("urn:s:b")),
    ("no_matching_subject", key("urn:s:c"), key("urn:s:d")),
]
results = []
comparisons = 0
for name, start, end in cases:
    original = execute(render(baseline, start, end))
    rewritten = execute(render(current, start, end))
    assert same(original, rewritten), name + ": selected rows differ"
    comparisons += 1
    old_count = execute(render(baseline, start, end, count=True))
    new_count = execute(render(current, start, end, count=True))
    assert old_count == new_count, name + ": counts differ"
    assert int(old_count[0][0]) == len(original), name + ": count/rows disagree"
    comparisons += 1
    if original:
        cursor = tuple(str(term) for term in original[len(original) // 2][6:])
        for limit in (1, 3):
            before = execute(render(baseline, start, end, cursor=cursor, limit=limit))
            after = execute(render(current, start, end, cursor=cursor, limit=limit))
            assert same(before, after), name + ": cursor page differs"
            comparisons += 1
    results.append({"case": name, "rows": len(original)})

killed = []
for mutation, (_, start, end) in [
    ("exclusive_first_component_end", cases[2]),
    ("discard_blank_subjects", cases[4]),
    ("omit_projected_bounds", cases[1]),
    ("outer_values_only", cases[1]),
]:
    expected = execute(render(baseline, start, end))
    wrong = execute(mutate(render(current, start, end), mutation))
    assert expected and not same(expected, wrong), f"Mutation survived in page query: {mutation}"
    expected_count = execute(render(baseline, start, end, count=True))
    wrong_count = execute(mutate(render(current, start, end, count=True), mutation))
    assert expected_count != wrong_count, f"Mutation survived in count query: {mutation}"
    killed.append({"mutation": mutation, "expected_rows": len(expected), "mutant_rows": len(wrong),
                   "expected_count": int(expected_count[0][0]), "mutant_count": int(wrong_count[0][0])})

print(json.dumps({
    "scope": "actual exported product templates over synthetic RDF; no publisher/performance claim",
    "rdflib": rdflib.__version__, "graph_triples": len(graph),
    "baseline_export_sha256": baseline_sha256, "current_export_sha256": current_sha256,
    "ordered_export_sha256": ordered_sha256,
    "physical_directive_checked_separately": directive.strip(),
    "other_templates_byte_identical": sorted(identical),
    "equivalence_comparisons": comparisons, "query_executions": executions,
    "cases": results, "mutations_killed_in_page_and_count": killed,
}, indent=2, ensure_ascii=False))
