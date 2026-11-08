using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace ChestCounter;

public static class Pretty
{
    public static string Playlist(string? playlist)
    {
        if (string.IsNullOrEmpty(playlist))
        {
            return L.T("mode inconnu", "unknown mode", "modo desconocido");
        }

        var p = playlist.ToLowerInvariant();
        var parts = new List<string>();
        if (p.Contains("habanero") || p.Contains("ranked")) parts.Add(L.T("Classée", "Ranked", "Clasificatoria"));
        if (p.Contains("nobuild")) parts.Add(L.T("Zéro construction", "Zero Build", "Cero construcción"));
        if (p.Contains("solo")) parts.Add(L.T("Solo", "Solo", "Solitario"));
        else if (p.Contains("duo")) parts.Add(L.T("Duo", "Duos", "Dúos"));
        else if (p.Contains("trio")) parts.Add(L.T("Trio", "Trios", "Tríos"));
        else if (p.Contains("squad")) parts.Add(L.T("Section", "Squads", "Escuadrones"));
        return parts.Count > 0 ? string.Join(" · ", parts) : playlist.Replace("Playlist_", "");
    }

    public static string Placement(MatchRecord m)
    {
        if (m.Placement is not int place || place <= 0)
        {
            return L.T("place inconnue", "unknown placement", "puesto desconocido");
        }
        if (place == 1)
        {
            return L.T("Top 1 !", "Victory Royale!", "¡Victoria magistral!");
        }
        return m.TotalPlayers is int total && total > 0 ? $"{L.Ordinal(place)} / {total}" : L.Ordinal(place);
    }

    public static string Clock(float seconds)
    {
        var s = (int)seconds;
        return $"{s / 60}:{s % 60:00}";
    }

    /// <summary>"à 7:16 · Coffre rare" (certain) or "vers 5:04 · Coffre rare (probable)".</summary>
    public static string Obtained(TreasureFind f)
    {
        if (f.ObtainedAtSeconds is not float t)
        {
            return L.T("moment et source inconnus", "time and source unknown", "momento y origen desconocidos");
        }
        var when = f.TimeExact ? $"{L.T("à", "at", "a los")} {Clock(t)}" : $"{L.T("vers", "around", "hacia los")} {Clock(t)}";
        return f.SourceCertain ? $"{when} · {f.SourceLabel}" : $"{when} · {f.SourceLabel} ({Likely})";
    }

    public static string Likely => L.T("probable", "likely", "probable");

    public static string ItemName(string item)
    {
        var name = item;
        foreach (var prefix in new[] { "WID_", "Athena", "AmmoData" })
        {
            name = name.Replace(prefix, "");
        }
        return name.Replace("_", " ").Trim();
    }
}

/// <summary>Replays analyzed so far / to analyze, shown as a loading card in the report.</summary>
public sealed record ScanProgress(int Done, int Total);

public static class ReportWriter
{
    public static readonly DateTime SessionStartUtc = DateTime.UtcNow;

    /// <summary>Set while several replays are being analyzed: the report shows a loading card and refreshes faster.</summary>
    public static volatile ScanProgress? Progress;

    /// <summary>A match belongs to this session if it ended after the tool was started (backlog replays do not count).</summary>
    public static bool IsThisSession(MatchRecord m)
    {
        var start = m.MatchStartUtc ?? m.AnalyzedAtUtc;
        if (start.Kind == DateTimeKind.Unspecified)
        {
            start = DateTime.SpecifyKind(start, DateTimeKind.Utc);
        }
        var end = start.ToUniversalTime().AddSeconds(m.DurationSeconds ?? 0);
        return end >= SessionStartUtc.AddMinutes(-2);
    }
    private static CultureInfo Fr => L.Culture;

    public static void WriteAll(Store store, Config config, TrackerData data)
    {
        try
        {
            ImportServer.Apply(data);
            var view = Profiles.View(data, config);
            WriteAtomic(store.ReportPath, Report(config, view, Profiles.List(data)));
            WriteAtomic(store.OverlayPath, Overlay(OverlayModel.Build(view, null, false)));
            WriteAtomic(store.CsvPath, Csv(data));
        }
        catch (Exception ex)
        {
            Console.WriteLine(L.T($"Impossible d'écrire le rapport : {ex.Message}", $"Cannot write the report: {ex.Message}", $"No se puede escribir el informe: {ex.Message}"));
        }
    }

    private static void WriteAtomic(string path, string content)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, content, new UTF8Encoding(false));
        File.Move(tmp, path, overwrite: true);
    }

    private static string H(string? s) => WebUtility.HtmlEncode(s ?? "");

    private static DateTime When(MatchRecord m) => (m.MatchStartUtc ?? m.AnalyzedAtUtc).ToLocalTime();

    private const string Style = """
        :root{--bg:#f6f5f2;--card:#fff;--ink:#1d1d1f;--muted:#6b6b70;--line:#e4e2dd;--accent:#b7791f;--accent-soft:#fbf1dc;--good:#2f7d4f;--star:#8a3ffc}
        @media (prefers-color-scheme:dark){:root:not([data-theme="light"]){--bg:#121214;--card:#1b1b1f;--ink:#ededf0;--muted:#9a9aa2;--line:#2c2c32;--accent:#e9b04f;--accent-soft:#33280f;--good:#5cc28a;--star:#b48cff}}
        :root[data-theme="dark"]{--bg:#121214;--card:#1b1b1f;--ink:#ededf0;--muted:#9a9aa2;--line:#2c2c32;--accent:#e9b04f;--accent-soft:#33280f;--good:#5cc28a;--star:#b48cff}
        *{box-sizing:border-box}
        body{margin:0;background:var(--bg);color:var(--ink);font:15px/1.5 "Segoe UI",system-ui,-apple-system,sans-serif}
        main{max-width:1040px;margin:0 auto;padding:28px 16px 48px}
        header{display:flex;flex-wrap:wrap;justify-content:space-between;align-items:baseline;gap:8px;margin-bottom:22px}
        h1{font-size:26px;margin:0;letter-spacing:-.01em}
        h2{font-size:15px;text-transform:uppercase;letter-spacing:.06em;color:var(--muted);margin:30px 0 10px}
        .muted{color:var(--muted)}
        .tiles{display:grid;grid-template-columns:repeat(auto-fit,minmax(150px,1fr));gap:12px}
        .tile{background:var(--card);border:1px solid var(--line);border-radius:12px;padding:14px 16px}
        .tile .v{font-size:30px;font-weight:700;font-variant-numeric:tabular-nums;line-height:1.2}
        .tile.main{background:var(--accent-soft);border-color:transparent}
        .tile.main .v{font-size:40px;color:var(--accent)}
        .tile .l{color:var(--muted);font-size:13px}
        .card{background:var(--card);border:1px solid var(--line);border-radius:12px;padding:14px 16px;overflow-x:auto}
        table{width:100%;border-collapse:collapse;font-variant-numeric:tabular-nums}
        th,td{text-align:left;padding:8px 10px;border-bottom:1px solid var(--line);vertical-align:top}
        th{font-size:12px;text-transform:uppercase;letter-spacing:.05em;color:var(--muted);font-weight:600}
        tr:last-child td{border-bottom:none}
        td.n{text-align:right;font-weight:600}
        td.nw{white-space:nowrap}
        .items{color:var(--muted);font-size:13px}
        .star{color:var(--star);font-weight:600}
        .flag{margin-top:16px;border-color:var(--accent);background:var(--accent-soft)}
        tr.unusual td{background:var(--accent-soft)}
        .card.chart{overflow:visible}
        .bars{display:flex;align-items:stretch;gap:4px;height:140px;padding-top:28px}
        .col{flex:1;min-width:6px;display:flex;align-items:flex-end;position:relative;cursor:default}
        .bar{width:100%;background:var(--accent);border-radius:3px 3px 0 0;transition:filter .12s}
        .bar.zero{background:var(--line);height:3px!important}
        .col:hover .bar{filter:brightness(1.25)}
        .col:hover .bar.zero{background:var(--muted)}
        .tip{position:absolute;left:50%;transform:translateX(-50%);white-space:nowrap;background:var(--ink);color:var(--bg);
             padding:3px 8px;border-radius:6px;font-size:12px;font-weight:600;opacity:0;pointer-events:none;transition:opacity .12s;z-index:2}
        .col:nth-child(-n+3) .tip{left:0;transform:none}
        .col:nth-last-child(-n+3) .tip{left:auto;right:0;transform:none}
        .col:hover .tip{opacity:1}
        .axis{display:flex;justify-content:space-between;color:var(--muted);font-size:12px;margin-top:6px}
        footer{margin-top:28px;color:var(--muted);font-size:13px}
        .loading{display:flex;gap:16px;align-items:center;background:var(--card);border:1px solid var(--accent);border-radius:12px;padding:16px 18px;margin-bottom:20px}
        .spin{width:30px;height:30px;flex:none;border:3px solid var(--line);border-top-color:var(--accent);border-radius:50%;animation:spin .8s linear infinite}
        @keyframes spin{to{transform:rotate(360deg)}}
        .loading .grow{flex:1;min-width:0}
        .pbar{height:8px;background:var(--line);border-radius:4px;overflow:hidden;margin:8px 0 6px}
        .pbar i{display:block;height:100%;background:var(--accent)}
        .profiles{margin:-12px 0 20px;font-size:13px}
        """;

    /// <summary>Auto refresh that can be paused (while teammate replays are being imported, a reload would cancel the import).</summary>
    private static string RefreshScript(int seconds) =>
        "<script>window.__busy=false;setTimeout(function(){if(!window.__busy)location.reload();}," + seconds * 1000 + ");</script>";

    public static string Report(Config config, TrackerData data, IReadOnlyList<Profile> profiles)
    {
        var progress = Progress;
        var matches = data.Matches.OrderByDescending(When).ToList();
        var totalChests = matches.Sum(m => m.ChestsOpened);
        var avg = matches.Count > 0 ? (double)totalChests / matches.Count : 0;
        var best = matches.Count > 0 ? matches.Max(m => m.ChestsOpened) : 0;
        var treasures = matches.Sum(m => m.TreasureFinds.Count);
        var session = matches.Where(IsThisSession).Sum(m => m.ChestsOpened);
        static int TeamOf(MatchRecord m) => m.ChestsOpened + m.Team.Sum(t => t.Chests);
        var teamTotal = matches.Sum(TeamOf);
        var teamAvg = matches.Count > 0 ? (double)teamTotal / matches.Count : 0;
        var teamBest = matches.Count > 0 ? matches.Max(TeamOf) : 0;
        var teamSession = matches.Where(IsThisSession).Sum(TeamOf);
        var player = matches.Select(m => m.PlayerName).FirstOrDefault(n => !string.IsNullOrEmpty(n)) ?? "";

        var sb = new StringBuilder();
        var title = L.T("Compteur de coffres", "Chest Counter", "Contador de cofres");
        var updated = L.T(
            $"Mis à jour le {DateTime.Now.ToString("dd/MM/yyyy à HH:mm:ss", Fr)} · actualisation auto",
            $"Updated {DateTime.Now.ToString("d MMM yyyy 'at' HH:mm:ss", Fr)} · auto refresh",
            $"Actualizado el {DateTime.Now.ToString("dd/MM/yyyy 'a las' HH:mm:ss", Fr)} · actualización automática");
        sb.Append($"""
            <!doctype html><html lang="{L.Code}"><head><meta charset="utf-8">
            <meta name="viewport" content="width=device-width,initial-scale=1">
            {RefreshScript(progress != null ? 2 : 15)}
            <title>{title}</title><style>{Style}</style></head><body><main>
            <header><h1>{title}{(player.Length > 0 ? " · " + H(player) : "")}</h1>
            <span class="muted">{updated}</span></header>
            {LoadingCard(progress)}{ProfilesLine(profiles, player)}
            <section class="tiles">
              <div class="tile main"><div class="v" id="v-total" data-s="{totalChests.ToString("N0", Fr)}" data-t="{teamTotal.ToString("N0", Fr)}">{totalChests.ToString("N0", Fr)}</div><div class="l" id="l-total" data-s="{L.T("coffres ouverts au total", "chests opened in total", "cofres abiertos en total")}" data-t="{L.T("coffres de l'équipe au total (au moins)", "team chests in total (at least)", "cofres del equipo en total (al menos)")}">{L.T("coffres ouverts au total", "chests opened in total", "cofres abiertos en total")}</div></div>
              <div class="tile"><div class="v" id="v-n">{matches.Count.ToString("N0", Fr)}</div><div class="l">{L.T("parties analysées", "matches analyzed", "partidas analizadas")}</div></div>
              <div class="tile"><div class="v" id="v-avg" data-s="{avg.ToString("0.0", Fr)}" data-t="{teamAvg.ToString("0.0", Fr)}">{avg.ToString("0.0", Fr)}</div><div class="l" id="l-avg" data-s="{L.T("coffres par partie", "chests per match", "cofres por partida")}" data-t="{L.T("coffres d'équipe par partie", "team chests per match", "cofres de equipo por partida")}">{L.T("coffres par partie", "chests per match", "cofres por partida")}</div></div>
              <div class="tile"><div class="v" id="v-best" data-s="{best}" data-t="{teamBest}">{best}</div><div class="l" id="l-best" data-s="{L.T("record en une partie", "best match", "récord en una partida")}" data-t="{L.T("record d'équipe en une partie", "team best match", "récord de equipo en una partida")}">{L.T("record en une partie", "best match", "récord en una partida")}</div></div>
              <div class="tile"><div class="v" id="v-ses" data-s="{session}" data-t="{teamSession}">{session}</div><div class="l" id="l-ses" data-s="{L.T("coffres depuis le lancement de l'outil", "chests since the tool was started", "cofres desde que se abrió la herramienta")}" data-t="{L.T("coffres d'équipe depuis le lancement", "team chests since the tool was started", "cofres de equipo desde que se abrió la herramienta")}">{L.T("coffres depuis le lancement de l'outil", "chests since the tool was started", "cofres desde que se abrió la herramienta")}</div></div>
              <div class="tile"><div class="v" id="v-tre">{treasures}</div><div class="l">{L.T("trésors repérés", "treasures found", "tesoros encontrados")}</div></div>
            </section>
            """);

        var chip = "font:inherit;font-weight:600;padding:6px 14px;border-radius:999px;border:1px solid var(--line);background:var(--card);color:var(--ink);cursor:pointer";
        sb.Append($"<div id=\"filters\" style=\"margin-top:18px;display:flex;flex-wrap:wrap;gap:8px;align-items:center\"><span class=\"muted\">{L.T("Parties prises en compte :", "Matches included:", "Partidas incluidas:")}</span>");
        foreach (var (n, label) in new[] { (1, L.T("Dernière partie", "Last match", "Última partida")), (3, "3"), (5, "5"), (10, "10"), (20, "20"), (50, "50"), (0, L.T("Toutes", "All", "Todas")) })
        {
            sb.Append($"<button type=\"button\" class=\"chip\" data-n=\"{n}\" style=\"{chip}\">{label}</button>");
        }
        sb.Append("</div>");
        var teamParts = TeamSection(matches).Split("<!--TEAMSPLIT-->");
        sb.Append(teamParts[0]);

        // Last 30 days, one bar per day.
        var today = DateTime.Now.Date;
        var days = Enumerable.Range(0, 30).Select(i => today.AddDays(i - 29)).ToList();
        var perDay = days.Select(d => matches.Where(m => When(m).Date == d).Sum(m => m.ChestsOpened)).ToList();
        var perDayTeam = days.Select(d => matches.Where(m => When(m).Date == d).Sum(TeamOf)).ToList();
        var max = Math.Max(1, perDay.Max());
        var maxT = Math.Max(1, perDayTeam.Max());
        sb.Append($"<h2>{L.T("Coffres par jour (30 derniers jours)", "Chests per day (last 30 days)", "Cofres por día (últimos 30 días)")}</h2><div class=\"card chart\"><div class=\"bars\">");
        for (var i = 0; i < days.Count; i++)
        {
            var v = perDay[i];
            var h = (int)Math.Round(100.0 * v / max);
            var vt = perDayTeam[i];
            var ht = (int)Math.Round(100.0 * vt / maxT);
            var dayLabel = $"{days[i].ToString(L.T("ddd dd/MM", "ddd d MMM", "ddd dd/MM"), Fr)}{L.T(" : ", ": ", ": ")}";
            var tip = dayLabel + L.Count(v, "coffre", "chest", "cofre");
            var tipT = dayLabel + L.Count(vt, "coffre", "chest", "cofre");
            // Tooltip sits just above the bar top. data-* = solo / team values, swapped by the team button.
            sb.Append($"<div class=\"col\" aria-label=\"{H(tip)}\" data-h=\"{h}\" data-ht=\"{ht}\" data-z=\"{(v == 0 ? 1 : 0)}\" data-zt=\"{(vt == 0 ? 1 : 0)}\" data-tip=\"{H(tip)}\" data-tipt=\"{H(tipT)}\"><span class=\"tip\" style=\"bottom:calc({Math.Max(h, 3)}% + 6px)\">{H(tip)}</span><div class=\"bar{(v == 0 ? " zero" : "")}\" style=\"height:{h}%\"></div></div>");
        }
        sb.Append($"</div><div class=\"axis\"><span>{days[0].ToString(L.T("dd/MM", "d MMM", "dd/MM"), Fr)}</span><span>{L.T("aujourd'hui", "today", "hoy")}</span></div></div>");

        if (teamParts.Length > 1)
        {
            sb.Append(teamParts[1]);
        }

        // Breakdown by chest type, then the other interactions (not in the total).
        var byKind = matches.SelectMany(m => m.Chests).GroupBy(c => c.Kind).OrderByDescending(g => g.Count()).ToList();
        var otherKinds = matches
            .SelectMany(m => m.OtherInteractions.Count > 0
                ? m.OtherInteractions
                : new Dictionary<string, int> { [ChestKinds.AmmoBox] = m.AmmoBoxesOpened, [ChestKinds.OtherContainer] = m.OtherContainersOpened })
            .GroupBy(kv => kv.Key)
            .Select(g => (Kind: g.Key, Count: g.Sum(kv => kv.Value)))
            .Where(x => x.Count > 0)
            .OrderByDescending(x => x.Count)
            .ToList();
        sb.Append($"<h2>{L.T("Par type", "By type", "Por tipo")}</h2><div class=\"card\"><table><tr><th>{L.T("Type", "Type", "Tipo")}</th><th style=\"text-align:right\">{L.T("Ouverts", "Opened", "Abiertos")}</th></tr>");
        foreach (var g in byKind)
        {
            sb.Append($"<tr><td>{H(L.Name(g.Key))}</td><td class=\"n\">{g.Count()}</td></tr>");
        }
        var destroyedTotal = matches.Sum(m => m.Chests.Count(c => c.Destroyed));
        if (destroyedTotal > 0)
        {
            sb.Append($"<tr><td class=\"muted\">{L.T("↳ dont coffres détruits (cassés au lieu d'être ouverts)", "↳ of which destroyed chests (broken instead of opened)", "↳ de ellos, cofres destruidos (rotos en lugar de abiertos)")}</td><td class=\"n muted\">{destroyedTotal}</td></tr>");
        }
        foreach (var (kind, count) in otherKinds)
        {
            sb.Append($"<tr><td class=\"muted\">{H(L.Name(kind))} {L.T("(hors total des coffres)", "(not in the chest total)", "(fuera del total de cofres)")}</td><td class=\"n muted\">{count}</td></tr>");
        }
        sb.Append("</table></div>");

        // Treasures.
        var treasureRows = matches.SelectMany(m => m.TreasureFinds.Select(f => (m, f))).ToList();
        if (treasureRows.Count > 0)
        {
            var unusual = treasureRows.Where(r => !r.f.SourceIsChest).ToList();
            if (unusual.Count > 0)
            {
                var kinds = H(string.Join(", ", unusual.Select(r => L.Name(r.f.SourceKind)).Distinct()));
                var count = L.Count(unusual.Count, "trésor obtenu", "trésors obtenus", "treasure", "treasures", "tesoro obtenido", "tesoros obtenidos");
                sb.Append(L.T(
                    $"<div class=\"card flag\">⚑ <b>{count} autrement qu'avec un coffre</b> : {kinds}. Détail dans le tableau ci-dessous.</div>",
                    $"<div class=\"card flag\">⚑ <b>{count} obtained other than from a chest</b>: {kinds}. Details in the table below.</div>",
                    $"<div class=\"card flag\">⚑ <b>{count} de otra forma que con un cofre</b>: {kinds}. Detalles en la tabla de abajo.</div>"));
            }

            sb.Append($"<h2>{L.T("Trésors obtenus", "Treasures obtained", "Tesoros obtenidos")}</h2><div class=\"card\"><table><tr><th>{L.T("Partie", "Match", "Partida")}</th><th>{L.T("Trésor", "Treasure", "Tesoro")}</th><th>{L.T("Obtenu à", "Obtained at", "Momento")}</th><th>{L.T("Source", "Source", "Origen")}</th></tr>");
            foreach (var (m, f) in treasureRows)
            {
                var id = f.PrizeId != null ? $" <span class=\"muted\">({H(f.PrizeId)})</span>" : (f.RawItem.StartsWith("WID_") ? $" <span class=\"muted\">({H(f.RawItem)})</span>" : "");
                var when = f.ObtainedAtSeconds is float t ? (f.TimeExact ? Pretty.Clock(t) : $"{L.T("vers", "around", "hacia los")} {Pretty.Clock(t)}") : "<span class=\"muted\">?</span>";
                var source = $"{(f.SourceIsChest ? "" : "⚑ ")}{H(f.SourceLabel)}"
                    + (f.SourceCertain ? "" : $" <span class=\"muted\">({Pretty.Likely})</span>")
                    + (!f.SourceIsChest && !string.IsNullOrEmpty(f.SourceBlueprint) ? $"<br><span class=\"muted\">{H(f.SourceBlueprint)}</span>" : "")
                    + (f.AlsoPossible.Count > 0 ? $"<br><span class=\"muted\">{L.T("aussi possible : ", "also possible: ", "también posible: ")}{H(string.Join(", ", f.AlsoPossible.Select(L.Name)))}</span>" : "");
                sb.Append($"<tr{(f.SourceIsChest ? "" : " class=\"unusual\"")}><td class=\"nw\">{L.DateTimeShort(When(m))}</td><td class=\"star\">{(f.FromGoldenChest ? "★★" : "★")} {H(f.DisplayName)}{id}</td><td class=\"nw\">{when}</td><td>{source}</td></tr>");
            }
            sb.Append("</table></div>");

            // Which kinds of containers / interactions have produced treasures so far.
            var bySource = treasureRows.GroupBy(r => r.f.SourceLabel).OrderByDescending(g => g.Count()).ToList();
            sb.Append($"<h2>{L.T("D'où viennent les trésors", "Where the treasures come from", "De dónde vienen los tesoros")}</h2><div class=\"card\"><table><tr><th>{L.T("Source", "Source", "Origen")}</th><th style=\"text-align:right\">{L.T("Trésors", "Treasures", "Tesoros")}</th><th style=\"text-align:right\">{L.T("dont certains", "of which certain", "de ellos, seguros")}</th></tr>");
            foreach (var g in bySource)
            {
                var isChest = g.First().f.SourceIsChest;
                sb.Append($"<tr{(isChest ? "" : " class=\"unusual\"")}><td>{(isChest ? "" : "⚑ ")}{H(g.Key)}</td><td class=\"n\">{g.Count()}</td><td class=\"n\">{g.Count(r => r.f.SourceCertain)}</td></tr>");
            }
            sb.Append("</table></div>");
        }

        // Matches: chest contents are only shown when a treasure came out.
        sb.Append($"<h2>{L.T("Dernières parties", "Latest matches", "Últimas partidas")}</h2><div class=\"card\"><table><tr><th>{L.T("Date", "Date", "Fecha")}</th><th>{L.T("Mode", "Mode", "Modo")}</th><th>{L.T("Place", "Placement", "Puesto")}</th><th style=\"text-align:right\">{L.T("Coffres", "Chests", "Cofres")}</th><th>{L.T("Trésor", "Treasure", "Tesoro")}</th></tr>");
        foreach (var m in matches.Take(100))
        {
            var mi = matches.IndexOf(m);
            var treasure = string.Join("<br>", m.TreasureFinds.Select(f => $"<span class=\"star\">{(f.FromGoldenChest ? "★★" : "★")} {H(f.DisplayName)}</span>"));
            sb.Append($"<tr class=\"mrow\" data-i=\"{mi}\"><td class=\"nw\">{L.DateTimeShort(When(m))}</td><td>{H(Pretty.Playlist(m.Playlist))}</td><td>{H(Pretty.Placement(m))}</td><td class=\"n\">{m.ChestsOpened}</td><td class=\"items\">{treasure}</td></tr>");
        }
        sb.Append("</table></div>");

        sb.Append(ControlsScript(matches, days));

        sb.Append(L.T(
            """
            <footer>Compte les coffres que tu as ouverts ou détruits toi-même, d'après tes fichiers replay (analysés après chaque partie).
            Les caisses de munitions et de nourriture ne sont pas comptées. ★★ = coffre doré de la chasse au trésor (cosmétique, accès boutique…),
            détecté de façon certaine. ★ = trésor reçu grâce à un de tes coffres (moment exact), nommé quand l'objet est identifiable.
            Les noms des récompenses se modifient dans treasures.json.</footer>
            </main></body></html>
            """,
            """
            <footer>Counts the chests you opened or destroyed yourself, from your replay files (analyzed after each match).
            Ammo boxes and food boxes are not counted. ★★ = golden treasure hunt chest (cosmetic, shop access…), detected for sure.
            ★ = treasure received from one of your own chests (exact moment), named when the item can be identified.
            Prize names can be edited in treasures.json.</footer>
            </main></body></html>
            """,
            """
            <footer>Cuenta los cofres que has abierto o destruido tú mismo, a partir de tus archivos de repetición (analizados después de cada partida).
            Las cajas de munición y de comida no se cuentan. ★★ = cofre dorado de la búsqueda del tesoro (cosmético, acceso a la tienda…),
            detectado con seguridad. ★ = tesoro recibido de uno de tus cofres (momento exacto), con nombre cuando el objeto se puede identificar.
            Los nombres de las recompensas se pueden editar en treasures.json.</footer>
            </main></body></html>
            """));
        return sb.ToString();
    }

    /// <summary>"Team" button + hidden section: chests of the teammates (a minimum: the replay only holds what the recorder's game saw).</summary>
    private static string TeamSection(List<MatchRecord> matches)
    {
        var withTeam = matches.Where(m => m.Team.Count > 0).ToList();
        if (withTeam.Count == 0)
        {
            return "";
        }
        var mine = withTeam.Sum(m => m.ChestsOpened);
        var mates = withTeam.Sum(m => m.Team.Sum(t => t.Chests));
        var canImport = ImportServer.Port != 0;
        var sb = new StringBuilder();
        sb.Append($"""
            <div style="margin-top:18px"><button id="teambtn" type="button" style="font:inherit;font-weight:600;padding:8px 16px;border-radius:10px;border:1px solid var(--accent);background:var(--accent-soft);color:var(--accent);cursor:pointer">{L.T("👥 Voir l'équipe", "👥 Show the team", "👥 Ver el equipo")}</button></div>
            <!--TEAMSPLIT-->
            <section id="team" style="display:none">
            <div class="card flag">{L.T(
                "⚠ <b>Attention : les chiffres de tes coéquipiers peuvent être plus bas que la réalité.</b><br>Le replay est l'enregistrement de <b>ton</b> jeu : il ne montre que ce qui se passe autour de toi. Si un coéquipier est loin de toi quand il ouvre ou casse un coffre, ton jeu ne reçoit pas l'information et ce coffre n'est pas compté. Ses vrais chiffres sont donc <b>au moins</b> ceux affichés. Les tiens, eux, sont complets.",
                "⚠ <b>Careful: your teammates' numbers may be lower than reality.</b><br>The replay is a recording of <b>your</b> game: it only shows what happens around you. If a teammate is far from you when they open or break a chest, your game never receives that information and the chest is not counted. Their real numbers are therefore <b>at least</b> those shown. Yours are complete.",
                "⚠ <b>Atención: las cifras de tus compañeros pueden ser más bajas que la realidad.</b><br>La repetición es la grabación de <b>tu</b> juego: solo muestra lo que pasa a tu alrededor. Si un compañero está lejos de ti cuando abre o rompe un cofre, tu juego no recibe esa información y ese cofre no se cuenta. Sus cifras reales son por tanto <b>al menos</b> las mostradas. Las tuyas están completas.")}</div>
            <h2>{L.T("Par coéquipier", "By teammate", "Por compañero")}</h2><div class="card"><table><tr><th>{L.T("Joueur", "Player", "Jugador")}</th><th style="text-align:right">{L.T("Parties", "Matches", "Partidas")}</th><th style="text-align:right">{L.T("Coffres (au moins)", "Chests (at least)", "Cofres (al menos)")}</th>{(canImport ? $"<th>{L.T("Replays", "Replays", "Repeticiones")}</th>" : "")}</tr>
            """);
        var rows = withTeam.SelectMany(m => m.Team).GroupBy(t => string.IsNullOrEmpty(t.EpicId) ? t.Name : t.EpicId)
            .Select(g => (Name: g.Last().Name, Epic: g.Last().EpicId ?? "", Matches: g.Count(), Chests: g.Sum(t => t.Chests), Exact: g.Count(t => t.Imported), Me: false)).ToList();
        var myName = withTeam.Select(m => m.PlayerName).FirstOrDefault(n => !string.IsNullOrEmpty(n)) ?? "?";
        rows.Add((myName, "", withTeam.Count, mine, withTeam.Count, true));
        foreach (var r in rows.OrderByDescending(r => r.Chests))
        {
            var tag = r.Me ? $" <span style=\"font-size:.75em;font-weight:700;padding:2px 8px;border-radius:999px;background:var(--accent-soft);color:var(--accent)\">{L.T("vous", "you", "tú")}</span>" : "";
            var check = r.Exact == r.Matches && !r.Me ? " <span style=\"color:var(--accent)\" title=\"" + L.T("Chiffre exact", "Exact figure", "Cifra exacta") + "\">✓</span>" : "";
            var cell = "";
            if (canImport)
            {
                var state = r.Exact > 0 ? L.T($"{r.Exact}/{r.Matches} parties exactes", $"{r.Exact}/{r.Matches} exact matches", $"{r.Exact}/{r.Matches} partidas exactas") : "";
                cell = r.Me ? "<td></td>" :
                    $"<td><button type=\"button\" class=\"impbtn\" data-name=\"{H(r.Name)}\" data-epic=\"{H(r.Epic)}\" style=\"font:inherit;font-size:13px;font-weight:600;padding:4px 10px;border-radius:8px;border:1px solid var(--accent);background:var(--accent-soft);color:var(--accent);cursor:pointer\">{L.T("Importer les replays", "Import replays", "Importar repeticiones")}</button> <span class=\"impst muted\" style=\"font-size:12px\">{state}</span></td>";
            }
            sb.Append($"<tr><td>{H(r.Name)}{tag}</td><td class=\"n\">{r.Matches}</td><td class=\"n\">{r.Chests}{check}</td>{cell}</tr>");
        }
        sb.Append($"</table></div><h2>{L.T("Par partie", "By match", "Por partida")}</h2><div class=\"card\"><table><tr><th>{L.T("Date", "Date", "Fecha")}</th><th style=\"text-align:right\">{L.T("Toi", "You", "Tú")}</th><th>{L.T("Coéquipiers", "Teammates", "Compañeros")}</th><th style=\"text-align:right\">{L.T("Équipe", "Team", "Equipo")}</th></tr>");
        foreach (var m in withTeam.Take(100))
        {
            var detail = string.Join(", ", m.Team.OrderByDescending(t => t.Chests).Select(t => $"{H(t.Name)} <b>{t.Chests}</b>{(t.Imported ? " <span style=\"color:var(--accent)\">✓</span>" : "")}"));
            sb.Append($"<tr class=\"mrow\" data-i=\"{matches.IndexOf(m)}\"><td class=\"nw\">{L.DateTimeShort(When(m))}</td><td class=\"n\">{m.ChestsOpened}</td><td>{detail}</td><td class=\"n\">{m.ChestsOpened + m.Team.Sum(t => t.Chests)}</td></tr>");
        }
        sb.Append("</table></div>");
        if (canImport)
        {
            sb.Append("<p class=\"muted\" style=\"font-size:13px\">" + L.T(
                "✓ = chiffre exact, lu dans le replay du joueur lui-même. Pour l'obtenir : demande à ton coéquipier ses fichiers .replay (dossier %LOCALAPPDATA%\\FortniteGame\\Saved\\Demos), puis clique sur « Importer les replays » à côté de son nom et choisis-les. Les parties que vous n'avez pas jouées ensemble sont ignorées.",
                "✓ = exact figure, read from the player's own replay. To get it: ask your teammate for their .replay files (folder %LOCALAPPDATA%\\FortniteGame\\Saved\\Demos), then click \"Import replays\" next to their name and pick them. Matches you did not play together are ignored.",
                "✓ = cifra exacta, leída de la repetición del propio jugador. Para obtenerla: pídele a tu compañero sus archivos .replay (carpeta %LOCALAPPDATA%\\FortniteGame\\Saved\\Demos), luego pulsa «Importar repeticiones» junto a su nombre y elígelos. Las partidas que no jugasteis juntos se ignoran.")
                + "</p><input type=\"file\" id=\"impfile\" accept=\".replay\" multiple hidden>");
        }
        sb.Append("</section>");
        if (canImport)
        {
            var texts = JsonSerializer.Serialize(new
            {
                progress = L.T("Lecture {a}/{b}…", "Reading {a}/{b}…", "Leyendo {a}/{b}…"),
                done = L.T("{a} partie(s) importée(s), {b} ignorée(s)", "{a} match(es) imported, {b} skipped", "{a} partida(s) importada(s), {b} ignorada(s)"),
                noserver = L.T("Le programme ne répond pas : ouvre-le puis recharge la page.", "The program is not answering: open it and reload the page.", "El programa no responde: ábrelo y recarga la página."),
            }, new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            sb.Append("<script>(function(){var IMP={port:" + ImportServer.Port + ",token:\"" + ImportServer.Token + "\",t:" + texts + "};" + ImportJs + "})();</script>");
        }
        return sb.ToString();
    }

    private const string ImportJs = """
        var inp=document.getElementById('impfile');if(!inp)return;var cur=null;
        document.querySelectorAll('.impbtn').forEach(function(b){b.addEventListener('click',function(){cur=b;window.__busy=true;inp.value='';inp.click();});});
        inp.addEventListener('cancel',function(){window.__busy=false;});
        inp.addEventListener('change',async function(){
          var files=[].slice.call(inp.files),btn=cur;
          if(!btn||!files.length){window.__busy=false;return;}
          var st=btn.parentNode.querySelector('.impst'),ok=0,skip=0,last='';
          btn.disabled=true;
          for(var i=0;i<files.length;i++){
            st.textContent=IMP.t.progress.replace('{a}',i+1).replace('{b}',files.length);
            try{
              var r=await fetch('http://127.0.0.1:'+IMP.port+'/import?token='+IMP.token+'&name='+encodeURIComponent(btn.dataset.name)+'&epic='+encodeURIComponent(btn.dataset.epic||''),{method:'POST',body:files[i]});
              var j=await r.json();
              if(j.status==='updated'){ok++;}else{skip++;last=j.message||'';if(j.status==='token')break;}
            }catch(e){skip++;last=IMP.t.noserver;break;}
          }
          st.textContent=IMP.t.done.replace('{a}',ok).replace('{b}',skip)+(last?' · '+last:'');
          btn.disabled=false;
          setTimeout(function(){window.__busy=false;if(ok>0)location.reload();},ok>0?2500:20000);
        });
        """;

    /// <summary>Client-side controls: "match filter" chips + solo/team switch. Recomputes tiles, chart and match tables from embedded per-match numbers.</summary>
    private static string ControlsScript(List<MatchRecord> matches, List<DateTime> days)
    {
        static int TeamOf(MatchRecord m) => m.ChestsOpened + m.Team.Sum(t => t.Chests);
        string Unit(int n) => L.Count(n, "coffre", "chest", "cofre").Split(' ', 2)[1];
        var cfg = new
        {
            lang = L.Code,
            d = matches.Select(m => new { k = When(m).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), c = m.ChestsOpened, t = TeamOf(m), s = IsThisSession(m) ? 1 : 0, r = m.TreasureFinds.Count }),
            days = days.Select(d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            dl = days.Select(d => d.ToString(L.T("ddd dd/MM", "ddd d MMM", "ddd dd/MM"), Fr)),
            sep = L.T(" : ", ": ", ": "),
            uz = Unit(0),
            uo = Unit(1),
            um = Unit(2),
            one = L.T("dernière partie", "last match", "última partida"),
            many = L.T("dernières parties", "last matches", "últimas partidas"),
            hide = L.T("👥 Masquer l'équipe", "👥 Hide the team", "👥 Ocultar el equipo"),
            show = L.T("👥 Voir l'équipe", "👥 Show the team", "👥 Ver el equipo"),
        };
        var json = JsonSerializer.Serialize(cfg, new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }).Replace("</", "<\\/");
        return """
            <script>
            (function(){
            var C=__CFG__;
            var b=document.getElementById('teambtn'),sec=document.getElementById('team');
            var team=false,n=0;
            try{team=localStorage.getItem('chestcounter-team')==='1'}catch(e){}
            try{n=parseInt(localStorage.getItem('chestcounter-n')||'0',10)||0}catch(e){}
            function fmt(x,d){return x.toLocaleString(C.lang,{minimumFractionDigits:d,maximumFractionDigits:d})}
            function unit(v){return v===0?C.uz:(v===1?C.uo:C.um)}
            function setText(id,t){var e=document.getElementById(id);if(e)e.textContent=t}
            function render(){
              var all=n<=0||n>=C.d.length,list=all?C.d:C.d.slice(0,n);
              var val=function(x){return team?x.t:x.c};
              var tot=0,best=0,ses=0,tre=0;
              list.forEach(function(x){var v=val(x);tot+=v;if(v>best)best=v;ses+=team?x.t*x.s:x.c*x.s;tre+=x.r});
              setText('v-total',fmt(tot,0));setText('v-n',fmt(list.length,0));setText('v-avg',fmt(list.length?tot/list.length:0,1));
              setText('v-best',String(best));setText('v-ses',String(ses));setText('v-tre',String(tre));
              var suf=all?'':' \u00b7 '+(n===1?C.one:n+' '+C.many);
              ['l-total','l-avg','l-best','l-ses'].forEach(function(id){var e=document.getElementById(id);if(e)e.textContent=e.getAttribute(team?'data-t':'data-s')+suf});
              var per=C.days.map(function(k){var s=0;list.forEach(function(x){if(x.k===k)s+=val(x)});return s});
              var mx=Math.max.apply(null,per.concat([1]));
              document.querySelectorAll('.bars .col').forEach(function(c,i){
                var v=per[i],h=Math.round(100*v/mx),bar=c.querySelector('.bar'),tip=c.querySelector('.tip'),t=C.dl[i]+C.sep+v+' '+unit(v);
                bar.style.height=h+'%';bar.classList.toggle('zero',v===0);tip.textContent=t;tip.style.bottom='calc('+Math.max(h,3)+'% + 6px)';c.setAttribute('aria-label',t);
              });
              document.querySelectorAll('tr.mrow').forEach(function(r){r.style.display=(all||parseInt(r.getAttribute('data-i'),10)<n)?'':'none'});
              document.querySelectorAll('#filters .chip').forEach(function(c){
                var on=parseInt(c.getAttribute('data-n'),10)===(n>0?n:0);
                c.style.background=on?'var(--accent-soft)':'var(--card)';c.style.color=on?'var(--accent)':'var(--ink)';c.style.borderColor=on?'var(--accent)':'var(--line)';
              });
              if(b){b.textContent=team?C.hide:C.show;b.setAttribute('data-open',team?'1':'0')}
              if(sec)sec.style.display=team?'block':'none';
            }
            function go(){
              document.querySelectorAll('#filters .chip').forEach(function(c){c.addEventListener('click',function(){n=parseInt(c.getAttribute('data-n'),10)||0;try{localStorage.setItem('chestcounter-n',String(n))}catch(e){}render()})});
              if(b)b.addEventListener('click',function(){team=!team;try{localStorage.setItem('chestcounter-team',team?'1':'0')}catch(e){}render()});
              render();
            }
            if(document.readyState==='loading'){document.addEventListener('DOMContentLoaded',go)}else{go()}
            })();
            </script>
            """.Replace("__CFG__", json);
    }

    private static string LoadingCard(ScanProgress? p)
    {
        if (p == null)
        {
            return "";
        }
        var pct = p.Total > 0 ? (int)Math.Round(100.0 * p.Done / p.Total) : 0;
        return L.T(
            $"""<div class="loading"><div class="spin"></div><div class="grow"><b>Analyse de tes replays… {p.Done} / {p.Total}</b><div class="pbar"><i style="width:{pct}%"></i></div><span class="muted">Cette page se met à jour toute seule : les chiffres ci-dessous augmentent au fil de la lecture des replays.</span></div></div>""",
            $"""<div class="loading"><div class="spin"></div><div class="grow"><b>Analyzing your replays… {p.Done} / {p.Total}</b><div class="pbar"><i style="width:{pct}%"></i></div><span class="muted">This page updates by itself: the numbers below grow as the replays are read.</span></div></div>""",
            $"""<div class="loading"><div class="spin"></div><div class="grow"><b>Analizando tus repeticiones… {p.Done} / {p.Total}</b><div class="pbar"><i style="width:{pct}%"></i></div><span class="muted">Esta página se actualiza sola: las cifras de abajo aumentan a medida que se leen las repeticiones.</span></div></div>""");
    }

    private static string ProfilesLine(IReadOnlyList<Profile> profiles, string player)
    {
        var others = profiles.Where(p => p.Name != player).Select(p => H(p.Name)).ToList();
        if (others.Count == 0)
        {
            return "";
        }
        return $"""<div class="profiles muted">{L.T("Autres profils sur ce PC : ", "Other profiles on this PC: ", "Otros perfiles en este PC: ")}{string.Join(", ", others)} · {L.T("touche P dans l'outil pour changer de profil", "press P in the tool to switch profile", "tecla P en la herramienta para cambiar de perfil")}</div>""";
    }

    /// <summary>Rewrites only the OBS overlay page (called on every live update).</summary>

    public static void WriteOverlay(Store store, OverlayView view)
    {
        try
        {
            WriteAtomic(store.OverlayPath, Overlay(view));
        }
        catch
        {
            // the page is a convenience: never stop the tracker for it
        }
    }

    public static string Overlay(OverlayView v)
    {
        var rows = new StringBuilder();
        foreach (var r in v.Rows)
        {
            rows.Append($"<div class=\"row{(r.Treasure ? " t" : "")}\"><span>{H(r.Label)}</span><b>{H(r.Value)}{(r.Detail != null ? $" <small>{H(r.Detail)}</small>" : "")}</b></div>");
        }
        return $$"""
            <!doctype html><html lang="{{L.Code}}"><head><meta charset="utf-8"><meta http-equiv="refresh" content="10">
            <title>{{L.T("Overlay coffres", "Chest overlay", "Overlay de cofres")}}</title>
            <style>
            html,body{margin:0;background:transparent;color:#e2e2e8;font:500 16px/1.3 "Segoe UI",system-ui,sans-serif}
            .box{display:inline-block;min-width:260px;padding:14px 18px 12px;border-radius:16px;background:rgba(18,18,22,.86);border:1px solid #3a3a42;text-shadow:0 1px 3px rgba(0,0,0,.6)}
            .head{display:flex;justify-content:space-between;gap:16px;font:700 12px "Segoe UI",sans-serif;letter-spacing:.08em;color:#9696a2}
            .live{color:#60d680}
            .counters{display:flex;align-items:stretch;gap:18px;margin:4px 0 10px}
            .counters>div{display:flex;flex-direction:column}
            .sep{width:1px;background:#2c2c34;margin:8px 0 2px}
            .big{font-size:40px;font-weight:800;line-height:1.1;font-variant-numeric:tabular-nums}
            .total{color:#f2b84b}.cur{color:#6ee096}.none{color:#50505c}
            .cap{font-size:11px;font-weight:600;color:#82828e}
            .rows{border-top:1px solid #2c2c34;padding-top:8px;display:grid;grid-template-columns:auto 1fr;gap:4px 12px}
            .row{display:contents}.row span{font-size:13px;font-weight:600;color:#82828e}.row b{font-weight:500}
            .row.t b{color:#c4a0ff}.row small{font-size:11px;font-weight:600;color:#82828e}
            </style></head><body><div class="box">
            <div class="head"><span>{{L.T("COFFRES", "CHESTS", "COFRES")}}</span>{{(v.Badge != null ? $"<span class=\"live\">{H(v.Badge)}</span>" : "")}}</div>
            <div class="counters">
              <div><span class="big total">{{H(v.Total)}}</span><span class="cap">total</span></div>
              <div class="sep"></div>
              <div><span class="big {{(v.Current == null ? "none" : v.CurrentLive ? "cur" : "")}}">{{H(v.Current ?? "—")}}</span><span class="cap">{{H(v.CurrentCaption)}}</span></div>
            </div>
            <div class="rows">{{rows}}</div>
            </div></body></html>
            """;
    }

    public static string Csv(TrackerData data)
    {
        var sb = new StringBuilder();
        sb.AppendLine(L.T("date;mode;joueur;place;joueurs;coffres;caisses_munitions;tresors;fichier", "date;mode;player;placement;players;chests;ammo_boxes;treasures;file", "fecha;modo;jugador;puesto;jugadores;cofres;cajas_municion;tesoros;archivo"));

        foreach (var m in data.Matches.OrderBy(When))
        {
            var t = string.Join(" + ", m.TreasureFinds.Select(f => f.DisplayName));
            sb.AppendLine(string.Join(";",
                When(m).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                Pretty.Playlist(m.Playlist), m.PlayerName, m.Placement, m.TotalPlayers,
                m.ChestsOpened, m.AmmoBoxesOpened, t, m.ReplayFile));
        }
        return sb.ToString();
    }
}
