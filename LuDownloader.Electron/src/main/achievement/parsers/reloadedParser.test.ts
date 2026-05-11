import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { parseReloadedAchievements } from './reloadedParser.ts';

test('Reloaded 3DM State Time branch', async () => {
  const dir = await fs.mkdtemp(path.join(os.tmpdir(), 'lud-rld3-'));
  try {
    const p = path.join(dir, 'achievements.ini');
    await fs.writeFile(
      p,
      ['[State]', 'my=0101', '[Time]', 'my=01020304'].join('\n'),
      'utf8'
    );
    const r = parseReloadedAchievements(p);
    assert.equal(r.my!.achieved, true);
    assert.equal(r.my!.unlockTime, 0x04030201);
  } finally {
    await fs.rm(dir, { recursive: true, force: true });
  }
});

test('Reloaded RLD per-section hex fields', async () => {
  const dir = await fs.mkdtemp(path.join(os.tmpdir(), 'lud-rldr-'));
  try {
    const p = path.join(dir, 'achievements.ini');
    await fs.writeFile(
      p,
      [
        '[ach]',
        'State=01000000',
        'Time=00000000',
        'CurProgress=02000000',
        'MaxProgress=0a000000'
      ].join('\n'),
      'utf8'
    );
    const r = parseReloadedAchievements(p);
    assert.equal(r.ach!.achieved, true);
    assert.equal(r.ach!.curProgress, 2);
    assert.equal(r.ach!.maxProgress, 10);
  } finally {
    await fs.rm(dir, { recursive: true, force: true });
  }
});
