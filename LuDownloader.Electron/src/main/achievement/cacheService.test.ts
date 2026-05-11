import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import {
  CacheService,
  PERCENTAGES_TTL_SECONDS,
  SCHEMA_KEY,
  SCHEMA_TTL_SECONDS
} from './cacheService.ts';

test('cache set/get roundtrip returns same payload', async () => {
  const root = await fs.mkdtemp(path.join(os.tmpdir(), 'lua-cache-'));
  const cache = new CacheService(root);
  const payload = { level: 2, tags: ['a', 'b'] as const };
  await cache.set('730', SCHEMA_KEY, payload);
  const got = await cache.get<typeof payload>('730', SCHEMA_KEY);
  assert.deepEqual(got, payload);
});

test('cache get returns null when TTL expired', async () => {
  const root = await fs.mkdtemp(path.join(os.tmpdir(), 'lua-cache-'));
  const cache = new CacheService(root);
  const filePath = path.join(root, '1', `${SCHEMA_KEY}.json`);
  await fs.mkdir(path.dirname(filePath), { recursive: true });
  const stale = { cachedAt: 0, data: { x: true } };
  await fs.writeFile(filePath, JSON.stringify(stale), 'utf8');
  const got = await cache.get<{ x: boolean }>('1', SCHEMA_KEY, SCHEMA_TTL_SECONDS);
  assert.equal(got, null);
});

test('cache get treats malformed JSON as miss', async () => {
  const root = await fs.mkdtemp(path.join(os.tmpdir(), 'lua-cache-'));
  const cache = new CacheService(root);
  const filePath = path.join(root, '2', `${SCHEMA_KEY}.json`);
  await fs.mkdir(path.dirname(filePath), { recursive: true });
  await fs.writeFile(filePath, '{ not valid', 'utf8');
  const got = await cache.get('2', SCHEMA_KEY);
  assert.equal(got, null);
});

test('PERCENTAGES_TTL_SECONDS matches 24h', () => {
  assert.equal(PERCENTAGES_TTL_SECONDS, 24 * 60 * 60);
});

test('clearAllCache removes entries and recreates root', async () => {
  const root = await fs.mkdtemp(path.join(os.tmpdir(), 'lua-cache-'));
  const cache = new CacheService(root);
  await cache.set('9', SCHEMA_KEY, { ok: true });
  await cache.clearAllCache();
  const after = await cache.get('9', SCHEMA_KEY);
  assert.equal(after, null);
  await fs.access(root);
});
