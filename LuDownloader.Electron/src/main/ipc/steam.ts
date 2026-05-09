import fs from 'node:fs/promises';
import { existsSync } from 'node:fs';
import path from 'node:path';
import { shell } from 'electron';
import WinReg from 'winreg';
import type { GameData, InstalledGame } from '../../shared/types';
import { manifestCacheDir } from './paths';
import { getDirectorySize, safeError } from './logger';
import { extractSingleLua, getSingleLuaFileName, processZip } from './zip';
import { listInstalled, saveInstalled } from './games';
import { ensureManifestGids } from './manifest';

export function parseAcfValue(content: string, key: string): string {
  const escaped = key.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  return content.match(new RegExp(`"${escaped}"\\s+"([^"]*)"`, 'i'))?.[1] || '';
}

export async function getSteamPath(): Promise<string> {
  const reg = new WinReg({ hive: WinReg.HKCU, key: '\\Software\\Valve\\Steam' });
  return new Promise((resolve) => {
    reg.get('SteamPath', (err, item) => {
      if (err || !item?.value) resolve('');
      else resolve(item.value.replace(/\//g, '\\'));
    });
  });
}

export async function getSteamLibraries(): Promise<string[]> {
  const steamPath = await getSteamPath();
  const libs = new Set<string>();
  if (steamPath) libs.add(steamPath);

  const vdf = path.join(steamPath, 'steamapps', 'libraryfolders.vdf');
  if (!existsSync(vdf)) {
    return [...libs];
  }

  const content = await fs.readFile(vdf, 'utf8');
  for (const match of content.matchAll(/"(?:path|\d+)"\s+"(.*?)"/g)) {
    const raw = match[1].replace(/\\\\/g, '\\');
    if (raw && existsSync(path.join(raw, 'steamapps'))) {
      libs.add(raw);
    }
  }
  return [...libs];
}

export async function writeAcf(gameData: GameData, steamLibraryPath: string): Promise<string> {
  const installDir = getInstallFolderName(gameData);
  const acfPath = path.join(steamLibraryPath, 'steamapps', `appmanifest_${gameData.appId}.acf`);
  await fs.mkdir(path.dirname(acfPath), { recursive: true });
  const sizeOnDisk = await getDirectorySize(path.join(steamLibraryPath, 'steamapps', 'common', installDir));
  await fs.writeFile(acfPath, buildAcf(gameData, installDir, sizeOnDisk), 'utf8');
  return acfPath;
}

export async function resolveManifestZip(game: InstalledGame): Promise<string> {
  if (game.manifestZipPath && existsSync(game.manifestZipPath)) {
    return game.manifestZipPath;
  }

  const cacheRoot = manifestCacheDir();
  if (!existsSync(cacheRoot)) {
    return '';
  }

  const files = await fs.readdir(cacheRoot);
  const match = files
    .filter((file) => file.toLowerCase().endsWith('.zip'))
    .find((file) => file === `${game.appId}.zip` || file.startsWith(`${game.appId}_`) || file.startsWith(`${game.appId}-`));
  return match ? path.join(cacheRoot, match) : '';
}

export async function getSteamIntegrationState(game: InstalledGame): Promise<{
  luaAdded: boolean;
  manifestAdded: boolean;
  message?: string;
}> {
  try {
    const steamPath = await getSteamPath();
    if (!steamPath) {
      return { luaAdded: false, manifestAdded: false, message: 'Steam path was not found.' };
    }

    const manifestAdded = Boolean(await findAcfPath(game));
    const zipPath = await resolveManifestZip(game);
    if (!zipPath) {
      return { luaAdded: false, manifestAdded, message: 'Manifest ZIP was not found.' };
    }

    const luaFileName = getSingleLuaFileName(zipPath);
    const luaAdded = existsSync(path.join(getLuaTargetDir(steamPath), luaFileName));
    return { luaAdded, manifestAdded };
  } catch (err) {
    return { luaAdded: false, manifestAdded: false, message: safeError(err) };
  }
}

export async function copyLuaToSteamConfig(zipPath: string): Promise<string> {
  const steamPath = await getSteamPath();
  if (!steamPath) {
    throw new Error('Steam path was not found.');
  }

  const lua = await extractSingleLua(zipPath);
  const targetDir = getLuaTargetDir(steamPath);
  const target = path.join(targetDir, lua.fileName);
  await fs.mkdir(targetDir, { recursive: true });
  if (!existsSync(target)) {
    await fs.copyFile(lua.path, target);
  }
  return target;
}

export async function findAcfPath(game: InstalledGame): Promise<string> {
  const libraryCandidates = [
    game.libraryPath,
    resolveSteamLibraryRoot(game.installPath),
    ...(await getSteamLibraries())
  ].filter(Boolean);

  for (const libraryPath of [...new Set(libraryCandidates)]) {
    const acfPath = path.join(libraryPath, 'steamapps', `appmanifest_${game.appId}.acf`);
    if (existsSync(acfPath)) {
      return acfPath;
    }
  }
  return '';
}

export async function openPath(target: string): Promise<void> {
  await shell.openPath(target);
}

export function showItemInFolder(target: string): void {
  shell.showItemInFolder(target);
}

export function resolveSteamLibraryRoot(installPath: string): string {
  if (!installPath) return '';
  const common = path.dirname(installPath);
  const steamapps = path.dirname(common);
  if (path.basename(common).toLowerCase() !== 'common') return '';
  if (path.basename(steamapps).toLowerCase() !== 'steamapps') return '';
  return path.dirname(steamapps);
}

export async function addToSteam(appId: string): Promise<InstalledGame> {
  const installed = await listInstalled();
  const game = installed.find((entry) => entry.appId === appId);
  if (!game) {
    throw new Error('Installed game record was not found.');
  }

  const zipPath = await resolveManifestZip(game);
  if (!zipPath) {
    throw new Error('Manifest ZIP was not found for this game.');
  }

  const zipGameData = await processZip(zipPath);
  await copyLuaToSteamConfig(zipPath);

  const manifestGIDs = await ensureManifestGids(game, zipGameData);
  const selectedDepots = game.selectedDepots?.length ? game.selectedDepots : Object.keys(manifestGIDs);
  if (!selectedDepots.length) {
    throw new Error('No depot manifests are available for this game.');
  }

  const libraryPath = game.libraryPath || resolveSteamLibraryRoot(game.installPath);
  if (!libraryPath) {
    throw new Error('Steam library path could not be resolved for this install.');
  }

  const gameData: GameData = {
    appId: game.appId,
    gameName: game.gameName || zipGameData.gameName || game.appId,
    installDir: game.installDir,
    buildId: game.steamBuildId || zipGameData.buildId,
    depots: zipGameData.depots || {},
    dlcs: zipGameData.dlcs || {},
    manifests: manifestGIDs,
    selectedDepots,
    manifestZipPath: zipPath,
    headerImageUrl: game.headerImageUrl
  };
  await writeAcf(gameData, libraryPath);

  const saved = await saveInstalled({
    ...game,
    libraryPath,
    manifestZipPath: zipPath,
    manifestGIDs,
    selectedDepots,
    steamBuildId: game.steamBuildId || zipGameData.buildId,
    registeredWithSteam: Boolean(await findAcfPath({ ...game, libraryPath }))
  });
  return saved;
}

function getLuaTargetDir(steamPath: string): string {
  return path.join(steamPath, 'config', 'lua');
}

function getInstallFolderName(data: GameData): string {
  if (data.installDir?.trim()) return data.installDir.trim();
  const safe = (data.gameName || '').replace(/[^\w\s-]/g, '').replace(/\s+/g, ' ').trim();
  return safe || `App_${data.appId}`;
}

function buildAcf(data: GameData, installDir: string, sizeOnDisk: number): string {
  const nowUnix = Math.floor(Date.now() / 1000).toString();
  const buildId = data.buildId || '0';
  const lines = [
    '"AppState"',
    '{',
    `\t"appid"\t\t"${data.appId}"`,
    '\t"Universe"\t\t"1"',
    `\t"name"\t\t"${escapeVdf(data.gameName)}"`,
    '\t"StateFlags"\t\t"4"',
    `\t"installdir"\t\t"${escapeVdf(installDir)}"`,
    `\t"LastUpdated"\t\t"${nowUnix}"`,
    `\t"SizeOnDisk"\t\t"${sizeOnDisk}"`,
    '\t"StagingSize"\t\t"0"',
    `\t"buildid"\t\t"${buildId}"`,
    '\t"UpdateResult"\t\t"0"',
    '\t"BytesToDownload"\t\t"0"',
    '\t"BytesDownloaded"\t\t"0"',
    '\t"BytesToStage"\t\t"0"',
    '\t"BytesStaged"\t\t"0"',
    `\t"TargetBuildID"\t\t"${buildId}"`,
    '\t"AutoUpdateBehavior"\t\t"0"',
    '\t"AllowOtherDownloadsWhileRunning"\t\t"0"',
    '\t"ScheduledAutoUpdate"\t\t"0"',
    '\t"InstalledDepots"',
    '\t{'
  ];

  for (const depotId of data.selectedDepots || []) {
    const manifest = data.manifests[depotId];
    if (!manifest) continue;
    lines.push(`\t\t"${depotId}"`);
    lines.push('\t\t{');
    lines.push(`\t\t\t"manifest"\t\t"${manifest}"`);
    lines.push(`\t\t\t"size"\t\t"${data.depots[depotId]?.size || 0}"`);
    lines.push('\t\t}');
  }
  lines.push('\t}', '}');
  return `${lines.join('\n')}\n`;
}

function escapeVdf(value: string): string {
  return value.replace(/\\/g, '\\\\').replace(/"/g, '\\"');
}
