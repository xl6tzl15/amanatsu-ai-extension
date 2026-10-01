using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Character;
using CharacterCreation;
using HarmonyLib;
using UnityEngine.SceneManagement;

namespace Amanatsu.AiExtension;

/// <summary>Read-only observation for mod development: loaded plugins, the BepInEx log, Harmony patches and waiting on conditions.</summary>
internal static class DebugApi
{
    internal static ApiResult Plugins()
    {
        var plugins = new List<object>();
        foreach (var info in IL2CPPChainloader.Instance.Plugins.Values.OrderBy(x => x.Metadata.GUID))
        {
            var file = string.IsNullOrEmpty(info.Location) ? null : new FileInfo(info.Location);
            plugins.Add(new
            {
                guid = info.Metadata.GUID,
                name = info.Metadata.Name,
                version = info.Metadata.Version?.ToString(),
                location = info.Location,
                fileWriteUtc = file is { Exists: true } ? file.LastWriteTimeUtc.ToString("O") : null,
                loaded = info.Instance != null
            });
        }
        return new(200, new { gameProcessId = Environment.ProcessId, startedUtc = System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime().ToString("O"), plugins, count = plugins.Count });
    }

    internal static ApiResult Log(string query)
    {
        var values = System.Web.HttpUtility.ParseQueryString(query.TrimStart('?'));
        long since = 0;
        if (values["since"] != null && !long.TryParse(values["since"], out since))
            return new(400, new { error = "since must be an integer cursor" });
        var limit = 200;
        if (values["limit"] != null && (!int.TryParse(values["limit"], out limit) || limit < 1 || limit > 1000))
            return new(400, new { error = "limit must be 1..1000" });
        LogLevel? level = null;
        if (values["level"] != null)
        {
            level = LogCapture.ParseLevel(values["level"]);
            if (level == null) return new(400, new { error = "level must be fatal, error, warning, message, info or debug" });
        }
        var entries = LogCapture.Read(since, limit, level, values["source"], values["contains"], out var next, out var oldest, out var more);
        return new(200, new { entries, count = entries.Count, next, oldest, more });
    }

    internal static ApiResult Harmony(string query)
    {
        var values = System.Web.HttpUtility.ParseQueryString(query.TrimStart('?'));
        var owner = values["owner"];
        var target = values["target"];
        var methods = new List<object>();
        foreach (var method in HarmonyLib.Harmony.GetAllPatchedMethods())
        {
            var info = HarmonyLib.Harmony.GetPatchInfo(method);
            if (info == null) continue;
            var name = (method.DeclaringType?.FullName ?? "?") + "." + method.Name;
            if (!string.IsNullOrEmpty(target) && name.IndexOf(target, StringComparison.OrdinalIgnoreCase) < 0) continue;
            object Patches(IEnumerable<Patch> patches) => patches.Select(p => new
            {
                owner = p.owner,
                method = (p.PatchMethod.DeclaringType?.FullName ?? "?") + "." + p.PatchMethod.Name,
                assembly = p.PatchMethod.DeclaringType?.Assembly.GetName().Name,
                priority = p.priority,
                index = p.index
            }).ToArray();
            // CreateAndPatchAll owners are "harmony-auto-<guid>", so the patch assembly name matches too.
            var assemblies = info.Prefixes.Concat(info.Postfixes).Concat(info.Transpilers).Concat(info.Finalizers)
                .Select(p => p.PatchMethod.DeclaringType?.Assembly.GetName().Name).Where(x => x != null).Distinct().ToArray();
            if (!string.IsNullOrEmpty(owner) && !info.Owners.Concat(assemblies).Any(o => o.IndexOf(owner, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
            methods.Add(new
            {
                target = name,
                signature = method.ToString(),
                owners = info.Owners.ToArray(),
                assemblies,
                prefixes = Patches(info.Prefixes),
                postfixes = Patches(info.Postfixes),
                transpilers = Patches(info.Transpilers),
                finalizers = Patches(info.Finalizers),
                // More than one owner on the same method is where mods can interfere with each other.
                shared = info.Owners.Count > 1
            });
        }
        return new(200, new { methods, count = methods.Count });
    }

    /// <summary>Parses a debug/wait body into a condition checked once per frame.</summary>
    internal sealed class Wait
    {
        private readonly string _scene;
        private readonly int? _humanCount;
        private readonly bool? _creatorReady;
        private readonly GameApi.ButtonSelector _button;
        private readonly string _logContains;
        private readonly string _logSource;
        private readonly LogLevel? _logLevel;
        private readonly long _logSince;
        private readonly HashSet<string> _eventTypes;
        private readonly string _eventContains;
        private readonly long _eventSince;
        private object _matchedEvent;
        internal readonly int TimeoutMs;
        internal readonly int StartFrame = UnityEngine.Time.frameCount;
        private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
        private object _matchedLog;

        internal const int MaxTimeoutMs = 120000;

        internal Wait(string body)
        {
            var j = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body).RootElement;
            TimeoutMs = 10000;
            if (j.TryGetProperty("timeoutMs", out var t) && (!t.TryGetInt32(out TimeoutMs) || TimeoutMs < 0 || TimeoutMs > MaxTimeoutMs))
                throw new ArgumentException($"timeoutMs must be 0..{MaxTimeoutMs}");
            if (j.TryGetProperty("scene", out var s)) _scene = s.GetString();
            if (j.TryGetProperty("humanCountAtLeast", out var h))
            {
                if (!h.TryGetInt32(out var n) || n < 0) throw new ArgumentException("humanCountAtLeast must be a non-negative integer");
                _humanCount = n;
            }
            if (j.TryGetProperty("creatorReady", out var c))
            {
                if (c.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new ArgumentException("creatorReady must be a boolean");
                _creatorReady = c.GetBoolean();
            }
            if (j.TryGetProperty("button", out var b)) _button = GameApi.ButtonSelector.Parse(b);
            if (j.TryGetProperty("log", out var l))
            {
                if (l.ValueKind != JsonValueKind.Object) throw new ArgumentException("log must be an object");
                _logContains = l.TryGetProperty("contains", out var lc) ? lc.GetString() : null;
                _logSource = l.TryGetProperty("source", out var ls) ? ls.GetString() : null;
                if (l.TryGetProperty("level", out var ll) && (_logLevel = LogCapture.ParseLevel(ll.GetString())) == null)
                    throw new ArgumentException("log.level must be fatal, error, warning, message, info or debug");
                // Only lines written after the wait started count unless a cursor is given.
                _logSince = l.TryGetProperty("since", out var lsi) ? lsi.GetInt64() : LogCapture.Cursor;
                if (_logContains == null && _logSource == null && _logLevel == null) throw new ArgumentException("log needs contains, source or level");
            }
            if (j.TryGetProperty("event", out var ev))
            {
                if (ev.ValueKind != JsonValueKind.Object) throw new ArgumentException("event must be an object");
                if (ev.TryGetProperty("type", out var et))
                    _eventTypes = (et.ValueKind == JsonValueKind.Array ? et.EnumerateArray().Select(x => x.GetString()) : new[] { et.GetString() }).ToHashSet();
                if (_eventTypes is { Count: 0 }) throw new ArgumentException("event.type must name at least one event type");
                _eventContains = ev.TryGetProperty("contains", out var ec) ? ec.GetString() : null;
                _eventSince = ev.TryGetProperty("since", out var es) ? es.GetInt64() : DebugEvents.Cursor;
                if (_eventTypes == null && _eventContains == null) throw new ArgumentException("event needs type or contains");
            }
            if (_scene == null && _humanCount == null && _creatorReady == null && _button == null && _logContains == null && _logSource == null && _logLevel == null && _eventTypes == null && _eventContains == null)
                throw new ArgumentException("give at least one of scene, humanCountAtLeast, creatorReady, button, log, event");
        }

        internal bool TimedOut => _clock.ElapsedMilliseconds >= TimeoutMs;

        internal bool Satisfied()
        {
            if (_scene != null && SceneManager.GetActiveScene().name != _scene) return false;
            if (_humanCount != null && HumanCount() < _humanCount) return false;
            if (_creatorReady != null && (HumanCustom.Instance?.Human != null) != _creatorReady) return false;
            if (_button != null && _button.Matches().Count == 0) return false;
            if (_logContains != null || _logSource != null || _logLevel != null)
            {
                _matchedLog ??= LogCapture.Read(_logSince, 1, _logLevel, _logSource, _logContains, out _, out _, out _).FirstOrDefault();
                if (_matchedLog == null) return false;
            }
            if (_eventTypes != null || _eventContains != null)
            {
                _matchedEvent ??= DebugEvents.Read(_eventSince, 1, _eventTypes, _eventContains, out _, out _, out _).FirstOrDefault();
                if (_matchedEvent == null) return false;
            }
            return true;
        }

        internal ApiResult Result(bool satisfied) => new(satisfied ? 200 : 408, new
        {
            satisfied,
            elapsedMs = _clock.ElapsedMilliseconds,
            frames = UnityEngine.Time.frameCount - StartFrame,
            state = new { scene = SceneManager.GetActiveScene().name, humanCount = HumanCount(), creatorReady = HumanCustom.Instance?.Human != null },
            log = _matchedLog,
            logCursor = LogCapture.Cursor,
            @event = _matchedEvent,
            eventCursor = DebugEvents.Cursor,
            error = satisfied ? null : "conditions not met before timeoutMs"
        });
    }

    private static int HumanCount()
    {
        var all = Human.List;
        return all == null ? 0 : all.Cast<Il2CppSystem.Collections.Generic.IReadOnlyCollection<Human>>().Count;
    }
}

/// <summary>Keeps the BepInEx log (every source and level) in memory with a sequence cursor.</summary>
internal sealed class LogCapture : ILogListener
{
    private const int Capacity = 5000;
    private static readonly object Sync = new();
    private static readonly LinkedList<Entry> Entries = new();
    private static long _sequence;
    private static LogCapture _instance;

    internal sealed record Entry(long seq, string utc, string level, string source, string message, bool fromDisk);

    internal static long Cursor { get { lock (Sync) return _sequence; } }

    public LogLevel LogLevelFilter => LogLevel.All;

    internal static void Install()
    {
        if (_instance != null) return;
        // Lines written before this plugin loaded (preloader, earlier plugins) come from the disk log.
        try
        {
            var path = Path.Combine(Paths.BepInExRootPath, "LogOutput.log");
            if (File.Exists(path))
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                var header = new Regex(@"^\[(\w+)\s*:\s*(.*?)\] ?(.*)$");
                string line;
                Entry current = null;
                while ((line = reader.ReadLine()) != null)
                {
                    var m = header.Match(line);
                    if (m.Success)
                    {
                        if (current != null) Add(current);
                        current = new Entry(0, null, m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value, true);
                    }
                    else if (current != null) current = current with { message = current.message + "\n" + line };
                }
                if (current != null) Add(current);
            }
        }
        catch { }
        _instance = new LogCapture();
        Logger.Listeners.Add(_instance);
    }

    public void LogEvent(object sender, LogEventArgs eventArgs)
    {
        var entry = Add(new Entry(0, DateTimeOffset.UtcNow.ToString("O"), eventArgs.Level.ToString(), eventArgs.Source?.SourceName ?? "", eventArgs.Data?.ToString() ?? "", false));
        if ((eventArgs.Level & (LogLevel.Fatal | LogLevel.Error | LogLevel.Warning)) != 0)
            DebugEvents.Push("log", new { entry.seq, entry.level, entry.source, message = entry.message.Length > 500 ? entry.message[..500] + "…" : entry.message });
    }

    private static Entry Add(Entry entry)
    {
        lock (Sync)
        {
            entry = entry with { seq = ++_sequence };
            Entries.AddLast(entry);
            while (Entries.Count > Capacity) Entries.RemoveFirst();
            return entry;
        }
    }

    internal static LogLevel? ParseLevel(string name) => name?.ToLowerInvariant() switch
    {
        "fatal" => LogLevel.Fatal,
        "error" => LogLevel.Error,
        "warning" => LogLevel.Warning,
        "message" => LogLevel.Message,
        "info" => LogLevel.Info,
        "debug" => LogLevel.Debug,
        _ => null
    };

    // Includes the given level and everything more severe.
    private static bool AtLeast(string level, LogLevel minimum)
    {
        if (!Enum.TryParse<LogLevel>(level, true, out var value)) return true;
        return value != LogLevel.None && value <= minimum;
    }

    internal static List<Entry> Read(long since, int limit, LogLevel? level, string source, string contains, out long next, out long oldest, out bool more)
    {
        var result = new List<Entry>();
        lock (Sync)
        {
            oldest = Entries.First?.Value.seq ?? _sequence + 1;
            next = since;
            more = false;
            foreach (var entry in Entries)
            {
                if (entry.seq <= since) continue;
                next = entry.seq;
                if (level != null && !AtLeast(entry.level, level.Value)) continue;
                if (!string.IsNullOrEmpty(source) && entry.source.IndexOf(source, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (!string.IsNullOrEmpty(contains) && entry.message.IndexOf(contains, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (result.Count == limit) { more = true; next = result[^1].seq; break; }
                result.Add(entry);
            }
            if (!more) next = _sequence;
        }
        return result;
    }

    public void Dispose() { }
}
