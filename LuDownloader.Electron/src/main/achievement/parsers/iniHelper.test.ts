import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { getIniSection, iniKeyGet, parseIniFile } from './iniHelper.ts';

test('parseIniFile handles comments empty lines and last value wins per key', async () => {
  const dir = await fs.mkdtemp(path.join(os.tmpdir(), 'lud-ini-'));
  try {
    const filePath = path.join(dir, 't.ini');
    await fs.writeFile(
      filePath,
      ['; head', '', '[Main]', 'a=1', 'A=2', '# c', '[other]', 'x = y '].join('\n'),
      'utf8'
    );
    const ini = parseIniFile(filePath);
    const main = getIniSection(ini, 'main');
    assert.ok(main);
    assert.equal(iniKeyGet(main!, 'a'), '2');
    const other = getIniSection(ini, 'OTHER');
    assert.equal(iniKeyGet(other!, 'x'), 'y');
  } finally {
    await fs.rm(dir, { recursive: true, force: true });
  }
});

test('parseIniFile ignores keys before first section', async () => {
  const tmpRoot = await fs.mkdtemp(path.join(os.tmpdir(), 'lud-ini2-'));
  try {
    const filePath = path.join(tmpRoot, 't.ini');
    await fs.writeFile(filePath, 'orphan=1\n[S]\nk=v\n', 'utf8');
    const ini = parseIniFile(filePath);
    assert.equal(ini.size, 1);
    assert.equal(iniKeyGet(getIniSection(ini, 's')!, 'k'), 'v');
  } finally {
    await fs.rm(tmpRoot, { recursive: true, force: true });
  }
});
