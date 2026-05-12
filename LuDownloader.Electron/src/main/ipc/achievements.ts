import fs from 'node:fs/promises';
import path from 'node:path';
import { dialog } from 'electron';
import type { BrowserWindow } from 'electron';
import type { GameAchievements } from '../../shared/types';
import { achievementCacheDir, achievementSnapshotDir } from './paths';

export async function listAllGameAchievements(): Promise<GameAchievements[]> {
  const cacheDir = achievementCacheDir();
  let entries: import('node:fs').Dirent[] = [];
  try {
    entries = await fs.readdir(cacheDir, { withFileTypes: true });
  } catch {
    return [];
  }
  const results: GameAchievements[] = [];
  for (const entry of entries) {
    if (!entry.isDirectory()) continue;
    const filePath = path.join(cacheDir, entry.name, 'game_achievements.json');
    try {
      const raw = await fs.readFile(filePath, 'utf8');
      results.push(JSON.parse(raw) as GameAchievements);
    } catch {
      /* skip missing/corrupt */
    }
  }
  return results;
}

export async function clearAchievementCache(): Promise<void> {
  const { CacheService } = await import('../achievement/cacheService');
  const cache = new CacheService(achievementCacheDir());
  await cache.clearAllCache();
}

export async function clearAchievementSnapshots(): Promise<void> {
  const dir = achievementSnapshotDir();
  await fs.rm(dir, { recursive: true, force: true });
  await fs.mkdir(dir, { recursive: true });
}

export async function pickAvatarFile(win: BrowserWindow): Promise<string | null> {
  const result = await dialog.showOpenDialog(win, {
    title: 'Select Avatar Image',
    filters: [{ name: 'Images', extensions: ['jpg', 'jpeg', 'png', 'webp', 'bmp', 'gif'] }],
    properties: ['openFile']
  });
  return result.filePaths[0] ?? null;
}
