using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using ChestCounter;

// --standby: the invisible process that waits for Fortnite to start ("open with Fortnite"). No console, nothing else.
if (args.Contains("--standby"))
{
    var dataIndex = Array.IndexOf(args, "--data");
    return OperatingSystem.IsWindows()
        ? FortniteWatch.RunStandby(dataIndex >= 0 && dataIndex + 1 < args.Length ? args[dataIndex + 1] : Store.DefaultFolder())
        : 1;
}

// The counter is a window (graphical interface). The command line options write into the terminal they come from.
var gui = !args.Any(a => a is "--once" or "--help" or "-h" or "--overlay-preview");
if (OperatingSystem.IsWindows() && !gui)
{
    NativeConsole.Ensure(attachToParent: true);
}
try { Console.OutputEncoding = Encoding.UTF8; } catch { /* no console (graphical interface) */ }

// A crash must not close the window before the message can be read (the exe is usually double-clicked).
AppDomain.CurrentDomain.UnhandledException += (_, e) =>
{
    ErrorLog.Write("Crash", e.ExceptionObject);
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine();
    Console.WriteLine(L.T("Erreur inattendue : ", "Unexpected error: ", "Error inesperado: ") + e.ExceptionObject);
    Console.ResetColor();
    if (!Console.IsInputRedirected)
    {
        Console.WriteLine(L.T("Appuie sur une touche pour fermer.", "Press any key to close.", "Pulsa una tecla para cerrar."));
        try { Console.ReadKey(intercept: true); } catch { }
    }
};

// ---- Arguments ---------------------------------------------------------------------------
string? dataDir = null, demosArg = null, langArg = null;
var once = false;
var reset = false;
var auto = false;          // opened by the standby when Fortnite started: closes itself after Fortnite
var afterUpdate = false;   // restarted by the updater: waits for the previous instance to quit
string? previewPath = null;
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--data": dataDir = args[++i]; break;
        case "--demos": demosArg = args[++i]; break;
        case "--lang": langArg = args[++i]; break;
        case "--once": once = true; break;
        case "--reset-config": reset = true; break;
        case "--overlay-preview": previewPath = args[++i]; break;
        case "--auto": auto = true; break;
        case "--after-update": afterUpdate = true; break;
        case "--gui": case "--console": break; // shortcuts of earlier versions: the window is the only interface now
        case "-h":
        case "--help":
            var langIndex = Array.IndexOf(args, "--lang");
            L.Set(langIndex >= 0 && langIndex + 1 < args.Length ? L.Normalize(args[langIndex + 1]) ?? L.SystemDefault : L.SystemDefault);
            Console.WriteLine(L.T("""
                Compteur de coffres Fortnite

                Options :
                  --lang <fr|en|es>   langue de l'outil (français, anglais ou espagnol)
                  --demos <dossier>   dossier des replays (par défaut %LOCALAPPDATA%\FortniteGame\Saved\Demos)
                  --data <dossier>    dossier des données (par défaut %APPDATA%\ChestCounter)
                  --once              analyse les replays terminés puis quitte
                  --reset-config      repose les questions de configuration
                  --overlay-preview <fichier.png>   enregistre des aperçus de l'overlay puis quitte
                """, """
                Fortnite Chest Counter

                Options:
                  --lang <fr|en|es>   language of the tool (French, English or Spanish)
                  --demos <folder>    replays folder (default %LOCALAPPDATA%\FortniteGame\Saved\Demos)
                  --data <folder>     data folder (default %APPDATA%\ChestCounter)
                  --once              analyze the finished replays, then quit
                  --reset-config      ask the setup questions again
                  --overlay-preview <file.png>   save previews of the overlay, then quit
                """, """
                Contador de cofres de Fortnite

                Opciones:
                  --lang <fr|en|es>   idioma de la herramienta (francés, inglés o español)
                  --demos <carpeta>   carpeta de repeticiones (por defecto %LOCALAPPDATA%\FortniteGame\Saved\Demos)
                  --data <carpeta>    carpeta de datos (por defecto %APPDATA%\ChestCounter)
                  --once              analiza las repeticiones terminadas y sale
                  --reset-config      vuelve a hacer las preguntas de configuración
                  --overlay-preview <archivo.png>   guarda vistas previas del overlay y sale
                """));
            return 0;
    }
}

dataDir ??= Store.DefaultFolder();
var store = new Store(dataDir);
ErrorLog.FilePath = store.ErrorLogPath;

// One instance at a time: two would count the same matches and fight over the shortcuts.
using var instance = new Mutex(false, AppInfo.InstanceMutexFor(dataDir));
if (previewPath == null && !AcquireInstance(instance, afterUpdate ? TimeSpan.FromSeconds(20) : TimeSpan.Zero))
{
    if (auto)
    {
        return 0; // opened with Fortnite while the player had already opened it: nothing to do
    }
    // Started again by the player: the counter already open comes to the front instead (versions from 1.5 on).
    if (OperatingSystem.IsWindows() && EventWaitHandle.TryOpenExisting(AppInfo.ShowEventFor(dataDir), out var show))
    {
        NativeConsole.AllowOthersToTakeForeground();
        show.Set();
        show.Dispose();
        return 0;
    }
    // An older version is open (it cannot be brought to the front): say where it is.
    L.Set(store.LoadConfig()?.Language ?? L.SystemDefault);
    var alreadyOpen = L.T("Le compteur de coffres est déjà ouvert : regarde dans la barre des tâches.",
        "The chest counter is already open: look in the taskbar.",
        "El contador de cofres ya está abierto: búscalo en la barra de tareas.");
    if (gui && OperatingSystem.IsWindows())
    {
        System.Windows.Forms.MessageBox.Show(alreadyOpen, "Chest Counter", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Information);
    }
    else
    {
        Console.WriteLine(alreadyOpen);
    }
    return 0;
}

// ---- Configuration -----------------------------------------------------------------------
var config = reset ? null : store.LoadConfig();
var firstRun = config == null;
var previousLanguage = reset ? store.LoadConfig()?.Language : null;
config ??= new Config();

// Language: --lang, else the saved choice, else the language of Windows (changed in the settings).
if (langArg != null)
{
    config.Language = L.Normalize(langArg) ?? config.Language;
}
if (L.Normalize(config.Language) == null)
{
    config.Language = L.Normalize(previousLanguage) ?? L.SystemDefault;
}
L.Set(config.Language);
config.ApplyTimeSettings();
var appName = L.T("Compteur de coffres Fortnite", "Fortnite Chest Counter", "Contador de cofres de Fortnite");

if (demosArg != null) config.DemosFolder = demosArg;
var defaultDemos = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FortniteGame", "Saved", "Demos");
if (string.IsNullOrWhiteSpace(config.DemosFolder))
{
    config.DemosFolder = defaultDemos;
}

if (previewPath != null)
{
    // Development: draws the overlay off-screen and saves it as PNG (idle + match in progress), then quits.
    var previewData = Profiles.View(store.LoadData(), config);
    // In memory only: the 3 latest matches count as "this session", to show a realistic session line.
    foreach (var m in previewData.Matches.OrderByDescending(m => m.MatchStartUtc ?? m.AnalyzedAtUtc).Take(3))
    {
        m.MatchStartUtc = DateTime.UtcNow;
    }
    if (!OperatingSystem.IsWindows())
    {
        return 1;
    }
    NativeOverlay.SetAppearance(config.OverlaySize, config.OverlayOpacity);
    // Locked, as in game. Each style in its three states: idle, match in progress, live count.
    NativeOverlay.Start(-20000, -20000, true, (_, _) => { }, _ => { });
    var fakeLive = new MatchRecord { ChestsOpened = 12, TreasureFinds = { new TreasureFind { Item = "Trésor" } } };
    foreach (var style in NativeOverlay.Styles)
    {
        NativeOverlay.SetStyle(style);
        foreach (var (suffix, live, inProgress) in new[] { ("-idle", (MatchRecord?)null, false), ("-inprogress", null, true), ("-live", fakeLive, true) })
        {
            NativeOverlay.Update(OverlayModel.Build(previewData, live, inProgress, config.ShowSquadTotal));
            Thread.Sleep(600);
            var name = Path.GetFileNameWithoutExtension(previewPath) + (style == "classic" ? "" : "-" + style) + suffix + ".png";
            var file = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(previewPath)) ?? ".", name);
            Console.WriteLine(NativeOverlay.Snapshot(file) ? L.T($"Aperçu : {file}", $"Preview: {file}", $"Vista previa: {file}") : L.T($"Échec de la capture : {file}", $"Capture failed: {file}", $"Error de captura: {file}"));
        }
    }
    return 0;
}

LowerPriority();
var data = store.LoadData();
var tracker = new Tracker(store, config, data);
// Before any scan: otherwise replays analyzed during the setup would be analyzed a second time right after.
var reanalyzing = tracker.UpgradeIfNeeded();

store.SaveConfig(config);

// Treasure hunt data (content.json: weeks, prize ids, odds, Treasure Hours, notices): the newest copy kept in the data
// folder, until the one on GitHub is read below. Pictures of the treasure collection: local copies (downloaded once).
TreasureCatalog.LoadCached(store.Folder);
TreasureImages.Folder = Path.Combine(store.Folder, "images");

// ---- Command line: analyze once and quit ---------------------------------------------------
if (once)
{
    Console.WriteLine($"{appName} {AppInfo.VersionText}");
    TreasureCatalog.RefreshAsync(store.Folder).Wait(TimeSpan.FromSeconds(20));
    if (TreasureImages.Missing())
    {
        TreasureImages.DownloadMissingAsync().Wait(TimeSpan.FromSeconds(30));
    }
    tracker.Scan(ignoreQuietPeriod: true);
    ReportWriter.WriteAll(store, config, data);
    tracker.PrintSummary();
    Console.WriteLine(L.T($"  Rapport : {store.ReportPath}", $"  Report: {store.ReportPath}", $"  Informe: {store.ReportPath}"));
    return 0;
}

// ---- Main loop -----------------------------------------------------------------------------
// The window shows the state of the counter (GuiState) and sends the player's choices (GuiAction), run here.
// What the counter prints goes to the "Activity" of the home tab (captured from Console.Out).
var quit = false;
var restart = false;
var replaysHintShown = false;     // "no replay recorded" said once
DateTime? fortniteSeenAt = null;  // when Fortnite was first seen running this session
var startedAt = DateTime.UtcNow;
var uninstalled = false;      // "uninstall": a script deletes the exe once the counter has quit
var wake = new AutoResetEvent(false);
var log = new ActivityLog();
UpdateInfo? update = null;
var updateAnnounced = false;
var showWhatsNew = !firstRun && config.LastVersion != AppInfo.VersionText;
// Coming from the console versions (before 1.5): their "minimize to the notification area" was on by default and is
// saved in every config. The window starts with the taskbar, like a new install; the setting is in Settings.
// Done once: the version is saved now (what's new stays on the home tab for this session).
if (!firstRun && (!Version.TryParse(config.LastVersion, out var lastVersion) || lastVersion < new Version(1, 5)))
{
    config.MinimizeToTray = false;
    config.LastVersion = AppInfo.VersionText;
    store.SaveConfig(config);
}
if (!showWhatsNew && config.LastVersion != AppInfo.VersionText)
{
    config.LastVersion = AppInfo.VersionText;
    store.SaveConfig(config);
}
DateTime? autoCloseAt = null;
DateTime? fortniteClosedSince = null;
FileSystemWatcher? watcher = null;
var watcherFired = false;
var nextScan = DateTime.UtcNow.AddSeconds(2);

Console.SetOut(log); // what the counter prints goes to the "Activity" of the window

if (reanalyzing)
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine(L.T("Nouvelle version de l'analyse : les replays encore présents vont être réanalysés (sans doublon).",
        "New analysis version: the replays still on disk will be analyzed again (no duplicates).",
        "Nueva versión del análisis: las repeticiones del disco se analizarán de nuevo (sin duplicados)."));
    Console.ResetColor();
}

if (config.ShowOverlay)
{
    StartOverlay();
}

if (OperatingSystem.IsWindows())
{
    Updater.CleanupOld();
    if (config.StartWithFortnite)
    {
        FortniteWatch.Apply(true, dataDir); // follows the exe if it was moved or updated, and restarts the standby
    }
    if (Process.GetProcessesByName("ChestTracker") is { Length: > 0 } oldTool)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine(L.T("L'ancienne version (ChestTracker.exe) est encore ouverte : ferme-la, sinon les deux comptent en même temps.",
            "The old version (ChestTracker.exe) is still open: close it, otherwise both count at the same time.",
            "La versión antigua (ChestTracker.exe) sigue abierta: ciérrala, si no las dos cuentan a la vez."));
        Console.ResetColor();
        foreach (var p in oldTool) p.Dispose();
    }
}

// Nothing to read yet: explain why, so that a new user is not left wondering.
if (!tracker.ReplayFiles().Any())
{
    Console.ForegroundColor = ConsoleColor.DarkYellow;
    Console.WriteLine(Directory.Exists(config.DemosFolder)
        ? L.T("Aucun replay pour l'instant : c'est normal si tu n'as pas encore joué depuis que les replays sont activés.",
              "No replay yet: that is expected if you have not played since replays were turned on.",
              "Aún no hay repeticiones: es normal si no has jugado desde que activaste las repeticiones.")
        : L.T("Le dossier des replays n'existe pas encore : Fortnite le crée à la première partie enregistrée.",
              "The replays folder does not exist yet: Fortnite creates it with the first recorded match.",
              "La carpeta de repeticiones aún no existe: Fortnite la crea con la primera partida grabada."));
    Console.WriteLine(L.T("Vérifie dans Fortnite : Paramètres → Jeu → Rediffusions → Enregistrer les rediffusions.",
        "Check in Fortnite: Settings → Game → Replays → Record Replays.",
        "Compruébalo en Fortnite: Ajustes → Juego → Repeticiones → Grabar repeticiones."));
    Console.ResetColor();
}

// Replays left from earlier (first launch, new analysis version): they are analyzed by the main loop, once the window
// is open. Meanwhile the report shows a loading card and the home tab a progress bar.
var backlog = tracker.BacklogCount();
if (backlog > 1)
{
    ReportWriter.Progress = new ScanProgress(0, backlog);
}
ReportWriter.WriteAll(store, config, data);
UpdateOverlay(store, config, data, tracker.LiveMatch, tracker.IsLive);

// The first time, the pictures of the treasure collection (always: the report needs them). Then a new version on
// GitHub (checked in the background, unless the updates are off).
// content.json is read from GitHub at each start, then every 2 hours (a few kilobytes): new weeks, prize ids, Treasure
// Hours and notices (a known problem...) without a new version of the tool, even for a counter left open all day.
// Always, like the pictures: it is only data.
var imagesAdded = false;
_ = Task.Run(async () =>
{
    while (!Volatile.Read(ref quit))
    {
        var changed = await TreasureCatalog.RefreshAsync(store.Folder);
        if (TreasureImages.Missing() && await TreasureImages.DownloadMissingAsync() > 0)
        {
            changed = true;
        }
        if (changed)
        {
            Volatile.Write(ref imagesAdded, true); // the report is written again by the main loop
            wake.Set();
        }
        await Task.Delay(TimeSpan.FromHours(2));
    }
});
if (config.UpdateMode != "off")
{
    _ = Task.Run(async () =>
    {
        var found = await Updater.CheckAsync();
        if (found != null)
        {
            Volatile.Write(ref update, found);
            wake.Set();
        }
    });
}

Console.CancelKeyPress += (_, e) => { e.Cancel = true; quit = true; wake.Set(); };

// Graphical interface: the local page, opened now (not when opened with Fortnite: the game keeps the screen).
GuiServer? guiServer = null;
var guiWindowed = false; // the page is shown in the counter's own window (WebView2), not in a browser
var guiActions = new ConcurrentQueue<Action>();
object? lastGuiState = null;
var welcome = firstRun;
if (gui)
{
    guiServer = new GuiServer(GuiState, GuiAction, store.Folder, store.ReportPath, store.OverlayPath);
    guiServer.Start();
    if (Environment.GetEnvironmentVariable("CHESTCOUNTER_GUI_URL_FILE") is { Length: > 0 } urlFile)
    {
        File.WriteAllText(urlFile, guiServer.Url); // development: the address is written instead of opening a window
    }
    else if (OperatingSystem.IsWindows() && GuiWindow.Available())
    {
        // The counter's own window: the close button asks before stopping it, minimizing goes to the taskbar or the tray.
        guiWindowed = true;
        GuiWindow.MinimizeToTray = config.MinimizeToTray;
        // CHESTCOUNTER_GUI_TAB (development): the tab the window opens on.
        var startUrl = guiServer.Url + "&w=1" + (Environment.GetEnvironmentVariable("CHESTCOUNTER_GUI_TAB") is { Length: > 0 } startTab ? "#" + startTab : "");
        GuiWindow.Start(startUrl, Path.Combine(store.Folder, "webview"), $"{appName} {AppInfo.VersionText}", startHidden: auto,
            quit: () => { quit = true; wake.Set(); },
            closeQuestion: () => L.T("Fermer le compteur de coffres ?\n\nTes prochaines parties ne seront comptées qu'à sa prochaine ouverture.",
                "Close the chest counter?\n\nYour next matches will only be counted when it is opened again.",
                "¿Cerrar el contador de cofres?\n\nTus próximas partidas solo se contarán cuando se vuelva a abrir."),
            closeTitle: () => appName,
            openText: () => L.T("Ouvrir", "Open", "Abrir"),
            quitText: () => L.T("Fermer le compteur", "Close the counter", "Cerrar el contador"),
            trayNotice: () => L.T("Le compteur continue de compter dans la zone de notification, à côté de l'horloge, en bas à droite de la barre des tâches (icône du coffre, parfois derrière la petite flèche ^). Clique dessus pour le rouvrir.",
                "The counter keeps counting in the notification area, next to the clock, at the bottom right of the taskbar (chest icon, sometimes behind the small ^ arrow). Click it to open it again.",
                "El contador sigue contando en el área de notificación, junto al reloj, abajo a la derecha de la barra de tareas (icono del cofre, a veces detrás de la pequeña flecha ^). Haz clic en él para volver a abrirlo."));
    }
    else if (!auto)
    {
        guiServer.Open(); // no WebView2 on this PC: the page in a browser window
    }
}

// Launched a second time: the window comes to the front (and out of the tray), or the page opens again.
if (OperatingSystem.IsWindows())
{
    var showMe = new EventWaitHandle(false, EventResetMode.AutoReset, AppInfo.ShowEventFor(dataDir));
    new Thread(() =>
    {
        while (showMe.WaitOne())
        {
            if (guiWindowed && OperatingSystem.IsWindows())
            {
                GuiWindow.Restore();
            }
            else
            {
                guiServer?.Open();
            }
            if (autoCloseAt != null)
            {
                autoCloseAt = null; // the player wants it: it stays open after Fortnite
                auto = false;
            }
            wake.Set();
        }
    }) { IsBackground = true, Name = "ShowRequests" }.Start();
}

WatchReplays();

// During a long scan, the window's actions (welcome card, settings…) are run between two replays.
tracker.BetweenFiles = () =>
{
    while (!quit && guiActions.TryDequeue(out var pending))
    {
        pending();
    }
};
tracker.StopRequested = () => quit;

nextScan = DateTime.UtcNow.AddSeconds(backlog > 0 ? 1 : 2);
var reportWeek = TreasureCatalog.ShownWeek(DateTime.UtcNow)?.Number;
while (!quit)
{
    if (DateTime.UtcNow >= nextScan)
    {
        try
        {
            tracker.Scan();
            if (ReportWriter.Progress != null)
            {
                // The replays left at startup are read (a batch clears it itself, a single replay does not).
                ReportWriter.Progress = null;
                ReportWriter.WriteAll(store, config, data);
            }
            AnnounceNewProfiles();
            // Match in progress: re-read the replay being recorded (throttled inside).
            tracker.UpdateLive();
            UpdateOverlay(store, config, data, tracker.LiveMatch, tracker.IsLive);
        }
        catch (Exception ex)
        {
            Console.WriteLine(L.T($"Erreur pendant la surveillance : {ex.Message}", $"Error while watching: {ex.Message}", $"Error durante la vigilancia: {ex.Message}"));
        }
        // A replay being written: check again soon. Otherwise only a safety scan once a minute.
        nextScan = DateTime.UtcNow.AddSeconds(tracker.IsFinishing ? 2 : tracker.HasPending ? 15 : (watcher != null && !auto ? 60 : 10));
        if (watcher == null && Directory.Exists(config.DemosFolder))
        {
            WatchReplays(); // Fortnite just created the folder (first recorded match)
        }
        CheckReplaysRecorded();
    }

    if (auto && OperatingSystem.IsWindows())
    {
        CheckAutoClose();
    }

    // A new treasure week (Sunday): the chances of the weekly treasures start again from that week's chests, even for
    // a counter left open with no match played.
    var shownWeek = TreasureCatalog.ShownWeek(DateTime.UtcNow)?.Number;
    if (shownWeek != reportWeek)
    {
        reportWeek = shownWeek;
        ReportWriter.WriteAll(store, config, data);
    }

    if (Volatile.Read(ref imagesAdded))
    {
        imagesAdded = false;
        ReportWriter.WriteAll(store, config, data); // the report shows the pictures just downloaded
    }

    var pendingUpdate = Volatile.Read(ref update);
    if (pendingUpdate != null && !updateAnnounced)
    {
        var idle = !tracker.IsLive && !tracker.HasPending && !tracker.IsFinishing;
        if (config.UpdateMode == "auto")
        {
            if (idle && AppInfo.ExePath != null)
            {
                updateAnnounced = true;
                InstallUpdate(pendingUpdate);
            }
        }
        else
        {
            updateAnnounced = true;
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(L.T($"Nouvelle version {pendingUpdate.Version.ToString(3)} disponible : ses nouveautés sont affichées dans l'accueil.",
                $"New version {pendingUpdate.Version.ToString(3)} available: what's new is shown on the home tab.",
                $"Nueva versión {pendingUpdate.Version.ToString(3)} disponible: las novedades se muestran en el inicio."));
            Console.ResetColor();
        }
    }

    if (quit)
    {
        break;
    }

    // Sleep until something happens (file change, action from the window) or the next scheduled scan.
    var wait = nextScan - DateTime.UtcNow;
    if (wait > TimeSpan.Zero && wake.WaitOne(wait))
    {
        // A file change: check again within 2 s (the readiness checks decide when a replay is complete).
        if (!quit && watcherFired)
        {
            watcherFired = false;
            nextScan = DateTime.UtcNow.AddSeconds(2) < nextScan ? DateTime.UtcNow.AddSeconds(2) : nextScan;
        }
    }

    while (!quit && guiActions.TryDequeue(out var guiAction))
    {
        guiAction();
    }
}

watcher?.Dispose();
if (OperatingSystem.IsWindows())
{
    NativeOverlay.Stop();
    if (guiWindowed)
    {
        GuiWindow.Close();
    }
}


if (restart && !uninstalled && AppInfo.ExePath is { } newExe)
{
    try { instance.ReleaseMutex(); } catch { }
    var restartArgs = string.Join(" ", args.Where(a => a is not ("--after-update" or "--reset-config"))
        .Select(a => a.Contains(' ') ? $"\"{a}\"" : a).Append("--after-update"));
    Process.Start(new ProcessStartInfo(newExe, restartArgs) { UseShellExecute = true })?.Dispose();
}
return 0;

// ---- Actions -------------------------------------------------------------------------------

void StartOverlay()
{
    if (!OperatingSystem.IsWindows() || NativeOverlay.IsRunning)
    {
        return;
    }
    NativeOverlay.SetAppearance(config.OverlaySize, config.OverlayOpacity);
    if (Array.IndexOf(NativeOverlay.Styles, config.OverlayStyle) < 0)
    {
        config.OverlayStyle = "hud"; // a style tried in a test version and removed since
    }
    NativeOverlay.SetStyle(config.OverlayStyle);
    NativeOverlay.Start(config.OverlayX, config.OverlayY, config.OverlayLocked,
        (x, y) =>
        {
            config.OverlayX = x;
            config.OverlayY = y;
            try { store.SaveConfig(config); } catch { /* ignore */ }
        },
        locked =>
        {
            config.OverlayLocked = locked;
            try { store.SaveConfig(config); } catch { /* ignore */ }
        });
    UpdateOverlay(store, config, data, tracker.LiveMatch, tracker.IsLive);
}

void ToggleOverlay()
{
    config.ShowOverlay = !config.ShowOverlay;
    if (config.ShowOverlay)
    {
        StartOverlay();
    }
    else if (OperatingSystem.IsWindows())
    {
        NativeOverlay.Stop();
    }
}

void ToggleStartWithFortnite()
{
    var on = !config.StartWithFortnite;
    if (OperatingSystem.IsWindows() && FortniteWatch.Apply(on, dataDir))
    {
        config.StartWithFortnite = on;
        return;
    }
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine(L.T("Ouvrir avec Fortnite : impossible ici, lance le compteur depuis ChestCounter.exe.",
        "Open with Fortnite: not possible here, run the counter from ChestCounter.exe.",
        "Abrir con Fortnite: no es posible aquí, abre el contador desde ChestCounter.exe."));
    Console.ResetColor();
}

static bool CopyToClipboard(string text)
{
    try
    {
        // clip.exe reads UTF-16 with a byte order mark (any path, accents included).
        using var clip = Process.Start(new ProcessStartInfo("clip.exe") { RedirectStandardInput = true, UseShellExecute = false, CreateNoWindow = true });
        if (clip == null)
        {
            return false;
        }
        var bytes = System.Text.Encoding.Unicode.GetPreamble().Concat(System.Text.Encoding.Unicode.GetBytes(text)).ToArray();
        clip.StandardInput.BaseStream.Write(bytes);
        clip.StandardInput.Close();
        return clip.WaitForExit(3000) && clip.ExitCode == 0;
    }
    catch
    {
        return false;
    }
}

// Uninstall: nothing left running, the exe deleted (and the data folder if asked). A running exe cannot delete itself:
// a small hidden script waits for the counter to quit, deletes the files, then deletes itself.
// Returns an error message, or null when the counter is about to quit.
string? UninstallNow(bool deleteData)
{
    if (!OperatingSystem.IsWindows() || AppInfo.ExePath is not { } exe)
    {
        return "ChestCounter.exe";
    }
    // Nothing may start again: no entry at Windows logon, no standby (it would keep the exe in use).
    FortniteWatch.Apply(false, dataDir);
    config.StartWithFortnite = false;
    if (!deleteData)
    {
        try { store.SaveConfig(config); } catch { /* the folder is kept as it is */ }
    }

    var lines = new List<string>
    {
        "@echo off",
        "chcp 65001 >nul",
        // Wait for the counter (and the standby) to quit: deleting fails while the exe is in use.
        "for /l %%i in (1,1,60) do (",
        $"  del /f /q \"{exe}\" >nul 2>&1",
        $"  if not exist \"{exe}\" goto done",
        "  ping -n 2 127.0.0.1 >nul",
        ")",
        ":done",
        $"del /f /q \"{exe}.old\" >nul 2>&1",
    };
    if (deleteData)
    {
        lines.Add($"rmdir /s /q \"{Path.GetFullPath(store.Folder)}\" >nul 2>&1");
        // Native libraries unpacked by the single-file exe.
        lines.Add("rmdir /s /q \"%TEMP%\\.net\\ChestCounter\" >nul 2>&1");
    }
    lines.Add("(goto) 2>nul & del \"%~f0\"");
    try
    {
        var script = Path.Combine(Path.GetTempPath(), $"ChestCounter-uninstall-{Environment.ProcessId}.cmd");
        File.WriteAllText(script, string.Join("\r\n", lines) + "\r\n", new System.Text.UTF8Encoding(false));
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{script}\"") { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetTempPath() })?.Dispose();
    }
    catch (Exception ex)
    {
        return ex.Message;
    }

    uninstalled = true;
    quit = true;
    return null;
}

// Manual check: asks GitHub now (a version may have come out since the counter was opened).
void CheckForUpdate()
{
    Console.WriteLine(L.T("Recherche d'une nouvelle version sur GitHub…", "Checking GitHub for a new version…", "Buscando una nueva versión en GitHub…"));
    var (found, reached) = Updater.CheckDetailedAsync().GetAwaiter().GetResult();
    if (found == null)
    {
        Console.WriteLine(reached
            ? L.T($"Tu as déjà la dernière version ({AppInfo.VersionText}).", $"You already have the latest version ({AppInfo.VersionText}).", $"Ya tienes la última versión ({AppInfo.VersionText}).")
            : L.T("GitHub est injoignable pour l'instant (pas de connexion ?) : réessaie plus tard.",
                "GitHub cannot be reached right now (no connection?): try again later.",
                "No se puede acceder a GitHub ahora mismo (¿sin conexión?): inténtalo más tarde."));
        return;
    }
    Volatile.Write(ref update, found);
    updateAnnounced = true;
}

void InstallUpdate(UpdateInfo u)
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine(L.T($"Téléchargement de la version {u.Version.ToString(3)}…", $"Downloading version {u.Version.ToString(3)}…", $"Descargando la versión {u.Version.ToString(3)}…"));
    Console.ResetColor();
    var error = Updater.Install(u);
    if (error != null)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(L.T($"Mise à jour impossible : {error}", $"Update failed: {error}", $"No se pudo actualizar: {error}"));
        Console.ResetColor();
        Console.WriteLine(L.T($"Tu peux la télécharger à la main : {u.PageUrl}", $"You can download it manually: {u.PageUrl}", $"Puedes descargarla a mano: {u.PageUrl}"));
        return;
    }
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine(L.T("Mise à jour installée : redémarrage du compteur…", "Update installed: restarting the counter…", "Actualización instalada: reiniciando el contador…"));
    Console.ResetColor();
    restart = true;
    quit = true;
}

void AnnounceNewProfiles()
{
    if (!tracker.NewProfilesFound)
    {
        return;
    }
    tracker.NewProfilesFound = false;
    Console.ForegroundColor = ConsoleColor.DarkYellow;
    Console.WriteLine(L.T("Plusieurs comptes Fortnite ont enregistré des replays sur ce PC : chacun a son profil. Choisis le tien en haut de la fenêtre, à côté du nom.",
        "Several Fortnite accounts recorded replays on this PC: each one has its own profile. Choose yours at the top of the window, next to the name.",
        "Varias cuentas de Fortnite grabaron repeticiones en este PC: cada una tiene su perfil. Elige la tuya arriba de la ventana, junto al nombre."));
    Console.ResetColor();
}

// Replays folder: Windows tells us when a file changes, instead of us re-reading the folder all the time.
void WatchReplays()
{
    watcher?.Dispose();
    watcher = null;
    try
    {
        if (Directory.Exists(config.DemosFolder))
        {
            var w = new FileSystemWatcher(config.DemosFolder, "*.replay")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true,
            };
            w.Created += (_, _) => { watcherFired = true; wake.Set(); };
            w.Changed += (_, _) => { watcherFired = true; wake.Set(); };
            w.Renamed += (_, _) => { watcherFired = true; wake.Set(); };
            watcher = w;
        }
    }
    catch
    {
        watcher = null; // fall back to the periodic scan
    }
}

// Fortnite has been running a long time and not a single replay was written since the counter started: replays are
// most likely off in Fortnite (the setting cannot be read from outside the game). Said once per session.
void CheckReplaysRecorded()
{
    if (replaysHintShown || !OperatingSystem.IsWindows())
    {
        return;
    }
    if (!FortniteWatch.IsFortniteRunning())
    {
        return;
    }
    fortniteSeenAt ??= DateTime.UtcNow;
    if (DateTime.UtcNow - fortniteSeenAt < TimeSpan.FromMinutes(40))
    {
        return;
    }
    var recent = tracker.ReplayFiles().Any(f => f.LastWriteTimeUtc >= startedAt);
    if (recent)
    {
        replaysHintShown = true; // replays are recorded: nothing to say
        return;
    }
    replaysHintShown = true;
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine(L.T("Fortnite est ouvert depuis 40 minutes et aucun replay n'a été enregistré. Si tu as joué des parties, vérifie dans Fortnite  Paramètres → Jeu → Rediffusions → Enregistrer les rediffusions.",
        "Fortnite has been open for 40 minutes and no replay was recorded. If you played matches, check in Fortnite  Settings → Game → Replays → Record Replays.",
        "Fortnite lleva 40 minutos abierto y no se ha grabado ninguna repetición. Si has jugado partidas, comprueba en Fortnite  Ajustes → Juego → Repeticiones → Grabar repeticiones."));
    Console.ResetColor();
}

void CheckAutoClose()
{
    var now = DateTime.UtcNow;
    if (FortniteWatch.IsFortniteRunning())
    {
        fortniteClosedSince = null;
        autoCloseAt = null; // Fortnite started again
        return;
    }
    fortniteClosedSince ??= now;
    // Wait for the last match to be counted (at most 5 minutes, in case a replay was left unfinished by a crash).
    var lastMatchDone = !tracker.IsLive && !tracker.HasPending && !tracker.IsFinishing;
    if (!lastMatchDone && now - fortniteClosedSince < TimeSpan.FromMinutes(5))
    {
        return;
    }
    if (autoCloseAt == null)
    {
        autoCloseAt = now.AddSeconds(30);
    }
    else if (now >= autoCloseAt)
    {
        quit = true;
    }
}

// Shows the new size / opacity on the overlay right away.
void ApplyOverlayAppearance()
{
    if (OperatingSystem.IsWindows() && NativeOverlay.IsRunning)
    {
        NativeOverlay.SetAppearance(config.OverlaySize, config.OverlayOpacity);
    }
}

// ---- Graphical interface: state for the page, actions from the page --------------------------------

object GuiState()
{
    try
    {
        var state = BuildGuiState();
        lastGuiState = state;
        return state;
    }
    catch
    {
        return lastGuiState ?? new { }; // the history was being written at that very moment: the previous state
    }
}

object BuildGuiState()
{
    var view = Profiles.View(data, config);
    var matches = view.Matches;
    var profile = Profiles.Current(data, config);
    string kind, text;
    if (autoCloseAt is { } closeAt)
    {
        kind = "busy";
        text = L.T($"Fortnite est fermé : le compteur se ferme dans {Math.Max(0, (int)Math.Ceiling((closeAt - DateTime.UtcNow).TotalSeconds))} s", $"Fortnite is closed: the counter closes in {Math.Max(0, (int)Math.Ceiling((closeAt - DateTime.UtcNow).TotalSeconds))} s", $"Fortnite está cerrado: el contador se cierra en {Math.Max(0, (int)Math.Ceiling((closeAt - DateTime.UtcNow).TotalSeconds))} s");
    }
    else if (tracker.IsLive)
    {
        kind = "live";
        text = tracker.LiveMatch is { } live
            ? L.T($"Partie en cours : {L.Count(live.ChestsOpened, "coffre", "chest", "cofre")}", $"Match in progress: {L.Count(live.ChestsOpened, "coffre", "chest", "cofre")}", $"Partida en curso: {L.Count(live.ChestsOpened, "coffre", "chest", "cofre")}")
            : L.T("Partie en cours", "Match in progress", "Partida en curso");
    }
    else if (tracker.IsFinishing || tracker.HasPending || ReportWriter.Progress != null)
    {
        kind = "busy";
        text = L.T("Comptage des coffres…", "Counting the chests…", "Contando los cofres…");
    }
    else if (!Directory.Exists(config.DemosFolder))
    {
        kind = "warn";
        text = L.T("Dossier des replays introuvable", "Replays folder not found", "Carpeta de repeticiones no encontrada");
    }
    else
    {
        kind = "";
        text = L.T("En attente d'une partie", "Waiting for a match", "Esperando una partida");
    }

    var last = matches.OrderByDescending(m => m.MatchStartUtc ?? m.AnalyzedAtUtc).FirstOrDefault();
    var session = matches.Where(ReportWriter.IsThisSession).ToList();
    var u = Volatile.Read(ref update);
    static string ColorClass(ConsoleColor c) => c switch
    {
        ConsoleColor.Red or ConsoleColor.DarkRed => "err",
        ConsoleColor.Yellow or ConsoleColor.DarkYellow => "warn",
        ConsoleColor.Green or ConsoleColor.DarkGreen => "ok",
        ConsoleColor.Cyan or ConsoleColor.DarkCyan => "info",
        ConsoleColor.Magenta or ConsoleColor.DarkMagenta => "tre",
        ConsoleColor.DarkGray => "mut",
        _ => "",
    };
    var progress = ReportWriter.Progress;
    return new
    {
        txt = GuiPage.Texts(),
        lang = L.Code,
        appName,
        version = AppInfo.VersionText,
        player = profile?.Name,
        firstRun = welcome,
        autoClose = autoCloseAt != null,
        status = new { kind, text },
        total = L.N(view.TotalChests),
        manual = view.ManualAdjust,
        session = L.N(session.Sum(m => m.ChestsOpened)),
        sessionMatches = L.N(session.Count),
        last = last == null ? null : new { chests = L.N(last.ChestsOpened), when = L.DateTimeShort(L.Shown(last.MatchStartUtc ?? last.AnalyzedAtUtc)), mode = Pretty.Playlist(last.Playlist) },
        squad = config.ShowSquadTotal && view.TeamChests > 0 ? new { total = L.N(view.SquadChests), team = L.N(view.TeamChests) } : null,
        treasures = L.N(matches.Sum(m => m.TreasureFinds.Count)),
        matches = L.N(matches.Count),
        average = matches.Count > 0 ? ((double)view.CountedChests / matches.Count).ToString("F1", L.Culture) : "0",
        progress = progress == null ? null : new { done = progress.Done, total = progress.Total },
        update = u == null ? null : new { version = u.Version.ToString(3), notes = u.WhatsNew, url = u.PageUrl },
        // First launch after an update: what this version brings (the notes are built into the exe).
        whatsNew = showWhatsNew && u == null ? ReleaseNotes.WhatsNew(ReleaseNotes.Embedded, L.Code) : null,
        updatesOn = config.UpdateMode != "off",
        activity = log.Last(60).Select(line => line.Select(s => new { t = s.Text, c = ColorClass(s.Fg) })).ToList(),
        overlay = new
        {
            show = config.ShowOverlay,
            running = OperatingSystem.IsWindows() && NativeOverlay.IsRunning,
            visible = OperatingSystem.IsWindows() && NativeOverlay.IsVisible,
            locked = OperatingSystem.IsWindows() && NativeOverlay.IsLocked,
            style = config.OverlayStyle,
            size = config.OverlaySize,
            opacity = config.OverlayOpacity,
            sizes = OverlayAppearance.Sizes,
            opacities = OverlayAppearance.Opacities,
            squad = config.ShowSquadTotal,
        },
        profiles = Profiles.List(data).Select(p => new { key = p.Key, name = p.Name, matches = L.N(p.Matches), chests = L.N(p.Chests) }).ToList(),
        profileKey = profile?.Key,
        settings = new
        {
            language = L.Code,
            languages = L.Codes.Select(c => new[] { c, L.NativeName(c) }).ToList(),
            windowed = guiWindowed,
            minimizeToTray = config.MinimizeToTray,
            startWithFortnite = config.StartWithFortnite,
            canStartWithFortnite = OperatingSystem.IsWindows() && AppInfo.ExePath != null,
            updateMode = config.UpdateMode,
            timeZone = config.TimeZone,
            clockFormat = config.ClockFormat,
            localZone = L.LocalOffset,
            windowsClock = L.WindowsClock24 ? "24" : "12",
            demos = config.DemosFolder,
            demosExists = Directory.Exists(config.DemosFolder),
        },
        paths = new { data = store.Folder, overlay = Path.GetFullPath(store.OverlayPath), errorLog = File.Exists(store.ErrorLogPath) ? store.ErrorLogPath : null },
        canUninstall = OperatingSystem.IsWindows() && AppInfo.ExePath != null,
        notices = TreasureCatalog.ActiveNotices(DateTime.UtcNow).Where(n => !config.DismissedNotices.Contains(n.Id))
            .Select(n => new { id = n.Id, level = n.Level, title = n.TitleIn(L.Code), text = n.TextIn(L.Code), link = n.Link }).ToList(),
        reportStamp = File.Exists(store.ReportPath) ? File.GetLastWriteTimeUtc(store.ReportPath).Ticks.ToString() : "",
    };
}

// Runs on the main loop (the counter's state is only changed there), the page waits for the result.
object? GuiAction(string name, System.Text.Json.JsonElement value)
{
    var done = new TaskCompletionSource<object?>();
    guiActions.Enqueue(() =>
    {
        try { done.SetResult(RunGuiAction(name, value)); }
        catch (Exception ex) { done.SetResult(new { error = ex.Message }); }
    });
    wake.Set();
    return done.Task.Wait(TimeSpan.FromSeconds(20)) ? done.Task.Result : null;
}

object? RunGuiAction(string name, System.Text.Json.JsonElement value)
{
    bool Bool() => value.ValueKind == System.Text.Json.JsonValueKind.True;
    string Str() => value.ValueKind == System.Text.Json.JsonValueKind.String ? value.GetString() ?? "" : value.ToString();
    int Int() => value.ValueKind == System.Text.Json.JsonValueKind.Number ? value.GetInt32() : ChestEstimate.ParseNumber(Str()) ?? 0;
    var settingsChanged = true;
    object? result = null;
    switch (name)
    {
        case "dismissWelcome": welcome = false; settingsChanged = false; break;
        case "dismissNotice":
            // only an information can be closed: a warning stays until its end date
            if (TreasureCatalog.Notices.FirstOrDefault(n => n.Id == Str()) is { Level: "info" } closed && !config.DismissedNotices.Contains(closed.Id))
            {
                config.DismissedNotices.Add(closed.Id);
                store.SaveConfig(config);
            }
            settingsChanged = false;
            break;
        case "keepOpen":
            // Opened with Fortnite, closing after it: the player wants it to stay open.
            autoCloseAt = null;
            auto = false;
            settingsChanged = false;
            break;
        case "dismissWhatsNew":
            showWhatsNew = false;
            config.LastVersion = AppInfo.VersionText;
            break;
        case "toggleOverlayVisible": if (OperatingSystem.IsWindows()) NativeOverlay.Toggle(); settingsChanged = false; break;
        case "toggleOverlayLock": if (OperatingSystem.IsWindows()) NativeOverlay.ToggleLock(); settingsChanged = false; break;
        case "checkUpdate": CheckForUpdate(); settingsChanged = false; break;
        case "installUpdate": if (Volatile.Read(ref update) is { } u) InstallUpdate(u); settingsChanged = false; break;
        case "showOverlay": if (Bool() != config.ShowOverlay) ToggleOverlay(); break;
        case "overlayStyle":
            if (Array.IndexOf(NativeOverlay.Styles, Str()) >= 0)
            {
                config.OverlayStyle = Str();
                if (OperatingSystem.IsWindows()) NativeOverlay.SetStyle(config.OverlayStyle);
            }
            break;
        case "overlaySize": config.OverlaySize = OverlayAppearance.ClampSize(Int()); ApplyOverlayAppearance(); break;
        case "overlayOpacity": config.OverlayOpacity = OverlayAppearance.ClampOpacity(Int()); ApplyOverlayAppearance(); break;
        case "showSquadTotal": config.ShowSquadTotal = Bool(); break;
        case "showSquadInReport": config.ShowSquadInReport = Bool(); store.SaveConfig(config); settingsChanged = false; break;
        case "showTeamInReport": config.ShowTeamTreasuresInReport = Bool(); store.SaveConfig(config); settingsChanged = false; break;
        case "language":
            if (L.Normalize(Str()) is { } code)
            {
                config.Language = code;
                L.Set(code);
                appName = L.T("Compteur de coffres Fortnite", "Fortnite Chest Counter", "Contador de cofres de Fortnite");
            }
            break;
        case "profile": if (Profiles.List(data).Any(p => p.Key == Str())) config.Profile = Str(); break;
        case "minimizeToTray":
            config.MinimizeToTray = Bool();
            if (OperatingSystem.IsWindows()) GuiWindow.MinimizeToTray = config.MinimizeToTray;
            break;
        case "startWithFortnite": if (Bool() != config.StartWithFortnite) ToggleStartWithFortnite(); break;
        case "updateMode": config.UpdateMode = Str() is "auto" or "off" ? Str() : "prompt"; break;
        case "timeZone": config.TimeZone = Str(); config.ApplyTimeSettings(); break;
        case "clockFormat": config.ClockFormat = Str(); config.ApplyTimeSettings(); break;
        case "demosFolder":
            if (Str().Trim().Trim('"') is { Length: > 0 } folder && folder != config.DemosFolder)
            {
                config.DemosFolder = folder;
                WatchReplays();
                nextScan = DateTime.UtcNow;
            }
            break;
        case "manualChests":
        {
            var view = Profiles.View(data, config);
            Profiles.SetManual(data, config, Math.Clamp(Int(), -view.CountedChests, 10_000_000));
            store.SaveData(data);
            break;
        }
        case "estimate":
        {
            var view = Profiles.View(data, config);
            settingsChanged = false;
            if (view.Matches.Count < ChestEstimate.MinCountedMatches)
            {
                result = new { error = string.Format(GuiPage.Texts()["estimateTooFew"].Replace("{0}", "{0}"), ChestEstimate.MinCountedMatches) };
            }
            else if (ChestEstimate.FromCareer(view, Int()) is { } e)
            {
                var avg = e.Average.ToString("0.0", L.Culture);
                result = new
                {
                    chests = e.Chests,
                    chestsText = L.N(e.Chests),
                    text = L.T($"({L.N(e.CareerMatches)} − {L.N(e.CountedMatches)} déjà comptées) × {avg} coffres en moyenne = {L.N(e.Chests)} coffres (remplace l'ajout précédent)",
                        $"({L.N(e.CareerMatches)} − {L.N(e.CountedMatches)} already counted) × {avg} chests on average = {L.N(e.Chests)} chests (replaces the previous addition)",
                        $"({L.N(e.CareerMatches)} − {L.N(e.CountedMatches)} ya contadas) × {avg} cofres de media = {L.N(e.Chests)} cofres (sustituye lo añadido antes)"),
                };
            }
            else
            {
                result = new { error = GuiPage.Texts()["estimateNothing"] };
            }
            break;
        }
        case "obtained":
            if (value.ValueKind == System.Text.Json.JsonValueKind.Object && value.TryGetProperty("id", out var prizeId)
                && TreasureCatalog.Weeks.SelectMany(w => w.Prizes).Prepend(TreasureCatalog.PirateShopAccess).Any(p => p.Id == prizeId.GetString()))
            {
                Profiles.SetObtained(data, config, prizeId.GetString()!, value.TryGetProperty("on", out var on) && on.ValueKind == System.Text.Json.JsonValueKind.True);
                store.SaveData(data);
                ReportWriter.WriteAll(store, config, data);
            }
            settingsChanged = false;
            break;
        case "copyObsPath": CopyToClipboard(Path.GetFullPath(store.OverlayPath)); settingsChanged = false; break;
        case "openDataFolder": Open(store.Folder); settingsChanged = false; break;
        case "openErrorLog": if (File.Exists(store.ErrorLogPath)) Open(store.ErrorLogPath); settingsChanged = false; break;
        case "openIssues": Open(AppInfo.IssuesUrl); settingsChanged = false; break;
        case "openRepo": Open(AppInfo.RepoUrl); settingsChanged = false; break;
        case "uninstall": result = UninstallNow(Bool()); settingsChanged = false; break;
        case "quit": quit = true; settingsChanged = false; break;
        default: settingsChanged = false; break;
    }
    if (settingsChanged)
    {
        store.SaveConfig(config);
        ReportWriter.WriteAll(store, config, data);
        UpdateOverlay(store, config, data, tracker.LiveMatch, tracker.IsLive);
    }
    return result;
}

static bool AcquireInstance(Mutex m, TimeSpan wait)
{
    try
    {
        return m.WaitOne(wait);
    }
    catch (AbandonedMutexException)
    {
        return true; // the previous instance was killed: it is ours now
    }
}

static void UpdateOverlay(Store store, Config config, TrackerData d, MatchRecord? live, bool inProgress)
{
    ReportWriter.OverlayStyle = config.OverlayStyle;
    var view = OverlayModel.Build(Profiles.View(d, config), live, inProgress, config.ShowSquadTotal);
    ReportWriter.WriteOverlay(store, view);
    if (OperatingSystem.IsWindows() && NativeOverlay.IsRunning)
    {
        NativeOverlay.Update(view);
    }
}

// Below-normal priority + Windows 11 "efficiency mode" (EcoQoS): the game always comes first.
static void LowerPriority()
{
    if (!OperatingSystem.IsWindows())
    {
        return;
    }
    try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
    try { EcoQos.Enable(); } catch { }
}

static void Open(string path)
{
    try
    {
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
    catch
    {
        Console.WriteLine(L.T($"Ouvre ce fichier dans ton navigateur : {path}", $"Open this file in your browser: {path}", $"Abre este archivo en tu navegador: {path}"));
    }
}
