#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Character;
using UnityEngine;

namespace Amanatsu.AiExtension;

// Part kits (0.6.0): one region of a face (or the hair) saved as replayable operations, so a
// face can be assembled from kits and then refined with individual parameters.
internal static class Kits
{
    static ApiResult Ok(object x) => new(200, x);
    static float[] C(Color c) => new[] { c.r, c.g, c.b, c.a };
    static float[] V(Vector3 v) => new[] { v.x, v.y, v.z };
    static object Op(string path, object body) => new { path, body };

    internal static string Root => Path.GetFullPath("BepInEx/config/amanatsu.ai-extension/kits");

    static readonly Dictionary<string, string[]> Shapes = new()
    {
        ["outline"] = new[] { "FaceBaseW", "FaceUpZ", "FaceUpY", "FaceUpSize", "FaceLowZ", "FaceLowW", "ChinLowY", "ChinLowZ", "ChinY", "ChinW", "ChinZ",
            "ChinTipY", "ChinTipZ", "ChinTipW", "CheekBoneW", "CheekBoneZ", "CheekW", "CheekZ", "CheekY", "EarSize", "EarRotY", "EarRotZ",
            "EarUpForm", "EarLowForm", "FaceSize", "FaceY", "HeadSize" },
        ["eyes"] = new[] { "EyelidsUpForm1", "EyelidsUpForm2", "EyelidsUpForm3", "EyelidsLowForm1", "EyelidsLowForm2", "EyelidsLowForm3",
            "EyeY", "EyeX", "EyeZ", "EyeTilt", "EyeH", "EyeW" },
        ["brows"] = new[] { "EyebrowY", "EyebrowX", "EyebrowRotZ", "EyebrowInForm", "EyebrowOutForm" },
        ["nose"] = new[] { "NoseTipH", "NoseY", "NoseBridgeH", "NoseBase" },
        ["mouth"] = new[] { "MouthY", "MouthW", "MouthZ", "MouthUpForm", "MouthLowForm", "MouthCornerFormRot", "MouthCornerFormY" },
        ["hair"] = Array.Empty<string>(),
    };

    internal static ApiResult? Execute(string method, string action, JsonElement j, Human h)
    {
        switch ((method, action))
        {
            case ("GET", "kits"): return List();
            case ("POST", "kit-save"):
                CreatorApi.SyncCoordinate(h);
                return Save(j, h.FileFace, h.Data.Coordinates[(int)h.FileStatus.coordinateType], "creator");
            case ("POST", "kit-from-card"): return FromCard(j);
            case ("POST", "kit-apply"):
                CreatorApi.SyncCoordinate(h);
                return Apply(j, h);
            case ("POST", "kit-delete"): return Delete(j);
        }
        return null;
    }

    static string Region(JsonElement j)
    {
        var region = j.TryGetProperty("region", out var r) ? r.GetString() : null;
        if (region == null || !Shapes.ContainsKey(region))
            throw new ArgumentException("region must be one of: " + string.Join(", ", Shapes.Keys));
        return region;
    }

    static string KitPath(string region, JsonElement j)
    {
        var name = j.TryGetProperty("name", out var n) ? n.GetString()?.Trim() : null;
        if (string.IsNullOrEmpty(name) || name.Length > 40 || name.StartsWith(".") || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("name must be 1-40 characters usable in a file name");
        return Path.Combine(Root, region, name + ".json");
    }

    static ApiResult List()
    {
        var kits = Directory.Exists(Root)
            ? Directory.EnumerateFiles(Root, "*.json", SearchOption.AllDirectories).Select(path =>
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                var root = doc.RootElement;
                var png = Path.ChangeExtension(path, ".png");
                return (object)new
                {
                    region = root.GetProperty("region").GetString(),
                    name = root.GetProperty("name").GetString(),
                    description = root.TryGetProperty("description", out var d) ? d.GetString() : "",
                    source = root.TryGetProperty("source", out var s) ? s.GetString() : "",
                    operations = root.GetProperty("operations").GetArrayLength(),
                    thumbnail = File.Exists(png) ? png : null
                };
            }).ToArray()
            : Array.Empty<object>();
        return Ok(new { root = Root, kits });
    }

    static ApiResult Save(JsonElement j, HumanDataFace f, HumanDataCoordinate co, string source)
    {
        var region = Region(j);
        var path = KitPath(region, j);
        var overwrite = j.TryGetProperty("overwrite", out var o) && o.GetBoolean();
        if (File.Exists(path) && !overwrite) throw new ArgumentException("a kit with this name exists; pass overwrite:true to replace it");
        var description = j.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";
        var operations = Build(region, f, co);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var kit = new { version = 1, region, name = Path.GetFileNameWithoutExtension(path), description, source, created = DateTime.UtcNow, operations };
        File.WriteAllText(path, JsonSerializer.Serialize(kit, new JsonSerializerOptions { WriteIndented = true }));
        return Ok(new { saved = path, thumbnail = Path.ChangeExtension(path, ".png"), region, operations = operations.Count });
    }

    static ApiResult FromCard(JsonElement j)
    {
        var file = j.TryGetProperty("file", out var fe) ? fe.GetString() : null;
        var path = CardInspect.ResolvePath(file);
        var data = new HumanData();
        if (!data.LoadCharaFile(path)) return new(422, new { error = "not a readable character card", file });
        var index = j.TryGetProperty("coordinate", out var c) ? c.GetInt32() : 0;
        if (index < 0 || index >= data.Coordinates.Length) throw new ArgumentException("coordinate out of range");
        return Save(j, data.Custom.Face, data.Coordinates[index], "card:" + Path.GetFileName(path));
    }

    static List<object> Build(string region, HumanDataFace f, HumanDataCoordinate co)
    {
        var ops = new List<object>();
        var labels = Enum.GetNames(typeof(HumanFace.Define.FaceShapeIdx));
        if (Shapes[region].Length > 0)
            ops.Add(Op("creator/shapes", new { face = Shapes[region].ToDictionary(label => label, label => f.shapeValueFace[Array.IndexOf(labels, label)]) }));
        switch (region)
        {
            case "eyes":
                ops.Add(Op("creator/face-parts", new { eyelineUp = f.eyelineUpId, eyelineDown = f.eyelineDownId, eyelid = f.eyelidId, white = f.whiteId, eye = f.pupil[0].id, pupil = f.pupil[0].overId }));
                ops.Add(Op("creator/eye-lines", new { eyelineColor = C(f.eyelineColor), eyelidColor = C(f.eyelidColor), eyelineUpWeight = f.eyelineUpWeight }));
                for (var lr = 0; lr < f.pupil.Length; lr++)
                {
                    ops.Add(Op("creator/color", new { target = "eye", slot = lr, channel = 0, color = C(f.pupil[lr].eye01Color) }));
                    ops.Add(Op("creator/color", new { target = "eye", slot = lr, channel = 1, color = C(f.pupil[lr].eye02Color) }));
                    ops.Add(Op("creator/color", new { target = "eye", slot = lr, channel = 2, color = C(f.pupil[lr].eye03Color) }));
                }
                var highlight = f.pupil[0].highlightInfos.Length > 0 ? C(f.pupil[0].highlightInfos[0].color) : null;
                ops.Add(Op("creator/makeup", highlight == null
                    ? new { eyeGradColor = C(f.pupil[0].eyeGradColor) }
                    : (object)new { eyeGradColor = C(f.pupil[0].eyeGradColor), eyeHighlightColor = highlight }));
                break;
            case "brows":
                ops.Add(Op("creator/face-parts", new { eyebrow = f.eyebrowId }));
                ops.Add(Op("creator/color", new { target = "eyebrow", color = C(f.eyebrowColor) }));
                break;
            case "nose":
                ops.Add(Op("creator/face-parts", new { nose = f.noseId }));
                break;
            case "mouth":
                ops.Add(Op("creator/face-parts", new { lipLine = f.lipLineId }));
                var m = co.FaceMakeup;
                ops.Add(Op("creator/makeup", new { lipId = m.lipId, lipColor = C(m.lipColor), lipHighlightColor = C(m.lipHighlightColor) }));
                break;
            case "hair":
                string[] kinds = { "hair_back", "hair_front", "hair_side", "hair_option" };
                for (var s = 0; s < co.Hair.parts.Length && s < kinds.Length; s++)
                {
                    var hp = co.Hair.parts[s];
                    // Hair kits carry the style only; colors stay with the character they are applied to.
                    ops.Add(Op("character/choice", new { kind = kinds[s], id = hp.id }));
                    foreach (var kv in hp.dictBundle)
                        ops.Add(Op("creator/hair-bundle", new { part = s, index = kv.Key, moveRate = V(kv.Value.moveRate), rotRate = V(kv.Value.rotRate) }));
                }
                break;
        }
        return ops;
    }

    static readonly string[] HairKinds = { "hair_back", "hair_front", "hair_side", "hair_option" };
    static readonly HashSet<string> PartPaths = new() { "creator/face-parts", "character/choice", "creator/hair-bundle" };

    // kit-apply options (0.7.0): "exclude" leaves named fields as they are, "dryRun" only reports the changes.
    // Exclude names: the groups "shapes", "parts", "colors"; a shape label (EyeW); a body key (eyelineColor, lipId);
    // "<target>Color" for creator/color (eyeColor, eyebrowColor); a hair kind (hair_front) for its style and bundles.
    static ApiResult Apply(JsonElement j, Human h)
    {
        var region = Region(j);
        var path = KitPath(region, j);
        if (!File.Exists(path)) return new(404, new { error = "kit not found", region, name = Path.GetFileNameWithoutExtension(path) });
        var exclude = new HashSet<string>(j.TryGetProperty("exclude", out var ex) && ex.ValueKind == JsonValueKind.Array
            ? ex.EnumerateArray().Select(x => x.GetString() ?? "") : Array.Empty<string>());
        var dryRun = j.TryGetProperty("dryRun", out var dr) && dr.GetBoolean();
        var kitOps = JsonNode.Parse(File.ReadAllText(path))["operations"].AsArray()
            .Select(op => (path: op["path"].GetValue<string>(), body: op["body"].AsObject())).ToList();
        foreach (var (opPath, _) in kitOps)
            if (opPath.Contains("..") || opPath.StartsWith("/") || opPath.StartsWith("creator/kit")) throw new ArgumentException("kit contains an invalid path");

        var known = new HashSet<string> { "shapes", "parts", "colors" };
        foreach (var (opPath, body) in kitOps) known.UnionWith(Names(opPath, body));
        var unknown = exclude.Where(x => !known.Contains(x)).ToArray();
        if (unknown.Length > 0)
            throw new ArgumentException($"unknown exclude name(s): {string.Join(", ", unknown)}; this kit accepts: {string.Join(", ", known.OrderBy(x => x))}");

        var operations = new List<(string path, JsonObject body)>();
        foreach (var (opPath, body) in kitOps)
        {
            var kept = Filter(opPath, Clone(body).AsObject(), exclude);
            if (kept != null) operations.Add((opPath, kept));
        }
        var current = Fields(JsonNode.Parse(JsonSerializer.Serialize(Build(region, h.FileFace, h.Data.Coordinates[(int)h.FileStatus.coordinateType]))).AsArray()
            .Select(op => (op["path"].GetValue<string>(), op["body"].AsObject())));
        var changes = Fields(operations).Where(kv => !Same(current.TryGetValue(kv.Key, out var before) ? before : null, kv.Value))
            .Select(kv => new { field = kv.Key, before = current.TryGetValue(kv.Key, out var b) ? Clone(b) : null, after = Clone(kv.Value) }).ToArray();
        var name = Path.GetFileNameWithoutExtension(path);
        if (dryRun) return Ok(new { dryRun = true, region, name, exclude = exclude.ToArray(), changes });

        var results = new List<object>();
        var failures = 0;
        foreach (var (opPath, body) in operations)
        {
            var r = GameApi.Execute("POST", "/api/v1/" + opPath, "", body.ToJsonString());
            if (r.Status != 200) { failures++; results.Add(new { path = opPath, status = r.Status, body = r.Body }); }
        }
        return new(failures == 0 ? 200 : 409, new { applied = region, name, exclude = exclude.ToArray(), changes, failures, results });
    }

    // Names an operation answers to in "exclude".
    static IEnumerable<string> Names(string path, JsonObject body)
    {
        switch (path)
        {
            case "creator/shapes": return body["face"].AsObject().Select(kv => kv.Key);
            case "creator/color": return new[] { body["target"].GetValue<string>() + "Color" };
            case "character/choice": return new[] { body["kind"].GetValue<string>() };
            case "creator/hair-bundle": return new[] { HairKinds[body["part"].GetValue<int>()] };
            default: return body.Select(kv => kv.Key);
        }
    }

    static JsonObject Filter(string path, JsonObject body, HashSet<string> exclude)
    {
        if (exclude.Contains("shapes") && path == "creator/shapes") return null;
        if (exclude.Contains("parts") && PartPaths.Contains(path)) return null;
        if (exclude.Contains("colors") && path == "creator/color") return null;
        switch (path)
        {
            case "creator/shapes":
                var face = body["face"].AsObject();
                foreach (var label in face.Select(kv => kv.Key).Where(exclude.Contains).ToArray()) face.Remove(label);
                return face.Count > 0 ? body : null;
            case "creator/color":
            case "character/choice":
            case "creator/hair-bundle":
                return Names(path, body).Any(exclude.Contains) ? null : body;
        }
        foreach (var key in body.Select(kv => kv.Key).Where(k => exclude.Contains(k) || exclude.Contains("colors") && k.EndsWith("Color")).ToArray())
            body.Remove(key);
        return body.Count > 0 ? body : null;
    }

    // Flattens operations to "field -> value" so a kit can be compared with the character.
    static Dictionary<string, JsonNode> Fields(IEnumerable<(string path, JsonObject body)> operations)
    {
        var fields = new Dictionary<string, JsonNode>();
        foreach (var (path, body) in operations)
            switch (path)
            {
                case "creator/shapes":
                    foreach (var kv in body["face"].AsObject()) fields[kv.Key] = kv.Value;
                    break;
                case "creator/color":
                    var key = body["target"].GetValue<string>() + "Color";
                    if (body.ContainsKey("slot")) key += $"[{body["slot"]}].{body["channel"]}";
                    fields[key] = body["color"];
                    break;
                case "character/choice":
                    fields[body["kind"].GetValue<string>()] = body["id"];
                    break;
                case "creator/hair-bundle":
                    var bundle = $"{HairKinds[body["part"].GetValue<int>()]}.bundle[{body["index"]}]";
                    fields[bundle + ".moveRate"] = body["moveRate"];
                    fields[bundle + ".rotRate"] = body["rotRate"];
                    break;
                default:
                    foreach (var kv in body) fields[kv.Key] = kv.Value;
                    break;
            }
        return fields;
    }

    static JsonNode Clone(JsonNode node) => node == null ? null : JsonNode.Parse(node.ToJsonString());

    static bool Same(JsonNode a, JsonNode b)
    {
        if (a == null || b == null) return a == null && b == null;
        if (a is JsonArray x && b is JsonArray y) return x.Count == y.Count && x.Zip(y).All(p => Same(p.First, p.Second));
        if (a is JsonValue && b is JsonValue && a.GetValue<JsonElement>().ValueKind == JsonValueKind.Number && b.GetValue<JsonElement>().ValueKind == JsonValueKind.Number)
            return Math.Abs(a.GetValue<JsonElement>().GetDouble() - b.GetValue<JsonElement>().GetDouble()) < 1e-4;
        return a.ToJsonString() == b.ToJsonString();
    }

    static ApiResult Delete(JsonElement j)
    {
        var region = Region(j);
        var path = KitPath(region, j);
        if (!File.Exists(path)) return new(404, new { error = "kit not found" });
        File.Delete(path);
        var png = Path.ChangeExtension(path, ".png");
        if (File.Exists(png)) File.Delete(png);
        return Ok(new { deleted = path });
    }
}
