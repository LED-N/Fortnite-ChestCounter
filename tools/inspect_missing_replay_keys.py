"""Read-only investigation of incomplete encrypted replays. Never print key material.

Requires cryptography (present in the investigation environment).
The optional sliding scan only tests 32-byte keys already present in the file;
it is not a search of the AES key space.
"""
import argparse
import collections
import hashlib
import json
from pathlib import Path
import struct
import time

from cryptography.hazmat.primitives.ciphers import Cipher, algorithms, modes


def fstring(data, position):
    length = struct.unpack_from("<i", data, position)[0]
    position += 4
    size = length if length >= 0 else -2 * length
    if size > 8192 or position + size > len(data):
        raise ValueError("Invalid FString bounds")
    value = data[position:position + size].decode("utf-8" if length >= 0 else "utf-16le")
    return value.rstrip("\0"), position + size


def inspect(path):
    data = path.read_bytes()
    magic, version = struct.unpack_from("<II", data)
    if magic != 0x1CA2E27F or version != 7:
        raise ValueError("This probe validates the version-7 files observed in this case")
    count = struct.unpack_from("<i", data, 8)[0]
    position = 12 + 20 * count
    duration, network, changelist = struct.unpack_from("<III", data, position)
    _, position = fstring(data, position + 12)
    live = struct.unpack_from("<I", data, position)[0]
    position += 12  # live and timestamp
    compressed, encrypted, size = struct.unpack_from("<III", data, position)
    key = data[position + 12:position + 12 + size]
    position += 12 + size
    events = []
    counts = collections.Counter()
    invalid = None
    while position + 8 <= len(data):
        kind, chunk_size = struct.unpack_from("<Ii", data, position)
        start = position + 8
        end = start + chunk_size
        if kind > 3 or chunk_size <= 0 or end > len(data):
            invalid = {"chunk_offset": position, "type": kind, "declared_size": chunk_size,
                       "remaining_bytes": len(data) - start}
            break
        counts[kind] += 1
        if kind == 3:
            _, p = fstring(data, start)
            group, p = fstring(data, p)
            metadata, p = fstring(data, p)
            _, _, length = struct.unpack_from("<III", data, p)
            p += 12
            if p + length > end:
                raise ValueError("Event exceeds containing chunk")
            events.append((group, metadata, data[p:p + length], p))
        position = end
    markers = {}
    for marker in (b"PlayerStateEncryptionKey", b"AthenaMatchStats", b"AthenaMatchTeamStats"):
        offsets = []
        p = 0
        while (p := data.find(marker, p)) >= 0:
            offsets.append(p)
            p += len(marker)
        markers[marker.decode()] = offsets
    result = {"file": path.name, "bytes": len(data), "sha256": hashlib.sha256(data).hexdigest(),
              "version": version, "changelist": changelist, "duration_ms": duration, "live": live,
              "compressed": compressed, "encrypted": encrypted, "key_length": len(key),
              "nonzero_key_bytes": sum(value != 0 for value in key), "valid_chunk_counts": dict(counts),
              "first_invalid_chunk": invalid, "final_event_markers": markers,
              "first_event_is_plain_timecode": bool(events and events[0][2].startswith(b"\x0a\0\0\0"))}
    return data, key, events, result


def decrypt(key, ciphertext):
    return Cipher(algorithms.AES(key), modes.ECB()).decryptor().update(ciphertext)


def valid_padding(plain):
    return bool(plain and 1 <= plain[-1] <= 16 and plain.endswith(bytes([plain[-1]]) * plain[-1]))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("folder", type=Path)
    parser.add_argument("--scan-embedded-keys", action="store_true")
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    records = []
    for path in sorted(args.folder.glob("*.replay")):
        try:
            records.append((path, *inspect(path)))
        except (OSError, ValueError, struct.error, UnicodeError) as error:
            print(path.name, type(error).__name__)
    reference_keys = [(path.name, key) for path, _, key, _, result in records
                      if result["live"] == 0 and any(key) and len(key) == 32]
    # Confirm the known-plaintext signature on working files of this build first.
    references = []
    for path, _, key, events, result in records:
        if result["live"] == 0 and any(key) and events:
            plain = decrypt(key, events[0][2])
            references.append(plain[:4] == b"\x0a\0\0\0" and plain[-4:] == b"\x04" * 4)
    signature_verified = bool(references and all(references))
    for path, data, key, events, result in records:
        if result["live"] or any(key) or not events:
            continue
        ciphertext = events[0][2]
        result["other_file_keys_tested"] = len(reference_keys)
        result["other_file_key_timecode_matches"] = sum(
            decrypt(candidate, ciphertext)[:4] == b"\x0a\0\0\0"
            and decrypt(candidate, ciphertext)[-4:] == b"\x04" * 4
            for _, candidate in reference_keys)
        result["zero_key_event_padding_valid"] = [valid_padding(decrypt(key, event[2])) for event in events]
        if args.scan_embedded_keys and signature_verified:
            print("Scanning embedded keys:", path.name, flush=True)
            start = time.monotonic()
            candidates = 0
            matches = []
            # A successfully persisted copy of the AES-256 key would have to appear at one of these offsets.
            for offset in range(len(data) - 31):
                candidate = data[offset:offset + 32]
                if candidate == bytes(32):
                    continue
                candidates += 1
                plain = decrypt(candidate, ciphertext)
                if plain[:4] == b"\x0a\0\0\0" and plain[-4:] == b"\x04" * 4:
                    matches.append(offset)
            result["embedded_key_candidates_tested"] = candidates
            result["embedded_key_matching_offsets"] = matches
            result["embedded_scan_seconds"] = round(time.monotonic() - start, 2)
            print("Completed:", path.name, "matches:", len(matches), flush=True)
        result["unchanged_after_probe"] = hashlib.sha256(path.read_bytes()).hexdigest() == result["sha256"]
    report = {"timecode_signature_verified_on_working_files": signature_verified,
              "files": [record[-1] for record in records]}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding="utf-8")
    print("Written:", args.output)


if __name__ == "__main__":
    main()
