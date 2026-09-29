#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Character;
using Character.PupilPreset;
using CharacterCreation;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.UI;
using CategoryNo = Character.List.Define.CategoryNo;

namespace Amanatsu.AiExtension;

// Creator endpoints added for AI-driven character building (0.4.0).
internal static class CreatorApiEx
{
    static ApiResult Ok(object x) => new(200, x);
    static float[] C(Color c) => new[] { c.r, c.g, c.b, c.a };
    static float[] V(Vector3 v) => new[] { v.x, v.y, v.z };
    static int? OptInt(JsonElement j, string k) => j.TryGetProperty(k, out var p) ? p.GetInt32() : null;
    static float? OptFloat(JsonElement j, string k)
    {
        if (!j.TryGetProperty(k, out var p)) return null;
        var v = p.GetSingle();
        if (!float.IsFinite(v)) throw new ArgumentException(k + " must be finite");
        return v;
    }
    static Color Col(JsonElement j)
    {
        var v = j.EnumerateArray().Select(x => x.GetSingle()).ToArray();
        if ((v.Length != 3 && v.Length != 4) || v.Any(x => !float.IsFinite(x) || x < 0 || x > 1))
            throw new ArgumentException("color must be 3 or 4 channels in 0..1");
        return new Color(v[0], v[1], v[2], v.Length == 4 ? v[3] : 1);
    }
    static Color? OptCol(JsonElement j, string k) => j.TryGetProperty(k, out var p) ? Col(p) : null;
    static Vector3 Vec(JsonElement j)
    {
        var v = j.EnumerateArray().Select(x => x.GetSingle()).ToArray();
        if (v.Length != 3 || v.Any(x => !float.IsFinite(x) || Math.Abs(x) > 1000)) throw new ArgumentException("invalid vector");
        return new Vector3(v[0], v[1], v[2]);
    }
    static void CheckId(CategoryNo category, int id)
    {
        if (!Human.LstCtrl.ContainsInfo(category, id)) throw new ArgumentException($"{category} has no id {id}; query /api/v1/catalog?category={category}");
    }

    internal static ApiResult? Execute(string method, string action, JsonElement j, Human h, HumanCustom custom)
    {
        switch (method, action)
        {
            case ("POST", "accessory-move"): return AccessoryMove(j, h);
            case ("POST", "accessory-clear"): return AccessoryClear(j, h);
            case ("POST", "clothes-pattern"): return ClothesPattern(j, h);
            case ("GET", "face-parts"): return Ok(FaceParts(h));
            case ("POST", "face-parts"): return SetFaceParts(j, h);
            case ("GET", "makeup"): return Ok(Makeup(h));
            case ("POST", "makeup"): return SetMakeup(j, h);
            case ("POST", "coordinate"): return SwitchCoordinate(j, h);
            case ("POST", "coordinate-copy"): return CopyCoordinate(j, h);
            case ("POST", "reset"): return Reset(j, h);
            case ("GET", "export"): return Ok(Export(h));
            case ("POST", "import"): return Import(j);
            case ("GET", "personalities"): return Ok(new { personalities = Personalities(custom) });
            case ("GET", "poses"): return Ok(Poses(h));
        }
        return null;
    }

    // ---- 1. accessory transforms -------------------------------------------------

    static Il2CppStructArray<Vector3> Moves(HumanDataAccessory.PartsInfo part) =>
        part.addMove == null ? null : new Il2CppStructArray<Vector3>(part.addMove.Pointer);

    internal static object AccessoryMoveInfo(HumanDataAccessory.PartsInfo part)
    {
        var m = Moves(part);
        if (m == null || m.Length < 6) return null;
        return Enumerable.Range(0, 2).Select(c => new { correct = c, pos = V(m[c * 3]), rot = V(m[c * 3 + 1]), scl = V(m[c * 3 + 2]) }).ToArray();
    }

    static ApiResult AccessoryMove(JsonElement j, Human h)
    {
        var slot = OptInt(j, "slot") ?? -1;
        var correct = OptInt(j, "correct") ?? 0;
        var parts = h.Coorde.Now.Accessory.parts;
        if (slot < 0 || slot >= parts.Length) throw new ArgumentException("accessory slot out of range");
        if (correct < 0 || correct > 1) throw new ArgumentException("correct must be 0 or 1");
        if (parts[slot].type == (int)CategoryNo.ao_none || !h.Acs.IsAccessory(slot)) throw new ArgumentException("slot has no accessory");
        Vector3? pos = j.TryGetProperty("pos", out var p) ? Vec(p) : null;
        Vector3? rot = j.TryGetProperty("rot", out var r) ? Vec(r) : null;
        Vector3? scl = j.TryGetProperty("scl", out var s) ? Vec(s) : null;
        var reset = j.TryGetProperty("reset", out var rs) && rs.GetBoolean();
        if (reset) h.Acs.ResetAccessoryMove(slot, correct, 7);
        for (var axis = 0; axis < 3; axis++)
        {
            var flag = 1 << axis;
            if (pos.HasValue) h.Acs.SetAccessoryPos(slot, correct, pos.Value[axis], false, flag);
            if (rot.HasValue) h.Acs.SetAccessoryRot(slot, correct, rot.Value[axis], false, flag);
            if (scl.HasValue) h.Acs.SetAccessoryScl(slot, correct, scl.Value[axis], false, flag);
        }
        CreatorApi.SyncCoordinate(h);
        return Ok(new { slot, correct, move = AccessoryMoveInfo(parts[slot]) });
    }

    static ApiResult AccessoryClear(JsonElement j, Human h)
    {
        var slot = OptInt(j, "slot") ?? -1;
        if (slot < 0 || slot >= h.Coorde.Now.Accessory.parts.Length) throw new ArgumentException("accessory slot out of range");
        ClearSlot(h, slot);
        CreatorApi.SyncCoordinate(h);
        return Ok(new { slot, cleared = h.Coorde.Now.Accessory.parts[slot].type == (int)CategoryNo.ao_none });
    }

    // ChangeAccessoryNone only removes the object; setting ao_none also clears the saved slot data.
    static void ClearSlot(Human h, int slot)
    {
        h.Acs.ChangeAccessory(slot, CategoryNo.ao_none, 0, HumanAccessory.Define.AccessoryParentKey.none, true, new Il2CppSystem.Nullable<bool>());
    }

    // ---- clothes pattern / gloss / metallic --------------------------------------------

    static ApiResult ClothesPattern(JsonElement j, Human h)
    {
        var slot = OptInt(j, "slot") ?? -1; var channel = OptInt(j, "channel") ?? -1;
        var parts = h.Coorde.Now.Clothes.parts;
        if (slot < 0 || slot >= parts.Length || channel < 0 || channel >= parts[slot].colorInfo.Length) throw new ArgumentException("clothes slot/channel out of range");
        var pattern = OptInt(j, "pattern"); var patternColor = OptCol(j, "patternColor");
        var gloss = OptFloat(j, "gloss"); var metallic = OptFloat(j, "metallic");
        if (pattern.HasValue && pattern.Value != 0) CheckId(CategoryNo.mt_pattern, pattern.Value);
        if (gloss is < 0 or > 1 || metallic is < 0 or > 1) throw new ArgumentException("gloss/metallic must be 0..1");
        var info = parts[slot].colorInfo[channel];
        if (pattern.HasValue) info.patternInfo.pattern = pattern.Value;
        if (patternColor.HasValue) info.patternInfo.patternColor = patternColor.Value;
        if (gloss.HasValue) info.gloss = gloss.Value;
        if (metallic.HasValue) info.metallic = metallic.Value;
        h.Cloth.AddUpdateClothesFlagsFull();
        h.Cloth.CreateClothesTexture(true, slot, true);
        CreatorApi.SyncCoordinate(h);
        return Ok(new { slot, channel, pattern = info.patternInfo.pattern, patternColor = C(info.patternInfo.patternColor), info.gloss, info.metallic });
    }

    internal static object ClothesPatterns(HumanDataClothes.PartsInfo p) =>
        p.colorInfo.Select(ci => new { pattern = ci.patternInfo.pattern, patternColor = C(ci.patternInfo.patternColor), ci.gloss, ci.metallic }).ToArray();

    // ---- 2. face part IDs ----------------------------------------------------------

    static object FaceParts(Human h)
    {
        var f = h.FileFace;
        var presets = PupilPresetDataList.Load();
        return new
        {
            eyebrow = f.eyebrowId, eyelineUp = f.eyelineUpId, eyelineDown = f.eyelineDownId, eyelid = f.eyelidId,
            white = f.whiteId, nose = f.noseId, lipLine = f.lipLineId, detail = f.detailId,
            foregroundEyes = (int)f.foregroundEyes, foregroundEyebrow = (int)f.foregroundEyebrow,
            pupils = f.pupil.Select(x => new
            {
                eye = x.id, pupil = x.overId, gradMask = x.gradMaskId, eyeGradColor = C(x.eyeGradColor),
                highlights = x.highlightInfos.Select(hl => new { hl.id, color = C(hl.color) }).ToArray()
            }).ToArray(),
            eyePresetCount = presets?.List?.Count ?? 0,
            categories = new
            {
                eyebrow = "mt_eyebrow", eyelineUp = "mt_eyeline_up", eyelineDown = "mt_eyeline_down", eyelid = "mt_eyelid",
                white = "mt_eye_white", nose = "mt_nose", lipLine = "mt_lipline", eye = "mt_eye", pupil = "mt_eyepipil",
                highlight = "mt_eye_hi_up", detail = "mt_face_detail"
            }
        };
    }

    static ApiResult SetFaceParts(JsonElement j, Human h)
    {
        var f = h.FileFace;
        var eyebrow = OptInt(j, "eyebrow"); var up = OptInt(j, "eyelineUp"); var down = OptInt(j, "eyelineDown");
        var eyelid = OptInt(j, "eyelid"); var white = OptInt(j, "white"); var nose = OptInt(j, "nose");
        var lipLine = OptInt(j, "lipLine"); var eye = OptInt(j, "eye"); var pupil = OptInt(j, "pupil");
        var detail = OptInt(j, "detail"); var preset = OptInt(j, "eyePreset");
        var presetFlags = OptInt(j, "eyePresetFlags") ?? 3;
        var fgEyes = OptInt(j, "foregroundEyes"); var fgBrow = OptInt(j, "foregroundEyebrow");
        if (fgEyes is < 0 or > 2 || fgBrow is < 0 or > 2) throw new ArgumentException("foregroundEyes/foregroundEyebrow must be 0..2");
        // Validate everything before touching the character.
        if (eyebrow.HasValue) CheckId(CategoryNo.mt_eyebrow, eyebrow.Value);
        if (up.HasValue) CheckId(CategoryNo.mt_eyeline_up, up.Value);
        if (down.HasValue) CheckId(CategoryNo.mt_eyeline_down, down.Value);
        if (eyelid.HasValue) CheckId(CategoryNo.mt_eyelid, eyelid.Value);
        if (white.HasValue) CheckId(CategoryNo.mt_eye_white, white.Value);
        if (nose.HasValue) CheckId(CategoryNo.mt_nose, nose.Value);
        if (lipLine.HasValue) CheckId(CategoryNo.mt_lipline, lipLine.Value);
        if (eye.HasValue) CheckId(CategoryNo.mt_eye, eye.Value);
        if (pupil.HasValue) CheckId(CategoryNo.mt_eyepipil, pupil.Value);
        if (detail.HasValue) CheckId(CategoryNo.mt_face_detail, detail.Value);
        PupilPresetDataList presets = null;
        if (preset.HasValue)
        {
            presets = PupilPresetDataList.Load();
            if (presets?.List == null || preset < 0 || preset >= presets.List.Count) throw new ArgumentException("eyePreset out of range; see GET creator/face-parts eyePresetCount");
            if (presetFlags < 1 || presetFlags > 3) throw new ArgumentException("eyePresetFlags must be 1 (shape), 2 (color) or 3 (both)");
        }

        if (preset.HasValue) presets.List[preset.Value].Set(f, (SettingFlag)presetFlags);
        if (fgEyes.HasValue) { f.foregroundEyes = (byte)fgEyes.Value; h.Face.ChangeSettingEyes(); }
        if (fgBrow.HasValue) { f.foregroundEyebrow = (byte)fgBrow.Value; h.Face.ChangeSettingEyebrow(new Il2CppSystem.Nullable<int>(f.eyebrowId)); }
        if (eyebrow.HasValue) h.Face.ChangeSettingEyebrow(new Il2CppSystem.Nullable<int>(eyebrow.Value));
        if (up.HasValue) h.Face.ChangeSettingEyelineUp(new Il2CppSystem.Nullable<int>(up.Value));
        if (down.HasValue) h.Face.ChangeSettingEyelineDown(new Il2CppSystem.Nullable<int>(down.Value));
        if (eyelid.HasValue) h.Face.ChangeSettingEyelid(new Il2CppSystem.Nullable<int>(eyelid.Value));
        if (white.HasValue) h.Face.ChangeSettingWhiteOfEyeID(new Il2CppSystem.Nullable<int>(white.Value));
        if (nose.HasValue) h.Face.ChangeSettingNose(new Il2CppSystem.Nullable<int>(nose.Value));
        if (lipLine.HasValue) h.Face.ChangeSettingLipLineID(new Il2CppSystem.Nullable<int>(lipLine.Value));
        if (detail.HasValue) h.Face.ChangeSettingDetailID(new Il2CppSystem.Nullable<int>(detail.Value));
        foreach (var p in f.pupil)
        {
            if (eye.HasValue) p.id = eye.Value;
            if (pupil.HasValue) p.overId = pupil.Value;
        }
        if (preset.HasValue || eye.HasValue || pupil.HasValue)
        {
            h.Face.ChangeSettingEyes();
            h.Face.ChangeSettingPupilTexture(2, true, true);
            h.Face.ChangeSettingEyeColor();
            h.Face.ChangeSettingEyePupilColor();
            h.Face.ChangeSettingEyeGradeColor();
        }
        return Ok(FaceParts(h));
    }

    // ---- 5. makeup and eye accent colors ------------------------------------------

    static object Makeup(Human h)
    {
        var m = h.Coorde.Now.FaceMakeup;
        return new
        {
            eyeshadowId = m.eyeshadowId, eyeshadowColor = C(m.eyeshadowColor),
            cheekId = m.cheekId, cheekColor = C(m.cheekColor), cheekHighlightColor = C(m.cheekHighlightColor),
            lipId = m.lipId, lipColor = C(m.lipColor), lipHighlightColor = C(m.lipHighlightColor),
            categories = new { eyeshadow = "mt_eyeshadow", cheek = "mt_cheek", lip = "mt_lip" }
        };
    }

    static ApiResult SetMakeup(JsonElement j, Human h)
    {
        var m = h.Coorde.Now.FaceMakeup;
        var shadowId = OptInt(j, "eyeshadowId"); var cheekId = OptInt(j, "cheekId"); var lipId = OptInt(j, "lipId");
        var shadowColor = OptCol(j, "eyeshadowColor"); var cheekColor = OptCol(j, "cheekColor");
        var cheekHl = OptCol(j, "cheekHighlightColor"); var lipColor = OptCol(j, "lipColor"); var lipHl = OptCol(j, "lipHighlightColor");
        var gradColor = OptCol(j, "eyeGradColor"); var hlColor = OptCol(j, "eyeHighlightColor");
        if (shadowId.HasValue) CheckId(CategoryNo.mt_eyeshadow, shadowId.Value);
        if (cheekId.HasValue) CheckId(CategoryNo.mt_cheek, cheekId.Value);
        if (lipId.HasValue) CheckId(CategoryNo.mt_lip, lipId.Value);

        if (shadowId.HasValue) m.eyeshadowId = shadowId.Value;
        if (shadowColor.HasValue) m.eyeshadowColor = shadowColor.Value;
        if (cheekId.HasValue) h.Face.ChangeSettingCheek(new Il2CppSystem.Nullable<int>(cheekId.Value));
        if (cheekColor.HasValue) h.Face.ChangeSettingCheekColor(new Il2CppSystem.Nullable<Color>(cheekColor.Value));
        if (cheekHl.HasValue) h.Face.ChangeSettingCheekHighlightColor(new Il2CppSystem.Nullable<Color>(cheekHl.Value));
        if (lipId.HasValue) h.Face.ChangeSettingLip(new Il2CppSystem.Nullable<int>(lipId.Value));
        if (lipColor.HasValue) h.Face.ChangeSettingLipColor(new Il2CppSystem.Nullable<Color>(lipColor.Value));
        if (lipHl.HasValue) h.Face.ChangeSettingLipHighlightColor(new Il2CppSystem.Nullable<Color>(lipHl.Value));
        if (gradColor.HasValue)
            for (byte lr = 0; lr < 2; lr++) h.Face.ChangeSettingEyeGradeColor(lr, new Il2CppSystem.Nullable<Color>(gradColor.Value));
        if (hlColor.HasValue)
            for (byte lr = 0; lr < 2; lr++)
                for (var i = 0; i < h.FileFace.pupil[lr].highlightInfos.Length; i++)
                    h.Face.ChangeSettingEyeHLColor(lr, i, new Il2CppSystem.Nullable<Color>(hlColor.Value));
        if (shadowId.HasValue || shadowColor.HasValue)
        {
            // Eyeshadow has no dedicated setter; it is baked into the face texture.
            h.Face.AddUpdateCMFaceFlagsFull();
            h.Face.CreateFaceTexture();
        }
        CreatorApi.SyncCoordinate(h);
        return Ok(Makeup(h));
    }

    // ---- 6. coordinates -----------------------------------------------------------

    static readonly string[] CoordinateToggles = { "01_Swim", "02_Pajama" };

    // Switching through the maker's own toggle keeps its UI in step with the character.
    internal static void ToggleCoordinate(int type)
    {
        if (type < 0 || type >= CoordinateToggles.Length) throw new ArgumentException("coordinate type must be 0 (swim) or 1 (pajama)");
        var toggle = UnityEngine.Object.FindObjectsOfType<Toggle>().FirstOrDefault(t => t.name == CoordinateToggles[type] && t.isActiveAndEnabled);
        if (toggle == null) throw new InvalidOperationException("coordinate toggle not visible; open the maker main screen");
        toggle.isOn = true;
    }

    static ApiResult SwitchCoordinate(JsonElement j, Human h)
    {
        var type = OptInt(j, "type") ?? -1;
        ToggleCoordinate(type);
        return Ok(new { requested = type, current = (int)h.FileStatus.coordinateType });
    }

    static ApiResult CopyCoordinate(JsonElement j, Human h)
    {
        var from = OptInt(j, "from") ?? -1; var to = OptInt(j, "to") ?? -1;
        var coords = h.Data.Coordinates;
        if (from < 0 || to < 0 || from >= coords.Length || to >= coords.Length || from == to) throw new ArgumentException("from/to must be different coordinate indices");
        var parts = j.TryGetProperty("parts", out var ps) ? ps.EnumerateArray().Select(x => x.GetString()).ToArray()
            : new[] { "hair", "accessory", "clothes", "makeup" };
        var unknown = parts.Except(new[] { "hair", "accessory", "clothes", "makeup" }).ToArray();
        if (unknown.Length > 0) throw new ArgumentException("unknown parts: " + string.Join(",", unknown));
        CreatorApi.SyncCoordinate(h);
        var src = coords[from]; var dst = coords[to];
        foreach (var part in parts)
            switch (part)
            {
                case "hair": dst.Hair.Copy(src.Hair); break;
                case "accessory": dst.Accessory.Copy(src.Accessory); break;
                case "clothes": dst.Clothes.Copy(src.Clothes); break;
                case "makeup": dst.FaceMakeup.Copy(src.FaceMakeup); break;
            }
        var current = (int)h.FileStatus.coordinateType;
        var reloaded = false;
        if (to == current)
        {
            // Re-enter the destination so the live model is rebuilt from the copied data.
            ToggleCoordinate(from);
            ToggleCoordinate(to);
            reloaded = true;
        }
        return Ok(new { from, to, parts, reloaded });
    }

    // ---- 3. reset to a neutral new character ---------------------------------------

    static ApiResult Reset(JsonElement j, Human h)
    {
        var face = new HumanDataFace();
        var body = new HumanDataBody();
        var keepProfile = j.TryGetProperty("keepProfile", out var kp) && kp.GetBoolean();
        h.Data.SkipRangeCheck = true;
        for (var i = 0; i < face.shapeValueFace.Length; i++) h.Face.SetShapeFaceValue(i, face.shapeValueFace[i]);
        for (var i = 0; i < body.shapeValueBody.Length; i++) h.Body.SetShapeBodyValue(i, body.shapeValueBody[i]);
        h.Face.UpdateShapeFaceValue();
        h.Body.UpdateShapeBodyValue();
        var fb = h.FileBody;
        fb.skinMainColor = body.skinMainColor; fb.skinShineId = body.skinShineId; fb.skinShinePower = body.skinShinePower;
        fb.sunburnUpId = 0; fb.sunburnDownId = 0;
        h.FileFace.moleInfo.ID = 0;
        h.Face.ChangeSettingEyebrow(new Il2CppSystem.Nullable<int>(face.eyebrowId));
        h.Face.ChangeSettingEyelineUp(new Il2CppSystem.Nullable<int>(face.eyelineUpId));
        h.Face.ChangeSettingEyelineDown(new Il2CppSystem.Nullable<int>(face.eyelineDownId));
        h.Face.ChangeSettingEyelid(new Il2CppSystem.Nullable<int>(face.eyelidId));
        h.Face.ChangeSettingNose(new Il2CppSystem.Nullable<int>(face.noseId));
        h.Body.AddUpdateCMBodyFlagsFull(); h.Face.AddUpdateCMFaceFlagsFull();
        h.Body.CreateBodyTexture(); h.Face.CreateFaceTexture();
        var slots = h.Coorde.Now.Accessory.parts.Length;
        for (var s = 0; s < slots; s++) if (h.Coorde.Now.Accessory.parts[s].type != (int)CategoryNo.ao_none) ClearSlot(h, s);
        for (var s = 0; s < h.Coorde.Now.Hair.parts.Length; s++)
        {
            var hp = h.Coorde.Now.Hair.parts[s];
            hp.useMesh = false; hp.useInner = false;
            h.Hair.ChangeSettingHairMeshColor(s); h.Hair.ChangeSettingHairInnerColor(s);
        }
        if (!keepProfile)
        {
            h.FileParam.lastname = "未設定"; h.FileParam.firstname = "新規"; h.FileParam.nickname = "新規";
            HumanCustom.Instance?.UpdateFullNameUI();
        }
        CreatorApi.SyncCoordinate(h);
        return Ok(new
        {
            reset = new[] { "faceShapes", "bodyShapes", "skin", "sunburn", "mole", "eyebrow", "eyelines", "eyelid", "nose", "accessories(current coordinate)", "hairMesh/inner(current coordinate)" },
            unchanged = new[] { "hair styles and colors", "clothes", "eye/pupil (use face-parts eyePreset)", "other coordinate" },
            note = "Choose parts explicitly afterwards; repeat for the other coordinate or use coordinate-copy."
        });
    }

    // ---- 8. poses ----------------------------------------------------------------------

    static int PoseCount(Il2CppSystem.Collections.Generic.IReadOnlyList<CharacterCreation.ListInfo.PoseInfoData> list) =>
        list == null ? 0 : list.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<CharacterCreation.ListInfo.PoseInfoData>>().Count;

    internal static object Poses(Human h)
    {
        var list = HumanCustom.Instance?.PoseList;
        var poses = new List<object>();
        if (list != null)
            for (var i = 0; i < PoseCount(list); i++) poses.Add(new { index = i, state = list[i].State, asset = list[i].Asset, bundle = list[i].Bundle });
        return new { controller = h.Body.animBody?.runtimeAnimatorController?.name, poses };
    }

    // Poses come from the maker's own list (index or state name), loading the controller when it differs.
    internal static Action ResolvePose(Human h, JsonElement pose)
    {
        var custom = HumanCustom.Instance ?? throw new ArgumentException("open character creation first");
        var list = custom.PoseList;
        if (PoseCount(list) == 0) throw new ArgumentException("the maker has no pose list");
        CharacterCreation.ListInfo.PoseInfoData data = null;
        if (pose.ValueKind == JsonValueKind.String)
        {
            var name = pose.GetString();
            for (var i = 0; i < PoseCount(list) && data == null; i++) if (list[i].State == name) data = list[i];
        }
        else
        {
            var index = pose.GetInt32();
            if (index >= 0 && index < PoseCount(list)) data = list[index];
        }
        if (data == null) throw new ArgumentException("unknown pose; see GET creator/poses");
        return () =>
        {
            if (h.Body.animBody?.runtimeAnimatorController?.name != data.Asset) custom.LoadAnimation(data.Bundle, data.Asset);
            custom.PlayAnimation(data.StateHash, new Il2CppSystem.Nullable<float>());
        };
    }

    // ---- 9. personalities ------------------------------------------------------------

    internal static int[] Personalities(HumanCustom custom)
    {
        var table = custom?.SampleVoiceTable;
        if (table == null) return Array.Empty<int>();
        return Enumerable.Range(0, 64).Where(i => table.ContainsKey(i)).ToArray();
    }

    // ---- 7. export / import ---------------------------------------------------------

    static object Op(string path, object body) => new { path, body };

    static List<object> Export(Human h)
    {
        CreatorApi.SyncCoordinate(h);
        var ops = new List<object>();
        var p = h.FileParam;
        ops.Add(Op("creator/profile", new { lastname = p.lastname, firstname = p.firstname, nickname = string.IsNullOrWhiteSpace(p.nickname) ? p.firstname : p.nickname, birthMonth = (int)p.birthMonth, birthDay = (int)p.birthDay, personality = p.personality, voiceRate = p.voiceRate, bloodType = (int)p.bloodType }));
        var fs = h.FileFace.shapeValueFace; var bs = h.FileBody.shapeValueBody;
        for (var i = 0; i < fs.Length; i++) ops.Add(Op("character/shape", new { region = "face", index = i, value = fs[i] }));
        for (var i = 0; i < bs.Length; i++) ops.Add(Op("character/shape", new { region = "body", index = i, value = bs[i] }));
        var f = h.FileFace;
        ops.Add(Op("creator/face-parts", new { eyebrow = f.eyebrowId, eyelineUp = f.eyelineUpId, eyelineDown = f.eyelineDownId, eyelid = f.eyelidId, white = f.whiteId, nose = f.noseId, lipLine = f.lipLineId, eye = f.pupil[0].id, pupil = f.pupil[0].overId, foregroundEyes = (int)f.foregroundEyes, foregroundEyebrow = (int)f.foregroundEyebrow }));
        ops.Add(Op("creator/eye-lines", new { eyelineColor = C(f.eyelineColor), eyelidColor = C(f.eyelidColor), eyelineUpWeight = f.eyelineUpWeight }));
        for (var lr = 0; lr < f.pupil.Length; lr++)
        {
            ops.Add(Op("creator/color", new { target = "eye", slot = lr, channel = 0, color = C(f.pupil[lr].eye01Color) }));
            ops.Add(Op("creator/color", new { target = "eye", slot = lr, channel = 1, color = C(f.pupil[lr].eye02Color) }));
            ops.Add(Op("creator/color", new { target = "eye", slot = lr, channel = 2, color = C(f.pupil[lr].eye03Color) }));
        }
        ops.Add(Op("creator/color", new { target = "eyebrow", color = C(f.eyebrowColor) }));
        ops.Add(Op("creator/makeup", f.pupil[0].highlightInfos.Length > 0
            ? new { eyeGradColor = C(f.pupil[0].eyeGradColor), eyeHighlightColor = C(f.pupil[0].highlightInfos[0].color) }
            : (object)new { eyeGradColor = C(f.pupil[0].eyeGradColor) }));
        var b = h.FileBody;
        ops.Add(Op("creator/skin", new { mainColor = C(b.skinMainColor), shineId = b.skinShineId, shinePower = b.skinShinePower, sunburnUpId = b.sunburnUpId, sunburnDownId = b.sunburnDownId, moleId = f.moleInfo.ID }));
        var original = (int)h.FileStatus.coordinateType;
        for (var c = 0; c < h.Data.Coordinates.Length && c < CoordinateToggles.Length; c++)
        {
            var co = h.Data.Coordinates[c];
            ops.Add(Op("creator/coordinate", new { type = c }));
            string[] hairKinds = { "hair_back", "hair_front", "hair_side", "hair_option" };
            for (var s = 0; s < co.Hair.parts.Length && s < hairKinds.Length; s++)
            {
                var hp = co.Hair.parts[s];
                ops.Add(Op("character/choice", new { kind = hairKinds[s], id = hp.id }));
                foreach (var (field, col) in new[] { ("base", hp.baseColor), ("start", hp.startColor), ("end", hp.endColor), ("outline", hp.outlineColor), ("gloss", hp.glossColor), ("shadow", hp.shadowColor), ("mesh", hp.meshColor), ("inner", hp.innerColor) })
                    ops.Add(Op("creator/color", new { target = "hair", slot = s, field, color = C(col) }));
                ops.Add(Op("creator/hair-flags", new { slot = s, useMesh = hp.useMesh, useInner = hp.useInner }));
                foreach (var kv in hp.dictBundle)
                    ops.Add(Op("creator/hair-bundle", new { part = s, index = kv.Key, moveRate = V(kv.Value.moveRate), rotRate = V(kv.Value.rotRate) }));
            }
            string[] clothKinds = { "clothes_top", "clothes_bot", "clothes_bra", "clothes_shorts", "clothes_gloves", "clothes_panst", "clothes_socks", "clothes_shoes", "clothes_add_arm", "clothes_add_leg", "clothes_add_other" };
            for (var s = 0; s < co.Clothes.parts.Length && s < clothKinds.Length; s++)
            {
                var cp = co.Clothes.parts[s];
                ops.Add(Op("character/choice", new { kind = clothKinds[s], id = cp.id }));
                for (var ch = 0; ch < cp.colorInfo.Length; ch++)
                {
                    var ci = cp.colorInfo[ch];
                    ops.Add(Op("creator/color", new { target = "clothes", slot = s, channel = ch, color = C(ci.baseColor) }));
                    ops.Add(Op("creator/clothes-pattern", new { slot = s, channel = ch, pattern = ci.patternInfo.pattern, patternColor = C(ci.patternInfo.patternColor), gloss = ci.gloss, metallic = ci.metallic }));
                }
            }
            for (var s = 0; s < co.Accessory.parts.Length; s++)
            {
                var ap = co.Accessory.parts[s];
                if (ap.type == (int)CategoryNo.ao_none)
                {
                    ops.Add(Op("creator/accessory-clear", new { slot = s }));
                    continue;
                }
                ops.Add(Op("creator/accessory", new { slot = s, category = ((CategoryNo)ap.type).ToString(), id = ap.id, parent = ap.parentKeyType }));
                for (var ch = 0; ch < ap.color.Length; ch++)
                    ops.Add(Op("creator/color", new { target = "accessory", slot = s, channel = ch, color = C(ap.color[ch]) }));
                var m = Moves(ap);
                if (m != null && m.Length >= 6)
                    for (var corr = 0; corr < 2; corr++)
                        ops.Add(Op("creator/accessory-move", new { slot = s, correct = corr, pos = V(m[corr * 3]), rot = V(m[corr * 3 + 1]), scl = V(m[corr * 3 + 2]) }));
            }
            var mk = co.FaceMakeup;
            ops.Add(Op("creator/makeup", new { eyeshadowId = mk.eyeshadowId, eyeshadowColor = C(mk.eyeshadowColor), cheekId = mk.cheekId, cheekColor = C(mk.cheekColor), cheekHighlightColor = C(mk.cheekHighlightColor), lipId = mk.lipId, lipColor = C(mk.lipColor), lipHighlightColor = C(mk.lipHighlightColor) }));
        }
        ops.Add(Op("creator/coordinate", new { type = original }));
        return ops;
    }

    // Replays operations through the public API so every step keeps its own validation.
    static ApiResult Import(JsonElement j)
    {
        if (!j.TryGetProperty("operations", out var opsElement) || opsElement.ValueKind != JsonValueKind.Array)
            throw new ArgumentException("provide {\"operations\":[{\"path\":...,\"body\":{...}}]} as returned by GET creator/export");
        var stopOnError = !j.TryGetProperty("stopOnError", out var soe) || soe.GetBoolean();
        var results = new List<object>();
        var index = 0; var failures = 0;
        foreach (var op in opsElement.EnumerateArray())
        {
            var path = op.GetProperty("path").GetString() ?? "";
            if (path.Contains("..") || path.StartsWith("/") || path == "creator/import") throw new ArgumentException($"operation {index}: invalid path");
            var r = GameApi.Execute("POST", "/api/v1/" + path, "", op.GetProperty("body").GetRawText());
            if (r.Status != 200)
            {
                failures++;
                results.Add(new { index, path, status = r.Status, body = r.Body });
                if (stopOnError) return new ApiResult(409, new { error = "import stopped", failedAt = index, results });
            }
            index++;
        }
        return Ok(new { applied = index - failures, failures, results });
    }
}
