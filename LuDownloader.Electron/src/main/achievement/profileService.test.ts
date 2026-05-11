import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { ProfileService, normalizeUserProfile } from './profileService.ts';

test('profile missing file yields defaults', async () => {
  const root = await fs.mkdtemp(path.join(os.tmpdir(), 'lua-prof-'));
  const svc = new ProfileService(path.join(root, 'profile.json'));
  const profile = await svc.load();
  assert.equal(profile.name, 'Player');
  assert.deepEqual(profile.featuredTrophiesByGame, {});
});

test('profile save then load roundtrip', async () => {
  const root = await fs.mkdtemp(path.join(os.tmpdir(), 'lua-prof-'));
  const p = path.join(root, 'profile.json');
  const svc = new ProfileService(p);
  await svc.save({
    name: 'Ada',
    avatarPath: 'C:\\a.png',
    featuredTrophiesByGame: { '570': ['NEW_ACH'] }
  });
  const loaded = await svc.load();
  assert.equal(loaded.name, 'Ada');
  assert.equal(loaded.avatarPath, 'C:\\a.png');
  assert.deepEqual(loaded.featuredTrophiesByGame, { '570': ['NEW_ACH'] });
});

test('profile partial JSON normalizes missing fields', () => {
  const profile = normalizeUserProfile({ name: '  ', featuredTrophiesByGame: undefined });
  assert.equal(profile.name, 'Player');
  assert.deepEqual(profile.featuredTrophiesByGame, {});
});

test('profile load on corrupt JSON returns defaults', async () => {
  const root = await fs.mkdtemp(path.join(os.tmpdir(), 'lua-prof-'));
  const p = path.join(root, 'profile.json');
  await fs.writeFile(p, '{"name":', 'utf8');
  const svc = new ProfileService(p);
  const profile = await svc.load();
  assert.equal(profile.name, 'Player');
});
