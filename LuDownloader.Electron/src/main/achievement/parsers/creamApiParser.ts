import type { RawAchievement } from '../../../shared/types.ts';
import { iniKeyGet, parseIniFile } from './iniHelper.ts';

export function parseCreamApiAchievements(filePath: string): Record<string, RawAchievement> {
  const result: Record<string, RawAchievement> = {};

  try {
    const ini = parseIniFile(filePath);
    for (const [apiName, keys] of ini) {
      let achieved = 0;
      const ach = iniKeyGet(keys, 'achieved');
      if (ach !== undefined) {
        achieved = Number.parseInt(ach, 10) || 0;
      }

      let unlockTime = 0;
      const utRaw = iniKeyGet(keys, 'unlocktime');
      if (utRaw !== undefined) {
        const raw = utRaw;
        const parsed = Number.parseInt(raw, 10);
        if (!Number.isNaN(parsed)) {
          unlockTime = parsed;
          const trimmed = raw.trim();
          const magnitude = trimmed.startsWith('-') ? trimmed.slice(1) : trimmed;
          if (magnitude.length === 7) {
            unlockTime *= 1000;
          }
        }
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

      result[apiName] = {
        achieved: achieved === 1,
        unlockTime: achieved === 1 ? unlockTime : 0,
        curProgress,
        maxProgress
      };
    }
  } catch {
    return result;
  }

  return result;
}
