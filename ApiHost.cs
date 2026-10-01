using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Logging;

namespace Amanatsu.AiExtension;

internal static class ApiHost
{
    internal static readonly ConcurrentQueue<Action> Pending = new();
    // Checked once per frame by AiBridgeBehaviour; returns true when finished.
    internal static readonly System.Collections.Generic.List<Func<bool>> PerFrame = new();
    private static HttpListener _listener;
    private static string _token;
    private static ManualLogSource _log;
    private static CancellationTokenSource _stop;
    // A whole-character import is several hundred operations.
    private const int MaxBody = 4 * 1024 * 1024;
    private static bool _capturePending;

    internal static void Start(int port, ManualLogSource log)
    {
        if (port < 1024 || port > 65535)
            throw new ArgumentOutOfRangeException(nameof(port));

        _log = log;
        var tokenPath = Path.Combine(Paths.ConfigPath, "amanatsu.ai-extension.token");
        _token = File.Exists(tokenPath) ? File.ReadAllText(tokenPath).Trim() : "";
        if (_token.Length < 32)
        {
            _token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            File.WriteAllText(tokenPath, _token + Environment.NewLine);
        }

        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        _listener.Start();
        _stop = new CancellationTokenSource();
        _ = AcceptLoop(_stop.Token);
        log.LogInfo($"AI API listening on http://127.0.0.1:{port}/; token file: {tokenPath}");
    }

    internal static void Stop()
    {
        _stop?.Cancel();
        _listener?.Close();
    }

    private static async Task AcceptLoop(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            HttpListenerContext context;
            try { context = await _listener.GetContextAsync(); }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException) { break; }
            _ = Handle(context);
        }
    }

    private static async Task Handle(HttpListenerContext context)
    {
        try
        {
            var request = context.Request;
            if (request.RemoteEndPoint == null || !IPAddress.IsLoopback(request.RemoteEndPoint.Address) ||
                !string.IsNullOrEmpty(request.Headers["Origin"]))
            {
                await Send(context, new ApiResult(403, new { error = "loopback CLI requests only" }));
                return;
            }

            var auth = request.Headers["Authorization"];
            if (auth == null || !auth.StartsWith("Bearer ", StringComparison.Ordinal) || !ValidToken(auth[7..]))
            {
                await Send(context, new ApiResult(401, new { error = "bearer token required" }));
                return;
            }

            if (request.HttpMethod != "GET" && request.HttpMethod != "POST")
            {
                await Send(context, new ApiResult(405, new { error = "GET or POST required" }));
                return;
            }

            if (request.ContentLength64 > MaxBody)
            {
                await Send(context, new ApiResult(413, new { error = "request too large" }));
                return;
            }

            string body = "";
            if (request.HttpMethod == "POST")
            {
                using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
                body = await reader.ReadToEndAsync();
                if (body.Length > MaxBody)
                {
                    await Send(context, new ApiResult(413, new { error = "request too large" }));
                    return;
                }
            }

            var completion = new TaskCompletionSource<ApiResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var method = request.HttpMethod;
            var path = request.Url.AbsolutePath;
            var query = request.Url.Query;
            if (method == "GET" && path == "/api/v1/debug/types")
            {
                // Managed reflection only, so it runs here instead of waiting for the main thread.
                ApiResult types;
                try { types = DebugInspect.Types(query); }
                catch (ArgumentException ex) { types = new ApiResult(400, new { error = ex.Message }); }
                await Send(context, types);
                return;
            }
            var timeout = TimeSpan.FromSeconds(10);
            Pending.Enqueue(() =>
            {
                if (completion.Task.IsCompleted)
                    return;
                var before = OperationLog.Before();
                var started = System.Diagnostics.Stopwatch.StartNew();
                void Finish(ApiResult value) {
                    started.Stop();
                    OperationLog.Request(method,path,body,value,started.Elapsed.TotalMilliseconds,before);
                    if(method=="POST") DebugEvents.Push("api",new{method,path,status=value.Status,durationMs=Math.Round(started.Elapsed.TotalMilliseconds,1)});
                    completion.TrySetResult(value);
                }
                if(method=="POST" && _capturePending && path!="/api/v1/debug/wait") {Finish(new ApiResult(409,new{error="capture in progress; retry after it completes"}));return;}
                ApiResult result;
                if(method=="POST" && path=="/api/v1/capture") {
                    // Expression and pose are applied first; framing waits until a new pose has settled
                    // because it measures the skeleton.
                    try { CaptureApi.BeginCapture(body); }
                    catch(NotInCreatorException ex){Finish(new ApiResult(409,new{error=ex.Message}));return;}
                    catch(ArgumentException ex){Finish(new ApiResult(400,new{error=ex.Message}));return;}
                    catch(Exception ex){Finish(new ApiResult(500,new{error=ex.Message}));return;}
                    _capturePending=true;
                    var frameAt=UnityEngine.Time.frameCount+(CaptureApi.PoseRequested?30:0);
                    var shootAt=int.MaxValue;
                    ApiResult framing=default;
                    Action capture=null;
                    capture=()=>{
                        if(completion.Task.IsCompleted){CaptureApi.EndCapture();_capturePending=false;return;}
                        if(shootAt==int.MaxValue){
                            if(UnityEngine.Time.frameCount<frameAt){Pending.Enqueue(capture);return;}
                            try{framing=GameApi.Execute("POST","/api/v1/camera/frame",query,body);}
                            catch(Exception ex){CaptureApi.EndCapture();_capturePending=false;Finish(new ApiResult(500,new{error=ex.Message}));return;}
                            if(framing.Status!=200){CaptureApi.EndCapture();_capturePending=false;Finish(framing);return;}
                            shootAt=UnityEngine.Time.frameCount+3;Pending.Enqueue(capture);return;
                        }
                        if(UnityEngine.Time.frameCount<shootAt){Pending.Enqueue(capture);return;}
                        try {var shot=GameApi.Execute("GET","/api/v1/screenshot","","");Finish(new ApiResult(shot.Status,new{framing=framing.Body,image=shot.Body}));}
                        catch(Exception ex){Finish(new ApiResult(500,new{error=ex.Message}));}
                        finally{CaptureApi.EndCapture();_capturePending=false;}
                    };
                    Pending.Enqueue(capture);return;
                }
                if(method=="POST" && path=="/api/v1/debug/wait") {
                    // Checked once per frame without blocking the main thread; see DebugApi.Wait.
                    DebugApi.Wait wait;
                    try { wait=new DebugApi.Wait(body); }
                    catch(Exception ex) when (ex is ArgumentException or System.Text.Json.JsonException or InvalidOperationException){Finish(new ApiResult(400,new{error=ex.Message}));return;}
                    PerFrame.Add(()=>{
                        if(completion.Task.IsCompleted)return true;
                        bool satisfied;
                        try{satisfied=wait.Satisfied();}
                        catch(Exception ex){Finish(new ApiResult(500,new{error=ex.Message}));return true;}
                        if(!satisfied&&!wait.TimedOut)return false;
                        Finish(wait.Result(satisfied));return true;
                    });
                    return;
                }
                try { result = GameApi.Execute(method, path, query, body); }
                catch (Exception ex)
                {
                    _log?.LogError(ex);
                    result = new ApiResult(500, new { error = ex.Message });
                }
                Finish(result);
            });

            if (method == "POST" && path == "/api/v1/debug/wait")
                timeout = TimeSpan.FromMilliseconds(DebugApi.Wait.MaxTimeoutMs + 10000);
            ApiResult result;
            try { result = await completion.Task.WaitAsync(timeout); }
            catch (TimeoutException)
            {
                completion.TrySetCanceled();
                result = new ApiResult(503, new { error = "game main thread did not respond" });
            }
            await Send(context, result);
        }
        catch (Exception ex)
        {
            _log?.LogError(ex);
            try { await Send(context, new ApiResult(500, new { error = "API request failed" })); }
            catch { }
        }
    }

    private static bool ValidToken(string supplied)
    {
        var a = Encoding.UTF8.GetBytes(supplied);
        var b = Encoding.UTF8.GetBytes(_token);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }

    private static async Task Send(HttpListenerContext context, ApiResult result)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(result.Body);
        context.Response.StatusCode = result.Status;
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes);
        context.Response.Close();
    }
}

internal readonly record struct ApiResult(int Status, object Body);
