import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import type { Achievement, GameAchievements, GameAchievementSnapshot } from '../../shared/types.ts';
import {
  diffAndUpdateSnapshot,
  diffFromSnapshot,
  loadSnapshot,
  normalizeGameAchievementSnapshot,
  saveSnapshot,
  snapshotFromGameAchievements
} from './achievementSnapshotService.ts';

function ach(overrides: Partial<Achievement> & Pick<Achievement, 'apiName'>): Achievement {
  const base: Achievement = {
    apiName: overrides.apiName,
    gameName: 'G',
    displayName: overrides.displayName ?? overrides.apiName,
    description: '',
    hidden: false,
    achieved: false,
    unlockTime: 0,
    curProgress: 0,
    maxProgress: 0,
    globalPercentage: 0
  };
  return { ...base, ...overrides };
}

function game(appId: string, list: Achievement[]): GameAchievements {
  const unlockedCount = list.filter((a) => a.achieved).length;
  const totalCount = list.length;
  const percentage = totalCount > 0 ? (unlockedCount / totalCount) * 100 : 0;
  return {
    appId,
    gameName: 'G',
    hasUsableInstallDirectory: false,
    source: 0,
    list,
    unlockedCount,
    totalCount,
    percentage,
    hasPlatinum: false
  };
}

test('diffFromSnapshot first run yields no diffs', () => {
  const current = game('1', [ach({ apiName: 'A', achieved: true })]);
  assert.deepEqual(diffFromSnapshot(null, current), []);
});

test('diffFromSnapshot detects new unlock', () => {
  const previous: GameAchievementSnapshot = {
    appId: '1',
    capturedAtUnix: 0,
    achievements: { A: { achieved: false, unlockTime: 0, curProgress: 0, maxProgress: 10 } }
  };
  const a = ach({ apiName: 'A', achieved: true, unlockTime: 9, curProgress: 10, maxProgress: 10 });
  const current = game('1', [a]);
  const diffs = diffFromSnapshot(previous, current);
  assert.equal(diffs.length, 1);
  assert.equal(diffs[0]!.isNewUnlock, true);
  assert.equal(diffs[0]!.isProgressMilestone, false);
  assert.equal(diffs[0]!.achievement.apiName, 'A');
});

test('diffFromSnapshot progress milestone when prior row existed', () => {
  const previous: GameAchievementSnapshot = {
    appId: '1',
    capturedAtUnix: 0,
    achievements: { A: { achieved: false, unlockTime: 0, curProgress: 1, maxProgress: 10 } }
  };
  const a = ach({ apiName: 'A', achieved: false, curProgress: 5, maxProgress: 10 });
  const diffs = diffFromSnapshot(previous, game('1', [a]));
  assert.equal(diffs.length, 1);
  assert.equal(diffs[0]!.isNewUnlock, false);
  assert.equal(diffs[0]!.isProgressMilestone, true);
  assert.equal(diffs[0]!.oldProgress, 10);
  assert.equal(diffs[0]!.newProgress, 50);
});

test('diffFromSnapshot no diff when unchanged', () => {
  const previous: GameAchievementSnapshot = {
    appId: '1',
    capturedAtUnix: 0,
    achievements: { A: { achieved: false, unlockTime: 0, curProgress: 2, maxProgress: 10 } }
  };
  const a = ach({ apiName: 'A', achieved: false, curProgress: 2, maxProgress: 10 });
  assert.deepEqual(diffFromSnapshot(previous, game('1', [a])), []);
});

test('diffFromSnapshot no milestone when progress decreases', () => {
  const previous: GameAchievementSnapshot = {
    appId: '1',
    capturedAtUnix: 0,
    achievements: { A: { achieved: false, unlockTime: 0, curProgress: 5, maxProgress: 10 } }
  };
  const a = ach({ apiName: 'A', achieved: false, curProgress: 3, maxProgress: 10 });
  assert.deepEqual(diffFromSnapshot(previous, game('1', [a])), []);
});

test('diffFromSnapshot skips brand-new apiName still locked even with progress', () => {
  const a = ach({ apiName: 'A', achieved: false, curProgress: 3, maxProgress: 10 });
  assert.deepEqual(diffFromSnapshot(null, game('1', [a])), []);
});

test('diffFromSnapshot skips new apiName still locked when baseline exists but key missing', () => {
  const previous: GameAchievementSnapshot = {
    appId: '1',
    capturedAtUnix: 0,
    achievements: { A: { achieved: false, unlockTime: 0, curProgress: 0, maxProgress: 0 } }
  };
  const list = [
    ach({ apiName: 'A', achieved: false }),
    ach({ apiName: 'B', achieved: false, curProgress: 4, maxProgress: 10 })
  ];
  assert.deepEqual(diffFromSnapshot(previous, game('1', list)), []);
});

test('normalizeGameAchievementSnapshot rejects malformed', () => {
  assert.equal(normalizeGameAchievementSnapshot(null), null);
  assert.equal(normalizeGameAchievementSnapshot({ appId: '', achievements: {} }), null);
});

test('loadSnapshot saveSnapshot roundtrip', async () => {
  const root = await fs.mkdtemp(path.join(os.tmpdir(), 'lua-snap-'));
  try {
    const snap = snapshotFromGameAchievements(
      game('42', [ach({ apiName: 'X', achieved: true, maxProgress: 0 })])
    );
    await saveSnapshot(root, snap);
    const loaded = await loadSnapshot(root, '42');
    assert.deepEqual(loaded?.achievements.X, snap.achievements.X);
  } finally {
    await fs.rm(root, { recursive: true, force: true });
  }
});

test('diffAndUpdateSnapshot replaces corrupt file', async () => {
  const root = await fs.mkdtemp(path.join(os.tmpdir(), 'lua-snap-bad-'));
  try {
    const snapPath = path.join(root, '7', 'snapshot.json');
    await fs.mkdir(path.dirname(snapPath), { recursive: true });
    await fs.writeFile(snapPath, '{ not json', 'utf8');
    const current = game('7', [ach({ apiName: 'A', achieved: false })]);
    const diffs = await diffAndUpdateSnapshot(root, current);
    assert.deepEqual(diffs, []);
    const loaded = await loadSnapshot(root, '7');
    assert.equal(loaded?.appId, '7');
    assert.equal(loaded?.achievements.A?.achieved, false);
  } finally {
    await fs.rm(root, { recursive: true, force: true });
  }
});
