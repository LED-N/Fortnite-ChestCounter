using System.Security.Cryptography;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ChestCounter;

/// <summary>Opt-in replay cleanup. Never changes history or auxiliary files.</summary>
public static class ReplayCleanup
{
    public static List<FileInfo> Candidates(Config config, TrackerData persisted, DateTime now)
    {
        if (!Directory.Exists(config.DemosFolder)) return new();
        var files = new DirectoryInfo(config.DemosFolder).EnumerateFiles("*.replay", System.IO.SearchOption.TopDirectoryOnly)
            .OrderByDescending(f => f.LastWriteTimeUtc).ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList();
        return files.Skip(Math.Max(10, config.ReplaysToKeep)).Where(f => Eligible(f, config, persisted, now)).ToList();
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
            if (persisted.AnalyzerVersion < ReplayAnalyzer.CurrentVersion ||
                !persisted.ReplayEvidence.TryGetValue(key, out var evidence) || string.IsNullOrEmpty(evidence.SessionId) ||
                !persisted.ProcessedFiles.TryGetValue(key, out var session) || session != evidence.SessionId ||
                !persisted.Matches.Any(m => m.SessionId == evidence.SessionId && m.ReplayFile == file.Name &&
                    m.IsBattleRoyale && m.AnalyzerVersion >= ReplayAnalyzer.CurrentVersion)) return false;
            if (ReplayHeaderProbe.IsLive(file.FullName) != false || ReplayHeaderProbe.KeyMissing(file.FullName)) return false;
            using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.None);
            var digest = Convert.ToHexString(SHA256.HashData(stream));
            file.Refresh();
            return file.Exists && Tracker.FileKey(file) == key && digest == evidence.Sha256;
        }
        catch { return false; }
    }

    // Open with DELETE access and no sharing, then validate and delete through the same handle.
    // This prevents a replay being opened, replaced or renamed between verification and deletion.
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security,
        uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int infoClass,
        ref FileDispositionInfo info, uint size);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileDispositionInfo { [MarshalAs(UnmanagedType.U1)] public bool DeleteFile; }

    public static long DeletePermanent(FileInfo candidate, Config config, TrackerData persisted)
    {
        if (!OperatingSystem.IsWindows()) return 0;
        try
        {
            // Preserve the identity presented to the user or selected by the scan.
            var expectedKey = Tracker.FileKey(candidate);
            // Refresh both the retention window and the persisted-analysis checks.
            var current = Candidates(config, persisted, DateTime.UtcNow)
                .FirstOrDefault(f => string.Equals(f.FullName, candidate.FullName, StringComparison.OrdinalIgnoreCase));
            if (current == null) return 0;
            var key = Tracker.FileKey(current);
            if (key != expectedKey) return 0;
            var evidence = persisted.ReplayEvidence[key];
            const uint genericRead = 0x80000000, delete = 0x00010000, openExisting = 3;
            using var handle = CreateFileW(current.FullName, genericRead | delete, 0, IntPtr.Zero, openExisting, 0, IntPtr.Zero);
            if (handle.IsInvalid) return 0;
            using var stream = new FileStream(handle, FileAccess.Read);
            var digest = Convert.ToHexString(SHA256.HashData(stream));
            current.Refresh();
            if (!current.Exists || Tracker.FileKey(current) != key || digest != evidence.Sha256) return 0;
            var length = stream.Length;
            var disposition = new FileDispositionInfo { DeleteFile = true };
            return SetFileInformationByHandle(handle, 4, ref disposition, 1) ? length : 0;
        }
        catch { return 0; }
    }
}
