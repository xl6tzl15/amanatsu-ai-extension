"""MCP server (stdio) for the Amanatsu AI Extension HTTP API.

The game must be running with the AI Extension plugin loaded. Tools wrap the most used
endpoints; call_api reaches every other endpoint listed by api_schema.
"""

import base64
import io
import json
import os

try:
    from mcp.server.mcpserver import Image, MCPServer as Server
    from mcp.server.mcpserver.exceptions import ToolError
except ImportError:  # mcp 1.x
    from mcp.server.fastmcp import FastMCP as Server, Image
    from mcp.server.fastmcp.exceptions import ToolError

import ai_api

PORT = int(os.environ.get("AMANATSU_AI_PORT", "38427"))
# The same endpoint list the plugin serves as GET schema and openapi.yaml is generated from.
with open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "api_endpoints.json"), encoding="utf-8") as f:
    ENDPOINTS = {(e["method"], e["path"]): e for e in json.load(f)["endpoints"]}
REGIONS = ("face", "bust", "upper_body", "waist", "legs", "full")
VIEWS = ("front", "back", "left", "right", "top", "bottom")

server = Server(
    "amanatsu-ai-extension",
    instructions=(
        "Controls the game Amanatsu Location (甘夏ろけーしょん) through the AI Extension plugin. "
        "Start with game_state; open_creator moves from the title screen to the female character creator. "
        "Colors are [r,g,b,a] in 0..1. Use capture to look at the result of every visual change. "
        "api_schema lists every endpoint; call_api reaches the ones without a dedicated tool."
    ),
)


def call(path, payload=None):
    status, body = ai_api.request(PORT, "/api/v1/" + path.lstrip("/"), payload)
    if status != 200:
        raise ToolError(f"HTTP {status} {path}: {json.dumps(body, ensure_ascii=False)}")
    return body


def png(image_b64=None, pil=None):
    if pil is not None:
        buffer = io.BytesIO()
        pil.save(buffer, format="PNG")
        return Image(data=buffer.getvalue(), format="png")
    return Image(data=base64.b64decode(image_b64), format="png")


def get_or_set(path, values):
    return call(path, values) if values else call(path)


@server.tool()
def game_state() -> dict:
    """Current scene, loaded characters and whether the character creator is open."""
    return call("state")


@server.tool()
def api_schema() -> dict:
    """Every HTTP endpoint with its body, for use with call_api."""
    return call("schema")


@server.tool(description=(
    "Call any endpoint, e.g. path \"creator/accessory\" with method \"POST\". Paths are relative to /api/v1/; "
    "query parameters go in the path (\"catalog?category=ao_head\"). Endpoints:\n"
    + "\n".join(f"{m} {p} - {e['summary']}" + (" [mutates]" if e["mutates"] else " [read-only]")
                + (" [needs creator]" if e["scene"] == "creator" else "") for (m, p), e in ENDPOINTS.items())
    + "\nOther plugins add endpoints under ext/{plugin guid}/ while the game runs; api_schema and extensions list them."
))
def call_api(path: str, body: dict | None = None, method: str = "GET") -> dict:
    method = method.upper()
    route = path.lstrip("/").split("?")[0]
    endpoints = ENDPOINTS
    if (method, route) not in endpoints and route.startswith("ext/"):
        endpoints = live_endpoints()
    if (method, route) not in endpoints:
        known = sorted(m for m, p in endpoints if p == route)
        raise ToolError(f"{method} {route} is not an endpoint" + (f"; use {' or '.join(known)}" if known else "; see api_schema"))
    result = call(path, body or {}) if method == "POST" else call(path)
    return result if isinstance(result, dict) else {"result": result}


def live_endpoints():
    """Core and registered extension endpoints from the running game's GET schema."""
    return {(e["method"], e["path"]): e for e in call("schema")["details"]}


@server.tool()
def extensions() -> dict:
    """Plugins that registered endpoints under ext/{owner}/ (call them with call_api), and rejected registrations."""
    return call("extensions")


@server.tool()
def open_creator() -> dict:
    """Go from the title screen to the female character creator (waits until it is ready)."""
    return ai_api.open_creator(PORT)


@server.tool()
def capture(region: str = "face", view: str = "front", expression: dict | None = None) -> list:
    """Frame the creator camera on a body region and return the image.

    region: face, bust, upper_body, waist, legs, full. view: front, back, left, right, top, bottom.
    expression: optional {"eyebrow","eyes","mouth"} pattern numbers held for the shot.
    """
    if region not in REGIONS or view not in VIEWS:
        raise ValueError(f"region must be one of {REGIONS}, view one of {VIEWS}")
    payload = {"region": region, "view": view}
    if expression:
        payload["expression"] = expression
    result = call("capture", payload)
    return [png(result["image"]["pngBase64"]), json.dumps(result["framing"], ensure_ascii=False)]


@server.tool()
def character_details() -> dict:
    """Profile, face/body shape values by name, colors, skin and coordinate parts of the open character."""
    details = call("creator/details")
    shapes = call("character")
    details["faceShapes"] = dict(zip(details.pop("faceLabels"), shapes["faceShapes"]))
    details["bodyShapes"] = dict(zip(details.pop("bodyLabels"), shapes["bodyShapes"]))
    return details


@server.tool()
def shape_guide() -> dict:
    """What raising and lowering each face and body shape slider does (read this before set_shapes)."""
    return call("creator/shape-guide")


@server.tool()
def set_shapes(face: dict | None = None, body: dict | None = None) -> dict:
    """Set face and/or body shape values by name (e.g. {"EyeW": 0.6}), 0..1 with 0.5 neutral; see shape_guide for directions."""
    payload = {}
    if face:
        payload["face"] = face
    if body:
        payload["body"] = body
    return call("creator/shapes", payload)


@server.tool()
def face_parts(values: dict | None = None) -> dict:
    """Read (no values) or set face part IDs: eyebrow, eyelineUp, eyelineDown, eyelid, white, nose, lipLine, detail, eye, pupil."""
    return get_or_set("creator/face-parts", values)


@server.tool()
def makeup(values: dict | None = None) -> dict:
    """Read (no values) or set makeup: eyeshadowId/Color, cheekId/Color, lipId/Color/HighlightColor, eyeGradColor, eyeHighlightColor."""
    return get_or_set("creator/makeup", values)


@server.tool()
def eye_lines(values: dict | None = None) -> dict:
    """Read (no values) or set eyelineColor, eyelidColor, eyelineUpWeight."""
    return get_or_set("creator/eye-lines", values)


@server.tool()
def set_color(target: str, color: list[float], slot: int | None = None, channel: int | None = None, field: str | None = None) -> dict:
    """Set a color. target: hair, eye, eyebrow, clothes, accessory; slot/channel/field select the part and color."""
    payload = {"target": target, "color": color}
    for key, value in (("slot", slot), ("channel", channel), ("field", field)):
        if value is not None:
            payload[key] = value
    return call("creator/color", payload)


@server.tool()
def hair_colors(values: dict) -> dict:
    """Set hair colors: slots?, base, start, end, outline, gloss, shadow, mesh, inner, useMesh, useInner."""
    return call("creator/hair-colors", values)


@server.tool()
def set_skin(values: dict) -> dict:
    """Set skin: mainColor, shineId, shinePower, sunburnUpId, sunburnDownId, moleId (all optional)."""
    return call("creator/skin", values)


@server.tool()
def set_profile(lastname: str, firstname: str, birth_month: int = 1, birth_day: int = 1, nickname: str | None = None) -> dict:
    """Set the name and birthday (cards do not keep the nickname)."""
    return call("creator/profile", {"lastname": lastname, "firstname": firstname, "nickname": nickname or firstname,
                                     "birthMonth": birth_month, "birthDay": birth_day})


@server.tool()
def hair_bundle(part: int, index: int, move_rate: list[float] | None = None, rot_rate: list[float] | None = None) -> dict:
    """Shift a movable bundle of a hair part (0 back, 1 front, 2 side, 3 option); rates are [x,y,z] in 0..1, 0.5 = none.

    character_details lists each part's bundles.
    """
    payload = {"part": part, "index": index}
    if move_rate is not None:
        payload["moveRate"] = move_rate
    if rot_rate is not None:
        payload["rotRate"] = rot_rate
    return call("creator/hair-bundle", payload)


@server.tool()
def set_choice(kind: str, id: int) -> dict:
    """Choose a part: head, hair_back, hair_front, hair_side, hair_option, clothes_* (IDs from catalog)."""
    return call("character/choice", {"kind": kind, "id": id})


@server.tool()
def params(values: dict | None = None, eye: int | None = None, highlight: int | None = None, part: int | None = None,
           slot: int | None = None, channel: int | None = None, foot: bool | None = None) -> dict:
    """Read (no values) or set the detailed maker values: pupil and iris size, highlights, eyelid line, eyebrow
    width, white of the eye, blush position, paints, body softness, nails, hair gloss, clothes pattern layout,
    accessory visibility and FK, rendering. Each entry says what raising and lowering it does.

    Scope selectors: eye 0/1 (default both), highlight index, part (hair 0 back, 1 front, 2 side, 3 option),
    slot (clothes or accessory), channel (clothes colour), foot (toenails).
    """
    payload = {k: v for k, v in (("eye", eye), ("highlight", highlight), ("part", part), ("slot", slot),
                                 ("channel", channel), ("foot", foot)) if v is not None}
    if values:
        payload["values"] = values
    return call("creator/params", payload) if payload else call("creator/params")


@server.tool()
def set_accessory(slot: int, category: str, id: int, parent: int = 0) -> dict:
    """Put an accessory in a slot (category ao_*, IDs from catalog). character_details shows each slot's
    accessory and how many adjustment tabs it has."""
    return call("creator/accessory", {"slot": slot, "category": category, "id": id, "parent": parent})


@server.tool()
def accessory_move(slot: int, tab: int = 1, pos: list[float] | None = None, rot: list[float] | None = None,
                   scl: list[float] | None = None, reset: bool = False) -> dict:
    """Move, rotate or scale an accessory, like the maker's adjustment tabs 01 and 02.

    Tab 1 moves a one-piece accessory, or the first piece of a two-piece one; tab 2 moves the second piece
    (for example the other cat ear). Only two-piece accessories have tab 2 (see "tabs" in character_details).
    pos, rot (degrees) and scl are absolute [x, y, z] values in the maker's units; reset restores the tab first.
    """
    payload = {"slot": slot, "tab": tab, "reset": reset}
    for key, value in (("pos", pos), ("rot", rot), ("scl", scl)):
        if value is not None:
            payload[key] = value
    return call("creator/accessory-move", payload)


@server.tool()
def freeze(blink: bool | None = None, eye_movement: bool | None = None, motion: bool | None = None) -> dict:
    """Stop (false) or resume (true) blinking, small eye movements and the body animation; no values reads the state."""
    payload = {k: v for k, v in (("blink", blink), ("eyeMovement", eye_movement), ("motion", motion)) if v is not None}
    return call("creator/freeze", payload) if payload else call("creator/freeze")


@server.tool()
def catalog(category: str) -> dict:
    """List the IDs and names of a part category, e.g. bo_hair_f, bo_head, co_bot, ao_hair."""
    return call("catalog?category=" + category)


@server.tool()
def cards(file: str | None = None) -> dict:
    """List character cards (no file), or read one card without loading it."""
    from urllib.parse import quote
    return call("card?file=" + quote(file)) if file else call("cards")


@server.tool()
def export_character() -> dict:
    """The open character as replayable operations (pass "operations" to import_character to restore it)."""
    return {"operations": call("creator/export")}


@server.tool()
def import_character(operations: list, stop_on_error: bool = True) -> dict:
    """Replay operations from export_character (restores a snapshot)."""
    return call("creator/import", {"operations": operations, "stopOnError": stop_on_error})


@server.tool()
def kits() -> dict:
    """List part kits (outline, eyes, brows, nose, mouth, hair)."""
    return call("creator/kits")


@server.tool()
def kit_apply(region: str, name: str, exclude: list[str] | None = None, dry_run: bool = False) -> dict:
    """Apply a part kit; exclude keeps groups (shapes, parts, colors) or named fields; dry_run only lists changes."""
    return call("creator/kit-apply", {"region": region, "name": name, "exclude": exclude or [], "dryRun": dry_run})


@server.tool()
def kit_try(region: str, name: str, exclude: list[str] | None = None) -> list:
    """Try a kit: returns a before|after image (before left) and the changes, then restores the character."""
    output = io.BytesIO()
    result = ai_api.kit_try(region, name, output, exclude or [], PORT)
    result["image"] = "returned above"
    return [Image(data=output.getvalue(), format="png"), json.dumps(result, ensure_ascii=False)]


@server.tool()
def kit_save(region: str, name: str, description: str = "", card: str | None = None, coordinate: int = 0, overwrite: bool = False) -> dict:
    """Save a region as a kit with a thumbnail, from the open character or (card) from a card file."""
    if card:
        return ai_api.kit_from_card(card, region, name, description, coordinate, overwrite, PORT)
    return ai_api.kit_save(region, name, description, overwrite, PORT)


@server.tool()
def dev_cycle(projects: list[str] | None = None, build: bool = True, ready_scene: str = "Title") -> dict:
    """Build ModSource/<project> (default AiExtension), close the game, deploy the DLLs to BepInEx/plugins,
    start the game, wait for ready_scene ("" to skip) and report whether the new DLLs loaded and the
    warning/error lines logged on startup. Pass logCursor to game_log as since to read only later lines."""
    try:
        return ai_api.dev_cycle(projects or ["AiExtension"], build, True, ready_scene, PORT)
    except RuntimeError as exc:
        raise ToolError(str(exc))


@server.tool()
def game_log(since: int = 0, level: str | None = None, source: str | None = None, contains: str | None = None,
             limit: int = 200) -> dict:
    """BepInEx log of every plugin and Unity. level keeps that level and more severe (fatal, error, warning,
    message, info, debug). Pass the returned next as since to get only newer lines."""
    from urllib.parse import urlencode
    values = {"since": since, "limit": limit, "level": level, "source": source, "contains": contains}
    return call("debug/log?" + urlencode({k: v for k, v in values.items() if v is not None}))


@server.tool()
def wait_for(scene: str | None = None, human_count_at_least: int | None = None, creator_ready: bool | None = None,
             button: dict | None = None, log: dict | None = None, timeout_seconds: float = 10) -> dict:
    """Wait until every given condition holds (max 120 s). button: {name?, text?, path?} of a visible,
    clickable button. log: {contains?, source?, level?} of a line written after the wait started.
    Returns satisfied:false with the current scene when it times out."""
    conditions = {k: v for k, v in (("scene", scene), ("humanCountAtLeast", human_count_at_least),
                                    ("creatorReady", creator_ready), ("button", button), ("log", log)) if v is not None}
    status, result = ai_api.wait(conditions, int(timeout_seconds * 1000), PORT)
    if status not in (200, 408):
        raise ToolError(f"HTTP {status} debug/wait: {json.dumps(result, ensure_ascii=False)}")
    return result


@server.tool()
def click(name: str | None = None, text: str | None = None, path: str | None = None, index: int | None = None) -> dict:
    """Click a visible button by GameObject name, label text or object path (trailing segments are enough).
    When several match, the error lists them; add index or a longer path."""
    payload = {k: v for k, v in (("name", name), ("text", text), ("path", path), ("index", index)) if v is not None}
    return call("ui/click", payload)


@server.tool()
def run_scenario(file: str | None = None, scenario: dict | None = None, read_only: bool = False,
                 variables: dict | None = None) -> dict:
    """Run a test scenario (a JSON/YAML file path, or the scenario object itself) and return the result.

    Steps: call, assert, click, wait, capture, sleep, dev_cycle, log_check, snapshot, diff; "finally" steps
    always run. read_only refuses every call that changes state. The report folder holds report.md,
    report.json, screenshots, diffs and events.json; see "Test scenarios" in AI_API_SPEC.md.
    """
    import scenario as runner
    if (file is None) == (scenario is None):
        raise ToolError("give file or scenario")
    try:
        report = runner.run_file(file, PORT, None, read_only, variables) if file else \
            runner.Runner(scenario, PORT, None, read_only, variables).run()
    except SystemExit as exc:
        raise ToolError(str(exc))
    return {
        "passed": report["passed"], "report": str(runner.Path(report["reportDir"]) / "report.md"),
        "steps": [{k: r.get(k) for k in ("index", "phase", "status", "kind", "name", "error")} for r in report["steps"]],
        "problems": report["problems"][:20], "elapsedSeconds": report["elapsedSeconds"],
    }


@server.tool()
def save_card() -> dict:
    """Save the open character to a new card and check the saved card against it."""
    return ai_api.save_verify(PORT)


if __name__ == "__main__":
    server.run()
