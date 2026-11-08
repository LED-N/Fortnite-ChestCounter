namespace ChestCounter;

/// <summary>Size and opacity choices of the overlay window (percent), offered in the settings.</summary>
public static class OverlayAppearance
{
    public static readonly int[] Sizes = { 70, 80, 90, 100, 115, 130, 150, 175 };

    /// <summary>Below 30 % the numbers can no longer be read.</summary>
    public static readonly int[] Opacities = { 100, 90, 80, 70, 60, 50, 40, 30 };

    public static int ClampSize(int percent) => Math.Clamp(percent, Sizes[0], Sizes[^1]);
    public static int ClampOpacity(int percent) => Math.Clamp(percent, Opacities[^1], 100);

    /// <summary>Next value of the list, back to the first one after the last (or for a value edited by hand).</summary>
    public static int Next(int[] values, int current) => values[(Array.IndexOf(values, current) + 1) % values.Length];
}

/// <summary>Builds what the overlays (window and OBS page) show from the saved matches and the match in progress.</summary>
public static class OverlayModel
{
    /// <summary>Caption of the right counter between two matches (also used to keep the overlay width stable).</summary>
    public static string LastMatchCaption => L.T("dernière partie", "last match", "última partida", "前の試合");

    /// <param name="live">Figures of the match in progress, when the replay could be read (null otherwise).</param>
    /// <param name="inProgress">A match is being recorded (even if its figures are not readable yet).</param>
    public static OverlayView Build(TrackerData d, MatchRecord? live, bool inProgress)
    {
        var liveChests = live?.ChestsOpened ?? 0;
        var liveTreasures = live?.TreasureFinds.Count ?? 0;

        var total = d.Matches.Sum(m => m.ChestsOpened) + liveChests;
        var session = d.Matches.Where(ReportWriter.IsThisSession).ToList();
        var rows = new List<OverlayRow>();

        // Session: chests, then small grey "n parties · moy. x" (finished matches of this session).
        string? sessionDetail = null;
        if (session.Count > 0)
        {
            var avg = (double)session.Sum(m => m.ChestsOpened) / session.Count;
            var avgText = Math.Round(avg, MidpointRounding.AwayFromZero).ToString("0", L.Culture);
            sessionDetail = $"{L.Count(session.Count, "partie", "parties", "match", "matches", "partida", "partidas", "試合")} · {L.T("moy.", "avg.", "media", "平均")} {avgText}";
        }
        rows.Add(new OverlayRow(L.T("Session", "Session", "Sesión", "今回の起動"), L.Count(session.Sum(m => m.ChestsOpened) + liveChests, "coffre", "chest", "cofre", "宝箱"), Detail: sessionDetail));

        var last = d.Matches.OrderByDescending(m => m.MatchStartUtc ?? m.AnalyzedAtUtc).FirstOrDefault();
        if (last != null)
        {
            rows.Add(new OverlayRow(L.T("Dernière", "Last", "Última", "前の試合"), L.Count(last.ChestsOpened, "coffre", "chest", "cofre", "宝箱")));
        }

        var treasures = d.Matches.Sum(m => m.TreasureFinds.Count) + liveTreasures;
        rows.Add(new OverlayRow(L.T("Trésors", "Treasures", "Tesoros", "お宝"), $"★ {L.N(treasures)}", Treasure: true));

        // Right counter: the match in progress when readable, "…" while it cannot be read yet, else the last match.
        if (live != null)
        {
            return new OverlayView(L.N(total), L.N(live.ChestsOpened), L.T("partie en cours", "current match", "partida actual", "現在の試合"), true, L.T("● EN DIRECT", "● LIVE", "● EN DIRECTO", "● ライブ"), rows);
        }
        if (inProgress)
        {
            return new OverlayView(L.N(total), "…", L.T("en cours", "in progress", "en curso", "試合中"), true, L.T("● PARTIE EN COURS", "● MATCH IN PROGRESS", "● PARTIDA EN CURSO", "● 試合中"), rows);
        }
        return new OverlayView(L.N(total), last != null ? L.N(last.ChestsOpened) : null, LastMatchCaption, false, null, rows);
    }
}
