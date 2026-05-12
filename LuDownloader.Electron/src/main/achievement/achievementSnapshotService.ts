import { existsSync } from 'node:fs';
import fs from 'node:fs/promises';
import path from 'node:path';
import type {
  AchievementDiff,
  GameAchievementSnapshot,
  GameAchievements,
  SnapshotAchievementState
} from '../../shared/types.ts';

function progressRatioPercent(curProgress: number, maxProgress: number): number {
  if (maxProgress <= 0) {
    return 0;
  }
  return (curProgress / maxProgress) * 100;
}

export function snapshotFromGameAchievements(current: GameAchievements): GameAchievementSnapshot {
  const achievements: Record<string, SnapshotAchievementState> = {};
  for (const a of current.list) {
    achievements[a.apiName] = {
      achieved: a.achieved,
      unlockTime: a.unlockTime,
      curProgress: a.curProgress,
      maxProgress: a.maxProgress
    };
  }
  return {
    appId: current.appId,
    capturedAtUnix: Math.floor(Date.now() / 1000),
    achievements
  };
}

export function diffFromSnapshot(
  previous: GameAchievementSnapshot | null,
  current: GameAchievements
): AchievementDiff[] {
  if (previous === null) {
    return [];
  }

  const out: AchievementDiff[] = [];
  const prevMap = previous.achievements;

  for (const achievement of current.list) {
    const prev = prevMap ? prevMap[achievement.apiName] : undefined;

    const isNewUnlock = achievement.achieved && (!prev || !prev.achieved);

    const oldProgress = prev ? progressRatioPercent(prev.curProgress, prev.maxProgress) : 0;
    const newProgress = progressRatioPercent(achievement.curProgress, achievement.maxProgress);

    const isProgressMilestone =
      !isNewUnlock && prev !== undefined && achievement.maxProgress > 0 && newProgress > oldProgress;

    if (!isNewUnlock && !isProgressMilestone) {
      continue;
    }

    out.push({
      achievement,
      isNewUnlock,
      isProgressMilestone,
      oldProgress,
      newProgress
    });
  }

  return out;
}

function normalizeSnapshotAchievementState(raw: unknown): SnapshotAchievementState | null {
  if (!raw || typeof raw !== 'object' || Array.isArray(raw)) {
    return null;
  }
  const o = raw as Record<string, unknown>;
  const achieved = o.achieved === true;
  const unlockTime = typeof o.unlockTime === 'number' && Number.isFinite(o.unlockTime) ? o.unlockTime : 0;
  const curProgress = typeof o.curProgress === 'number' && Number.isFinite(o.curProgress) ? o.curProgress : 0;
  const maxProgress = typeof o.maxProgress === 'number' && Number.isFinite(o.maxProgress) ? o.maxProgress : 0;
  return { achieved, unlockTime, curProgress, maxProgress };
}

export function normalizeGameAchievementSnapshot(raw: unknown): GameAchievementSnapshot | null {
  if (!raw || typeof raw !== 'object' || Array.isArray(raw)) {
    return null;
  }
  const o = raw as Record<string, unknown>;
  const appId = typeof o.appId === 'string' ? o.appId.trim() : '';
  if (!appId) {
    return null;
  }
  const capturedAtUnix =
    typeof o.capturedAtUnix === 'number' && Number.isFinite(o.capturedAtUnix)
      ? Math.floor(o.capturedAtUnix)
      : 0;
  const achRaw = o.achievements;
  if (!achRaw || typeof achRaw !== 'object' || Array.isArray(achRaw)) {
    return null;
  }
  const achievements: Record<string, SnapshotAchievementState> = {};
  for (const [k, v] of Object.entries(achRaw)) {
    if (!k.trim()) {
      continue;
    }
    const row = normalizeSnapshotAchievementState(v);
    if (row) {
      achievements[k] = row;
    }
  }
  return { appId, capturedAtUnix, achievements };
}

async function writeTextFileAtomic(filePath: string, content: string): Promise<void> {
  await fs.mkdir(path.dirname(filePath), { recursive: true });
  const tmp = `${filePath}.tmp`;
  await fs.writeFile(tmp, content, 'utf8');
  await fs.rename(tmp, filePath);
}

function snapshotFilePath(snapshotRoot: string, appId: string): string | null {
  const id = appId.trim();
  const root = snapshotRoot.trim();
  if (!id || !root) {
    return null;
  }
  return path.join(root, id, 'snapshot.json');
}

export async function loadSnapshot(snapshotRoot: string, appId: string): Promise<GameAchievementSnapshot | null> {
  const filePath = snapshotFilePath(snapshotRoot, appId);
  if (!filePath || !existsSync(filePath)) {
    return null;
  }
  try {
    const rawText = await fs.readFile(filePath, 'utf8');
    if (!rawText.trim()) {
      return null;
    }
    const parsed: unknown = JSON.parse(rawText);
    return normalizeGameAchievementSnapshot(parsed);
  } catch {
    return null;
  }
}

export async function saveSnapshot(snapshotRoot: string, snapshot: GameAchievementSnapshot): Promise<void> {
  const filePath = snapshotFilePath(snapshotRoot, snapshot.appId);
  if (!filePath) {
    return;
  }
  await writeTextFileAtomic(filePath, `${JSON.stringify(snapshot, null, 2)}\n`);
}

export async function diffAndUpdateSnapshot(
  snapshotRoot: string,
  current: GameAchievements
): Promise<AchievementDiff[]> {
  const previous = await loadSnapshot(snapshotRoot, current.appId);
  const diffs = diffFromSnapshot(previous, current);
  await saveSnapshot(snapshotRoot, snapshotFromGameAchievements(current));
  return diffs;
}
