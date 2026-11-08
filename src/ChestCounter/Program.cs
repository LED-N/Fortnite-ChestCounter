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

// Windows app: open a console window (or write into the terminal for command line options).
if (OperatingSystem.IsWindows())
{
    NativeConsole.Ensure(attachToParent: args.Any(a => a is "--once" or "--help" or "-h" or "--overlay-preview"));
}
Console.OutputEncoding = Encoding.UTF8;

// A crash must not close the window before the message can be read (the exe is usually double-clicked).
AppDomain.CurrentDomain.UnhandledException += (_, e) =>
{
    ErrorLog.Write("Crash", e.ExceptionObject);
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine();
    Console.WriteLine(L.T("Erreur inattendue : ", "Unexpected error: ", "Error inesperado: ", "予期しないエラー: ") + e.ExceptionObject);
    Console.ResetColor();
    if (!Console.IsInputRedirected)
    {
        Console.WriteLine(L.T("Appuie sur une touche pour fermer.", "Press any key to close.", "Pulsa una tecla para cerrar.", "何かキーを押すと閉じます。"));
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
        case "-h":
        case "--help":
            var langIndex = Array.IndexOf(args, "--lang");
            L.Set(langIndex >= 0 && langIndex + 1 < args.Length ? L.Normalize(args[langIndex + 1]) ?? L.SystemDefault : L.SystemDefault);
            Console.WriteLine(L.T("""
                Compteur de coffres Fortnite

                Options :
                  --lang <fr|en|es|ja>   langue de l'outil (français, anglais, espagnol ou japonais)
                  --demos <dossier>   dossier des replays (par défaut %LOCALAPPDATA%\FortniteGame\Saved\Demos)
                  --data <dossier>    dossier des données (par défaut %APPDATA%\ChestCounter)
                  --once              analyse les replays terminés puis quitte
                  --reset-config      repose les questions de configuration
                  --overlay-preview <fichier.png>   enregistre des aperçus de l'overlay puis quitte
                """, """
                Fortnite Chest Counter

                Options:
                  --lang <fr|en|es|ja>   language of the tool (French, English, Spanish or Japanese)
                  --demos <folder>    replays folder (default %LOCALAPPDATA%\FortniteGame\Saved\Demos)
                  --data <folder>     data folder (default %APPDATA%\ChestCounter)
                  --once              analyze the finished replays, then quit
                  --reset-config      ask the setup questions again
                  --overlay-preview <file.png>   save previews of the overlay, then quit
                """, """
                Contador de cofres de Fortnite

                Opciones:
                  --lang <fr|en|es|ja>   idioma de la herramienta (francés, inglés, español o japonés)
                  --demos <carpeta>   carpeta de repeticiones (por defecto %LOCALAPPDATA%\FortniteGame\Saved\Demos)
                  --data <carpeta>    carpeta de datos (por defecto %APPDATA%\ChestCounter)
                  --once              analiza las repeticiones terminadas y sale
                  --reset-config      vuelve a hacer las preguntas de configuración
                  --overlay-preview <archivo.png>   guarda vistas previas del overlay y sale
                """, """
                Fortnite 宝箱カウンター

                オプション:
                  --lang <fr|en|es|ja>   使用言語（フランス語、英語、スペイン語、日本語）
                  --demos <folder>    リプレイフォルダー（既定: %LOCALAPPDATA%\FortniteGame\Saved\Demos）
                  --data <folder>     データフォルダー（既定: %APPDATA%\ChestCounter）
                  --once              終了済みのリプレイを解析して終了
                  --reset-config      初期設定をやり直す
                  --overlay-preview <file.png>   オーバーレイのプレビューを保存して終了
                """));
            return 0;
    }
}

dataDir ??= Store.DefaultFolder();
var store = new Store(dataDir);
ErrorLog.FilePath = store.ErrorLogPath;
var interactive = ConsoleUi.Interactive;

// One instance at a time: two would count the same matches and fight over the shortcuts.
using var instance = new Mutex(false, AppInfo.InstanceMutexFor(dataDir));
if (previewPath == null && !AcquireInstance(instance, afterUpdate ? TimeSpan.FromSeconds(20) : TimeSpan.Zero))
{
    if (auto)
    {
        return 0; // opened with Fortnite while the player had already opened it: nothing to do
    }
    L.Set(store.LoadConfig()?.Language ?? L.SystemDefault);
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine(L.T("  Le compteur de coffres est déjà ouvert : regarde dans la barre des tâches.",
        "  The chest counter is already open: look in the taskbar.",
        "  El contador de cofres ya está abierto: búscalo en la barra de tareas.", "  宝箱カウンターは既に起動しています。タスクバーを確認してください。"));
    Console.ResetColor();
    if (interactive)
    {
        Console.WriteLine(L.T("  (cette fenêtre se ferme dans 10 secondes)", "  (this window closes in 10 seconds)", "  (esta ventana se cierra en 10 segundos)", "  （このウィンドウは10秒後に閉じます）"));
        Thread.Sleep(10_000);
    }
    return 0;
}

// ---- Configuration -----------------------------------------------------------------------
var config = reset ? null : store.LoadConfig();
var firstRun = config == null;
var previousLanguage = reset ? store.LoadConfig()?.Language : null;
config ??= new Config();

// Language: --lang, else the saved choice, else asked once (Windows language when nobody can answer).
if (langArg != null)
{
    config.Language = L.Normalize(langArg) ?? config.Language;
}
if (L.Normalize(config.Language) == null)
{
    var suggested = L.Normalize(previousLanguage) ?? L.SystemDefault;
    if (interactive)
    {
        ConsoleUi.Screen("Fortnite Chest Counter  ·  Compteur de coffres  ·  Contador de cofres  ·  宝箱カウンター", "1/4");
        ConsoleUi.Text("Choisis ta langue  /  Choose your language  /  Elige tu idioma  /  言語を選んでください");
        var choice = ConsoleUi.Menu(L.Codes.Select(L.NativeName).ToList(), Math.Max(0, Array.IndexOf(L.Codes, suggested)), "↑ ↓  +  Entrée / Enter / Intro / 決定");
        config.Language = L.Codes[choice];
    }
    else
    {
        config.Language = suggested;
    }
}
L.Set(config.Language);
var appName = L.T("Compteur de coffres Fortnite", "Fortnite Chest Counter", "Contador de cofres de Fortnite", "Fortnite 宝箱カウンター");
ConsoleUi.SetTitle($"{appName} {AppInfo.VersionText}");

if (demosArg != null) config.DemosFolder = demosArg;
var defaultDemos = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FortniteGame", "Saved", "Demos");
if (string.IsNullOrWhiteSpace(config.DemosFolder))
{
    config.DemosFolder = defaultDemos;
}

if (previewPath != null)
{
    // Development: draws the overlay off-screen and saves it as PNG (idle + match in progress), then quits.
    store.LoadPrizeNames();
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
    NativeOverlay.Start(-20000, -20000, false, (_, _) => { }, _ => { });
    var fakeLive = new MatchRecord { ChestsOpened = 12, TreasureFinds = { new TreasureFind { Item = "Trésor" } } };
    foreach (var (suffix, live, inProgress) in new[] { ("-idle", (MatchRecord?)null, false), ("-inprogress", null, true), ("-live", fakeLive, true) })
    {
        NativeOverlay.Update(OverlayModel.Build(previewData, live, inProgress));
        Thread.Sleep(600);
        var file = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(previewPath)) ?? ".", Path.GetFileNameWithoutExtension(previewPath) + suffix + ".png");
        Console.WriteLine(NativeOverlay.Snapshot(file) ? L.T($"Aperçu : {file}", $"Preview: {file}", $"Vista previa: {file}", $"プレビュー: {file}") : L.T($"Échec de la capture : {file}", $"Capture failed: {file}", $"Error de captura: {file}", $"画像の保存に失敗しました: {file}"));
    }
    return 0;
}

LowerPriority();
store.LoadPrizeNames();
var data = store.LoadData();
var tracker = new Tracker(store, config, data);
// Before any scan: otherwise replays analyzed during the setup would be analyzed a second time right after.
var reanalyzing = tracker.UpgradeIfNeeded();
var reportOpened = false;

// ---- First launch: setup screens ----------------------------------------------------------
if (firstRun && interactive)
{
    // Step 2: the replays folder.
    ConsoleUi.Screen(appName, "2/4");
    ConsoleUi.Text(L.T("Tes replays", "Your replays", "Tus repeticiones", "リプレイの設定"), ConsoleColor.White);
    Console.WriteLine();
    if (!Directory.Exists(config.DemosFolder))
    {
        ConsoleUi.Text(L.T("✖ Dossier des replays introuvable :", "✖ Replays folder not found:", "✖ Carpeta de repeticiones no encontrada:", "✖ リプレイフォルダーが見つかりません:"), ConsoleColor.Red);
        ConsoleUi.Hint("  " + config.DemosFolder);
        Console.WriteLine();
        ConsoleUi.Text(L.T("Fortnite le crée à la première partie enregistrée. Si tu viens d'activer les replays, c'est normal.",
            "Fortnite creates it with the first recorded match. If you just turned replays on, that is expected.",
            "Fortnite la crea con la primera partida grabada. Si acabas de activar las repeticiones, es normal.", "Fortniteで初めて試合を録画すると作成されます。リプレイを有効にしたばかりなら問題ありません。"));
        var where = ConsoleUi.Menu(new[]
        {
            L.T("Garder ce dossier (conseillé)", "Keep this folder (recommended)", "Mantener esta carpeta (recomendado)", "このフォルダーを使用する（推奨）"),
            L.T("Indiquer un autre dossier", "Enter another folder", "Indicar otra carpeta", "別のフォルダーを指定する"),
        });
        if (where == 1)
        {
            config.DemosFolder = ConsoleUi.Input(L.T("Chemin du dossier des replays : ", "Path of the replays folder: ", "Ruta de la carpeta de repeticiones: ", "リプレイフォルダーのパス: "), config.DemosFolder);
            Console.WriteLine();
        }
    }
    else
    {
        ConsoleUi.Text(L.T("✔ Dossier des replays trouvé :", "✔ Replays folder found:", "✔ Carpeta de repeticiones encontrada:", "✔ リプレイフォルダーが見つかりました:"), ConsoleColor.Green);
        ConsoleUi.Hint("  " + config.DemosFolder);
    }
    Console.WriteLine();
    ConsoleUi.Text(L.T("Important : dans Fortnite, active  Paramètres → Jeu → Replays → Enregistrer les replays.",
        "Important: in Fortnite, turn on  Settings → Game → Replays → Record Replays.",
        "Importante: en Fortnite, activa  Ajustes → Juego → Repeticiones → Grabar repeticiones.", "重要: Fortniteの「設定 → ゲーム → リプレイ → リプレイを録画」を有効にしてください。"), ConsoleColor.Yellow);
    ConsoleUi.Hint(L.T("Sans ça, Fortnite n'enregistre aucun replay et l'outil n'a rien à compter.",
        "Without it, Fortnite saves no replay and the tool has nothing to count.",
        "Sin eso, Fortnite no graba ninguna repetición y la herramienta no tiene nada que contar.", "無効の場合、Fortniteはリプレイを保存しないため、宝箱を集計できません。"));
    Console.WriteLine();

    // Replays already on the PC.
    var existing = tracker.UnprocessedFiles().Where(f => ReplayHeaderProbe.IsLive(f.FullName) != true).ToList();
    var analyzeNow = false;
    if (existing.Count > 0)
    {
        var present = L.Count(existing.Count, "replay est déjà présent", "replays sont déjà présents", "replay is already on your PC", "replays are already on your PC",
            "repetición ya está en tu PC", "repeticiones ya están en tu PC", "件のリプレイが既にPCにあります");
        ConsoleUi.Text(L.T($"{present} sur ton PC. Les compter aussi ?", $"{present}. Count them too?", $"{present}. ¿Contarlas también?", $"{present}。これらも集計しますか？"));
        analyzeNow = ConsoleUi.Menu(new[]
        {
            L.T("Oui, les analyser maintenant", "Yes, analyze them now", "Sí, analizarlas ahora", "はい、今すぐ解析する"),
            L.T("Non, seulement mes prochaines parties", "No, only my next matches", "No, solo mis próximas partidas", "いいえ、これからの試合のみ集計する"),
        }) == 0;
        if (!analyzeNow)
        {
            tracker.MarkSkipped(existing);
        }
    }
    else
    {
        ConsoleUi.WaitEnter(L.T("Entrée pour continuer", "Enter to continue", "Intro para continuar", "Enterで続行"));
    }
    store.SaveConfig(config);

    if (analyzeNow)
    {
        ConsoleUi.Screen(appName, L.T("analyse", "analysis", "análisis", "解析"));
        ConsoleUi.Text(L.T("Analyse de tes replays…", "Analyzing your replays…", "Analizando tus repeticiones…", "リプレイを解析しています…"), ConsoleColor.White);
        ConsoleUi.Hint(L.T("Le rapport s'ouvre dans ton navigateur et se remplit au fur et à mesure.",
            "The report opens in your browser and fills in as the replays are read.",
            "El informe se abre en tu navegador y se completa al leer las repeticiones.", "ブラウザーでレポートが開き、リプレイの解析に合わせて結果が表示されます。"));
        Console.WriteLine();
        ReportWriter.Progress = new ScanProgress(0, existing.Count);
        ReportWriter.WriteAll(store, config, data);
        if (config.OpenReportOnStart)
        {
            Open(store.ReportPath);
            reportOpened = true;
        }
        tracker.Scan();
        tracker.NewProfilesFound = false;
        Console.WriteLine();
        if (Profiles.List(data).Count < 2)
        {
            ConsoleUi.WaitEnter(L.T("Entrée pour continuer", "Enter to continue", "Intro para continuar", "Enterで続行"));
        }
    }

    if (Profiles.List(data).Count >= 2)
    {
        ConsoleUi.Screen(appName, L.T("profil", "profile", "perfil", "プロフィール"));
        ChooseProfile(data, config, store);
    }

    // Step 3: open with Fortnite.
    if (OperatingSystem.IsWindows() && AppInfo.ExePath != null)
    {
        ConsoleUi.Screen(appName, "3/4");
        ConsoleUi.Text(L.T("Ouvrir le compteur automatiquement quand Fortnite démarre ?", "Open the counter automatically when Fortnite starts?", "¿Abrir el contador automáticamente cuando se inicia Fortnite?", "Fortniteの起動時にカウンターを自動で開きますか？"), ConsoleColor.White);
        Console.WriteLine();
        ConsoleUi.Text(L.T("Pratique pour ne jamais oublier de le lancer : sans lui, tes parties ne sont pas comptées.",
            "Handy to never forget it: without it, your matches are not counted.",
            "Práctico para no olvidarte nunca de abrirlo: sin él, tus partidas no se cuentan.", "起動し忘れを防げます。カウンターが起動していない間の試合は集計されません。"));
        ConsoleUi.Hint(L.T("Un tout petit processus invisible attend le lancement de Fortnite, puis ouvre le compteur.\nLe compteur se ferme tout seul après Fortnite. Désactivable à tout moment : touche C (réglages).",
            "A tiny invisible process waits for Fortnite to start, then opens the counter.\nThe counter closes by itself after Fortnite. Can be turned off at any time: C key (settings).",
            "Un pequeño proceso invisible espera a que se inicie Fortnite y luego abre el contador.\nEl contador se cierra solo después de Fortnite. Desactívalo cuando quieras: tecla C (ajustes).", "小さな常駐プロセスが非表示で待機し、Fortniteの起動時にカウンターを開きます。\nFortnite終了後、カウンターも自動で閉じます。Cキーの設定からいつでも無効にできます。"));
        var withFortnite = ConsoleUi.Menu(new[]
        {
            L.T("Oui (conseillé)", "Yes (recommended)", "Sí (recomendado)", "はい（推奨）"),
            L.T("Non, je le lancerai moi-même", "No, I will start it myself", "No, lo abriré yo mismo", "いいえ、手動で起動する"),
        }) == 0;
        config.StartWithFortnite = withFortnite && FortniteWatch.Apply(true, dataDir);
        store.SaveConfig(config);
    }

    // Step 4: ready.
    ConsoleUi.Screen(appName, "4/4");
    ConsoleUi.Text(L.T("C'est prêt !", "All set!", "¡Todo listo!", "準備ができました！"), ConsoleColor.Green);
    Console.WriteLine();
    ConsoleUi.Text(L.T("Laisse cette fenêtre ouverte (tu peux la réduire) pendant que tu joues :",
        "Keep this window open (you can minimize it) while you play:",
        "Deja esta ventana abierta (puedes minimizarla) mientras juegas:", "プレイ中はこのウィンドウを開いたままにしてください（最小化できます）。"));
    ConsoleUi.Text(L.T("les coffres de chaque partie sont comptés quelques secondes après sa fin.",
        "the chests of each match are counted a few seconds after it ends.",
        "los cofres de cada partida se cuentan unos segundos después de que termine.", "各試合の終了から数秒後に、開けた宝箱が集計されます。"));
    Console.WriteLine();
    ConsoleUi.Text(L.T("Tes réglages et ton historique sont enregistrés dans :", "Your settings and history are saved in:", "Tus ajustes y tu historial se guardan en:", "設定と履歴の保存先:"));
    ConsoleUi.Hint("  " + store.Folder);
    ConsoleUi.Hint(L.T("  (pour tout recommencer : lance ChestCounter.exe --reset-config)", "  (to start over: run ChestCounter.exe --reset-config)", "  (para empezar de cero: ejecuta ChestCounter.exe --reset-config)", "  （設定をやり直すには ChestCounter.exe --reset-config を実行してください）"));
    Console.WriteLine();
    PrintShortcuts(overlayStarted: false);
    ConsoleUi.Hint(L.T("Pour voir l'overlay en jeu, mets Fortnite en « Plein écran fenêtré » (Paramètres → Vidéo → Mode de fenêtre).",
        "To see the overlay in game, set Fortnite to \"Windowed Fullscreen\" (Settings → Video → Window Mode).",
        "Para ver el overlay en el juego: Ajustes → Vídeo → Modo ventana → «Pantalla completa en ventana».", "ゲーム中にオーバーレイを表示するには、Fortniteの「設定 → 映像 → ウィンドウモード」を「ウィンドウフルスクリーン」にしてください。"));
    ConsoleUi.WaitEnter(L.T("Appuie sur Entrée pour démarrer", "Press Enter to start", "Pulsa Intro para empezar", "Enterで開始"));
}
else
{
    store.SaveConfig(config);
    if (interactive && !once && Profiles.List(data).Count >= 2)
    {
        ConsoleUi.Screen(appName, L.T("profil", "profile", "perfil", "プロフィール"));
        ChooseProfile(data, config, store);
    }
}

// ---- Main screen ---------------------------------------------------------------------------
ConsoleUi.Screen(appName, $"v{AppInfo.VersionText}");
if (reanalyzing)
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    ConsoleUi.Text(L.T("Nouvelle version de l'analyse : les replays encore présents vont être réanalysés (sans doublon).",
        "New analysis version: the replays still on disk will be analyzed again (no duplicates).",
        "Nueva versión del análisis: las repeticiones del disco se analizarán de nuevo (sin duplicados).", "解析機能が更新されました。保存されているリプレイを再解析します（重複集計はしません）。"));
    Console.ResetColor();
    Console.WriteLine();
}

if (once)
{
    tracker.Scan(ignoreQuietPeriod: true);
    ReportWriter.WriteAll(store, config, data);
    tracker.PrintSummary();
    Console.WriteLine(L.T($"  Rapport : {store.ReportPath}", $"  Report: {store.ReportPath}", $"  Informe: {store.ReportPath}", $"  レポート: {store.ReportPath}"));
    return 0;
}

if (OperatingSystem.IsWindows() && config.ShowOverlay)
{
    NativeOverlay.SetAppearance(config.OverlaySize, config.OverlayOpacity);
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
        ConsoleUi.Text(L.T("L'ancienne version (ChestTracker.exe) est encore ouverte : ferme-la, sinon les deux comptent en même temps.",
            "The old version (ChestTracker.exe) is still open: close it, otherwise both count at the same time.",
            "La versión antigua (ChestTracker.exe) sigue abierta: ciérrala, si no las dos cuentan a la vez.", "旧バージョン（ChestTracker.exe）がまだ起動しています。同時に集計されるため、旧バージョンを閉じてください。"));
        Console.ResetColor();
        Console.WriteLine();
        foreach (var p in oldTool) p.Dispose();
    }
}

PrintStatus();
PrintShortcuts(overlayStarted: true);

// Nothing to read yet: explain why, so that a new user is not left wondering.
if (!tracker.ReplayFiles().Any())
{
    Console.ForegroundColor = ConsoleColor.DarkYellow;
    ConsoleUi.Text(Directory.Exists(config.DemosFolder)
        ? L.T("Aucun replay pour l'instant : c'est normal si tu n'as pas encore joué depuis que les replays sont activés.",
              "No replay yet: that is expected if you have not played since replays were turned on.",
              "Aún no hay repeticiones: es normal si no has jugado desde que activaste las repeticiones.", "リプレイがまだありません。リプレイを有効にしてから一度もプレイしていない場合は正常です。")
        : L.T("Le dossier des replays n'existe pas encore : Fortnite le crée à la première partie enregistrée.",
              "The replays folder does not exist yet: Fortnite creates it with the first recorded match.",
              "La carpeta de repeticiones aún no existe: Fortnite la crea con la primera partida grabada.", "リプレイフォルダーがまだありません。Fortniteで初めて試合を録画すると作成されます。"));
    ConsoleUi.Text(L.T("Vérifie dans Fortnite : Paramètres → Jeu → Replays → Enregistrer les replays.",
        "Check in Fortnite: Settings → Game → Replays → Record Replays.",
        "Compruébalo en Fortnite: Ajustes → Juego → Repeticiones → Grabar repeticiones.", "Fortniteの「設定 → ゲーム → リプレイ → リプレイを録画」を確認してください。"));
    Console.ResetColor();
    Console.WriteLine();
}

// Replays left from earlier: the report opens with a loading card, the console shows a progress bar.
var backlog = tracker.BacklogCount();
if (backlog > 1)
{
    ReportWriter.Progress = new ScanProgress(0, backlog);
}
ReportWriter.WriteAll(store, config, data);
if (config.OpenReportOnStart && !reportOpened && !auto)
{
    Open(store.ReportPath);
}
if (backlog > 0)
{
    tracker.Scan();
    ReportWriter.Progress = null;
    ReportWriter.WriteAll(store, config, data);
}
if (tracker.NewProfilesFound)
{
    tracker.NewProfilesFound = false;
    Console.ForegroundColor = ConsoleColor.DarkYellow;
    ConsoleUi.Text(L.T("Plusieurs comptes Fortnite ont enregistré des replays sur ce PC : chacun a son profil. Touche P pour choisir le tien.",
        "Several Fortnite accounts recorded replays on this PC: each one has its own profile. Press P to choose yours.",
        "Varias cuentas de Fortnite grabaron repeticiones en este PC: cada una tiene su perfil. Pulsa P para elegir el tuyo.", "このPCには複数のFortniteアカウントのリプレイがあります。アカウントごとにプロフィールを用意しています。Pキーで選択してください。"));
    Console.ResetColor();
}
tracker.PrintSummary();
UpdateOverlay(store, config, data, tracker.LiveMatch, tracker.IsLive);

Console.WriteLine();
ConsoleUi.Hint(L.T("En attente de parties… (les coffres sont comptés quelques secondes après la fin de chaque partie)",
    "Waiting for matches… (chests are counted a few seconds after the end of each match)",
    "Esperando partidas… (los cofres se cuentan unos segundos después del final de cada partida)", "試合を待っています…（各試合の終了から数秒後に宝箱を集計します）"));
Console.WriteLine();

if (auto && OperatingSystem.IsWindows())
{
    NativeConsole.Minimize(); // opened with Fortnite: the game keeps the screen
}

var quit = false;
var restart = false;
var wake = new AutoResetEvent(false);

// New version on GitHub (checked in the background; the only network access of the tool).
UpdateInfo? update = null;
var updateAnnounced = false;
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

// Opened with Fortnite: closes itself once Fortnite is closed and the last match is counted.
DateTime? autoCloseAt = null;
DateTime? fortniteClosedSince = null;
var keys = new BlockingCollection<ConsoleKeyInfo>();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; quit = true; wake.Set(); };

// Keyboard: one thread blocked on ReadKey (no polling). Menus opened from the main loop read the same queue.
if (interactive)
{
    new Thread(() =>
    {
        while (true)
        {
            try { keys.Add(Console.ReadKey(intercept: true)); wake.Set(); }
            catch { return; }
        }
    }) { IsBackground = true, Name = "Keyboard" }.Start();
    ConsoleUi.ReadKey = () => keys.Take();
}

// Replays folder: Windows tells us when a file changes, instead of us re-reading the folder all the time.
FileSystemWatcher? watcher = null;
try
{
    if (Directory.Exists(config.DemosFolder))
    {
        watcher = new FileSystemWatcher(config.DemosFolder, "*.replay")
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };
        watcher.Created += (_, _) => wake.Set();
        watcher.Changed += (_, _) => wake.Set();
        watcher.Renamed += (_, _) => wake.Set();
    }
}
catch
{
    watcher = null; // fall back to the periodic scan
}

var nextScan = DateTime.UtcNow.AddSeconds(2);
while (!quit)
{
    if (DateTime.UtcNow >= nextScan)
    {
        try
        {
            var added = tracker.Scan();
            if (added > 0)
            {
                tracker.PrintSummary();
            }
            if (tracker.NewProfilesFound)
            {
                tracker.NewProfilesFound = false;
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                ConsoleUi.Text(L.T("Plusieurs comptes ont maintenant un profil : touche P pour choisir celui à afficher.",
                    "Several accounts now have a profile: press P to choose the one to show.",
                    "Ahora varias cuentas tienen un perfil: pulsa P para elegir cuál mostrar.", "複数のアカウントのプロフィールが見つかりました。Pキーで表示するアカウントを選択してください。"));
                Console.ResetColor();
            }
            // Match in progress: re-read the replay being recorded (throttled inside).
            var liveChanged = tracker.UpdateLive();
            if (added > 0 || liveChanged)
            {
                UpdateOverlay(store, config, data, tracker.LiveMatch, tracker.IsLive);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(L.T($"Erreur pendant la surveillance : {ex.Message}", $"Error while watching: {ex.Message}", $"Error durante la vigilancia: {ex.Message}", $"監視中にエラーが発生しました: {ex.Message}"));
        }
        // A replay being written: check again soon. Otherwise only a safety scan once a minute.
        nextScan = DateTime.UtcNow.AddSeconds(tracker.IsFinishing ? 2 : tracker.HasPending ? 15 : (watcher != null && !auto ? 60 : 10));
    }

    if (auto && OperatingSystem.IsWindows())
    {
        CheckAutoClose();
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
            ConsoleUi.Text(L.T($"Nouvelle version {pendingUpdate.Version.ToString(3)} disponible (tu as la {AppInfo.VersionText}) : appuie sur U pour l'installer. Rien n'est perdu.",
                $"New version {pendingUpdate.Version.ToString(3)} available (you have {AppInfo.VersionText}): press U to install it. Nothing is lost.",
                $"Nueva versión {pendingUpdate.Version.ToString(3)} disponible (tienes la {AppInfo.VersionText}): pulsa U para instalarla. No se pierde nada.", $"新しいバージョン {pendingUpdate.Version.ToString(3)} があります（現在: {AppInfo.VersionText}）。Uキーでインストールできます。データは引き継がれます。"));
            Console.ResetColor();
            ConsoleUi.Hint(L.T($"Nouveautés : {pendingUpdate.PageUrl}", $"What's new: {pendingUpdate.PageUrl}", $"Novedades: {pendingUpdate.PageUrl}", $"更新内容: {pendingUpdate.PageUrl}"));
        }
    }

    // Sleep until something happens (file change, key press) or the next scheduled scan.
    var wait = nextScan - DateTime.UtcNow;
    if (wait > TimeSpan.Zero && wake.WaitOne(wait))
    {
        // A file change: check again within 2 s (the readiness checks decide when a replay is complete).
        if (keys.Count == 0 && !quit)
        {
            nextScan = DateTime.UtcNow.AddSeconds(2) < nextScan ? DateTime.UtcNow.AddSeconds(2) : nextScan;
        }
    }

    while (!quit && keys.TryTake(out var key))
    {
        if (autoCloseAt != null)
        {
            // Any key keeps the counter open after Fortnite.
            autoCloseAt = null;
            auto = false;
            ConsoleUi.Text(L.T("Le compteur reste ouvert.", "The counter stays open.", "El contador sigue abierto.", "カウンターを開いたままにします。"), ConsoleColor.Green);
            continue;
        }
        switch (char.ToLowerInvariant(key.KeyChar))
        {
            case 'c':
                Settings();
                DrawMain();
                ReportWriter.WriteAll(store, config, data);
                UpdateOverlay(store, config, data, tracker.LiveMatch, tracker.IsLive);
                break;
            case 'u':
                CheckAndInstallUpdate();
                break;
            case 'r': Open(store.ReportPath); break;
            case 'o':
                if (OperatingSystem.IsWindows() && NativeOverlay.IsRunning) NativeOverlay.Toggle();
                else Open(store.OverlayPath);
                break;
            case 'l':
                if (OperatingSystem.IsWindows() && NativeOverlay.IsRunning) NativeOverlay.ToggleLock();
                break;
            case 'p':
                if (Profiles.List(data).Count < 2)
                {
                    ConsoleUi.Hint(L.T("Un seul compte a enregistré des replays sur ce PC : pas d'autre profil pour l'instant.",
                        "Only one account recorded replays on this PC: no other profile yet.",
                        "Solo una cuenta ha grabado repeticiones en este PC: todavía no hay otro perfil.", "このPCでリプレイを録画したアカウントは1つだけです。他のプロフィールはまだありません。"));
                    break;
                }
                Console.WriteLine();
                ChooseProfile(data, config, store);
                ReportWriter.WriteAll(store, config, data);
                UpdateOverlay(store, config, data, tracker.LiveMatch, tracker.IsLive);
                tracker.PrintSummary();
                break;
            case 's': tracker.PrintSummary(); break;
            case 'h':
            case '?':
                Console.WriteLine();
                PrintShortcuts(overlayStarted: true);
                break;
            case 'q': quit = true; break;
        }
    }
}

watcher?.Dispose();

if (restart && AppInfo.ExePath is { } newExe)
{
    try { instance.ReleaseMutex(); } catch { }
    var restartArgs = string.Join(" ", args.Where(a => a != "--after-update").Select(a => a.Contains(' ') ? $"\"{a}\"" : a).Append("--after-update"));
    Process.Start(new ProcessStartInfo(newExe, restartArgs) { UseShellExecute = true })?.Dispose();
    return 0;
}

Console.WriteLine(L.T("  À bientôt !", "  See you soon!", "  ¡Hasta pronto!", "  またお会いしましょう！"));
return 0;

// ---- Helpers --------------------------------------------------------------------------------

void DrawMain()
{
    ConsoleUi.Screen(appName, $"v{AppInfo.VersionText}");
    PrintStatus();
    PrintShortcuts(overlayStarted: true);
}

void CheckAutoClose()
{
    var now = DateTime.UtcNow;
    if (FortniteWatch.IsFortniteRunning())
    {
        fortniteClosedSince = null;
        if (autoCloseAt != null)
        {
            autoCloseAt = null; // Fortnite started again
        }
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
        Console.WriteLine();
        ConsoleUi.Text(L.T("Fortnite est fermé : le compteur se ferme dans 30 secondes. Appuie sur une touche pour le garder ouvert.",
            "Fortnite is closed: the counter closes in 30 seconds. Press any key to keep it open.",
            "Fortnite está cerrado: el contador se cierra en 30 segundos. Pulsa una tecla para mantenerlo abierto.", "Fortniteが終了しました。カウンターは30秒後に閉じます。開いたままにするには何かキーを押してください。"), ConsoleColor.Yellow);
        nextScan = autoCloseAt.Value;
    }
    else if (now >= autoCloseAt)
    {
        quit = true;
    }
}

// U key: installs the version found at startup, otherwise asks GitHub now (a version may have come out since the
// counter was opened). Nothing is checked when updates are turned off in the settings (no network access at all).
void CheckAndInstallUpdate()
{
    var known = Volatile.Read(ref update);
    if (known == null)
    {
        if (config.UpdateMode == "off")
        {
            ConsoleUi.Hint(L.T("Mises à jour désactivées : active-les dans les réglages (C) pour que U puisse chercher sur GitHub.",
                "Updates are turned off: turn them on in the settings (C) so that U can check GitHub.",
                "Actualizaciones desactivadas: actívalas en los ajustes (C) para que U pueda buscar en GitHub.", "更新は無効です。Cキーの設定で有効にすると、UキーでGitHubの更新を確認できます。"));
            return;
        }
        ConsoleUi.Hint(L.T("Recherche d'une nouvelle version sur GitHub…", "Checking GitHub for a new version…", "Buscando una nueva versión en GitHub…", "GitHubで新しいバージョンを確認しています…"));
        var (found, reached) = Updater.CheckDetailedAsync().GetAwaiter().GetResult();
        if (found == null)
        {
            ConsoleUi.Hint(reached
                ? L.T($"Tu as déjà la dernière version ({AppInfo.VersionText}).", $"You already have the latest version ({AppInfo.VersionText}).", $"Ya tienes la última versión ({AppInfo.VersionText}).", $"既に最新バージョン（{AppInfo.VersionText}）を使用しています。")
                : L.T("GitHub est injoignable pour l'instant (pas de connexion ?) : réessaie plus tard.",
                    "GitHub cannot be reached right now (no connection?): try again later.",
                    "No se puede acceder a GitHub ahora mismo (¿sin conexión?): inténtalo más tarde.", "現在GitHubに接続できません。ネットワーク接続を確認し、後でもう一度お試しください。"));
            return;
        }
        Volatile.Write(ref update, found);
        updateAnnounced = true;
        known = found;
        ConsoleUi.Text(L.T($"Nouvelle version {found.Version.ToString(3)} trouvée (tu as la {AppInfo.VersionText}).",
            $"New version {found.Version.ToString(3)} found (you have {AppInfo.VersionText}).",
            $"Nueva versión {found.Version.ToString(3)} encontrada (tienes la {AppInfo.VersionText}).", $"新しいバージョン {found.Version.ToString(3)} が見つかりました（現在: {AppInfo.VersionText}）。"), ConsoleColor.Green);
        ConsoleUi.Hint(L.T($"Nouveautés : {found.PageUrl}", $"What's new: {found.PageUrl}", $"Novedades: {found.PageUrl}", $"更新内容: {found.PageUrl}"));
    }
    InstallUpdate(known);
}

void InstallUpdate(UpdateInfo u)
{
    Console.WriteLine();
    ConsoleUi.Text(L.T($"Téléchargement de la version {u.Version.ToString(3)}…", $"Downloading version {u.Version.ToString(3)}…", $"Descargando la versión {u.Version.ToString(3)}…", $"バージョン {u.Version.ToString(3)} をダウンロードしています…"), ConsoleColor.Cyan);
    var error = Updater.Install(u);
    if (error != null)
    {
        ConsoleUi.Text(L.T($"Mise à jour impossible : {error}", $"Update failed: {error}", $"No se pudo actualizar: {error}", $"更新に失敗しました: {error}"), ConsoleColor.Red);
        ConsoleUi.Hint(L.T($"Tu peux la télécharger à la main : {u.PageUrl}", $"You can download it manually: {u.PageUrl}", $"Puedes descargarla a mano: {u.PageUrl}", $"手動でもダウンロードできます: {u.PageUrl}"));
        return;
    }
    ConsoleUi.Text(L.T("Mise à jour installée : redémarrage du compteur…", "Update installed: restarting the counter…", "Actualización instalada: reiniciando el contador…", "更新をインストールしました。カウンターを再起動しています…"), ConsoleColor.Green);
    restart = true;
    quit = true;
}

// Settings screen (C key): everything that would otherwise need editing config.json.
void Settings()
{
    var selected = 0;
    while (true)
    {
        ConsoleUi.Screen(appName, L.T("réglages", "settings", "ajustes", "設定"));
        string YesNo(bool b) => b ? L.T("oui", "yes", "sí", "はい") : L.T("non", "no", "no", "いいえ");
        var updates = config.UpdateMode switch
        {
            "auto" => L.T("installées automatiquement", "installed automatically", "instaladas automáticamente", "自動インストール"),
            "off" => L.T("désactivées", "off", "desactivadas", "無効"),
            _ => L.T("proposées", "offered", "propuestas", "通知する"),
        };
        ConsoleUi.Hint(L.T("Ouvrir avec Fortnite : un petit processus invisible attend Fortnite et ouvre le compteur, qui se ferme après.",
            "Open with Fortnite: a tiny invisible process waits for Fortnite and opens the counter, which closes afterwards.",
            "Abrir con Fortnite: un pequeño proceso invisible espera a Fortnite y abre el contador, que se cierra después.", "Fortniteと同時に起動: 小さな非表示プロセスがFortniteの起動を待ち、カウンターを開きます。Fortnite終了後はカウンターも閉じます。"));
        ConsoleUi.Hint(L.T("Mises à jour : vérifiées sur GitHub au démarrage et avec U (le seul accès réseau de l'outil).",
            "Updates: checked on GitHub at startup and with U (the only network access of the tool).",
            "Actualizaciones: se comprueban en GitHub al iniciar y con U (único acceso a la red).", "更新: 起動時とUキーでGitHubを確認します（このツールがネットワークに接続するのは更新時のみです）。"));
        ConsoleUi.Hint(L.T("Overlay et rapport au démarrage : pris en compte au prochain lancement.",
            "Overlay and report at startup: applied at the next launch.",
            "Overlay e informe al iniciar: se aplican en el próximo inicio.", "起動時のオーバーレイとレポートの設定は、次回起動時に反映されます。"));
        ConsoleUi.Hint(L.T("Taille et opacité de l'overlay : appliquées tout de suite (Entrée pour passer à la valeur suivante).",
            "Overlay size and opacity: applied at once (Enter for the next value).",
            "Tamaño y opacidad del overlay: se aplican al instante (Intro para pasar al valor siguiente).", "オーバーレイのサイズと不透明度はすぐに反映されます（Enterで次の値に変更）。"));
        string Percent(int p) => L.T($"{p} %", $"{p}%", $"{p} %", $"{p}%");
        var items = new List<string>
        {
            $"{L.T("Langue", "Language", "Idioma", "言語")} : {L.NativeName(L.Code)}",
            $"{L.T("Ouvrir avec Fortnite", "Open with Fortnite", "Abrir con Fortnite", "Fortniteと同時に起動")} : {YesNo(config.StartWithFortnite)}",
            $"{L.T("Mises à jour", "Updates", "Actualizaciones", "更新")} : {updates}",
            $"{L.T("Overlay au démarrage", "Overlay at startup", "Overlay al iniciar", "起動時にオーバーレイを表示")} : {YesNo(config.ShowOverlay)}",
            $"{L.T("Taille de l'overlay", "Overlay size", "Tamaño del overlay", "オーバーレイのサイズ")} : {Percent(config.OverlaySize)}",
            $"{L.T("Opacité de l'overlay", "Overlay opacity", "Opacidad del overlay", "オーバーレイの不透明度")} : {Percent(config.OverlayOpacity)}",
            $"{L.T("Ouvrir le rapport au démarrage", "Open the report at startup", "Abrir el informe al iniciar", "起動時にレポートを開く")} : {YesNo(config.OpenReportOnStart)}",
            $"{L.T("Dossier des replays", "Replays folder", "Carpeta de repeticiones", "リプレイフォルダー")} : {config.DemosFolder}",
            L.T("← Retour", "← Back", "← Volver", "← 戻る"),
        };
        selected = ConsoleUi.Menu(items, selected, L.T("↑ ↓ pour choisir, Entrée pour modifier", "↑ ↓ to choose, Enter to change", "↑ ↓ para elegir, Intro para cambiar", "↑ ↓で選択、Enterで変更"));
        switch (selected)
        {
            case 0:
                config.Language = L.Codes[(Array.IndexOf(L.Codes, L.Code) + 1) % L.Codes.Length]; // fr → en → es → ja → fr
                L.Set(config.Language);
                appName = L.T("Compteur de coffres Fortnite", "Fortnite Chest Counter", "Contador de cofres de Fortnite", "Fortnite 宝箱カウンター");
                ConsoleUi.SetTitle($"{appName} {AppInfo.VersionText}");
                break;
            case 1:
                var on = !config.StartWithFortnite;
                if (OperatingSystem.IsWindows() && FortniteWatch.Apply(on, dataDir))
                {
                    config.StartWithFortnite = on;
                }
                else
                {
                    ConsoleUi.Text(L.T("Impossible ici : lance le compteur depuis ChestCounter.exe.", "Not possible here: run the counter from ChestCounter.exe.", "No es posible aquí: abre el contador desde ChestCounter.exe.", "ここでは変更できません。ChestCounter.exeからカウンターを起動してください。"), ConsoleColor.Red);
                    ConsoleUi.WaitEnter(L.T("Entrée pour continuer", "Enter to continue", "Intro para continuar", "Enterで続行"));
                }
                break;
            case 2:
                config.UpdateMode = config.UpdateMode switch { "prompt" => "auto", "auto" => "off", _ => "prompt" };
                break;
            case 3:
                config.ShowOverlay = !config.ShowOverlay;
                break;
            case 4:
                config.OverlaySize = OverlayAppearance.Next(OverlayAppearance.Sizes, config.OverlaySize);
                ApplyOverlayAppearance();
                break;
            case 5:
                config.OverlayOpacity = OverlayAppearance.Next(OverlayAppearance.Opacities, config.OverlayOpacity);
                ApplyOverlayAppearance();
                break;
            case 6:
                config.OpenReportOnStart = !config.OpenReportOnStart;
                break;
            case 7:
                config.DemosFolder = ConsoleUi.Input(L.T("Nouveau dossier (Entrée = inchangé) : ", "New folder (Enter = unchanged): ", "Nueva carpeta (Intro = sin cambios): ", "新しいフォルダー（Enterのみで変更なし）: "), config.DemosFolder);
                break;
            default:
                store.SaveConfig(config);
                return;
        }
        store.SaveConfig(config);
    }
}

// Shows the new size / opacity on the overlay right away (it stays visible above the console).
void ApplyOverlayAppearance()
{
    if (OperatingSystem.IsWindows() && NativeOverlay.IsRunning)
    {
        NativeOverlay.SetAppearance(config.OverlaySize, config.OverlayOpacity);
    }
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

void PrintStatus()
{
    var profile = Profiles.Current(data, config);
    var demosNote = Directory.Exists(config.DemosFolder) ? "" : L.T("  (introuvable pour l'instant)", "  (not found yet)", "  (aún no encontrada)", "  （まだ見つかりません）");
    ConsoleUi.Text(L.T($"Profil   : {profile?.Name ?? "créé automatiquement à ta première partie"}",
        $"Profile  : {profile?.Name ?? "created automatically with your first match"}",
        $"Perfil       : {profile?.Name ?? "se crea automáticamente con tu primera partida"}", $"プロフィール: {profile?.Name ?? "最初の試合で自動作成されます"}"));
    ConsoleUi.Text($"{L.T("Replays  : ", "Replays  : ", "Repeticiones : ", "リプレイ: ")}{config.DemosFolder}{demosNote}");
    ConsoleUi.Text(L.T($"Données  : {store.Folder}", $"Data     : {store.Folder}", $"Datos        : {store.Folder}", $"データ: {store.Folder}"));
    Console.WriteLine();
}

// What each shortcut does, in plain words.
static void PrintShortcuts(bool overlayStarted)
{
    var hotkeys = !overlayStarted || !OperatingSystem.IsWindows() || !NativeOverlay.IsRunning || NativeOverlay.HotkeyRegistered;
    var lockKey = !overlayStarted || !OperatingSystem.IsWindows() || !NativeOverlay.IsRunning || NativeOverlay.LockHotkeyRegistered;

    void Key(string key, string text)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write($"    {key,-14}");
        Console.ResetColor();
        Console.WriteLine(text);
    }

    ConsoleUi.Text(L.T("Raccourcis (fonctionnent partout, même en jeu)", "Shortcuts (work anywhere, even in game)", "Atajos (funcionan en cualquier sitio, incluso en el juego)", "ショートカット（ゲーム中も使用できます）"), ConsoleColor.White);
    Key(L.T("Ctrl+Maj+O", "Ctrl+Shift+O", "Ctrl+Mayús+O", "Ctrl+Shift+O"), L.T("afficher / masquer l'overlay", "show / hide the overlay", "mostrar / ocultar el overlay", "オーバーレイを表示 / 非表示")
        + (hotkeys ? "" : L.T("  (indisponible : déjà pris par un autre programme, utilise O ici)", "  (unavailable: used by another program, use O here)", "  (no disponible: lo usa otro programa, usa O aquí)", "  （他のプログラムが使用中です。このウィンドウでOキーを押してください）")));
    Key(L.T("Ctrl+Maj+L", "Ctrl+Shift+L", "Ctrl+Mayús+L", "Ctrl+Shift+L"), L.T("verrouiller / déverrouiller l'overlay", "lock / unlock the overlay", "bloquear / desbloquear el overlay", "オーバーレイを固定 / 固定解除")
        + (lockKey ? "" : L.T("  (indisponible : déjà pris par un autre programme, utilise L ici)", "  (unavailable: used by another program, use L here)", "  (no disponible: lo usa otro programa, usa L aquí)", "  （他のプログラムが使用中です。このウィンドウでLキーを押してください）")));
    ConsoleUi.Hint(L.T("                verrouillé : la souris passe au travers et il ne bouge plus (idéal en jeu)",
        "                locked: the mouse goes through it and it cannot move (best in game)",
        "                bloqueado: el ratón lo atraviesa y no se mueve (ideal en el juego)", "                固定時: マウス操作が背面に届き、位置を動かせません（プレイ中におすすめ）"));
    ConsoleUi.Hint(L.T("                déverrouillé : clic gauche maintenu pour le déplacer, clic droit pour le masquer",
        "                unlocked: hold the left mouse button to move it, right-click to hide it",
        "                desbloqueado: arrástralo con el clic izquierdo, clic derecho para ocultarlo", "                固定解除時: 左ドラッグで移動、右クリックで非表示"));
    Console.WriteLine();
    ConsoleUi.Text(L.T("Touches dans cette fenêtre", "Keys in this window", "Teclas en esta ventana", "このウィンドウ内のキー操作"), ConsoleColor.White);
    Key("R", L.T("ouvrir le rapport (statistiques détaillées)", "open the report (detailed statistics)", "abrir el informe (estadísticas detalladas)", "レポートを開く（詳しい統計）"));
    Key("O  /  L", L.T("afficher-masquer  /  verrouiller-déverrouiller l'overlay", "show-hide  /  lock-unlock the overlay", "mostrar-ocultar  /  bloquear-desbloquear el overlay", "オーバーレイを表示・非表示 / 固定・固定解除"));
    Key("P", L.T("changer de profil (si plusieurs comptes jouent sur ce PC)", "switch profile (if several accounts play on this PC)", "cambiar de perfil (si varias cuentas juegan en este PC)", "プロフィールを切り替える（このPCで複数のアカウントを使用する場合）"));
    Key("C", L.T("réglages (langue, ouvrir avec Fortnite, mises à jour…)", "settings (language, open with Fortnite, updates…)", "ajustes (idioma, abrir con Fortnite, actualizaciones…)", "設定（言語、Fortniteと同時に起動、更新など）"));
    Key("U", L.T("chercher / installer une nouvelle version", "check for / install a new version", "buscar / instalar una nueva versión", "新しいバージョンを確認 / インストール"));
    Key("S", L.T("résumé", "summary", "resumen", "集計結果"));
    Key("H", L.T("revoir cette aide", "show this help again", "volver a ver esta ayuda", "このヘルプを表示"));
    Key("Q", L.T("quitter", "quit", "salir", "終了"));
    Console.WriteLine();
}

// Several accounts recorded replays on this PC: which one is played now (the overlay and the report show it).
static void ChooseProfile(TrackerData d, Config c, Store s)
{
    var list = Profiles.List(d);
    var current = Math.Max(0, list.FindIndex(p => p.Key == Profiles.Current(d, c)?.Key));
    ConsoleUi.Text(L.T("Plusieurs comptes Fortnite ont enregistré des replays sur ce PC.", "Several Fortnite accounts recorded replays on this PC.", "Varias cuentas de Fortnite han grabado repeticiones en este PC.", "このPCには複数のFortniteアカウントのリプレイがあります。"));
    ConsoleUi.Text(L.T("Sur quel compte joues-tu ?", "Which account are you playing on?", "¿Con qué cuenta juegas?", "どのアカウントでプレイしますか？"), ConsoleColor.White);
    ConsoleUi.Hint(L.T("Chaque compte garde ses propres statistiques. Tu pourras changer à tout moment avec la touche P.",
        "Each account keeps its own statistics. You can switch at any time with the P key.",
        "Cada cuenta conserva sus propias estadísticas. Puedes cambiar cuando quieras con la tecla P.", "統計はアカウントごとに保存されます。Pキーでいつでも切り替えられます。"));
    var choice = ConsoleUi.Menu(list.Select(Profiles.Describe).ToList(), current);
    c.Profile = list[choice].Key;
    try { s.SaveConfig(c); } catch { /* ignore */ }
    ConsoleUi.Text(L.T($"Profil affiché : {list[choice].Name}", $"Profile shown: {list[choice].Name}", $"Perfil mostrado: {list[choice].Name}", $"表示中のプロフィール: {list[choice].Name}"), ConsoleColor.Green);
    Console.WriteLine();
}

static void UpdateOverlay(Store store, Config config, TrackerData d, MatchRecord? live, bool inProgress)
{
    var view = OverlayModel.Build(Profiles.View(d, config), live, inProgress);
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
        Console.WriteLine(L.T($"  Ouvre ce fichier dans ton navigateur : {path}", $"  Open this file in your browser: {path}", $"  Abre este archivo en tu navegador: {path}", $"  次のファイルをブラウザーで開いてください: {path}"));
    }
}
