import type { RawAchievement } from '../../../shared/types.ts';
import { iniKeyGet, parseIniFile } from './iniHelper.ts';

export function parseCodexAchievements(filePath: string): Record<string, RawAchievement> {
  const result: Record<string, RawAchievement> = {};

  try {
    const ini = parseIniFile(filePath);
    for (const [apiName, keys] of ini) {
      const unlockRaw = iniKeyGet(keys, 'UnlockTime');
      let unlockTime = 0;
      if (unlockRaw !== undefined) {
        unlockTime = Number.parseInt(unlockRaw, 10) || 0;
      }

      let curProgress = 0;
      const cp = iniKeyGet(keys, 'CurProgress');
      if (cp !== undefined) {
        curProgress = Number.parseInt(cp, 10) || 0;
      }

      let maxProgress = 0;
      const mp = iniKeyGet(keys, 'MaxProgress');
      if (mp !== undefined) {
        maxProgress = Number.parseInt(mp, 10) || 0;
      }

      let achieved = unlockTime > 0;
      if (!achieved && maxProgress > 0 && curProgress > 0 && curProgress === maxProgress) {
        achieved = true;
      }

      result[apiName] = {
        achieved,
        unlockTime,
        curProgress,
        maxProgress
      };
    }
  } catch {
    return result;
  }

  return result;
}
