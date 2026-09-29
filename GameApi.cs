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
                ("GET", "/api/v1/self-shadow") => SelfShadowState(),
                ("POST", "/api/v1/self-shadow") => SetSelfShadow(Parse(body)),
                ("GET", "/api/v1/slider-unlock") => SliderUnlockState(),
                ("POST", "/api/v1/slider-unlock") => SetSliderUnlock(Parse(body)),
                ("GET", "/api/v1/favorability") => FavorabilityState(),
                ("POST", "/api/v1/favorability") => SetFavorability(body),
                ("GET", "/api/v1/character-parameters") => FavorabilityState(),
                ("POST", "/api/v1/character-parameters") => SetFavorability(body),
                ("GET", "/api/v1/realtime-outfit") => FavorabilityState(),
                ("POST", "/api/v1/realtime-outfit") => SetFavorability(body),
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
                ("GET", "/api/v1/camera/landmarks") => Ok(CaptureApi.Landmarks()),
                ("POST", "/api/v1/camera/frame") => CaptureApi.Frame(Parse(body)),
                _ when path.StartsWith("/api/v1/creator/") => CreatorApi.Execute(method, path.Substring(16), Parse(string.IsNullOrEmpty(body) ? "{}" : body)),
                _ => new ApiResult(404, new { error = "unknown endpoint" })
            };
        }
        catch (ArgumentException ex) { return new ApiResult(400, new { error = ex.Message }); }
        catch (JsonException ex) { return new ApiResult(400, new { error = ex.Message }); }
    }

    private static ApiResult Ok(object value) => new(200, value);
    private static JsonElement Parse(string body) => JsonDocument.Parse(body).RootElement;

    private static object Schema() => new
    {
        version = "v1",
        endpoints = new[]
        {
            "GET /api/v1/state",
            "POST /api/v1/navigate {target:female-creator} (one step per call; repeat until done:true)",
            "POST /api/v1/creator/shapes {face?:{name|index:value},body?:{name|index:value}} (validated as a whole, one model refresh)",
            "GET /api/v1/creator/kits",
            "POST /api/v1/creator/kit-save {region:outline|eyes|brows|nose|mouth|hair,name,description?,overwrite?}",
            "POST /api/v1/creator/kit-from-card {file,region,name,description?,coordinate?,overwrite?}",
            "POST /api/v1/creator/kit-apply {region,name,exclude?:[shapes|parts|colors|field names],dryRun?}",
            "POST /api/v1/creator/verify-card {file}",
            "POST /api/v1/creator/kit-delete {region,name}",
            "GET|POST /api/v1/creator/eye-lines {eyelineColor?,eyelidColor?,eyelineUpWeight?}",
            "POST /api/v1/creator/hair-colors {slots?:[0..3],base?,start?,end?,outline?,gloss?,shadow?,mesh?,inner?,useMesh?,useInner?}",
            "GET /api/v1/cards",
            "GET /api/v1/card?file=<relative png path>",
            "POST /api/v1/creator/reset {keepProfile?}",
            "GET|POST /api/v1/creator/face-parts {eyebrow,eyelineUp,eyelineDown,eyelid,white,nose,lipLine,detail,eye,pupil,eyePreset,eyePresetFlags}",
            "GET|POST /api/v1/creator/makeup {eyeshadowId,eyeshadowColor,cheekId,cheekColor,cheekHighlightColor,lipId,lipColor,lipHighlightColor,eyeGradColor,eyeHighlightColor}",
            "POST /api/v1/creator/accessory-move {slot,correct,pos,rot,scl,reset}",
            "POST /api/v1/creator/accessory-clear {slot}",
            "POST /api/v1/creator/clothes-pattern {slot,channel,pattern,patternColor,gloss,metallic}",
            "POST /api/v1/creator/coordinate {type}",
            "POST /api/v1/creator/coordinate-copy {from,to,parts}",
            "GET /api/v1/creator/export",
            "POST /api/v1/creator/import {operations,stopOnError}",
            "GET /api/v1/creator/personalities",
            "GET /api/v1/creator/poses",
            "GET /api/v1/ui/buttons",
            "GET /api/v1/ui/toggles",
            "GET /api/v1/ui/input-sliders",
            "GET /api/v1/self-shadow",
            "POST /api/v1/self-shadow {enabled:boolean}",
            "GET /api/v1/slider-unlock",
            "POST /api/v1/slider-unlock {enabled:boolean}",
            "GET /api/v1/favorability",
            "POST /api/v1/favorability {uniqueId|listIndex|name,point?:int,delta?:int,level?:int,isMaxLevel?:boolean}",
            "GET /api/v1/character-parameters",
            "POST /api/v1/character-parameters {uniqueId|listIndex|name,parameter?:favorability|inclusiveness|proactivity|curiosity,point?,delta?,level?,isMaxLevel?,gaugeStageIndex?,gaugeStage?:0..6,unlockScenes?:boolean,coordinateType?,clothesPart?,clothesState?,accessorySlot?,accessoryVisible?,allAccessoriesVisible?}",
            "GET /api/v1/realtime-outfit (realtimeOutfit state is included per character)",
            "POST /api/v1/realtime-outfit {uniqueId|listIndex|name,coordinateType?:swimsuit|afterBath,clothesPart?:top|bottom|bra|shorts|gloves|pantyhose|socks|shoes,clothesState?:clothing|halfUndress|naked,accessorySlot?:int,accessoryVisible?:boolean,allAccessoriesVisible?:boolean}",
            "GET /api/v1/screenshot (base64 PNG)",
            "GET /api/v1/camera/landmarks (world-space skeleton positions)",
            "POST /api/v1/camera/frame {region:face|bust|upper_body|waist|legs|full,view:front|back|left|right|top|bottom}",
            "POST /api/v1/capture {region,view} (frame, wait for render, return framing metadata and image.pngBase64)",
            "GET /api/v1/creator/details (profile, named shape indices, colors, coordinate parts)",
            "GET /api/v1/creator/diagnostics",
            "GET /api/v1/creator/files (initialize the native card UI first)",
            "POST /api/v1/creator/native-ui {command:new-card|capture|save-card|back|load-card,index?:number}",
            "POST /api/v1/creator/color {target:hair|eye|eyebrow|clothes|accessory,slot?,channel?,field?,color:[r,g,b,a]}",
            "POST /api/v1/creator/accessory {slot,category:ao_*,id,parent}",
            "POST /api/v1/creator/hair-bundle {part:0..3,index,moveRate?:[x,y,z],rotRate?:[x,y,z]} (normalized rates for this style's movable bundles)",
            "POST /api/v1/creator/profile {lastname,firstname,nickname,birthMonth,birthDay}",
            "GET or POST /api/v1/creator/camera {position?,direction?,rotation?}",
            "POST /api/v1/ui/click {id}",
            "POST /api/v1/ui/toggle {id,value:boolean}",
            "POST /api/v1/ui/input-slider {id,text:string} (exercise the maker's real numeric-entry event path)",
            "GET /api/v1/character",
            "POST /api/v1/character/shape {region:body|face,index,value:-5..5}",
            "POST /api/v1/character/choice {kind:head|hair_back|hair_front|hair_side|hair_option|clothes_bot|clothes_bra|clothes_shorts|clothes_gloves|clothes_panst|clothes_socks|clothes_shoes,id}",
            "GET /api/v1/catalog?category=bo_head|bo_hair_f|co_bot|...",
            "GET /api/v1/logs?limit=100 (recent operation log entries)"
        }
    };

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

    private static Type SelfShadowControllerType()
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var type = assembly.GetType("Amanatsu.SelfShadowToggle.ShadowController", false);
            if (type != null)
                return type;
        }
        return null;
    }

    private static ApiResult SelfShadowState()
    {
        var type = SelfShadowControllerType();
        if (type == null)
            return new ApiResult(503, new { available = false, error = "self-shadow plugin is not loaded" });
        var available = (bool)(type.GetProperty("Available")?.GetValue(null) ?? false);
        var enabled = (bool)(type.GetProperty("CurrentEnabled")?.GetValue(null) ?? false);
        return Ok(new { available, enabled });
    }

    private static ApiResult SetSelfShadow(JsonElement body)
    {
        var type = SelfShadowControllerType();
        if (type == null)
            return new ApiResult(503, new { available = false, error = "self-shadow plugin is not loaded" });
        var requested = RequiredBool(body, "enabled");
        var applied = (bool)(type.GetMethod("SetEnabledFromApi")?.Invoke(null, new object[] { requested }) ?? false);
        var enabled = (bool)(type.GetProperty("CurrentEnabled")?.GetValue(null) ?? false);
        return applied ? Ok(new { available = true, requested, enabled })
            : new ApiResult(503, new { available = false, requested, enabled, error = "self-shadow controller is not ready" });
    }

    private static Type SliderUnlockControllerType()
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var type = assembly.GetType("Amanatsu.UnlockAll.UnlockController", false);
            if (type != null)
                return type;
        }
        return null;
    }

    private static ApiResult SliderUnlockState()
    {
        var type = SliderUnlockControllerType();
        if (type == null)
            return new ApiResult(503, new { available = false, error = "slider-unlock plugin is not loaded" });
        var available = (bool)(type.GetProperty("Available")?.GetValue(null) ?? false);
        var enabled = (bool)(type.GetProperty("CurrentEnabled")?.GetValue(null) ?? false);
        return Ok(new { available, enabled });
    }

    private static ApiResult SetSliderUnlock(JsonElement body)
    {
        var type = SliderUnlockControllerType();
        if (type == null)
            return new ApiResult(503, new { available = false, error = "slider-unlock plugin is not loaded" });
        var requested = RequiredBool(body, "enabled");
        var applied = (bool)(type.GetMethod("SetEnabledFromApi")?.Invoke(null, new object[] { requested }) ?? false);
        var enabled = (bool)(type.GetProperty("CurrentEnabled")?.GetValue(null) ?? false);
        return applied ? Ok(new { available = true, requested, enabled })
            : new ApiResult(503, new { available = false, requested, enabled, error = "slider-unlock controller is not ready" });
    }

    private static Type FavorabilityControllerType()
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var type = assembly.GetType("Amanatsu.FavorabilityControl.FavorabilityController", false);
            if (type != null)
                return type;
        }
        return null;
    }

    private static ApiResult FavorabilityState()
    {
        var type = FavorabilityControllerType();
        if (type == null)
            return new ApiResult(503, new { available = false, error = "favorability plugin is not loaded" });
        var json = type.GetMethod("GetStateJson")?.Invoke(null, null) as string;
        if (string.IsNullOrEmpty(json))
            return new ApiResult(503, new { available = false, error = "favorability controller is not ready" });
        using var document = JsonDocument.Parse(json);
        return Ok(document.RootElement.Clone());
    }

    private static ApiResult SetFavorability(string body)
    {
        var type = FavorabilityControllerType();
        if (type == null)
            return new ApiResult(503, new { available = false, error = "favorability plugin is not loaded" });
        var json = type.GetMethod("ApplyFromApi")?.Invoke(null, new object[] { body }) as string;
        if (string.IsNullOrEmpty(json))
            return new ApiResult(503, new { available = false, error = "favorability controller is not ready" });
        using var document = JsonDocument.Parse(json);
        var result = document.RootElement.Clone();
        if (result.TryGetProperty("ok", out var ok) && !ok.GetBoolean())
        {
            var code = result.TryGetProperty("code", out var codeElement) ? codeElement.GetString() : "";
            return new ApiResult(code == "not_ready" ? 503 : code is "no_save_data" or "not_found" or "outfit_unavailable" ? 409 : 400, result);
        }
        return Ok(result);
    }

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

    private static ApiResult Click(JsonElement body)
    {
        var id = RequiredInt(body, "id");
        var button = ActiveButtons().FirstOrDefault(x => x.GetInstanceID() == id);
        if (button == null)
            return new ApiResult(404, new { error = "active button not found; refresh /ui/buttons" });
        if (!button.interactable)
            return new ApiResult(409, new { error = "button is disabled" });
        if (!IsVisible(button.transform))
            return new ApiResult(409, new { error = "button is hidden" });

        var name = button.gameObject.name;
        var sceneHandle = SceneManager.GetActiveScene().handle;
        if (_transitionSceneHandle != sceneHandle)
        {
            TransitionClicks.Clear();
            _transitionSceneHandle = sceneHandle;
        }
        var isTransition = name is "Female" or "Male";
        if (isTransition && !TransitionClicks.Add(id))
            return new ApiResult(409, new { error = "scene transition was already requested; wait for the next screen" });
        button.onClick.Invoke();
        return Ok(new { clicked = id, name, path = ObjectPath(button.transform) });
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

        var choices = new List<(int Id, string Name)>();
        foreach (var pair in table)
        {
            if (choices.Count >= 500) break;
            choices.Add((pair.Key, pair.Value?.Name ?? ""));
        }
        return Ok(new { category = category.ToString(), choices = choices.OrderBy(x => x.Id).Select(x => new { id = x.Id, name = x.Name }).ToArray() });
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
