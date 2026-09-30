# Amanatsu AI Extension API specification

Version: `v1`  
Plugin version inspected: `0.9.2`  
Default base URL: `http://127.0.0.1:38427`

This document describes the local HTTP API exposed by `Amanatsu AI Extension`.
It is written for another AI agent or automation client that needs to inspect and
operate Amanatsu Location without guessing about the current UI state.

## Safety rules

1. Never terminate `AmanatsuLocation.exe` unless the user explicitly authorizes
   that termination in the current turn.
2. Use the existing installation and its existing `UserData`. Do not make a copy
   of the installation or copy `UserData` for testing.
3. Before changing a character, read `GET /api/v1/character` and
   `GET /api/v1/creator/details`. If the open character may contain unsaved user
   work, do not change it. Ask the user or save a separate new card only when that
   action is authorized.
4. Never overwrite or delete a character card implicitly. The direct
   `POST /api/v1/creator/save` endpoint refuses an existing filename.
5. UI instance IDs are temporary. Refresh the corresponding UI collection just
   before every click, toggle, or numeric entry.
6. A successful mutation response is not sufficient verification. Read the state
   back and capture the relevant front and side views.

## Connection and authentication

The server binds only to `127.0.0.1`. Requests with a non-loopback source or any
`Origin` header are rejected. The bearer token is stored at:

```text
BepInEx/config/amanatsu.ai-extension.token
```

Every request must include:

```http
Authorization: Bearer <token contents>
Content-Type: application/json
```

Only `GET` and `POST` are accepted. Request bodies are limited to 4 MiB (4,194,304 bytes).
The default port can be changed in the BepInEx config under `[API] Port`.

PowerShell example:

```powershell
$token = (Get-Content -LiteralPath '.\BepInEx\config\amanatsu.ai-extension.token' -Raw).Trim()
$headers = @{ Authorization = "Bearer $token" }
Invoke-RestMethod -Headers $headers -Uri 'http://127.0.0.1:38427/api/v1/state'
```

Python clients can reuse [`ai_api.py`](./ai_api.py):

```python
from ai_api import request

status, state = request(38427, "/api/v1/state")
if status != 200:
    raise RuntimeError((status, state))
```

## Readiness and concurrency

Call `GET /api/v1/state` first. Character editing is available only when
`characterEditorReady` is `true`. Requests execute on the Unity main thread.
A request times out with `503` if the main thread does not answer within ten
seconds.

`POST /api/v1/capture` waits three rendered frames before taking the image. While
a capture is pending, other POST requests return `409`. Send mutations
sequentially and wait for each response.

## Common status codes

| Status | Meaning |
| --- | --- |
| `200` | Request completed. Inspect returned `after`, `enabled`, or saved state. |
| `400` | Invalid JSON, field, range, category, or choice ID. |
| `401` | Missing or incorrect bearer token. |
| `403` | Request was not local or contained an `Origin` header. |
| `404` | Unknown endpoint or stale UI instance ID. |
| `405` | Unsupported HTTP method. |
| `409` | Wrong scene/UI state, hidden control, disabled control, missing save data, or capture already in progress. |
| `413` | Request exceeded 4 MiB (4,194,304 bytes). |
| `501` | Direct card loading is disabled, or direct saving failed to produce a valid PNG card. |
| `503` | Main thread timeout or an optional companion plugin is unavailable. |

Errors use `{"error":"message"}`. Some companion-plugin errors also return a
machine-readable `code`.

## Core state endpoints

### `GET /api/v1/schema`

Returns the API version, the plugin version, a compact one-line list of endpoints (`endpoints`), and the full definitions with JSON Schema bodies (`details`, `components`). All of it comes from `api_endpoints.json`, the same file `openapi.yaml` is generated from.

### `GET /api/v1/state`

Returns:

```json
{
  "unityVersion": "6000.0.65f1",
  "scene": "CustomScene",
  "sceneBuildIndex": 4,
  "characterEditorReady": true,
  "visibleButtons": 28
}
```

### `GET /api/v1/character`

Returns the current character's names, raw body and face shape arrays, head ID,
hair IDs, current-coordinate clothing IDs, and skin/detail IDs. The response is
the authoritative readback after shape and choice mutations.

### `GET /api/v1/creator/details`

Returns the profile, active coordinate, hair parts and movable bundles, eye
colors, clothing colors, accessories, and the ordered `bodyLabels` and
`faceLabels`. Clients should prefer these returned labels over a hard-coded
table when possible.

### `GET /api/v1/creator/diagnostics`

Lists loaded `Human` objects, their active/disposed state, position, and whether
they are the current maker character.

## Shape editing

### `POST /api/v1/character/shape`

```json
{"region":"body","index":3,"value":0.15}
```

`region` is `body` or `face`. `value` must be finite and within `-5..5`. The API
sets `HumanData.SkipRangeCheck=true`, applies the shape, refreshes the model, and
returns `before`, `requested`, `accepted`, `after`, and `skipRangeCheck`.

Values outside the normal `0..1` range require the Slider and Clear Unlocker to
be loaded because the game's animation-key evaluator otherwise cannot safely
extrapolate them. Prefer `0..1` unless an extended value is specifically needed.

Body indices:

| Index | Label | Index | Label |
| ---: | --- | ---: | --- |
| 0 | Height | 21 | Belly |
| 1 | HeadSize | 22 | WaistUpW |
| 2 | NeckSize | 23 | WaistUpZ |
| 3 | BustSize | 24 | WaistLowW |
| 4 | BustY | 25 | WaistLowZ |
| 5 | BustRotX | 26 | HipSize |
| 6 | BustX | 27 | HipRotX |
| 7 | BustRotY | 28 | ThighUpSize |
| 8 | BustSharp | 29 | ThighLowSize |
| 9 | BustForm1 | 30 | Calf |
| 10 | BustForm2 | 31 | KneeSize |
| 11 | AreolaBulge | 32 | AnkleSize |
| 12 | NipWeight | 33 | ShoulderW |
| 13 | NipStand | 34 | ShoulderZ |
| 14 | BodyShoulderW | 35 | ArmUpSize |
| 15 | BodyShoulderZ | 36 | ElbowSize |
| 16 | BodyUpW | 37 | ArmFront |
| 17 | BodyUpZ | 38 | WristSize |
| 18 | BodyLowW | 39 | HandW |
| 19 | BodyLowZ | 40 | NeckY |
| 20 | WaistY | 41 | HipSizeY |

Face indices:

| Index | Label | Index | Label |
| ---: | --- | ---: | --- |
| 0 | FaceBaseW | 28 | EyelidsLowForm2 |
| 1 | FaceUpZ | 29 | EyelidsLowForm3 |
| 2 | FaceUpY | 30 | EyeY |
| 3 | FaceUpSize | 31 | EyeX |
| 4 | FaceLowZ | 32 | EyeZ |
| 5 | FaceLowW | 33 | EyeTilt |
| 6 | ChinLowY | 34 | EyeH |
| 7 | ChinLowZ | 35 | EyeW |
| 8 | ChinY | 36 | NoseTipH |
| 9 | ChinW | 37 | NoseY |
| 10 | ChinZ | 38 | NoseBridgeH |
| 11 | ChinTipY | 39 | MouthY |
| 12 | ChinTipZ | 40 | MouthW |
| 13 | ChinTipW | 41 | MouthZ |
| 14 | CheekBoneW | 42 | MouthUpForm |
| 15 | CheekBoneZ | 43 | MouthLowForm |
| 16 | CheekW | 44 | MouthCornerFormRot |
| 17 | CheekZ | 45 | MouthCornerFormY |
| 18 | CheekY | 46 | EarSize |
| 19 | EyebrowY | 47 | EarRotY |
| 20 | EyebrowX | 48 | EarRotZ |
| 21 | EyebrowRotZ | 49 | EarUpForm |
| 22 | EyebrowInForm | 50 | EarLowForm |
| 23 | EyebrowOutForm | 51 | NoseBase |
| 24 | EyelidsUpForm1 | 52 | FaceSize |
| 25 | EyelidsUpForm2 | 53 | FaceY |
| 26 | EyelidsUpForm3 | 54 | HeadSize |
| 27 | EyelidsLowForm1 |  |  |

For female characters use head choice ID `0` (`type 1`). In this game's current
asset set, head choice ID `1` (`type 2`) is a male-oriented outline and should
not be selected merely to differentiate a female face.

## Part catalogs and choices

### `GET /api/v1/catalog?category=<CategoryNo>`

Returns sorted `{id,name}` choices from the game's live category table. Query a
catalog before selecting an ID. Common categories include:

```text
bo_head bo_hair_b bo_hair_f bo_hair_s bo_hair_o
co_top co_bot co_bra co_shorts co_gloves co_panst co_socks co_shoes
co_add_arm co_add_leg co_add_other
```

### `POST /api/v1/character/choice`

```json
{"kind":"hair_front","id":16}
```

Kinds: `head`, `hair_back`, `hair_front`, `hair_side`, `hair_option`,
`clothes_top`, `clothes_bot`, `clothes_bra`, `clothes_shorts`,
`clothes_gloves`, `clothes_panst`, `clothes_socks`, `clothes_shoes`,
`clothes_add_arm`, `clothes_add_leg`, and `clothes_add_other`.

The ID must exist in the matching game catalog. The schema text in plugin 0.3.4
omits `clothes_top` and the three `clothes_add_*` kinds, but the implementation
supports them.

## Profile, colors, hair, and accessories

### `POST /api/v1/creator/profile`

```json
{
  "lastname":"鷹瀬",
  "firstname":"怜",
  "nickname":"れい",
  "birthMonth":11,
  "birthDay":4,
  "personality":3,
  "voiceRate":0.5,
  "bloodType":1
}
```

All three names are required, nonblank, and at most 20 characters. The birthday
must be a valid month/day pair. `personality` must be one of
`GET /api/v1/creator/personalities` (the maker's sample-voice table),
`voiceRate` is `0..1`, `bloodType` is `0..3`. Omitted fields keep their value.
In the current game build, a nickname set through this endpoint can read back in
the open maker but become empty after saving and loading a card. Verify the saved
card with `GET /api/v1/card?file=` when the nickname matters.

### `POST /api/v1/creator/color`

Colors are RGB or RGBA arrays in `0..1`.

```json
{"target":"eye","slot":0,"channel":1,"color":[0.2,0.15,0.1,1]}
```

Targets:

| Target | Additional fields |
| --- | --- |
| `hair` | `slot:0..3`, `field:base|start|end|outline|gloss|shadow` |
| `eye` | `slot:0..1`, `channel:0..2` |
| `eyebrow` | no meaningful slot/channel requirement |
| `clothes` | `slot`, `channel`; available channel count depends on the selected item |
| `accessory` | `slot`, `channel`; available channel count depends on the selected item |

### `POST /api/v1/creator/hair-bundle`

```json
{
  "part":1,
  "index":0,
  "moveRate":[0.5,0.25,0.5],
  "rotRate":[0.5,0.5,0.5]
}
```

`part` is hair slot `0..3`. `index` must be present in that style's `bundles`
from `creator/details`. Rates are normalized `0..1`. Supply at least one of
`moveRate` or `rotRate`. Use this endpoint to fit bangs and movable hair pieces
to the face instead of accepting the default placement blindly.

### `POST /api/v1/creator/skin`

```json
{"mainColor":[0.975,0.915,0.895,1],"shineId":5,"shinePower":0.5,"sunburnUpId":0,"sunburnDownId":0,"moleId":0}
```

All fields are optional; every supplied field is validated before any is applied.
The endpoint raises the body/face texture update flags and rebuilds both
textures. `creator/details.skin` reads the same values back. Sunburn ID `0` and
mole ID `0` mean none (as on the user's own cards).

### `POST /api/v1/creator/hair-flags`

```json
{"slot":1,"useMesh":false,"useInner":false}
```

Turns hair mesh (streak) and inner color on or off per hair slot. Colors use
`creator/color` with `target:"hair"` and `field:"mesh"` or `field:"inner"`.
Hair settings are per coordinate; apply them to each coordinate.

### `POST /api/v1/creator/accessory`

```json
{"slot":0,"category":"ao_hair","id":0,"parent":0}
```

`slot` addresses the current coordinate's accessory array. `category` must be a
valid `ao_*` enum category, the ID must exist, and `parent` must be a valid game
parent enum value. This endpoint currently selects an accessory and its parent;
it does not expose accessory transform adjustment.

## Detailed values (0.9.0)

### `GET /api/v1/creator/params`, `POST /api/v1/creator/params`

Reads or sets maker values that have no dedicated endpoint: pupil and iris size and
position, iris gradient, eye highlights, the double-eyelid line, eyebrow width and
thickness, white of the eye, nose and facial shading, snaggletooth, bang
transparency, blush position/rotation/size, face and body paints, body softness and
bust weight, skin highlight/shadow/tan colours, nipples and pubic hair, nails, hair
gloss and optional hair pieces, clothes pattern layout, emblems, sleeves, hidden
parts and paints, accessory visibility, sway and FK bones, the futanari flag, and
rendering (toon ramp, shadow depth, line width).

`GET` (or `POST` without `values`) returns every value. Face and body values carry
`increase` and `decrease` (in Japanese, the same text as `creator/shape-guide`); IDs
and colours carry `meaning`. Four directions (eyelid-line rotation, highlight height and tilt, blush
rotation) are marked "not confirmed".

```json
{"values":{"pupilWidth":0.6,"pupilHeight":0.62},"eye":0}
```

Scope selectors: `eye` (0 or 1, default both), `highlight` (index), `part` (hair 0
back, 1 front, 2 side, 3 option), `slot` (clothes or accessory), `channel` (clothes
colour), `foot` (true for toenails). `creator/export` includes these values.
Every value is checked before anything changes; if the game rejects one while
applying, all values the request touched, including that one, are put back.
The voice pitch follows `voiceRate` in `creator/profile`.

### `GET /api/v1/creator/shape-guide`

Lists the maker's face and body sliders by menu (顔 / 体) and tab, in the maker's own
order and with the maker's Japanese labels. Each slider gives `api` (`shapes.face`,
`shapes.body` for `creator/shapes`, or `params` for `creator/params`), `name`, `select`
(scope selectors to send with `creator/params`, such as `{"highlight":1}` or
`{"foot":true}`), and `increase` / `decrease` in Japanese. Tabs without sliders say
where their values are set; `hidden` lists values the maker does not show. Directions
that could not be checked say 「向きは未確認」.

### `GET /api/v1/creator/freeze`, `POST /api/v1/creator/freeze`

```json
{"blink":false,"eyeMovement":false,"motion":false}
```

`false` stops blinking, small eye movements or the body animation; `true` puts back
the state they had before they were stopped (kept separately for each character). All fields are optional; the response shows the current state.

## Part kits (0.6.0)

A character can be built from scratch with the individual parameters. Part kits
can also be used: a kit holds one region (`outline`, `eyes`, `brows`, `nose`,
`mouth`, `hair`) and can be applied to the open character. Kits can be cut out
region by region from the open character or from a reference card. Hair kits hold
the style only, not colors.

Kits are stored in `BepInEx/config/amanatsu.ai-extension/kits/<region>/<name>.json`,
with an optional `<name>.png` thumbnail beside each file.

| Endpoint | Body | Result |
| --- | --- | --- |
| `GET /api/v1/creator/kits` | | every kit: `region`, `name`, `description`, `source`, `operations`, `thumbnail` path |
| `POST /api/v1/creator/kit-save` | `{"region","name","description"?,"overwrite"?}` | saves the region of the open character |
| `POST /api/v1/creator/kit-from-card` | `{"file","region","name","description"?,"coordinate"?,"overwrite"?}` | saves the region of a card without loading it (`file` as in `card?file=`) |
| `POST /api/v1/creator/kit-apply` | `{"region","name","exclude"?,"dryRun"?}` | applies the kit to the open character; `changes` lists each changed field with `before` and `after` |
| `POST /api/v1/creator/kit-delete` | `{"region","name"}` | deletes the kit and its thumbnail |

Names are 1-40 characters usable in a file name. An existing kit is replaced only
with `"overwrite":true`.

`kit-apply` options:

- `"dryRun":true` returns `changes` without applying anything.
- `"exclude"` keeps fields as they are. It takes the groups `shapes`, `parts` and
  `colors`, a shape label (`EyeW`), a field name (`eyelineColor`, `lipId`),
  `<target>Color` for colors set per target (`eyeColor`, `eyebrowColor`), or a hair
  kind (`hair_front`). An unknown name is rejected with the names the kit accepts.

```json
{"region":"eyes","name":"almond","exclude":["colors"],"dryRun":true}
```

`ai_api.py` wraps these with thumbnails:

```text
ai_api.py kits
ai_api.py kit-save eyes almond --description "long lashes, almond shape"
ai_api.py kit-from-card UserData/chara/female/AL_F_xxx.png mouth full-lips
ai_api.py kit-apply eyes almond
ai_api.py kit-apply eyes almond --exclude colors --dry-run
ai_api.py kit-try eyes almond try.png --exclude colors
```

`kit-try` captures the region before and after applying the kit, writes them side
by side (before left, after right) to the output file, prints the changes, and
restores the character.

## Batch and navigation endpoints (0.5.0)

### `POST /api/v1/navigate`

```json
{"target":"female-creator"}
```

Moves from the title screen to the female character creator one step per call
(`CharaCreation`, then `Female`) so each screen can load in between. Repeat
until the response has `"done":true`; other responses report `clicked` or
`waiting`. `ai_api.py open-creator` wraps the loop.

### `POST /api/v1/creator/shapes`

```json
{"face":{"EyeTilt":0.45,"MouthW":0.5},"body":{"Height":0.66,"BustSize":0.5}}
```

Sets any number of face and body shapes in one request. Keys are the labels from
`creator/details` (`faceLabels`, `bodyLabels`) or numeric indices. Every value is
validated before anything changes, and the model is refreshed once. The response
lists `before`/`after` for each value.

### `POST /api/v1/creator/hair-colors`

```json
{"slots":[0,1,2,3],"base":[0.86,0.8,0.68,1],"start":[0.7,0.64,0.52,1],"end":[0.95,0.92,0.84,1],"inner":[0.56,0.68,0.58,1],"useInner":true}
```

Applies any of `base`, `start`, `end`, `outline`, `gloss`, `shadow`, `mesh` and
`inner`, plus the `useMesh`/`useInner` flags, to the listed hair slots (all four
when `slots` is omitted) in one request. Hair belongs to the current coordinate;
apply it to each coordinate.

### `GET|POST /api/v1/creator/eye-lines`

```json
{"eyelineColor":[0.2,0.15,0.13,1],"eyelidColor":[1,1,1,1],"eyelineUpWeight":1}
```

Reads or sets the lash line color, the eyelid crease color and the upper lash
line weight. Omitted fields keep their value. `card?file=` and `export` include
these values.

### Saving with a fresh thumbnail — `ai_api.py save-card`

`save_card()` in `ai_api.py` (CLI: `ai_api.py save-card`) drives the maker's
own save screen (SystemMenu, CharaSave, `new-card`, `capture`, `save-card`) and
returns the new file name, confirmed by the file that appeared in
`UserData/chara/female`.

### Checking a saved card — `creator/verify-card`, `ai_api.py save-verify`

`POST /api/v1/creator/verify-card {"file"}` reads the card back from disk and
compares it with the open character (profile, face and body shapes, face parts and
colors, skin, and each coordinate's hair, clothes, accessories and makeup). The
result has `matches` and `mismatches` (`field`, `card`, `open`). The nickname is
not compared because cards do not store it.

`ai_api.py save-verify` saves a new card and checks it in one step:

```json
{"saved":"AL_F_xxx.png","matches":true,"mismatches":[]}
```

### Card lookup

`card?file=` also accepts a bare file name (`AL_F_xxx.png`) or a path relative to
`UserData/chara`, and the error message shows an example path.

## Extended creator endpoints (0.4.0)

### Card inspection without loading — `GET /api/v1/cards`, `GET /api/v1/card?file=`

Works in any scene. `cards` lists every PNG under `UserData/chara` (including
sub-folders) and `DefaultData/**/chara`. `card?file=<relative path>` reads that
card into a detached `HumanData` and returns profile, face/body shapes, face part
IDs, eye colors, skin, and per-coordinate hair (with bundles), clothes,
accessories (with parent and transforms), and makeup. Nothing in the scene
changes. Paths outside those two roots are rejected.

### `POST /api/v1/creator/reset`

Returns the open character to the maker's initial character (the one shown when
the maker opens) with the maker's own restore: face, body, skin, hair, clothes,
accessories and both coordinates. `{"keepProfile":true}` keeps the name, birthday,
personality, voice and blood type. The response has `before` and `after` names.

### `GET|POST /api/v1/creator/face-parts`

```json
{"eyebrow":3,"eyelineUp":11,"eyelineDown":2,"eyelid":12,"white":0,"nose":1,"lipLine":0,"detail":1,"eye":9,"pupil":0,"eyePreset":8,"eyePresetFlags":3}
```

Every ID is checked against its catalog (`mt_eyebrow`, `mt_eyeline_up`,
`mt_eyeline_down`, `mt_eyelid`, `mt_eye_white`, `mt_nose`, `mt_lipline`,
`mt_face_detail`, `mt_eye`, `mt_eyepipil`) before anything is applied.
`eyePreset` is an index into the game's pupil preset list (count in the GET
response); flags `1` = shape/IDs, `2` = colors, `3` = both. The preset list order
is **not** the order of the maker's thumbnails; choose by index and verify with a
capture. `eye`/`pupil` apply to both eyes.

### `GET|POST /api/v1/creator/makeup`

```json
{"eyeshadowId":1,"eyeshadowColor":[0.6,0.4,0.5,1],"cheekId":2,"cheekColor":[1,0.7,0.7,1],"cheekHighlightColor":[1,1,1,1],"lipId":1,"lipColor":[0.9,0.4,0.45,1],"lipHighlightColor":[1,1,1,1],"eyeGradColor":[0.3,0.2,0.5,1],"eyeHighlightColor":[1,0.95,0.8,1]}
```

Makeup belongs to the current coordinate. Eye gradation/highlight colors apply
to both eyes and all highlight slots.

### Accessories — transforms, default parent, clearing

`POST /api/v1/creator/accessory` now uses the item's default parent when `parent`
is omitted. `creator/details.accessories[]` reports `category`, `parent` and
`move` (the maker's adjustment tabs 01 and 02, each with `pos`/`rot`/`scl`) and, in
`creator/details`, `tabs`: 1 for a one-piece accessory, 2 for a two-piece one such as
cat ears, where tab 1 moves the first piece and tab 2 the second.

```json
{"slot":0,"tab":1,"pos":[-3,-2,0],"rot":[0,0,45],"scl":[1.6,1.6,1.6]}
```

`POST /api/v1/creator/accessory-move` sets absolute values in the maker's own
units through `SetAccessoryPos/Rot/Scl`; `"reset":true` first restores the tab's
defaults. `tab` is 1 or 2 (`correct` 0 or 1 is the same). Asking for tab 2 on a
one-piece accessory is an error unless the values are the defaults. `POST /api/v1/creator/accessory-clear {"slot":1}` empties a slot
(sets `ao_none`, which also clears the saved data).

### `POST /api/v1/creator/clothes-pattern`

```json
{"slot":2,"channel":0,"pattern":0,"patternColor":[1,1,1,1],"gloss":0.1,"metallic":0}
```

Sets a clothes channel's pattern (`mt_pattern` id, `0` = none), pattern color,
gloss and metallic, then rebuilds that garment's texture. Patterns stay on the
slot when the item is changed, so clear them when switching garments.
`creator/details` and `card?file=` report them; export includes them.

### Coordinates

`POST /api/v1/creator/coordinate {"type":0|1}` switches through the maker's own
toggle so the UI stays in sync. `POST /api/v1/creator/coordinate-copy
{"from":0,"to":1,"parts":["hair","accessory","clothes","makeup"]}` copies data
between coordinates and rebuilds the live model if the destination is current.

### Export / import — `GET /api/v1/creator/export`, `POST /api/v1/creator/import`

Export returns an ordered list of `{"path","body"}` operations that rebuild the
loaded character through the public endpoints (profile, all shapes, face parts,
eye/eyebrow colors, skin, and for each coordinate: hair ids/colors/flags/bundles,
clothes ids/colors, accessories with colors and transforms, makeup). Import
replays such a list: `{"operations":[...],"stopOnError":true}`; each step keeps
its own validation, and the response reports failures. The CLI wraps both:
`ai_api.py export file.json`, `ai_api.py import file.json`.

### Capture expression and pose

`POST /api/v1/capture` accepts
`{"region":"bust","view":"front","expression":{"eyebrow":1,"eyes":0,"mouth":1},"pose":1}`.
`pose` is an index or state name from `GET /api/v1/creator/poses` (the maker's
own pose list; unknown poses return 400). A new pose is applied first and the
framing is measured 30 frames later, after the skeleton has settled. Expression
patterns are range-checked and restored after the capture; the pose is left as
set (capture again with `"pose":0` to return to the default stance).

## UI automation

### `GET /api/v1/ui/buttons`

Returns active buttons with `id`, `name`, hierarchy `path`, label `text`,
`interactable`, and `visible`.

### `POST /api/v1/ui/click`

```json
{"id":-12345}
```

Refresh the button list immediately before clicking. Match by several fields
(`name`, `path`, `text`, visibility), because names such as `Prev`, `Next`, and
`btnTemp(Clone)` are duplicated. IDs change after scene and UI reconstruction.

### `GET /api/v1/ui/toggles`

Returns active toggles with the same identity fields plus `isOn`.

### `POST /api/v1/ui/toggle`

```json
{"id":-12345,"value":true}
```

Only visible, interactable toggles can be changed. Read `isOn` first and avoid
using a toggle as a blind click.

### `GET /api/v1/ui/input-sliders`

Returns the active maker numeric controls with `id`, hierarchy `path`, displayed
`text`, normalized `value`, limits, and `wholeNumbers`.

### `POST /api/v1/ui/input-slider`

```json
{"id":-12345,"text":"125"}
```

This invokes the maker's real numeric-entry `onEndEdit` path. The displayed text
is an integer percentage even though Unity stores normalized floating-point
values. Refresh the list after opening the relevant maker submenu.

## Camera and screenshots

### `GET /api/v1/screenshot`

Returns the current screen as `pngBase64`, including visible UI.

### `GET /api/v1/camera/landmarks`

Returns current world-space skeleton bone positions and face-renderer bounds.

### `POST /api/v1/camera/frame`

```json
{"region":"face","view":"left"}
```

Regions: `face`, `bust`, `upper_body`, `waist`, `legs`, `full`. Views: `front`,
`back`, `left`, `right`, `top`, `bottom`. Framing is computed from the current
deformed face mesh and skeleton, so face position follows character height.
It returns the calculated center, size, distance, FOV, measured height, and
anchors but does not return an image.

### `POST /api/v1/capture`

Uses the same body as `camera/frame`, applies the framing, disables blinking,
waits three frames, captures a PNG, and restores the blink flag. Response:

```json
{
  "framing": {"region":"face","view":"front","source":"..."},
  "image": {"width":1600,"height":900,"pngBase64":"..."}
}
```

### `GET|POST /api/v1/creator/camera`

GET returns maker camera `position`, `rotation`, `direction`, and `fov`. POST
sets any supplied fields. Vectors contain three finite numbers; FOV is `5..100`.

## Character card save and load

### Direct safe save

`POST /api/v1/creator/save` accepts a filename only:

```json
{"file":"AL_F_example.png"}
```

Directories are rejected and existing cards are never overwritten. In some
runtime states, the game's direct `SaveCharaFile` method emits only a serialized
payload, without a PNG thumbnail. The extension detects this, removes that
invalid new file, and returns `501`. Use the native capture flow below for a
complete PNG card.

### Native UI workflow

`POST /api/v1/creator/native-ui` accepts:

```json
{"command":"new-card"}
{"command":"capture"}
{"command":"save-card"}
{"command":"back"}
{"command":"load-card","index":0}
```

Recommended save sequence:

1. Read and preserve the current character when necessary.
2. Open `SystemMenu`, then `CharaSave` through refreshed visible toggles.
3. Send `new-card` and wait for the capture screen.
4. Send `capture` and wait for the save button.
5. Send `save-card`.
6. Compare the filenames in `UserData/chara/female` before and after. Expect one
   new file. Do not infer success from the button response alone.

Recommended load sequence:

1. Open the native character load/save UI so its file list is initialized.
2. Call `GET /api/v1/creator/files`.
3. Select the exact `FileName`; do not assume list order.
4. Call `native-ui` with `{"command":"load-card","index":entry.index}`.
5. Wait for model reconstruction, then read `character` and `creator/details`.
6. Capture front and side images and verify both coordinates if both matter.

`POST /api/v1/creator/load` always returns `501`; direct load is disabled because
safe scene lifecycle handling is not implemented.

## Coordinates

The maker exposes `01_Swim` and `02_Pajama` as UI toggles. The API returns the
active numeric coordinate in `creator/details.coordinate`. Character choices,
colors, hair settings, and accessories modify the current coordinate and are
copied into the underlying coordinate data after API mutations.

To copy a complete coordinate, use the native `btnCoordeAllCopy` flow and confirm
the destination. Re-read the current coordinate and clothing IDs afterward.

## Self shadow and slider unlock

### `GET|POST /api/v1/self-shadow`

POST body: `{"enabled":false}`. Requires `Amanatsu Self Shadow Toggle`. A `200`
response reports `available`, `requested`, and actual `enabled`.

### `GET|POST /api/v1/slider-unlock`

POST body: `{"enabled":true}`. Requires `Amanatsu Slider and Clear Unlocker`.
This controls extended maker ranges and the supporting runtime range behavior.

## Character parameters and realtime outfits

### Target selection

Mutation requests require exactly one of `uniqueId`, `listIndex`, or `name`.
Prefer `uniqueId` after reading current state. Names can be ambiguous and list
indices can change.

### `GET /api/v1/favorability`

Returns the companion parameter-control state and character list.

### `POST /api/v1/favorability`

Supports `point`, `delta`, `level`, and `isMaxLevel`. It is retained as a
compatibility endpoint for favorability changes.

### `GET|POST /api/v1/character-parameters`

Select `parameter` from `favorability`, `inclusiveness`, `proactivity`, or
`curiosity`. Mutations may include `point`, `delta`, `level`, `isMaxLevel`,
`gaugeStageIndex`, `gaugeStage` (`0..6`), `nightEventCount`, `hCount`,
`massageCount`, `latePoint`, `cost`, and `unlockScenes`.

### `GET|POST /api/v1/realtime-outfit`

POST can set `coordinateType` (`swimsuit` or `afterBath`), a clothing state, or
accessory visibility. Clothing parts are `top`, `bottom`, `bra`, `shorts`,
`gloves`, `pantyhose`, `socks`, and `shoes`. States are `clothing`,
`halfUndress`, and `naked`.

Examples:

```json
{"uniqueId":123,"coordinateType":"afterBath"}
```

```json
{"uniqueId":123,"clothesPart":"top","clothesState":"halfUndress"}
```

```json
{"uniqueId":123,"accessorySlot":0,"accessoryVisible":false}
```

## Operation log

### `GET /api/v1/logs?limit=100`

Returns recent API mutations and observed state transitions. Use it when a visual
result differs from a successful response. Keep the limit modest during routine
work and preserve relevant entries with the character's verification artifacts.

## Recommended AI character workflow

1. Check `state` and stop if `characterEditorReady` is false.
2. Read `character`, `creator/details`, and diagnostics.
3. Establish whether the open character contains user work before any mutation.
4. Write the intended adult character concept and visual constraints first.
   `ai_api.py open-creator` opens the creator from the title screen.
5. Study references with `GET /api/v1/card?file=` instead of loading them.
6. Start from `creator/reset`, query catalogs and choose only existing parts
   (`face-parts`, `makeup`, `character/choice`, `creator/accessory`).
7. Apply type-1 head, face, full-body proportions, hair, colors, and clothes;
   place accessories with `accessory-move`.
8. Inspect face front/left/right, bust, and full front/back captures.
9. Adjust hair bundles after observing facial occlusion and profile fit.
10. Build and verify both coordinates (`coordinate`, `coordinate-copy`).
11. Keep a `creator/export` snapshot, save to a new card (`ai_api.py save-card`),
    read that exact file back with `card?file=` and verify numeric and visual state.

Do not treat differentiation as a reason to select unsuitable parts or degrade
facial quality. Build identity through proportion, hair fit, eyes, palette,
accessories, and clothing as a coherent whole.

