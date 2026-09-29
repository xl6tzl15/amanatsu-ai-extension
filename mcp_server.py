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


@server.tool()
def call_api(path: str, body: dict | None = None, method: str = "GET") -> dict:
    """Call any endpoint, e.g. path "creator/accessory" with method "POST". Paths are relative to /api/v1/."""
    result = call(path, body or {}) if method.upper() == "POST" else call(path)
    return result if isinstance(result, dict) else {"result": result}


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
def set_shapes(face: dict | None = None, body: dict | None = None) -> dict:
    """Set face and/or body shape values by name (e.g. {"EyeW": 0.6}); names come from character_details."""
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
def save_card() -> dict:
    """Save the open character to a new card and check the saved card against it."""
    return ai_api.save_verify(PORT)


if __name__ == "__main__":
    server.run()
