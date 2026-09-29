#nullable disable
using System;
using System.IO;
using System.Linq;
using Character;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using CategoryNo = Character.List.Define.CategoryNo;

namespace Amanatsu.AiExtension;

// Reads character cards into a detached HumanData; nothing is loaded into a scene.
internal static class CardInspect
{
    static float[] C(Color c) => new[] { c.r, c.g, c.b, c.a };
    static float[] V(Vector3 v) => new[] { v.x, v.y, v.z };
    static readonly string[] Roots = { Path.GetFullPath("UserData/chara"), Path.GetFullPath("DefaultData") };

    internal static ApiResult List()
    {
        var cards = Roots.Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, "*.png", SearchOption.AllDirectories)
                .Where(p => root.EndsWith("chara", StringComparison.OrdinalIgnoreCase) || p.Replace('\\', '/').Contains("/chara/"))
                .Select(p => new { file = Path.GetRelativePath(Directory.GetCurrentDirectory(), p).Replace('\\', '/'), modified = File.GetLastWriteTimeUtc(p) }))
            .OrderBy(x => x.file, StringComparer.OrdinalIgnoreCase).ToArray();
        return new(200, new { cards });
    }

    internal static string ResolvePath(string file) => Resolve(file);

    static string Resolve(string file)
    {
        const string example = "pass a path from GET /api/v1/cards (e.g. UserData/chara/female/AL_F_xxx.png) or just the file name";
        if (string.IsNullOrWhiteSpace(file)) throw new ArgumentException("file is required: " + example);
        // A bare file name or "female/x.png" is looked up under UserData/chara as well.
        var candidates = new[] { file, Path.Combine("UserData", "chara", file), Path.Combine("UserData", "chara", "female", file), Path.Combine("UserData", "chara", "male", file) };
        foreach (var candidate in candidates)
        {
            var full = Path.GetFullPath(candidate);
            if (!Roots.Any(r => full.StartsWith(r + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))) continue;
            if (full.EndsWith(".png", StringComparison.OrdinalIgnoreCase) && File.Exists(full)) return full;
        }
        throw new ArgumentException("card not found under UserData/chara or DefaultData; " + example);
    }

    internal static ApiResult Read(string query)
    {
        var file = Uri.UnescapeDataString((query ?? "").TrimStart('?').Split('&').Select(x => x.Split('=', 2))
            .FirstOrDefault(x => x[0] == "file")?.ElementAtOrDefault(1) ?? "");
        var path = Resolve(file);
        var d = new HumanData();
        if (!d.LoadCharaFile(path)) return new(422, new { error = "not a readable character card", file });
        return new(200, Describe(d, file));
    }

    // Card contents as plain values; also used to compare a saved card with the open character.
    static object Describe(HumanData d, string file)
    {
        var p = d.Parameter; var f = d.Custom.Face; var b = d.Custom.Body;
        return new
        {
            file,
            profile = new { p.sex, p.lastname, p.firstname, p.nickname, p.personality, p.birthMonth, p.birthDay, p.voiceRate, p.bloodType },
            faceShapes = f.shapeValueFace.ToArray(), bodyShapes = b.shapeValueBody.ToArray(),
            face = new
            {
                f.headId, f.eyebrowId, f.eyelineUpId, f.eyelineDownId, f.eyelidId, f.whiteId, f.noseId, f.lipLineId, f.detailId,
                eyebrowColor = C(f.eyebrowColor), eyelineColor = C(f.eyelineColor), eyelidColor = C(f.eyelidColor), f.eyelineUpWeight, moleId = f.moleInfo.ID, foregroundEyes = (int)f.foregroundEyes, foregroundEyebrow = (int)f.foregroundEyebrow,
                pupils = f.pupil.Select(x => new { eye = x.id, pupil = x.overId, eye01Color = C(x.eye01Color), eye02Color = C(x.eye02Color), eye03Color = C(x.eye03Color), eyeGradColor = C(x.eyeGradColor) }).ToArray()
            },
            skin = new { mainColor = C(b.skinMainColor), b.skinShineId, b.skinShinePower, b.sunburnUpId, b.sunburnDownId, b.detailId },
            coordinates = d.Coordinates.Select((co, index) => new
            {
                index,
                hair = co.Hair.parts.Select(hp => new
                {
                    hp.id, baseColor = C(hp.baseColor), startColor = C(hp.startColor), endColor = C(hp.endColor),
                    hp.useMesh, hp.useInner,
                    bundles = BundleList(hp)
                }).ToArray(),
                clothes = co.Clothes.parts.Select(cp => new { cp.id, colors = cp.colorInfo.Select(ci => C(ci.baseColor)).ToArray(), patterns = CreatorApiEx.ClothesPatterns(cp) }).ToArray(),
                accessories = co.Accessory.parts.Select((ap, slot) => new { slot, category = ((CategoryNo)ap.type).ToString(), ap.id, parent = ap.parentKeyType, move = CreatorApiEx.AccessoryMoveInfo(ap), colors = ap.color.Select(C).ToArray(), gloss = ap.gloss?.ToArray(), metallic = ap.metallic?.ToArray() })
                    .Where(a => a.category != nameof(CategoryNo.ao_none)).ToArray(),
                makeup = new { co.FaceMakeup.eyeshadowId, co.FaceMakeup.cheekId, co.FaceMakeup.lipId, lipColor = C(co.FaceMakeup.lipColor), cheekColor = C(co.FaceMakeup.cheekColor) }
            }).ToArray()
        };
    }

    // creator/verify-card (0.7.0): reads a saved card back from disk and compares every value above
    // with the open character; mismatches list the differing fields.
    internal static ApiResult Verify(System.Text.Json.JsonElement j, Human h)
    {
        var file = j.TryGetProperty("file", out var fe) ? fe.GetString() : null;
        var path = Resolve(file);
        var d = new HumanData();
        if (!d.LoadCharaFile(path)) return new(422, new { error = "not a readable character card", file });
        CreatorApi.SyncCoordinate(h);
        var saved = System.Text.Json.JsonSerializer.SerializeToElement(Describe(d, file));
        var open = System.Text.Json.JsonSerializer.SerializeToElement(Describe(h.Data, file));
        var mismatches = new System.Collections.Generic.List<object>();
        Compare("", saved, open, mismatches);
        return new(200, new { file = Path.GetRelativePath(Directory.GetCurrentDirectory(), path).Replace('\\', '/'), matches = mismatches.Count == 0, mismatches });
    }

    static void Compare(string at, System.Text.Json.JsonElement card, System.Text.Json.JsonElement open, System.Collections.Generic.List<object> mismatches)
    {
        if (mismatches.Count >= 100) return;
        if (card.ValueKind != open.ValueKind) { mismatches.Add(new { field = at, card, open }); return; }
        switch (card.ValueKind)
        {
            case System.Text.Json.JsonValueKind.Object:
                foreach (var prop in card.EnumerateObject())
                    // Cards never store the nickname (no saved card has one), so it is not compared.
                    if (at + "." + prop.Name != "profile.nickname" && open.TryGetProperty(prop.Name, out var other)) Compare(at.Length == 0 ? prop.Name : at + "." + prop.Name, prop.Value, other, mismatches);
                break;
            case System.Text.Json.JsonValueKind.Array:
                var a = card.EnumerateArray().ToArray(); var b = open.EnumerateArray().ToArray();
                if (a.Length != b.Length) { mismatches.Add(new { field = at + ".length", card = a.Length, open = b.Length }); return; }
                for (var i = 0; i < a.Length; i++) Compare($"{at}[{i}]", a[i], b[i], mismatches);
                break;
            case System.Text.Json.JsonValueKind.Number:
                if (Math.Abs(card.GetDouble() - open.GetDouble()) > 1e-4) mismatches.Add(new { field = at, card, open });
                break;
            default:
                if (card.GetRawText() != open.GetRawText()) mismatches.Add(new { field = at, card, open });
                break;
        }
    }

    static object BundleList(HumanDataHair.PartsInfo hp)
    {
        var list = new System.Collections.Generic.List<object>();
        foreach (var kv in hp.dictBundle) list.Add(new { index = kv.Key, moveRate = V(kv.Value.moveRate), rotRate = V(kv.Value.rotRate), kv.Value.noShake });
        return list;
    }
}
