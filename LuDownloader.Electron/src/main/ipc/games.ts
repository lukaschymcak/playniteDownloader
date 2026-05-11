import fs from 'node:fs/promises';
import { existsSync } from 'node:fs';
import path from 'node:path';
import type { InstalledGame, LibraryRow, ReconcileResult, SavedLibraryGame, UpdateStatus } from '../../shared/types';
import { dataDir } from './paths';
import { readJson, writeJson } from './jsonStore';
import { getSteamIntegrationState, getSteamLibraries, parseAcfValue, resolveSteamLibraryRoot } from './steam';
import { getDirectorySize, safeError, warn } from './logger';

const installedFile = (): string => path.join(dataDir(), 'installed_games.json');
const libraryFile = (): string => path.join(dataDir(), 'library_games.json');

export async function listInstalled(): Promise<InstalledGame[]> {
  const games = await readJson<Record<string, unknown>[]>(installedFile(), []);
  if (!Array.isArray(games)) {
    return [];
  }

  const normalized = games.map(normalizeInstalledRecord);
  const appOwned = normalized.filter(isAppOwnedInstalled);
  if (appOwned.length !== normalized.length) {
    await writeInstalledList(appOwned);
  }
  return appOwned;
}

export async function saveInstalled(game: InstalledGame): Promise<InstalledGame> {
  const games = await listInstalled();
  const next = games.filter((g) => g.appId !== game.appId);
  next.push(normalizeInstalled(game));
  await writeInstalledList(next);
  return game;
}

export async function setGoldbergState(appId: string, state: 'required' | 'applied' | 'not_needed'): Promise<InstalledGame> {
  const games = await listInstalled();
  const existing = games.find((g) => g.appId === appId);
  if (!existing) {
    throw new Error(`Installed game not found for appId ${appId}.`);
  }
  existing.goldbergState = state;
  await writeInstalledList(games);
  return existing;
}

export async function removeInstalled(appId: string, deleteFiles = false): Promise<void> {
  const games = await listInstalled();
  const game = games.find((g) => g.appId === appId);
  if (deleteFiles && game?.installPath && existsSync(game.installPath)) {
    const target = assertSafeInstallDeletePath(game);
    await fs.rm(target, { recursive: true, force: true });
  }
  await writeInstalledList(games.filter((g) => g.appId !== appId));
}

export async function scanInstalled(): Promise<InstalledGame[]> {
  const games = await listInstalled();
  const next = games.filter((g) => !g.installPath || existsSync(g.installPath));
  if (next.length !== games.length) {
    await writeInstalledList(next);
  }
  return next;
}

export async function listSavedLibrary(): Promise<SavedLibraryGame[]> {
  const games = await readJson<Record<string, unknown>[]>(libraryFile(), []);
  return Array.isArray(games) ? games.map(normalizeSavedRecord) : [];
}

export async function addSavedLibrary(game: Pick<SavedLibraryGame, 'appId' | 'gameName' | 'headerImageUrl'>): Promise<SavedLibraryGame> {
  const entries = await listSavedLibrary();
  const existing = entries.find((entry) => entry.appId === game.appId);
  if (existing) {
    existing.gameName = game.gameName || existing.gameName || game.appId;
    existing.headerImageUrl = game.headerImageUrl || existing.headerImageUrl;
    await writeSavedList(entries);
    return existing;
  }

  const saved: SavedLibraryGame = {
    appId: game.appId,
    gameName: game.gameName || game.appId,
    headerImageUrl: game.headerImageUrl,
    addedDate: new Date().toISOString()
  };
  entries.push(saved);
  await writeSavedList(entries);
  return saved;
}

export async function removeSavedLibrary(appId: string): Promise<void> {
  const entries = await listSavedLibrary();
  await writeSavedList(entries.filter((entry) => entry.appId !== appId));
}

export async function buildLibraryRows(statuses: Record<string, UpdateStatus> = {}): Promise<LibraryRow[]> {
  const installed = await scanInstalled();
  const saved = await listSavedLibrary();
  const installedByApp = new Map(installed.filter((g) => g.appId).map((g) => [g.appId, g]));
  const rows: LibraryRow[] = await Promise.all(installed.map(async (game) => {
    const steamState = await getSteamIntegrationState(game);
    return {
      appId: game.appId,
      name: game.gameName || game.appId,
      state: 'installed',
      update: statuses[game.appId]?.status || 'cannot_determine',
      luaAdded: steamState.luaAdded,
      manifestAdded: steamState.manifestAdded,
      steamIntegrationMessage: steamState.message,
      size: game.sizeOnDisk,
      installedDate: game.installedDate,
      path: game.installPath,
      headerImageUrl: game.headerImageUrl,
      executablePath: game.executablePath,
      goldbergState: game.goldbergState
    };
  }));

  for (const entry of saved) {
    if (!entry.appId || installedByApp.has(entry.appId)) {
      continue;
    }
    rows.push({
      appId: entry.appId,
      name: entry.gameName || entry.appId,
      state: 'saved',
      update: 'cannot_determine',
      headerImageUrl: entry.headerImageUrl
    });
  }

  return rows.sort((a, b) => a.name.localeCompare(b.name));
}

export async function reconcileInstalled(): Promise<ReconcileResult> {
  const [installed, saved, discovered] = await Promise.all([
    listInstalled(),
    listSavedLibrary(),
    discoverSteamInstalls()
  ]);
  const result: ReconcileResult = { added: 0, updated: 0, removed: 0 };
  const candidates = new Set<string>();
  installed.forEach((g) => g.appId && candidates.add(g.appId));
  saved.forEach((g) => g.appId && candidates.add(g.appId));

  const byApp = new Map(installed.map((g) => [g.appId, g]));
  for (const appId of candidates) {
    const found = discovered.get(appId);
    if (!found) {
      continue;
    }
    const existing = byApp.get(appId);
    if (!existing) {
      continue;
    }
    let changed = false;
    for (const key of ['installPath', 'libraryPath', 'installDir', 'gameName'] as const) {
      if ((!existing[key] || existing[key] !== found[key]) && found[key]) {
        existing[key] = found[key];
        changed = true;
      }
    }
    if (changed) {
      result.updated++;
    }
  }

  const kept = installed.filter((g) => !g.installPath || existsSync(g.installPath));
  result.removed = installed.length - kept.length;
  if (result.added || result.updated || result.removed) {
    await writeInstalledList(kept);
  }
  return result;
}

export async function linkFolder(appId: string, folderPath: string, gameName?: string): Promise<InstalledGame> {
  const stat = await fs.stat(folderPath);
  if (!stat.isDirectory()) {
    throw new Error('Selected path is not a directory.');
  }
  const game = normalizeInstalled({
    appId,
    gameName: gameName || appId,
    installPath: folderPath,
    libraryPath: resolveSteamLibraryRoot(folderPath) || '',
    installDir: path.basename(folderPath),
    installedDate: new Date().toISOString(),
    sizeOnDisk: await getDirectorySize(folderPath),
    selectedDepots: [],
    manifestGIDs: {},
    drmStripped: false,
    registeredWithSteam: false,
    gseSavesCopied: false
    ,
    goldbergState: 'required'
  });
  return saveInstalled(game);
}

async function discoverSteamInstalls(): Promise<Map<string, InstalledGame>> {
  const result = new Map<string, InstalledGame>();
  for (const lib of await getSteamLibraries()) {
    try {
      const steamapps = path.join(lib, 'steamapps');
      const files = await fs.readdir(steamapps);
      for (const file of files.filter((f) => /^appmanifest_\d+\.acf$/i.test(f))) {
        const appId = file.match(/^appmanifest_(\d+)\.acf$/i)?.[1];
        if (!appId) continue;
        const content = await fs.readFile(path.join(steamapps, file), 'utf8');
        const installDir = parseAcfValue(content, 'installdir') || `App_${appId}`;
        const installPath = path.join(steamapps, 'common', installDir);
        if (!existsSync(installPath)) continue;
        result.set(appId, normalizeInstalled({
          appId,
          gameName: parseAcfValue(content, 'name') || `App_${appId}`,
          installDir,
          installPath,
          libraryPath: lib,
          installedDate: new Date().toISOString(),
          sizeOnDisk: await getDirectorySize(installPath),
          selectedDepots: [],
          manifestGIDs: {},
          steamBuildId: parseAcfValue(content, 'buildid') || '0',
          drmStripped: false,
          registeredWithSteam: true,
          gseSavesCopied: false,
          goldbergState: 'required',
          headerImageUrl: `https://cdn.akamai.steamstatic.com/steam/apps/${appId}/library_600x900.jpg`
        }));
      }
    } catch (err) {
      await warn(`Steam library scan failed for ${lib}: ${safeError(err)}`);
    }
  }
  return result;
}

function assertSafeInstallDeletePath(game: InstalledGame): string {
  const target = path.resolve(game.installPath);
  const libraryRoot = game.libraryPath || resolveSteamLibraryRoot(target);
  if (!libraryRoot) {
    throw new Error('Refusing to uninstall because the install path is not inside a known Steam library.');
  }

  const commonRoot = path.resolve(libraryRoot, 'steamapps', 'common');
  const relative = path.relative(commonRoot, target);
  if (!relative || relative.startsWith('..') || path.isAbsolute(relative)) {
    throw new Error('Refusing to uninstall outside the Steam library common folder.');
  }

  return target;
}

async function writeInstalledList(games: InstalledGame[]): Promise<void> {
  await writeJson(installedFile(), games.map((game) => ({
    AppId: game.appId,
    GameName: game.gameName,
    InstallPath: game.installPath,
    LibraryPath: game.libraryPath,
    InstallDir: game.installDir,
    InstalledDate: game.installedDate,
    SizeOnDisk: game.sizeOnDisk,
    SelectedDepots: game.selectedDepots,
    ManifestGIDs: game.manifestGIDs,
    DrmStripped: game.drmStripped,
    RegisteredWithSteam: game.registeredWithSteam,
    GseSavesCopied: game.gseSavesCopied,
    GoldbergState: game.goldbergState,
    HeaderImageUrl: game.headerImageUrl,
    SteamBuildId: game.steamBuildId,
    ManifestZipPath: game.manifestZipPath,
    ExecutablePath: game.executablePath
  })));
}

async function writeSavedList(games: SavedLibraryGame[]): Promise<void> {
  await writeJson(libraryFile(), games.map((game) => ({
    AppId: game.appId,
    GameName: game.gameName,
    AddedDate: game.addedDate,
    HeaderImageUrl: game.headerImageUrl
  })));
}

function normalizeInstalledRecord(record: Record<string, unknown>): InstalledGame {
  return normalizeInstalled({
    appId: str(record.appId ?? record.AppId),
    gameName: str(record.gameName ?? record.GameName),
    installPath: str(record.installPath ?? record.InstallPath),
    libraryPath: str(record.libraryPath ?? record.LibraryPath),
    installDir: str(record.installDir ?? record.InstallDir),
    installedDate: str(record.installedDate ?? record.InstalledDate),
    sizeOnDisk: Number(record.sizeOnDisk ?? record.SizeOnDisk ?? 0),
    selectedDepots: arr(record.selectedDepots ?? record.SelectedDepots),
    manifestGIDs: obj(record.manifestGIDs ?? record.ManifestGIDs),
    drmStripped: Boolean(record.drmStripped ?? record.DrmStripped),
    registeredWithSteam: Boolean(record.registeredWithSteam ?? record.RegisteredWithSteam),
    gseSavesCopied: Boolean(record.gseSavesCopied ?? record.GseSavesCopied),
    goldbergState: parseGoldbergState(record.goldbergState ?? record.GoldbergState, Boolean(record.gseSavesCopied ?? record.GseSavesCopied)),
    headerImageUrl: optStr(record.headerImageUrl ?? record.HeaderImageUrl),
    steamBuildId: optStr(record.steamBuildId ?? record.SteamBuildId),
    manifestZipPath: optStr(record.manifestZipPath ?? record.ManifestZipPath),
    executablePath: optStr(record.executablePath ?? record.ExecutablePath)
  });
}

function normalizeSavedRecord(record: Record<string, unknown>): SavedLibraryGame {
  return {
    appId: str(record.appId ?? record.AppId),
    gameName: str(record.gameName ?? record.GameName),
    addedDate: str(record.addedDate ?? record.AddedDate) || new Date().toISOString(),
    headerImageUrl: optStr(record.headerImageUrl ?? record.HeaderImageUrl)
  };
}

function normalizeInstalled(game: InstalledGame): InstalledGame {
  return {
    appId: game.appId || '',
    gameName: game.gameName || game.appId || '',
    installPath: game.installPath || '',
    libraryPath: game.libraryPath || '',
    installDir: game.installDir || '',
    installedDate: game.installedDate || new Date().toISOString(),
    sizeOnDisk: Number(game.sizeOnDisk || 0),
    selectedDepots: game.selectedDepots || [],
    manifestGIDs: game.manifestGIDs || {},
    drmStripped: Boolean(game.drmStripped),
    registeredWithSteam: Boolean(game.registeredWithSteam),
    gseSavesCopied: Boolean(game.gseSavesCopied),
    goldbergState: game.goldbergState || (game.gseSavesCopied ? 'applied' : 'required'),
    headerImageUrl: game.headerImageUrl,
    steamBuildId: game.steamBuildId,
    manifestZipPath: game.manifestZipPath,
    executablePath: game.executablePath
  };
}

function isAppOwnedInstalled(game: InstalledGame): boolean {
  if (!game.registeredWithSteam) {
    return true;
  }

  if (game.manifestZipPath) {
    return true;
  }

  if (game.selectedDepots?.length) {
    return true;
  }

  if (game.manifestGIDs && Object.keys(game.manifestGIDs).length > 0) {
    return true;
  }

  return false;
}

function str(value: unknown): string {
  return value == null ? '' : String(value);
}

function optStr(value: unknown): string | undefined {
  return value == null || value === '' ? undefined : String(value);
}

function arr(value: unknown): string[] {
  return Array.isArray(value) ? value.map(String) : [];
}

function obj(value: unknown): Record<string, string> {
  if (!value || typeof value !== 'object') return {};
  return Object.fromEntries(Object.entries(value as Record<string, unknown>).map(([k, v]) => [k, String(v)]));
}

function parseGoldbergState(value: unknown, gseSavesCopied: boolean): 'required' | 'applied' | 'not_needed' {
  if (value === 'required' || value === 'applied' || value === 'not_needed') {
    return value;
  }
  return gseSavesCopied ? 'applied' : 'required';
}
