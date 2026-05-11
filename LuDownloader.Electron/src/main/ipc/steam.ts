import fs from 'node:fs/promises';
import { existsSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { shell } from 'electron';
import AdmZip from 'adm-zip';
import WinReg from 'winreg';
import type { GameData, InstalledGame } from '../../shared/types';
import { manifestCacheDir } from './paths';
import { getDirectorySize, info, safeError } from './logger';
import { extractSingleLua, getSingleLuaFileName, processZip } from './zip';
import { listInstalled, saveInstalled } from './games';
import { ensureManifestGids, fetchManifestGids } from './manifest';

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
  const previousBuild = await readExistingBuildInfo(acfPath);
  const buildId = normalizeBuildId(gameData.buildId, previousBuild.buildId, previousBuild.targetBuildId);
  await fs.writeFile(acfPath, buildAcf(gameData, installDir, sizeOnDisk, buildId), 'utf8');
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

  // Refresh manifest/build metadata first so ACF is written in a stable installed state.
  await fetchManifestGids([appId]).catch(() => undefined);
  const refreshedInstalled = await listInstalled();
  const refreshed = refreshedInstalled.find((entry) => entry.appId === appId) || game;

  const zipPath = await resolveManifestZip(refreshed);
  if (!zipPath) {
    throw new Error('Manifest ZIP was not found for this game.');
  }

  const zipGameData = await processZip(zipPath);
  await copyLuaToSteamConfig(zipPath);

  const manifestGIDs = await ensureManifestGids(refreshed, zipGameData);
  const selectedDepots = (refreshed.selectedDepots?.length ? refreshed.selectedDepots : Object.keys(manifestGIDs))
    .filter((depotId) => Boolean(manifestGIDs[depotId]));
  if (!selectedDepots.length) {
    throw new Error('No depot manifests are available for this game.');
  }
  const missingSelected = selectedDepots.filter((depotId) => !manifestGIDs[depotId]);
  if (missingSelected.length > 0) {
    throw new Error(`Selected depots are missing manifest GIDs: ${missingSelected.join(', ')}`);
  }

  const libraryPath = refreshed.libraryPath || resolveSteamLibraryRoot(refreshed.installPath);
  if (!libraryPath) {
    throw new Error('Steam library path could not be resolved for this install.');
  }

  const buildResolution = await resolveStableBuildId(refreshed, zipGameData);
  const buildId = buildResolution.buildId;

  const gameData: GameData = {
    appId: refreshed.appId,
    gameName: refreshed.gameName || zipGameData.gameName || refreshed.appId,
    installDir: refreshed.installDir,
    buildId,
    depots: zipGameData.depots || {},
    dlcs: zipGameData.dlcs || {},
    manifests: manifestGIDs,
    selectedDepots,
    manifestZipPath: zipPath,
    headerImageUrl: refreshed.headerImageUrl
  };
  await writeAcf(gameData, libraryPath);
  const depotcache = await syncDepotcache(libraryPath, zipPath, gameData.selectedDepots, gameData.manifests);

  await info(
    [
      `Steam registration completed for app ${refreshed.appId}.`,
      `selectedDepots=[${selectedDepots.join(', ')}]`,
      `manifests=[${selectedDepots.map((id) => `${id}:${manifestGIDs[id]}`).join(', ')}]`,
      `buildId=${buildId}`,
      `buildSource=${buildResolution.source}`,
      `depotcache expected=${depotcache.expected} synced=${depotcache.synced} missing=${depotcache.missing.length}`
    ].join(' ')
  );

  const saved = await saveInstalled({
    ...refreshed,
    libraryPath,
    manifestZipPath: zipPath,
    manifestGIDs,
    selectedDepots,
    steamBuildId: buildId,
    registeredWithSteam: Boolean(await findAcfPath({ ...refreshed, libraryPath }))
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

function buildAcf(data: GameData, installDir: string, sizeOnDisk: number, buildId: string): string {
  const nowUnix = Math.floor(Date.now() / 1000).toString();
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

function normalizeBuildId(...values: Array<string | undefined>): string {
  for (const value of values) {
    const v = String(value || '').trim();
    if (isUsableBuildId(v)) {
      return v;
    }
  }
  const diag = values.map((v, idx) => `s${idx + 1}=${String(v || '').trim() || '<empty>'}`).join(', ');
  throw new Error(`Cannot write ACF without a non-zero buildid. Sources: ${diag}`);
}

async function resolveStableBuildId(
  game: InstalledGame,
  zipGameData: GameData
): Promise<{ buildId: string; source: 'acf-buildid' | 'acf-targetbuildid' | 'installed-steamBuildId' | 'zip-buildId' }> {
  const acfPath = await findAcfPath(game);
  if (acfPath && existsSync(acfPath)) {
    const content = await fs.readFile(acfPath, 'utf8');
    const fromAcf = parseAcfValue(content, 'buildid').trim();
    if (isUsableBuildId(fromAcf)) {
      return { buildId: fromAcf, source: 'acf-buildid' };
    }
    const targetFromAcf = parseAcfValue(content, 'TargetBuildID').trim();
    if (isUsableBuildId(targetFromAcf)) {
      return { buildId: targetFromAcf, source: 'acf-targetbuildid' };
    }
  }
  if (isUsableBuildId(String(game.steamBuildId || '').trim())) {
    return { buildId: String(game.steamBuildId).trim(), source: 'installed-steamBuildId' };
  }
  if (isUsableBuildId(String(zipGameData.buildId || '').trim())) {
    return { buildId: String(zipGameData.buildId).trim(), source: 'zip-buildId' };
  }
  const diag = [
    `steamBuildId=${String(game.steamBuildId || '').trim() || '<empty>'}`,
    `zipBuildId=${String(zipGameData.buildId || '').trim() || '<empty>'}`,
    `acfPath=${acfPath || '<none>'}`
  ].join(', ');
  throw new Error(`Could not resolve a non-zero buildid for ACF. Sources: ${diag}`);
}

async function readExistingBuildInfo(acfPath: string): Promise<{ buildId?: string; targetBuildId?: string }> {
  if (!existsSync(acfPath)) return {};
  try {
    const content = await fs.readFile(acfPath, 'utf8');
    const buildId = parseAcfValue(content, 'buildid').trim() || undefined;
    const targetBuildId = parseAcfValue(content, 'TargetBuildID').trim() || undefined;
    return { buildId, targetBuildId };
  } catch {
    return {};
  }
}

function isUsableBuildId(value: string): boolean {
  if (!/^\d+$/.test(value)) return false;
  const n = Number.parseInt(value, 10);
  // Treat 0 and 1 as invalid placeholders for our writer flow.
  return Number.isFinite(n) && n > 1;
}

async function syncDepotcache(
  libraryPath: string,
  zipPath: string,
  selectedDepots: string[],
  manifests: Record<string, string>
): Promise<{ expected: number; synced: number; missing: string[] }> {
  const depotcacheDir = path.join(libraryPath, 'steamapps', 'depotcache');
  await fs.mkdir(depotcacheDir, { recursive: true });

  const tempManifestRoot = path.join(os.tmpdir(), 'ludownloader_manifests');
  const zip = new AdmZip(zipPath);
  const entries = zip.getEntries();

  let synced = 0;
  const missing: string[] = [];

  for (const depotId of selectedDepots) {
    const manifestGid = manifests[depotId];
    if (!manifestGid) {
      missing.push(`${depotId}:<missing-gid>`);
      continue;
    }
    const fileName = `${depotId}_${manifestGid}.manifest`;
    const target = path.join(depotcacheDir, fileName);
    const tempSource = path.join(tempManifestRoot, fileName);

    if (existsSync(tempSource)) {
      await fs.copyFile(tempSource, target);
      synced += 1;
      continue;
    }

    const zipEntry = entries.find((entry) => !entry.isDirectory && path.basename(entry.entryName) === fileName);
    if (zipEntry) {
      await fs.writeFile(target, zipEntry.getData());
      synced += 1;
      continue;
    }

    missing.push(`${depotId}:${manifestGid}`);
  }

  if (missing.length > 0) {
    throw new Error(
      `Depotcache sync failed. Missing manifest artifact(s): ${missing.join(', ')}. ` +
      'Re-download manifest cache and retry Add to Steam / Rerun Manifest.'
    );
  }

  await cleanupTempManifestFiles(selectedDepots, manifests, tempManifestRoot);
  return { expected: selectedDepots.length, synced, missing };
}

async function cleanupTempManifestFiles(
  selectedDepots: string[],
  manifests: Record<string, string>,
  tempManifestRoot: string
): Promise<void> {
  await Promise.all(selectedDepots.map(async (depotId) => {
    const manifestGid = manifests[depotId];
    if (!manifestGid) return;
    const filePath = path.join(tempManifestRoot, `${depotId}_${manifestGid}.manifest`);
    await fs.unlink(filePath).catch(() => undefined);
  }));
}
