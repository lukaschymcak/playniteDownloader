import type { RawAchievement } from '../../../shared/types.ts';
import { WinRegAdapter } from '../registryAdapter.ts';

export interface GreenLumaRegistryAccess {
  listValueNames(keyPath: string): Promise<string[]>;
  getDword(keyPath: string, valueName: string): Promise<number | null>;
}

function achievementsKeyFromEncoded(encodedPath: string): string | null {
  const pipe = encodedPath.indexOf('|');
  if (pipe < 0) return null;
  const root = encodedPath.slice(0, pipe).trim();
  const keyPath = encodedPath.slice(pipe + 1).trim();
  if (!keyPath) return null;
  if (root.toUpperCase() !== 'HKCU') {
    return null;
  }
  return keyPath;
}

export function createGreenLumaRegistryAccess(adapter: WinRegAdapter): GreenLumaRegistryAccess {
  return {
    listValueNames: (p) => adapter.listValueNames(p),
    getDword: async (p, n) => {
      const v = await adapter.getValue(p, n);
      if (v === null) return null;
      if (typeof v === 'number') return Number.isFinite(v) ? Math.trunc(v) : null;
      const num = Number(v);
      return Number.isFinite(num) ? Math.trunc(num) : null;
    }
  };
}

export async function parseGreenLumaAchievements(
  encodedPath: string,
  reg: GreenLumaRegistryAccess
): Promise<Record<string, RawAchievement>> {
  const result: Record<string, RawAchievement> = {};
  try {
    const keyPath = achievementsKeyFromEncoded(encodedPath);
    if (!keyPath) {
      return result;
    }

    const valueNames = await reg.listValueNames(keyPath);
    for (const valueName of valueNames) {
      if (valueName.toLowerCase().endsWith('_time')) {
        continue;
      }

      const stateRaw = await reg.getDword(keyPath, valueName);
      const state = stateRaw === null ? 0 : stateRaw;

      let timestamp = 0;
      const timeRaw = await reg.getDword(keyPath, `${valueName}_Time`);
      if (timeRaw !== null) {
        timestamp = timeRaw;
      }

      result[valueName] = {
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
