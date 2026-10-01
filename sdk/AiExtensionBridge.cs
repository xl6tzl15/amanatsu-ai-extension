// Copy this file into a BepInEx plugin (or include it with <Compile Include=".../AiExtensionBridge.cs" />)
// to publish endpoints through Amanatsu AI Extension. It needs no reference to Amanatsu.AiExtension.dll,
// works whether AI Extension loads before or after the plugin, and does nothing when it is not installed.
using System;
using System.Collections.Generic;

namespace Amanatsu.AiExtension.Sdk;

internal static class AiExtensionBridge
{
    // Holds a List<object[]> of waiting registrations until AI Extension loads, then its Action<object[]>.
    private const string Slot = "amanatsu.ai-extension.registrations";

    /// <summary>
    /// Publishes one endpoint at /api/v1/ext/{owner}/{path}.
    /// metadataJson: {"method":"GET|POST","path":"...","summary":"...","mutates":bool,"scene":"any|creator"}
    /// plus optional "query" and "body" JSON Schemas, as in api_endpoints.json.
    /// handler(method, query, body) runs on the Unity main thread and returns (HTTP status, JSON response body).
    /// </summary>
    internal static void Register(string owner, string metadataJson, Func<string, string, string, Tuple<int, string>> handler)
    {
        var registration = new object[] { owner, metadataJson, handler };
        var domain = AppDomain.CurrentDomain;
        lock (domain)
        {
            switch (domain.GetData(Slot))
            {
                case Action<object[]> register:
                    register(registration);
                    return;
                case List<object[]> pending:
                    pending.Add(registration);
                    return;
                default:
                    domain.SetData(Slot, new List<object[]> { registration });
                    return;
            }
        }
    }
}
