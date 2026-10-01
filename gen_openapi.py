"""Writes openapi.yaml from api_endpoints.json and checks the file against the C# routes.

    python gen_openapi.py          check routes, then write openapi.yaml
    python gen_openapi.py --check  check routes and that openapi.yaml is current (no writing)

The build runs this; a route missing from either side stops the build.
"""

import json
import re
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
CATALOG = HERE / "api_endpoints.json"
OUTPUT = HERE / "openapi.yaml"
PREFIX = "/api/v1/"
CREATOR_FILES = ("CreatorApi.cs", "CreatorApiEx.cs", "CreatorApiBatch.cs", "CreatorParams.cs", "Kits.cs")


def load():
    return json.loads(CATALOG.read_text(encoding="utf-8"))


def version():
    text = (HERE / "AiExtension.csproj").read_text(encoding="utf-8")
    return re.search(r"<Version>([^<]+)</Version>", text).group(1)


def source_routes():
    """(method or None, path) for every route the C# code answers; None means any method."""
    routes = set()
    method_re = re.compile(r'method\s*==\s*"(GET|POST)"')
    for file in HERE.glob("*.cs"):
        creator = file.name in CREATOR_FILES
        for line in file.read_text(encoding="utf-8").splitlines():
            for m, p in re.findall(r'\(\s*"(GET|POST)"\s*,\s*"([a-z0-9/-]+)"\s*\)', line):
                if p.startswith(PREFIX):
                    routes.add((m, p[len(PREFIX):]))
                elif creator:
                    routes.add((m, "creator/" + p))
            method = method_re.search(line)
            for p in re.findall(r'path\s*==\s*"' + re.escape(PREFIX) + r'([a-z0-9/-]+)"', line):
                routes.add((method.group(1) if method else None, p))
            if creator:
                for p in re.findall(r'action\s*[!=]=\s*"([a-z-]+)"', line):
                    routes.add((method.group(1) if method else None, "creator/" + p))
    return routes


def check_routes(catalog):
    listed = {(e["method"], e["path"]) for e in catalog["endpoints"]}
    listed_paths = {p for _, p in listed}
    found = source_routes()
    errors = []
    for method, path in sorted(found, key=lambda r: (r[1], r[0] or "")):
        if method is None and path not in listed_paths:
            errors.append(f"route in code, missing from api_endpoints.json: {path}")
        elif method is not None and (method, path) not in listed:
            errors.append(f"route in code, missing from api_endpoints.json: {method} {path}")
    found_exact = {r for r in found if r[0]}
    found_any = {p for m, p in found if m is None}
    for method, path in sorted(listed, key=lambda r: (r[1], r[0])):
        if (method, path) not in found_exact and path not in found_any:
            errors.append(f"listed in api_endpoints.json, not answered by the code: {method} {path}")
    for e in catalog["endpoints"]:
        if not isinstance(e.get("mutates"), bool) or e.get("scene") not in ("any", "creator"):
            errors.append(f"{e['method']} {e['path']}: needs mutates (true/false) and scene (any/creator)")
    if len({(e["method"], e["path"]) for e in catalog["endpoints"]}) != len(catalog["endpoints"]):
        errors.append("api_endpoints.json lists an endpoint twice")
    return errors


def resolve(node):
    """Turn the catalog's short {"$ref": "Name"} into OpenAPI component references."""
    if isinstance(node, dict):
        if set(node) == {"$ref"} and "/" not in node["$ref"]:
            return {"$ref": "#/components/schemas/" + node["$ref"]}
        return {k: resolve(v) for k, v in node.items()}
    if isinstance(node, list):
        return [resolve(v) for v in node]
    return node


def build(catalog):
    paths = {}
    for e in catalog["endpoints"]:
        op = {"summary": e["summary"], "x-mutates": e["mutates"], "x-scene": e["scene"]}
        if e.get("deprecated"):
            op["deprecated"] = True
        if "query" in e:
            op["parameters"] = []
            for name, schema in e["query"].items():
                schema = dict(schema)
                required = schema.pop("required", False)
                description = schema.pop("description", None)
                param = {"in": "query", "name": name, "required": required, "schema": schema}
                if description:
                    param["description"] = description
                op["parameters"].append(param)
        if "body" in e:
            op["requestBody"] = {"required": True, "content": {"application/json": {"schema": resolve(e["body"])}}}
        error = {"content": {"application/json": {"schema": {"$ref": "#/components/schemas/Error"}}}}
        op["responses"] = {
            "200": {"description": "OK"},
            "400": {"description": "Invalid request", **error},
            "default": {"description": "Error", **error},
        }
        paths.setdefault(PREFIX + e["path"], {})[e["method"].lower()] = op
    return {
        "openapi": "3.1.0",
        "info": {
            "title": "Amanatsu AI Extension",
            "version": version(),
            "description": "Local loopback API for Amanatsu Location. Generated from api_endpoints.json by gen_openapi.py; "
                           "do not edit by hand. See AI_API_SPEC.md for workflow and safety constraints.",
        },
        "servers": [{"url": "http://127.0.0.1:38427"}],
        "security": [{"bearerAuth": []}],
        "components": {
            "securitySchemes": {"bearerAuth": {"type": "http", "scheme": "bearer"}},
            "schemas": resolve(catalog["components"]),
        },
        "paths": paths,
    }


def yaml(value, indent=0):
    """Small YAML writer (block style, JSON-quoted scalars) so no third-party module is needed."""
    pad = "  " * indent
    lines = []
    if isinstance(value, dict):
        for k, v in value.items():
            key = k if re.fullmatch(r"[A-Za-z_$/][\w$/.{}-]*", k) else json.dumps(k)
            if isinstance(v, (dict, list)) and v:
                lines.append(f"{pad}{key}:")
                lines.extend(yaml(v, indent + 1))
            else:
                lines.append(f"{pad}{key}: {scalar(v)}")
    else:
        for v in value:
            if isinstance(v, (dict, list)) and v:
                inner = yaml(v, indent + 1)
                lines.append(f"{pad}- {inner[0].lstrip()}")
                lines.extend(inner[1:])
            else:
                lines.append(f"{pad}- {scalar(v)}")
    return lines


def scalar(v):
    if v == {}:
        return "{}"
    if v == []:
        return "[]"
    return json.dumps(v, ensure_ascii=False)


def main():
    catalog = load()
    errors = check_routes(catalog)
    text = "\n".join(yaml(build(catalog))) + "\n"
    if "--check" in sys.argv and OUTPUT.read_text(encoding="utf-8") != text:
        errors.append("openapi.yaml is out of date; run python gen_openapi.py")
    if errors:
        print("\n".join(errors), file=sys.stderr)
        sys.exit(1)
    if "--check" not in sys.argv:
        OUTPUT.write_text(text, encoding="utf-8", newline="\n")
    print(f"{len(catalog['endpoints'])} endpoints, openapi {'checked' if '--check' in sys.argv else 'written'}")


if __name__ == "__main__":
    main()
