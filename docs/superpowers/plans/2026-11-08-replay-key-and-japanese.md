# Replay key validation and Japanese support Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans for implementation and superpowers:dispatching-parallel-agents for independent translation scopes. Track completed checks below.

**Goal:** Reject missing replay keys before decryption and provide a Japanese user interface and executable.
**Architecture:** Validate encryption keys at library and tracker boundaries; extend existing inline translation calls with Japanese arguments while preserving stored names.
**Tech Stack:** C#, .NET 10, Windows, existing HTML report and native overlay.
**Spec:** ../specs/2026-11-08-replay-key-and-japanese-design.md

## Global Constraints

- Preserve existing saved data and fr/en/es behavior.
- Never alter replay files or recover keys by changing padding.
- Work in this extracted workspace; no .git exists, so worktree and commits are unavailable. Do not initialize an unrelated repository.
- User approved implementation; continue without another approval checkpoint.

## Review Focus

- Completed and live replays with empty/all-zero keys must not reach AES.
- Fresh completed files without keys must remain pending and be retried.
- Old files without keys must not block scanning indefinitely.
- Previous padding errors must be re-evaluated once after upgrade.
- Japanese formatting, name mapping, help and all report/overlay surfaces must work without changing stored keys.

## Task 1: Replay key validation

- [x] Create a dependency-free regression console project referencing ChestCounter.
- [x] Run failing tests for zero-key headers, synthetic valid encrypted/non-encrypted payloads, tracker key readiness and migration.
- [x] Add key validation and descriptive invalid-key exception; update tracker readiness and incomplete handling, bump analyzer version for re-evaluation.
- [x] Run tests including actual problematic replay headers; preserve files.

## Task 2: Japanese translation

- [x] Add failing language regressions for ja normalization/culture/ordinal/count/name mapping.
- [x] Extend L.T and L.Count with Japanese forms; translate app strings, report, overlay and help. Keep French persisted names.
- [x] Verify translations and existing language switching; add Japanese startup documentation.

## Task 3: Delivery and review

- [x] Run regression suite and Release build.
- [x] Publish self-contained Windows executable to dist.
- [x] Generate Japanese report and overlay previews in an isolated test data folder; inspect the output.
- [x] Request independent review, resolve material findings, rerun affected checks.
- [x] Update investigation and handoff with commands, results, executable path and remaining limitations.

## Completion ledger

Task 1: complete — baseline 7 passed / 9 failed, then 16 passed after key validation.
Task 2: complete — Japanese suite passed; review regressions first failed and then passed for standard prize switching and console columns.
Task 3: complete — final 21 passed, 0 failed including 2 real missing-key files and artifact generation. Final Release publish and executable smoke run succeeded (2 valid real replays analyzed, 2 missing-key files skipped). See ../../implementation-handoff.md.

Ruling: no Git initialization/worktree/commit — this is an extracted archive without .git, and preserving the supplied workspace is preferable to inventing repository history.
Ruling: use native implementation with independent translation agents and a fresh reviewer — user approved continuous implementation; independent scopes avoid conflicting edits.
