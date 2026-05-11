import { existsSync, readFileSync } from 'node:fs';
import type { RawAchievement } from '../../../shared/types.ts';

export function parseGoldbergAchievements(filePath: string): Record<string, RawAchievement> {
  const result: Record<string, RawAchievement> = {};
  if (!existsSync(filePath)) {
    return result;
  }

  try {
    const text = readFileSync(filePath, 'utf8');
    const root = JSON.parse(text) as Record<string, unknown>;
    if (!root || typeof root !== 'object' || Array.isArray(root)) {
      return result;
    }

    for (const [apiName, token] of Object.entries(root)) {
      try {
        if (!token || typeof token !== 'object' || Array.isArray(token)) {
          continue;
        }
        const obj = token as Record<string, unknown>;
        const earned = obj['earned'] != null && Boolean(obj['earned']);
        const t = obj['earned_time'];
        const earnedTime = typeof t === 'number' ? t : Number(t) || 0;

        result[apiName] = {
          achieved: earned,
          unlockTime: earned ? earnedTime : 0,
          curProgress: 0,
          maxProgress: 0
        };
      } catch {
        /* skip entry */
      }
    }
  } catch {
    return result;
  }

  return result;
}
