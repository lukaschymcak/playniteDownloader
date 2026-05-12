import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import type { DiscoveryRecord, InstalledGame } from '../../shared/types.ts';
import { EmulatorSource, SourceId } from '../../shared/types.ts';
import { CacheService } from './cacheService.ts';
import { diffAndUpdateSnapshot } from './achievementSnapshotService.ts';
import { processAppId } from './processAppId.ts';

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

test('processAppId merges Goldberg file with Steam schema from stub fetch', async () => {
  const dir = await fs.mkdtemp(path.join(os.tmpdir(), 'lud-proc-'));
  const root = await fs.mkdtemp(path.join(os.tmpdir(), 'lud-proc-cache-'));
  try {
    const achPath = path.join(dir, 'achievements.json');
    await fs.writeFile(
      achPath,
      JSON.stringify({
        ACH_FILE: { earned: true, earned_time: 42 }
      }),
      'utf8'
    );

    const game = stubGame({
      appId: '480',
      installPath: dir,
      gameName: 'LocalName',
      installDir: 'Spacewar'
    });

    const discovery: DiscoveryRecord[] = [
      { appId: '480', source: SourceId.Goldberg, kind: 'file', location: achPath }
    ];

    const schemaJson = {
      game: {
        gameName: 'SchemaTitle',
        availableGameStats: {
          achievements: [
            {
              name: 'ACH_FILE',
              displayName: 'File Ach',
              description: 'desc',
              hidden: 0,
              icon: '',
              icongray: ''
            }
          ]
        }
      }
    };

    const pctJson = {
      achievementpercentages: {
        achievements: [{ name: 'ACH_FILE', percent: 3 }]
      }
    };

    const stub: typeof fetch = async (input) => {
      const u = typeof input === 'string' ? input : input instanceof Request ? input.url : input.toString();
      if (u.includes('GetSchemaForGame')) {
        return new Response(JSON.stringify(schemaJson), { status: 200 });
      }
      if (u.includes('GetGlobalAchievementPercentagesForApp')) {
        return new Response(JSON.stringify(pctJson), { status: 200 });
      }
      return new Response('', { status: 404 });
    };

    const cache = new CacheService(root);
    const result = await processAppId(
      '480',
      discovery,
      { steamWebApiKey: 'k', achievementScanOfficialSteamLibraries: false },
      [game],
      cache,
      stub
    );

    assert.equal(result.appId, '480');
    assert.equal(result.gameName, 'SchemaTitle');
    assert.equal(result.list.length, 1);
    const a = result.list[0]!;
    assert.equal(a.displayName, 'File Ach');
    assert.equal(a.achieved, true);
    assert.equal(a.unlockTime, 42);
    assert.equal(a.globalPercentage, 3);
    assert.equal(result.source & EmulatorSource.Goldberg, EmulatorSource.Goldberg);
    assert.equal(result.hasPlatinum, true);
    assert.equal(result.unlockedCount, 1);
    assert.equal(result.totalCount, 1);
  } finally {
    await fs.rm(dir, { recursive: true, force: true });
    await fs.rm(root, { recursive: true, force: true });
  }
});

test('processAppId sorts list by displayName then apiName', async () => {
  const dir = await fs.mkdtemp(path.join(os.tmpdir(), 'lud-proc-sort-'));
  const root = await fs.mkdtemp(path.join(os.tmpdir(), 'lud-proc-sort-cache-'));
  try {
    const achPath = path.join(dir, 'achievements.json');
    await fs.writeFile(
      achPath,
      JSON.stringify({
        Z_KEY: { earned: false, earned_time: 0 },
        A_KEY: { earned: false, earned_time: 0 }
      }),
      'utf8'
    );

    const game = stubGame({ appId: '1', installPath: dir, gameName: 'Ctx', installDir: 'g' });

    const schemaJson = {
      game: {
        gameName: 'S',
        availableGameStats: {
          achievements: [
            { name: 'Z_KEY', displayName: 'Zebra', description: '', hidden: 0, icon: '', icongray: '' },
            { name: 'A_KEY', displayName: 'Apple', description: '', hidden: 0, icon: '', icongray: '' },
            { name: 'M_KEY', displayName: 'Apple', description: '', hidden: 0, icon: '', icongray: '' }
          ]
        }
      }
    };

    const stub: typeof fetch = async (input) => {
      const u = typeof input === 'string' ? input : input instanceof Request ? input.url : input.toString();
      if (u.includes('GetSchemaForGame')) {
        return new Response(JSON.stringify(schemaJson), { status: 200 });
      }
      if (u.includes('GetGlobalAchievementPercentagesForApp')) {
        return new Response(JSON.stringify({ achievementpercentages: { achievements: [] } }), { status: 200 });
      }
      return new Response('', { status: 404 });
    };

    const result = await processAppId(
      '1',
      [{ appId: '1', source: SourceId.Goldberg, kind: 'file', location: achPath }],
      { steamWebApiKey: 'k', achievementScanOfficialSteamLibraries: false },
      [game],
      new CacheService(root),
      stub
    );

    assert.deepEqual(
      result.list.map((x) => x.apiName),
      ['A_KEY', 'M_KEY', 'Z_KEY']
    );
  } finally {
    await fs.rm(dir, { recursive: true, force: true });
    await fs.rm(root, { recursive: true, force: true });
  }
});

test('processAppId source mask ORs all discovery SourceId bits', async () => {
  const dir = await fs.mkdtemp(path.join(os.tmpdir(), 'lud-proc-mask-'));
  const root = await fs.mkdtemp(path.join(os.tmpdir(), 'lud-proc-mask-cache-'));
  try {
    const d1 = path.join(dir, 'a.json');
    const d2 = path.join(dir, 'b.json');
    await fs.writeFile(d1, JSON.stringify({ X: { earned: false, earned_time: 0 } }), 'utf8');
    await fs.writeFile(d2, JSON.stringify({ X: { earned: false, earned_time: 0 } }), 'utf8');

    const game = stubGame({ appId: '2', installPath: dir });

    const stub: typeof fetch = async (input) => {
      const u = typeof input === 'string' ? input : input instanceof Request ? input.url : input.toString();
      if (u.includes('GetSchemaForGame')) {
        return new Response(
          JSON.stringify({
            game: {
              gameName: '',
              availableGameStats: {
                achievements: [
                  { name: 'X', displayName: 'X', description: '', hidden: 0, icon: '', icongray: '' }
                ]
              }
            }
          }),
          { status: 200 }
        );
      }
      if (u.includes('GetGlobalAchievementPercentagesForApp')) {
        return new Response(JSON.stringify({ achievementpercentages: { achievements: [] } }), { status: 200 });
      }
      return new Response('', { status: 404 });
    };

    const expected = EmulatorSource.Goldberg | EmulatorSource.GSE;
    const result = await processAppId(
      '2',
      [
        { appId: '2', source: SourceId.Goldberg, kind: 'file', location: d1 },
        { appId: '2', source: SourceId.GSE, kind: 'file', location: d2 }
      ],
      { steamWebApiKey: '', achievementScanOfficialSteamLibraries: false },
      [game],
      new CacheService(root),
      stub
    );

    assert.equal(result.source, expected);
  } finally {
    await fs.rm(dir, { recursive: true, force: true });
    await fs.rm(root, { recursive: true, force: true });
  }
});

test('processAppId then diffAndUpdateSnapshot detects unlock on second run', async () => {
  const dir = await fs.mkdtemp(path.join(os.tmpdir(), 'lud-proc-snap-'));
  const cacheRoot = await fs.mkdtemp(path.join(os.tmpdir(), 'lud-proc-snap-cache-'));
  const snapRoot = await fs.mkdtemp(path.join(os.tmpdir(), 'lud-proc-snap-snaps-'));
  try {
    const achPath = path.join(dir, 'achievements.json');
    await fs.writeFile(
      achPath,
      JSON.stringify({
        K: { earned: false, earned_time: 0 }
      }),
      'utf8'
    );

    const game = stubGame({ appId: '99', installPath: dir });

    const schemaJson = {
      game: {
        gameName: 'T',
        availableGameStats: {
          achievements: [
            { name: 'K', displayName: 'K', description: '', hidden: 0, icon: '', icongray: '' }
          ]
        }
      }
    };

    const stub: typeof fetch = async (input) => {
      const u = typeof input === 'string' ? input : input instanceof Request ? input.url : input.toString();
      if (u.includes('GetSchemaForGame')) {
        return new Response(JSON.stringify(schemaJson), { status: 200 });
      }
      if (u.includes('GetGlobalAchievementPercentagesForApp')) {
        return new Response(JSON.stringify({ achievementpercentages: { achievements: [] } }), { status: 200 });
      }
      return new Response('', { status: 404 });
    };

    const discovery: DiscoveryRecord[] = [
      { appId: '99', source: SourceId.Goldberg, kind: 'file', location: achPath }
    ];
    const settings = { steamWebApiKey: 'k', achievementScanOfficialSteamLibraries: false };

    const first = await processAppId('99', discovery, settings, [game], new CacheService(cacheRoot), stub);
    assert.deepEqual(await diffAndUpdateSnapshot(snapRoot, first), []);

    await fs.writeFile(
      achPath,
      JSON.stringify({
        K: { earned: true, earned_time: 100 }
      }),
      'utf8'
    );

    const second = await processAppId('99', discovery, settings, [game], new CacheService(cacheRoot), stub);
    const diffs = await diffAndUpdateSnapshot(snapRoot, second);
    assert.equal(diffs.length, 1);
    assert.equal(diffs[0]!.isNewUnlock, true);
    assert.equal(diffs[0]!.achievement.apiName, 'K');
  } finally {
    await fs.rm(dir, { recursive: true, force: true });
    await fs.rm(cacheRoot, { recursive: true, force: true });
    await fs.rm(snapRoot, { recursive: true, force: true });
  }
});
