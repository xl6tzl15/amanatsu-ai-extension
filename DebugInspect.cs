using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Amanatsu.AiExtension;

/// <summary>
/// Read-only view of scenes, GameObjects and component fields. Native getters are never called except the
/// SafeGetters below, because some (Renderer.material and similar) create objects or change state when read.
/// </summary>
internal static class DebugInspect
{
    private const int MaxArrayItems = 20;

    // Property getters known to only read. Everything else is read through fields.
    private static readonly HashSet<string> SafeGetters = new()
    {
        "enabled", "isActiveAndEnabled", "interactable", "isOn", "value", "minValue", "maxValue", "wholeNumbers",
        "text", "color", "alpha", "sprite", "sharedMaterial", "sharedMaterials", "sharedMesh", "sortingOrder",
        "raycastTarget", "fontSize", "isPlaying", "isVisible", "fieldOfView", "orthographic", "intensity", "range",
        "rootBone", "bones", "bounds", "localBounds", "shadowCastingMode", "receiveShadows", "updateWhenOffscreen",
        "runtimeAnimatorController", "speed", "nearClipPlane", "farClipPlane", "depth", "cullingMask", "shadows",
        "renderMode", "sortingLayerName", "pixelRect", "anchoredPosition", "sizeDelta", "pivot", "anchorMin", "anchorMax"
    };

    private static System.Collections.Specialized.NameValueCollection Query(string query) =>
        System.Web.HttpUtility.ParseQueryString((query ?? "").TrimStart('?'));

    private static int IntParam(System.Collections.Specialized.NameValueCollection q, string name, int fallback, int min, int max)
    {
        if (q[name] == null) return fallback;
        if (!int.TryParse(q[name], out var value) || value < min || value > max)
            throw new ArgumentException($"{name} must be {min}..{max}");
        return value;
    }

    private static bool BoolParam(System.Collections.Specialized.NameValueCollection q, string name, bool fallback) =>
        q[name] == null ? fallback : q[name] is "1" or "true" ? true : q[name] is "0" or "false" ? false : throw new ArgumentException(name + " must be true or false");

    // ---- scenes and objects ----

    private static IEnumerable<Scene> LoadedScenes()
    {
        for (var i = 0; i < SceneManager.sceneCount; i++) yield return SceneManager.GetSceneAt(i);
        // DontDestroyOnLoad cannot be enumerated through SceneManager, so an empty marker object is kept there.
        if (_marker == null)
        {
            _marker = new GameObject("AmanatsuAiExtension.DontDestroyOnLoadMarker");
            UnityEngine.Object.DontDestroyOnLoad(_marker);
        }
        var persistent = _marker.scene;
        if (persistent.IsValid()) yield return persistent;
    }

    private static GameObject _marker;
    private const string Hidden = "(hidden)";

    // Runtime objects outside every scene, such as the BepInEx manager that holds plugin components.
    private static IEnumerable<GameObject> HiddenRoots() =>
        Resources.FindObjectsOfTypeAll<GameObject>().Where(go => go != null && !go.scene.IsValid() &&
            (go.hideFlags & HideFlags.DontSave) != 0 && go.transform.parent == null);

    private static string SceneName(GameObject go) => go.scene.IsValid() ? go.scene.name : Hidden;

    private static IEnumerable<GameObject> Roots(string sceneName)
    {
        foreach (var scene in LoadedScenes())
            if (scene.IsValid() && scene.isLoaded && (sceneName == null || scene.name == sceneName))
                foreach (var root in scene.GetRootGameObjects()) yield return root;
        if (sceneName == null || sceneName == Hidden)
            foreach (var root in HiddenRoots()) yield return root;
    }

    internal static ApiResult Scenes()
    {
        var active = SceneManager.GetActiveScene().handle;
        var scenes = LoadedScenes().Select(s => (object)new
        {
            name = s.name, handle = s.handle, buildIndex = s.buildIndex, isLoaded = s.isLoaded,
            active = s.handle == active, rootCount = s.rootCount
        }).ToList();
        scenes.Add(new { name = Hidden, handle = 0, buildIndex = -1, isLoaded = true, active = false, rootCount = HiddenRoots().Count() });
        return new(200, new { scenes });
    }

    internal static string ObjectPath(Transform transform)
    {
        var names = new List<string>();
        for (var t = transform; t != null && names.Count < 64; t = t.parent) names.Add(t.gameObject.name);
        names.Reverse();
        return string.Join("/", names);
    }

    private static UnityEngine.Object FromId(int id) => Resources.InstanceIDToObject(id);

    private static GameObject GameObjectFromId(int id)
    {
        var obj = FromId(id);
        if (obj == null) return null;
        var go = obj.TryCast<GameObject>();
        if (go != null) return go;
        return obj.TryCast<Component>()?.gameObject;
    }

    // "Root/Child/Grandchild" as in ui/buttons paths; the first object whose names match is used.
    private static GameObject GameObjectFromPath(string path)
    {
        var parts = path.Split('/');
        foreach (var root in Roots(null))
        {
            if (root.name != parts[0]) continue;
            var found = Descend(root.transform, parts, 1);
            if (found != null) return found.gameObject;
        }
        return null;
    }

    private static Transform Descend(Transform current, string[] parts, int index)
    {
        if (index == parts.Length) return current;
        for (var i = 0; i < current.childCount; i++)
        {
            var child = current.GetChild(i);
            if (child.name != parts[index]) continue;
            var found = Descend(child, parts, index + 1);
            if (found != null) return found;
        }
        return null;
    }

    private static GameObject Target(System.Collections.Specialized.NameValueCollection q)
    {
        if (q["id"] != null)
        {
            if (!int.TryParse(q["id"], out var id)) throw new ArgumentException("id must be an integer");
            return GameObjectFromId(id);
        }
        if (!string.IsNullOrEmpty(q["path"])) return GameObjectFromPath(q["path"]);
        throw new ArgumentException("give id or path");
    }

    private static string TypeName(Component c)
    {
        if (c == null) return "(missing script)";
        try { return c.GetIl2CppType().FullName; } catch { return c.GetType().FullName; }
    }

    private static string ShortName(string fullName) => fullName.Substring(fullName.LastIndexOf('.') + 1);

    internal static ApiResult Tree(string query)
    {
        var q = Query(query);
        var depth = IntParam(q, "depth", 3, 0, 64);
        var offset = IntParam(q, "offset", 0, 0, int.MaxValue);
        var limit = IntParam(q, "limit", 200, 1, 2000);
        var withComponents = BoolParam(q, "components", true);
        var filter = q["filter"];
        var roots = new List<(Transform T, int Depth)>();
        if (q["id"] != null || !string.IsNullOrEmpty(q["path"]))
        {
            var go = Target(q);
            if (go == null) return new(404, new { error = "object not found; ids change every run, refresh with debug/tree or debug/find" });
            roots.Add((go.transform, 0));
        }
        else
        {
            foreach (var root in Roots(q["scene"])) roots.Add((root.transform, 0));
            if (roots.Count == 0) return new(404, new { error = "scene not loaded or empty; see debug/scenes" });
        }

        var nodes = new List<object>();
        var matched = 0;
        var more = false;
        var stack = new Stack<(Transform T, int Depth)>(Enumerable.Reverse(roots));
        while (stack.Count > 0)
        {
            var (t, d) = stack.Pop();
            var go = t.gameObject;
            string[] components = null;
            if (withComponents || filter != null)
                components = go.GetComponents<Component>().Select(TypeName).Select(ShortName).ToArray();
            var include = filter == null || go.name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                          components.Any(c => c.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0);
            if (include)
            {
                if (matched >= offset)
                {
                    if (nodes.Count == limit) { more = true; break; }
                    nodes.Add(new
                    {
                        id = go.GetInstanceID(), name = go.name, depth = d,
                        path = filter != null ? ObjectPath(t) : null,
                        active = go.activeSelf, activeInHierarchy = go.activeInHierarchy,
                        childCount = t.childCount, components = withComponents ? components : null
                    });
                }
                matched++;
            }
            if (d < depth)
                for (var i = t.childCount - 1; i >= 0; i--) stack.Push((t.GetChild(i), d + 1));
        }
        return new(200, new { nodes, count = nodes.Count, offset, more, next = more ? offset + nodes.Count : (int?)null });
    }

    private static float[] V(Vector3 v) => new[] { v.x, v.y, v.z };

    internal static ApiResult Object(string query)
    {
        var q = Query(query);
        var go = Target(q);
        if (go == null) return new(404, new { error = "object not found; ids change every run, refresh with debug/tree or debug/find" });
        var t = go.transform;
        var children = new List<object>();
        for (var i = 0; i < t.childCount && i < 500; i++)
        {
            var c = t.GetChild(i).gameObject;
            children.Add(new { id = c.GetInstanceID(), name = c.name, active = c.activeSelf });
        }
        return new(200, new
        {
            id = go.GetInstanceID(), name = go.name, path = ObjectPath(t), scene = SceneName(go),
            active = go.activeSelf, activeInHierarchy = go.activeInHierarchy, layer = go.layer, tag = SafeTag(go),
            parent = t.parent == null ? null : new { id = t.parent.gameObject.GetInstanceID(), name = t.parent.gameObject.name },
            transform = new
            {
                localPosition = V(t.localPosition), localEulerAngles = V(t.localEulerAngles), localScale = V(t.localScale),
                position = V(t.position), eulerAngles = V(t.eulerAngles), lossyScale = V(t.lossyScale)
            },
            components = go.GetComponents<Component>().Select(c => new
            {
                id = c == null ? 0 : c.GetInstanceID(), type = TypeName(c),
                enabled = c?.TryCast<Behaviour>() is { } b ? b.enabled : (bool?)null
            }).ToArray(),
            childCount = t.childCount, children
        });
    }

    private static string SafeTag(GameObject go)
    {
        try { return go.tag; } catch { return null; }
    }

    internal static ApiResult Find(string query)
    {
        var q = Query(query);
        var type = q["type"];
        var name = q["name"];
        if (string.IsNullOrEmpty(type) && string.IsNullOrEmpty(name)) throw new ArgumentException("give type and/or name");
        var limit = IntParam(q, "limit", 50, 1, 500);
        var includeInactive = BoolParam(q, "inactive", true);
        var results = new List<object>();
        var more = false;
        foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            if (go == null || (!go.scene.IsValid() && (go.hideFlags & HideFlags.DontSave) == 0)) continue;
            if (!includeInactive && !go.activeInHierarchy) continue;
            if (!string.IsNullOrEmpty(name) && go.name.IndexOf(name, StringComparison.OrdinalIgnoreCase) < 0) continue;
            object[] matches = null;
            if (!string.IsNullOrEmpty(type))
            {
                matches = go.GetComponents<Component>().Where(c => c != null).Select(c => (c, n: TypeName(c)))
                    .Where(x => string.Equals(x.n, type, StringComparison.OrdinalIgnoreCase) || string.Equals(ShortName(x.n), type, StringComparison.OrdinalIgnoreCase))
                    .Select(x => (object)new { id = x.c.GetInstanceID(), type = x.n }).ToArray();
                if (matches.Length == 0) continue;
            }
            if (results.Count == limit) { more = true; break; }
            results.Add(new { id = go.GetInstanceID(), name = go.name, path = ObjectPath(go.transform), scene = SceneName(go), activeInHierarchy = go.activeInHierarchy, components = matches });
        }
        return new(200, new { results, count = results.Count, more });
    }

    // ---- component values ----

    private static readonly Lazy<Dictionary<string, Type>> TypesByName = new(() =>
    {
        var map = new Dictionary<string, Type>();
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            foreach (var type in SafeTypes(assembly))
                if (type.FullName != null) map.TryAdd(type.FullName, type);
        return map;
    });

    private static Type[] SafeTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t != null).ToArray(); }
        catch { return Array.Empty<Type>(); }
    }

    private static Type ManagedType(Il2CppSystem.Type il2cppType)
    {
        var name = il2cppType?.FullName;
        if (name == null) return null;
        var map = TypesByName.Value;
        return map.TryGetValue(name, out var t) || map.TryGetValue("Il2Cpp" + name, out t) ? t : null;
    }

    // The object viewed as its actual runtime class, so that subclass fields are visible.
    private static object AsRuntimeType(object value)
    {
        if (value is not Il2CppObjectBase native) return value;
        try
        {
            var type = ManagedType(Il2CppType.TypeFromPointer(IL2CPP.il2cpp_object_get_class(native.Pointer)));
            if (type == null || type == value.GetType() || !type.IsSubclassOf(typeof(Il2CppObjectBase))) return value;
            var cast = typeof(Il2CppObjectBase).GetMethod(nameof(Il2CppObjectBase.TryCast))!.MakeGenericMethod(type);
            return cast.Invoke(native, null) ?? value;
        }
        catch { return value; }
    }

    private static bool StopType(Type t) =>
        t == null || t == typeof(object) || t == typeof(Il2CppObjectBase) || t == typeof(Il2CppSystem.Object) ||
        t == typeof(UnityEngine.Object) || t == typeof(ValueType);

    // Classes generated from the game (in BepInEx/interop-*), as opposed to managed classes injected by plugins.
    private static bool IsGenerated(Type t)
    {
        if (!typeof(Il2CppObjectBase).IsAssignableFrom(t)) return false;
        var location = t.Assembly.Location;
        return !string.IsNullOrEmpty(location) && location.Replace('\\', '/').Contains("/interop-", StringComparison.OrdinalIgnoreCase);
    }

    // Il2CppInterop renames <X>k__BackingField to _X_k__BackingField.
    private static string CleanName(string name) =>
        name.StartsWith("_") && name.EndsWith("_k__BackingField") ? name[1..^"_k__BackingField".Length] : name;

    internal static string FriendlyName(Type type)
    {
        if (type == null) return null;
        if (type.IsArray) return FriendlyName(type.GetElementType()) + "[]";
        var name = (type.IsNested ? FriendlyName(type.DeclaringType) + "+" : type.Namespace == null ? "" : type.Namespace + ".") + type.Name;
        if (!type.IsGenericType) return name;
        var tick = name.IndexOf('`');
        if (tick >= 0) name = name[..tick];
        var args = type.GetGenericArguments().Skip(type.IsNested ? type.DeclaringType.GetGenericArguments().Length : 0);
        return name + "<" + string.Join(", ", args.Select(FriendlyName)) + ">";
    }

    private sealed record Member(string Name, Type Type, Func<object, object> Read, string Kind);

    // IL2CPP fields (interop properties backed by NativeFieldInfoPtr_), plain managed fields, and SafeGetters.
    private static List<Member> Members(Type type, bool getters)
    {
        var members = new List<Member>();
        var seen = new HashSet<string>();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        for (var t = type; !StopType(t); t = t.BaseType)
        {
            var interop = IsGenerated(t);
            foreach (var p in t.GetProperties(flags))
            {
                if (p.GetIndexParameters().Length > 0 || p.GetMethod == null || seen.Contains(p.Name)) continue;
                var nativeField = interop && t.GetField("NativeFieldInfoPtr_" + p.Name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public) != null;
                if (nativeField) { members.Add(new(CleanName(p.Name), p.PropertyType, o => p.GetValue(o), "field")); seen.Add(p.Name); }
                else if (getters && SafeGetters.Contains(p.Name)) { members.Add(new(p.Name, p.PropertyType, o => p.GetValue(o), "getter")); seen.Add(p.Name); }
            }
            if (interop && !t.IsValueType) continue; // interop classes keep no managed instance fields of their own
            foreach (var f in t.GetFields(flags))
            {
                var name = f.Name.StartsWith("<") && f.Name.Contains(">k__BackingField") ? f.Name[1..f.Name.IndexOf('>')] : f.Name;
                if (name.StartsWith("<") || !seen.Add(name)) continue;
                members.Add(new(name, f.FieldType, o => f.GetValue(o), "field"));
            }
        }
        return members;
    }

    internal static ApiResult Component(string query)
    {
        var q = Query(query);
        if (!int.TryParse(q["id"], out var id)) throw new ArgumentException("integer id of a component required (from debug/object)");
        var depth = IntParam(q, "depth", 1, 0, 3);
        var getters = BoolParam(q, "getters", true);
        var path = q["path"];
        var current = Resolve(id, path, getters, out var component, out var failure);
        if (failure != null) return failure.Value;
        return new(200, new
        {
            id, type = TypeName(component), gameObject = new { id = component.gameObject.GetInstanceID(), name = component.gameObject.name },
            path = path ?? "", value = Describe(current, depth, getters, expand: true)
        });
    }

    /// <summary>The component with this instance id, or the value at a field path inside it.</summary>
    internal static object Resolve(int id, string path, bool getters, out Component component, out ApiResult? failure)
    {
        failure = null;
        component = FromId(id)?.TryCast<Component>();
        if (component == null)
        {
            failure = new(404, new { error = "component not found; ids change every run, refresh with debug/object" });
            return null;
        }
        object current = AsRuntimeType(component);
        if (string.IsNullOrEmpty(path)) return current;
        foreach (var segment in path.Split('.'))
        {
            var (name, index) = ParseSegment(segment);
            if (name.Length > 0)
            {
                var member = Members(current.GetType(), getters).FirstOrDefault(m => m.Name == name);
                if (member == null)
                {
                    failure = new(404, new { error = $"no field {name} on {current.GetType().FullName}", at = segment });
                    return null;
                }
                current = AsRuntimeType(member.Read(current));
            }
            if (index != null)
            {
                current = Index(current, index.Value, out var error);
                if (error != null)
                {
                    failure = new(404, new { error, at = segment });
                    return null;
                }
                current = AsRuntimeType(current);
            }
            if (current == null) break;
        }
        return current;
    }

    internal static object DescribeValue(object value, int depth, bool getters) => Describe(value, depth, getters, expand: false);

    // A component's own fields (DescribeValue gives a component only as a reference).
    internal static object DescribeComponent(Component component, int depth) => Describe(AsRuntimeType(component), depth, true, expand: true);

    internal static string ComponentTypeName(Component c) => TypeName(c);

    internal static GameObject FindGameObject(int? id, string path) =>
        id != null ? GameObjectFromId(id.Value) : !string.IsNullOrEmpty(path) ? GameObjectFromPath(path) : null;

    internal static IEnumerable<(string Scene, GameObject Root)> SceneRoots()
    {
        foreach (var scene in LoadedScenes())
            if (scene.IsValid() && scene.isLoaded)
                foreach (var root in scene.GetRootGameObjects()) yield return (scene.name, root);
    }

    internal static IEnumerable<Scene> Scenes(bool includePersistent) =>
        includePersistent ? LoadedScenes() : Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt);

    private static (string Name, int? Index) ParseSegment(string segment)
    {
        var open = segment.IndexOf('[');
        if (open < 0) return (segment, null);
        if (!segment.EndsWith("]") || !int.TryParse(segment[(open + 1)..^1], out var index) || index < 0)
            throw new ArgumentException($"bad path segment {segment}; use name or name[index]");
        return (segment[..open], index);
    }

    private static object Index(object value, int index, out string error)
    {
        error = null;
        var count = CountOf(value);
        if (count == null) { error = "not an array or list"; return null; }
        if (index >= count) { error = $"index {index} out of range (count {count})"; return null; }
        return ItemAt(value, index);
    }

    private static int? CountOf(object value)
    {
        if (value is Array a) return a.Length;
        if (value is IList list) return list.Count;
        if (value is Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppArrayBase array) return array.Length;
        if (value is Il2CppObjectBase && value.GetType().FullName?.StartsWith("Il2CppSystem.Collections.Generic.List`1") == true)
            return (int)value.GetType().GetProperty("Count")!.GetValue(value)!;
        return null;
    }

    private static object ItemAt(object value, int index)
    {
        if (value is Array a) return a.GetValue(index);
        if (value is IList list) return list[index];
        return value.GetType().GetProperty("Item")!.GetValue(value, new object[] { index });
    }

    private static object Describe(object value, int depth, bool getters, bool expand = false)
    {
        switch (value)
        {
            case null: return null;
            case string s: return s.Length > 2000 ? s[..2000] + "…" : s;
            case bool or int or long or short or byte or sbyte or uint or ulong or ushort or char: return value;
            case float f: return float.IsFinite(f) ? f : f.ToString();
            case double d: return double.IsFinite(d) ? d : d.ToString();
            case Enum e: return e.ToString();
            case Vector2 v: return new[] { v.x, v.y };
            case Vector3 v: return new[] { v.x, v.y, v.z };
            case Vector4 v: return new[] { v.x, v.y, v.z, v.w };
            case Quaternion v: return new { quaternion = new[] { v.x, v.y, v.z, v.w }, euler = V(v.eulerAngles) };
            case Color c: return new[] { c.r, c.g, c.b, c.a };
            case Color32 c: return new[] { c.r, c.g, c.b, c.a };
            case Rect r: return new { r.x, r.y, r.width, r.height };
            case Bounds b: return new { center = V(b.center), size = V(b.size) };
            case Il2CppSystem.String s: return (string)s;
            case UnityEngine.Object o when !expand:
                try { return new { @ref = o.GetInstanceID(), type = o.GetIl2CppType().FullName, name = o.name }; }
                catch { return new { @ref = 0, type = o.GetType().FullName, destroyed = true }; }
        }
        var count = CountOf(value);
        if (count != null)
        {
            if (depth <= 0) return new { type = FriendlyName(value.GetType()), count };
            var items = new List<object>();
            for (var i = 0; i < count && i < MaxArrayItems; i++) items.Add(Describe(AsRuntimeType(ItemAt(value, i)), depth - 1, getters));
            return new { type = FriendlyName(value.GetType()), count, items, truncated = count > MaxArrayItems };
        }
        var type = value.GetType();
        if (value is Delegate) return new { type = FriendlyName(type), @delegate = true };
        var isNative = value is Il2CppObjectBase;
        if (!isNative && !type.IsValueType && depth <= 0) return new { type = FriendlyName(type), text = Truncate(value.ToString()) };
        if (depth <= 0 && !expand) return new { type = isNative ? SafeIl2CppName(value) : FriendlyName(type) };
        var fields = new Dictionary<string, object>();
        foreach (var member in Members(type, getters))
        {
            try { fields[member.Name] = Describe(AsRuntimeType(member.Read(value)), depth - 1, getters); }
            catch (Exception ex) { fields[member.Name] = new { error = (ex.InnerException ?? ex).Message }; }
        }
        var typeName = isNative ? SafeIl2CppName(value) : FriendlyName(type);
        return new { type = typeName, fields };
    }

    private static string SafeIl2CppName(object value)
    {
        try
        {
            var name = Il2CppType.TypeFromPointer(IL2CPP.il2cpp_object_get_class(((Il2CppObjectBase)value).Pointer)).FullName;
            // Generic IL2CPP names carry assembly-qualified arguments; the managed view reads better.
            return name.Contains("[[") ? FriendlyName(value.GetType()) : name;
        }
        catch { return value.GetType().FullName; }
    }

    private static string Truncate(string s) => s == null ? null : s.Length > 300 ? s[..300] + "…" : s;

    // ---- types (managed reflection only; safe to run off the main thread) ----

    internal static ApiResult Types(string query)
    {
        var q = Query(query);
        var exact = q["type"];
        if (!string.IsNullOrEmpty(exact))
        {
            if (!TypesByName.Value.TryGetValue(exact, out var type)) return new(404, new { error = "type not found; search with q" });
            return new(200, TypeMembers(type));
        }
        var text = q["q"];
        if (string.IsNullOrEmpty(text) || text.Length < 2) throw new ArgumentException("q (at least 2 characters) or type required");
        var limit = IntParam(q, "limit", 100, 1, 1000);
        var matches = TypesByName.Value.Values
            .Where(t => t.FullName.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0 && !t.FullName.Contains('<'))
            .OrderBy(t => t.FullName.Length).ThenBy(t => t.FullName).ToList();
        return new(200, new
        {
            types = matches.Take(limit).Select(t => new
            {
                type = t.FullName, assembly = t.Assembly.GetName().Name,
                kind = t.IsEnum ? "enum" : t.IsValueType ? "struct" : t.IsInterface ? "interface" : "class",
                baseType = FriendlyName(t.BaseType)
            }).ToArray(),
            count = Math.Min(limit, matches.Count), total = matches.Count, more = matches.Count > limit
        });
    }

    private static object TypeMembers(Type type)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        var interop = IsGenerated(type);
        bool NativeField(string name) => interop && type.GetField("NativeFieldInfoPtr_" + name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public) != null;
        var properties = type.GetProperties(flags).Where(p => p.GetIndexParameters().Length == 0).ToArray();
        var accessors = properties.SelectMany(p => new[] { p.GetMethod, p.SetMethod }).Where(m => m != null).ToHashSet();
        string Sig(MethodInfo m) => $"{FriendlyName(m.ReturnType)} {m.Name}({string.Join(", ", m.GetParameters().Select(p => FriendlyName(p.ParameterType) + " " + p.Name))})";
        return new
        {
            type = type.FullName, assembly = type.Assembly.GetName().Name, baseType = FriendlyName(type.BaseType), interop,
            enumValues = type.IsEnum ? Enum.GetNames(type) : null,
            fields = properties.Where(p => NativeField(p.Name)).Select(p => new { name = CleanName(p.Name), type = FriendlyName(p.PropertyType), @static = p.GetMethod?.IsStatic ?? false })
                .Concat(interop ? Enumerable.Empty<object>().Select(x => new { name = "", type = "", @static = false })
                    : type.GetFields(flags).Where(f => !f.Name.StartsWith("<")).Select(f => new { name = f.Name, type = FriendlyName(f.FieldType), @static = f.IsStatic }))
                .ToArray(),
            properties = properties.Where(p => !NativeField(p.Name)).Select(p => new { name = p.Name, type = FriendlyName(p.PropertyType), @static = (p.GetMethod ?? p.SetMethod).IsStatic, safeToRead = SafeGetters.Contains(p.Name) }).ToArray(),
            methods = type.GetMethods(flags).Where(m => !accessors.Contains(m) && !m.IsSpecialName && !m.Name.StartsWith("<"))
                .Select(m => new { signature = Sig(m), @static = m.IsStatic }).OrderBy(m => m.signature).ToArray()
        };
    }
}
