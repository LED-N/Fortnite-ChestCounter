using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using ChestCounter;
using FortniteReplayReader.Models;
using Unreal.Core;
using Unreal.Core.Exceptions;
using Unreal.Core.Models;

var failures = new List<string>();
var passed = 0;
void Check(string name, Action action)
{
    try { action(); Console.WriteLine($"PASS {name}"); passed++; }
    catch (Exception e) { failures.Add(name); Console.WriteLine($"FAIL {name}: {e.GetBaseException().Message}"); }
}
void Assert(bool value, string message) { if (!value) throw new Exception(message); }

byte[] Header(byte[] key, bool live = false, bool encrypted = true)
{
    using var ms = new MemoryStream();
    using var w = new System.IO.BinaryWriter(ms, Encoding.UTF8, true);
    w.Write(0x1CA2E27Fu); w.Write(7u); w.Write(0); // custom versions
    w.Write(1000u); w.Write(0u); w.Write(0u);
    w.Write(1); w.Write((byte)0); // FString
    w.Write(live ? 1u : 0u); w.Write(DateTime.UtcNow.ToBinary());
    w.Write(0u); w.Write(encrypted ? 1u : 0u); w.Write(key.Length); w.Write(key);
    return ms.ToArray();
}

foreach (var live in new[] { false, true })
foreach (var key in new[] { Array.Empty<byte>(), new byte[32], new byte[] { 1, 2, 3 } })
    Check($"Reject missing/invalid header key live={live} length={key.Length}", () =>
    {
        var reader = new ProbeReader();
        using var archive = new Unreal.Core.BinaryReader(Header(key, live).AsMemory());
        try { reader.ReadReplayInfo(archive); throw new Exception("Invalid encryption key was accepted"); }
        catch (InvalidReplayException e) { Assert(e.Message.Contains("key", StringComparison.OrdinalIgnoreCase), "Explain missing encryption key"); }
    });

var validKey = Enumerable.Range(1, 32).Select(x => (byte)x).ToArray();
Check("Accept valid encrypted header", () =>
{
    var reader = new ProbeReader();
    using var archive = new Unreal.Core.BinaryReader(Header(validKey).AsMemory());
    reader.ReadReplayInfo(archive);
});
Check("Accept unencrypted header without key", () =>
{
    var reader = new ProbeReader();
    using var archive = new Unreal.Core.BinaryReader(Header(Array.Empty<byte>(), encrypted: false).AsMemory());
    reader.ReadReplayInfo(archive);
});
Check("AES ECB PKCS7 valid payload round trip", () =>
{
    var plain = Encoding.UTF8.GetBytes("Replay payload with valid encryption key");
    using var aes = Aes.Create(); aes.Key = validKey;
    var encrypted = aes.EncryptEcb(plain, PaddingMode.PKCS7);
    var reader = new ProbeReader(); reader.SetInfo(true, validKey);
    using var input = new Unreal.Core.BinaryReader(encrypted.AsMemory());
    using var output = reader.Decrypt(input, encrypted.Length);
    Assert(output.ReadBytes(plain.Length).SequenceEqual(plain), "Valid encrypted payload changed");
});
Check("Unencrypted payload passes through", () =>
{
    var plain = new byte[] { 1, 2, 3, 4, 5 };
    var reader = new ProbeReader(); reader.SetInfo(false, Array.Empty<byte>());
    using var input = new Unreal.Core.BinaryReader(plain.AsMemory());
    using var output = reader.Decrypt(input, plain.Length);
    Assert(output.ReadBytes(plain.Length).SequenceEqual(plain), "Unencrypted payload changed");
});
Check("Decrypt guard rejects zero key before AES", () =>
{
    var reader = new ProbeReader(); reader.SetInfo(true, new byte[32]);
    using var input = new Unreal.Core.BinaryReader(new byte[16].AsMemory());
    try { using var output = reader.Decrypt(input, 16); throw new Exception("Zero key reached AES"); }
    catch (InvalidReplayException) { Assert(input.Position == 0, "Guard consumed encrypted bytes"); }
});

var testFolder = Path.Combine(Path.GetTempPath(), "ChestCounterRegression-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(testFolder);
try
{
    var demos = Path.Combine(testFolder, "Demos"); Directory.CreateDirectory(demos);
    var store = new Store(Path.Combine(testFolder, "Data"));
    var config = new Config { DemosFolder = demos, Language = "en" };
    L.Set("en");
    var tracker = new Tracker(store, config, new TrackerData());
    var isReady = typeof(Tracker).GetMethod("IsReady", BindingFlags.NonPublic | BindingFlags.Instance)!;
    bool Ready(FileInfo f, bool once = false) => (bool)isReady.Invoke(tracker, new object[] { f, once })!;
    FileInfo Fixture(string name, byte[] bytes, TimeSpan age)
    {
        var path = Path.Combine(demos, name + ".replay"); File.WriteAllBytes(path, bytes);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow - age); return new FileInfo(path);
    }
    Check("Fresh finalized zero-key replay remains pending, even --once", () =>
    {
        var f = Fixture("fresh", Header(new byte[32]), TimeSpan.FromSeconds(15));
        Assert(!Ready(f) && !Ready(f) && !Ready(f, true), "Missing key should wait for finalization");
    });
    Check("Old finalized zero-key replay is skipped with key explanation", () =>
    {
        var f = Fixture("old", Header(new byte[32]), TimeSpan.FromMinutes(2));
        Assert(Ready(f), "Old missing-key replay must not remain pending forever");
        var process = typeof(Tracker).GetMethod("Process", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var result = process.Invoke(tracker, new object[] { f })!;
        var skipped = (string?)result.GetType().GetProperty("Skipped")!.GetValue(result);
        Assert(skipped?.Contains("key", StringComparison.OrdinalIgnoreCase) == true, "Expected missing-key skip explanation");
        Assert(tracker.Data.ProcessedFiles[Tracker.FileKey(f)] == "missing-key", "Missing key must not be stored as padding error");
        Assert(tracker.UnprocessedFiles().All(x => x.Name != f.Name), "Skipped file should not retry forever");
        File.SetLastWriteTimeUtc(f.FullName, DateTime.UtcNow); f.Refresh();
        Assert(tracker.UnprocessedFiles().Any(x => x.Name == f.Name), "Updated file must be eligible again");
    });
    Check("Valid finalized replay is ready after stable quiet period", () =>
    {
        var f = Fixture("valid", Header(validKey), TimeSpan.FromSeconds(15));
        Assert(!Ready(f) && Ready(f), "Valid completed file should need two stable checks");
    });
    Check("Old live replay without key is skipped", () =>
    {
        var f = Fixture("abandoned", Header(new byte[32], live: true), TimeSpan.FromMinutes(11));
        Assert(Ready(f), "Abandoned recording should be processed as incomplete");
    });
    Check("Upgrade re-evaluates prior padding errors once and preserves explicit skips", () =>
    {
        var data = new TrackerData { AnalyzerVersion = 10 };
        data.ProcessedFiles["bad"] = "error: Padding is invalid and cannot be removed.";
        data.ProcessedFiles["skip"] = "skipped";
        var upgrade = new Tracker(store, config, data);
        upgrade.UpgradeIfNeeded();
        Assert(!data.ProcessedFiles.ContainsKey("bad"), "Old padding error was not reset");
        Assert(data.ProcessedFiles["skip"] == "skipped", "Explicit user skip must be preserved");
        data.ProcessedFiles["new"] = "missing-key"; upgrade.UpgradeIfNeeded();
        Assert(data.ProcessedFiles.ContainsKey("new"), "Repeated upgrade should not reset results");
    });
}
finally { Directory.Delete(testFolder, recursive: true); }

// The Japanese suite is discovered so this runner can also establish the key-validation baseline independently.
var japanese = Assembly.GetExecutingAssembly().GetTypes().FirstOrDefault(t => t.Name == "JapaneseRegression");
if (japanese != null) Check("Japanese localization", () => japanese.GetMethod("Run")!.Invoke(null, null));
var consoleWidth = Assembly.GetExecutingAssembly().GetTypes().FirstOrDefault(t => t.Name == "ConsoleWidthRegression");
if (consoleWidth != null) Check("Japanese console display width", () => consoleWidth.GetMethod("Run")!.Invoke(null, null));

var realIndex = Array.IndexOf(args, "--real-replays");
if (realIndex >= 0 && realIndex + 1 < args.Length)
foreach (var file in Directory.EnumerateFiles(args[realIndex + 1], "*.replay")
    .Where(file => ReplayHeaderProbe.IsLive(file) == false && ReplayHeaderProbe.KeyMissing(file)))
    Check($"Real replay missing key is rejected before events: {Path.GetFileName(file)}", () =>
    {
        // Only completed snapshots with missing keys are selected; running recordings are excluded.
        using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var ms = new MemoryStream(); fs.CopyTo(ms); var bytes = ms.ToArray();
        var reader = new ProbeReader();
        using var archive = new Unreal.Core.BinaryReader(bytes.AsMemory());
        try { reader.ReadReplayInfo(archive); throw new Exception("Real missing key accepted"); }
        catch (InvalidReplayEncryptionKeyException) { }
    });

var artifactIndex = Array.IndexOf(args, "--artifacts");
if (artifactIndex >= 0 && artifactIndex + 1 < args.Length)
    Check("Generate Japanese report and preview data", () =>
    {
        L.Set("ja");
        var artifacts = new Store(Path.GetFullPath(args[artifactIndex + 1]));
        var config = new Config { Language = "ja", UpdateMode = "off", OpenReportOnStart = false,
            ShowOverlay = false, DemosFolder = Path.Combine(artifacts.Folder, "Demos") };
        Directory.CreateDirectory(config.DemosFolder);
        var data = new TrackerData { AnalyzerVersion = ReplayAnalyzer.CurrentVersion };
        var match = new MatchRecord { PlayerName = "サンプルプレイヤー", EpicId = "preview-player", SessionId = "preview-match",
            MatchStartUtc = new DateTime(2026, 11, 8, 2, 0, 0, DateTimeKind.Utc), Placement = 3, TotalPlayers = 100,
            DurationSeconds = 1200, ChestsOpened = 18, AmmoBoxesOpened = 4, Eliminations = 5,
            Playlist = "Playlist_DefaultSolo", AnalyzerVersion = ReplayAnalyzer.CurrentVersion };
        match.Chests.Add(new ChestRecord { Kind = ChestKinds.Normal, TimeSeconds = 61, Blueprint = "Tiered_Chest" });
        match.Chests.Add(new ChestRecord { Kind = ChestKinds.Rare, TimeSeconds = 145, Destroyed = true, Blueprint = "RareChest" });
        match.TreasureFinds.Add(new TreasureFind { Item = "Feu d'artifice doré", SourceKind = ChestKinds.Rare,
            ObtainedAtSeconds = 145, SourceCertain = true, TimeExact = true, FromGoldenChest = true });
        data.Matches.Add(match);
        artifacts.SaveConfig(config); artifacts.SaveData(data);
        ReportWriter.WriteAll(artifacts, config, data);
        Assert(File.ReadAllText(artifacts.ReportPath).Contains("宝箱カウンター"), "Japanese artifact missing");
    });
Console.WriteLine($"{passed} passed, {failures.Count} failed");
return failures.Count == 0 ? 0 : 1;

sealed class ProbeReader : FortniteReplayReader.ReplayReader
{
    public ProbeReader() { Replay = new FortniteReplay { Info = new ReplayInfo(), Header = new ReplayHeader() }; }
    public void SetInfo(bool encrypted, byte[] key) { Replay.Info.IsEncrypted = encrypted; Replay.Info.EncryptionKey = key; }
    public FArchive Decrypt(FArchive input, int size) => DecryptBuffer(input, size);
}
