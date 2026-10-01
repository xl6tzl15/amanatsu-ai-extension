using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Character;
using Character.List;
using CharacterCreation;
using CharacterCreation.UI;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Amanatsu.AiExtension;

internal static class GameApi
{
    private static readonly HashSet<int> TransitionClicks = new();
    private static int _transitionSceneHandle = int.MinValue;

    internal static ApiResult Execute(string method, string path, string query, string body)
    {
        try
        {
            return (method, path) switch
            {
                ("GET", "/api/v1/schema") => Ok(Schema()),
                ("GET", "/api/v1/state") => Ok(State()),
                ("GET", "/api/v1/ui/buttons") => Ok(Buttons()),
                ("GET", "/api/v1/ui/toggles") => Ok(Toggles()),
                ("GET", "/api/v1/ui/input-sliders") => Ok(InputSliders()),
                // Served by the companion plugins through the extension registry.
                ("GET", "/api/v1/self-shadow") => Companion("GET", SelfShadow, query, body, "self-shadow"),
                ("POST", "/api/v1/self-shadow") => Companion("POST", SelfShadow, query, body, "self-shadow"),
                ("GET", "/api/v1/slider-unlock") => Companion("GET", SliderUnlock, query, body, "slider-unlock"),
                ("POST", "/api/v1/slider-unlock") => Companion("POST", SliderUnlock, query, body, "slider-unlock"),
                ("GET", "/api/v1/favorability") => Companion("GET", Favorability, query, body, "favorability"),
                ("POST", "/api/v1/favorability") => Companion("POST", Favorability, query, body, "favorability"),
                ("GET", "/api/v1/character-parameters") => Companion("GET", Favorability, query, body, "favorability"),
                ("POST", "/api/v1/character-parameters") => Companion("POST", Favorability, query, body, "favorability"),
                ("GET", "/api/v1/realtime-outfit") => Companion("GET", Favorability, query, body, "favorability"),
                ("POST", "/api/v1/realtime-outfit") => Companion("POST", Favorability, query, body, "favorability"),
                ("GET", "/api/v1/extensions") => ExtensionRegistry.List(),
                ("GET", "/api/v1/screenshot") => Screenshot(),
                ("POST", "/api/v1/ui/click") => Click(Parse(body)),
                ("POST", "/api/v1/ui/toggle") => SetToggle(Parse(body)),
                ("POST", "/api/v1/ui/input-slider") => SetInputSlider(Parse(body)),
                ("GET", "/api/v1/character") => CharacterState(),
                ("POST", "/api/v1/character/shape") => SetShape(Parse(body)),
                ("POST", "/api/v1/navigate") => Navigate(Parse(body)),
                ("POST", "/api/v1/character/choice") => SetChoice(Parse(body)),
                ("GET", "/api/v1/catalog") => Catalog(query),
                ("GET", "/api/v1/cards") => CardInspect.List(),
                ("GET", "/api/v1/card") => CardInspect.Read(query),
                ("GET", "/api/v1/logs") => OperationLog.Read(query),
                ("GET", "/api/v1/debug/plugins") => DebugApi.Plugins(),
                ("GET", "/api/v1/debug/log") => DebugApi.Log(query),
                ("GET", "/api/v1/debug/harmony") => DebugApi.Harmony(query),
                ("GET", "/api/v1/debug/scenes") => DebugInspect.Scenes(),
                ("GET", "/api/v1/debug/tree") => DebugInspect.Tree(query),
                ("GET", "/api/v1/debug/object") => DebugInspect.Object(query),
                ("GET", "/api/v1/debug/component") => DebugInspect.Component(query),
                ("GET", "/api/v1/debug/find") => DebugInspect.Find(query),
                ("GET", "/api/v1/debug/events") => DebugEvents.List(query),
                ("POST", "/api/v1/debug/watch") => Watches.Add(Parse(body)),
                ("GET", "/api/v1/debug/watches") => Watches.List(),
                ("POST", "/api/v1/debug/unwatch") => Watches.Remove(Parse(body)),
                ("POST", "/api/v1/debug/snapshot") => Snapshots.Take(Parse(string.IsNullOrEmpty(body) ? "{}" : body)),
                ("GET", "/api/v1/debug/snapshots") => Snapshots.List(),
                ("POST", "/api/v1/debug/snapshot-delete") => Snapshots.Delete(Parse(body)),
                ("GET", "/api/v1/debug/diff") => Snapshots.Diff(query),
                ("GET", "/api/v1/camera/landmarks") => Ok(CaptureApi.Landmarks()),
                ("POST", "/api/v1/camera/frame") => CaptureApi.Frame(Parse(body)),
                _ when path.StartsWith("/api/v1/ext/") => ExtensionRegistry.Execute(method, path.Substring(8), query, body),
                _ when path.StartsWith("/api/v1/creator/") => CreatorApi.Execute(method, path.Substring(16), Parse(string.IsNullOrEmpty(body) ? "{}" : body)),
                _ => new ApiResult(404, new { error = "unknown endpoint" })
            };
        }
        catch (ArgumentException ex) { return new ApiResult(400, new { error = ex.Message }); }
        catch (JsonException ex) { return new ApiResult(400, new { error = ex.Message }); }
    }

    private static ApiResult Ok(object value) => new(200, value);
    private static JsonElement Parse(string body) => JsonDocument.Parse(body).RootElement;

    // Built from api_endpoints.json (embedded), the same file openapi.yaml and the MCP server use.
    private static readonly Lazy<JsonElement> EndpointCatalog = new(() =>
    {
        using var stream = typeof(GameApi).Assembly.GetManifestResourceStream("api_endpoints.json");
        using var doc = JsonDocument.Parse(stream);
        return doc.RootElement.Clone();
    });

    private static object Schema()
    {
        var catalog = EndpointCatalog.Value;
        var components = catalog.GetProperty("components");
        // Core endpoints first, then the ones other plugins registered (they carry "extension": owner).
        var endpoints = catalog.GetProperty("endpoints").EnumerateArray().Concat(ExtensionRegistry.Catalog()).ToArray();
        return new
        {
            version = "v1",
            plugin = typeof(GameApi).Assembly.GetName().Version?.ToString(3),
            endpoints = endpoints.Select(e => Line(e, components)).ToArray(),
            details = endpoints,
            components
        };
    }

    // "POST /api/v1/creator/freeze {blink?,eyeMovement?,motion?} (summary) [mutates, creator]"
    private static string Line(JsonElement e, JsonElement components)
    {
        var line = e.GetProperty("method").GetString() + " /api/v1/" + e.GetProperty("path").GetString();
        if (e.TryGetProperty("query", out var query))
            line += "?" + string.Join("&", query.EnumerateObject().Select(q => q.Name + "="));
        if (e.TryGetProperty("body", out var body))
        {
            var fields = new List<string>();
            CollectFields(body, components, fields);
            line += " {" + string.Join(",", fields) + "}";
        }
        return line + " (" + e.GetProperty("summary").GetString() + ")" + Tags(e);
    }

    // " [mutates, creator]": whether the call changes state and which screen it needs.
    private static string Tags(JsonElement e)
    {
        var tags = new List<string>();
        if (e.GetProperty("mutates").GetBoolean()) tags.Add("mutates");
        var scene = e.GetProperty("scene").GetString();
        if (scene != "any") tags.Add(scene);
        return tags.Count == 0 ? " [read-only]" : " [" + string.Join(", ", tags) + "]";
    }

    private static void CollectFields(JsonElement schema, JsonElement components, List<string> fields)
    {
        if (schema.TryGetProperty("$ref", out var reference))
        {
            if (components.TryGetProperty(reference.GetString(), out var target)) CollectFields(target, components, fields);
            return;
        }
        if (schema.TryGetProperty("allOf", out var all)) foreach (var part in all.EnumerateArray()) CollectFields(part, components, fields);
        if (!schema.TryGetProperty("properties", out var properties)) return;
        var required = schema.TryGetProperty("required", out var r) ? r.EnumerateArray().Select(x => x.GetString()).ToHashSet() : new HashSet<string>();
        foreach (var property in properties.EnumerateObject())
            fields.Add(required.Contains(property.Name) ? property.Name : property.Name + "?");
    }

    private static object State()
    {
        var scene = SceneManager.GetActiveScene();
        var custom = HumanCustom.Instance;
        return new
        {
            unityVersion = Application.unityVersion,
            scene = scene.name,
            sceneBuildIndex = scene.buildIndex,
            characterEditorReady = custom != null && custom.Human != null,
            visibleButtons = ActiveButtons().Count()
        };
    }

    private const string SelfShadow = "ext/amanatsu.selfshadowtoggle/state";
    private const string SliderUnlock = "ext/amanatsu.unlockall/slider-unlock";
    private const string Favorability = "ext/amanatsu.favorabilitycontrol/state";

    private static ApiResult Companion(string method, string path, string query, string body, string feature) =>
        ExtensionRegistry.Has(method, path)
            ? ExtensionRegistry.Execute(method, path, query, body)
            : new ApiResult(503, new { available = false, error = $"{feature} plugin is not loaded (or is a version that does not register {path})" });

    private static IEnumerable<Button> ActiveButtons()
    {
        foreach (var button in Resources.FindObjectsOfTypeAll<Button>())
        {
            if (button != null && button.gameObject != null &&
                button.gameObject.scene.IsValid() && button.gameObject.activeInHierarchy &&
                button.isActiveAndEnabled)
                yield return button;
        }
    }

    private static object Buttons()
    {
        var result = new List<object>();
        foreach (var button in ActiveButtons().Take(600))
        {
            var label = button.GetComponentInChildren<TMP_Text>(true);
            result.Add(new
            {
                id = button.GetInstanceID(),
                name = button.gameObject.name,
                path = ObjectPath(button.transform),
                text = label != null ? label.text : "",
                interactable = button.interactable,
                visible = IsVisible(button.transform)
            });
        }
        return new { buttons = result, count = result.Count };
    }

    private static IEnumerable<Toggle> ActiveToggles()
    {
        foreach (var toggle in Resources.FindObjectsOfTypeAll<Toggle>())
        {
            if (toggle != null && toggle.gameObject != null &&
                toggle.gameObject.scene.IsValid() && toggle.gameObject.activeInHierarchy &&
                toggle.isActiveAndEnabled)
                yield return toggle;
        }
    }

    private static object Toggles()
    {
        var result = new List<object>();
        foreach (var toggle in ActiveToggles().Take(600))
        {
            var label = toggle.GetComponentInChildren<TMP_Text>(true);
            result.Add(new
            {
                id = toggle.GetInstanceID(),
                name = toggle.gameObject.name,
                path = ObjectPath(toggle.transform),
                text = label != null ? label.text : "",
                interactable = toggle.interactable,
                visible = IsVisible(toggle.transform),
                isOn = toggle.isOn
            });
        }
        return new { toggles = result, count = result.Count };
    }

    private static IEnumerable<InputSliderButton> ActiveInputSliders()
    {
        foreach (var component in Resources.FindObjectsOfTypeAll<InputSliderButton>())
        {
            if (component != null && component.gameObject != null && component.Input != null && component.Slider != null &&
                component.gameObject.scene.IsValid() && component.gameObject.activeInHierarchy &&
                component.Input.isActiveAndEnabled)
                yield return component;
        }
    }

    // The label the maker shows next to a slider (the texts in the slider row other than the number box).
    private static string SliderLabel(InputSliderButton component)
    {
        var inputText = component.Input.textComponent;
        var parts = new List<string>();
        foreach (var t in component.GetComponentsInChildren<TMPro.TMP_Text>(true))
            if (t != null && t != inputText && !string.IsNullOrWhiteSpace(t.text) && t.transform.IsChildOf(component.Input.transform) == false) parts.Add(t.text.Trim());
        foreach (var t in component.GetComponentsInChildren<UnityEngine.UI.Text>(true))
            if (t != null && !string.IsNullOrWhiteSpace(t.text) && !t.transform.IsChildOf(component.Input.transform)) parts.Add(t.text.Trim());
        return string.Join(" / ", parts.Distinct());
    }

    private static object InputSliders()
    {
        var result = new List<object>();
        foreach (var component in ActiveInputSliders().Take(600))
        {
            result.Add(new
            {
                id = component.GetInstanceID(),
                name = component.gameObject.name,
                path = ObjectPath(component.transform),
                text = component.Input.text,
                label = SliderLabel(component),
                order = component.transform.GetSiblingIndex(),
                value = component.Slider.value,
                minimum = component.Slider.minValue,
                maximum = component.Slider.maxValue,
                wholeNumbers = component.Slider.wholeNumbers
            });
        }
        return new { inputSliders = result, count = result.Count };
    }

    private static ApiResult SetInputSlider(JsonElement body)
    {
        var id = RequiredInt(body, "id");
        if (!body.TryGetProperty("text", out var textValue) || textValue.ValueKind != JsonValueKind.String)
            throw new ArgumentException("text must be a string");
        var text = textValue.GetString() ?? "";
        var component = ActiveInputSliders().FirstOrDefault(x => x.GetInstanceID() == id);
        if (component == null)
            return new ApiResult(404, new { error = "active input slider not found; refresh /ui/input-sliders" });

        component.Input.SetTextWithoutNotify(text);
        component.Input.onEndEdit.Invoke(text);
        return Ok(new
        {
            id,
            submitted = text,
            text = component.Input.text,
            value = component.Slider.value,
            minimum = component.Slider.minValue,
            maximum = component.Slider.maxValue
        });
    }

    private static ApiResult Screenshot()
    {
        var texture = ScreenCapture.CaptureScreenshotAsTexture();
        if (texture == null)
            return new ApiResult(503, new { error = "screenshot unavailable" });
        try
        {
            var png = ImageConversion.EncodeToPNG(texture);
            var bytes = new byte[png.Length];
            for (var i = 0; i < bytes.Length; i++) bytes[i] = png[i];
            return Ok(new { width = texture.width, height = texture.height, pngBase64 = Convert.ToBase64String(bytes) });
        }
        finally { UnityEngine.Object.Destroy(texture); }
    }

    private static string ObjectPath(Transform transform)
    {
        var names = new List<string>();
        for (var t = transform; t != null && names.Count < 24; t = t.parent)
            names.Add(t.gameObject.name);
        names.Reverse();
        return string.Join("/", names);
    }

    private static bool IsVisible(Transform transform)
    {
        for (var t = transform; t != null; t = t.parent)
        {
            var group = t.GetComponent<CanvasGroup>();
            if (group != null && group.alpha <= 0.01f)
                return false;
            var canvas = t.GetComponent<Canvas>();
            if (canvas != null && !canvas.enabled)
                return false;
        }
        return true;
    }

    private static readonly HashSet<string> NavigateClicks = new();
    private static int _navigateScene;

    // Title -> character creation -> female, one click per call so each scene can load in between.
    private static ApiResult Navigate(JsonElement body)
    {
        var target = RequiredString(body, "target");
        if (target != "female-creator") throw new ArgumentException("target must be female-creator");
        if (HumanCustom.Instance?.Human != null) return Ok(new { done = true, target });
        var scene = SceneManager.GetActiveScene();
        if (_navigateScene != scene.handle) { NavigateClicks.Clear(); _navigateScene = scene.handle; }
        foreach (var name in new[] { "CharaCreation", "Female" })
        {
            var button = ActiveButtons().FirstOrDefault(b => b.gameObject.name == name && b.interactable && IsVisible(b.transform));
            if (button == null) continue;
            if (!NavigateClicks.Add(name)) return Ok(new { done = false, waiting = name, scene = scene.name });
            button.onClick.Invoke();
            return Ok(new { done = false, clicked = name, scene = scene.name });
        }
        return Ok(new { done = false, waiting = "next screen", scene = scene.name });
    }

    // Selects buttons by stable properties instead of the per-run instance id.
    internal sealed class ButtonSelector
    {
        internal string Name, Text, Path;
        internal int? Index;

        internal static ButtonSelector Parse(JsonElement j)
        {
            if (j.ValueKind != JsonValueKind.Object) throw new ArgumentException("button selector must be an object");
            string S(string key) => j.TryGetProperty(key, out var v) ? v.ValueKind == JsonValueKind.String ? v.GetString() : throw new ArgumentException(key + " must be a string") : null;
            var selector = new ButtonSelector { Name = S("name"), Text = S("text"), Path = S("path") };
            if (j.TryGetProperty("index", out var i))
            {
                if (!i.TryGetInt32(out var n) || n < 0) throw new ArgumentException("index must be a non-negative integer");
                selector.Index = n;
            }
            if (selector.Name == null && selector.Text == null && selector.Path == null)
                throw new ArgumentException("button selector needs name, text or path");
            return selector;
        }

        // Visible buttons matching every given property; path matches the whole path or its trailing segments.
        internal List<Button> Matches(bool interactableOnly = true)
        {
            var result = new List<Button>();
            foreach (var button in ActiveButtons())
            {
                if (interactableOnly && !button.interactable) continue;
                if (Name != null && button.gameObject.name != Name) continue;
                if (Path != null)
                {
                    var path = ObjectPath(button.transform);
                    if (path != Path && !path.EndsWith("/" + Path, StringComparison.Ordinal)) continue;
                }
                if (Text != null && Label(button).Trim() != Text.Trim()) continue;
                if (!IsVisible(button.transform)) continue;
                result.Add(button);
            }
            return result;
        }
    }

    private static string Label(Button button)
    {
        var label = button.GetComponentInChildren<TMP_Text>(true);
        return label != null ? label.text : "";
    }

    private static ApiResult Click(JsonElement body)
    {
        Button button;
        if (body.TryGetProperty("id", out _))
        {
            var id = RequiredInt(body, "id");
            button = ActiveButtons().FirstOrDefault(x => x.GetInstanceID() == id);
            if (button == null)
                return new ApiResult(404, new { error = "active button not found; refresh /ui/buttons" });
        }
        else
        {
            var selector = ButtonSelector.Parse(body);
            var matches = selector.Matches(false);
            if (matches.Count == 0)
                return new ApiResult(404, new { error = "no visible button matches name/text/path" });
            if (selector.Index == null && matches.Count > 1)
                return new ApiResult(409, new { error = "several buttons match; add index or a more specific path", candidates = matches.Take(20).Select(b => new { id = b.GetInstanceID(), name = b.gameObject.name, path = ObjectPath(b.transform), text = Label(b), interactable = b.interactable }).ToArray() });
            var index = selector.Index ?? 0;
            if (index >= matches.Count)
                return new ApiResult(404, new { error = $"index {index} out of range; {matches.Count} buttons match" });
            button = matches[index];
        }
        if (!button.interactable)
            return new ApiResult(409, new { error = "button is disabled" });
        if (!IsVisible(button.transform))
            return new ApiResult(409, new { error = "button is hidden" });

        var buttonId = button.GetInstanceID();
        var name = button.gameObject.name;
        var sceneHandle = SceneManager.GetActiveScene().handle;
        if (_transitionSceneHandle != sceneHandle)
        {
            TransitionClicks.Clear();
            _transitionSceneHandle = sceneHandle;
        }
        var isTransition = name is "Female" or "Male";
        if (isTransition && !TransitionClicks.Add(buttonId))
            return new ApiResult(409, new { error = "scene transition was already requested; wait for the next screen" });
        button.onClick.Invoke();
        return Ok(new { clicked = buttonId, name, path = ObjectPath(button.transform), text = Label(button) });
    }

    private static ApiResult SetToggle(JsonElement body)
    {
        var id = RequiredInt(body, "id");
        var value = RequiredBool(body, "value");
        var toggle = ActiveToggles().FirstOrDefault(x => x.GetInstanceID() == id);
        if (toggle == null)
            return new ApiResult(404, new { error = "active toggle not found; refresh /ui/toggles" });
        if (!toggle.interactable)
            return new ApiResult(409, new { error = "toggle is disabled" });
        if (!IsVisible(toggle.transform))
            return new ApiResult(409, new { error = "toggle is hidden" });
        var before = toggle.isOn;
        toggle.isOn = value;
        return Ok(new { id, before, after = toggle.isOn });
    }

    private static Human CurrentHuman()
    {
        var custom = HumanCustom.Instance;
        if (custom == null || custom.Human == null)
            return null;
        return custom.Human;
    }

    private static ApiResult CharacterState()
    {
        var human = CurrentHuman();
        if (human == null)
            return new ApiResult(409, new { error = "open character creation first" });

        var body = human.FileBody;
        var face = human.FileFace;
        var coordinate = human.Coorde.Now;
        var hair = coordinate?.Hair?.parts;
        var hairIds = new List<int>();
        if (hair != null)
            for (var i = 0; i < hair.Length; i++) hairIds.Add(hair[i]?.id ?? -1);
        var clothes = coordinate?.Clothes?.parts;
        var clothesIds = new List<int>();
        if (clothes != null)
            for (var i = 0; i < clothes.Length; i++) clothesIds.Add(clothes[i]?.id ?? -1);

        return Ok(new
        {
            name = human.Name,
            lastname = human.FileParam.lastname,
            firstname = human.FileParam.firstname,
            bodyShapes = Values(body.shapeValueBody),
            faceShapes = Values(face.shapeValueFace),
            headId = face.headId,
            hairIds,
            clothesIds,
            skinId = body.skinId,
            faceSkinId = face.skinId,
            bodyDetailId = body.detailId,
            faceDetailId = face.detailId
        });
    }

    private static float[] Values(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<float> source)
    {
        if (source == null) return Array.Empty<float>();
        var values = new float[source.Length];
        for (var i = 0; i < values.Length; i++) values[i] = source[i];
        return values;
    }

    private static ApiResult SetShape(JsonElement body)
    {
        var human = CurrentHuman();
        if (human == null)
            return new ApiResult(409, new { error = "open character creation first" });

        var region = RequiredString(body, "region");
        var index = RequiredInt(body, "index");
        var value = RequiredFloat(body, "value");
        if (!float.IsFinite(value) || value < -5 || value > 5)
            throw new ArgumentException("value must be finite and between -5 and 5");

        // Preserve extended values in character data. Amanatsu.UnlockAll also
        // patches the animation-key evaluator, which otherwise derives an
        // invalid array index for rates outside 0..1.
        human.Data.SkipRangeCheck = true;

        if (region == "body")
        {
            var values = human.FileBody.shapeValueBody;
            if (values == null || index < 0 || index >= values.Length)
                throw new ArgumentException("body shape index out of range");
            var before = values[index];
            var accepted = human.Body.SetShapeBodyValue(index, value, true);
            human.Body.SetUpdateShapeBodyValue();
            human.SetUpdateShape();
            return Ok(new { region, index, before, requested = value, accepted, after = values[index], skipRangeCheck = human.Data.SkipRangeCheck });
        }

        if (region == "face")
        {
            var values = human.FileFace.shapeValueFace;
            if (values == null || index < 0 || index >= values.Length)
                throw new ArgumentException("face shape index out of range");
            var before = values[index];
            var accepted = human.Face.SetShapeFaceValue(index, value);
            human.Face.SetUpdateShapeFaceValue();
            human.SetUpdateShape();
            return Ok(new { region, index, before, requested = value, accepted, after = values[index], skipRangeCheck = human.Data.SkipRangeCheck });
        }

        throw new ArgumentException("region must be body or face");
    }

    private static ApiResult SetChoice(JsonElement body)
    {
        var human = CurrentHuman();
        if (human == null)
            return new ApiResult(409, new { error = "open character creation first" });

        var kind = RequiredString(body, "kind");
        var id = RequiredInt(body, "id");
        var category = kind switch
        {
            "head" => Define.CategoryNo.bo_head,
            "hair_back" => Define.CategoryNo.bo_hair_b,
            "hair_front" => Define.CategoryNo.bo_hair_f,
            "hair_side" => Define.CategoryNo.bo_hair_s,
            "hair_option" => Define.CategoryNo.bo_hair_o,
            "clothes_top" => Define.CategoryNo.co_top,
            "clothes_bot" => Define.CategoryNo.co_bot,
            "clothes_bra" => Define.CategoryNo.co_bra,
            "clothes_shorts" => Define.CategoryNo.co_shorts,
            "clothes_gloves" => Define.CategoryNo.co_gloves,
            "clothes_panst" => Define.CategoryNo.co_panst,
            "clothes_socks" => Define.CategoryNo.co_socks,
            "clothes_shoes" => Define.CategoryNo.co_shoes,
            "clothes_add_arm" => Define.CategoryNo.co_add_arm,
            "clothes_add_leg" => Define.CategoryNo.co_add_leg,
            "clothes_add_other" => Define.CategoryNo.co_add_other,
            _ => throw new ArgumentException("unsupported choice kind")
        };
        if (!Human.LstCtrl.ContainsInfo(category, id))
            throw new ArgumentException("id is not present in the game's category list");

        if (kind == "head")
        {
            var before = human.FileFace.headId;
            human.Face.ChangeHead(id, true);
            return Ok(new { kind, before, requested = id, after = human.FileFace.headId });
        }

        if (kind.StartsWith("clothes_", StringComparison.Ordinal))
        {
            var clothesSlot = (int)category - (int)Define.CategoryNo.co_top;
            var clothesParts = human.Coorde.Now.Clothes.parts;
            var previousClothes = clothesParts[clothesSlot].id;
            switch (kind)
            {
                case "clothes_top": human.Cloth.ChangeClothesTop(id, 0, 0, 0, true); break;
                case "clothes_bot": human.Cloth.ChangeClothesBot(id, true); break;
                case "clothes_bra": human.Cloth.ChangeClothesBra(id, true); break;
                case "clothes_shorts": human.Cloth.ChangeClothesShorts(id, true); break;
                case "clothes_gloves": human.Cloth.ChangeClothesGloves(id, true); break;
                case "clothes_panst": human.Cloth.ChangeClothesPanst(id, true); break;
                case "clothes_socks": human.Cloth.ChangeClothesSocks(id, true); break;
                case "clothes_shoes": human.Cloth.ChangeClothesShoes(id, true); break;
                case "clothes_add_arm": human.Cloth.ChangeClothesArmAddParts(id, true); break;
                case "clothes_add_leg": human.Cloth.ChangeClothesLegAddParts(id, true); break;
                case "clothes_add_other": human.Cloth.ChangeClothesOtherAddParts(id, true); break;
            }
            CreatorApi.SyncCoordinate(human);
            return Ok(new { kind, before = previousClothes, requested = id, after = clothesParts[clothesSlot].id });
        }

        var hairKind = kind switch
        {
            "hair_back" => HumanHair.Define.HairKind.back,
            "hair_front" => HumanHair.Define.HairKind.front,
            "hair_side" => HumanHair.Define.HairKind.side,
            _ => HumanHair.Define.HairKind.option
        };
        var parts = human.Coorde.Now.Hair.parts;
        var slot = (int)hairKind;
        var previous = parts[slot].id;
        human.Hair.ChangeHair(hairKind, id, true);
        CreatorApi.SyncCoordinate(human);
        return Ok(new { kind, before = previous, requested = id, after = parts[slot].id });
    }

    private static ApiResult Catalog(string query)
    {
        var human = CurrentHuman();
        if (human == null)
            return new ApiResult(409, new { error = "open character creation first" });

        var parameters = System.Web.HttpUtility.ParseQueryString(query.TrimStart('?'));
        if (!Enum.TryParse<Define.CategoryNo>(parameters["category"], true, out var category))
            throw new ArgumentException("valid category query parameter required");
        var table = Human.LstCtrl.GetCategoryInfo(category);
        if (table == null)
            return Ok(new { category = category.ToString(), choices = Array.Empty<object>() });

        var offset = 0;
        if (parameters["offset"] != null && (!int.TryParse(parameters["offset"], out offset) || offset < 0))
            throw new ArgumentException("offset must be a non-negative integer");
        var limit = 500;
        if (parameters["limit"] != null && (!int.TryParse(parameters["limit"], out limit) || limit < 1))
            throw new ArgumentException("limit must be a positive integer");
        var choices = new List<(int Id, string Name)>();
        foreach (var pair in table)
            choices.Add((pair.Key, pair.Value?.Name ?? ""));
        var page = choices.OrderBy(x => x.Id).Skip(offset).Take(limit).Select(x => new { id = x.Id, name = x.Name }).ToArray();
        return Ok(new { category = category.ToString(), total = choices.Count, offset, more = offset + page.Length < choices.Count, choices = page });
    }

    private static int RequiredInt(JsonElement body, string name) =>
        body.TryGetProperty(name, out var value) && value.TryGetInt32(out var number)
            ? number : throw new ArgumentException($"integer {name} required");

    private static float RequiredFloat(JsonElement body, string name) =>
        body.TryGetProperty(name, out var value) && value.TryGetSingle(out var number)
            ? number : throw new ArgumentException($"number {name} required");

    private static string RequiredString(JsonElement body, string name) =>
        body.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : throw new ArgumentException($"string {name} required");

    private static bool RequiredBool(JsonElement body, string name) =>
        body.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean() : throw new ArgumentException($"boolean {name} required");
}
