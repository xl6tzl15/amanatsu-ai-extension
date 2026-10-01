"""Command-line client for the local Amanatsu AI Extension API."""

import argparse
import base64
import json
from pathlib import Path
from urllib.error import HTTPError
from urllib.request import Request, urlopen


GAME_ROOT = Path(__file__).resolve().parents[2]
TOKEN_FILE = GAME_ROOT / "BepInEx" / "config" / "amanatsu.ai-extension.token"
GAME_EXE = GAME_ROOT / "AmanatsuLocation.exe"
PLUGIN_DIR = GAME_ROOT / "BepInEx" / "plugins" / "SELF"


def request(port, path, payload=None, timeout=30):
    token = TOKEN_FILE.read_text(encoding="utf-8").strip()
    data = None if payload is None else json.dumps(payload).encode("utf-8")
    req = Request(
        f"http://127.0.0.1:{port}{path}",
        data=data,
        headers={"Authorization": f"Bearer {token}", "Content-Type": "application/json"},
    )
    try:
        with urlopen(req, timeout=timeout) as response:
            return response.status, json.load(response)
    except HTTPError as exc:
        return exc.code, json.load(exc)


def open_creator(port=38427, timeout=90):
    """Go from the title screen to the female character creator (POST /api/v1/navigate)."""
    import time
    for _ in range(timeout):
        status, result = request(port, "/api/v1/navigate", {"target": "female-creator"})
        if status != 200:
            raise RuntimeError((status, result))
        if result.get("done"):
            time.sleep(2)  # let the maker finish building its UI
            return result
        time.sleep(1)
    raise RuntimeError("character creator did not open")


def save_card(port=38427):
    """Save the open character to a new card with a fresh thumbnail; returns the new file name.

    Drives the maker's own save screen (SystemMenu -> CharaSave -> new-card -> capture -> save-card)
    and confirms the result by the new file on disk, not by the button responses.
    """
    import time
    folder = GAME_ROOT / "UserData" / "chara" / "female"
    before = {p.name for p in folder.glob("*.png")}

    def toggle(name, value):
        status, toggles = request(port, "/api/v1/ui/toggles")
        item = next((t for t in toggles["toggles"] if t["name"] == name), None)
        if item is None:
            raise RuntimeError(f"toggle {name} not found")
        if item["isOn"] != value:
            request(port, "/api/v1/ui/toggle", {"id": item["id"], "value": value})
        time.sleep(0.5)

    toggle("SystemMenu", True)
    toggle("CharaSave", True)
    time.sleep(1)
    for command in ("new-card", "capture", "save-card"):
        status, result = request(port, "/api/v1/creator/native-ui", {"command": command})
        if status != 200:
            raise RuntimeError((command, status, result))
        time.sleep(3)
    for _ in range(10):
        created = sorted({p.name for p in folder.glob("*.png")} - before)
        if created:
            toggle("SystemMenu", False)
            return created[-1]
        time.sleep(1)
    raise RuntimeError("no new card appeared in UserData/chara/female")


# Crop boxes on a 1600x900 capture for each kit region (face capture; hair uses the bust capture).
KIT_CROPS = {"outline": (360, 120, 1240, 880), "eyes": (470, 300, 1130, 640), "brows": (470, 220, 1130, 520),
             "nose": (560, 380, 1040, 760), "mouth": (560, 560, 1040, 800), "hair": None}


def kit_thumbnail(region, path, port=38427):
    """Capture the current character framed for the region and write it to path (PNG)."""
    import io
    from PIL import Image
    status, result = request(port, "/api/v1/capture", {"region": "bust" if region == "hair" else "face", "view": "front"})
    if status != 200:
        raise RuntimeError((status, result))
    image = Image.open(io.BytesIO(base64.b64decode(result["image"]["pngBase64"]))).convert("RGB")
    box = KIT_CROPS[region]
    image = image.crop(box) if box else image.crop((300, 0, 1300, 900))
    image.thumbnail((480, 480))
    image.save(path)
    return path


def kit_save(region, name, description="", overwrite=False, port=38427):
    """Save the open character's region as a kit and capture its thumbnail."""
    status, result = request(port, "/api/v1/creator/kit-save",
                             {"region": region, "name": name, "description": description, "overwrite": overwrite})
    if status != 200:
        raise RuntimeError((status, result))
    kit_thumbnail(region, result["thumbnail"], port)
    return result


def kit_from_card(file, region, name, description="", coordinate=0, overwrite=False, port=38427):
    """Extract a region from a card (without loading it) and store it as a kit, then preview its thumbnail."""
    status, result = request(port, "/api/v1/creator/kit-from-card",
                             {"file": file, "region": region, "name": name, "description": description,
                              "coordinate": coordinate, "overwrite": overwrite})
    if status != 200:
        raise RuntimeError((status, result))
    kit_preview(region, name, result["thumbnail"], port)
    return result


def restore_snapshot(snapshot, port=38427):
    """Import a creator/export snapshot and confirm that a fresh export matches it exactly; raises otherwise."""
    status, result = request(port, "/api/v1/creator/import", {"operations": snapshot, "stopOnError": False})
    if status != 200 or result.get("failures"):
        raise RuntimeError(("restore failed", status, result))
    status, now = request(port, "/api/v1/creator/export")
    if status != 200:
        raise RuntimeError(("export after restore failed", status, now))
    differ = [i for i, (a, b) in enumerate(zip(snapshot, now)) if json.dumps(a, sort_keys=True) != json.dumps(b, sort_keys=True)]
    if len(snapshot) != len(now) or differ:
        raise RuntimeError(("restore did not match the snapshot", len(snapshot), len(now), [snapshot[i]["path"] for i in differ[:10]]))
    return True


def kit_preview(region, name, thumbnail, port=38427):
    """Apply a kit to the open character, capture its thumbnail and restore the character (verified against a fresh export)."""
    status, snapshot = request(port, "/api/v1/creator/export")
    if status != 200:
        raise RuntimeError((status, snapshot))
    try:
        status, result = request(port, "/api/v1/creator/kit-apply", {"region": region, "name": name})
        if status != 200:
            raise RuntimeError((status, result))
        kit_thumbnail(region, thumbnail, port)
    finally:
        restore_snapshot(snapshot, port)


def kit_apply(region, name, exclude=(), dry_run=False, port=38427):
    """Apply a kit (or with dry_run only list its changes); exclude keeps the named fields as they are."""
    return request(port, "/api/v1/creator/kit-apply",
                   {"region": region, "name": name, "exclude": list(exclude), "dryRun": dry_run})


def kit_try(region, name, output, exclude=(), port=38427):
    """Try a kit on the open character: write a before|after image to output, list the changes, then restore and verify the restore."""
    import io
    from PIL import Image

    def shot():
        status, result = request(port, "/api/v1/capture", {"region": "bust" if region == "hair" else "face", "view": "front"})
        if status != 200:
            raise RuntimeError((status, result))
        image = Image.open(io.BytesIO(base64.b64decode(result["image"]["pngBase64"]))).convert("RGB")
        box = KIT_CROPS[region]
        return image.crop(box) if box else image.crop((300, 0, 1300, 900))

    status, snapshot = request(port, "/api/v1/creator/export")
    if status != 200:
        raise RuntimeError((status, snapshot))
    before = shot()
    try:
        status, result = kit_apply(region, name, exclude, False, port)
        if status != 200:
            raise RuntimeError((status, result))
        after = shot()
    finally:
        restored = restore_snapshot(snapshot, port)
    sheet = Image.new("RGB", (before.width + after.width, max(before.height, after.height)))
    sheet.paste(before, (0, 0))
    sheet.paste(after, (before.width, 0))
    sheet.save(output, format="PNG")
    return {"image": str(output), "left": "before", "right": "after", "region": region, "name": name,
            "exclude": result["exclude"], "changes": result["changes"], "restored": restored}


def save_verify(port=38427):
    """Save the open character to a new card, read the card back from disk and compare it with the character."""
    file = save_card(port)
    status, result = request(port, "/api/v1/creator/verify-card", {"file": file})
    if status != 200:
        raise RuntimeError((status, result))
    return {"saved": file, "matches": result["matches"], "mismatches": result["mismatches"]}


def wait(conditions, timeout_ms=10000, port=38427):
    """POST debug/wait: block until every condition holds; returns (status, result), 408 on timeout."""
    return request(port, "/api/v1/debug/wait", {**conditions, "timeoutMs": timeout_ms}, timeout=timeout_ms / 1000 + 15)


def game_running(pid=None):
    """Whether the game (or, with pid, that game process) is running."""
    import subprocess
    query = f"PID eq {pid}" if pid else f"IMAGENAME eq {GAME_EXE.name}"
    out = subprocess.run(["tasklist", "/FI", query, "/FO", "CSV", "/NH"], capture_output=True, text=True).stdout
    return GAME_EXE.name.lower() in out.lower()


def api_process_id(port=38427):
    """Process id of the game whose API answers on this port, or None."""
    try:
        status, plugins = request(port, "/api/v1/debug/plugins", timeout=3)
        return plugins.get("gameProcessId") if status == 200 else None
    except OSError:
        return None


def stop_game(timeout=5, port=38427):
    """Ask the game to close, then force it after timeout seconds.

    Targets the process whose API answers on port; only when the API is not answering does it fall back
    to every AmanatsuLocation.exe.
    """
    import subprocess
    import time
    pid = api_process_id(port)
    target = ["/PID", str(pid)] if pid else ["/IM", GAME_EXE.name]
    label = f" (pid {pid})" if pid else " (by image name)"
    if not game_running(pid):
        return "not running"
    subprocess.run(["taskkill", *target], capture_output=True)
    for _ in range(timeout * 2):
        if not game_running(pid):
            return "closed" + label
        time.sleep(0.5)
    subprocess.run(["taskkill", "/F", *target], capture_output=True)
    for _ in range(20):
        if not game_running(pid):
            return "killed" + label
        time.sleep(0.5)
    raise RuntimeError("the game did not exit")


def start_game():
    import subprocess
    flags = subprocess.DETACHED_PROCESS | subprocess.CREATE_NEW_PROCESS_GROUP
    subprocess.Popen([str(GAME_EXE)], cwd=str(GAME_ROOT), creationflags=flags, close_fds=True)


def wait_api(port=38427, timeout=180):
    """Poll GET state until the plugin's API answers; returns the state."""
    import time
    from urllib.error import URLError
    deadline = time.time() + timeout
    while time.time() < deadline:
        try:
            status, state = request(port, "/api/v1/state", timeout=5)
            if status == 200:
                return state
        except (URLError, OSError):
            pass
        time.sleep(1)
    raise RuntimeError(f"API did not answer within {timeout}s")


def build_project(name):
    """dotnet build -c Release for ModSource/<name>; returns the built DLL path."""
    import re
    import subprocess
    folder = GAME_ROOT / "ModSource" / name
    projects = list(folder.glob("*.csproj"))
    if len(projects) != 1:
        raise RuntimeError(f"ModSource/{name} must contain exactly one .csproj")
    text = projects[0].read_text(encoding="utf-8")
    assembly = re.search(r"<AssemblyName>([^<]+)</AssemblyName>", text)
    framework = re.search(r"<TargetFramework>([^<]+)</TargetFramework>", text).group(1)
    result = subprocess.run(["dotnet", "build", str(projects[0]), "-c", "Release", "-nologo", "-v", "q"],
                            capture_output=True, text=True, encoding="utf-8", errors="replace")
    if result.returncode != 0:
        lines = [l for l in result.stdout.splitlines() if ": error" in l] or result.stdout.splitlines()[-30:]
        raise RuntimeError(f"build of {name} failed:\n" + "\n".join(dict.fromkeys(lines)))
    dll = folder / "bin" / "Release" / framework / ((assembly.group(1) if assembly else projects[0].stem) + ".dll")
    if not dll.exists():
        raise RuntimeError(f"build of {name} produced no {dll}")
    return dll


def deployed_path(dll):
    """Where the game loads this DLL from: the existing copy under BepInEx/plugins, else plugins/SELF."""
    existing = list((GAME_ROOT / "BepInEx" / "plugins").rglob(dll.name))
    if len(existing) > 1:
        raise RuntimeError(f"{dll.name} is installed more than once: {[str(p) for p in existing]}")
    return existing[0] if existing else PLUGIN_DIR / dll.name


def dev_cycle(projects=("AiExtension",), build=True, restart=True, ready_scene="Title", port=38427):
    """Build, stop the game, deploy the DLLs, start the game and confirm the new DLLs are the ones loaded.

    Returns the steps, each plugin's loaded version and the error/warning lines the game logged on startup.
    """
    import shutil
    import time
    started = time.time()
    report = {"projects": list(projects), "steps": []}
    dlls = []
    for name in projects:
        if build:
            dll = build_project(name)
        else:
            built = sorted((GAME_ROOT / "ModSource" / name).glob("bin/Release/*/Amanatsu.*.dll"), key=lambda p: p.stat().st_mtime)
            if not built:
                raise RuntimeError(f"no built DLL for {name}; run with build")
            dll = built[-1]
        dlls.append(dll)
        report["steps"].append({"built" if build else "using": str(dll)})
    if restart:
        report["steps"].append({"stop": stop_game(port=port)})
    elif game_running():
        raise RuntimeError("the game is running and locks its plugin DLLs; deploying needs restart")
    targets = []
    for dll in dlls:
        target = deployed_path(dll)
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(dll, target)
        targets.append(target)
        report["steps"].append({"deployed": str(target)})
    if not restart:
        report["elapsedSeconds"] = round(time.time() - started, 1)
        return report
    start_game()
    report["steps"].append({"started": str(GAME_EXE)})
    report["steps"].append({"api": wait_api(port)})
    scene_reached = True
    if ready_scene:
        status, result = wait({"scene": ready_scene}, 120000, port)
        scene_reached = status == 200
        report["steps"].append({"scene": ready_scene, "reached": scene_reached, "elapsedMs": result.get("elapsedMs"),
                                "state": result.get("state")})
    status, plugins = request(port, "/api/v1/debug/plugins")
    loaded = []
    for target in targets:
        match = next((p for p in plugins.get("plugins", [])
                      if p.get("location") and Path(p["location"]).resolve() == target.resolve()), None)
        loaded.append({"dll": target.name, "loaded": bool(match and match["loaded"]),
                       "guid": match and match["guid"], "version": match and match["version"]})
    report["plugins"] = loaded
    status, problems = request(port, "/api/v1/debug/log?level=warning&limit=200")
    report["startupProblems"] = [{"level": e["level"], "source": e["source"], "message": e["message"][:400]}
                                 for e in problems.get("entries", [])]
    report["logCursor"] = problems.get("next")
    report["sceneReached"] = scene_reached
    report["ok"] = scene_reached and all(p["loaded"] for p in loaded)
    report["elapsedSeconds"] = round(time.time() - started, 1)
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--port", type=int, default=38427)
    sub = parser.add_subparsers(dest="command", required=True)
    for name in ("schema", "state", "buttons", "toggles", "input-sliders", "character"):
        sub.add_parser(name)
    for command_name in ("favorability", "parameters"):
        parameter_cmd = sub.add_parser(command_name)
        parameter_cmd.add_argument("unique_id", nargs="?", type=int)
        parameter_cmd.add_argument("--index", type=int, dest="list_index")
        parameter_cmd.add_argument("--name")
        parameter_cmd.add_argument("--parameter", choices=("favorability", "inclusiveness", "proactivity", "curiosity"), default="favorability")
        parameter_cmd.add_argument("--point", type=int)
        parameter_cmd.add_argument("--delta", type=int)
        parameter_cmd.add_argument("--level", type=int)
        parameter_cmd.add_argument("--max-level", choices=("on", "off"))
        parameter_cmd.add_argument("--gauge-index", type=int)
        parameter_cmd.add_argument("--gauge-stage", type=int, choices=range(7))
        parameter_cmd.add_argument("--night-count", type=int)
        parameter_cmd.add_argument("--h-count", type=int)
        parameter_cmd.add_argument("--massage-count", type=int)
        parameter_cmd.add_argument("--late-point", type=int)
        parameter_cmd.add_argument("--cost", type=int)
        parameter_cmd.add_argument("--unlock-scenes", action="store_true")
    outfit = sub.add_parser("outfit")
    outfit.add_argument("unique_id", nargs="?", type=int)
    outfit.add_argument("--index", type=int, dest="list_index")
    outfit.add_argument("--name")
    outfit.add_argument("--coordinate", choices=("swimsuit", "afterBath"))
    outfit.add_argument("--part", choices=("top", "bottom", "bra", "shorts", "gloves", "pantyhose", "socks", "shoes"))
    outfit.add_argument("--state", choices=("clothing", "halfUndress", "naked"))
    outfit.add_argument("--accessory-slot", type=int)
    outfit.add_argument("--accessory-visible", choices=("on", "off"))
    outfit.add_argument("--all-accessories", choices=("on", "off"))
    shadow = sub.add_parser("shadow")
    shadow.add_argument("value", nargs="?", choices=("on", "off"))
    slider_unlock = sub.add_parser("slider-unlock")
    slider_unlock.add_argument("value", nargs="?", choices=("on", "off"))
    logs = sub.add_parser("logs")
    logs.add_argument("--limit", type=int, default=100)
    sub.add_parser("diagnostics")
    catalog = sub.add_parser("catalog")
    catalog.add_argument("category")
    catalog.add_argument("--offset", type=int, default=0)
    catalog.add_argument("--limit", type=int, default=500)
    screenshot = sub.add_parser("screenshot")
    screenshot.add_argument("output", type=Path)
    capture = sub.add_parser("capture")
    capture.add_argument("region", choices=("face","bust","upper_body","waist","legs","full"))
    capture.add_argument("view", choices=("front","left","right","back","top","bottom"))
    capture.add_argument("output", type=Path)
    click = sub.add_parser("click")
    click.add_argument("id", type=int)
    toggle = sub.add_parser("toggle")
    toggle.add_argument("id", type=int)
    toggle.add_argument("value", choices=("on", "off"))
    slider_input = sub.add_parser("input-slider")
    slider_input.add_argument("id", type=int)
    slider_input.add_argument("text")
    shape = sub.add_parser("shape")
    shape.add_argument("region", choices=("body", "face"))
    shape.add_argument("index", type=int)
    shape.add_argument("value", type=float)
    choice = sub.add_parser("choice")
    choice.add_argument("kind")
    choice.add_argument("id", type=int)
    sub.add_parser("cards")
    card = sub.add_parser("card")
    card.add_argument("file")
    export = sub.add_parser("export")
    export.add_argument("output", type=Path)
    importer = sub.add_parser("import")
    importer.add_argument("input", type=Path)
    importer.add_argument("--continue-on-error", action="store_true")
    sub.add_parser("open-creator", help="go from the title screen to the female character creator")
    sub.add_parser("kits", help="list part kits")
    kit_save_cmd = sub.add_parser("kit-save", help="save a region of the open character as a kit (with thumbnail)")
    kit_save_cmd.add_argument("region", choices=tuple(KIT_CROPS))
    kit_save_cmd.add_argument("name")
    kit_save_cmd.add_argument("--description", default="")
    kit_save_cmd.add_argument("--overwrite", action="store_true")
    kit_card_cmd = sub.add_parser("kit-from-card", help="extract a region of a card as a kit (with preview thumbnail)")
    kit_card_cmd.add_argument("file")
    kit_card_cmd.add_argument("region", choices=tuple(KIT_CROPS))
    kit_card_cmd.add_argument("name")
    kit_card_cmd.add_argument("--description", default="")
    kit_card_cmd.add_argument("--coordinate", type=int, default=0)
    kit_card_cmd.add_argument("--overwrite", action="store_true")
    kit_apply_cmd = sub.add_parser("kit-apply", help="apply a kit to the open character")
    kit_apply_cmd.add_argument("region", choices=tuple(KIT_CROPS))
    kit_apply_cmd.add_argument("name")
    kit_apply_cmd.add_argument("--exclude", nargs="+", default=[], help="groups (shapes, parts, colors) or field names to keep")
    kit_apply_cmd.add_argument("--dry-run", action="store_true", help="only list the changes")
    kit_try_cmd = sub.add_parser("kit-try", help="try a kit: before|after image and changes, then restore the character")
    kit_try_cmd.add_argument("region", choices=tuple(KIT_CROPS))
    kit_try_cmd.add_argument("name")
    kit_try_cmd.add_argument("output", type=Path)
    kit_try_cmd.add_argument("--exclude", nargs="+", default=[])
    sub.add_parser("save-verify", help="save the open character to a new card and check the saved card against it")
    sub.add_parser("save-card", help="save the open character to a new card with a fresh thumbnail")
    cycle = sub.add_parser("dev-cycle", help="build, stop the game, deploy to BepInEx/plugins, start it and check the new DLL loaded")
    cycle.add_argument("projects", nargs="*", default=["AiExtension"], help="folders under ModSource (default AiExtension)")
    cycle.add_argument("--no-build", action="store_true", help="deploy the last build")
    cycle.add_argument("--no-restart", action="store_true", help="only build and deploy (the game must be closed)")
    cycle.add_argument("--ready-scene", default="Title", help="scene to wait for after start (empty to skip)")
    sub.add_parser("stop-game", help="close the game (forced after 5 seconds)")
    sub.add_parser("start-game", help="start the game and wait for the API")
    sub.add_parser("plugins", help="loaded BepInEx plugins and their versions")
    log_cmd = sub.add_parser("log", help="BepInEx log lines after a cursor")
    log_cmd.add_argument("--since", type=int, default=0)
    log_cmd.add_argument("--limit", type=int, default=200)
    log_cmd.add_argument("--level", choices=("fatal", "error", "warning", "message", "info", "debug"))
    log_cmd.add_argument("--source")
    log_cmd.add_argument("--contains")
    harmony = sub.add_parser("harmony", help="Harmony patches by target method and owner")
    harmony.add_argument("--owner")
    harmony.add_argument("--target")
    wait_cmd = sub.add_parser("wait", help="wait until every given condition holds")
    wait_cmd.add_argument("--scene")
    wait_cmd.add_argument("--humans", type=int, dest="human_count")
    wait_cmd.add_argument("--creator-ready", choices=("true", "false"))
    wait_cmd.add_argument("--button-name")
    wait_cmd.add_argument("--button-text")
    wait_cmd.add_argument("--button-path")
    wait_cmd.add_argument("--log-contains")
    wait_cmd.add_argument("--log-source")
    wait_cmd.add_argument("--log-level", choices=("fatal", "error", "warning", "message", "info", "debug"))
    wait_cmd.add_argument("--timeout", type=float, default=10, help="seconds (max 120)")
    click_by = sub.add_parser("click-by", help="click a button by name, label text or path instead of its per-run id")
    click_by.add_argument("--name")
    click_by.add_argument("--text")
    click_by.add_argument("--path")
    click_by.add_argument("--index", type=int)
    sub.add_parser("extensions", help="plugins that registered endpoints under ext/")
    call_cmd = sub.add_parser("call", help="call any endpoint, e.g. call GET debug/scenes")
    call_cmd.add_argument("method", choices=("GET", "POST"))
    call_cmd.add_argument("path", help="relative to /api/v1/, query string included")
    call_cmd.add_argument("--body", default="{}", help="JSON body for POST")
    args = parser.parse_args()

    if args.command == "call":
        status, data = request(args.port, "/api/v1/" + args.path.lstrip("/"),
                               json.loads(args.body) if args.method == "POST" else None)
        print(json.dumps({"status": status, "body": data}, ensure_ascii=False, indent=1))
        raise SystemExit(0 if status == 200 else 1)

    if args.command == "dev-cycle":
        result = dev_cycle(args.projects, not args.no_build, not args.no_restart, args.ready_scene, args.port)
        print(json.dumps(result, ensure_ascii=False, indent=1))
        raise SystemExit(0 if result.get("ok", True) else 1)
    if args.command == "stop-game":
        print(json.dumps({"stop": stop_game(port=args.port)}))
        return
    if args.command == "start-game":
        start_game()
        print(json.dumps({"state": wait_api(args.port)}, ensure_ascii=False))
        return
    if args.command == "wait":
        conditions = {}
        if args.scene:
            conditions["scene"] = args.scene
        if args.human_count is not None:
            conditions["humanCountAtLeast"] = args.human_count
        if args.creator_ready:
            conditions["creatorReady"] = args.creator_ready == "true"
        button = {k: v for k, v in (("name", args.button_name), ("text", args.button_text), ("path", args.button_path)) if v}
        if button:
            conditions["button"] = button
        log = {k: v for k, v in (("contains", args.log_contains), ("source", args.log_source), ("level", args.log_level)) if v}
        if log:
            conditions["log"] = log
        status, data = wait(conditions, int(args.timeout * 1000), args.port)
        print(json.dumps({"status": status, **data}, ensure_ascii=False, indent=1))
        raise SystemExit(0 if status == 200 else 1)

    if args.command == "kits":
        status, data = request(args.port, "/api/v1/creator/kits")
        print(json.dumps(data, ensure_ascii=False, indent=2))
        return
    if args.command == "kit-save":
        print(json.dumps(kit_save(args.region, args.name, args.description, args.overwrite, args.port), ensure_ascii=False))
        return
    if args.command == "kit-from-card":
        print(json.dumps(kit_from_card(args.file, args.region, args.name, args.description, args.coordinate, args.overwrite, args.port), ensure_ascii=False))
        return
    if args.command == "kit-apply":
        status, data = kit_apply(args.region, args.name, args.exclude, args.dry_run, args.port)
        print(json.dumps(data, ensure_ascii=False, indent=1))
        raise SystemExit(0 if status == 200 else 1)
    if args.command == "kit-try":
        print(json.dumps(kit_try(args.region, args.name, args.output, args.exclude, args.port), ensure_ascii=False, indent=1))
        return
    if args.command == "save-verify":
        result = save_verify(args.port)
        print(json.dumps(result, ensure_ascii=False, indent=1))
        raise SystemExit(0 if result["matches"] else 1)
    if args.command == "open-creator":
        print(json.dumps(open_creator(args.port), ensure_ascii=False))
        return
    if args.command == "save-card":
        print(json.dumps({"saved": save_card(args.port)}, ensure_ascii=False))
        return

    if args.command in ("cards", "card", "export", "import"):
        from urllib.parse import quote
        if args.command == "cards":
            status, data = request(args.port, "/api/v1/cards")
        elif args.command == "card":
            status, data = request(args.port, "/api/v1/card?file=" + quote(args.file))
        elif args.command == "export":
            status, data = request(args.port, "/api/v1/creator/export")
            if status == 200:
                args.output.write_text(json.dumps({"operations": data}, ensure_ascii=False, indent=1), encoding="utf-8")
                data = {"saved": str(args.output), "operations": len(data)}
        else:
            document = json.loads(args.input.read_text(encoding="utf-8"))
            operations = document["operations"] if isinstance(document, dict) else document
            status, data = request(args.port, "/api/v1/creator/import",
                                   {"operations": operations, "stopOnError": not args.continue_on_error})
        print(json.dumps(data, ensure_ascii=False, indent=2))
        raise SystemExit(0 if status == 200 else 1)

    paths = {
        "schema": "/api/v1/schema",
        "state": "/api/v1/state",
        "buttons": "/api/v1/ui/buttons",
        "toggles": "/api/v1/ui/toggles",
        "input-sliders": "/api/v1/ui/input-sliders",
        "character": "/api/v1/character",
        "screenshot": "/api/v1/screenshot",
        "plugins": "/api/v1/debug/plugins",
        "extensions": "/api/v1/extensions",
    }
    payload = None
    if args.command in paths:
        path = paths[args.command]
    elif args.command == "shadow":
        path = "/api/v1/self-shadow"
        if args.value is not None:
            payload = {"enabled": args.value == "on"}
    elif args.command == "slider-unlock":
        path = "/api/v1/slider-unlock"
        if args.value is not None:
            payload = {"enabled": args.value == "on"}
    elif args.command in ("favorability", "parameters"):
        path = "/api/v1/favorability" if args.command == "favorability" else "/api/v1/character-parameters"
        selectors = sum(x is not None for x in (args.unique_id, args.list_index, args.name))
        mutations = any(x is not None for x in (
            args.point, args.delta, args.level, args.max_level, args.gauge_index,
            args.gauge_stage, args.night_count, args.h_count, args.massage_count,
            args.late_point, args.cost
        )) or args.unlock_scenes
        if selectors > 1:
            parser.error("parameter target must use only one of unique_id, --index, or --name")
        if selectors == 0 and mutations:
            parser.error("parameter mutation requires unique_id, --index, or --name")
        if selectors == 1:
            payload = {"parameter": args.parameter}
            if args.unique_id is not None:
                payload["uniqueId"] = args.unique_id
            elif args.list_index is not None:
                payload["listIndex"] = args.list_index
            else:
                payload["name"] = args.name
            if args.point is not None:
                payload["point"] = args.point
            if args.delta is not None:
                payload["delta"] = args.delta
            if args.level is not None:
                payload["level"] = args.level
            if args.max_level is not None:
                payload["isMaxLevel"] = args.max_level == "on"
            if args.gauge_index is not None:
                payload["gaugeStageIndex"] = args.gauge_index
            if args.gauge_stage is not None:
                payload["gaugeStage"] = args.gauge_stage
            if args.night_count is not None:
                payload["nightEventCount"] = args.night_count
            if args.h_count is not None:
                payload["hCount"] = args.h_count
            if args.massage_count is not None:
                payload["massageCount"] = args.massage_count
            if args.late_point is not None:
                payload["latePoint"] = args.late_point
            if args.cost is not None:
                payload["cost"] = args.cost
            if args.unlock_scenes:
                payload["unlockScenes"] = True
    elif args.command == "outfit":
        path = "/api/v1/realtime-outfit"
        selectors = sum(x is not None for x in (args.unique_id, args.list_index, args.name))
        if selectors != 1:
            parser.error("outfit target requires exactly one of unique_id, --index, or --name")
        if (args.part is None) != (args.state is None):
            parser.error("--part and --state must be provided together (omit --part to apply --state to all clothes)")
        if (args.accessory_slot is None) != (args.accessory_visible is None):
            parser.error("--accessory-slot and --accessory-visible must be provided together")
        if not any(x is not None for x in (args.coordinate, args.state, args.accessory_slot, args.all_accessories)):
            parser.error("outfit requires a coordinate, clothes, or accessory change")
        payload = {}
        if args.unique_id is not None:
            payload["uniqueId"] = args.unique_id
        elif args.list_index is not None:
            payload["listIndex"] = args.list_index
        else:
            payload["name"] = args.name
        if args.coordinate is not None:
            payload["coordinateType"] = args.coordinate
        if args.state is not None:
            payload["clothesState"] = args.state
            if args.part is not None:
                payload["clothesPart"] = args.part
        if args.accessory_slot is not None:
            payload["accessorySlot"] = args.accessory_slot
            payload["accessoryVisible"] = args.accessory_visible == "on"
        if args.all_accessories is not None:
            payload["allAccessoriesVisible"] = args.all_accessories == "on"
    elif args.command == "catalog":
        path = f"/api/v1/catalog?category={args.category}&offset={args.offset}&limit={args.limit}"
    elif args.command in ("log", "harmony"):
        from urllib.parse import urlencode
        if args.command == "log":
            values = {"since": args.since, "limit": args.limit, "level": args.level, "source": args.source, "contains": args.contains}
        else:
            values = {"owner": args.owner, "target": args.target}
        path = f"/api/v1/debug/{args.command}?" + urlencode({k: v for k, v in values.items() if v is not None})
    elif args.command == "click-by":
        path = "/api/v1/ui/click"
        payload = {k: v for k, v in (("name", args.name), ("text", args.text), ("path", args.path), ("index", args.index)) if v is not None}
    elif args.command == "logs":
        path = f"/api/v1/logs?limit={args.limit}"
    elif args.command == "diagnostics":
        path = "/api/v1/creator/diagnostics"
    elif args.command == "capture":
        path, payload = "/api/v1/capture", {"region":args.region,"view":args.view}
    elif args.command == "click":
        path, payload = "/api/v1/ui/click", {"id": args.id}
    elif args.command == "toggle":
        path, payload = "/api/v1/ui/toggle", {"id": args.id, "value": args.value == "on"}
    elif args.command == "input-slider":
        path, payload = "/api/v1/ui/input-slider", {"id": args.id, "text": args.text}
    elif args.command == "shape":
        path, payload = "/api/v1/character/shape", {
            "region": args.region, "index": args.index, "value": args.value
        }
    else:
        path, payload = "/api/v1/character/choice", {"kind": args.kind, "id": args.id}

    status, result = request(args.port, path, payload)
    if args.command == "screenshot" and status == 200:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_bytes(base64.b64decode(result.pop("pngBase64")))
        result["file"] = str(args.output.resolve())
    if args.command == "capture" and status == 200:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_bytes(base64.b64decode(result['image'].pop('pngBase64')))
        result['file']=str(args.output.resolve())
    print(json.dumps({"status": status, **result}, ensure_ascii=False, indent=2))
    raise SystemExit(0 if status == 200 else 1)


if __name__ == "__main__":
    main()
