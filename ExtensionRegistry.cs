using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using BepInEx.Logging;
using CharacterCreation;

namespace Amanatsu.AiExtension;

/// <summary>Endpoints other plugins publish at /api/v1/ext/{owner}/{path} through sdk/AiExtensionBridge.cs.</summary>
internal static class ExtensionRegistry
{
    private const string Slot = "amanatsu.ai-extension.registrations";
    private static readonly Regex OwnerPattern = new("^[A-Za-z0-9._-]+$");
    private static readonly Regex PathPattern = new("^[a-z0-9-]+(/[a-z0-9-]+)*$");
    private static readonly Dictionary<(string Method, string Path), Endpoint> Endpoints = new();
    private static readonly List<object> Rejected = new();
    private static ManualLogSource _log;

    private sealed record Endpoint(string Owner, string Method, string Path, JsonElement Metadata, bool Mutates, string Scene,
        Func<string, string, string, Tuple<int, string>> Handler);

    internal static void Install(ManualLogSource log)
    {
        _log = log;
        var domain = AppDomain.CurrentDomain;
        lock (domain)
        {
            if (domain.GetData(Slot) is List<object[]> pending)
                foreach (var registration in pending) Accept(registration);
            domain.SetData(Slot, new Action<object[]>(Accept));
        }
    }

    private static void Accept(object[] registration)
    {
        var owner = registration.Length > 0 ? registration[0] as string : null;
        try
        {
            if (registration.Length != 3 || owner == null || registration[1] is not string json ||
                registration[2] is not Func<string, string, string, Tuple<int, string>> handler)
                throw new ArgumentException("registration must be (owner, metadataJson, Func<string,string,string,Tuple<int,string>>)");
            if (!OwnerPattern.IsMatch(owner)) throw new ArgumentException("owner must be the plugin GUID (letters, digits, . _ -)");
            using var document = JsonDocument.Parse(json);
            var meta = document.RootElement;
            string Text(string key) => meta.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : throw new ArgumentException($"metadata {key} must be a string");
            var method = Text("method");
            if (method is not ("GET" or "POST")) throw new ArgumentException("method must be GET or POST");
            var path = Text("path");
            if (!PathPattern.IsMatch(path)) throw new ArgumentException("path must be lowercase segments of a-z, 0-9 and -");
            Text("summary");
            if (!meta.TryGetProperty("mutates", out var mutates) || mutates.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new ArgumentException("metadata mutates must be true or false");
            var scene = Text("scene");
            if (scene is not ("any" or "creator")) throw new ArgumentException("scene must be any or creator");
            var full = "ext/" + owner + "/" + path;
            if (Endpoints.ContainsKey((method, full))) throw new ArgumentException($"{method} {full} is already registered");

            // The catalog form of the entry: the full path and the owner replace what the plugin gave.
            var entry = new Dictionary<string, JsonElement>();
            foreach (var property in meta.EnumerateObject()) entry[property.Name] = property.Value.Clone();
            entry["path"] = JsonSerializer.SerializeToElement(full);
            entry["extension"] = JsonSerializer.SerializeToElement(owner);
            Endpoints[(method, full)] = new Endpoint(owner, method, full, JsonSerializer.SerializeToElement(entry), mutates.GetBoolean(), scene, handler);
            _log?.LogInfo($"Extension endpoint registered: {method} /api/v1/{full}");
        }
        catch (Exception ex) when (ex is ArgumentException or JsonException)
        {
            Rejected.Add(new { owner, error = ex.Message });
            _log?.LogError($"Extension endpoint from {owner ?? "?"} rejected: {ex.Message}");
        }
    }

    internal static IEnumerable<JsonElement> Catalog() => Endpoints.Values.OrderBy(e => e.Path).ThenBy(e => e.Method).Select(e => e.Metadata);

    internal static bool Has(string method, string path) => Endpoints.ContainsKey((method, path));

    internal static ApiResult List() => new(200, new
    {
        extensions = Endpoints.Values.GroupBy(e => e.Owner).OrderBy(g => g.Key).Select(g => new
        {
            owner = g.Key,
            endpoints = g.OrderBy(e => e.Path).Select(e => e.Method + " /api/v1/" + e.Path).ToArray()
        }).ToArray(),
        rejected = Rejected.ToArray()
    });

    /// <summary>Runs a registered handler; path is relative to /api/v1/ (starts with ext/).</summary>
    internal static ApiResult Execute(string method, string path, string query, string body)
    {
        if (!Endpoints.TryGetValue((method, path), out var endpoint))
        {
            var other = Endpoints.Keys.Where(k => k.Path == path).Select(k => k.Method).ToArray();
            return other.Length > 0
                ? new ApiResult(405, new { error = $"{path} answers {string.Join(" or ", other)}" })
                : new ApiResult(404, new { error = "no extension registered this endpoint; see GET extensions" });
        }
        if (endpoint.Scene == "creator" && HumanCustom.Instance?.Human == null)
            return new ApiResult(409, new { error = "open character creation first", extension = endpoint.Owner });
        Tuple<int, string> result;
        try { result = endpoint.Handler(method, query ?? "", body ?? ""); }
        catch (Exception ex)
        {
            _log?.LogError($"Extension {endpoint.Owner} failed on {method} {path}: {ex}");
            return new ApiResult(500, new { error = ex.Message, extension = endpoint.Owner });
        }
        if (result == null || result.Item1 < 100 || result.Item1 > 599)
            return new ApiResult(500, new { error = "extension returned no valid HTTP status", extension = endpoint.Owner });
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrEmpty(result.Item2) ? "{}" : result.Item2);
            return new ApiResult(result.Item1, document.RootElement.Clone());
        }
        catch (JsonException)
        {
            return new ApiResult(500, new { error = "extension returned invalid JSON", extension = endpoint.Owner });
        }
    }
}
