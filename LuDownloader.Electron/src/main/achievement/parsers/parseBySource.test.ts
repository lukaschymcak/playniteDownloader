import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { SourceId } from '../../../shared/types.ts';
import { parseAchievementsBySource } from './parseBySource.ts';

test('parseAchievementsBySource maps Goldberg and Darksiders to expected parsers', async () => {
  const dir = await fs.mkdtemp(path.join(os.tmpdir(), 'lud-dispatch-'));
  try {
    const goldbergPath = path.join(dir, 'achievements.json');
    await fs.writeFile(goldbergPath, JSON.stringify({ z: { earned: true, earned_time: 1 } }), 'utf8');
    const g = parseAchievementsBySource(SourceId.Goldberg, goldbergPath);
    assert.equal(g.z!.achieved, true);

    const hlmPath = path.join(dir, 'hlm.ini');
    await fs.writeFile(hlmPath, ['[Achievements]', 'k=1', '[AchievementsUnlockTimes]', 'k=2'].join('\n'), 'utf8');
    const d = parseAchievementsBySource(SourceId.Darksiders, hlmPath);
    assert.equal(d.k!.unlockTime, 2);

    const skPath = path.join(dir, 'sk.ini');
    await fs.writeFile(skPath, ['[AchievementsUnlockTimes]', 'k=3'].join('\n'), 'utf8');
    const s = parseAchievementsBySource(SourceId.Skidrow, skPath);
    assert.equal(s.k!.unlockTime, 3);

    assert.deepEqual(parseAchievementsBySource(SourceId.GreenLuma, 'any'), {});
  } finally {
    await fs.rm(dir, { recursive: true, force: true });
  }
});
