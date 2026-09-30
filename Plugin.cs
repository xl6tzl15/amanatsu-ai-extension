using System;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using UnityEngine;

namespace Amanatsu.AiExtension;

[BepInPlugin("amanatsu.ai-extension", "Amanatsu AI Extension", "0.9.0")]
[BepInDependency("amanatsu.unlockall", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("amanatsu.selfshadowtoggle", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("amanatsu.favorabilitycontrol", BepInDependency.DependencyFlags.SoftDependency)]
public sealed class Plugin : BasePlugin
{
    public override void Load()
    {
        var port = Config.Bind("API", "Port", 38427, "Loopback HTTP port for the AI extension.");
        AiBridgeBehaviour.LogSource = Log;
        OperationLog.Initialize(Log);
        AddComponent<AiBridgeBehaviour>();
        ApiHost.Start(port.Value, Log);
    }
}

public sealed class AiBridgeBehaviour : MonoBehaviour
{
    internal static BepInEx.Logging.ManualLogSource LogSource;
    private static float _nextObservation;

    public AiBridgeBehaviour(IntPtr pointer) : base(pointer) { }

    private void Update()
    {
        if (Time.realtimeSinceStartup >= _nextObservation)
        {
            _nextObservation = Time.realtimeSinceStartup + 0.5f;
            OperationLog.ObserveGameState();
        }
        for (var i = 0; i < 16 && ApiHost.Pending.TryDequeue(out var action); i++)
        {
            try { action(); }
            catch (Exception ex) { LogSource?.LogError(ex); }
        }
    }

    private void OnDestroy()
    {
        OperationLog.Record("session_end", new { });
        ApiHost.Stop();
    }
}
