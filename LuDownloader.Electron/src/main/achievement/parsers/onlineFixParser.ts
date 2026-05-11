import type { RawAchievement } from '../../../shared/types.ts';
import { iniKeyGet, parseIniFile } from './iniHelper.ts';

export function parseOnlineFixAchievements(filePath: string): Record<string, RawAchievement> {
  const result: Record<string, RawAchievement> = {};

  try {
    const ini = parseIniFile(filePath);
    for (const [apiName, keys] of ini) {
      let achieved = false;
      const ar = iniKeyGet(keys, 'achieved');
      if (ar !== undefined) {
        const val = ar.toLowerCase();
        achieved = val === 'true' || val === '1';
      }

      let timestamp = 0;
      const ts = iniKeyGet(keys, 'timestamp');
      if (ts !== undefined) {
        timestamp = Number.parseInt(ts, 10) || 0;
      }

      result[apiName] = {
        achieved,
        unlockTime: achieved ? timestamp : 0,
        curProgress: 0,
        maxProgress: 0
      };
    }
  } catch {
    return result;
  }

  return result;
}
