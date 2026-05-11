import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { parseGoldbergAchievements } from './goldbergParser.ts';

test('parseGoldbergAchievements maps earned and earned_time; skips bad entries', async () => {
  const dir = await fs.mkdtemp(path.join(os.tmpdir(), 'lud-gberg-'));
  try {
    const filePath = path.join(dir, 'achievements.json');
    await fs.writeFile(
      filePath,
      JSON.stringify({
        a: { earned: true, earned_time: 100 },
        b: { earned: false, earned_time: 200 },
        bad: 'x',
        c: { earned: true, earned_time: '50' }
      }),
      'utf8'
    );
    const r = parseGoldbergAchievements(filePath);
    assert.equal(r.a!.achieved, true);
    assert.equal(r.a!.unlockTime, 100);
    assert.equal(r.b!.achieved, false);
    assert.equal(r.b!.unlockTime, 0);
    assert.ok(!('bad' in r));
    assert.equal(r.c!.unlockTime, 50);
  } finally {
    await fs.rm(dir, { recursive: true, force: true });
  }
});

test('parseGoldbergAchievements missing file returns empty', () => {
  assert.deepEqual(parseGoldbergAchievements(path.join(os.tmpdir(), 'missing-gberg.json')), {});
});
