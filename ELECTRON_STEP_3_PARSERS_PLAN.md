---
name: Electron Step 3 Parsing Layer
overview: Complete [ELECTRON_ACHIEVEMENTS_PLAN.md](ELECTRON_ACHIEVEMENTS_PLAN.md) Step 3 by porting LuiAchieve parsers and IniHelper to [LuDownloader.Electron/src/main/achievement/parsers/](LuDownloader.Electron/src/main/achievement/parsers/) with `node:test` fixtures—no discovery wiring, Steam, aggregation, IPC, or UI.
todos:
  - id: step3-ini
    content: iniHelper + unit tests (IniHelper.cs parity)
    status: pending
  - id: step3-goldberg
    content: goldbergParser + tests (JSON earned / earned_time)
    status: pending
  - id: step3-ini-parsers
    content: codex/cream/hoodlum/ali213/onlinefix/skidrow + tests
    status: pending
  - id: step3-sse
    content: smartSteamEmuParser + tests (stats.bin)
    status: pending
  - id: step3-reloaded
    content: reloadedParser + tests (dual INI + hex LE)
    status: pending
  - id: step3-greenluma
    content: greenLumaParser + registry value listing + mock tests
    status: pending
  - id: step3-dispatch-verify
    content: parseBySource switch + extend npm test + npm run build
    status: pending
isProject: false
---

# Electron Achievements — Step 3 (Parsing Layer)

## 1. Scope and functionality

- **Objective:** Finish Step 3 of [ELECTRON_ACHIEVEMENTS_PLAN.md](ELECTRON_ACHIEVEMENTS_PLAN.md): port **[AchievementPlugin/LuiAchieve/Parsers/](AchievementPlugin/LuiAchieve/Parsers/)** so a **file path** or GreenLuma **`HKCU|...`** registry path yields `Record<string, RawAchievement>` matching [IAchievementParser.cs](AchievementPlugin/LuiAchieve/Parsers/IAchievementParser.cs) (no throws; return `{}` on failure; per-entry skip where C# skips).
- **Strict scope — will not:** Modify [discoveryService.ts](LuDownloader.Electron/src/main/achievement/discoveryService.ts) flow (Step 4); Steam API / icon cache (Step 6); aggregation / snapshot / watchers / notifications (Steps 7–10); renderer or IPC; `index.ts` lifecycle wiring (Step 13); new npm dependencies.
- **Inputs / outputs:**
  - **In:** `string` absolute path to emulator save file, **or** GreenLuma-encoded `HKCU|SOFTWARE\...` + minimal **async** registry accessor for production (`winreg`) and mocks in tests.
  - **Out:** `Record<string, RawAchievement>` per [LuDownloader.Electron/src/shared/types.ts](LuDownloader.Electron/src/shared/types.ts). SmartSteamEmu keys = **lowercase CRC32 hex** as C# `ToString("x")` (not fixed width).
- **Legitimate edge cases:** Missing file → `{}`; bad JSON/INI → partial `{}`; Cream 7-digit `unlocktime` ×1000; Codex progress-complete with `UnlockTime==0`; Reloaded 3DM vs RLD branches; GreenLuma DWORD + `_Time` pairing; `stats.bin` count vs payload mismatch → `{}`; persisted nonsense achievement keys skipped.

**Mapping (plugin → Electron):**

| C# | Electron TS (under `main/achievement/parsers/`) |
|----|---------------------------------------------------|
| [IniHelper.cs](AchievementPlugin/LuiAchieve/Parsers/IniHelper.cs) | `iniHelper.ts` |
| [GoldbergParser.cs](AchievementPlugin/LuiAchieve/Parsers/GoldbergParser.cs) | `goldbergParser.ts` (same JSON for GSE/Empress) |
| [CodexParser.cs](AchievementPlugin/LuiAchieve/Parsers/CodexParser.cs) | `codexParser.ts` |
| [CreamApiParser.cs](AchievementPlugin/LuiAchieve/Parsers/CreamApiParser.cs) | `creamApiParser.ts` |
| [HoodlumParser.cs](AchievementPlugin/LuiAchieve/Parsers/HoodlumParser.cs) | `hoodlumParser.ts` |
| [Ali213Parser.cs](AchievementPlugin/LuiAchieve/Parsers/Ali213Parser.cs) | `ali213Parser.ts` |
| [OnlineFixParser.cs](AchievementPlugin/LuiAchieve/Parsers/OnlineFixParser.cs) | `onlineFixParser.ts` |
| [SkidrowParser.cs](AchievementPlugin/LuiAchieve/Parsers/SkidrowParser.cs) | `skidrowParser.ts` |
| [SmartSteamEmuParser.cs](AchievementPlugin/LuiAchieve/Parsers/SmartSteamEmuParser.cs) | `smartSteamEmuParser.ts` |
| [ReloadedParser.cs](AchievementPlugin/LuiAchieve/Parsers/ReloadedParser.cs) | `reloadedParser.ts` |
| [GreenLumaParser.cs](AchievementPlugin/LuiAchieve/Parsers/GreenLumaParser.cs) | `greenLumaParser.ts` |

**Sync rule:** C# file parsers are sync; Node `winreg` is callback-based — **file parsers use `readFileSync` + try/catch**; **GreenLuma only** returns `Promise<Record<...>>`.

## 2. System design

- **Files:** New directory [LuDownloader.Electron/src/main/achievement/parsers/](LuDownloader.Electron/src/main/achievement/parsers/) holding modules in the mapping table; optional [LuDownloader.Electron/src/main/achievement/parsers/parseBySource.ts](LuDownloader.Electron/src/main/achievement/parsers/parseBySource.ts) as a single `switch (SourceId)` (no factory pattern); extend [LuDownloader.Electron/src/main/achievement/registryAdapter.ts](LuDownloader.Electron/src/main/achievement/registryAdapter.ts) **only** with value-name listing needed by GreenLuma (HKCU) or colocate minimal `winreg` calls in `greenLumaParser.ts` if that stays shorter than duplicating hive logic **three** times (Rule of 3).
- **Separation:** Parsers = parse only; no settings, cache, or profile I/O; tests use temp dirs and mocked registry for GreenLuma.
- **Dependencies:** None new — reuse [`winreg`](LuDownloader.Electron/package.json) already in project.

## 3. Execution order and test checkpoints

**Task 1 — `iniHelper`**  
- **Depends on:** Nothing.  
- **Subtasks:** Implement comment/section/key=value rules from [IniHelper.cs](AchievementPlugin/LuiAchieve/Parsers/IniHelper.cs); case-insensitive section/key storage matching C#.  
- **TEST CHECKPOINT 1:** `node:test` — string or temp file fixtures for comments, empty lines, keys before `[Section]`, section merge.  

**Task 2 — `goldbergParser`**  
- **Depends on:** Nothing.  
- **Subtasks:** Read UTF-8 file; `JSON.parse` root object; for each key, read `earned` / `earned_time`; inner try/catch per entry.  
- **TEST CHECKPOINT 2:** Fixture with two achievements + one invalid child → partial map; missing file → `{}`.  

**Task 3 — INI-backed parsers (Codex, CreamAPI, Hoodlum, Ali213, OnlineFix, Skidrow)**  
- **Depends on:** Task 1.  
- **Subtasks:** Port each `.cs` file logic verbatim (CODEX progress edge, Cream 7-digit `unlocktime`, Hoodlum two sections, OnlineFix `true`/`1`, Skidrow timestamp-only sections). No shared base class; extract a tiny shared helper **only** after the same three-way duplication (master-architect Rule of 3).  
- **TEST CHECKPOINT 3:** One test file per parser or grouped `describe` blocks with minimal `.ini` strings on disk.  

**Task 4 — `smartSteamEmuParser`**  
- **Depends on:** Nothing.  
- **Subtasks:** `Buffer` read; int32 LE count; 24-byte records; filter `stateValue > 1`; build CRC hex key with `toString(16)`.  
- **TEST CHECKPOINT 4:** Hand-built buffer one record + corrupt count case → `{}`.  

**Task 5 — `reloadedParser`**  
- **Depends on:** Task 1.  
- **Subtasks:** Branch on presence of `[State]`+`[Time]` vs per-section hex fields; port `HexToUInt32LE` (8 hex chars → 4 bytes LE → uint32).  
- **TEST CHECKPOINT 5:** Two fixtures: 3DM layout and RLD layout.  

**Task 6 — `greenLumaParser` + registry hook**  
- **Depends on:** Nothing.  
- **Subtasks:** Split `HKCU|`; enumerate value names; skip `*_Time` in achievement pass; read DWORD + optional `_Time`; never throw, return `{}` on errors.  
- **TEST CHECKPOINT 6:** Mock `listValueNames` / `getDword` (or equivalent) — no real registry.  

**Task 7 — Dispatch + verification**  
- **Depends on:** Tasks 2–6.  
- **Subtasks:** `parseBySource(source: SourceId, filePath: string): Record<string, RawAchievement>` mapping file sources to parsers consistent with filenames in [discoveryService.ts](LuDownloader.Electron/src/main/achievement/discoveryService.ts); export GreenLuma parse **separately** (async). Extend [LuDownloader.Electron/package.json](LuDownloader.Electron/package.json) `test` script to run `parsers/**/*.test.ts` if not already picked up.  
- **TEST CHECKPOINT 7:** `npx tsc --noEmit` in `LuDownloader.Electron`, `npm run build`, `npm test` all pass.  

## 4. Security and error handling

- **Boundary:** Treat file path and registry reads as untrusted: wrap each public parse entry in try/catch; return `{}` on failure at boundary.  
- **Targeted error catching:** Match C# — inner loops may swallow single-entry errors (Goldberg); no global logger requirement inside parsers.  
- **Secrets:** N/A.  

## 5. Performance and cleanup

- **Sync file reads** OK for small saves; no directory enumeration inside parsers.  
- **No** persistent handles; GreenLuma uses short-lived `winreg` operations only.  

## 6. Testing strategy

- **Pragmatic:** Assert exact maps for fixtures; do not test Node `JSON.parse` or `Buffer` itself.  
- **Mocking:** GreenLuma registry; filesystem via `os.tmpdir()` / `mkdtemp`.  

## 7. Documentation requirements

- **Zero obvious comments** on trivial lines.  
- **Why-only** for Cream 7-digit rule, SmartSteamEmu CRC keys, Reloaded LE hex if naming alone is insufficient.  
- **Naming:** `parseGoldbergAchievements`, `parseCodexAchievements`, `parseGreenLumaAchievements`, etc.  

Follow [@.cursor/rules/master-architect.mdc](.cursor/rules/master-architect.mdc): no new deps, no extra abstraction layers, tests after every task before the next.
