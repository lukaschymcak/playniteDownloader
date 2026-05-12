import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import type { DiscoveryRecord, InstalledGame } from '../../shared/types.ts';
import { EmulatorSource, SourceId } from '../../shared/types.ts';
import { CacheService } from './cacheService.ts';
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
    assert.equal(result.hasPlatinum, false);
    assert.equal(result.unlockedCount, 1);
    assert.equal(result.totalCount, 1);
  } finally {
    await fs.rm(dir, { recursive: true, force: true });
    await fs.rm(root, { recursive: true, force: true });
  }
});
