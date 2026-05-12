import type { GameAchievements } from '../../../shared/types';

export type TrophyGrade = 'gold' | 'silver' | 'bronze';

export function gradeAchievement(globalPercentage: number): TrophyGrade {
  if (globalPercentage > 0 && globalPercentage < 15) return 'gold';
  if (globalPercentage >= 15 && globalPercentage < 30) return 'silver';
  return 'bronze';
}

export function achievementPoints(globalPercentage: number): number {
  const g = gradeAchievement(globalPercentage);
  if (g === 'gold') return 100;
  if (g === 'silver') return 50;
  return 10;
}

export interface TrophyCounts {
  platinum: number;
  gold: number;
  silver: number;
  bronze: number;
}

export interface PlayerStats {
  trophyCounts: TrophyCounts;
  totalPoints: number;
  level: number;
}

export function computePlayerStats(games: GameAchievements[]): PlayerStats {
  let platinum = 0, gold = 0, silver = 0, bronze = 0, totalPoints = 0;
  for (const game of games) {
    if (game.hasPlatinum) {
      platinum++;
      totalPoints += 200;
    }
    for (const ach of game.list) {
      if (!ach.achieved) continue;
      const g = gradeAchievement(ach.globalPercentage);
      if (g === 'gold') { gold++; totalPoints += 100; }
      else if (g === 'silver') { silver++; totalPoints += 50; }
      else { bronze++; totalPoints += 10; }
    }
  }
  return {
    trophyCounts: { platinum, gold, silver, bronze },
    totalPoints,
    level: Math.floor(1 + totalPoints / 500)
  };
}

export interface MonthBucket {
  label: string;
  count: number;
}

export function buildMonthlyUnlockData(games: GameAchievements[]): MonthBucket[] {
  const now = new Date();
  const buckets = new Map<string, number>();
  for (let i = 11; i >= 0; i--) {
    const d = new Date(now.getFullYear(), now.getMonth() - i, 1);
    const key = `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`;
    buckets.set(key, 0);
  }
  for (const game of games) {
    for (const ach of game.list) {
      if (!ach.achieved || !ach.unlockTime) continue;
      const d = new Date(ach.unlockTime * 1000);
      const key = `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`;
      const existing = buckets.get(key);
      if (existing !== undefined) buckets.set(key, existing + 1);
    }
  }
  return [...buckets.entries()].map(([key, count]) => ({
    label: key.slice(5),
    count
  }));
}
