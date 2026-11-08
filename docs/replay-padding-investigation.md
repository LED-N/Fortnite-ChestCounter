# リプレイ復号エラー調査・引き継ぎ

調査日: 2026-11-08 (Asia/Tokyo)

## 結論

追記: 後続のログ・ファイル構造の再調査で、ディスク容量不足による保存失敗の証拠を確認した。`missing-key-recovery-investigation.md` を参照。鍵がゼロの理由について、初回調査時点の未確定事項を補うもの。

`Padding is invalid and cannot be removed.` は、鍵が全バイト `00` のリプレイを AES-ECB / PKCS7 で復号しようとして発生している。対象2ファイルの先頭イベントを単独で復号して同じ例外を再現した。パディング方式の誤りと判断できる証拠はない。

## 確認した証拠

対象は `%LOCALAPPDATA%\FortniteGame\Saved\Demos` 内の以下2本。

| ファイル | FileVersion | IsLive | IsEncrypted | 鍵長 | 鍵の非ゼロバイト数 |
| --- | --- | --- | --- | --- | --- |
| UnsavedReplay-2026.11.08-11.33.51.replay | 7 | 0 | 1 | 32 | 0 |
| UnsavedReplay-2026.11.08-11.40.33.replay | 7 | 0 | 1 | 32 | 0 |

- 両方ともヘッダーの鍵はファイルオフセット586から32バイト。
- 最初のイベントは `Timecode` / `TimecodeVersionedMeta`。ペイロードはオフセット748から16バイト。
- ヘッダーから読み取った鍵を使い、PowerShellから .NET の `Aes.Create()`、`Mode=ECB`、`Padding=PKCS7`、`TransformFinalBlock()` を実行すると、両方とも同じパディング例外になった。
- `%APPDATA%\ChestCounter\errors.log` の11:39:55および11:46:18の記録も `DecryptBuffer → ReadEvent → ReadReplayChunks` での同じ例外を示す。
- 3本目の11:49:04のリプレイは使用中で、今回の再現確認の対象外。

## コード上の経路

1. `src/ChestCounter/Tracker.cs` の `IsReady()` は `IsLive == false` かつ古いファイル等の条件で、`KeyMissing()` の判定に到達する前に解析可能とする。
2. 同ファイルの `Process()` は `IsLive == true && KeyMissing()` の場合だけ未完成として除外する。今回は `IsLive == false` のため通過する。
3. `lib/Unreal.Core/ReplayReader.cs` の `ReadReplayInfo()` は暗号化済み・録画完了ファイルでも `EncryptionKey.Length == 0` しか検査しない。32バイトのゼロ埋め鍵は通過する。
4. `lib/FortniteReplayReader/FortniteReplayReader.cs` の `ReadEvent()` はイベントの種類による分岐より先に `DecryptBuffer()` を実行する。
5. `DecryptBuffer()` はゼロ埋め鍵をそのまま `aes.DecryptEcb(..., PaddingMode.PKCS7)` に渡し、先頭イベントで失敗する。
6. `ReadReplayChunks()` の復号例外保護は `IsLive == true` 限定なので、今回の例外は呼び出し元へ伝播する。

なお、`ReplayHeaderProbe.KeyMissing()` 自体にはゼロ埋め鍵の検出処理が既にあるが、上記の経路で活用されていない。

## 未確定事項と次の作業

- 録画完了フラグなのに鍵がゼロのままの理由（録画の終了処理、生成環境側の実装等）は、保存ファイルと現存ログだけでは確定できない。終了フラグだけで鍵の確定を判断できないことは確認済み。
- 修正する場合は、録画完了フラグに依存せず、暗号化ファイルの鍵が空または全ゼロであれば復号へ進めないようにする。書き込み途中の再試行と、鍵のないまま確定したファイルの扱いを区別する。
- 鍵の検査を追加しても、失われた鍵の復元や既存ファイルの解析成功を保証するものではない。
- `PaddingMode.None` への変更や例外の握りつぶしでは正しい復号データは得られない。
- 実装変更は行っていない。リプレイ本体とアプリのデータファイルは変更していない。
- 作業フォルダーには `.git` がなく、`git status` が `not a git repository` となるためコミット不可。この資料を引き継ぎ記録とする。

## 追加実装（2026-11-08）

上記の「実装変更は行っていない」は初回調査時点の記録。ユーザー承認後、以下を実装した。

- `ReplayInfo.HasValidEncryptionKey` で空・全ゼロ・不正なAES鍵長を拒否。
- ヘッダー読み込みと復号直前の両方に検証を置き、専用の `InvalidReplayEncryptionKeyException` で原因を通知。
- 完了フラグがあっても鍵がなければ最終更新から1分待機し、古い鍵なしファイルは `missing-key` としてスキップ。ファイル更新時には再び対象になる。
- バッチ処理でもスキップ理由を表示。鍵の読み取り競合時は次回スキャンで再試行。
- AnalyzerVersionを11へ更新し、既存のエラー記録を一度だけ再評価。
- 日本語UIと日本語起動用ランチャーを追加。
- 回帰テストと実ファイル検証の結果、配布物の場所は `implementation-handoff.md` に記載。
