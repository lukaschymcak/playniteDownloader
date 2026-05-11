import type { RawAchievement } from '../../../shared/types.ts';
import { iniKeyGet, parseIniFile } from './iniHelper.ts';

export function parseAli213Achievements(filePath: string): Record<string, RawAchievement> {
  const result: Record<string, RawAchievement> = {};

  try {
    const ini = parseIniFile(filePath);
    for (const [apiName, keys] of ini) {
      let haveAchieved = 0;
      const ha = iniKeyGet(keys, 'HaveAchieved');
      if (ha !== undefined) {
        haveAchieved = Number.parseInt(ha, 10) || 0;
      }

      let haveAchievedTime = 0;
      const hat = iniKeyGet(keys, 'HaveAchievedTime');
      if (hat !== undefined) {
        haveAchievedTime = Number.parseInt(hat, 10) || 0;
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
        achieved: haveAchieved === 1,
        unlockTime: haveAchieved === 1 ? haveAchievedTime : 0,
        curProgress,
        maxProgress
      };
    }
  } catch {
    return result;
  }

  return result;
}
