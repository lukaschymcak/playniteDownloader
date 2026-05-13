import type { GameAchievements } from '../../../shared/types';

export type TrophyGrade = 'gold' | 'silver' | 'bronze';

export function gradeAchievement(globalPercentage: number): TrophyGrade {
  if (globalPercentage > 0 && globalPercentage < 20) return 'gold';
  if (globalPercentage >= 20 && globalPercentage < 40) return 'silver';
  return 'bronze';
}

export function achievementPoints(globalPercentage: number): number {
  const g = gradeAchievement(globalPercentage);
  if (g === 'gold') return 200;
  if (g === 'silver') return 100;
  return 50;
}

export const PLATINUM_XP = 500;

export interface TrophyCounts {
  platinum: number;
  gold: number;
  silver: number;
  bronze: number;
}

export interface PlayerStats {
  trophyCounts: TrophyCounts;
  totalPoints: number;
  totalUnlocked: number;
  totalAchievements: number;
  level: number;
  levelXp: number;
  levelSize: number;
  toNext: number;
}

export function computePlayerStats(games: GameAchievements[]): PlayerStats {
  let platinum = 0, gold = 0, silver = 0, bronze = 0, totalPoints = 0;
  let totalUnlocked = 0, totalAchievements = 0;
  for (const game of games) {
    totalAchievements += game.totalCount;
    totalUnlocked += game.unlockedCount;
    if (game.hasPlatinum) {
      platinum++;
      totalPoints += PLATINUM_XP;
    }
    for (const ach of game.list) {
      if (!ach.achieved) continue;
      const g = gradeAchievement(ach.globalPercentage);
      if (g === 'gold') { gold++; totalPoints += 200; }
      else if (g === 'silver') { silver++; totalPoints += 100; }
      else { bronze++; totalPoints += 50; }
    }
  }
  const levelSize = 1000;
  const level = Math.floor(1 + totalPoints / levelSize);
  const levelXp = totalPoints % levelSize;
  const toNext = levelSize - levelXp;
  return {
    trophyCounts: { platinum, gold, silver, bronze },
    totalPoints,
    totalUnlocked,
    totalAchievements,
    level,
    levelXp,
    levelSize,
    toNext
  };
}

export interface MonthBucket {
  label: string;
  count: number;
}

/** Legacy 12-month line chart data. */
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

export interface MonthBarBucket {
  label: string;
  count: number;
  highlight: boolean;
}

const MONTH_LABELS = ['Jan','Feb','Mar','Apr','May','Jun','Jul','Aug','Sep','Oct','Nov','Dec'];

export function buildMonthlyBarData(games: GameAchievements[], months = 6): MonthBarBucket[] {
  const now = new Date();
  const currentKey = `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}`;
  const buckets = new Map<string, number>();
  for (let i = months - 1; i >= 0; i--) {
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
    label: MONTH_LABELS[parseInt(key.slice(5)) - 1],
    count,
    highlight: key === currentKey
  }));
}

export interface RecentUnlock {
  apiName: string;
  displayName: string;
  gameName: string;
  appId: string;
  grade: TrophyGrade;
  globalPercentage: number;
  unlockTime: number;
  xp: number;
}

export function buildRecentUnlocks(games: GameAchievements[], limit = 10): RecentUnlock[] {
  const items: RecentUnlock[] = [];
  for (const game of games) {
    for (const ach of game.list) {
      if (!ach.achieved || !ach.unlockTime) continue;
      const grade = gradeAchievement(ach.globalPercentage);
      items.push({
        apiName: ach.apiName,
        displayName: ach.displayName,
        gameName: game.gameName,
        appId: game.appId,
        grade,
        globalPercentage: ach.globalPercentage,
        unlockTime: ach.unlockTime,
        xp: achievementPoints(ach.globalPercentage)
      });
    }
  }
  items.sort((a, b) => b.unlockTime - a.unlockTime);
  return items.slice(0, limit);
}

/** Hue derived from appId for game art placeholder background. */
export function appIdToHue(appId: string): number {
  let h = 0;
  for (let i = 0; i < appId.length; i++) {
    h = (h * 31 + appId.charCodeAt(i)) & 0xffffffff;
  }
  return Math.abs(h) % 360;
}
