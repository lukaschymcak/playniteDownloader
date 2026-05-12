# Electron Achievements Implementation Plan

This document outlines the step-by-step plan for implementing the achievement system in `LuDownloader.Electron`, mirroring the functionality of the `AchievementPlugin`.

## Step 1: Core Contracts (Foundation)
**Purpose:** Define the data structures that will flow through the entire system. Every parser, service, and UI component depends on these types.

- **Shared Models:**
    - `RawAchievement`: The basic achievement data as extracted from emulator files. (Ref: `AchievementPlugin/LuiAchieve/Parsers/RawAchievement.cs`)
    - `Achievement`: The enriched achievement model with metadata (name, description, icons, unlock status). (Ref: `AchievementPlugin/LuiAchieve/Models/Achievement.cs`)
    - `GameAchievements`: A container for all achievements of a specific game. (Ref: `AchievementPlugin/LuiAchieve/Models/GameAchievements.cs`)
    - `ScanResult`: The result of a source discovery scan.
    - `Diff`: Represents changes in achievement status (e.g., newly unlocked).
- **Enums & Settings:**
    - `SourceId`: Enum for different achievement sources (Goldberg, CODEX, etc.). (Ref: `AchievementPlugin/LuiAchieve/Models/EmulatorSource.cs`)
    - `AchievementSettings`: The configuration shape for the achievement system. (Ref: `AchievementPlugin/LuiAchieve/Settings/LuiAchieveSettings.cs`)

## Step 2: Config + Storage Foundation
**Purpose:** Provide persistence for settings, cached achievement data, and user profiles.

- **Settings Store:** Load/save achievement-specific settings.
- **Cache Store:** Store downloaded achievement schemas and icons from Steam to avoid redundant API calls. (Ref: `AchievementPlugin/LuiAchieve/Services/CacheService.cs`)
- **Profile Store:** Track user-specific stats (total points, unlock history). (Ref: `AchievementPlugin/LuiAchieve/Services/ProfileService.cs`)

## Step 3: Parsing Layer
**Purpose:** Extract raw data from various emulator formats. These should be pure functions where possible.

- **Emulator Parsers:** Individual parsers for Goldberg, CODEX, CreamAPI, etc.
- **Helper Utilities:** INI parsing helpers.
- **Reference:** `AchievementPlugin/LuiAchieve/Parsers/` (e.g., `GoldbergParser.cs`, `CodexParser.cs`, `IniHelper.cs`)

## Step 4: Source Discovery Layer
**Purpose:** Locate where achievement files are stored on the user's system.

- **Scanner:** Recursively scan configured roots and the Windows registry for known achievement file patterns. (Ref: `AchievementPlugin/LuiAchieve/Services/EmulatorScanner.cs`)
- **AppId Resolver:** Logic to map a file path back to a Steam AppId based on folder structure. (Ref: `AchievementPlugin/LuiAchieve/InstallDirRules.cs`)

## Step 5: Game Context Resolution
**Purpose:** Map Steam AppIds to local installation directories and Steam library roots.

- **Install Mapper:** Determine where a game is installed to find its local achievement files (for sources that store data in the game folder). (Ref: `AchievementPlugin/LuiAchieve/Services/GameInstallMapper.cs`)
- **Library Helpers:** Use existing Steam library detection to narrow down search paths. (Ref: `AchievementPlugin/LuiAchieve/Services/SteamLibraryHelper.cs`)

## Step 6: Steam Enrichment Services
**Purpose:** Fetch missing metadata from the Steam API to make achievements look "official."

- **Steam API Client:** Fetch achievement schemas (names, descriptions, icons) and global unlock percentages. (Ref: `AchievementPlugin/LuiAchieve/Services/SteamApiService.cs`)
- **Icon Cache:** Download and store achievement icons locally.

## Step 7: Aggregation Pipeline
**Purpose:** The core logic that combines raw local data with Steam metadata to produce the final `GameAchievements` object.

- **ProcessAppId:** Orchestrate the flow: Find local files -> Parse -> Fetch Steam metadata -> Merge.
- **Merge Rules:** Handle conflicts if multiple sources provide data for the same game.
- **Reference:** `AchievementPlugin/LuiAchieve/Services/AchievementService.cs`

## Step 8: Snapshot + Diff Engine
**Purpose:** Detect when an achievement has been unlocked by comparing the current state against a previous baseline.

- **Baseline Management:** Save a "snapshot" of achievement progress.
- **Diffing Logic:** Identify newly unlocked achievements or progress increments.
- **Reference:** `AchievementPlugin/LuiAchieve/Services/AchievementSnapshotService.cs`

## Step 9: Watcher Orchestration
**Purpose:** Monitor achievement files in real-time and trigger the aggregation pipeline when changes occur.

- **File Watchers:** Set up OS-level watchers on achievement file locations. (Ref: `AchievementPlugin/LuiAchieve/Services/FileWatcherService.cs`)
- **Debounce Logic:** Prevent multiple triggers for the same change.
- **Change Processor:** Call the aggregation pipeline and diff engine when a change is detected. (Ref: `AchievementPlugin/LuiAchieve/Services/AchievementFileChangeProcessor.cs`)

## Step 10: Notification System
**Purpose:** Inform the user when an achievement is unlocked.

- **Notification Queue:** Manage multiple pending notifications. (Ref: `AchievementPlugin/LuiAchieve/Notifications/NotificationQueue.cs`)
- **Presenter:** Logic to display the notification (e.g., custom Electron window or native toast).
- **Assets:** Play unlock sounds and display icons.

## Step 11: UI Read Path
**Purpose:** Display achievements to the user within the app.

- **Views:** Game grid indicators, achievement lists, detail views, and user profile stats.
- **Reference:** `AchievementPlugin/LuiAchieve/Views/` (e.g., `GameGridView.cs`, `AchievementsView.cs`)

## Step 12: UI Write Path (Settings)
**Purpose:** Allow users to configure the achievement system.

- **Settings UI:** Toggles for sources, custom path configuration, API key entry, and notification preferences. (Ref: `AchievementPlugin/LuiAchieve/Views/LuiAchieveSettingsView.cs`)

## Step 13: Lifecycle Wiring
**Purpose:** Initialize the system on app startup and clean up on shutdown.

- **Startup Priming:** Perform an initial scan and start watchers. (Ref: `AchievementPlugin/LuiAchieve/Services/AchievementStartupPrimingService.cs`)
- **Disposal:** Gracefully stop watchers and save final states.

## Step 14: Hardening + Tests
**Purpose:** Ensure reliability and prevent regressions.

- **Unit Tests:** Test parsers, diff logic, and debouncing.
- **Integration Tests:** Smoke test the entire pipeline from file change to notification.
- **Reference:** `AchievementPlugin/LuiAchieve/LuiAchieve.Tests/`
