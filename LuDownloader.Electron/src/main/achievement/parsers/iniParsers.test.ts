import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { parseAli213Achievements } from './ali213Parser.ts';
import { parseCodexAchievements } from './codexParser.ts';
import { parseCreamApiAchievements } from './creamApiParser.ts';
import { parseHoodlumAchievements } from './hoodlumParser.ts';
import { parseOnlineFixAchievements } from './onlineFixParser.ts';
import { parseSkidrowAchievements } from './skidrowParser.ts';

test('Codex progress complete with UnlockTime 0 still achieved', async () => {
  const dir = await fs.mkdtemp(path.join(os.tmpdir(), 'lud-codex-'));
  try {
    const p = path.join(dir, 'achievements.ini');
    await fs.writeFile(
      p,
      ['[achi1]', 'UnlockTime=0', 'CurProgress=10', 'MaxProgress=10'].join('\n'),
      'utf8'
    );
    const r = parseCodexAchievements(p);
    assert.equal(r.achi1!.achieved, true);
    assert.equal(r.achi1!.unlockTime, 0);
    assert.equal(r.achi1!.curProgress, 10);
  } finally {
    await fs.rm(dir, { recursive: true, force: true });
  }
});

test('CreamApi 7-digit unlocktime scales by 1000', async () => {
  const dir = await fs.mkdtemp(path.join(os.tmpdir(), 'lud-cream-'));
  try {
    const p = path.join(dir, 'CreamAPI.Achievements.cfg');
    await fs.writeFile(
      p,
      ['[x]', 'achieved=1', 'unlocktime=1234567', 'CurProgress=0', 'MaxProgress=0'].join('\n'),
      'utf8'
    );
    const r = parseCreamApiAchievements(p);
    assert.equal(r.x!.unlockTime, 1234567000);
  } finally {
    await fs.rm(dir, { recursive: true, force: true });
  }
});

test('Hoodlum pairs Achievements and AchievementsUnlockTimes', async () => {
  const dir = await fs.mkdtemp(path.join(os.tmpdir(), 'lud-hood-'));
  try {
    const p = path.join(dir, 'hlm.ini');
    await fs.writeFile(
      p,
      ['[Achievements]', 'k=1', '[AchievementsUnlockTimes]', 'k=42'].join('\n'),
      'utf8'
    );
    const r = parseHoodlumAchievements(p);
    assert.equal(r.k!.achieved, true);
    assert.equal(r.k!.unlockTime, 42);
  } finally {
    await fs.rm(dir, { recursive: true, force: true });
  }
});

test('Skidrow uses AchievementsUnlockTimes timestamps only', async () => {
  const dir = await fs.mkdtemp(path.join(os.tmpdir(), 'lud-skid-'));
  try {
    const p = path.join(dir, 'achiev.ini');
    await fs.writeFile(p, ['[AchievementsUnlockTimes]', 'a=0', 'b=99'].join('\n'), 'utf8');
    const r = parseSkidrowAchievements(p);
    assert.equal(r.a!.achieved, false);
    assert.equal(r.b!.achieved, true);
    assert.equal(r.b!.unlockTime, 99);
  } finally {
    await fs.rm(dir, { recursive: true, force: true });
  }
});

test('OnlineFix accepts true and 1', async () => {
  const dir = await fs.mkdtemp(path.join(os.tmpdir(), 'lud-of-'));
  try {
    const p = path.join(dir, 'achievements.ini');
    await fs.writeFile(
      p,
      ['[o1]', 'achieved=true', 'timestamp=5', '[o2]', 'achieved=1', 'timestamp=6'].join('\n'),
      'utf8'
    );
    const r = parseOnlineFixAchievements(p);
    assert.equal(r.o1!.unlockTime, 5);
    assert.equal(r.o2!.achieved, true);
    assert.equal(r.o2!.unlockTime, 6);
  } finally {
    await fs.rm(dir, { recursive: true, force: true });
  }
});

test('Ali213 HaveAchieved and time', async () => {
  const dir = await fs.mkdtemp(path.join(os.tmpdir(), 'lud-ali-'));
  try {
    const p = path.join(dir, 'Achievements.Bin');
    await fs.writeFile(
      p,
      ['[x]', 'HaveAchieved=1', 'HaveAchievedTime=77', 'CurProgress=1', 'MaxProgress=2'].join('\n'),
      'utf8'
    );
    const r = parseAli213Achievements(p);
    assert.equal(r.x!.achieved, true);
    assert.equal(r.x!.unlockTime, 77);
    assert.equal(r.x!.maxProgress, 2);
  } finally {
    await fs.rm(dir, { recursive: true, force: true });
  }
});
