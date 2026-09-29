#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Character;
using UnityEngine;

namespace Amanatsu.AiExtension;

// Batch endpoints (0.5.0): a whole look used to take dozens of single-value requests.
internal static class CreatorApiBatch
{
    static ApiResult Ok(object x) => new(200, x);

    static readonly string[] HairFields = { "base", "start", "end", "outline", "gloss", "shadow", "mesh", "inner" };

    internal static ApiResult? Execute(string method, string action, JsonElement j, Human h)
    {
        if (method == "POST" && action == "shapes") return Shapes(j, h);
        if (method == "POST" && action == "hair-colors") return HairColors(j, h);
        if (action == "eye-lines") return EyeLines(method, j, h);
        return null;
    }

    // {"face":{"EyeW":0.6,"35":0.6},"body":{"Height":0.5}} — names from creator/details labels or indices.
    static ApiResult Shapes(JsonElement j, Human h)
    {
        var plan = new List<(string Region, int Index, string Label, float Value)>();
        foreach (var (region, labels, length) in new[]
        {
            ("face", Enum.GetNames(typeof(HumanFace.Define.FaceShapeIdx)), h.FileFace.shapeValueFace.Length),
            ("body", Enum.GetNames(typeof(HumanBody.Define.BodyShapeIdx)), h.FileBody.shapeValueBody.Length)
        })
        {
            if (!j.TryGetProperty(region, out var values)) continue;
            if (values.ValueKind != JsonValueKind.Object) throw new ArgumentException($"{region} must be an object of name: value");
            foreach (var entry in values.EnumerateObject())
            {
                var index = int.TryParse(entry.Name, out var number) ? number : Array.IndexOf(labels, entry.Name);
                if (index < 0 || index >= length) throw new ArgumentException($"unknown {region} shape '{entry.Name}'");
                var value = entry.Value.GetSingle();
                if (!float.IsFinite(value) || value < -5 || value > 5) throw new ArgumentException($"{region}.{entry.Name} must be finite and between -5 and 5");
                plan.Add((region, index, index < labels.Length ? labels[index] : index.ToString(), value));
            }
        }
        if (plan.Count == 0) throw new ArgumentException("supply face and/or body values");

        // Everything was validated above; now apply and refresh the model once.
        h.Data.SkipRangeCheck = true;
        var results = new List<object>();
        foreach (var (region, index, label, value) in plan)
        {
            var values = region == "face" ? h.FileFace.shapeValueFace : h.FileBody.shapeValueBody;
            var before = values[index];
            if (region == "face") h.Face.SetShapeFaceValue(index, value);
            else h.Body.SetShapeBodyValue(index, value, true);
            results.Add(new { region, index, label, before, after = values[index] });
        }
        if (plan.Any(p => p.Region == "face")) h.Face.SetUpdateShapeFaceValue();
        if (plan.Any(p => p.Region == "body")) h.Body.SetUpdateShapeBodyValue();
        h.SetUpdateShape();
        return Ok(new { applied = results.Count, results });
    }

    // {"slots":[0,1,2,3],"base":[..],"start":[..],...,"useMesh":true,"useInner":true}; slots default to all four.
    static ApiResult HairColors(JsonElement j, Human h)
    {
        var parts = h.Coorde.Now.Hair.parts;
        var slots = j.TryGetProperty("slots", out var s)
            ? s.EnumerateArray().Select(x => x.GetInt32()).Distinct().ToArray()
            : Enumerable.Range(0, parts.Length).ToArray();
        if (slots.Length == 0 || slots.Any(x => x < 0 || x >= parts.Length)) throw new ArgumentException("slots must be hair slots 0..3");
        var colors = new Dictionary<string, Color>();
        foreach (var field in HairFields)
            if (j.TryGetProperty(field, out var c)) colors[field] = ParseColor(c, field);
        bool? useMesh = j.TryGetProperty("useMesh", out var m) ? m.GetBoolean() : null;
        bool? useInner = j.TryGetProperty("useInner", out var i) ? i.GetBoolean() : null;
        if (colors.Count == 0 && useMesh == null && useInner == null) throw new ArgumentException("supply at least one color field or flag");

        foreach (var slot in slots)
        {
            var p = parts[slot];
            foreach (var (field, color) in colors)
                switch (field)
                {
                    case "base": p.baseColor = color; break;
                    case "start": p.startColor = color; break;
                    case "end": p.endColor = color; break;
                    case "outline": p.outlineColor = color; break;
                    case "gloss": p.glossColor = color; break;
                    case "shadow": p.shadowColor = color; break;
                    case "mesh": p.meshColor = color; break;
                    case "inner": p.innerColor = color; break;
                }
            if (useMesh.HasValue) p.useMesh = useMesh.Value;
            if (useInner.HasValue) p.useInner = useInner.Value;
            h.Hair.ChangeSettingHairColor(slot, true, true, true);
            h.Hair.ChangeSettingHairOutlineColor(slot);
            h.Hair.ChangeSettingHairGlossColor(slot, 0);
            h.Hair.ChangeSettingHairShadowColor(slot);
            h.Hair.ChangeSettingHairMeshColor(slot);
            h.Hair.ChangeSettingHairInnerColor(slot);
        }
        CreatorApi.SyncCoordinate(h);
        return Ok(new { slots, fields = colors.Keys.ToArray(), useMesh, useInner });
    }

    // Lash line and eyelid colors and eyelid placement: the maker has them, the API did not.
    static ApiResult EyeLines(string method, JsonElement j, Human h)
    {
        var f = h.FileFace;
        if (method == "POST")
        {
            if (j.TryGetProperty("eyelineColor", out var ec)) { f.eyelineColor = ParseColor(ec, "eyelineColor"); for (var i = 0; i < 2; i++) h.Face.ChangeSettingEyelineColor(i, new Il2CppSystem.Nullable<Color>(f.eyelineColor)); }
            if (j.TryGetProperty("eyelidColor", out var lc)) { f.eyelidColor = ParseColor(lc, "eyelidColor"); h.Face.ChangeSettingEyelid(new Il2CppSystem.Nullable<int>(f.eyelidId)); }
            if (j.TryGetProperty("eyelineUpWeight", out var w)) { f.eyelineUpWeight = w.GetSingle(); h.Face.ChangeSettingEyelineUp(new Il2CppSystem.Nullable<int>(f.eyelineUpId)); }
        }
        else if (method != "GET") return new(405, new { error = "method not allowed" });
        return Ok(new { eyelineColor = C(f.eyelineColor), eyelidColor = C(f.eyelidColor), f.eyelineUpWeight });
    }

    static float[] C(Color c) => new[] { c.r, c.g, c.b, c.a };

    static Color ParseColor(JsonElement j, string field)
    {
        var v = j.EnumerateArray().Select(x => x.GetSingle()).ToArray();
        if ((v.Length != 3 && v.Length != 4) || v.Any(x => !float.IsFinite(x) || x < 0 || x > 1))
            throw new ArgumentException($"{field} must be 3 or 4 channels in 0..1");
        return new Color(v[0], v[1], v[2], v.Length == 4 ? v[3] : 1);
    }
}
