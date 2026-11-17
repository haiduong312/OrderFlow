#!/usr/bin/env python3
"""Publish structural test facts, never arbitrary assertion values, stdout, cookies or attachments."""
import argparse
import json
import re
import shutil
import xml.etree.ElementTree as ET
from pathlib import Path

NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
REQUIRED_BROWSER = {
    "registration_validation", "session_restore", "catalog_filter_pages", "cart_quantities_restore",
    "flow_registered_purchase", "flow_lost_create", "flow_happy_payment", "flow_payment_timeout",
    "admin_order_lifecycle_reports", "account_switch_isolation", "flow_checkout_account_isolation",
    "flow_realtime_reconnect",
}
BROWSER_SCENARIOS = REQUIRED_BROWSER | {
    "credentials_csrf", "missing_expired_auth", "cross_tab_isolation", "loading_empty_error",
    "unavailable_restore", "responsive_navigation", "catalog_categories_detail", "cart_price_change",
    "cart_archived_unavailable", "cart_account_scope", "cart_cross_tab", "flow_expired_order",
    "flow_late_refund", "flow_cancel_notifications", "flow_refund_review", "admin_catalog_management",
    "admin_stock_recovery", "admin_permissions_conflicts", "admin_email_retry", "admin_empty_error_views",
}


def identifier(value):
    value = value.split(",", 1)[0]
    return value if re.fullmatch(r"[A-Za-z_][A-Za-z0-9_.+]*", value) else "unavailable"


def locations(text):
    # Keep repository source file/line, exclude method arguments and all error message values.
    return sorted(set(re.findall(r"(?:tests|src|client)/(?:[A-Za-z0-9_.-]+/)*[A-Za-z0-9_.-]+\.(?:cs|tsx?|mjs):(?:line )?\d+", text.replace("\\", "/"))))[:8]


def read_xml(path):
    if path.stat().st_size > 20 * 1024 * 1024:
        raise ValueError("Test report exceeds the 20 MiB input limit")
    return ET.parse(path).getroot()


def trx(path):
    root = read_xml(path)
    definitions = {
        test.attrib["id"]: test.find("t:TestMethod", NS)
        for test in root.findall("t:TestDefinitions/t:UnitTest", NS)
    }
    results, scenarios = [], set()
    for result in root.findall("t:Results/t:UnitTestResult", NS):
        display = result.attrib.get("testName", "")
        known_scenarios = sorted(s for s in BROWSER_SCENARIOS if f'"{s}"' in display)
        method = definitions.get(result.attrib.get("testId"))
        name = "unavailable" if method is None else identifier(method.attrib.get("className", "")) + "." + identifier(method.attrib.get("name", ""))
        outcome = result.attrib.get("outcome", "Failed")
        outcome = "passed" if outcome == "Passed" else "skipped" if outcome == "NotExecuted" else "failed"
        duration = result.attrib.get("duration", "")
        results.append({"test": name, "outcome": outcome,
                        "scenario": known_scenarios[0] if len(known_scenarios) == 1 else None,
                        "duration": duration if re.fullmatch(r"[0-9:.]+", duration) else "unavailable",
                        "locations": locations(result.findtext("t:Output/t:ErrorInfo/t:StackTrace", "", NS))})
        # Names are used for validation only. They can contain arbitrary parameter values: never export them.
        scenarios.update(s for s in REQUIRED_BROWSER if f'"{s}"' in display)
    return results, scenarios


def junit(path):
    root = read_xml(path)
    results = []
    for index, case in enumerate(root.iter("testcase"), 1):
        name = case.attrib.get("classname", "")
        filename = re.search(r"(?:tests/)?[A-Za-z0-9_.-]+\.test\.ts", name)
        failure = case.find("failure")
        results.append({"test": (filename.group() if filename else "frontend") + f"#{index}",
                        "outcome": "failed" if failure is not None or case.find("error") is not None else "skipped" if case.find("skipped") is not None else "passed",
                        "locations": locations("" if failure is None else failure.text or "")})
    return results, set()


def publish(raw, public, suite, format):
    public.mkdir(parents=True, exist_ok=True)
    (public / "summary.json").unlink(missing_ok=True)
    for old in public.glob("masked-failure-*.png"):
        old.unlink()  # Only this helper's generated files; do not retain screenshots from a prior local run.
    results, scenarios = [], set()
    files = sorted(raw.glob("*.trx" if format == "trx" else "*.xml"))
    for path in files:
        rows, covered = trx(path) if format == "trx" else junit(path)
        results.extend(rows)
        scenarios.update(covered)
    minimum = 32 if suite == "browser" else 1
    counts = {outcome: sum(r["outcome"] == outcome for r in results) for outcome in ("passed", "failed", "skipped")}
    missing = sorted(REQUIRED_BROWSER - scenarios) if suite == "browser" else []
    valid = bool(files) and len(results) >= minimum and counts["failed"] == counts["skipped"] == 0 and not missing
    summary = {"suite": suite, "valid": valid, "total": len(results), **counts,
               "missing_required_scenarios": missing, "tests": results,
               "privacy": "Assertion messages, display parameters, stdout, environment, raw XML/HTML, network traces and arbitrary attachments omitted."}
    images = []
    # Only the privacy fixture's explicitly masked screenshots, never automatic/raw attachments.
    for index, path in enumerate(sorted(raw.glob("browser/**/safe-failure-*.png"))[:16], 1):
        if path.stat().st_size <= 2 * 1024 * 1024 and path.read_bytes().startswith(b"\x89PNG\r\n\x1a\n"):
            shutil.copyfile(path, public / f"masked-failure-{index}.png")
            images.append({"file": f"masked-failure-{index}.png",
                           "scenario": next((part for part in path.parts if part in BROWSER_SCENARIOS), None)})
    summary["images"] = images
    (public / "summary.json").write_text(json.dumps(summary, indent=2) + "\n")
    print(f'{suite}: {len(results)} tests; passed={counts["passed"]}, failed={counts["failed"]}, skipped={counts["skipped"]}; report valid={valid}')
    for result in results:
        if result["outcome"] != "passed":
            print(result["outcome"], result["test"], ", ".join(result["locations"]))
    if missing:
        print("Missing critical browser scenarios:", ", ".join(missing))
    return valid


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--raw", required=True, type=Path)
    parser.add_argument("--public", required=True, type=Path)
    parser.add_argument("--suite", required=True, choices=["unit", "integration", "browser", "frontend"])
    parser.add_argument("--format", required=True, choices=["trx", "junit"])
    args = parser.parse_args()
    try:
        valid = publish(args.raw, args.public, args.suite, args.format)
    except (ET.ParseError, ValueError, OSError):
        # Malformed input might itself contain secrets: do not format that exception.
        print("Invalid or unavailable test report; no raw diagnostic content published.")
        args.public.mkdir(parents=True, exist_ok=True)
        (args.public / "summary.json").write_text(json.dumps({"suite": args.suite, "valid": False,
                                                            "error": "Invalid or unavailable test report"}) + "\n")
        valid = False
    raise SystemExit(0 if valid else 1)
