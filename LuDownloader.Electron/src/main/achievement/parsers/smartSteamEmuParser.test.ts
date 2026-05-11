import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { computeSteamEmuCrc32Utf8, parseSmartSteamEmuAchievements } from './smartSteamEmuParser.ts';

test('parseSmartSteamEmuAchievements reads one record and uses lowercase hex crc key', async () => {
  const dir = await fs.mkdtemp(path.join(os.tmpdir(), 'lud-sse-'));
  try {
    const api = 'MY_ACH';
    const crc = computeSteamEmuCrc32Utf8(api);
    const crcHex = crc.toString(16);
    const unlock = 170;
    const stateAchieved = 1;
    const buf = Buffer.alloc(4 + 24);
    buf.writeInt32LE(1, 0);
    buf.writeUInt32LE(crc, 4);
    buf.writeInt32LE(unlock, 12);
    buf.writeInt32LE(stateAchieved, 24);

    const filePath = path.join(dir, 'stats.bin');
    await fs.writeFile(filePath, buf);

    const r = parseSmartSteamEmuAchievements(filePath);
    assert.ok(r[crcHex]);
    assert.equal(r[crcHex]!.achieved, true);
    assert.equal(r[crcHex]!.unlockTime, unlock);
  } finally {
    await fs.rm(dir, { recursive: true, force: true });
  }
});

test('parseSmartSteamEmuAchievements count mismatch returns empty', async () => {
  const dir = await fs.mkdtemp(path.join(os.tmpdir(), 'lud-sse2-'));
  try {
    const filePath = path.join(dir, 'stats.bin');
    const buf = Buffer.alloc(8);
    buf.writeInt32LE(99, 0);
    buf.writeInt32LE(0, 4);
    await fs.writeFile(filePath, buf);
    assert.deepEqual(parseSmartSteamEmuAchievements(filePath), {});
  } finally {
    await fs.rm(dir, { recursive: true, force: true });
  }
});

test('parseSmartSteamEmuAchievements skips stateValue > 1', async () => {
  const dir = await fs.mkdtemp(path.join(os.tmpdir(), 'lud-sse3-'));
  try {
    const crc = 0xabcdef01;
    const buf = Buffer.alloc(4 + 24);
    buf.writeInt32LE(1, 0);
    buf.writeUInt32LE(crc, 4);
    buf.writeInt32LE(0, 12);
    buf.writeInt32LE(9, 24);
    const filePath = path.join(dir, 'stats.bin');
    await fs.writeFile(filePath, buf);
    assert.deepEqual(parseSmartSteamEmuAchievements(filePath), {});
  } finally {
    await fs.rm(dir, { recursive: true, force: true });
  }
});
