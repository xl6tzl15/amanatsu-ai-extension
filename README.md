# Amanatsu AI Extension

Local BepInEx 6 IL2CPP API for observing and controlling AmanatsuLocation.

Complete agent-facing documentation is in [AI_API_SPEC.md](./AI_API_SPEC.md).
Machine-readable endpoint metadata is in [openapi.yaml](./openapi.yaml), generated from [api_endpoints.json](./api_endpoints.json) by `python gen_openapi.py` (the build runs it and stops if a route is missing from either the code or the list).

## Install

Requires the game with BepInEx 6 (IL2CPP) and Python 3.10 or later. Extract the release ZIP into the game folder (the folder with `AmanatsuLocation.exe`). It places `BepInEx/plugins/Amanatsu.AiExtension.dll` and the client files in `ModSource/AiExtension`.

## Run

The installed DLL is `BepInEx/plugins/Amanatsu.AiExtension.dll`. Start the game, then use `python ModSource/AiExtension/ai_api.py state`. The API listens only on `127.0.0.1:38427`. Every request needs a bearer token stored in `BepInEx/config/amanatsu.ai-extension.token`. The CLI reads it automatically. Browser-origin requests are rejected.

## MCP server

`mcp_server.py` exposes the API to MCP clients over stdio. Place this folder at `<game folder>/ModSource/AiExtension`, install the SDK with `python -m pip install mcp`, start the game, and register the server:

```text
claude mcp add amanatsu-ai -- python C:/path/to/game/ModSource/AiExtension/mcp_server.py
```

For Claude Desktop, add it to `claude_desktop_config.json`:

```json
{"mcpServers": {"amanatsu-ai": {"command": "python", "args": ["C:/path/to/game/ModSource/AiExtension/mcp_server.py"]}}}
```

For Codex, add it to `~/.codex/config.toml`:

```toml
[mcp_servers.amanatsu-ai]
command = "python"
args = ["C:/path/to/game/ModSource/AiExtension/mcp_server.py"]
```

Tools: `game_state`, `open_creator`, `capture` (returns the image), `character_details`, `set_shapes`, `face_parts`, `makeup`, `eye_lines`, `set_color`, `hair_colors`, `hair_bundle`, `set_skin`, `set_profile`, `set_choice`, `params`, `shape_guide`, `set_accessory`, `accessory_move`, `freeze`, `catalog`, `cards`, `export_character`, `import_character`, `kits`, `kit_apply`, `kit_try` (returns the before/after image), `kit_save`, `save_card` (saves and checks the card), `extensions` (endpoints other plugins registered), `dev_cycle` (build, deploy and restart the game), `game_log` (BepInEx log), `wait_for` (wait for a scene, button or log line), `click` (button by name, text or path), `api_schema` and `call_api` for every other endpoint. The `call_api` description marks each endpoint `[read-only]` or `[mutates]` and `[needs creator]`. A different port is set with the environment variable `AMANATSU_AI_PORT`.

## Commands

```text
python ModSource/AiExtension/ai_api.py state
python ModSource/AiExtension/ai_api.py buttons
python ModSource/AiExtension/ai_api.py input-sliders
python ModSource/AiExtension/ai_api.py input-slider 12345 -25
python ModSource/AiExtension/ai_api.py click 41670
python ModSource/AiExtension/ai_api.py character
python ModSource/AiExtension/ai_api.py catalog bo_hair_f
python ModSource/AiExtension/ai_api.py shape body 1 0.65
python ModSource/AiExtension/ai_api.py choice hair_front 2
python ModSource/AiExtension/ai_api.py screenshot C:\temp\amanatsu.png
python ModSource/AiExtension/ai_api.py diagnostics
python ModSource/AiExtension/ai_api.py logs --limit 30
python ModSource/AiExtension/ai_api.py shadow off
python ModSource/AiExtension/ai_api.py slider-unlock off
python ModSource/AiExtension/ai_api.py dev-cycle
python ModSource/AiExtension/ai_api.py plugins
python ModSource/AiExtension/ai_api.py log --level warning
python ModSource/AiExtension/ai_api.py harmony --owner Amanatsu.AiChat
python ModSource/AiExtension/ai_api.py wait --scene Title --timeout 60
python ModSource/AiExtension/ai_api.py click-by --name Female
python ModSource/AiExtension/ai_api.py extensions
python ModSource/AiExtension/ai_api.py call GET ext/amanatsu.unlockall/slider-unlock
python ModSource/AiExtension/ai_api.py catalog ao_hair --offset 500 --limit 500
```

Button and toggle IDs change between game runs. Refresh their lists before acting. The character endpoints require the character creation scene. Shape values accept -5..5; values outside 0..1 require `Amanatsu.UnlockAll` 1.3.0 or later so the game's animation-key evaluator can extrapolate without indexing outside its key array. The extension declares that plugin as a soft load-order dependency. Choice IDs are checked against the game's own category list. The API invokes game methods on the Unity main thread. Changes are not automatically saved to a character card.

`input-sliders` lists the active maker percentage controls. `input-slider` submits text through the same `TMP_InputField.onEndEdit` event used by manual entry, so it can validate negative and over-100 input without mouse automation. With UnlockAll 1.3.2, the displayed percentage is always an integer while the normalized shape value remains a float.

Available HTTP endpoints and payloads are returned by `GET /api/v1/schema`. The screenshot API captures the game render only while its window is visible; a hidden window may return black.

## Verified in game

- API authentication, scene state, UI button listing and button invocation.
- Navigation from the title screen to female character creation through button invocation.
- Character state readback, reversible body and face shape edits, head and hair ID changes, and clothing ID changes.
- Screenshot capture and visible image differences for hair, body shape, and clothing changes.

`GET /api/v1/creator/details` returns the game's body/face parameter names, profile, coordinate parts, and colors. Clothing choices also support `clothes_top`, `clothes_add_arm`, `clothes_add_leg`, and `clothes_add_other`.

Hair parts in `creator/details` include `pos`, `rot`, `scl` and each style's `bundles` with normalized `moveRate` and `rotRate`. The visually important hair placement on existing cards is stored in these **bundle corrections**; the top-level part transforms can all be identity even when the hairstyle was hand-adjusted. Change a bundle through `POST /api/v1/creator/hair-bundle {"part":1,"index":0,"moveRate":[0.5,0.4,0.45],"rotRate":[0.25,0.5,0.5]}`. Fields are optional individually, but at least one rate is required. All components must be 0..1. The game applies its own hair-correction update and the coordinate is synchronized for saving. Bundle indices differ by style: inspect `creator/details` again after changing a hair ID. Adjust every coordinate separately when both are used.

## Operation log and duplicate character prevention

API mutations are recorded in `BepInEx/logs/amanatsu-ai/operations-YYYYMMDD.jsonl` (UTC date). Each JSON line has a timestamp, a run ID, a sequence number, the API path, safe request fields, response status, elapsed time, and game state before and after the operation. Scene and human-count changes are also logged. The bearer token and screenshot contents are not logged. Read recent entries with `python ModSource/AiExtension/ai_api.py logs --limit 30` or `GET /api/v1/logs?limit=30`.

The title screen's Female and Male buttons launch scene transitions. Repeating either API click in the same scene returns HTTP 409. Automation should click each transition button once, then poll `state` until the new scene is ready. Manual mouse and keyboard input is not captured by this log.

The experimental `POST /api/v1/creator/load` endpoint remains disabled (HTTP 501). Directly loading card bytes into the current `HumanData` and calling `Reload()` did not provide a verified safe editor lifecycle. The verified replacement is the native editor flow below.

## Anatomical camera and capture

```text
python ModSource/AiExtension/ai_api.py capture face front C:\temp\face.png
python ModSource/AiExtension/ai_api.py capture bust left C:\temp\bust-left.png
python ModSource/AiExtension/ai_api.py capture full back C:\temp\back.png
```

`POST /api/v1/capture {"region":"face","view":"front"}` returns `framing` and `image` (width, height, pngBase64). Regions: face, bust, upper_body, waist, legs, full. Views: front, back, left, right, top, bottom, relative to character rotation. `POST /api/v1/camera/frame` accepts the same body and only moves the camera. `GET /api/v1/camera/landmarks` returns world-space skeleton diagnostics.

The current deformed face mesh is baked to measure its actual bounds. Neck, chest, hips and feet are read from the character skeleton. The normal renderer culling bounds are oversized in this game and are deliberately not used for framing. Body-region widths and padding scale with the measured height; extreme accessories, poses and hair are not guaranteed to fit. Captures wait three game frames after framing and temporarily suspend blinking. UI remains visible. Other POST requests receive 409 during capture; retry after it completes.

Verified at body-height 0 and 1: face-center Y changed from approximately 11.423 to 14.585, measured height from 11.798 to 15.020. Six directions were captured in the live editor.

## Native card save and load

Initialize the file UI through SystemMenu and CharaSave/CharaLoad toggles. `GET /api/v1/creator/files` returns the current visible entries and indices. Load with `POST /api/v1/creator/native-ui {"command":"load-card","index":N}`. Refresh indices after changing the list. Save a NEW card using commands `new-card`, `capture`, `save-card`, allowing the native UI to become ready between steps. Set the camera before opening the capture screen. The PNG includes appended character data; never re-encode it as an ordinary image.

Coordinate changes are synchronized from the runtime coordinate into the save data. Clothing colors invoke the native texture rebuild; eyebrow colors invoke their native material update. Save/reload comparisons should include both coordinate types. Nickname persistence remains unreliable; age and extended backstory are external notes, not game fields. Accessory transforms and all material properties are not yet exposed. API operation logs do not record manual mouse/keyboard changes.

## 0.4.0 additions

Card inspection without loading (`cards`, `card?file=`), `creator/reset`, face part IDs and pupil presets (`creator/face-parts`), makeup and eye accent colors (`creator/makeup`), accessory transforms/default parent/clearing (`accessory-move`, `accessory-clear`), coordinate switch/copy, export/import of a whole character as replayable operations, capture expression/pose, and personality/voice/blood type in `creator/profile`. Details and examples are in `AI_API_SPEC.md`.

## 0.5.0 additions

`POST /api/v1/navigate`, `POST /api/v1/creator/shapes`, `POST /api/v1/creator/hair-colors`, `GET|POST /api/v1/creator/eye-lines`, `ai_api.py open-creator` and `ai_api.py save-card`; `card?file=` also accepts a bare file name. Usage is in `AI_API_SPEC.md`.

## 0.6.0 additions

Part kits: `creator/kits`, `kit-save`, `kit-from-card`, `kit-apply`, `kit-delete`, and `ai_api.py kits` / `kit-save` / `kit-from-card` / `kit-apply`. Export now includes the eye gradient and highlight colors. Usage is in `AI_API_SPEC.md`.

## 0.7.0 additions

`kit-apply` takes `exclude` and `dryRun` and reports the changed fields. `ai_api.py kit-try` writes a before/after image of a kit and restores the character. `creator/verify-card` and `ai_api.py save-verify` check a saved card against the open character. Usage is in `AI_API_SPEC.md`.

## 0.8.0 additions

MCP server `mcp_server.py`; see "MCP server" above.

## 0.8.1 changes

`character_details` in the MCP server includes face and body shape values by name. New MCP tools `set_skin`, `set_profile` and `hair_bundle`. `creator/reset` returns to the maker's initial character (face, body, hair, clothes and accessories) instead of neutral values.

## 0.9.0 additions

`creator/params` reads and writes the detailed maker values that had no endpoint (pupil size, highlights, eyelid line, blush position, body softness, nails, clothes pattern layout, accessory visibility and FK, rendering and more), each with what raising and lowering it does; `creator/export` includes them. `creator/shape-guide` lists the maker's face and body sliders by tab and order with what raising and lowering them does (in Japanese); `SHAPE_GUIDE.en.md` is the same guide in English. `creator/freeze` stops blinking, eye movement and the body animation. Imports of whole characters are faster (one reload at the end) and accept up to 4 MB. Accessories report their adjustment tabs (`tabs`), and `accessory-move` takes `tab` 1 or 2. MCP tools `params`, `shape_guide`, `set_accessory`, `accessory_move` and `freeze`.

## 0.9.1 changes

- `creator/params` checks every value (type, range, target slot) before changing anything, and puts back already-applied values if the game rejects one.
- Toon ramp, shadow depth and line width are applied with the game's own rendering setters; the futanari flag reloads the character.
- `creator/freeze` resumes to the state from before it stopped.
- `ai_api.py kit-try` and kit previews confirm the restore against a fresh export and fail if it does not match.
- The request size limit in `AI_API_SPEC.md` is corrected to 4 MiB.

## 0.9.2 changes

- Every endpoint is defined once in `api_endpoints.json`. `GET schema`, `openapi.yaml` and the MCP tool `call_api` are built from it, so `openapi.yaml` now lists all 73 endpoints.
- `creator/params` also puts back the value whose setter failed partway.
- `creator/freeze` keeps the state from before a stop separately for each character.

## 0.10.0 additions

- Every endpoint in `api_endpoints.json` has `mutates` (whether it changes state) and `scene` (`creator` or `any`). `GET schema` and `openapi.yaml` (`x-mutates`, `x-scene`) show them.
- `debug/plugins`: loaded plugins with versions and DLL paths.
- `debug/log`: the BepInEx log of every plugin, read with a cursor.
- `debug/harmony`: patched methods and the plugin that patched each.
- `debug/wait`: waits for a scene, a character count, the creator, a button or a log line.
- `debug/scenes`, `debug/tree`, `debug/object`, `debug/component`, `debug/find`: scenes, the GameObject hierarchy, transforms, components and their field values, read without calling getters that change state.
- `debug/types`: game and plugin types with their fields, properties and methods.
- `debug/events`: scene loads, character count, creator, top-level objects, warnings and errors, POST calls and watched values in one stream, read with a cursor. `debug/wait` can wait for an event.
- `debug/watch`: reports every change of a component field or a GameObject's active state or transform.
- `debug/snapshot` and `debug/diff`: what changed in a subtree, for example after a click.
- `scenario.py`: runs test scenarios (API calls, clicks, waits, checks on the responses, captures, snapshots and diffs, build and restart, log checks, and cleanup steps that always run) and writes a report with screenshots, log lines and events. `--read-only` refuses every call that changes state. Examples are in `examples`. MCP tool `run_scenario`.
- `ui/click` accepts `name`, `text` and `path` instead of the per-run `id`.
- `ai_api.py dev-cycle`: builds, closes the game, deploys the DLLs, starts the game and reports whether they loaded. See "Mod development" in `AI_API_SPEC.md`.
- Other BepInEx plugins can publish endpoints at `/api/v1/ext/{plugin guid}/...` by including `sdk/AiExtensionBridge.cs`. They appear in `GET schema`, `GET extensions` and the MCP tool `call_api`. See "Extension endpoints" in `AI_API_SPEC.md`.
- Self Shadow Toggle 1.5.0, Slider and Clear Unlocker 1.5.0 and Character Parameter Control 1.3.0 serve their endpoints this way. `self-shadow`, `slider-unlock`, `favorability`, `character-parameters` and `realtime-outfit` work as before and need these versions.
- `catalog` sorts the whole category before returning it, takes `offset` and `limit`, and reports `total` and `more`.
- `ai_api.py call` calls any endpoint; `ai_api.py extensions` lists the registered ones.
- MCP tools `dev_cycle`, `game_log`, `wait_for`, `click` and `extensions`.
- `openapi.yaml` lists every status each endpoint can answer with its body schema, and marks the request bodies that may be left out. `api_endpoints.json` holds the endpoint-specific statuses as `responses` and those bodies as `bodyOptional`.
- The `creator/shapes` body schema requires a value in `face` or `body`; `creator/hair-colors` requires at least one colour or flag and a non-empty `slots`; button selectors require `name`, `text` or `path`; `debug/wait` requires at least one condition.
- `card`, `creator/verify-card` and `creator/kit-from-card` list `422` for a file that is not a readable card.
- Every endpoint that needs the character creator answers `409` outside it (`creator/*`, `capture` and `camera/*` answered `400` before).
- The `creator/hair-bundle` body schema requires `moveRate` or `rotRate`; character targets require exactly one of `uniqueId`, `listIndex` and `name`.
- `creator/freeze` forgets the stopped state of characters that no longer exist.
