using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using BepInEx;
using BepInEx.Logging;
using Character;
using CharacterCreation;
using UnityEngine.SceneManagement;

namespace Amanatsu.AiExtension;

/// <summary>Small, local JSONL audit trail for API mutations and game lifecycle events.</summary>
internal static class OperationLog
{
    private static readonly object Sync = new();
    private static readonly string RunId = Guid.NewGuid().ToString("N");
    private static long _sequence;
    private static string _directory;
    private static ManualLogSource _log;
    private static string _lastScene;
    private static int? _lastHumanCount;

    internal static void Initialize(ManualLogSource log)
    {
        _log = log;
        _directory = Path.Combine(Paths.BepInExRootPath, "logs", "amanatsu-ai");
        Directory.CreateDirectory(_directory);
        Record("session_start", new { gameProcessId = Environment.ProcessId });
    }

    private static string FilePath() => Path.Combine(_directory, $"operations-{DateTime.UtcNow:yyyyMMdd}.jsonl");

    private static object Snapshot()
    {
        try
        {
            var scene = SceneManager.GetActiveScene();
            var all = Human.List;
            var count = all == null ? 0 : all.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<Human>>().Count;
            return new { scene = scene.name, sceneHandle = scene.handle, humanCount = count, editorHuman = HumanCustom.Instance?.Human?.Name };
        }
        catch (Exception ex) { return new { unavailable = ex.GetType().Name }; }
    }

    internal static void ObserveGameState()
    {
        try
        {
            var scene = SceneManager.GetActiveScene().name;
            var all = Human.List;
            var count = all == null ? 0 : all.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<Human>>().Count;
            if (scene != _lastScene || count != _lastHumanCount)
            {
                Record("game_state", new { state = Snapshot() });
                _lastScene = scene;
                _lastHumanCount = count;
            }
        }
        catch (Exception ex) { _log?.LogWarning($"Operation state observation failed: {ex.Message}"); }
    }

    private static object SafeArguments(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using var document = JsonDocument.Parse(body);
            var safe = new Dictionary<string, object>();
            foreach (var name in new[] { "id", "value", "region", "view", "command", "category", "parent", "position", "direction", "rotation", "index", "part", "moveRate", "rotRate", "kind", "target", "slot", "channel", "field", "file", "visible", "color", "lastname", "firstname", "nickname", "birthMonth", "birthDay" })
                if (document.RootElement.TryGetProperty(name, out var value)) safe[name] = value.Clone();
            return safe;
        }
        catch { return new { invalidJson = true }; }
    }

    internal static void Request(string method, string path, string body, ApiResult result, double milliseconds, object before)
    {
        if (method != "POST") return;
        object response;
        try
        {
            var json = JsonSerializer.Serialize(result.Body);
            response = json.Length <= 1600 ? result.Body : new { truncated = true, length = json.Length };
        }
        catch { response = null; }
        Record("api_operation", new { method, path, arguments = SafeArguments(body), status = result.Status, response, durationMs = Math.Round(milliseconds, 2), before, after = Snapshot() });
    }

    internal static object Before() => Snapshot();

    internal static void Record(string eventType, object data)
    {
        if (_directory == null) return;
        try
        {
            var entry = new { utc = DateTimeOffset.UtcNow.ToString("O"), runId = RunId, sequence = Interlocked.Increment(ref _sequence), eventType, data };
            var line = JsonSerializer.Serialize(entry) + Environment.NewLine;
            lock (Sync) File.AppendAllText(FilePath(), line);
        }
        catch (Exception ex) { _log?.LogError($"Operation log write failed: {ex.Message}"); }
    }

    internal static ApiResult Read(string query)
    {
        var values = System.Web.HttpUtility.ParseQueryString(query.TrimStart('?'));
        var limit = 100;
        if (values["limit"] != null && (!int.TryParse(values["limit"], out limit) || limit < 1 || limit > 500))
            return new(400, new { error = "limit must be 1..500" });
        var files = Directory.GetFiles(_directory, "operations-*.jsonl").OrderByDescending(x => x).Take(3).ToArray();
        var lines = new List<string>();
        foreach (var file in files)
        {
            lines.InsertRange(0, File.ReadLines(file).TakeLast(limit));
            if (lines.Count >= limit) break;
        }
        var entries = lines.TakeLast(limit).Select(x => JsonSerializer.Deserialize<JsonElement>(x)).ToArray();
        return new(200, new { files, entries, count = entries.Length });
    }
}
