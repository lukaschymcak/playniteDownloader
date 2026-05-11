import test from 'node:test';
import assert from 'node:assert/strict';
import { parseGreenLumaAchievements, type GreenLumaRegistryAccess } from './greenLumaParser.ts';

test('parseGreenLumaAchievements reads DWORD pairs and skips *_Time value names', async () => {
  const storage = new Map<string, number>();

  storage.set('ACH1', 1);
  storage.set('ACH1_Time', 12345);
  storage.set('ACH2', 0);
  storage.set('ACH2_Time', 888);

  const reg: GreenLumaRegistryAccess = {
    async listValueNames(): Promise<string[]> {
      return ['ACH1', 'ACH1_Time', 'ACH2'];
    },
    async getDword(_keyPath: string, valueName: string): Promise<number | null> {
      return storage.has(valueName) ? storage.get(valueName)! : null;
    }
  };

  const r = await parseGreenLumaAchievements('HKCU|SOFTWARE\\GLR\\AppID\\1\\Achievements', reg);
  assert.equal(r.ACH1!.achieved, true);
  assert.equal(r.ACH1!.unlockTime, 12345);
  assert.equal(r.ACH2!.achieved, false);
  assert.equal(r.ACH2!.unlockTime, 0);
});

test('parseGreenLumaAchievements rejects non-HKCU prefix', async () => {
  const reg: GreenLumaRegistryAccess = {
    async listValueNames(): Promise<string[]> {
      throw new Error('should not list');
    },
    async getDword(): Promise<number | null> {
      return null;
    }
  };
  assert.deepEqual(await parseGreenLumaAchievements('HKLM|\\\\x', reg), {});
});
