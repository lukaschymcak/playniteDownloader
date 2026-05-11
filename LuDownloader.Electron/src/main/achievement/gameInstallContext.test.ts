import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import type { InstalledGame } from '../../shared/types.ts';
import { buildAchievementGameContext } from './gameInstallContext.ts';

function stubGame(overrides: Partial<InstalledGame> & Pick<InstalledGame, 'appId'>): InstalledGame {
  const base: InstalledGame = {
    appId: overrides.appId,
    gameName: 'Game',
    installPath: '',
    libraryPath: '',
    installDir: '',
    installedDate: '',
    sizeOnDisk: 0,
    selectedDepots: [],
    manifestGIDs: {},
    drmStripped: false,
    registeredWithSteam: false,
    gseSavesCopied: false
  };
  return { ...base, ...overrides };
}

test('buildAchievementGameContext uses installed game when install path exists', async () => {
  const dir = await fs.mkdtemp(path.join(os.tmpdir(), 'lud-achctx-'));
  try {
    const g = stubGame({
      appId: '730',
      installPath: dir,
      libraryPath: 'D:\\SteamLibrary',
      installDir: 'Counter Strike',
      gameName: 'CS2'
    });
    const ctx = await buildAchievementGameContext('730', [g], { achievementScanOfficialSteamLibraries: false });
    assert.equal(ctx.hasUsableInstallDirectory, true);
    assert.equal(ctx.installPath, dir);
    assert.equal(ctx.libraryRoot, 'D:\\SteamLibrary');
    assert.equal(ctx.gameName, 'CS2');
    assert.equal(ctx.installDir, 'Counter Strike');
  } finally {
    await fs.rm(dir, { recursive: true, force: true });
  }
});

test('buildAchievementGameContext not usable when install path missing on disk', async () => {
  const g = stubGame({
    appId: '999',
    installPath: 'C:\\NoSuchPath\\Game999',
    libraryPath: '',
    installDir: 'x'
  });
  const ctx = await buildAchievementGameContext('999', [g], { achievementScanOfficialSteamLibraries: false });
  assert.equal(ctx.hasUsableInstallDirectory, false);
  assert.equal(ctx.installPath, null);
});

test('buildAchievementGameContext empty when no record and official scan off', async () => {
  const ctx = await buildAchievementGameContext('123', [], { achievementScanOfficialSteamLibraries: false });
  assert.equal(ctx.appId, '123');
  assert.equal(ctx.hasUsableInstallDirectory, false);
  assert.equal(ctx.installPath, null);
});
