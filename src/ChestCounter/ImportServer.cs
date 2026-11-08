using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;

namespace ChestCounter;

/// <summary>A teammate's replay that has been read; the main loop merges it into the data.</summary>
public sealed record PendingImport(string SessionId, string? EpicId, string Name, int Chests, TaskCompletionSource<int> Done);

/// <summary>
/// Tiny web server on 127.0.0.1 only (nothing leaves the PC). The report's "Import the replays" button sends the
/// teammate's .replay files here; each one is read as <b>that player's</b> game, which gives their exact chest count.
/// </summary>
public static class ImportServer
{
    private const long MaxBody = 600L * 1024 * 1024;
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static HttpListener? _listener;

    /// <summary>0 when the server could not start (the report then hides the import buttons).</summary>
    public static int Port { get; private set; }

    /// <summary>Random secret of this run: other web pages cannot talk to the server without it.</summary>
    public static string Token { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

    public static ConcurrentQueue<PendingImport> Queue { get; } = new();
    public static Action? OnQueued { get; set; }

    public static void Start()
    {
        for (var port = 47831; port < 47845; port++)
        {
            try
            {
                var l = new HttpListener();
                l.Prefixes.Add($"http://127.0.0.1:{port}/");
                l.Start();
                _listener = l;
                Port = port;
                break;
            }
            catch
            {
                // port busy or not allowed: try the next one
            }
        }
        if (_listener == null)
        {
            return;
        }
        _ = Task.Run(async () =>
        {
            while (_listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await _listener.GetContextAsync(); }
                catch { return; }
                _ = Task.Run(() => HandleAsync(ctx));
            }
        });
    }

    private static async Task Reply(HttpListenerContext ctx, int status, object body)
    {
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(body);
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = "application/json; charset=utf-8";
            ctx.Response.ContentLength64 = bytes.Length;
            await ctx.Response.OutputStream.WriteAsync(bytes);
        }
        catch
        {
            // the page went away
        }
        finally
        {
            try { ctx.Response.Close(); } catch { }
        }
    }

    private static async Task HandleAsync(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        ctx.Response.Headers["Access-Control-Allow-Origin"] = "*";
        ctx.Response.Headers["Access-Control-Allow-Headers"] = "*";
        ctx.Response.Headers["Access-Control-Allow-Methods"] = "POST, OPTIONS";
        if (req.HttpMethod == "OPTIONS")
        {
            await Reply(ctx, 204, new { });
            return;
        }
        if (req.HttpMethod != "POST" || req.Url?.AbsolutePath != "/import")
        {
            await Reply(ctx, 404, new { status = "error", message = "not found" });
            return;
        }
        var q = HttpUtility.ParseQueryString(req.Url.Query);
        if (q["token"] != Token)
        {
            await Reply(ctx, 403, new { status = "token", message = L.T("Page périmée : recharge le rapport (F5) puis réessaie.", "Outdated page: reload the report (F5) and try again.", "Página caducada: recarga el informe (F5) y vuelve a intentarlo.") });
            return;
        }
        var name = (q["name"] ?? "").Trim();
        var epic = string.IsNullOrWhiteSpace(q["epic"]) ? null : q["epic"]!.Trim();
        if (name.Length == 0)
        {
            await Reply(ctx, 400, new { status = "error", message = "name" });
            return;
        }
        if (req.ContentLength64 > MaxBody)
        {
            await Reply(ctx, 413, new { status = "error", message = L.T("Fichier trop gros.", "File too big.", "Archivo demasiado grande.") });
            return;
        }

        await Gate.WaitAsync(); // one replay at a time: keeps memory low
        try
        {
            using var ms = new MemoryStream();
            await req.InputStream.CopyToAsync(ms);
            ms.Position = 0;

            MatchRecord? rec = null;
            Exception? failure = null;
            var worker = new Thread(() =>
            {
                try { rec = new ReplayAnalyzer().Analyze(ms, "import.replay", name); }
                catch (Exception e) { failure = e; }
            }) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "ReplayImport" };
            worker.Start();
            if (!worker.Join(TimeSpan.FromMinutes(3)) || failure != null || rec == null)
            {
                await Reply(ctx, 200, new { status = "error", message = L.T("Replay illisible.", "Unreadable replay.", "Repetición ilegible.") });
                return;
            }

            if (rec.NotTrackedOwner != null)
            {
                await Reply(ctx, 200, new { status = "notfound", message = L.T($"{name} n'est pas dans ce replay.", $"{name} is not in this replay.", $"{name} no está en esta repetición.") });
                return;
            }
            if (!rec.IsReplayOwner)
            {
                await Reply(ctx, 200, new { status = "notowner", message = L.T($"Ce replay n'a pas été enregistré par {name} (ses chiffres ne seraient pas complets).", $"This replay was not recorded by {name} (the figures would not be complete).", $"Esta repetición no la grabó {name} (las cifras no estarían completas).") });
                return;
            }
            if (epic != null && rec.EpicId != null && !string.Equals(epic, rec.EpicId, StringComparison.OrdinalIgnoreCase))
            {
                await Reply(ctx, 200, new { status = "notowner", message = L.T("Ce replay est celui d'un autre joueur.", "This replay belongs to another player.", "Esta repetición es de otro jugador.") });
                return;
            }

            var done = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            Queue.Enqueue(new PendingImport(rec.SessionId, epic ?? rec.EpicId, rec.PlayerName ?? name, rec.ChestsOpened, done));
            OnQueued?.Invoke();
            var updated = await done.Task.WaitAsync(TimeSpan.FromMinutes(5));
            if (updated == 0)
            {
                await Reply(ctx, 200, new { status = "nomatch", message = L.T("Cette partie n'est pas dans tes replays (tu n'y étais pas, ou elle n'est pas encore analysée).", "This match is not among your replays (you were not in it, or it is not analyzed yet).", "Esta partida no está entre tus repeticiones (no estabas, o aún no está analizada).") });
                return;
            }
            await Reply(ctx, 200, new { status = "updated", chests = rec.ChestsOpened, message = "ok" });
        }
        catch (Exception e)
        {
            await Reply(ctx, 200, new { status = "error", message = e.Message });
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>Stores the figure and applies it to the matching match. Returns how many matches were updated (main thread only).</summary>
    public static int Merge(TrackerData data, PendingImport imp)
    {
        data.TeammateImports.RemoveAll(i => i.SessionId == imp.SessionId && Same(i.EpicId, i.Name, imp.EpicId, imp.Name));
        if (!data.Matches.Any(m => m.SessionId == imp.SessionId && m.Team.Any(t => Same(t.EpicId, t.Name, imp.EpicId, imp.Name))))
        {
            return 0;
        }
        data.TeammateImports.Add(new TeammateImport { SessionId = imp.SessionId, EpicId = imp.EpicId, Name = imp.Name, Chests = imp.Chests });
        return Apply(data);
    }

    private static bool Same(string? epicA, string nameA, string? epicB, string nameB) =>
        !string.IsNullOrEmpty(epicA) && !string.IsNullOrEmpty(epicB)
            ? string.Equals(epicA, epicB, StringComparison.OrdinalIgnoreCase)
            : string.Equals(nameA, nameB, StringComparison.OrdinalIgnoreCase);

    /// <summary>Idempotent: sets the exact figures on the teammates' entries (also after a re-analysis of the user's replays).</summary>
    public static int Apply(TrackerData data)
    {
        var n = 0;
        foreach (var imp in data.TeammateImports)
        {
            foreach (var m in data.Matches.Where(m => m.SessionId == imp.SessionId))
            {
                var t = m.Team.FirstOrDefault(t => Same(t.EpicId, t.Name, imp.EpicId, imp.Name));
                if (t == null) continue;
                t.Chests = imp.Chests;
                t.Imported = true;
                n++;
            }
        }
        return n;
    }
}
