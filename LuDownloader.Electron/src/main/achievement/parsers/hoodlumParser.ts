import type { RawAchievement } from '../../../shared/types.ts';
import { getIniSection, iniKeyGet, parseIniFile } from './iniHelper.ts';

export function parseHoodlumAchievements(filePath: string): Record<string, RawAchievement> {
  const result: Record<string, RawAchievement> = {};

  try {
    const ini = parseIniFile(filePath);
    const achievements = getIniSection(ini, 'Achievements');
    if (!achievements) {
      return result;
    }

    const timestamps = getIniSection(ini, 'AchievementsUnlockTimes') ?? new Map<string, string>();

    for (const [apiName, rawState] of achievements) {
      let state = 0;
      state = Number.parseInt(rawState, 10) || 0;

      let timestamp = 0;
      const ts = iniKeyGet(timestamps, apiName);
      if (ts !== undefined) {
        timestamp = Number.parseInt(ts, 10) || 0;
      }

      result[apiName] = {
        achieved: state === 1,
        unlockTime: state === 1 ? timestamp : 0,
        curProgress: 0,
        maxProgress: 0
      };
    }
  } catch {
    return result;
  }

  return result;
}
