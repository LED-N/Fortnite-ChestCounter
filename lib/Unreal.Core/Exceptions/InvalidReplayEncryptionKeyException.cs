namespace Unreal.Core.Exceptions;

/// <summary>The replay has no usable key yet; changing AES padding cannot recover its data.</summary>
public sealed class InvalidReplayEncryptionKeyException : InvalidReplayException
{
    public InvalidReplayEncryptionKeyException()
        : base("Replay encryption key is missing or invalid (empty, all zeros, or unsupported AES key length). The replay cannot be decrypted until a valid key is written.") { }
}
