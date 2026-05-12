import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import { watch } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import type { AppSettings, GameAchievements } from '../../shared/types.ts';
import { SourceId } from '../../shared/types.ts';

import {
  ACHIEVEMENTS_CHANGED_CHANNEL,
  ACHIEVEMENTS_DIFF_CHANNEL,
  processAchievementWatchTestEvent,
  resetAchievementWatchTestState,
  setAchievementEventSink,
  setAchievementWatcherDirsForTests,
  setAchievementWatcherPipelineForTests,
  setAchievementWatchDebounceMsForTests,
  setFsWatchForTests,
  setLoadSettingsForTests,
  startAchievementWatchers,
  stopAchievementWatchers
} from './achievementWatcherService.ts';

function baseTestSettings(overrides: Partial<AppSettings>): AppSettings {
  return {
    apiKey: '',
    downloadPath: '',
    maxDownloads: 20,
    steamUsername: '',
    steamWebApiKey: '',
    igdbClientId: '',
    igdbClientSecret: '',
    goldbergFilesPath: '',
    goldbergAccountName: '',
    goldbergSteamId: '',
    cloudServerUrl: '',
    cloudApiKey: '',
    achievementEnabledSources: [
      SourceId.Goldberg,
      SourceId.GSE,
      SourceId.Empress,
      SourceId.Codex,
      SourceId.Rune,
      SourceId.OnlineFix,
      SourceId.SmartSteamEmu,
      SourceId.Skidrow,
      SourceId.Darksiders,
      SourceId.Ali213,
      SourceId.Hoodlum,
      SourceId.CreamApi,
      SourceId.GreenLuma,
      SourceId.Reloaded
    ],
    achievementSourceRoots: {},
    hoodlumSavePath: '',
    achievementUserGameLibraryRoots: [],
    achievementScanOfficialSteamLibraries: true,
    achievementFirstRunDismissed: false,
    ...overrides
  };
}

function stubGameAchievements(appId: string): GameAchievements {
  return {
    appId,
    gameName: 'G',
    hasUsableInstallDirectory: false,
    source: 0,
    list: [],
    unlockedCount: 0,
    totalCount: 0,
    percentage: 0,
    hasPlatinum: false
  };
}

async function cleanupWatcherTest(): Promise<void> {
  await stopAchievementWatchers();
  resetAchievementWatchTestState();
}

async function mkdirAchievementTestDirs(): Promise<{ root: string; cacheDir: string; snapshotDir: string }> {
  const root = await fs.mkdtemp(path.join(os.tmpdir(), 'lua-aw-ach-'));
  const cacheDir = path.join(root, 'cache');
  const snapshotDir = path.join(root, 'snapshots');
  await fs.mkdir(cacheDir, { recursive: true });
  await fs.mkdir(snapshotDir, { recursive: true });
  setAchievementWatcherDirsForTests({ cacheDir, snapshotDir });
  return { root, cacheDir, snapshotDir };
}

test('startAchievementWatchers is idempotent; stop closes watchers', async () => {
  const dir = await fs.mkdtemp(path.join(os.tmpdir(), 'lua-aw-'));
  try {
    let opened = 0;
    let closed = 0;
    setFsWatchForTests(((_root, _opts, _cb) => {
      opened++;
      return { close: () => closed++ } as unknown as ReturnType<typeof watch>;
    }) as typeof watch);
    setLoadSettingsForTests(async () =>
      baseTestSettings({
        achievementScanOfficialSteamLibraries: false,
        achievementEnabledSources: [SourceId.Goldberg],
        achievementSourceRoots: { [SourceId.Goldberg]: [dir] }
      })
    );
    await startAchievementWatchers();
    const n = opened;
    await startAchievementWatchers();
    assert.equal(opened, n);
    await stopAchievementWatchers();
    assert.equal(closed, n);
  } finally {
    await cleanupWatcherTest();
    await fs.rm(dir, { recursive: true, force: true });
  }
});

test('rapid events for same source and appId debounce to one process run', async () => {
  const ach = await mkdirAchievementTestDirs();
  const dir = await fs.mkdtemp(path.join(os.tmpdir(), 'lua-aw-db-'));
  try {
    setFsWatchForTests((() => ({ close: () => {} }) as unknown as ReturnType<typeof watch>) as typeof watch);
    setLoadSettingsForTests(async () =>
      baseTestSettings({
        achievementScanOfficialSteamLibraries: false,
        achievementEnabledSources: [SourceId.Goldberg],
        achievementSourceRoots: { [SourceId.Goldberg]: [dir] }
      })
    );
    let runs = 0;
    setAchievementWatcherPipelineForTests({
      scanAllSources: async () => [],
      listInstalled: async () => [],
      processAppId: async () => {
        runs++;
        return stubGameAchievements('480');
      },
      diffAndUpdateSnapshot: async () => []
    });
    setAchievementWatchDebounceMsForTests(20);
    await startAchievementWatchers();
    await processAchievementWatchTestEvent(SourceId.Goldberg, dir, 'change', '480\\a.json');
    await processAchievementWatchTestEvent(SourceId.Goldberg, dir, 'change', '480\\b.json');
    await processAchievementWatchTestEvent(SourceId.Goldberg, dir, 'change', '480\\c.json');
    await new Promise((r) => setTimeout(r, 120));
    assert.equal(runs, 1);
  } finally {
    await cleanupWatcherTest();
    await fs.rm(dir, { recursive: true, force: true });
    await fs.rm(ach.root, { recursive: true, force: true });
  }
});

test('same appId from two sources runs processAppId sequentially', async () => {
  const ach = await mkdirAchievementTestDirs();
  const dirG = await fs.mkdtemp(path.join(os.tmpdir(), 'lua-aw-qg-'));
  const dirS = await fs.mkdtemp(path.join(os.tmpdir(), 'lua-aw-qs-'));
  try {
    setFsWatchForTests((() => ({ close: () => {} }) as unknown as ReturnType<typeof watch>) as typeof watch);
    setLoadSettingsForTests(async () =>
      baseTestSettings({
        achievementScanOfficialSteamLibraries: false,
        achievementEnabledSources: [SourceId.Goldberg, SourceId.GSE],
        achievementSourceRoots: {
          [SourceId.Goldberg]: [dirG],
          [SourceId.GSE]: [dirS]
        }
      })
    );
    let concurrent = 0;
    let maxConcurrent = 0;
    setAchievementWatcherPipelineForTests({
      scanAllSources: async () => [],
      listInstalled: async () => [],
      processAppId: async () => {
        concurrent++;
        maxConcurrent = Math.max(maxConcurrent, concurrent);
        await new Promise((r) => setTimeout(r, 50));
        concurrent--;
        return stubGameAchievements('480');
      },
      diffAndUpdateSnapshot: async () => []
    });
    setAchievementWatchDebounceMsForTests(0);
    await startAchievementWatchers();
    await processAchievementWatchTestEvent(SourceId.Goldberg, dirG, 'change', '480\\a.json');
    await processAchievementWatchTestEvent(SourceId.GSE, dirS, 'change', '480\\b.json');
    await new Promise((r) => setTimeout(r, 200));
    assert.equal(maxConcurrent, 1);
  } finally {
    await cleanupWatcherTest();
    await fs.rm(dirG, { recursive: true, force: true });
    await fs.rm(dirS, { recursive: true, force: true });
    await fs.rm(ach.root, { recursive: true, force: true });
  }
});

test('process emits achievements channels via sink', async () => {
  const ach = await mkdirAchievementTestDirs();
  const dir = await fs.mkdtemp(path.join(os.tmpdir(), 'lua-aw-sink-'));
  try {
    setFsWatchForTests((() => ({ close: () => {} }) as unknown as ReturnType<typeof watch>) as typeof watch);
    setLoadSettingsForTests(async () =>
      baseTestSettings({
        achievementScanOfficialSteamLibraries: false,
        achievementEnabledSources: [SourceId.Goldberg],
        achievementSourceRoots: { [SourceId.Goldberg]: [dir] }
      })
    );
    setAchievementWatcherPipelineForTests({
      scanAllSources: async () => [],
      listInstalled: async () => [],
      processAppId: async () => stubGameAchievements('42'),
      diffAndUpdateSnapshot: async () => []
    });
    setAchievementWatchDebounceMsForTests(5);
    const channels: string[] = [];
    setAchievementEventSink((ch) => channels.push(ch));
    await startAchievementWatchers();
    await processAchievementWatchTestEvent(SourceId.Goldberg, dir, 'change', '42\\achievements.json');
    await new Promise((r) => setTimeout(r, 80));
    assert.ok(channels.includes(ACHIEVEMENTS_CHANGED_CHANNEL));
    assert.ok(channels.includes(ACHIEVEMENTS_DIFF_CHANNEL));
  } finally {
    await cleanupWatcherTest();
    await fs.rm(dir, { recursive: true, force: true });
    await fs.rm(ach.root, { recursive: true, force: true });
  }
});
