using System.Security.Cryptography;
using Microsoft.VisualBasic.FileIO;

namespace ChestCounter;

/// <summary>Manual, opt-in cleanup. Never changes history or auxiliary files.</summary>
public static class ReplayCleanup
{
    public static List<FileInfo> Candidates(Config config, TrackerData persisted, DateTime now)
    {
        if (!Directory.Exists(config.DemosFolder)) return new();
        var files = new DirectoryInfo(config.DemosFolder).EnumerateFiles("*.replay", System.IO.SearchOption.TopDirectoryOnly)
            .OrderByDescending(f => f.LastWriteTimeUtc).ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList();
        return files.Skip(10).Where(f => Eligible(f, config, persisted, now)).ToList();
    }

    private static bool Protected(string path, IEnumerable<string> paths)
    {
        foreach (var entry in paths)
        {
            if (string.IsNullOrWhiteSpace(entry)) continue;
            try
            {
                var full = Path.GetFullPath(entry).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (path.Equals(full, StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith(full + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch { return true; } // Invalid configuration fails closed.
        }
        return false;
    }

    public static bool Eligible(FileInfo file, Config config, TrackerData persisted, DateTime now)
    {
        try
        {
            file.Refresh();
            if (!file.Exists || file.LastWriteTimeUtc > now.AddDays(-7) ||
                (file.Attributes & (FileAttributes.ReparsePoint | FileAttributes.ReadOnly)) != 0 ||
                Protected(file.FullName, config.ProtectedReplayPaths)) return false;
            // Do not traverse directory junctions or accept files outside the configured directory.
            var dir = new DirectoryInfo(Path.GetFullPath(config.DemosFolder));
            if (!string.Equals(file.DirectoryName, dir.FullName, StringComparison.OrdinalIgnoreCase)) return false;
            for (var parent = dir; parent != null; parent = parent.Parent)
                if ((parent.Attributes & FileAttributes.ReparsePoint) != 0) return false;
            var key = Tracker.FileKey(file);
            if (!persisted.ReplayEvidence.TryGetValue(key, out var evidence) || string.IsNullOrEmpty(evidence.SessionId) ||
                !persisted.Matches.Any(m => m.SessionId == evidence.SessionId && m.ReplayFile == file.Name && m.IsBattleRoyale)) return false;
            if (ReplayHeaderProbe.IsLive(file.FullName) != false || ReplayHeaderProbe.KeyMissing(file.FullName)) return false;
            using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.None);
            var digest = Convert.ToHexString(SHA256.HashData(stream));
            file.Refresh();
            return file.Exists && Tracker.FileKey(file) == key && digest == evidence.Sha256;
        }
        catch { return false; }
    }

    public static bool Recycle(FileInfo candidate, Config config, TrackerData persisted)
    {
        if (!OperatingSystem.IsWindows()) return false;
        // Recompute the protected newest ten and all other checks after confirmation.
        if (!Candidates(config, persisted, DateTime.UtcNow).Any(f => f.FullName == candidate.FullName)) return false;
        try
        {
            FileSystem.DeleteFile(candidate.FullName, UIOption.AllDialogs, RecycleOption.SendToRecycleBin, UICancelOption.ThrowException);
            return !File.Exists(candidate.FullName);
        }
        catch { return false; }
    }
}
