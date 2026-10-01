# Amanatsu AI Extension API specification

Version: `v1`  
Plugin version inspected: `0.10.0`  
Default base URL: `http://127.0.0.1:38427`

This document describes the local HTTP API exposed by `Amanatsu AI Extension`.
It is written for another AI agent or automation client that needs to inspect and
operate Amanatsu Location without guessing about the current UI state.

## Safety rules

1. Never terminate `AmanatsuLocation.exe` unless the user explicitly authorizes
   that termination in the current turn. `ai_api.py dev-cycle` and `stop-game`
   close the game.
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
seconds. `POST /api/v1/debug/wait` is the exception: it answers when its
conditions hold or after its own `timeoutMs`.

`POST /api/v1/capture` waits three rendered frames before taking the image. While
a capture is pending, other POST requests (except `debug/wait`) return `409`. Send mutations
sequentially and wait for each response.

## Common status codes

| Status | Meaning |
| --- | --- |
| `200` | Request completed. Inspect returned `after`, `enabled`, or saved state. |
| `400` | Invalid JSON, field, range, category, or choice ID. |
| `401` | Missing or incorrect bearer token. |
| `403` | Request was not local or contained an `Origin` header. |
| `404` | Unknown endpoint, stale UI instance ID, or no button matching a selector. |
| `408` | `debug/wait` conditions did not hold before `timeoutMs`. |
| `405` | Unsupported HTTP method. |
| `409` | Wrong scene/UI state, hidden control, disabled control, or capture already in progress. |
| `413` | Request exceeded 4 MiB (4,194,304 bytes). |
| `422` | The file is not a readable character card. |
| `501` | Direct card loading is disabled, or direct saving failed to produce a valid PNG card. |
| `503` | Main thread timeout, or the screen could not be captured. |

Errors use `{"error":"message"}`.

## Core state endpoints

### `GET /api/v1/schema`

Returns the API version, the plugin version, a compact one-line list of endpoints (`endpoints`), and the full definitions with JSON Schema bodies (`details`, `components`). All of it comes from `api_endpoints.json`, the same file `openapi.yaml` is generated from.

Every endpoint has two attributes, also written to `openapi.yaml` as `x-mutates` and `x-scene`:

| Attribute | Values |
| --- | --- |
| `mutates` | `true`: the call changes game state or files. `false`: it only reads and can be called at any time. |
| `scene` | `creator`: needs the character creator open (`characterEditorReady: true`). `any`: works on every screen. |

Each line of `endpoints` ends with `[read-only]`, `[mutates]`, `[creator]` or `[mutates, creator]`.

`openapi.yaml` lists, for each endpoint, every status it can answer with its
meaning. Every endpoint can answer `400`, `401`, `403`, `413` and `503` (main
thread timeout); `scene: creator` endpoints `409` when the creator is not
open; POST endpoints `409` while a capture is in progress. Other statuses come
from the endpoint's `responses` in `api_endpoints.json`, either as a
description or as `{"description", "schema"}`. Error responses have the
`Error` body (`{"error": ...}`, sometimes with more fields), except
`debug/wait` `408` (`WaitResult`) and `creator/kit-apply` `409`
(`KitApplyResult`). Where `responses` gives a `200` schema, `openapi.yaml`
describes the success body too (`debug/wait`: `WaitResult`;
`creator/kit-apply`: `KitApplyResult`, or `KitDryRunResult` with `dryRun`).
The build stops when the plugin's code answers a status that no endpoint
lists. A request body marked not required (`bodyOptional` in
`api_endpoints.json`) may be left out; it is taken as `{}`.

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

`region` is `body` or `face`. `value` may be any finite number. The API
sets `HumanData.SkipRangeCheck=true`, applies the shape, refreshes the model, and
returns `before`, `requested`, `accepted`, `after`, and `skipRangeCheck`.

`0..1` is the maker's normal range. The API sets no range of its own: a value
outside it is written as given. Whether the game shows such a value depends on
the installation; read `after` and capture the result to judge it.

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

Returns `{id,name}` choices from the game's live category table, sorted by ID.
Parts that other mods add to that table are included. Query a catalog before
selecting an ID.

| Parameter | Meaning |
| --- | --- |
| `offset` | Skip this many choices (default 0). |
| `limit` | Return at most this many (default 500). |

The response gives `total` (all choices in the category) and `more` (`true`
when choices after this page remain). Common categories include:

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

By instance ID from `ui/buttons`:

```json
{"id":-12345}
```

Or by properties that stay the same between runs:

```json
{"name":"Female"}
{"text":"キャラクリエイト"}
{"path":"CharaCreationMenu/Female"}
{"name":"Back","path":"GamePlayMenu/Back"}
```

A selector matches visible buttons that have every given property. `name` is
the GameObject name, `text` the label, `path` the whole object path or its
trailing segments. When several buttons match, the response is `409` with up to
20 `candidates`; add `index` (0-based, in `ui/buttons` order) or a longer
`path`. A disabled or hidden button returns `409`.

IDs change after scene and UI reconstruction; refresh the button list
immediately before clicking by ID.

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

## Operation log

### `GET /api/v1/logs?limit=100`

Returns recent API mutations and observed state transitions. Use it when a visual
result differs from a successful response. Keep the limit modest during routine
work and preserve relevant entries with the character's verification artifacts.

## Extension endpoints (0.10.0)

Other BepInEx plugins can publish their own endpoints through this API. They
appear at `/api/v1/ext/{plugin guid}/{path}`, in `GET schema` (with
`"extension": "<guid>"`), in `GET extensions` and in the MCP tool `call_api`.
They are not in `openapi.yaml`, which lists only the endpoints of this plugin.

### `GET /api/v1/extensions`

Each registering plugin (`owner`) with its endpoints, and `rejected`: every
registration that was refused, with the reason.

### Calling an extension endpoint

Like any other endpoint, with the bearer token:

```text
python ModSource/AiExtension/ai_api.py call GET extensions
python ModSource/AiExtension/ai_api.py call POST ext/my.plugin.guid/speed --body "{\"value\":2}"
```

| Status | Meaning |
| --- | --- |
| `404` | No plugin registered this path. |
| `405` | The path exists with the other method. |
| `409` | The endpoint has `scene: creator` and the character creator is not open. |
| `500` | The plugin's handler threw, or returned no valid status or invalid JSON; `extension` names the plugin. |

Other statuses and the body come from the plugin.

### Publishing endpoints from a plugin

1. Add `ModSource/AiExtension/sdk/AiExtensionBridge.cs` to the plugin, for
   example in its `.csproj`:

   ```xml
   <Compile Include="../AiExtension/sdk/AiExtensionBridge.cs" Link="AiExtensionBridge.cs" />
   ```

   The plugin needs no reference to `Amanatsu.AiExtension.dll` and no load-order
   dependency. Registrations made before AI Extension loads are kept and taken
   when it loads. Without AI Extension, registering does nothing.

2. Register each endpoint in `Load()`:

   ```csharp
   using Amanatsu.AiExtension.Sdk;

   AiExtensionBridge.Register("my.plugin.guid",
       """{"method":"POST","path":"speed","summary":"Set the walking speed","mutates":true,"scene":"any",
           "body":{"type":"object","properties":{"value":{"type":"number"}},"required":["value"]}}""",
       (method, query, body) => Tuple.Create(200, "{\"ok\":true}"));
   ```

The metadata is one endpoint in the form of `api_endpoints.json`:

| Field | Required | Value |
| --- | --- | --- |
| `method` | yes | `GET` or `POST` |
| `path` | yes | Lowercase segments of `a-z`, `0-9` and `-`, separated by `/`. Published under `ext/{owner}/`. |
| `summary` | yes | One line describing the endpoint. |
| `mutates` | yes | `true` if the call changes game state or files. |
| `scene` | yes | `creator` (AI Extension answers `409` unless the creator is open) or `any`. |
| `query`, `body` | no | JSON Schemas, shown in `GET schema`. |

The owner should be the plugin's BepInEx GUID (letters, digits, `.`, `_`, `-`).
A method and path can be registered once.

The handler receives the HTTP method, the query string (with its leading `?`,
or empty) and the request body (empty for GET). It runs on the Unity main
thread, inside the same request queue, ten-second timeout and operation log
as the built-in endpoints. It returns the HTTP status and a JSON response body.

## Mod development (0.10.0)

These endpoints only read, except that `debug/wait` holds its request open.

### `GET /api/v1/debug/plugins`

Every plugin BepInEx loaded: `guid`, `name`, `version`, DLL `location`,
`fileWriteUtc` and `loaded`, plus the game's `gameProcessId` and `startedUtc`.
Use it to confirm that a new build is the one running.

### `GET /api/v1/debug/log?since=&limit=&level=&source=&contains=`

The BepInEx log of every plugin and Unity, kept in memory (the last 5000
lines). Lines written before this plugin loaded are read from
`BepInEx/LogOutput.log` and have `fromDisk: true` and no `utc`.

| Parameter | Meaning |
| --- | --- |
| `since` | Return lines after this cursor. Pass the previous response's `next`. |
| `limit` | 1..1000, default 200. `more: true` when more lines match. |
| `level` | `fatal`, `error`, `warning`, `message`, `info` or `debug`; keeps that level and more severe. |
| `source` | Substring of the log source name. |
| `contains` | Substring of the message. |

Each entry has `seq`, `utc`, `level`, `source`, `message` and `fromDisk`.
`oldest` is the first cursor still in memory; if it is greater than your
`since`, lines were dropped.

`GET /api/v1/logs` is a different log: the API's own operation record.

### `GET /api/v1/debug/harmony?owner=&target=`

Every method patched through Harmony with its `prefixes`, `postfixes`,
`transpilers` and `finalizers`. Each patch gives its Harmony `owner`, patch
`method`, `assembly` and `priority`. `shared: true` marks a method patched by
more than one owner. `owner` filters by owner ID or patching assembly name
(plugins that use `CreateAndPatchAll` have an owner of the form
`harmony-auto-<guid>`); `target` filters by `Type.Method`.

### `POST /api/v1/debug/wait`

Waits until every given condition holds. Conditions are checked once per frame.

```json
{"scene":"CustomScene","creatorReady":true,"timeoutMs":60000}
{"button":{"name":"Female"},"timeoutMs":5000}
{"log":{"level":"error"},"timeoutMs":30000}
```

| Field | Condition |
| --- | --- |
| `scene` | Active scene name, for example `Title`, `CustomScene`, `map000`. |
| `humanCountAtLeast` | At least this many characters are loaded. |
| `creatorReady` | The character creator is (or is not) open. |
| `button` | A visible, clickable button matches this selector (same as `ui/click`). |
| `log` | A log line with `contains`, `source` and/or `level` was written after the wait started, or after `since`. |
| `event` | A `debug/events` entry of `type` (one name or a list) and/or whose data contains `contains` was added after the wait started, or after `since`. |
| `timeoutMs` | 0..120000, default 10000. |

`200` with `satisfied: true`, or `408` with `satisfied: false`. Both give
`elapsedMs`, `frames`, the current `state` (scene, humanCount, creatorReady),
the matching `log` line and `event`, and the current `logCursor` and
`eventCursor`.

### Scenes and objects

Instance IDs (`id`) of objects and components change every time the game
starts and when objects are rebuilt. Look them up again with `debug/tree`,
`debug/find` or `debug/object` in each run; to name an object across runs, use
its `path`.

#### `GET /api/v1/debug/scenes`

Loaded scenes with `name`, `handle`, `buildIndex`, `isLoaded`, `active` and
`rootCount`, followed by `DontDestroyOnLoad` and `(hidden)`. `(hidden)` holds
runtime objects that belong to no scene, such as `BepInEx_Manager`, which
carries the components that plugins add. AI Extension keeps one empty object,
`AmanatsuAiExtension.DontDestroyOnLoadMarker`, in `DontDestroyOnLoad` to find
that scene.

#### `GET /api/v1/debug/tree?scene=&id=&path=&depth=&filter=&components=&offset=&limit=`

The hierarchy, depth first. Without `scene`, `id` or `path` it covers every
scene in `debug/scenes`.

| Parameter | Meaning |
| --- | --- |
| `scene` | One scene name from `debug/scenes`. |
| `id`, `path` | Start at this object instead (`path` as in `ui/buttons`, e.g. `CustomScene/UI/Root`). |
| `depth` | Levels below the start, 0..64, default 3. |
| `filter` | Only objects whose name or a component type contains this text. Each result then carries its `path`. |
| `components` | `false` leaves out component type names. |
| `offset`, `limit` | Paging; `limit` 1..2000, default 200. Pass `next` as the following `offset`. |

Each node has `id`, `name`, `depth`, `active` (its own flag),
`activeInHierarchy`, `childCount` and `components` (short type names).

#### `GET /api/v1/debug/object?id=` or `?path=`

One GameObject (`id` may also be one of its components): `path`, `scene`,
`active`, `activeInHierarchy`, `layer`, `tag`, `parent`, `transform` (local
position, rotation in degrees and scale, and the world position, rotation and
scale), `components` with their `id`, full `type` and `enabled`, and up to 500
`children`.

#### `GET /api/v1/debug/component?id=&path=&depth=&getters=`

The values of one component (`id` from `debug/object`).

- IL2CPP fields and the managed fields of plugin components are read directly.
- Property getters are not called, because some change state when read (for
  example `Renderer.material` creates a material copy). Only these getters
  are read, and only while `getters` is `true` (the default): `enabled`,
  `isActiveAndEnabled`, `interactable`, `isOn`, `value`, `minValue`,
  `maxValue`, `wholeNumbers`, `text`, `color`, `alpha`, `sprite`,
  `sharedMaterial`, `sharedMaterials`, `sharedMesh`, `sortingOrder`,
  `raycastTarget`, `fontSize`, `isPlaying`, `isVisible`, `fieldOfView`,
  `orthographic`, `intensity`, `range`, `rootBone`, `bones`, `bounds`,
  `localBounds`, `shadowCastingMode`, `receiveShadows`, `updateWhenOffscreen`,
  `runtimeAnimatorController`, `speed`, `nearClipPlane`, `farClipPlane`,
  `depth`, `cullingMask`, `shadows`, `renderMode`, `sortingLayerName`,
  `pixelRect`, `anchoredPosition`, `sizeDelta`, `pivot`, `anchorMin`,
  `anchorMax`.
- A referenced Unity object is given as `{"ref": id, "type", "name"}` and is
  not opened. Read it with `debug/component` or `debug/object` and its id.
- Arrays and lists give `count` and up to 20 `items`.
- `depth` (0..3, default 1) is how many levels of nested objects are opened.
- `path` opens a nested value first: field names joined with `.`, and `[n]`
  for an array or list element, for example
  `m_OnClick.m_PersistentCalls.m_Calls[0]`.

`value` is `{"type", "fields": {...}}` for objects; numbers, strings, enums
(as names), vectors and colors (as arrays), and `Bounds` (`center`, `size`)
are given directly.

#### `GET /api/v1/debug/find?type=&name=&inactive=&limit=`

GameObjects in every scene in `debug/scenes` with a component whose type
equals `type` (full name such as `UnityEngine.UI.Button`, or the short name
`Button`) and/or whose name contains `name`. Each result gives `id`, `name`,
`path`, `scene`, `activeInHierarchy` and the matching `components` with their
ids. `inactive=false` leaves out inactive objects. `limit` 1..500, default 50.

#### `GET /api/v1/debug/types?q=` or `?type=`

`q` (at least 2 characters) searches the full names of every loaded type: the
game's interop types and the plugins' own. `type` with an exact full name
lists that type's `fields` (IL2CPP fields), `properties` (with `safeToRead`
for the getters `debug/component` reads), `methods` with their signatures, and
`enumValues` for enums. This endpoint does not wait for the main thread.

### Changes over time

#### `GET /api/v1/debug/events?since=&type=&contains=&limit=`

Everything below in the order it happened, kept in memory (the last 5000).
Each event has `seq`, `utc`, `frame` (-1 for log lines from other threads),
`type` and `data`.

| Type | When | Data |
| --- | --- | --- |
| `scene_loaded`, `scene_unloaded` | A scene finished loading or was unloaded. Checked every frame. | `scene`, `handle` |
| `active_scene` | The active scene changed. | `scene`, `handle` |
| `humans` | The number of loaded characters changed. Checked every 0.5 s. | `before`, `after` |
| `creator` | The character creator became ready or closed. | `ready` |
| `root_added`, `root_removed` | A top-level object appeared in or left a scene (including DontDestroyOnLoad). | `id`, `name`, `scene` |
| `log` | A warning, error or fatal line was logged by any plugin or Unity. | `seq` (as in `debug/log`), `level`, `source`, `message` |
| `api` | A POST request to this API finished. | `method`, `path`, `status`, `durationMs` |
| `watch` | A watched value changed. | `watch`, `name`, `before`, `after` |
| `watch_lost` | A watched object or field no longer exists; the watch is removed. | `watch`, `name`, `id`, `path`, `property` |

`type` filters by a comma-separated list; `contains` keeps events whose data,
as JSON, contains the text. `limit` 1..1000, default 200. Pass `next` as the
following `since`; `oldest` is the first `seq` still kept.

#### `POST /api/v1/debug/watch`

Reads one value every 0.5 s and adds a `watch` event when it changes.

```json
{"id":-53398,"path":"m_IsOn","name":"system menu"}
{"objectPath":"CustomScene/UI/Root","property":"activeInHierarchy"}
{"id":-24894,"property":"position"}
```

| Field | Meaning |
| --- | --- |
| `id` + `path` | A component id and a field path, read as in `debug/component`. Without `path`, the component itself (its reference). |
| `id` or `objectPath` + `property` | A GameObject and one of `activeSelf`, `activeInHierarchy`, `position`, `localPosition`, `localEulerAngles`, `localScale` (rounded to 4 decimals), `name`, `childCount`. |
| `depth` | 0..2, default 0: how far a nested value is opened before comparing. |
| `name` | A label repeated in the events. |

The response gives the `watch` number, the current `value` and `cursor`, the
`since` for reading only the events after it. `GET debug/watches` lists the
watches with their last value and number of changes. `POST debug/unwatch`
with `{"watch":1}` or `{"all":true}` stops them.

#### `POST /api/v1/debug/snapshot`, `GET /api/v1/debug/diff`

A snapshot keeps, for each object of a subtree, its active flag, local
position, rotation and scale, and its component types, as flat keys:

```text
CustomScene/UI/Root/Cvs_CategoryView/Top[1] :: active
CustomScene/UI/Root/Cvs_CategoryView/Top[1] :: localPosition
CustomScene/UI/Root/Cvs_CategoryView/Top[1] :: components
CustomScene/UI/Root/Cvs_CategoryView/Top[1] :: Toggle.m_IsOn
```

Siblings with the same name are told apart by order: the first has no suffix,
the second `[1]`, the third `[2]`, and so on. The same applies to several
components of one type. Fields are kept
only for the component types named in `fieldTypes`, because many components
fill caches as they render and would otherwise show as changes.

| Field | Meaning |
| --- | --- |
| `id`, `path` | Root object. Without them, `scene` (one scene from `debug/scenes`, including `DontDestroyOnLoad` and `(hidden)`) or every scene. |
| `depth` | Levels below the root, 0..64, default 64. |
| `fieldTypes` | Comma-separated component types (short or full names) whose fields are kept, e.g. `Toggle,Button,TextMeshProUGUI`; `*` keeps every component's fields. |
| `limit` | Objects, 1..20000, default 2000. `truncated` tells whether it was reached. |
| `name` | Snapshot name (default `snap1`, `snap2`, ...). A snapshot of the same name is replaced. |

`GET debug/diff?from=<name>` captures the same subtree again with the same
settings and compares; `to=<name>` compares with another snapshot instead.
The response gives `counts` and up to `limit` (default 500) `changed`
(`key`, `before`, `after`), `added` and `removed` entries; `contains` keeps
only keys containing the text. `GET debug/snapshots` lists the snapshots;
`POST debug/snapshot-delete` with `{"snapshot":"name"}` or `{"all":true}`
drops them. Snapshots are kept in memory until the game exits.

What a click changes:

```text
POST debug/snapshot {"path":"CustomScene/UI/Root","name":"before","fieldTypes":"Toggle,Button"}
POST ui/click {"name":"..."}
GET  debug/diff?from=before
```

### Build, deploy and restart — `ai_api.py dev-cycle`

```text
python ModSource/AiExtension/ai_api.py dev-cycle
python ModSource/AiExtension/ai_api.py dev-cycle AiExtension MyPlugin
python ModSource/AiExtension/ai_api.py dev-cycle --no-build
python ModSource/AiExtension/ai_api.py dev-cycle --ready-scene ""
```

In order:

1. Builds each named folder under `ModSource` with `dotnet build -c Release`
   (default `AiExtension`). Stops with the compiler errors if a build fails.
2. Closes the game whose API answers on `--port` (by its process id from
   `debug/plugins`). When the API does not answer, every
   `AmanatsuLocation.exe` is closed. It is forced after 5 seconds.
3. Copies each DLL over its installed copy under `BepInEx/plugins` (or into
   `BepInEx/plugins/SELF` if none is installed). Stops if a DLL is installed
   twice.
4. Starts `AmanatsuLocation.exe`, waits for `GET state`, then for the
   `--ready-scene` (default `Title`).
5. Reports, for each DLL, whether the plugin at that location loaded and its
   version, and lists every warning and error logged during startup
   (`startupProblems`). `logCursor` is the `since` value for reading only
   later lines.

`ok` is `true`, and the exit code 0, when every DLL loaded and the game
reached the ready scene within 120 seconds (`sceneReached`). `--no-restart` builds and deploys only and
requires the game to be closed. `stop-game` and `start-game` run steps 2 and 4
alone.

### Other CLI commands

```text
python ModSource/AiExtension/ai_api.py plugins
python ModSource/AiExtension/ai_api.py log --since 619 --level warning
python ModSource/AiExtension/ai_api.py harmony --owner my.plugin.guid
python ModSource/AiExtension/ai_api.py wait --scene Title --timeout 60
python ModSource/AiExtension/ai_api.py wait --button-name Female --timeout 5
python ModSource/AiExtension/ai_api.py click-by --text キャラクリエイト
python ModSource/AiExtension/ai_api.py click-by --name Back --path GamePlayMenu/Back
python ModSource/AiExtension/ai_api.py call GET debug/scenes
python ModSource/AiExtension/ai_api.py call GET "debug/tree?scene=Title&depth=2"
python ModSource/AiExtension/ai_api.py call GET "debug/find?type=Button&name=Female"
python ModSource/AiExtension/ai_api.py call GET "debug/object?path=Title/Canvas"
python ModSource/AiExtension/ai_api.py call GET "debug/component?id=41900&path=m_OnClick.m_PersistentCalls"
python ModSource/AiExtension/ai_api.py call GET "debug/types?q=HumanCustom"
```

Title screen to the female character creator with selectors only:

```text
python ModSource/AiExtension/ai_api.py click-by --name CharaCreation
python ModSource/AiExtension/ai_api.py wait --button-name Female --timeout 10
python ModSource/AiExtension/ai_api.py click-by --name Female
python ModSource/AiExtension/ai_api.py wait --creator-ready true --timeout 60
```

## Test scenarios — `scenario.py`

A scenario is a list of steps run against the API, with checks, written as
JSON (or YAML when PyYAML is installed). Each run writes a report folder.

```text
python ModSource/AiExtension/scenario.py run ModSource/AiExtension/examples/title_to_creator.json
python ModSource/AiExtension/scenario.py run my_test.yaml --read-only --var root=CustomScene/UI/Root
python ModSource/AiExtension/scenario.py check my_test.json
```

`check` only validates the file. `run` prints each step as it runs and exits
with 0 when every step passed. The MCP tool `run_scenario` takes a file path or
the scenario object.

### File

```json
{
  "name": "system-menu-diff",
  "description": "Open the system menu and check what changed.",
  "vars": { "root": "CustomScene/UI/Root" },
  "steps": [
    { "name": "creator is open", "wait": { "creatorReady": true }, "timeout": 5 },
    { "name": "find the toggle", "assert": "ui/toggles",
      "checks": [ { "path": "$.toggles[?name=SystemMenu].isOn", "equals": false } ],
      "save": { "menu": "$.toggles[?name=SystemMenu].id" } },
    { "snapshot": { "path": "${root}", "name": "closed", "fieldTypes": "Toggle" } },
    { "call": "POST ui/toggle", "body": { "id": "${menu}", "value": true } },
    { "sleep": 0.5 },
    { "diff": { "from": "closed", "contains": "SystemMenu" }, "checks": [ { "path": "$.counts.changed", "gte": 1 } ] },
    { "capture": "menu-open" }
  ],
  "finally": [
    { "call": "POST ui/toggle", "body": { "id": "${menu}", "value": false } }
  ]
}
```

The steps run in order. After a failed step the remaining steps are skipped
(unless the failed step has `"continueOnFailure": true`). The `finally` steps
always run, after success or failure. When a step fails, a screenshot of the
screen at that moment is saved as `failure.png`.

### Steps

Each step has exactly one of these keys, and optionally `name`.

| Key | Value | Does |
| --- | --- | --- |
| `call` | `"METHOD path"`, e.g. `"POST ui/toggle"` | Calls the endpoint (path relative to `/api/v1/`, query included) with `body`. |
| `assert` | `"path"` | `GET` of the endpoint, for checks. |
| `click` | `{"name"|"text"|"path"|"index"}` | `POST ui/click` with this selector. |
| `wait` | conditions of `debug/wait` | Waits up to `timeout` seconds (default 10); fails if the conditions do not hold. |
| `capture` | `"file"` or `{"file", "region", "view", "expression", "pose"}` | Saves a screenshot, or with `region` a framed creator capture, as PNG in the report folder. |
| `sleep` | seconds | Waits. |
| `dev_cycle` | `{"projects", "build", "readyScene"}` | Runs `ai_api.py dev-cycle`; fails if a plugin did not load. Saves its report. |
| `log_check` | `{"level", "source", "contains", "max"}` | Fails if more than `max` (default 0) log lines of `level` (default `error`) or worse were written since the scenario started (or since the last `dev_cycle`). |
| `snapshot` | body of `debug/snapshot` | Takes a snapshot. |
| `diff` | `"name"` or `{"from", "to", "contains", "limit"}` | `debug/diff`; saves the result as JSON in the report folder. |

`call`, `assert`, `click`, `wait`, `snapshot` and `diff` also take:

| Key | Meaning |
| --- | --- |
| `status` | Expected HTTP status or list of statuses (default 200). |
| `checks` | List of checks on the response, all of which must pass. |
| `save` | `{"variable": "$.json.path"}`: keeps values from the response. |

### Checks and paths

A check is `{"path": "<path>", "<operator>": value}`.

| Operator | Passes when the value at the path |
| --- | --- |
| `equals`, `notEquals` | equals / differs from the value |
| `contains`, `notContains` | (a string, list or object) contains / does not contain it |
| `in` | is one of the listed values |
| `exists` | exists (`true`) or does not exist (`false`) |
| `matches` | is a string matching the regular expression |
| `gt`, `gte`, `lt`, `lte` | compares as a number |
| `length` | has this many elements or characters |

Paths start with `$`: `$.a.b` for keys, `[0]` and `[-1]` for list
elements, `[*]` for every element (the result is a list), and
`[?key=value]` for the first element whose `key` equals `value`.

### Variables

`${name}` in any string of a step is replaced by a variable. A string that is
only `${name}` takes the variable's own type (number, boolean, list).
Variables come from `vars`, from `--var name=value` (read as JSON when
possible) and from `save`.

### Read-only runs

With `--read-only` (MCP: `read_only`), every call to an endpoint marked
`mutates: true` in `GET schema`, every endpoint not listed there, and
`dev_cycle` fail without being sent.

### Report

The report folder is `ModSource/AiExtension/reports/<name>-<date>-<time>`
(or `--report-dir`). It holds:

| File | Content |
| --- | --- |
| `report.md` | Result, each step with its status and time, the failures, the files, and the warnings and errors logged during the run. |
| `report.json` | The same with each request, a response excerpt, the variables and the log lines. |
| `events.json` | Every `debug/events` entry from the run. |
| `*.png` | Captures, and `failure.png` when a step failed. |
| `stepNN-diff-*.json`, `stepNN-dev-cycle.json` | Full results of `diff` and `dev_cycle` steps. |

### Examples

`ModSource/AiExtension/examples` contains:

| File | Starts in | Checks |
| --- | --- | --- |
| `title_to_creator.json` | the title screen | Reaching the creator with selectors, the state, one character loaded, a face capture, no errors from Amanatsu plugins. |
| `system_menu_diff.json` | the character creator | Opening the system menu changes the menu toggle and shows its panel; closes it again in `finally` and checks that it is closed. |

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

