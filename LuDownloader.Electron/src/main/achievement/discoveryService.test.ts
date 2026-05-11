import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { SourceId, type AppSettings } from '../../shared/types';
import {
  buildSourceRoots,
  dedupeNormalizedPaths,
  isNumericAppId,
  isStrictlyInside,
  normalizeWindowsPath,
  resolveAppIdFromPath,
  scanAllSources,
  type RegistryAdapter
} from './discoveryService';

const baseSettings: AppSettings = {
  apiKey: '',
  downloadPath: '',
  maxDownloads: 10,
  steamUsername: '',
  steamWebApiKey: '',
  igdbClientId: '',
  igdbClientSecret: '',
  goldbergFilesPath: '',
  goldbergAccountName: '',
  goldbergSteamId: '',
  cloudServerUrl: '',
  cloudApiKey: '',
  achievementEnabledSources: Object.values(SourceId).filter((s) => s !== SourceId.None),
  achievementSourceRoots: {},
  hoodlumSavePath: '',
  achievementUserGameLibraryRoots: [],
  achievementScanOfficialSteamLibraries: true,
  achievementFirstRunDismissed: false
};

test('dedupeNormalizedPaths normalizes and dedupes case-insensitively', () => {
  const result = dedupeNormalizedPaths([
    'C:\\Games\\',
    'c:\\games',
    'C:\\Games\\\\',
    'D:\\A'
  ]);
  assert.equal(result.length, 2);
});

test('isNumericAppId accepts only digits', () => {
  assert.equal(isNumericAppId('480'), true);
  assert.equal(isNumericAppId('48a'), false);
  assert.equal(isNumericAppId(''), false);
});

test('isStrictlyInside rejects root-equal and outside paths', () => {
  const root = normalizeWindowsPath('C:\\A')!;
  assert.equal(isStrictlyInside(root, root), false);
  assert.equal(isStrictlyInside(root, `${root}\\B\\file.txt`), true);
  assert.equal(isStrictlyInside(root, 'C:\\Other\\file.txt'), false);
});

test('resolveAppIdFromPath finds nearest numeric ancestor under root', () => {
  const root = 'C:\\Users\\L\\AppData\\Roaming\\Goldberg SteamEmu Saves';
  const full = 'C:\\Users\\L\\AppData\\Roaming\\Goldberg SteamEmu Saves\\12345\\achievements.json';
  assert.equal(resolveAppIdFromPath(full, root), '12345');
});

test('buildSourceRoots merges defaults and custom roots', () => {
  const settings: AppSettings = {
    ...baseSettings,
    achievementSourceRoots: {
      [SourceId.Goldberg]: ['C:\\CustomGoldberg']
    },
    hoodlumSavePath: 'D:\\HoodlumSaves'
  };
  const roots = buildSourceRoots(settings);
  assert.ok(roots[SourceId.Goldberg].some((p) => p.toLowerCase().includes('customgoldberg')));
  assert.ok(roots[SourceId.Hoodlum].some((p) => p.toLowerCase().includes('hoodlumsaves')));
});

test('scanAllSources scans file sources and greenluma registry adapter', async () => {
  const tempRoot = await fs.mkdtemp(path.join(os.tmpdir(), 'lud-discovery-'));
  try {
    const gseRoot = path.join(tempRoot, 'GSE');
    await fs.mkdir(path.join(gseRoot, '480'), { recursive: true });
    await fs.writeFile(path.join(gseRoot, '480', 'achievements.json'), '{}', 'utf8');

    const settings: AppSettings = {
      ...baseSettings,
      achievementEnabledSources: [SourceId.GSE, SourceId.GreenLuma],
      achievementSourceRoots: {
        [SourceId.GSE]: [gseRoot]
      }
    };

    const registry: RegistryAdapter = {
      async listSubKeys(baseKey: string): Promise<string[]> {
        if (baseKey.includes('GLR')) return ['570'];
        return [];
      },
      async getValue(keyPath: string, valueName: string): Promise<string | number | null> {
        if (valueName !== 'SkipStatsAndAchievements') return null;
        if (keyPath.includes('\\570')) return 0;
        return null;
      }
    };

    const results = await scanAllSources(settings, registry);
    assert.ok(results.some((r) => r.source === SourceId.GSE && r.appId === '480' && r.kind === 'file'));
    assert.ok(results.some((r) => r.source === SourceId.GreenLuma && r.appId === '570' && r.kind === 'registry'));
  } finally {
    await fs.rm(tempRoot, { recursive: true, force: true });
  }
});
