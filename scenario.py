"""Runs test scenarios against the AI Extension API and writes a report.

    python scenario.py run examples/title_to_creator.json
    python scenario.py run my_test.yaml --read-only --var card=foo.png

A scenario is JSON (or YAML when PyYAML is installed). See "Test scenarios" in AI_API_SPEC.md.
"""

import argparse
import base64
import json
import re
import sys
import time
from datetime import datetime
from pathlib import Path

import ai_api

HERE = Path(__file__).resolve().parent
REPORTS = HERE / "reports"
ACTIONS = ("call", "assert", "click", "wait", "capture", "sleep", "dev_cycle", "log_check", "snapshot", "diff")


class StepFailure(Exception):
    pass


# ---- values ----

def load(path):
    text = Path(path).read_text(encoding="utf-8")
    if Path(path).suffix.lower() in (".yaml", ".yml"):
        try:
            import yaml
        except ImportError:
            raise SystemExit("YAML scenarios need PyYAML (python -m pip install pyyaml); JSON works without it")
        return yaml.safe_load(text)
    return json.loads(text)


def substitute(value, variables):
    """Replaces ${name} in strings; a string that is only ${name} takes the variable's own type."""
    if isinstance(value, str):
        whole = re.fullmatch(r"\$\{([A-Za-z0-9_.-]+)\}", value)
        if whole:
            if whole.group(1) not in variables:
                raise StepFailure(f"variable {whole.group(1)} is not set")
            return variables[whole.group(1)]

        def one(m):
            if m.group(1) not in variables:
                raise StepFailure(f"variable {m.group(1)} is not set")
            v = variables[m.group(1)]
            return v if isinstance(v, str) else json.dumps(v, ensure_ascii=False)
        return re.sub(r"\$\{([A-Za-z0-9_.-]+)\}", one, value)
    if isinstance(value, list):
        return [substitute(v, variables) for v in value]
    if isinstance(value, dict):
        return {k: substitute(v, variables) for k, v in value.items()}
    return value


_MISSING = object()


def same(value, text):
    """A filter value written as text against a JSON value: true/false and numbers compare by their text."""
    if isinstance(value, bool):
        return str(value).lower() == text.lower()
    return str(value) == text


def select(document, path):
    """Small JSONPath: $.a.b, [0], [-1], [*] (every element) and [?key=value] (first element whose key equals value).

    Returns _MISSING when the path does not exist; [*] returns a list.
    """
    if not path.startswith("$"):
        raise StepFailure(f"path must start with $: {path}")
    tokens = re.findall(r"\.([^.\[\]]+)|\[([^\]]*)\]", path[1:])
    current = [document]
    many = False
    for key, bracket in tokens:
        nxt = []
        for node in current:
            if key:
                if isinstance(node, dict) and key in node:
                    nxt.append(node[key])
            elif bracket == "*":
                many = True
                if isinstance(node, list):
                    nxt.extend(node)
                elif isinstance(node, dict):
                    nxt.extend(node.values())
            elif bracket.startswith("?"):
                k, _, want = bracket[1:].partition("=")
                want = want.lstrip("=").strip("'\"")
                for item in node if isinstance(node, list) else []:
                    if isinstance(item, dict) and k in item and same(item[k], want):
                        nxt.append(item)
                        break
            else:
                try:
                    index = int(bracket)
                except ValueError:
                    raise StepFailure(f"bad index [{bracket}] in {path}")
                if isinstance(node, list) and -len(node) <= index < len(node):
                    nxt.append(node[index])
        current = nxt
    if many:
        return current
    return current[0] if current else _MISSING


OPERATORS = ("equals", "notEquals", "contains", "notContains", "in", "exists", "matches", "gt", "gte", "lt", "lte", "length")


def check(document, spec):
    """One assertion: {"path": "$.x", "<operator>": value}. Returns None or a failure message."""
    path = spec.get("path", "$")
    actual = select(document, path)
    ops = [op for op in OPERATORS if op in spec]
    if not ops:
        raise StepFailure(f"check on {path} needs one of {', '.join(OPERATORS)}")
    shown = "missing" if actual is _MISSING else json.dumps(actual, ensure_ascii=False)[:300]
    for op in ops:
        want = spec[op]
        if op == "exists":
            ok = (actual is not _MISSING) == bool(want)
        elif actual is _MISSING:
            ok = False
        elif op == "equals":
            ok = actual == want
        elif op == "notEquals":
            ok = actual != want
        elif op in ("contains", "notContains"):
            has = want in actual if isinstance(actual, (list, str)) else isinstance(actual, dict) and want in actual
            ok = has if op == "contains" else not has
        elif op == "in":
            ok = actual in want
        elif op == "matches":
            ok = isinstance(actual, str) and re.search(want, actual) is not None
        elif op == "length":
            ok = hasattr(actual, "__len__") and len(actual) == want
        else:
            try:
                ok = {"gt": actual > want, "gte": actual >= want, "lt": actual < want, "lte": actual <= want}[op]
            except TypeError:
                ok = False
        if not ok:
            return f"{path} {op} {json.dumps(want, ensure_ascii=False)} failed; actual {shown}"
    return None


# ---- running ----

class Runner:
    def __init__(self, scenario, port=38427, report_dir=None, read_only=False, variables=None):
        if not isinstance(scenario, dict) or not isinstance(scenario.get("steps"), list):
            raise SystemExit('a scenario is an object with "steps": [...]')
        self.scenario = scenario
        self.port = port
        self.read_only = read_only
        self.variables = {**scenario.get("vars", {}), **(variables or {})}
        name = re.sub(r"[^A-Za-z0-9._-]+", "-", scenario.get("name", "scenario"))[:60]
        self.dir = Path(report_dir) if report_dir else REPORTS / f"{name}-{datetime.now():%Y%m%d-%H%M%S}"
        self.dir.mkdir(parents=True, exist_ok=True)
        self.endpoints = None
        self.log_cursor = 0
        self.event_cursor = 0

    def request(self, method, path, body=None, timeout=60):
        path = path.lstrip("/")
        if self.read_only:
            route = path.split("?")[0]
            if self.endpoints is None:
                status, schema = ai_api.request(self.port, "/api/v1/schema")
                if status != 200:
                    raise StepFailure("schema unavailable for --read-only")
                self.endpoints = {(e["method"], e["path"]): e for e in schema["details"]}
            endpoint = self.endpoints.get((method, route))
            if endpoint is None or endpoint.get("mutates") and route != "debug/wait":
                raise StepFailure(f"{method} {route} changes state; refused by --read-only")
        try:
            return ai_api.request(self.port, "/api/v1/" + path, body if method == "POST" else None, timeout=timeout)
        except OSError as exc:
            raise StepFailure(f"{method} {path}: game API unreachable ({exc})")

    def cursors(self):
        try:
            status, log = ai_api.request(self.port, "/api/v1/debug/log?limit=1&since=999999999999", timeout=5)
            self.log_cursor = log.get("next", 0) if status == 200 else 0
            status, events = ai_api.request(self.port, "/api/v1/debug/events?limit=1&since=999999999999", timeout=5)
            self.event_cursor = events.get("next", 0) if status == 200 else 0
        except OSError:
            pass

    def save_png(self, b64, name):
        file = self.dir / (name if name.endswith(".png") else name + ".png")
        file.write_bytes(base64.b64decode(b64))
        return file.name

    def expect(self, status, body, step):
        expected = step.get("status", 200)
        if status not in (expected if isinstance(expected, list) else [expected]):
            raise StepFailure(f"status {status}, expected {expected}: {json.dumps(body, ensure_ascii=False)[:500]}")
        failures = [f for f in (check(body, c) for c in step.get("checks", [])) if f]
        if failures:
            raise StepFailure("; ".join(failures))
        for name, path in step.get("save", {}).items():
            value = select(body, path)
            if value is _MISSING:
                raise StepFailure(f"save {name}: {path} not found")
            self.variables[name] = value

    def run_step(self, step, record):
        kinds = [k for k in ACTIONS if k in step]
        if len(kinds) != 1:
            raise StepFailure(f"a step needs exactly one of {', '.join(ACTIONS)}; got {kinds or 'none'}")
        kind = kinds[0]
        record["kind"] = kind
        step = substitute(step, self.variables)
        value = step[kind]
        if kind in ("call", "assert"):
            method, _, path = value.partition(" ") if " " in value else ("GET", "", value)
            if kind == "assert" and method != "GET":
                raise StepFailure("assert reads with GET; use call for POST")
            status, body = self.request(method.upper(), path, step.get("body", {}))
            record["request"] = f"{method.upper()} {path}"
            record["response"] = excerpt(body)
            self.expect(status, body, step)
        elif kind == "click":
            status, body = self.request("POST", "ui/click", value)
            record["request"] = "POST ui/click " + json.dumps(value, ensure_ascii=False)
            record["response"] = excerpt(body)
            self.expect(status, body, step)
        elif kind == "wait":
            timeout = float(step.get("timeout", 10))
            status, body = self.request("POST", "debug/wait", {**value, "timeoutMs": int(timeout * 1000)}, timeout=timeout + 15)
            record["response"] = excerpt(body)
            if status != 200:
                raise StepFailure(f"not satisfied within {timeout}s; state {json.dumps(body.get('state'), ensure_ascii=False)}")
            self.expect(status, body, {k: v for k, v in step.items() if k in ("checks", "save")})
        elif kind == "capture":
            spec = value if isinstance(value, dict) else {"file": value}
            file = spec.get("file") or f"step{record['index']:02d}"
            if spec.get("region"):
                payload = {k: spec[k] for k in ("region", "view", "expression", "pose") if k in spec}
                status, body = self.request("POST", "capture", payload)
                if status != 200:
                    raise StepFailure(f"capture failed: {status} {excerpt(body)}")
                record["files"] = [self.save_png(body["image"]["pngBase64"], file)]
            else:
                status, body = self.request("GET", "screenshot")
                if status != 200:
                    raise StepFailure(f"screenshot failed: {status} {excerpt(body)}")
                record["files"] = [self.save_png(body["pngBase64"], file)]
        elif kind == "sleep":
            time.sleep(float(value))
        elif kind == "dev_cycle":
            spec = value if isinstance(value, dict) else {}
            if self.read_only:
                raise StepFailure("dev_cycle restarts the game; refused by --read-only")
            try:
                report = ai_api.dev_cycle(spec.get("projects", ["AiExtension"]), spec.get("build", True), True,
                                          spec.get("readyScene", "Title"), self.port)
            except RuntimeError as exc:
                raise StepFailure(str(exc))
            record["response"] = {"plugins": report["plugins"], "sceneReached": report["sceneReached"],
                                  "startupProblems": len(report["startupProblems"]), "elapsedSeconds": report["elapsedSeconds"]}
            (self.dir / f"step{record['index']:02d}-dev-cycle.json").write_text(json.dumps(report, ensure_ascii=False, indent=1), encoding="utf-8")
            record["files"] = [f"step{record['index']:02d}-dev-cycle.json"]
            self.cursors()
            if not report["sceneReached"]:
                raise StepFailure(f"the game did not reach {spec.get('readyScene', 'Title')} within 120 s")
            if not report["ok"]:
                raise StepFailure("a plugin did not load: " + json.dumps(report["plugins"]))
        elif kind == "log_check":
            spec = value if isinstance(value, dict) else {}
            from urllib.parse import urlencode
            query = {"since": self.log_cursor, "limit": 1000, "level": spec.get("level", "error")}
            for k in ("source", "contains"):
                if spec.get(k):
                    query[k] = spec[k]
            status, body = self.request("GET", "debug/log?" + urlencode(query))
            if status != 200:
                raise StepFailure(f"debug/log failed: {status}")
            lines = body["entries"]
            record["response"] = {"lines": [f"[{e['level']}:{e['source']}] {e['message'][:300]}" for e in lines[:20]], "count": len(lines)}
            if len(lines) > int(spec.get("max", 0)):
                raise StepFailure(f"{len(lines)} {query['level']} line(s) since the scenario started (max {spec.get('max', 0)})")
        elif kind == "snapshot":
            status, body = self.request("POST", "debug/snapshot", value)
            record["response"] = excerpt(body)
            self.expect(status, body, step)
        elif kind == "diff":
            from urllib.parse import urlencode
            spec = value if isinstance(value, dict) else {"from": value}
            status, body = self.request("GET", "debug/diff?" + urlencode({k: v for k, v in spec.items() if k in ("from", "to", "contains", "limit")}))
            file = f"step{record['index']:02d}-diff-{spec['from']}.json"
            (self.dir / file).write_text(json.dumps(body, ensure_ascii=False, indent=1), encoding="utf-8")
            record["files"] = [file]
            record["response"] = {"counts": body.get("counts")}
            self.expect(status, body, step)

    def run_list(self, steps, phase, records, start):
        ok = True
        for i, step in enumerate(steps):
            record = {"index": start + i, "phase": phase, "name": step.get("name", ""), "status": "pass"}
            began = time.time()
            if not ok and phase == "steps":
                record["status"] = "skipped"
                record["kind"] = next((k for k in ACTIONS if k in step), "?")
                records.append(record)
                print(f"  skipped {record['index']:>2} {record['kind']:9} {record['name']}", file=sys.stderr)
                continue
            try:
                self.run_step(step, record)
            except StepFailure as exc:
                record["status"] = "fail"
                record["error"] = str(exc)
                if not step.get("continueOnFailure"):
                    ok = False
            record["durationMs"] = round((time.time() - began) * 1000)
            records.append(record)
            print(f"  {record['status']:7} {record['index']:>2} {record.get('kind', '?'):9} {record['name'] or record.get('request', '')}"
                  + (f"\n          {record['error']}" if record.get("error") else ""), file=sys.stderr)
        return ok

    def run(self):
        started = time.time()
        self.cursors()
        start_log, start_event = self.log_cursor, self.event_cursor
        records = []
        print(f"scenario {self.scenario.get('name', '')}", file=sys.stderr)
        passed = self.run_list(self.scenario["steps"], "steps", records, 1)
        if not passed:
            try:
                status, shot = ai_api.request(self.port, "/api/v1/screenshot", timeout=15)
                if status == 200:
                    self.save_png(shot["pngBase64"], "failure")
            except OSError:
                pass
        cleanup = self.run_list(self.scenario.get("finally", []), "finally", records, len(records) + 1)
        report = {
            "scenario": self.scenario.get("name", ""), "description": self.scenario.get("description", ""),
            "passed": passed and cleanup, "stepsPassed": passed, "finallyPassed": cleanup,
            "readOnly": self.read_only, "started": datetime.fromtimestamp(started).isoformat(timespec="seconds"),
            "elapsedSeconds": round(time.time() - started, 1), "steps": records, "variables": self.variables,
            "reportDir": str(self.dir),
        }
        report["problems"], report["events"] = self.collect(start_log, start_event)
        (self.dir / "report.json").write_text(json.dumps(report, ensure_ascii=False, indent=1), encoding="utf-8")
        (self.dir / "report.md").write_text(markdown(report), encoding="utf-8")
        return report

    def collect(self, log_since, event_since):
        """Warning/error log lines and events written while the scenario ran."""
        try:
            status, log = ai_api.request(self.port, f"/api/v1/debug/log?since={log_since}&level=warning&limit=1000", timeout=15)
            problems = [{"level": e["level"], "source": e["source"], "message": e["message"][:500]} for e in log.get("entries", [])] if status == 200 else []
            status, events = ai_api.request(self.port, f"/api/v1/debug/events?since={event_since}&limit=1000", timeout=15)
            items = events.get("events", []) if status == 200 else []
            (self.dir / "events.json").write_text(json.dumps(items, ensure_ascii=False, indent=1), encoding="utf-8")
            return problems, len(items)
        except OSError:
            return [], 0


def excerpt(body, limit=2000):
    text = json.dumps(body, ensure_ascii=False)
    if len(text) <= limit:
        return body
    return {"truncated": True, "length": len(text), "start": text[:limit]}


def markdown(report):
    lines = [f"# {report['scenario'] or 'Scenario'}: {'PASS' if report['passed'] else 'FAIL'}", ""]
    if report["description"]:
        lines += [report["description"], ""]
    lines += [f"Started {report['started']}, {report['elapsedSeconds']} s" + (", read-only" if report["readOnly"] else ""), "",
              "| # | Phase | Status | Kind | Step | ms |", "| --- | --- | --- | --- | --- | --- |"]
    for r in report["steps"]:
        label = (r["name"] or r.get("request", "")).replace("|", "\\|")
        lines.append(f"| {r['index']} | {r['phase']} | {r['status']} | {r.get('kind', '')} | {label} | {r.get('durationMs', '')} |")
    failures = [r for r in report["steps"] if r["status"] == "fail"]
    if failures:
        lines += ["", "## Failures", ""]
        lines += [f"- Step {r['index']} ({r['name'] or r.get('kind')}): {r['error']}" for r in failures]
    files = [f for r in report["steps"] for f in r.get("files", [])]
    if files:
        lines += ["", "## Files", ""] + [f"- [{f}]({f})" for f in files]
    lines += ["", f"## Warnings and errors logged during the run ({len(report['problems'])})", ""]
    lines += [f"- [{p['level']}:{p['source']}] {p['message'][:200]}" for p in report["problems"][:50]] or ["None."]
    lines += ["", f"Events during the run: {report['events']} (events.json)", ""]
    return "\n".join(lines)


def run_file(path, port=38427, report_dir=None, read_only=False, variables=None):
    return Runner(load(path), port, report_dir, read_only, variables).run()


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--port", type=int, default=38427)
    sub = parser.add_subparsers(dest="command", required=True)
    run = sub.add_parser("run", help="run a scenario file and write a report")
    run.add_argument("file", type=Path)
    run.add_argument("--report-dir", type=Path, help="default: reports/<name>-<time> next to this script")
    run.add_argument("--read-only", action="store_true", help="refuse every call that changes state (mutates:true)")
    run.add_argument("--var", action="append", default=[], help="name=value (value parsed as JSON when possible)")
    check_cmd = sub.add_parser("check", help="check a scenario file without running it")
    check_cmd.add_argument("file", type=Path)
    args = parser.parse_args()

    if args.command == "check":
        scenario = load(args.file)
        errors = []
        for phase in ("steps", "finally"):
            for i, step in enumerate(scenario.get(phase, []), 1):
                kinds = [k for k in ACTIONS if k in step]
                if len(kinds) != 1:
                    errors.append(f"{phase} {i}: needs exactly one of {', '.join(ACTIONS)}")
        print(json.dumps({"ok": not errors, "steps": len(scenario.get("steps", [])), "finally": len(scenario.get("finally", [])), "errors": errors}, indent=1))
        raise SystemExit(0 if not errors else 1)

    variables = {}
    for item in args.var:
        name, _, raw = item.partition("=")
        try:
            variables[name] = json.loads(raw)
        except json.JSONDecodeError:
            variables[name] = raw
    report = run_file(args.file, args.port, args.report_dir, args.read_only, variables)
    print(json.dumps({"passed": report["passed"], "report": str(Path(report["reportDir"]) / "report.md"),
                      "failed": [f"{r['index']}: {r['error']}" for r in report["steps"] if r["status"] == "fail"],
                      "problems": len(report["problems"]), "elapsedSeconds": report["elapsedSeconds"]}, ensure_ascii=False, indent=1))
    raise SystemExit(0 if report["passed"] else 1)


if __name__ == "__main__":
    main()
