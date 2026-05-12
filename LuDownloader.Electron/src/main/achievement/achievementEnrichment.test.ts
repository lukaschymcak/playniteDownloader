import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { CacheService, SCHEMA_KEY } from './cacheService.ts';
import { loadAchievementEnrichment } from './achievementEnrichment.ts';

test('loadAchievementEnrichment caches schema and downloads icon when key set', async () => {
  const root = await fs.mkdtemp(path.join(os.tmpdir(), 'lua-enrich-'));
  const cache = new CacheService(root);
  const appId = '100';

  const schemaJson = {
    game: {
      gameName: 'Enriched',
      availableGameStats: {
        achievements: [
          {
            name: 'A1',
            displayName: 'One',
            description: 'd',
            hidden: 0,
            icon: 'deadbeef',
            icongray: ''
          }
        ]
      }
    }
  };

  const pctJson = {
    achievementpercentages: {
      achievements: [{ name: 'A1', percent: 9.25 }]
    }
  };

  const stub: typeof fetch = async (input: Request | string | URL) => {
    const u = typeof input === 'string' ? input : input instanceof URL ? input.href : input.url;
    if (u.includes('GetSchemaForGame')) {
      return new Response(JSON.stringify(schemaJson), { status: 200 });
    }
    if (u.includes('GetGlobalAchievementPercentagesForApp')) {
      return new Response(JSON.stringify(pctJson), { status: 200 });
    }
    if (u.includes('steamcommunity/public/images')) {
      return new Response(new Uint8Array([7, 8, 9]), { status: 200 });
    }
    return new Response('no', { status: 404 });
  };

  const data = await loadAchievementEnrichment(appId, 'api-key', cache, stub);
  assert.equal(data.gameName, 'Enriched');
  assert.equal(data.achievements.length, 1);
  assert.equal(data.percentages.A1, 9.25);

  const cached = await cache.get<{ gameName: string }>(appId, SCHEMA_KEY);
  assert.ok(cached);
  assert.equal(cached!.gameName, 'Enriched');

  assert.equal(cache.iconExists(appId, 'deadbeef.jpg'), true);
});

test('loadAchievementEnrichment without key uses cache entry without TTL', async () => {
  const root = await fs.mkdtemp(path.join(os.tmpdir(), 'lua-enrich2-'));
  const cache = new CacheService(root);
  const appId = '200';
  const payload = { gameName: 'CachedOnly', achievements: [] as const };
  await cache.set(appId, SCHEMA_KEY, payload);

  const stub: typeof fetch = async () => {
    throw new Error('no network');
  };
  const data = await loadAchievementEnrichment(appId, '', cache, stub);
  assert.equal(data.gameName, 'CachedOnly');
});
