import { existsSync } from 'node:fs';
import path from 'node:path';
import type { AchievementGameContext, AppSettings, InstalledGame } from '../../shared/types.ts';

type AchievementGameInstallSettings = Pick<AppSettings, 'achievementScanOfficialSteamLibraries'>;

function libraryRootFromInstallPath(installPath: string): string {
  const common = path.dirname(installPath);
  const steamapps = path.dirname(common);
  if (path.basename(common).toLowerCase() !== 'common') {
    return '';
  }
  if (path.basename(steamapps).toLowerCase() !== 'steamapps') {
    return '';
  }
  return path.dirname(steamapps);
}

function emptyContext(appId: string): AchievementGameContext {
  return {
    appId,
    installPath: null,
    libraryRoot: null,
    installDir: null,
    gameName: null,
    hasUsableInstallDirectory: false
  };
}

export async function buildAchievementGameContext(
  appId: string,
  installedGames: InstalledGame[],
  settings: AchievementGameInstallSettings
): Promise<AchievementGameContext> {
  try {
    const local = installedGames.find((g) => g.appId === appId);
    if (local && local.installPath.trim().length > 0 && existsSync(local.installPath)) {
      const libraryRoot =
        local.libraryPath.trim().length > 0 ? local.libraryPath : libraryRootFromInstallPath(local.installPath);
      return {
        appId,
        installPath: local.installPath,
        libraryRoot: libraryRoot || null,
        installDir: local.installDir || null,
        gameName: local.gameName || null,
        hasUsableInstallDirectory: true
      };
    }

    if (!settings.achievementScanOfficialSteamLibraries) {
      return emptyContext(appId);
    }

    // Dynamic import keeps node:test from loading Electron via games -> steam when this branch is unused.
    const { resolveSteamInstallByAppId } = await import('../ipc/games.ts');
    const steam = await resolveSteamInstallByAppId(appId);
    if (!steam || !steam.installPath || !existsSync(steam.installPath)) {
      return emptyContext(appId);
    }
    return {
      appId,
      installPath: steam.installPath,
      libraryRoot: steam.libraryPath || null,
      installDir: steam.installDir || null,
      gameName: steam.gameName || null,
      hasUsableInstallDirectory: true
    };
  } catch {
    return emptyContext(appId);
  }
}
