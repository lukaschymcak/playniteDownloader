import type { RawAchievement } from '../../../shared/types.ts';
import { getIniSection, hasIniSections, iniKeyGet, parseIniFile } from './iniHelper.ts';

function hexToUInt32Le(hex: string): number {
  if (!hex || hex.length < 8) {
    return 0;
  }
  try {
    const bytes = Buffer.alloc(4);
    for (let i = 0; i < 4; i++) {
      bytes[i] = Number.parseInt(hex.slice(i * 2, i * 2 + 2), 16);
    }
    return bytes.readUInt32LE(0);
  } catch {
    return 0;
  }
}

export function parseReloadedAchievements(filePath: string): Record<string, RawAchievement> {
  const result: Record<string, RawAchievement> = {};

  try {
    const ini = parseIniFile(filePath);

    if (hasIniSections(ini, 'State', 'Time')) {
      const stateSection = getIniSection(ini, 'State')!;
      const timeSection = getIniSection(ini, 'Time')!;

      for (const [apiName, stateVal] of stateSection) {
        const achieved = stateVal === '0101';
        let timestamp = 0;
        const timeRaw = iniKeyGet(timeSection, apiName);
        if (timeRaw !== undefined) {
          timestamp = hexToUInt32Le(timeRaw);
        }
        result[apiName] = {
          achieved,
          unlockTime: achieved ? timestamp : 0,
          curProgress: 0,
          maxProgress: 0
        };
      }
    } else {
      for (const [apiName, keys] of ini) {
        let stateVal = 0;
        const st = iniKeyGet(keys, 'State');
        if (st !== undefined) {
          stateVal = hexToUInt32Le(st);
        }

        let timeVal = 0;
        const tm = iniKeyGet(keys, 'Time');
        if (tm !== undefined) {
          timeVal = hexToUInt32Le(tm);
        }

        let curVal = 0;
        const cr = iniKeyGet(keys, 'CurProgress');
        if (cr !== undefined) {
          curVal = hexToUInt32Le(cr);
        }

        let maxVal = 0;
        const mx = iniKeyGet(keys, 'MaxProgress');
        if (mx !== undefined) {
          maxVal = hexToUInt32Le(mx);
        }

        const achieved = stateVal === 1;
        result[apiName] = {
          achieved,
          unlockTime: achieved ? timeVal : 0,
          curProgress: curVal,
          maxProgress: maxVal
        };
      }
    }
  } catch {
    return result;
  }

  return result;
}
