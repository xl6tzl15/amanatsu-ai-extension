using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Character;
using CharacterCreation;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Amanatsu.AiExtension;

/// <summary>
/// One cursor-addressed stream of what changed in the game: scenes, characters, the creator, root objects,
/// warning and error log lines, POST operations and watched values. Also keeps snapshots for debug/diff.
/// </summary>
internal static class DebugEvents
{
    private const int Capacity = 5000;
    private static readonly object Sync = new();
    private static readonly LinkedList<Event> Events = new();
    private static long _sequence;

    internal sealed record Event(long seq, string utc, int frame, string type, object data);

    internal static long Cursor { get { lock (Sync) return _sequence; } }

    internal static void Push(string type, object data)
    {
        // Log lines can arrive from other threads, where Unity must not be called.
        var frame = Environment.CurrentManagedThreadId == _mainThread ? Time.frameCount : -1;
        lock (Sync)
        {
            Events.AddLast(new Event(++_sequence, DateTimeOffset.UtcNow.ToString("O"), frame, type, data));
            while (Events.Count > Capacity) Events.RemoveFirst();
        }
    }

    internal static List<Event> Read(long since, int limit, HashSet<string> types, string contains, out long next, out long oldest, out bool more)
    {
        var result = new List<Event>();
        lock (Sync)
        {
            oldest = Events.First?.Value.seq ?? _sequence + 1;
            next = since;
            more = false;
            foreach (var e in Events)
            {
                if (e.seq <= since) continue;
                if (types != null && !types.Contains(e.type)) continue;
                if (contains != null && JsonSerializer.Serialize(e.data).IndexOf(contains, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (result.Count == limit) { more = true; break; }
                result.Add(e);
            }
            next = more ? result[^1].seq : _sequence;
        }
        return result;
    }

    internal static ApiResult List(string query)
    {
        var q = System.Web.HttpUtility.ParseQueryString(query.TrimStart('?'));
        long since = 0;
        if (q["since"] != null && !long.TryParse(q["since"], out since)) throw new ArgumentException("since must be an integer cursor");
        var limit = 200;
        if (q["limit"] != null && (!int.TryParse(q["limit"], out limit) || limit < 1 || limit > 1000)) throw new ArgumentException("limit must be 1..1000");
        var types = string.IsNullOrEmpty(q["type"]) ? null : q["type"].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet();
        var events = Read(since, limit, types, q["contains"], out var next, out var oldest, out var more);
        return new(200, new { events, count = events.Count, next, oldest, more });
    }

    // ---- observation, called every frame from AiBridgeBehaviour ----

    private static Dictionary<int, string> _scenes;
    private static int _activeScene = int.MinValue;
    private static float _nextSlow;
    private static Dictionary<int, (string Scene, string Name)> _roots;
    private static int? _humans;
    private static bool? _creator;

    private static int _mainThread = -1;

    internal static void Tick()
    {
        _mainThread = Environment.CurrentManagedThreadId;
        try { ObserveScenes(); }
        catch (Exception ex) { AiBridgeBehaviour.LogSource?.LogWarning($"Scene observation failed: {ex.Message}"); }
        if (Time.realtimeSinceStartup < _nextSlow) return;
        _nextSlow = Time.realtimeSinceStartup + 0.5f;
        try { ObserveState(); ObserveRoots(); Watches.Check(); }
        catch (Exception ex) { AiBridgeBehaviour.LogSource?.LogWarning($"Event observation failed: {ex.Message}"); }
    }

    private static void ObserveScenes()
    {
        var now = new Dictionary<int, string>();
        for (var i = 0; i < SceneManager.sceneCount; i++)
        {
            var s = SceneManager.GetSceneAt(i);
            if (s.isLoaded) now[s.handle] = s.name;
        }
        if (_scenes != null)
        {
            foreach (var (handle, name) in now)
                if (!_scenes.ContainsKey(handle)) Push("scene_loaded", new { scene = name, handle });
            foreach (var (handle, name) in _scenes)
                if (!now.ContainsKey(handle)) Push("scene_unloaded", new { scene = name, handle });
        }
        _scenes = now;
        var active = SceneManager.GetActiveScene();
        if (active.handle != _activeScene)
        {
            if (_activeScene != int.MinValue) Push("active_scene", new { scene = active.name, handle = active.handle });
            _activeScene = active.handle;
        }
    }

    private static void ObserveState()
    {
        var all = Human.List;
        var humans = all == null ? 0 : all.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<Human>>().Count;
        if (_humans != null && humans != _humans) Push("humans", new { before = _humans, after = humans });
        _humans = humans;
        var creator = HumanCustom.Instance?.Human != null;
        if (_creator != null && creator != _creator) Push("creator", new { ready = creator });
        _creator = creator;
    }

    private static void ObserveRoots()
    {
        var now = new Dictionary<int, (string Scene, string Name)>();
        foreach (var (scene, root) in DebugInspect.SceneRoots())
            now[root.GetInstanceID()] = (scene, root.name);
        if (_roots != null)
        {
            foreach (var (id, v) in now)
                if (!_roots.ContainsKey(id)) Push("root_added", new { id, name = v.Name, scene = v.Scene });
            foreach (var (id, v) in _roots)
                if (!now.ContainsKey(id)) Push("root_removed", new { id, name = v.Name, scene = v.Scene });
        }
        _roots = now;
    }
}

/// <summary>Values read every half second; each change becomes a "watch" event.</summary>
internal static class Watches
{
    private static readonly HashSet<string> ObjectProperties = new()
    { "activeSelf", "activeInHierarchy", "position", "localPosition", "localEulerAngles", "localScale", "name", "childCount" };

    private sealed class Watch
    {
        public int Id;
        public string Name, Path, Property;
        public int Target;
        public int Depth;
        public string Last;
        public object LastValue;
        public long Changes;
    }

    private static readonly Dictionary<int, Watch> All = new();
    private static int _nextId;

    private static float[] V(Vector3 v) => new[] { (float)Math.Round(v.x, 4), (float)Math.Round(v.y, 4), (float)Math.Round(v.z, 4) };

    private static bool Read(Watch w, out object value)
    {
        value = null;
        if (w.Property != null)
        {
            var go = DebugInspect.FindGameObject(w.Target, null);
            if (go == null) return false;
            var t = go.transform;
            value = w.Property switch
            {
                "activeSelf" => go.activeSelf, "activeInHierarchy" => go.activeInHierarchy, "name" => go.name,
                "position" => V(t.position), "localPosition" => V(t.localPosition), "localEulerAngles" => V(t.localEulerAngles),
                "localScale" => V(t.localScale), _ => t.childCount
            };
            return true;
        }
        var resolved = DebugInspect.Resolve(w.Target, w.Path, true, out _, out var failure);
        if (failure != null) return false;
        value = DebugInspect.DescribeValue(resolved, w.Depth, true);
        return true;
    }

    internal static ApiResult Add(JsonElement j)
    {
        var w = new Watch { Name = j.TryGetProperty("name", out var n) ? n.GetString() : null };
        w.Property = j.TryGetProperty("property", out var p) ? p.GetString() : null;
        w.Path = j.TryGetProperty("path", out var fp) ? fp.GetString() : null;
        if (w.Property != null && !ObjectProperties.Contains(w.Property))
            throw new ArgumentException("property must be one of " + string.Join(", ", ObjectProperties));
        if (w.Property != null && w.Path != null) throw new ArgumentException("give path (component field) or property (GameObject), not both");
        w.Depth = 0;
        if (j.TryGetProperty("depth", out var d) && (!d.TryGetInt32(out w.Depth) || w.Depth < 0 || w.Depth > 2))
            throw new ArgumentException("depth must be 0..2");
        if (j.TryGetProperty("id", out var idElement))
        {
            if (!idElement.TryGetInt32(out w.Target)) throw new ArgumentException("id must be an integer");
        }
        else if (w.Property != null && j.TryGetProperty("objectPath", out var op))
        {
            var go = DebugInspect.FindGameObject(null, op.GetString());
            if (go == null) return new(404, new { error = "object not found" });
            w.Target = go.GetInstanceID();
        }
        else throw new ArgumentException(w.Property != null ? "give id or objectPath of a GameObject" : "give id of a component");
        if (!Read(w, out var value)) return new(404, new { error = w.Property != null ? "object not found" : "component or field not found; check id and path with debug/component" });
        w.LastValue = value;
        w.Last = JsonSerializer.Serialize(value);
        w.Id = ++_nextId;
        All[w.Id] = w;
        return new(200, new { watch = w.Id, name = w.Name, value, cursor = DebugEvents.Cursor });
    }

    internal static ApiResult Remove(JsonElement j)
    {
        if (j.TryGetProperty("all", out var a) && a.ValueKind == JsonValueKind.True)
        {
            var count = All.Count;
            All.Clear();
            return new(200, new { removed = count });
        }
        if (!j.TryGetProperty("watch", out var id) || !id.TryGetInt32(out var watch)) throw new ArgumentException("integer watch (or all:true) required");
        return All.Remove(watch) ? new(200, new { removed = 1 }) : new(404, new { error = "no such watch" });
    }

    internal static ApiResult List() => new(200, new
    {
        watches = All.Values.OrderBy(w => w.Id).Select(w => new
        {
            watch = w.Id, name = w.Name, id = w.Target, path = w.Path, property = w.Property, depth = w.Depth,
            value = w.LastValue, changes = w.Changes
        }).ToArray()
    });

    internal static void Check()
    {
        foreach (var w in All.Values.ToArray())
        {
            object value;
            bool found;
            try { found = Read(w, out value); }
            catch (Exception ex) { found = false; value = ex.Message; }
            if (!found)
            {
                All.Remove(w.Id);
                DebugEvents.Push("watch_lost", new { watch = w.Id, name = w.Name, id = w.Target, path = w.Path, property = w.Property });
                continue;
            }
            var json = JsonSerializer.Serialize(value);
            if (json == w.Last) continue;
            DebugEvents.Push("watch", new { watch = w.Id, name = w.Name, before = w.LastValue, after = value });
            w.Last = json;
            w.LastValue = value;
            w.Changes++;
        }
    }
}

/// <summary>Flat key/value copies of a subtree (active flags, transforms, component types and fields) for diffing.</summary>
internal static class Snapshots
{
    private sealed class Snapshot
    {
        public string Name;
        public int? RootId;
        public string RootPath, Scene;
        public int Depth, Limit;
        public HashSet<string> FieldTypes; // null: no fields; contains "*": every component
        public string Utc;
        public int Frame;
        public bool Truncated;
        public Dictionary<string, string> Values;
    }

    private static readonly Dictionary<string, Snapshot> All = new();
    private static int _counter;

    private static string R(float v) => Math.Round(v, 4).ToString(System.Globalization.CultureInfo.InvariantCulture);
    private static string V(Vector3 v) => $"[{R(v.x)},{R(v.y)},{R(v.z)}]";

    private static Dictionary<string, string> Capture(Snapshot s, out bool truncated)
    {
        var values = new Dictionary<string, string>();
        var roots = new List<(Transform T, string Key)>();
        if (s.RootId != null || s.RootPath != null)
        {
            var go = DebugInspect.FindGameObject(s.RootId, s.RootId == null ? s.RootPath : null) ?? (s.RootPath != null ? DebugInspect.FindGameObject(null, s.RootPath) : null);
            if (go == null) throw new KeyNotFoundException("snapshot root not found");
            roots.Add((go.transform, go.name));
        }
        else
        {
            var siblings = new Dictionary<string, int>();
            foreach (var (scene, root) in DebugInspect.SceneRoots())
            {
                if (s.Scene != null && scene != s.Scene) continue;
                var key = scene + ":" + root.name;
                var n = siblings.TryGetValue(key, out var c) ? c : 0;
                siblings[key] = n + 1;
                roots.Add((root.transform, n == 0 ? key : $"{key}[{n}]"));
            }
        }
        var objects = 0;
        truncated = false;
        var stack = new Stack<(Transform T, string Key, int Depth)>(roots.Select(r => (r.T, r.Key, 0)).Reverse());
        while (stack.Count > 0)
        {
            var (t, key, depth) = stack.Pop();
            if (objects == s.Limit) { truncated = true; break; }
            objects++;
            var go = t.gameObject;
            values[key + " :: active"] = go.activeSelf ? "true" : "false";
            values[key + " :: localPosition"] = V(t.localPosition);
            values[key + " :: localEulerAngles"] = V(t.localEulerAngles);
            values[key + " :: localScale"] = V(t.localScale);
            var components = go.GetComponents<Component>();
            values[key + " :: components"] = string.Join(",", components.Select(DebugInspect.ComponentTypeName));
            if (s.FieldTypes != null)
            {
                var seen = new Dictionary<string, int>();
                foreach (var c in components)
                {
                    if (c == null) continue;
                    var type = DebugInspect.ComponentTypeName(c);
                    var shortType = type[(type.LastIndexOf('.') + 1)..];
                    if (!s.FieldTypes.Contains("*") && !s.FieldTypes.Contains(shortType) && !s.FieldTypes.Contains(type)) continue;
                    var n = seen.TryGetValue(shortType, out var k) ? k : 0;
                    seen[shortType] = n + 1;
                    var prefix = $"{key} :: {shortType}{(n == 0 ? "" : $"[{n}]")}.";
                    var described = DebugInspect.DescribeComponent(c, 1);
                    var fields = JsonSerializer.SerializeToElement(described);
                    if (!fields.TryGetProperty("fields", out var map)) continue;
                    foreach (var f in map.EnumerateObject())
                        values[prefix + f.Name] = f.Value.ValueKind == JsonValueKind.Object && f.Value.TryGetProperty("ref", out var r) ? "ref:" + r : f.Value.GetRawText();
                }
            }
            if (depth >= s.Depth) continue;
            var names = new Dictionary<string, int>();
            var children = new List<(Transform, string, int)>();
            for (var i = 0; i < t.childCount; i++)
            {
                var child = t.GetChild(i);
                var n = names.TryGetValue(child.name, out var c) ? c : 0;
                names[child.name] = n + 1;
                children.Add((child, $"{key}/{child.name}{(n == 0 ? "" : $"[{n}]")}", depth + 1));
            }
            for (var i = children.Count - 1; i >= 0; i--) stack.Push(children[i]);
        }
        return values;
    }

    internal static ApiResult Take(JsonElement j)
    {
        var s = new Snapshot
        {
            Name = j.TryGetProperty("name", out var n) ? n.GetString() : null,
            RootPath = j.TryGetProperty("path", out var p) ? p.GetString() : null,
            Scene = j.TryGetProperty("scene", out var sc) ? sc.GetString() : null,
            FieldTypes = j.TryGetProperty("fieldTypes", out var f) && f.ValueKind == JsonValueKind.String && f.GetString().Length > 0
                ? f.GetString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet() : null,
            Depth = 64, Limit = 2000
        };
        if (j.TryGetProperty("id", out var id))
        {
            if (!id.TryGetInt32(out var rootId)) throw new ArgumentException("id must be an integer");
            s.RootId = rootId;
            var go = DebugInspect.FindGameObject(rootId, null);
            if (go == null) return new(404, new { error = "object not found" });
            s.RootPath = DebugInspect.ObjectPath(go.transform);
        }
        if (j.TryGetProperty("depth", out var d) && (!d.TryGetInt32(out s.Depth) || s.Depth < 0 || s.Depth > 64)) throw new ArgumentException("depth must be 0..64");
        if (j.TryGetProperty("limit", out var l) && (!l.TryGetInt32(out s.Limit) || s.Limit < 1 || s.Limit > 20000)) throw new ArgumentException("limit must be 1..20000 objects");
        s.Name ??= "snap" + ++_counter;
        if (!System.Text.RegularExpressions.Regex.IsMatch(s.Name, "^[A-Za-z0-9._-]{1,64}$")) throw new ArgumentException("name must be 1..64 of letters, digits, . _ -");
        try { s.Values = Capture(s, out s.Truncated); }
        catch (KeyNotFoundException ex) { return new(404, new { error = ex.Message }); }
        s.Utc = DateTimeOffset.UtcNow.ToString("O");
        s.Frame = Time.frameCount;
        All[s.Name] = s;
        return new(200, new { snapshot = s.Name, root = s.RootPath, scene = s.Scene, values = s.Values.Count, truncated = s.Truncated, s.Utc, s.Frame });
    }

    internal static ApiResult List() => new(200, new
    {
        snapshots = All.Values.OrderBy(s => s.Utc).Select(s => new { snapshot = s.Name, root = s.RootPath, scene = s.Scene, depth = s.Depth, fieldTypes = s.FieldTypes?.OrderBy(x => x).ToArray(), values = s.Values.Count, truncated = s.Truncated, utc = s.Utc, frame = s.Frame }).ToArray()
    });

    internal static ApiResult Delete(JsonElement j)
    {
        if (j.TryGetProperty("all", out var a) && a.ValueKind == JsonValueKind.True)
        {
            var count = All.Count;
            All.Clear();
            return new(200, new { removed = count });
        }
        var name = j.TryGetProperty("snapshot", out var n) ? n.GetString() : throw new ArgumentException("snapshot (or all:true) required");
        return All.Remove(name) ? new(200, new { removed = 1 }) : new(404, new { error = "no such snapshot" });
    }

    internal static ApiResult Diff(string query)
    {
        var q = System.Web.HttpUtility.ParseQueryString(query.TrimStart('?'));
        if (q["from"] == null || !All.TryGetValue(q["from"], out var from)) return new(404, new { error = "from must name a snapshot; see debug/snapshots" });
        Dictionary<string, string> to;
        string toName;
        if (q["to"] != null)
        {
            if (!All.TryGetValue(q["to"], out var target)) return new(404, new { error = "no such to snapshot" });
            to = target.Values;
            toName = target.Name;
        }
        else
        {
            try { to = Capture(from, out _); }
            catch (KeyNotFoundException ex) { return new(404, new { error = ex.Message }); }
            toName = "now";
        }
        var limit = 500;
        if (q["limit"] != null && (!int.TryParse(q["limit"], out limit) || limit < 1)) throw new ArgumentException("limit must be a positive integer");
        var contains = q["contains"];
        bool Keep(string key) => contains == null || key.IndexOf(contains, StringComparison.OrdinalIgnoreCase) >= 0;
        var changed = from.Values.Where(kv => Keep(kv.Key) && to.TryGetValue(kv.Key, out var v) && v != kv.Value)
            .Select(kv => new { key = kv.Key, before = kv.Value, after = to[kv.Key] }).ToList();
        var added = to.Where(kv => Keep(kv.Key) && !from.Values.ContainsKey(kv.Key)).Select(kv => new { key = kv.Key, value = kv.Value }).ToList();
        var removed = from.Values.Where(kv => Keep(kv.Key) && !to.ContainsKey(kv.Key)).Select(kv => new { key = kv.Key, value = kv.Value }).ToList();
        return new(200, new
        {
            from = from.Name, to = toName,
            counts = new { changed = changed.Count, added = added.Count, removed = removed.Count },
            changed = changed.Take(limit), added = added.Take(limit), removed = removed.Take(limit),
            truncated = changed.Count > limit || added.Count > limit || removed.Count > limit
        });
    }
}
