namespace Unreal.Core.Models;

/// <summary>
/// see https://github.com/EpicGames/UnrealEngine/blob/70bc980c6361d9a7d23f6d23ffe322a2d6ef16fb/Engine/Source/Runtime/NetworkReplayStreaming/LocalFileNetworkReplayStreaming/Public/LocalFileNetworkReplayStreaming.h#L88
/// </summary>
public class ReplayInfo
{
    public uint LengthInMs { get; set; }
    public uint NetworkVersion { get; set; }
    public uint Changelist { get; set; }
    public string FriendlyName { get; set; }
    public DateTime Timestamp { get; set; }
    public long TotalDataSizeInBytes { get; set; }
    public bool IsLive { get; set; }
    public bool IsCompressed { get; set; }
    public bool IsEncrypted { get; set; }
    public ReadOnlyMemory<byte> EncryptionKey { get; set; }
    /// <summary>Encrypted recordings use a nonzero AES key; an all-zero key is an unfinished placeholder.</summary>
    public bool HasValidEncryptionKey
    {
        get
        {
            if (!IsEncrypted) return true;
            if (EncryptionKey.Length is not (16 or 24 or 32)) return false;
            foreach (var value in EncryptionKey.Span)
            {
                if (value != 0) return true;
            }
            return false;
        }
    }
    public ReplayVersionHistory FileVersion { get; set; }
}
