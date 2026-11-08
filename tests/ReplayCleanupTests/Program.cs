using System.Security.Cryptography;
using ChestCounter;

var root = Path.Combine(Path.GetTempPath(), "chest-cleanup-test-" + Guid.NewGuid());
Directory.CreateDirectory(root);
try
{
    var config = new Config { DemosFolder = root };
    var data = new TrackerData { AnalyzerVersion = ReplayAnalyzer.CurrentVersion };
    var now = DateTime.UtcNow;
    var files = new List<FileInfo>();
    for (var i = 0; i < 14; i++)
    {
        var path = Path.Combine(root, $"replay{i}.replay");
        using (var b = new BinaryWriter(File.Create(path)))
        {
            b.Write(0x1CA2E27Fu); b.Write(6u); b.Write(1u); b.Write(1u); b.Write(1u);
            b.Write(0); b.Write(0u); b.Write(0L); b.Write(0u); b.Write(0u);
        }
        File.SetLastWriteTimeUtc(path, now.AddDays(-20).AddMinutes(i));
        var f = new FileInfo(path); files.Add(f);
        var session = "session" + i;
        data.Matches.Add(new MatchRecord { SessionId = session, ReplayFile = f.Name, AnalyzerVersion = ReplayAnalyzer.CurrentVersion });
        data.ProcessedFiles[Tracker.FileKey(f)] = session;
        data.ReplayEvidence[Tracker.FileKey(f)] = new ReplayEvidence { SessionId = session, Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) };
    }
    void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    Check(ReplayCleanup.Candidates(config, data, now).Count == 4, "Keep newest ten");
    var first = files[0]; var key = Tracker.FileKey(first); var evidence = data.ReplayEvidence[key];
    data.ReplayEvidence.Remove(key);
    data.ProcessedFiles[key] = "session0";
    Check(!ReplayCleanup.Eligible(first, config, data, now), "ProcessedFiles alone must not qualify");
    data.ReplayEvidence[key] = evidence;
    data.Matches.RemoveAt(0);
    Check(!ReplayCleanup.Eligible(first, config, data, now), "Missing history");
    data.Matches.Insert(0, new MatchRecord { SessionId = "session0", ReplayFile = first.Name, AnalyzerVersion = ReplayAnalyzer.CurrentVersion });
    data.ProcessedFiles[key] = "error: failed";
    Check(!ReplayCleanup.Eligible(first, config, data, now), "Failed reanalysis");
    data.ProcessedFiles[key] = "session0";
    data.AnalyzerVersion--;
    Check(!ReplayCleanup.Eligible(first, config, data, now), "Pending analyzer upgrade");
    data.AnalyzerVersion++;
    config.ProtectedReplayPaths.Add(root);
    Check(!ReplayCleanup.Eligible(first, config, data, now), "Protected folder");
    config.ProtectedReplayPaths.Clear(); config.ProtectedReplayPaths.Add(first.FullName);
    Check(!ReplayCleanup.Eligible(first, config, data, now), "Protected file");
    config.ProtectedReplayPaths.Clear();
    using (var locked = new FileStream(first.FullName, FileMode.Open, FileAccess.Read, FileShare.None))
        Check(!ReplayCleanup.Eligible(first, config, data, now), "File in use");
    evidence.Sha256 = new string('0', 64);
    Check(!ReplayCleanup.Eligible(first, config, data, now), "Changed content");
    evidence.Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(first.FullName)));
    Check(!ReplayCleanup.Eligible(first, config, data, now.AddDays(-14)), "Recent file");
    config.ReplaysToKeep = 13;
    Check(ReplayCleanup.Candidates(config, data, now).Count == 1, "Configurable retention");
    config.ReplaysToKeep = 10;
    Check(!new Config().AutoCleanupReplays, "Automatic cleanup defaults off");
    if (OperatingSystem.IsWindows())
    {
        var newest = files[^1];
        Check(ReplayCleanup.DeletePermanent(newest, config, data) == 0 && File.Exists(newest.FullName), "Newest is retained");
        using (var locked = new FileStream(first.FullName, FileMode.Open, FileAccess.Read, FileShare.None))
            Check(ReplayCleanup.DeletePermanent(first, config, data) == 0, "Locked replay is retained");
        var second = files[1];
        var size = second.Length;
        Check(ReplayCleanup.DeletePermanent(second, config, data) == size && !File.Exists(second.FullName), "Eligible file is deleted");
    }
    using (var stream = new FileStream(first.FullName, FileMode.Open, FileAccess.Write)) { stream.Position = 24; stream.WriteByte(1); }
    first.Refresh(); data.ReplayEvidence[Tracker.FileKey(first)] = new ReplayEvidence { SessionId = "session0", Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(first.FullName))) };
    Check(!ReplayCleanup.Eligible(first, config, data, now), "Live file");
    if (OperatingSystem.IsWindows())
    {
        var store = new Store(Path.Combine(root, "data"));
        foreach (var f in Directory.EnumerateFiles(root, "*.replay").Select(path => new FileInfo(path)))
            if (!data.ProcessedFiles.ContainsKey(Tracker.FileKey(f))) data.ProcessedFiles[Tracker.FileKey(f)] = "incomplete";
        store.SaveData(data);
        var tracker = new Tracker(store, config, data);
        var before = Directory.GetFiles(root, "*.replay").Length;
        tracker.Scan();
        Check(Directory.GetFiles(root, "*.replay").Length == before, "Disabled cleanup must not delete");
        config.AutoCleanupReplays = true;
        tracker.Scan();
        Check(Directory.GetFiles(root, "*.replay").Length < before, "Enabled cleanup runs during a scan");
        Check(store.LoadData().Matches.Count == 14, "Persisted history must remain intact");
    }
    Check(data.Matches.Count == 14, "History must remain intact");
    Console.WriteLine("Replay cleanup safety checks passed.");
}
finally { Directory.Delete(root, true); }
