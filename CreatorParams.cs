#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Character;
using CharacterCreation;
using UnityEngine;

namespace Amanatsu.AiExtension;

// creator/params (0.9.0): maker values that have no dedicated endpoint, read and written by name.
// Each entry carries what raising and lowering the value does, so an agent does not guess directions.
internal static class CreatorParams
{
    sealed class Ctx
    {
        public Human H;
        public HumanDataFace F => H.FileFace;
        public HumanDataBody B => H.FileBody;
        public HumanDataCoordinate Data;
        public HumanDataCoordinate Co => Data ?? H.Coorde.Now;
        public byte[] Eyes = { 0, 1 };
        public int Highlight, Part, Slot, Channel;
        public bool Foot;
        // Reloads are collected and run once after all values of a request are set.
        public bool ReloadCoordinate, ReloadHair, ReloadHead, ReloadAll;
    }

    sealed class P
    {
        public string Name, Scope, Type, Up, Down;
        public Func<Ctx, object> Get;
        public Action<Ctx, JsonElement> Set;
    }

    static float[] C(Color c) => new[] { c.r, c.g, c.b, c.a };
    static float[] V(Vector2 v) => new[] { v.x, v.y };
    static Color Col(JsonElement j)
    {
        var v = j.EnumerateArray().Select(x => x.GetSingle()).ToArray();
        if ((v.Length != 3 && v.Length != 4) || v.Any(x => !float.IsFinite(x) || x < 0 || x > 1)) throw new ArgumentException("color must be 3 or 4 channels in 0..1");
        return new Color(v[0], v[1], v[2], v.Length == 4 ? v[3] : 1);
    }
    static float F01(JsonElement j)
    {
        var v = j.GetSingle();
        if (!float.IsFinite(v)) throw new ArgumentException("value must be a number");
        return v;
    }
    static Il2CppSystem.Nullable<float> NF(float v) => new(v);
    // The second (absolute value) argument of the two-argument setters must be passed as an explicit empty Nullable.
    static readonly Il2CppSystem.Nullable<float> NoAbs = new();
    static Il2CppSystem.Nullable<Color> NC(Color c) => new(c);
    static Il2CppSystem.Nullable<int> NI(int v) => new(v);

    static P Float(string name, string scope, Func<Ctx, float> get, Action<Ctx, float> set, string up, string down) =>
        new() { Name = name, Scope = scope, Type = "float", Up = up, Down = down, Get = c => get(c), Set = (c, j) => set(c, F01(j)) };
    static P Colr(string name, string scope, Func<Ctx, Color> get, Action<Ctx, Color> set, string what) =>
        new() { Name = name, Scope = scope, Type = "color", Up = what, Down = what, Get = c => C(get(c)), Set = (c, j) => set(c, Col(j)) };
    static P Int(string name, string scope, Func<Ctx, int> get, Action<Ctx, int> set, string what) =>
        new() { Name = name, Scope = scope, Type = "id", Up = what, Down = what, Get = c => get(c), Set = (c, j) => set(c, j.GetInt32()) };
    static P Bool(string name, string scope, Func<Ctx, bool> get, Action<Ctx, bool> set, string on, string off) =>
        new() { Name = name, Scope = scope, Type = "bool", Up = on, Down = off, Get = c => get(c), Set = (c, j) => set(c, j.GetBoolean()) };

    static P Bools(string name, string scope, Func<Ctx, Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<bool>> get, Action<Ctx> apply, string what) =>
        new() { Name = name, Scope = scope, Type = "bool[]", Up = what, Down = what, Get = c => get(c).ToArray(),
            Set = (c, j) => { var arr = get(c); var v = j.EnumerateArray().Select(x => x.GetBoolean()).ToArray(); if (v.Length != arr.Length) throw new ArgumentException($"{name} needs {arr.Length} values"); for (var i = 0; i < v.Length; i++) arr[i] = v[i]; apply(c); } };

    static void FaceTex(Ctx c) { c.H.Face.AddUpdateCMFaceFlagsFull(); c.H.Face.CreateFaceTexture(); }
    static void BodyTex(Ctx c) { c.H.Body.AddUpdateCMBodyFlagsFull(); c.H.Body.CreateBodyTexture(); }
    static HumanDataFace.PupilInfo Pupil(Ctx c, byte lr) => c.F.pupil[lr];
    static HumanDataFace.HighlightInfo Hl(Ctx c, byte lr)
    {
        var list = c.F.pupil[lr].highlightInfos;
        if (c.Highlight < 0 || c.Highlight >= list.Length) throw new ArgumentException($"highlight must be 0..{list.Length - 1}");
        return list[c.Highlight];
    }
    static HumanDataHair.PartsInfo HairPart(Ctx c)
    {
        if (c.Part < 0 || c.Part >= c.Co.Hair.parts.Length) throw new ArgumentException("part must be 0 (back), 1 (front), 2 (side) or 3 (option)");
        return c.Co.Hair.parts[c.Part];
    }
    static HumanDataClothes.PartsInfo Cloth(Ctx c)
    {
        if (c.Slot < 0 || c.Slot >= c.Co.Clothes.parts.Length) throw new ArgumentException($"slot must be 0..{c.Co.Clothes.parts.Length - 1}");
        return c.Co.Clothes.parts[c.Slot];
    }
    static HumanDataClothes.PartsInfo.ColorInfo ClothColor(Ctx c)
    {
        var p = Cloth(c);
        if (c.Channel < 0 || c.Channel >= p.colorInfo.Length) throw new ArgumentException($"channel must be 0..{p.colorInfo.Length - 1}");
        return p.colorInfo[c.Channel];
    }
    static HumanDataAccessory.PartsInfo Acs(Ctx c)
    {
        if (c.Slot < 0 || c.Slot >= c.Co.Accessory.parts.Length) throw new ArgumentException($"slot must be 0..{c.Co.Accessory.parts.Length - 1}");
        return c.Co.Accessory.parts[c.Slot];
    }
    static HumanDataBodyMakeup.NailInfo Nail(Ctx c) => c.Foot ? c.Co.BodyMakeup.nailLegInfo : c.Co.BodyMakeup.nailInfo;
    static void ReloadClothes(Ctx c) { c.ReloadCoordinate = true; }
    // During creator/import the reloads of all params operations are merged and run once at the end.
    static Ctx _batch;
    internal static void BeginBatch(Human h) => _batch = new Ctx { H = h };
    internal static void EndBatch()
    {
        var batch = _batch; _batch = null;
        if (batch == null) return;
        FlushReloads(batch);
        CreatorApi.SyncCoordinate(batch.H);
    }
    static void FlushReloads(Ctx c)
    {
        if (_batch != null && c != _batch)
        {
            _batch.ReloadAll |= c.ReloadAll; _batch.ReloadCoordinate |= c.ReloadCoordinate;
            _batch.ReloadHair |= c.ReloadHair; _batch.ReloadHead |= c.ReloadHead;
            return;
        }
        if (c.ReloadAll) { CreatorApi.SyncCoordinate(c.H); c.H.Reload(); return; }
        if (c.ReloadCoordinate) c.H.ReloadCoordinate();
        if (c.ReloadHair) c.H.ReloadHair();
        if (c.ReloadHead) c.H.ReloadHead();
    }

    static readonly P[] All = Build();

    static P[] Build()
    {
        var list = new List<P>
        {
            // ---- eyes (both eyes unless "eye" is 0 = right or 1 = left)
            Float("pupilWidth", "face", c => c.F.pupilWidth, (c, v) => c.H.Face.ChangeSettingEyePupilWidth(NF(v)),
                "the pupil (dark centre of the iris) becomes wider", "the pupil becomes narrower"),
            Float("pupilHeight", "face", c => c.F.pupilHeight, (c, v) => c.H.Face.ChangeSettingEyePupilHeight(NF(v)),
                "the pupil becomes taller", "the pupil becomes shorter"),
            Float("irisWidth", "face", c => c.F.eyeWidth, (c, v) => c.H.Face.ChangeSettingEyeWidth(NF(v)),
                "the iris (coloured part) becomes wider", "the iris becomes narrower"),
            Float("irisHeight", "face", c => c.F.eyeHeight, (c, v) => c.H.Face.ChangeSettingEyeHeight(NF(v)),
                "the iris becomes taller", "the iris becomes shorter"),
            Float("irisX", "face", c => c.F.eyeX, (c, v) => c.H.Face.ChangeSettingEyePosX(NF(v)),
                "the irises move toward the nose", "the irises move toward the outer corners"),
            Float("irisY", "face", c => c.F.eyeY, (c, v) => c.H.Face.ChangeSettingEyePosY(NF(v)),
                "the irises move up", "the irises move down"),
            Float("eyeGradPosition", "eye", c => Pupil(c, c.Eyes[0]).eyeGradPosition, (c, v) => { foreach (var lr in c.Eyes) c.H.Face.ChangeSettingEyeGradePosition(lr, NF(v)); },
                "the iris gradient moves down (the top of the iris keeps the base colour)", "the gradient moves up"),
            Float("eyeGradSize", "eye", c => Pupil(c, c.Eyes[0]).eyeGradSize, (c, v) => { foreach (var lr in c.Eyes) c.H.Face.ChangeSettingEyeGradeSize(lr, NF(v)); },
                "the iris gradient covers more of the iris", "the gradient covers less"),
            Float("eyelidX", "face", c => c.F.eyelidPos.x, (c, v) => c.H.Face.ChangeSettingEyelidPositionX(NF(v)),
                "the double-eyelid line moves toward the inner corner", "it moves toward the outer corner"),
            Float("eyelidY", "face", c => c.F.eyelidPos.y, (c, v) => c.H.Face.ChangeSettingEyelidPositionY(NF(v)),
                "the double-eyelid line moves up", "it moves down"),
            Float("eyelidRotation", "face", c => c.F.eyelidRotation, (c, v) => c.H.Face.ChangeSettingEyelidRotation(NF(v)),
                "the double-eyelid line tilts (which way is not confirmed yet; check with a capture)", "it tilts the other way"),
            Float("eyelidSize", "face", c => c.F.eyelidSize, (c, v) => c.H.Face.ChangeSettingEyelidSize(NF(v)),
                "the double-eyelid line becomes larger", "it becomes smaller"),
            Float("eyebrowWidth", "face", c => c.F.eyebrowWidth, (c, v) => c.H.Face.ChangeSettingEyebrowWidth(NF(v), NoAbs),
                "the eyebrow texture becomes wider (longer)", "it becomes narrower (shorter)"),
            Float("eyebrowHeight", "face", c => c.F.eyebrowHeight, (c, v) => c.H.Face.ChangeSettingEyebrowHeight(NF(v), NoAbs),
                "the eyebrow becomes thicker", "it becomes thinner"),
            Float("noseGloss", "face", c => c.F.noseGlossIntensity, (c, v) => c.H.Face.ChangeSettingNoseGlossIntensity(NF(v)),
                "the nose highlight becomes stronger", "it becomes weaker"),
            Float("faceDetailPower", "face", c => c.F.detailPower, (c, v) => c.H.Face.ChangeSettingDetailPower(NF(v)),
                "facial shading details become stronger", "they become fainter"),
            Float("hairTransparency", "face", c => c.F.hairTransparency, (c, v) => { c.F.hairTransparency = v; c.ReloadHair = true; },
                "bangs become more see-through over the eyes", "bangs become more opaque"),
            Bool("doubleTooth", "face", c => c.F.doubleTooth, (c, v) => { c.F.doubleTooth = v; c.ReloadHead = true; },
                "a snaggletooth (yaeba) is shown", "no snaggletooth"),
            Int("pupilEditType", "face", c => c.F.pupilEditType, (c, v) => c.F.pupilEditType = (byte)v,
                "how the maker edits the two eyes (0 = both together, 1 = separately); no visual change"),
            Colr("eyebrowColor2", "face", c => c.F.eyebrowColor2, (c, v) => { c.F.eyebrowColor2 = v; c.H.Face.ChangeSettingEyebrowColor(1, NC(v)); },
                "second eyebrow colour"),
            Colr("noseColor", "face", c => c.F.noseColor, (c, v) => c.H.Face.ChangeSettingNoseColor(NC(v)), "nose line colour"),
            Colr("faceDetailColor", "face", c => c.F.detailColor, (c, v) => c.H.Face.ChangeSettingDetailColor(NC(v)), "facial shading colour"),
            Colr("whiteBaseColor", "face", c => c.F.whiteBaseColor, (c, v) => { c.F.whiteBaseColor = v; c.H.Face.ChangeSettingWhiteOfEyeColor(0, NC(v)); }, "main colour of the white of the eye"),
            Colr("whiteSubColor", "face", c => c.F.whiteSubColor, (c, v) => { c.F.whiteSubColor = v; c.H.Face.ChangeSettingWhiteOfEyeColor(1, NC(v)); }, "second colour of the white of the eye"),
            Colr("whiteSub2Color", "face", c => c.F.whiteSub2Color, (c, v) => { c.F.whiteSub2Color = v; c.H.Face.ChangeSettingWhiteOfEyeColor(2, NC(v)); }, "third colour of the white of the eye"),
            Colr("eyelineColor2", "face", c => c.F.eyelineColor2, (c, v) => { c.F.eyelineColor2 = v; c.H.Face.ChangeSettingEyelineUp(NI(c.F.eyelineUpId)); c.H.Face.ChangeSettingEyelineDown(NI(c.F.eyelineDownId)); }, "second lash colour"),
            Colr("eyelineColor3", "face", c => c.F.eyelineColor3, (c, v) => { c.F.eyelineColor3 = v; c.H.Face.ChangeSettingEyelineUp(NI(c.F.eyelineUpId)); c.H.Face.ChangeSettingEyelineDown(NI(c.F.eyelineDownId)); }, "third lash colour"),
        };
        for (var i = 0; i < 3; i++)
        {
            var index = i;
            list.Add(Colr($"pupilColor{i + 1}", "eye", c => index == 0 ? Pupil(c, c.Eyes[0]).pupil01Color : index == 1 ? Pupil(c, c.Eyes[0]).pupil02Color : Pupil(c, c.Eyes[0]).pupil03Color,
                (c, v) => { foreach (var lr in c.Eyes) c.H.Face.ChangeSettingEyePupilColor(lr, index, NC(v)); }, $"pupil colour {index + 1}"));
        }
        // ---- eye highlights ("eye" and "highlight" index)
        list.AddRange(new[]
        {
            Int("highlightId", "highlight", c => Hl(c, c.Eyes[0]).id, (c, v) => { foreach (var lr in c.Eyes) c.H.Face.ChangeSettingEyeHL(lr, c.Highlight, NI(v)); }, "highlight shape ID"),
            Float("highlightX", "highlight", c => Hl(c, c.Eyes[0]).x, (c, v) => { foreach (var lr in c.Eyes) c.H.Face.ChangeSettingEyeHLPosX(lr, c.Highlight, NF(v)); },
                "the highlight moves to the character's right (the viewer's left) in both eyes", "it moves to the character's left"),
            Float("highlightY", "highlight", c => Hl(c, c.Eyes[0]).y, (c, v) => { foreach (var lr in c.Eyes) c.H.Face.ChangeSettingEyeHLPosY(lr, c.Highlight, NF(v)); },
                "the highlight moves vertically (which way is not confirmed yet; check with a capture)", "it moves the other way"),
            Float("highlightWidth", "highlight", c => Hl(c, c.Eyes[0]).width, (c, v) => { foreach (var lr in c.Eyes) c.H.Face.ChangeSettingEyeHLWidth(lr, c.Highlight, NF(v)); },
                "the highlight becomes wider", "it becomes narrower"),
            Float("highlightHeight", "highlight", c => Hl(c, c.Eyes[0]).height, (c, v) => { foreach (var lr in c.Eyes) c.H.Face.ChangeSettingEyeHLHeight(lr, c.Highlight, NF(v)); },
                "the highlight becomes taller", "it becomes shorter"),
            Float("highlightTilt", "highlight", c => Hl(c, c.Eyes[0]).rotation, (c, v) => { foreach (var lr in c.Eyes) c.H.Face.ChangeSettingEyeHLTilt(lr, c.Highlight, NF(v)); },
                "the highlight rotates (which way is not confirmed yet; check with a capture)", "it rotates the other way"),
            Colr("highlightColor", "highlight", c => Hl(c, c.Eyes[0]).color, (c, v) => { foreach (var lr in c.Eyes) c.H.Face.ChangeSettingEyeHLColor(lr, c.Highlight, NC(v)); }, "highlight colour"),
            Colr("highlightColor2", "highlight", c => Hl(c, c.Eyes[0]).color2, (c, v) => { foreach (var lr in c.Eyes) c.H.Face.ChangeSettingEyeHLColor_02(lr, c.Highlight, NC(v)); }, "second highlight colour"),
        });
        // ---- makeup (current coordinate)
        list.AddRange(new[]
        {
            Float("cheekX", "makeup", c => c.Co.FaceMakeup.cheekPos.x, (c, v) => c.H.Face.ChangeSettingCheekPositionX(NF(v)),
                "the blush moves outward (toward the ears)", "the blush moves inward (toward the nose)"),
            Float("cheekY", "makeup", c => c.Co.FaceMakeup.cheekPos.y, (c, v) => c.H.Face.ChangeSettingCheekPositionY(NF(v)),
                "the blush moves up", "the blush moves down"),
            Float("cheekRotation", "makeup", c => c.Co.FaceMakeup.cheekRotation, (c, v) => c.H.Face.ChangeSettingCheekRotation(NF(v)),
                "the blush rotates (which way is not confirmed yet; check with a capture)", "it rotates the other way"),
            Float("cheekSize", "makeup", c => c.Co.FaceMakeup.cheekSize, (c, v) => c.H.Face.ChangeSettingCheekSize(NF(v)),
                "the blush becomes larger", "it becomes smaller"),
        });
        for (var i = 0; i < 3; i++)
        {
            var index = i;
            list.Add(Int($"facePaint{i + 1}Id", "makeup", c => PaintAt(c.Co.FaceMakeup.paintInfos, index).ID, (c, v) => { PaintAt(c.Co.FaceMakeup.paintInfos, index).ID = v; FaceTex(c); }, "face paint ID (0 = none)"));
            list.Add(Colr($"facePaint{i + 1}Color", "makeup", c => PaintAt(c.Co.FaceMakeup.paintInfos, index).color, (c, v) => { PaintAt(c.Co.FaceMakeup.paintInfos, index).color = v; FaceTex(c); }, "face paint colour"));
            list.Add(Int($"facePaint{i + 1}Layout", "makeup", c => PaintAt(c.Co.FaceMakeup.paintInfos, index).layoutID, (c, v) => { PaintAt(c.Co.FaceMakeup.paintInfos, index).layoutID = v; FaceTex(c); }, "face paint placement preset"));
        }
        // ---- body
        list.AddRange(new[]
        {
            Float("bustSoftness", "body", c => c.B.bustSoftness, (c, v) => c.H.Body.ChangeBustSpring(NF(v), NoAbs), "the bust sways more", "the bust is firmer"),
            Float("bustWeight", "body", c => c.B.bustWeight, (c, v) => c.H.Body.ChangeBustGravity(NF(v), NoAbs), "the bust hangs lower (heavier)", "the bust sits higher (lighter)"),
            Float("upperArmSoftness", "body", c => c.B.upperArmSoftness, (c, v) => c.H.Body.ChangeUpperArmSoftness(NF(v), NoAbs), "upper arms sway more", "upper arms are firmer"),
            Float("bellySoftness", "body", c => c.B.bellySoftness, (c, v) => c.H.Body.ChangeBellySoftness(NF(v), NoAbs), "the belly sways more", "the belly is firmer"),
            Float("waistSoftness", "body", c => c.B.waistSoftness, (c, v) => c.H.Body.ChangeWaistSoftness(NF(v), NoAbs), "the waist sways more", "the waist is firmer"),
            Float("hipSoftness", "body", c => c.B.hipSoftness, (c, v) => c.H.Body.ChangeHipSoftness(NF(v), NoAbs), "the hips sway more", "the hips are firmer"),
            Float("thighSoftness", "body", c => c.B.thighSoftness, (c, v) => c.H.Body.ChangeThighSoftness(NF(v), NoAbs), "thighs sway more", "thighs are firmer"),
            Float("bodyDetailPower", "body", c => c.B.detailPower, (c, v) => c.H.Body.ChangeSettingBodyDetailPower(NF(v)), "body shading details become stronger", "they become fainter"),
            Float("areolaSize", "body", c => c.B.areolaSize, (c, v) => c.H.Body.ChangeSettingAreolaSize(NF(v), NoAbs), "areolae become larger", "areolae become smaller"),
            Float("nipGloss", "body", c => c.B.nipGlossPower, (c, v) => { c.B.nipGlossPower = v; c.H.Body.ChangeSettingNip(NI(c.B.nipId)); }, "nipples look glossier", "nipples look matte"),
            Int("nipId", "body", c => c.B.nipId, (c, v) => c.H.Body.ChangeSettingNip(NI(v)), "nipple type ID"),
            Colr("nipColor", "body", c => c.B.nipColor, (c, v) => c.H.Body.ChangeSettingNipColor(NC(v)), "nipple colour"),
            Colr("nipColor2", "body", c => c.B.nipColor2, (c, v) => { c.B.nipColor2 = v; c.H.Body.ChangeSettingNipColor(NC(c.B.nipColor)); }, "second nipple colour"),
            Int("underhairId", "body", c => c.B.underhairId, (c, v) => c.H.Body.ChangeSettingUnderhair(NI(v)), "pubic hair type ID (0 = none)"),
            Colr("underhairColor", "body", c => c.B.underhairColor, (c, v) => c.H.Body.ChangeSettingUnderhairColor(NC(v)), "pubic hair colour"),
            Colr("skinHighlightColor", "body", c => c.B.skinHighlightColor, (c, v) => { c.B.skinHighlightColor = v; BodyTex(c); FaceTex(c); }, "skin highlight colour"),
            Colr("skinShadowColor", "body", c => c.B.skinShadowColor, (c, v) => { c.B.skinShadowColor = v; BodyTex(c); FaceTex(c); c.H.Face.UpdateShadowColor(); }, "skin shadow colour"),
            Colr("sunburnColor", "body", c => c.B.sunburnColor, (c, v) => { c.B.sunburnColor = v; BodyTex(c); }, "tan colour"),
            Colr("sunburnUpColor", "body", c => c.B.sunburnUpColor, (c, v) => { c.B.sunburnUpColor = v; BodyTex(c); }, "upper tan-line colour"),
            Colr("sunburnDownColor", "body", c => c.B.sunburnDownColor, (c, v) => { c.B.sunburnDownColor = v; BodyTex(c); }, "lower tan-line colour"),
            Bool("drawAddLine", "body", c => c.B.drawAddLine, (c, v) => { c.B.drawAddLine = v; c.H.Body.VisibleAddBodyLine(new Il2CppSystem.Nullable<bool>(v)); }, "extra body outlines are drawn", "no extra body outlines"),
            Bool("drawBustShadow", "body", c => c.B.drawBustShadow, (c, v) => { c.B.drawBustShadow = v; c.H.Body.VisibleBustShadow(new Il2CppSystem.Nullable<bool>(v)); }, "the shadow under the bust is drawn", "no bust shadow"),
        });
        for (var i = 0; i < 3; i++)
        {
            var index = i;
            list.Add(Colr($"skinDetailColor{i + 1}", "body", c => c.B.skinDetailColors[index], (c, v) => { c.B.skinDetailColors[index] = v; BodyTex(c); FaceTex(c); }, $"skin detail colour {index + 1}"));
        }
        // ---- nails ("foot": true for toenails)
        list.Add(Int("nailId", "nail", c => Nail(c).ID, (c, v) => { Nail(c).ID = v; BodyTex(c); }, "nail type ID (0 = none)"));
        list.Add(Float("nailGloss", "nail", c => Nail(c).glossPower, (c, v) => { Nail(c).glossPower = v; BodyTex(c); }, "nails look glossier", "nails look matte"));
        for (var i = 0; i < 3; i++)
        {
            var index = i;
            list.Add(Colr($"nailColor{i + 1}", "nail", c => Nail(c).colors[index], (c, v) => { Nail(c).colors[index] = v; BodyTex(c); }, $"nail colour {index + 1}"));
        }
        // ---- hair ("part": 0 back, 1 front, 2 side, 3 option)
        list.AddRange(new[]
        {
            Colr("hairGlossColor2", "hair", c => HairPart(c).glossColor2, (c, v) => { HairPart(c).glossColor2 = v; c.H.Hair.ChangeSettingHairGlossColor(c.Part, 1); }, "second hair gloss colour"),
            Int("hairGlossId", "hairAll", c => c.Co.Hair.glossId, (c, v) => { c.Co.Hair.glossId = v; c.H.Hair.ChangeSettingHairGlossMaskAll(); }, "hair gloss pattern ID (all hair parts)"),
            Float("hairGlossSize", "hairAll", c => c.Co.Hair.glossSize, (c, v) => { c.Co.Hair.glossSize = v; c.H.Hair.ChangeSettingHairGlossSizeAll(); }, "the hair gloss band becomes wider (all hair parts)", "it becomes narrower"),
        });
        for (var i = 0; i < 3; i++)
        {
            var index = i;
            list.Add(Colr($"hairAcsColor{i + 1}", "hair", c => HairPart(c).acsColor[index], (c, v) => { HairPart(c).acsColor[index] = v; c.H.Hair.ChangeSettingHairAcsColor(c.Part); }, $"colour {index + 1} of hair ornaments built into the style"));
        }
        // ---- clothes ("slot" and pattern "channel")
        list.AddRange(new[]
        {
            Float("patternTilingX", "clothes", c => ClothColor(c).patternInfo.tiling.x, (c, v) => { var t = ClothColor(c).patternInfo.tiling; t.x = v; ClothColor(c).patternInfo.tiling = t; ReloadClothes(c); }, "the pattern repeats more often horizontally (smaller)", "the pattern repeats less (larger)"),
            Float("patternTilingY", "clothes", c => ClothColor(c).patternInfo.tiling.y, (c, v) => { var t = ClothColor(c).patternInfo.tiling; t.y = v; ClothColor(c).patternInfo.tiling = t; ReloadClothes(c); }, "the pattern repeats more often vertically (smaller)", "the pattern repeats less (larger)"),
            Float("patternOffsetX", "clothes", c => ClothColor(c).patternInfo.offset.x, (c, v) => { var t = ClothColor(c).patternInfo.offset; t.x = v; ClothColor(c).patternInfo.offset = t; ReloadClothes(c); }, "the pattern shifts horizontally", "it shifts the other way"),
            Float("patternOffsetY", "clothes", c => ClothColor(c).patternInfo.offset.y, (c, v) => { var t = ClothColor(c).patternInfo.offset; t.y = v; ClothColor(c).patternInfo.offset = t; ReloadClothes(c); }, "the pattern shifts vertically", "it shifts the other way"),
            Float("patternRotate", "clothes", c => ClothColor(c).patternInfo.rotate, (c, v) => { ClothColor(c).patternInfo.rotate = v; ReloadClothes(c); }, "the pattern rotates", "it rotates the other way"),
            Float("clothesDent", "clothes", c => Cloth(c).DentPower, (c, v) => { Cloth(c).DentPower = v; ReloadClothes(c); }, "the garment presses into the skin more", "it presses in less"),
            Int("emblemId", "clothes", c => Cloth(c).emblemeId, (c, v) => { Cloth(c).emblemeId = v; c.H.Cloth.ChangeCustomEmblem(c.Slot, 0); }, "first emblem ID (0 = none)"),
            Int("emblemId2", "clothes", c => Cloth(c).emblemeId2, (c, v) => { Cloth(c).emblemeId2 = v; c.H.Cloth.ChangeCustomEmblem(c.Slot, 1); }, "second emblem ID (0 = none)"),
            Int("sleevesType", "clothes", c => Cloth(c).sleevesType, (c, v) => { Cloth(c).sleevesType = v; ReloadClothes(c); }, "sleeve variant of the garment"),
            Int("hideCategory", "clothes", c => Cloth(c).hideCategory, (c, v) => { Cloth(c).hideCategory = v; ReloadClothes(c); }, "which other clothing this garment hides"),
        });
        for (var i = 0; i < 3; i++)
        {
            var index = i;
            list.Add(Int($"topSubParts{i + 1}", "clothesAll", c => c.Co.Clothes.subPartsId[index], (c, v) => { c.Co.Clothes.subPartsId[index] = v; ReloadClothes(c); }, $"sub part {index + 1} of the top (collar, sleeves and similar)"));
        }
        list.Add(Int("shirtType", "clothesAll", c => c.Co.Clothes.shirtType, (c, v) => { c.Co.Clothes.shirtType = (byte)v; ReloadClothes(c); }, "how the top is worn (tucked in or out)"));
        // ---- accessories ("slot")
        list.AddRange(new[]
        {
            Bool("partsOfHead", "accessory", c => Acs(c).partsOfHead, (c, v) => { Acs(c).partsOfHead = v; ReloadClothes(c); }, "the accessory counts as part of the head", "it counts as a separate accessory"),
            Int("accessoryHideCategory", "accessory", c => Acs(c).hideCategory, (c, v) => { Acs(c).hideCategory = v; ReloadClothes(c); }, "which body or hair category the accessory hides"),
            Int("accessoryHideClothes", "accessory", c => Acs(c).hideCategoryClothes, (c, v) => { Acs(c).hideCategoryClothes = v; ReloadClothes(c); }, "the clothing category whose removal also hides this accessory"),
            Bool("accessoryNoShake", "accessory", c => Acs(c).noShake, (c, v) => { Acs(c).noShake = v; c.H.Acs.ChangeShakeAccessory(c.Slot); }, "the accessory does not sway", "it sways"),
        });
        // ---- clothes and accessory flags, paints, hair options, FK
        list.Add(Bools("hideOpt", "clothes", c => Cloth(c).hideOpt, ReloadClothes, "per option part of this garment: true hides that part"));
        list.Add(Bools("hideBraOpt", "clothesAll", c => c.Co.Clothes.hideBraOpt, ReloadClothes, "per bra option part: true hides it"));
        list.Add(Bools("hideShortsOpt", "clothesAll", c => c.Co.Clothes.hideShortsOpt, ReloadClothes, "per panty option part: true hides it"));
        list.Add(Bools("visibleTimings", "accessory", c => Acs(c).visibleTimings, ReloadClothes, "per game situation: true shows the accessory then"));
        list.Add(Bool("accessoryFkUse", "accessory", c => Acs(c).fkInfo.use, (c, v) => { Acs(c).fkInfo.use = v; c.H.Acs.SetupAccessoryFK(c.Slot, new Il2CppSystem.Nullable<bool>(false)); }, "the accessory's FK bones (bendable hair pieces) are used", "FK bones are not used"));
        list.Add(new P { Name = "accessoryFkBones", Scope = "accessory", Type = "vector3[]", Up = "rotation of each FK bone in degrees [x,y,z], from the root outward", Down = "rotation of each FK bone in degrees [x,y,z], from the root outward",
            Get = c => Acs(c).fkInfo.bones.Select(b => new[] { b.x, b.y, b.z }).ToArray(),
            Set = (c, j) =>
            {
                var bones = Acs(c).fkInfo.bones;
                var v = j.EnumerateArray().Select(x => x.EnumerateArray().Select(y => y.GetSingle()).ToArray()).ToArray();
                if (v.Length != bones.Length || v.Any(x => x.Length != 3)) throw new ArgumentException($"accessoryFkBones needs {bones.Length} [x,y,z] values");
                for (var i = 0; i < v.Length; i++) bones[i] = new Vector3(v[i][0], v[i][1], v[i][2]);
                c.H.Acs.UpdateAccessoryFK(c.Slot);
            } });
        list.Add(new P { Name = "hairOptions", Scope = "hair", Type = "map", Up = "optional pieces of this hair part by ID: true shows the piece", Down = "optional pieces of this hair part by ID: true shows the piece",
            Get = c => { var d = new Dictionary<string, bool>(); foreach (var kv in HairPart(c).dictOption) d[kv.Key.ToString()] = kv.Value; return d; },
            Set = (c, j) => { foreach (var prop in j.EnumerateObject()) { var id = int.Parse(prop.Name); HairPart(c).dictOption[id] = prop.Value.GetBoolean(); c.H.Hair.ChangeOptionHair(c.Part, id, prop.Value.GetBoolean()); } } });
        for (var i = 0; i < 3; i++)
        {
            var index = i;
            list.Add(Int($"bodyPaint{i + 1}Id", "makeup", c => PaintAt(c.Co.BodyMakeup.paintInfos, index).ID, (c, v) => { PaintAt(c.Co.BodyMakeup.paintInfos, index).ID = v; BodyTex(c); }, "body paint ID (0 = none)"));
            list.Add(Colr($"bodyPaint{i + 1}Color", "makeup", c => PaintAt(c.Co.BodyMakeup.paintInfos, index).color, (c, v) => { PaintAt(c.Co.BodyMakeup.paintInfos, index).color = v; BodyTex(c); }, "body paint colour"));
            list.Add(Int($"bodyPaint{i + 1}Layout", "makeup", c => PaintAt(c.Co.BodyMakeup.paintInfos, index).layoutID, (c, v) => { PaintAt(c.Co.BodyMakeup.paintInfos, index).layoutID = v; BodyTex(c); }, "body paint placement preset"));
        }
        for (var i = 0; i < 3; i++)
        {
            var index = i;
            HumanDataPaintInfo Paint(Ctx c) { var arr = Cloth(c).paintInfos; if (arr == null || index >= arr.Length) throw new ArgumentException("this clothes paint slot does not exist"); return arr[index]; }
            list.Add(Int($"clothesPaint{i + 1}Id", "clothes", c => Paint(c).ID, (c, v) => { Paint(c).ID = v; ReloadClothes(c); }, "clothes paint ID (0 = none)"));
            list.Add(Colr($"clothesPaint{i + 1}Color", "clothes", c => Paint(c).color, (c, v) => { Paint(c).color = v; ReloadClothes(c); }, "clothes paint colour"));
            list.Add(new P { Name = $"clothesPaint{i + 1}Layout", Scope = "clothes", Type = "vector4", Up = "paint placement [x, y, scale, rotation] on the garment", Down = "paint placement [x, y, scale, rotation] on the garment",
                Get = c => { var l = Paint(c).layout; return new[] { l.x, l.y, l.z, l.w }; },
                Set = (c, j) => { var v = j.EnumerateArray().Select(x => x.GetSingle()).ToArray(); if (v.Length != 4) throw new ArgumentException("layout needs 4 values"); Paint(c).layout = new Vector4(v[0], v[1], v[2], v[3]); ReloadClothes(c); } });
        }
        // ---- paint and mole placement: layout x = left/right, y = up/down, z = rotation, w = size
        P Layout(string name, string scope, Func<Ctx, HumanDataPaintInfo> info, int axis, Action<Ctx> apply, string up, string down) =>
            Float(name, scope, c => { var l = info(c).layout; return axis == 0 ? l.x : axis == 1 ? l.y : axis == 2 ? l.z : l.w; },
                (c, v) => { var l = info(c).layout; if (axis == 0) l.x = v; else if (axis == 1) l.y = v; else if (axis == 2) l.z = v; else l.w = v; info(c).layout = l; apply(c); }, up, down);
        list.Add(Int("moleId", "face", c => c.F.moleInfo.ID, (c, v) => { c.F.moleInfo.ID = v; FaceTex(c); }, "mole type ID (0 = none)"));
        list.Add(Colr("moleColor", "face", c => c.F.moleInfo.color, (c, v) => { c.F.moleInfo.color = v; FaceTex(c); }, "mole colour"));
        list.Add(Layout("moleX", "face", c => c.F.moleInfo, 0, FaceTex, "", ""));
        list.Add(Layout("moleY", "face", c => c.F.moleInfo, 1, FaceTex, "", ""));
        list.Add(Layout("moleSize", "face", c => c.F.moleInfo, 3, FaceTex, "", ""));
        for (var i = 0; i < 3; i++)
        {
            var index = i;
            HumanDataPaintInfo FacePaint(Ctx c) => PaintAt(c.Co.FaceMakeup.paintInfos, index);
            HumanDataPaintInfo BodyPaint(Ctx c) => PaintAt(c.Co.BodyMakeup.paintInfos, index);
            list.Add(Layout($"facePaint{i + 1}X", "makeup", FacePaint, 0, FaceTex, "", ""));
            list.Add(Layout($"facePaint{i + 1}Y", "makeup", FacePaint, 1, FaceTex, "", ""));
            list.Add(Layout($"facePaint{i + 1}Rotation", "makeup", FacePaint, 2, FaceTex, "", ""));
            list.Add(Layout($"facePaint{i + 1}Size", "makeup", FacePaint, 3, FaceTex, "", ""));
            list.Add(Layout($"bodyPaint{i + 1}X", "makeup", BodyPaint, 0, BodyTex, "", ""));
            list.Add(Layout($"bodyPaint{i + 1}Y", "makeup", BodyPaint, 1, BodyTex, "", ""));
            list.Add(Layout($"bodyPaint{i + 1}Rotation", "makeup", BodyPaint, 2, BodyTex, "", ""));
            list.Add(Layout($"bodyPaint{i + 1}Size", "makeup", BodyPaint, 3, BodyTex, "", ""));
        }
        list.Add(Float("skinShinePower", "body", c => c.B.skinShinePower, (c, v) => c.H.Body.ChangeSettingBodyShinePower(NF(v)), "", ""));
        // ---- profile and rendering
        list.Add(Bool("isFutanari", "profile", c => c.H.FileParam.isFutanari, (c, v) => { c.H.FileParam.isFutanari = v; c.ReloadAll = true; }, "the character is futanari", "not futanari"));
        list.Add(Int("rampId", "graphic", c => c.H.Data.Graphic.RampID, (c, v) => { c.H.Data.Graphic.RampID = v; c.H.Graphic.ChangeRampTexture(NI(v)); }, "toon shading ramp (shadow gradient) ID"));
        list.Add(Float("shadowDepth", "graphic", c => c.H.Data.Graphic.ShadowDepth, (c, v) => { c.H.Data.Graphic.ShadowDepth = v; c.H.Graphic.ChangeShadowDepth(NF(v)); }, "shadows on the character become lighter", "shadows become darker"));
        list.Add(Float("lineWidth", "graphic", c => c.H.Data.Graphic.LineWidth, (c, v) => { c.H.Data.Graphic.LineWidth = v; c.H.Graphic.ChangeLineWidth(NF(v)); }, "outlines become thicker", "outlines become thinner"));
        return list.ToArray();
    }


    // Export support: operations that restore every value here. Coordinate-scoped values come from the worn
    // coordinate (call once per coordinate); the rest once per character.
    static readonly HashSet<string> FaceBodyScopes = new() { "face", "eye", "highlight", "makeup", "body", "nail" };
    static readonly string[] CoordinateScopes = { "makeup", "nail", "hair", "hairAll", "clothes", "clothesAll", "accessory" };

    internal static List<object> Snapshot(Human h, HumanDataCoordinate coordinate)
    {
        var ops = new List<object>();
        void Add(string scope, Dictionary<string, object> selector, Ctx c)
        {
            var values = new Dictionary<string, object>();
            foreach (var p in All.Where(p => p.Scope == scope))
            {
                try { values[p.Name] = p.Get(c); } catch { }
            }
            if (values.Count == 0) return;
            selector["values"] = values;
            ops.Add(new { path = "creator/params", body = selector });
        }
        if (coordinate != null)
        {
            var co = coordinate;
            Add("makeup", new(), new Ctx { H = h, Data = co });
            Add("nail", new() { ["foot"] = false }, new Ctx { H = h, Data = co });
            Add("nail", new() { ["foot"] = true }, new Ctx { H = h, Data = co, Foot = true });
            Add("hairAll", new(), new Ctx { H = h, Data = co });
            for (var part = 0; part < co.Hair.parts.Length; part++) Add("hair", new() { ["part"] = part }, new Ctx { H = h, Data = co, Part = part });
            Add("clothesAll", new(), new Ctx { H = h, Data = co });
            for (var slot = 0; slot < co.Clothes.parts.Length; slot++)
                for (var ch = 0; ch < co.Clothes.parts[slot].colorInfo.Length; ch++)
                    Add("clothes", new() { ["slot"] = slot, ["channel"] = ch }, new Ctx { H = h, Data = co, Slot = slot, Channel = ch });
            for (var slot = 0; slot < co.Accessory.parts.Length; slot++)
                if (co.Accessory.parts[slot].type != (int)Character.List.Define.CategoryNo.ao_none)
                    Add("accessory", new() { ["slot"] = slot }, new Ctx { H = h, Data = co, Slot = slot });
            return ops;
        }
        Add("face", new(), new Ctx { H = h });
        Add("body", new(), new Ctx { H = h });
        Add("profile", new(), new Ctx { H = h });
        Add("graphic", new(), new Ctx { H = h });
        for (byte lr = 0; lr < 2; lr++)
        {
            Add("eye", new() { ["eye"] = (int)lr }, new Ctx { H = h, Eyes = new[] { lr } });
            var count = h.FileFace.pupil[lr].highlightInfos.Length;
            for (var i = 0; i < count; i++) Add("highlight", new() { ["eye"] = (int)lr, ["highlight"] = i }, new Ctx { H = h, Eyes = new[] { lr }, Highlight = i });
        }
        return ops;
    }

    static void Validate(P p, JsonElement value, object current)
    {
        string Bad(string what) => $"{p.Name}: {what}";
        bool Number(JsonElement e) => e.ValueKind == JsonValueKind.Number && float.IsFinite(e.GetSingle());
        switch (p.Type)
        {
            case "float":
                if (!Number(value)) throw new ArgumentException(Bad("must be a number"));
                break;
            case "id":
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out _)) throw new ArgumentException(Bad("must be an integer"));
                break;
            case "bool":
                if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new ArgumentException(Bad("must be true or false"));
                break;
            case "color":
                if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() is not (3 or 4) || value.EnumerateArray().Any(x => !Number(x) || x.GetSingle() < 0 || x.GetSingle() > 1))
                    throw new ArgumentException(Bad("color must be 3 or 4 channels in 0..1"));
                break;
            case "vector4":
                if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != 4 || value.EnumerateArray().Any(x => !Number(x))) throw new ArgumentException(Bad("needs 4 numbers"));
                break;
            case "bool[]":
                var count = ((System.Collections.ICollection)current).Count;
                if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != count || value.EnumerateArray().Any(x => x.ValueKind is not (JsonValueKind.True or JsonValueKind.False)))
                    throw new ArgumentException(Bad($"needs {count} true/false values"));
                break;
            case "vector3[]":
                var bones = ((System.Collections.ICollection)current).Count;
                if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != bones || value.EnumerateArray().Any(x => x.ValueKind != JsonValueKind.Array || x.GetArrayLength() != 3 || x.EnumerateArray().Any(y => !Number(y))))
                    throw new ArgumentException(Bad($"needs {bones} [x,y,z] values"));
                break;
            case "map":
                if (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().Any(x => !int.TryParse(x.Name, out _) || x.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)))
                    throw new ArgumentException(Bad("needs an object of piece ID: true/false"));
                break;
        }
    }

    static HumanDataPresetPaintInfo PaintAt(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<HumanDataPresetPaintInfo> arr, int index)
    {
        if (arr == null || index >= arr.Length) throw new ArgumentException("this paint slot does not exist");
        return arr[index];
    }

    static Ctx Context(JsonElement j, Human h)
    {
        var c = new Ctx { H = h };
        if (j.ValueKind == JsonValueKind.Object)
        {
            if (j.TryGetProperty("eye", out var e) && e.ValueKind == JsonValueKind.Number)
            {
                var lr = e.GetInt32();
                if (lr != 0 && lr != 1) throw new ArgumentException("eye must be 0 or 1 (omit for both)");
                c.Eyes = new[] { (byte)lr };
            }
            if (j.TryGetProperty("highlight", out var hl)) c.Highlight = hl.GetInt32();
            if (j.TryGetProperty("part", out var p)) c.Part = p.GetInt32();
            if (j.TryGetProperty("slot", out var s)) c.Slot = s.GetInt32();
            if (j.TryGetProperty("channel", out var ch)) c.Channel = ch.GetInt32();
            if (j.TryGetProperty("foot", out var ft)) c.Foot = ft.GetBoolean();
        }
        return c;
    }

    static object Describe(P p, Ctx c)
    {
        object value;
        try { value = p.Get(c); } catch (Exception ex) { value = new { error = ex.Message }; }
        // Raise/lower notes are given for face and body values only.
        if (!FaceBodyScopes.Contains(p.Scope))
            return p.Type is "float" ? new { name = p.Name, scope = p.Scope, type = p.Type, value }
                : (object)new { name = p.Name, scope = p.Scope, type = p.Type, value, meaning = p.Up };
        // Face and body sliders use the maker-ordered Japanese notes of creator/shape-guide.
        var (up, down) = ShapeGuide.ParamNotes.TryGetValue(p.Name, out var note) ? note : (p.Up, p.Down);
        return p.Type is "float" or "bool"
            ? new { name = p.Name, scope = p.Scope, type = p.Type, value, increase = up, decrease = down }
            : (object)new { name = p.Name, scope = p.Scope, type = p.Type, value, meaning = p.Up };
    }

    internal static ApiResult? Execute(string method, string action, JsonElement j, Human h)
    {
        if (action == "freeze") return Freeze(method, j, h);
        if (action != "params") return null;
        if (method == "GET")
        {
            var c = Context(j, h);
            return new(200, new { note = Note, @params = All.Select(p => Describe(p, c)).ToArray() });
        }
        if (method != "POST") return null;
        var ctx = Context(j, h);
        // A POST without "values" reads with the given scope selectors.
        if (!j.TryGetProperty("values", out var values))
            return new(200, new { note = Note, @params = All.Select(p => Describe(p, ctx)).ToArray() });
        if (values.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("values must be an object of name: value (names from GET creator/params)");
        var byName = All.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
        var unknown = values.EnumerateObject().Select(x => x.Name).Where(n => !byName.ContainsKey(n)).ToArray();
        if (unknown.Length > 0) throw new ArgumentException("unknown parameter(s): " + string.Join(", ", unknown));
        // Validate every value (type, range, target slot) before anything changes.
        var entries = values.EnumerateObject().Select(prop => (p: byName[prop.Name], value: prop.Value.Clone())).ToList();
        var previous = new List<(P p, JsonElement value)>();
        foreach (var (p, value) in entries)
        {
            var before = p.Get(ctx);
            Validate(p, value, before);
            previous.Add((p, JsonSerializer.SerializeToElement(before)));
        }
        // Apply; if the game rejects one, put back every value touched so far,
        // including the one whose setter failed partway.
        var applied = new List<object>();
        var done = 0;
        try
        {
            foreach (var (p, value) in entries)
            {
                done++;
                p.Set(ctx, value);
                applied.Add(Describe(p, ctx));
            }
        }
        catch
        {
            for (var i = done - 1; i >= 0; i--)
            {
                try { previous[i].p.Set(ctx, previous[i].value); } catch { }
            }
            FlushReloads(ctx);
            CreatorApi.SyncCoordinate(h);
            throw;
        }
        FlushReloads(ctx);
        CreatorApi.SyncCoordinate(h);
        return new(200, new { applied });
    }

    // State from before a stop, kept per character so resuming one does not restore another's.
    sealed class Frozen { public bool? Blink; public (bool, bool)? EyeMovement; public float? Speed; }
    static readonly Dictionary<IntPtr, Frozen> _frozen = new();

    // creator/freeze: stops blinking, small eye movements and the body animation (for comparing shots).
    static ApiResult Freeze(string method, JsonElement j, Human h)
    {
        var animator = h.Body.animBody;
        if (method == "POST")
        {
            if (!_frozen.TryGetValue(h.Pointer, out var f)) _frozen[h.Pointer] = f = new Frozen();
            // Stopping remembers the current state; resuming puts that state back.
            if (j.TryGetProperty("blink", out var b))
            {
                if (!b.GetBoolean()) { f.Blink ??= h.Face.GetEyesBlinkFlag(); h.Face.ChangeEyesBlinkFlag(false); }
                else { h.Face.ChangeEyesBlinkFlag(f.Blink ?? true); f.Blink = null; }
            }
            if (j.TryGetProperty("eyeMovement", out var e))
            {
                if (!e.GetBoolean())
                {
                    f.EyeMovement ??= (h.Face.GetEyesMicroSaccadeFlag(), h.Face.GetEyesShaking());
                    h.Face.ChangeEyesMicroSaccadeFlag(false); h.Face.ChangeEyesShaking(false);
                }
                else
                {
                    var (saccade, shaking) = f.EyeMovement ?? (true, true);
                    h.Face.ChangeEyesMicroSaccadeFlag(saccade); h.Face.ChangeEyesShaking(shaking);
                    f.EyeMovement = null;
                }
            }
            if (j.TryGetProperty("motion", out var m) && animator != null)
            {
                if (!m.GetBoolean()) { if (animator.speed > 0f) f.Speed = animator.speed; animator.speed = 0f; }
                else { animator.speed = f.Speed ?? 1f; f.Speed = null; }
            }
            if (f.Blink == null && f.EyeMovement == null && f.Speed == null) _frozen.Remove(h.Pointer);
        }
        else if (method != "GET") return new(405, new { error = "use GET or POST" });
        return new(200, new
        {
            blink = h.Face.GetEyesBlinkFlag(),
            eyeMovement = h.Face.GetEyesMicroSaccadeFlag(),
            motion = animator == null || animator.speed > 0f
        });
    }

    const string Note = "Values without a stated range are the maker's own 0..1 slider values unless the maker allows more. " +
        "\"eye\" (0 or 1, default both), \"highlight\" (index), \"part\" (hair 0 back, 1 front, 2 side, 3 option), " +
        "\"slot\" (clothes or accessory), \"channel\" (clothes colour) and \"foot\" (toenails) select what a scoped value applies to.";
}
