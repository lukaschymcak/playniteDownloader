import type { RawAchievement } from '../../../shared/types.ts';
import { getIniSection, parseIniFile } from './iniHelper.ts';

export function parseSkidrowAchievements(filePath: string): Record<string, RawAchievement> {
  const result: Record<string, RawAchievement> = {};

  try {
    const ini = parseIniFile(filePath);
    const sec = getIniSection(ini, 'AchievementsUnlockTimes');
    if (!sec) {
      return result;
    }

    for (const [apiName, raw] of sec) {
      const timestamp = Number.parseInt(raw, 10) || 0;
      result[apiName] = {
        achieved: timestamp > 0,
        unlockTime: timestamp,
        curProgress: 0,
        maxProgress: 0
      };
    }
  } catch {
    return result;
  }

  return result;
}
