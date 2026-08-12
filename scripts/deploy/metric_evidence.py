#!/usr/bin/env python3
import json
import math
import re
import sys


MAXIMUM_PAYLOAD_BYTES = 4 * 1024 * 1024
REVISION = re.compile(r"^[a-z0-9][a-z0-9-]{0,127}$")
TIMESTAMP = re.compile(r"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$")


class EvidenceFailure(ValueError):
    pass


def require(condition, message):
    if not condition:
        raise EvidenceFailure(message)


def revision_of(series):
    metadata = series.get("metadatavalues")
    if not isinstance(metadata, list):
        return None
    for item in metadata:
        if not isinstance(item, dict):
            continue
        name = item.get("name")
        value = name.get("value") if isinstance(name, dict) else None
        if isinstance(value, str) and value.lower() == "revisionname":
            return item.get("value")
    return None


def values_for(payload, metric_name, revision, start, required):
    metrics = payload.get("value")
    require(isinstance(metrics, list), "metric response has no value array")
    values = []
    for metric in metrics:
        if not isinstance(metric, dict):
            continue
        name = metric.get("name")
        if not isinstance(name, dict) or name.get("value") != metric_name:
            continue
        series_list = metric.get("timeseries")
        if not isinstance(series_list, list):
            continue
        for series in series_list:
            if not isinstance(series, dict) or revision_of(series) != revision:
                continue
            data = series.get("data")
            if not isinstance(data, list):
                continue
            for point in data:
                if not isinstance(point, dict):
                    continue
                timestamp = point.get("timeStamp")
                maximum = point.get("maximum")
                # The workflow asks Azure for a wider timespan than the measured
                # window, so the payload carries buckets that must not reach the
                # maximum. Bounds and buckets are all fixed-width UTC stamps, for
                # which lexicographic order is chronological order; anything in
                # another shape is not comparable and is dropped, which can only
                # fail the required-bucket check below, never pass it.
                if not isinstance(timestamp, str) or not TIMESTAMP.fullmatch(timestamp):
                    continue
                if not start <= timestamp <= required:
                    continue
                if isinstance(maximum, bool) or not isinstance(maximum, (int, float)):
                    continue
                if math.isfinite(maximum) and maximum >= 0:
                    values.append((timestamp, maximum))
    require(values, f"candidate metric series is missing for {metric_name}")
    required_values = [value for timestamp, value in values if timestamp == required]
    require(required_values, f"candidate {metric_name} metric does not cover the required load bucket")
    return math.floor(max(value for _, value in values)), math.floor(max(required_values))


def evidence(payload, revision, start, required):
    require(isinstance(payload, dict), "metric response must be an object")
    require(bool(TIMESTAMP.fullmatch(start)) and bool(TIMESTAMP.fullmatch(required))
            and start <= required, "measured window bounds are not a UTC range")
    memory_max, memory_required = values_for(
        payload, "WorkingSetBytes", revision, start, required)
    replicas_max, replicas_required = values_for(
        payload, "Replicas", revision, start, required)
    return {
        "revision": revision,
        "window_start": start,
        "required_timestamp": required,
        "memory_max": memory_max,
        "memory_required": memory_required,
        "replicas_max": replicas_max,
        "replicas_required": replicas_required,
    }


def main():
    if (len(sys.argv) != 4
            or not REVISION.fullmatch(sys.argv[1])
            or not TIMESTAMP.fullmatch(sys.argv[2])
            or not TIMESTAMP.fullmatch(sys.argv[3])
            or sys.argv[2] > sys.argv[3]):
        print("usage: metric_evidence.py REVISION WINDOW_START REQUIRED_TIMESTAMP",
              file=sys.stderr)
        return 2
    raw = sys.stdin.buffer.read(MAXIMUM_PAYLOAD_BYTES + 1)
    if not raw:
        print("metric response is empty", file=sys.stderr)
        return 2
    if len(raw) > MAXIMUM_PAYLOAD_BYTES:
        print("metric response exceeds its byte limit", file=sys.stderr)
        return 2
    try:
        payload = json.loads(raw)
        result = evidence(payload, sys.argv[1], sys.argv[2], sys.argv[3])
    except (UnicodeDecodeError, json.JSONDecodeError):
        print("metric response is malformed", file=sys.stderr)
        return 2
    except EvidenceFailure as error:
        print(str(error), file=sys.stderr)
        return 1
    print(json.dumps(result, separators=(",", ":"), sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
