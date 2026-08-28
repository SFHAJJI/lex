#!/usr/bin/env python3
import json
import math
from pathlib import Path
import re
import sys


MAXIMUM_INPUT_BYTES = 256 * 1024
RESOURCE_NAME = re.compile(r"^[A-Za-z0-9_.()-]{1,128}$")
API_VERSIONS = {
    "managed_environment": "2025-07-01",
    "managed_environment_telemetry": "2024-10-02-preview",
    "container_app": "2025-07-01",
    "diagnostic_settings": "2021-05-01-preview",
    "diagnostic_settings_categories": "2021-05-01-preview",
    "application_insights": "2020-02-02",
    "log_analytics_workspace": "2025-07-01",
}


class PolicyFailure(ValueError):
    pass


def require(condition, message):
    if not condition:
        raise PolicyFailure(message)


def exact_object(value, keys, label):
    require(isinstance(value, dict), f"{label} shape")
    require(set(value) == set(keys), f"{label} fields")
    return value


def resource_name(value, label):
    require(isinstance(value, str) and RESOURCE_NAME.fullmatch(value), f"{label} name")
    return value


def zero(value, label):
    require(type(value) is int and value == 0, f"{label} diagnostic settings")


def diagnostic_shape(value, label):
    require(value == "array", f"{label} diagnostic settings shape")


def signal_names(value, label):
    require(isinstance(value, list), f"{label} shape")
    require(all(isinstance(item, str) and RESOURCE_NAME.fullmatch(item) for item in value),
            f"{label} names")
    require(len(value) == len(set(value)), f"{label} duplicates")
    return value


def strict_object(pairs):
    value = {}
    for key, item in pairs:
        if key in value:
            raise PolicyFailure("input malformed")
        value[key] = item
    return value


def reject_nonfinite(_value):
    raise PolicyFailure("input malformed")


def reject_nested_nonfinite(value):
    if isinstance(value, float) and not math.isfinite(value):
        raise PolicyFailure("input malformed")
    if isinstance(value, dict):
        for item in value.values():
            reject_nested_nonfinite(item)
    elif isinstance(value, list):
        for item in value:
            reject_nested_nonfinite(item)


def load(path):
    try:
        with Path(path).open("rb") as source:
            raw = source.read(MAXIMUM_INPUT_BYTES + 1)
    except OSError as error:
        raise PolicyFailure("input unavailable") from error
    require(0 < len(raw) <= MAXIMUM_INPUT_BYTES, "input size")
    try:
        value = json.loads(
            raw,
            object_pairs_hook=strict_object,
            parse_constant=reject_nonfinite,
        )
        reject_nested_nonfinite(value)
        return value
    except (UnicodeDecodeError, json.JSONDecodeError) as error:
        raise PolicyFailure("input malformed") from error


def validate_policy(policy):
    exact_object(policy,
        ["schema", "api_versions", "environment", "container_app",
         "application_insights", "workspaces"], "policy")
    require(policy["schema"] == "lex-telemetry-policy/1", "policy schema")
    versions = exact_object(policy["api_versions"], API_VERSIONS, "API versions")
    require(versions == API_VERSIONS, "API versions")

    environment = exact_object(policy["environment"],
        ["resource_group_name", "name", "log_destination", "workspace_linked",
         "diagnostic_setting_count", "required_diagnostic_categories",
         "dapr_application_insights_enabled", "managed_open_telemetry"],
        "environment policy")
    resource_name(environment["resource_group_name"], "environment resource group")
    resource_name(environment["name"], "environment")
    require(environment["log_destination"] == "log-analytics",
            "environment policy destination")
    require(environment["workspace_linked"] is True, "environment workspace")
    zero(environment["diagnostic_setting_count"], "environment policy")
    categories = environment["required_diagnostic_categories"]
    require(categories == ["ContainerAppConsoleLogs", "ContainerAppHTTPLogs",
                           "ContainerAppSystemLogs"], "environment policy categories")
    require(environment["dapr_application_insights_enabled"] is False,
            "environment Dapr Application Insights")
    validate_managed_open_telemetry(environment["managed_open_telemetry"],
                                    "environment policy")

    container_app = exact_object(policy["container_app"],
        ["resource_group_name", "name", "diagnostic_setting_count", "dapr_enabled",
         "dapr_api_logging_enabled"], "container app policy")
    resource_name(container_app["resource_group_name"], "container app resource group")
    resource_name(container_app["name"], "container app")
    zero(container_app["diagnostic_setting_count"], "container app policy")
    require(container_app["dapr_enabled"] is False, "container app Dapr")
    require(container_app["dapr_api_logging_enabled"] is False,
            "container app Dapr API logging")

    insights = exact_object(policy["application_insights"],
        ["resource_group_name", "name", "workspace_name", "workspace_linked",
         "ip_masking_enabled", "diagnostic_setting_count"],
        "application insights policy")
    resource_name(insights["resource_group_name"], "application insights resource group")
    resource_name(insights["name"], "application insights")
    resource_name(insights["workspace_name"], "application insights workspace")
    require(insights["workspace_linked"] is True, "application insights workspace")
    require(insights["ip_masking_enabled"] is True,
            "application insights IP masking")
    zero(insights["diagnostic_setting_count"], "application insights policy")
    validate_workspaces(policy["workspaces"], "workspace policy")
    require(insights["workspace_name"] == policy["workspaces"][1]["name"],
            "application insights workspace policy")


def validate_managed_open_telemetry(value, label, readback=False):
    keys = ["app_insights_destination_enabled", "data_dog_destination_enabled",
            "otlp_destination_count", "trace_destinations", "log_destinations",
            "metric_destinations"]
    if readback:
        keys.append("otlp_destinations_type")
    item = exact_object(value, keys, f"{label} managed OpenTelemetry")
    require(item["app_insights_destination_enabled"] is False,
            f"{label} managed Application Insights destination")
    require(item["data_dog_destination_enabled"] is False,
            f"{label} managed Data Dog destination")
    require(type(item["otlp_destination_count"]) is int
            and item["otlp_destination_count"] == 0,
            f"{label} managed OTLP destination count")
    if readback:
        require(item["otlp_destinations_type"] in {"array", "null"},
                f"{label} managed OTLP destination shape")
    for signal in ("trace", "log", "metric"):
        names = signal_names(item[f"{signal}_destinations"],
                             f"{label} managed {signal} destinations")
        require(names == [], f"{label} managed {signal} destinations")


def validate_workspaces(workspaces, label):
    require(isinstance(workspaces, list) and len(workspaces) == 2, f"{label} set")
    expected_purposes = ["container_apps", "application_insights"]
    for index, (workspace, purpose) in enumerate(zip(workspaces, expected_purposes)):
        item = exact_object(workspace,
            ["purpose", "resource_group_name", "name", "resource_only_permissions"],
            f"{label} {index}")
        require(item["purpose"] == purpose, f"{label} purpose")
        resource_name(item["resource_group_name"], f"{label} resource group")
        resource_name(item["name"], f"{label} workspace")
        require(item["resource_only_permissions"] is True,
                f"{label} resource-only permissions")


def verify(policy, readback):
    validate_policy(policy)
    exact_object(readback,
        ["schema", "environment", "container_app", "application_insights", "workspaces"],
        "readback")
    require(readback["schema"] == "lex-telemetry-readback/1", "readback schema")

    expected = policy["environment"]
    actual = exact_object(readback["environment"],
        ["resource_group_name", "name", "log_destination", "workspace_linked",
         "diagnostic_setting_count", "diagnostic_settings_type",
         "diagnostic_categories", "diagnostic_categories_type",
         "dapr_application_insights_enabled", "managed_open_telemetry"],
        "environment readback")
    require(actual["resource_group_name"] == expected["resource_group_name"]
            and actual["name"] == expected["name"], "environment identity")
    require(actual["log_destination"] == expected["log_destination"],
            "environment destination")
    require(actual["workspace_linked"] is expected["workspace_linked"],
            "environment workspace")
    zero(actual["diagnostic_setting_count"], "environment readback")
    diagnostic_shape(actual["diagnostic_settings_type"], "environment readback")
    diagnostic_shape(actual["diagnostic_categories_type"],
                     "environment diagnostic categories")
    categories = actual["diagnostic_categories"]
    require(isinstance(categories, list)
            and all(isinstance(item, str) for item in categories)
            and len(categories) == len(set(categories)), "environment diagnostic categories")
    require(set(expected["required_diagnostic_categories"]).issubset(categories),
            "environment required diagnostic categories")
    require(actual["dapr_application_insights_enabled"]
            is expected["dapr_application_insights_enabled"],
            "environment Dapr Application Insights")
    validate_managed_open_telemetry(actual["managed_open_telemetry"],
                                    "environment readback", readback=True)

    expected = policy["container_app"]
    actual = exact_object(readback["container_app"],
        ["resource_group_name", "name", "diagnostic_setting_count",
         "diagnostic_settings_type", "dapr_enabled", "dapr_api_logging_enabled"],
        "container app readback")
    require(actual["resource_group_name"] == expected["resource_group_name"]
            and actual["name"] == expected["name"], "container app identity")
    zero(actual["diagnostic_setting_count"], "container app readback")
    diagnostic_shape(actual["diagnostic_settings_type"], "container app readback")
    require(actual["dapr_enabled"] is expected["dapr_enabled"], "container app Dapr")
    require(actual["dapr_api_logging_enabled"] is expected["dapr_api_logging_enabled"],
            "container app Dapr API logging")

    expected = policy["application_insights"]
    actual = exact_object(readback["application_insights"],
        ["resource_group_name", "name", "workspace_name", "workspace_linked",
         "ip_masking_enabled", "diagnostic_setting_count", "diagnostic_settings_type"],
        "application insights readback")
    require(actual["resource_group_name"] == expected["resource_group_name"]
            and actual["name"] == expected["name"], "application insights identity")
    require(actual["workspace_name"] == expected["workspace_name"]
            and actual["workspace_linked"] is expected["workspace_linked"],
            "application insights workspace")
    require(actual["ip_masking_enabled"] is expected["ip_masking_enabled"],
            "application insights IP masking")
    zero(actual["diagnostic_setting_count"], "application insights readback")
    diagnostic_shape(actual["diagnostic_settings_type"], "application insights readback")

    validate_workspaces(readback["workspaces"], "workspace readback")
    for expected, actual in zip(policy["workspaces"], readback["workspaces"]):
        require(actual["name"] == expected["name"], "workspace identity")
        require(actual["resource_group_name"] == expected["resource_group_name"],
                "workspace resource group")


def main():
    if len(sys.argv) != 3:
        print("usage: telemetry_policy.py POLICY READBACK", file=sys.stderr)
        return 2
    try:
        policy = load(sys.argv[1])
        readback = load(sys.argv[2])
        verify(policy, readback)
    except PolicyFailure as error:
        message = str(error)
        if message in {"input unavailable", "input size", "input malformed"}:
            print(f"telemetry policy input is {message.removeprefix('input ')}", file=sys.stderr)
            return 2
        print(f"telemetry policy mismatch: {message}", file=sys.stderr)
        return 1
    print("telemetry policy matched")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
