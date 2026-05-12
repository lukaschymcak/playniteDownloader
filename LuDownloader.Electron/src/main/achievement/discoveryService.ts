import fs from 'node:fs/promises';
import path from 'node:path';
import {
  EmulatorSource,
  SourceId,
  type AppSettings,
  type DiscoveryRecord,
  type FileChangeEvent,
  type ResolvedChange,
  type ScanResult,
  type EmulatorSourceMask
} from '../../shared/types.ts';

export interface RegistryAdapter {
  listSubKeys(baseKey: string): Promise<string[]>;
  getValue(keyPath: string, valueName: string): Promise<string | number | null>;
  listValueNames(keyPath: string): Promise<string[]>;
}

interface ScanPattern {
  source: SourceId;
  fileName: string;
  rootPath: string;
  includeSubdirectories?: boolean;
}

const FILE_SOURCES: readonly SourceId[] = [
  SourceId.Goldberg,
  SourceId.GSE,
  SourceId.Empress,
  SourceId.Codex,
  SourceId.Rune,
  SourceId.OnlineFix,
  SourceId.SmartSteamEmu,
  SourceId.Skidrow,
  SourceId.Darksiders,
  SourceId.Ali213,
  SourceId.Hoodlum,
  SourceId.CreamApi,
  SourceId.Reloaded
];

/** Emulator sources backed by disk files; GreenLuma stays registry-only so it is not fs-watched here. */
export function achievementFileDiscoverySources(): readonly SourceId[] {
  return FILE_SOURCES;
}

const GREENLUMA_BASE_KEYS = ['SOFTWARE\\GLR\\AppID', 'SOFTWARE\\GL2020\\AppID'] as const;

const FILE_SOURCE_SET = new Set<string>(FILE_SOURCES as unknown as string[]);

function isFileDiscoverySource(source: SourceId): boolean {
  return FILE_SOURCE_SET.has(source);
}

const DEFAULT_ROOTS: Readonly<Record<SourceId, string[]>> = {
  [SourceId.None]: [],
  [SourceId.Goldberg]: [expandEnv('%APPDATA%\\Goldberg SteamEmu Saves')],
  [SourceId.GSE]: [expandEnv('%APPDATA%\\GSE Saves')],
  [SourceId.Empress]: [expandEnv('%APPDATA%\\EMPRESS'), expandEnv('%PUBLIC%\\Documents\\EMPRESS')],
  [SourceId.Codex]: [expandEnv('%PUBLIC%\\Documents\\Steam\\CODEX')],
  [SourceId.Rune]: [expandEnv('%PUBLIC%\\Documents\\Steam\\RUNE')],
  [SourceId.OnlineFix]: [expandEnv('%PUBLIC%\\Documents\\OnlineFix')],
  [SourceId.SmartSteamEmu]: [expandEnv('%APPDATA%\\SmartSteamEmu')],
  [SourceId.Skidrow]: [expandEnv('%LOCALAPPDATA%\\SKIDROW')],
  [SourceId.Darksiders]: [path.join(expandEnv('%USERPROFILE%'), 'Documents', 'DARKSiDERS')],
  [SourceId.Ali213]: [path.join(expandEnv('%USERPROFILE%'), 'Documents', 'VALVE')],
  [SourceId.Hoodlum]: [],
  [SourceId.CreamApi]: [expandEnv('%APPDATA%\\CreamAPI')],
  [SourceId.GreenLuma]: [],
  [SourceId.Reloaded]: [expandEnv('%PROGRAMDATA%\\Steam')]
};

export async function collectExtraDiscoveryRoots(settings: AppSettings): Promise<string[]> {
  const userRoots = dedupeNormalizedPaths(settings.achievementUserGameLibraryRoots ?? []);
  if (!settings.achievementScanOfficialSteamLibraries) {
    return userRoots;
  }
  try {
    const { getSteamLibraries } = await import('../ipc/steam.ts');
    const fromSteam = await getSteamLibraries();
    return dedupeNormalizedPaths([...userRoots, ...fromSteam]);
  } catch {
    return userRoots;
  }
}

export async function scanAllSources(
  settings: AppSettings,
  registryAdapter?: RegistryAdapter
): Promise<DiscoveryRecord[]> {
  const records: DiscoveryRecord[] = [];
  const enabled = getEnabledSources(settings);
  const extraScanRoots = await collectExtraDiscoveryRoots(settings);

  for (const pattern of buildScanPatterns(settings, enabled, extraScanRoots)) {
    const found = await scanPattern(pattern);
    records.push(...found);
  }

  if (enabled.has(SourceId.GreenLuma) && registryAdapter) {
    const greenLuma = await scanGreenLumaRegistry(registryAdapter);
    records.push(...greenLuma);
  }

  return dedupeDiscoveryRecords(records);
}

export async function resolveChangedPath(
  changeEvent: FileChangeEvent,
  settings: AppSettings
): Promise<ResolvedChange | null> {
  const enabled = getEnabledSources(settings);
  if (!enabled.has(changeEvent.source) || changeEvent.source === SourceId.GreenLuma) {
    return null;
  }

  const root = normalizeWindowsPath(changeEvent.rootPath);
  const full = normalizeWindowsPath(changeEvent.fullPath);

  if (!root || !full) {
    await logUnresolved(changeEvent.source, changeEvent.fullPath, changeEvent.rootPath, 'invalid_path');
    return null;
  }

  if (!isStrictlyInside(root, full)) {
    await logUnresolved(changeEvent.source, full, root, 'outside_root');
    return null;
  }

  const appId = resolveAppIdFromPath(full, root);
  if (!appId) {
    await logUnresolved(changeEvent.source, full, root, 'missing_numeric_appid');
    return null;
  }

  return {
    appId,
    source: changeEvent.source,
    location: full,
    eventType: changeEvent.eventType,
    timestamp: changeEvent.timestamp ?? Date.now()
  };
}

export function buildSourceRoots(settings: AppSettings, extraScanRoots: string[] = []): Record<SourceId, string[]> {
  const custom = settings.achievementSourceRoots ?? {};
  const result = {} as Record<SourceId, string[]>;
  const extras = dedupeNormalizedPaths(extraScanRoots);

  for (const source of Object.values(SourceId)) {
    const base = source === SourceId.Hoodlum
      ? []
      : [...(DEFAULT_ROOTS[source] ?? [])];
    const additional = Array.isArray(custom[source]) ? custom[source] : [];
    const combined = [...base, ...additional];
    if (source === SourceId.Hoodlum && settings.hoodlumSavePath.trim().length > 0) {
      combined.push(settings.hoodlumSavePath);
    }
    // Same extras as other FILE_SOURCES so hlm.ini under user/Steam library trees is discoverable.
    if (extras.length > 0 && isFileDiscoverySource(source)) {
      combined.push(...extras);
    }
    result[source] = dedupeNormalizedPaths(combined);
  }

  return result;
}

export function toScanResults(records: DiscoveryRecord[]): ScanResult[] {
  return records.map((r) => ({
    appId: r.appId,
    source: sourceIdToEmulatorMask(r.source),
    filePath: r.location
  }));
}

export function sourceIdToEmulatorMask(id: SourceId): EmulatorSourceMask {
  switch (id) {
    case SourceId.None:
      return EmulatorSource.None;
    case SourceId.Goldberg:
      return EmulatorSource.Goldberg;
    case SourceId.GSE:
      return EmulatorSource.GSE;
    case SourceId.Codex:
      return EmulatorSource.Codex;
    case SourceId.Rune:
      return EmulatorSource.Rune;
    case SourceId.Empress:
      return EmulatorSource.Empress;
    case SourceId.OnlineFix:
      return EmulatorSource.OnlineFix;
    case SourceId.SmartSteamEmu:
      return EmulatorSource.SmartSteamEmu;
    case SourceId.Skidrow:
      return EmulatorSource.Skidrow;
    case SourceId.Darksiders:
      return EmulatorSource.Darksiders;
    case SourceId.Ali213:
      return EmulatorSource.Ali213;
    case SourceId.Hoodlum:
      return EmulatorSource.Hoodlum;
    case SourceId.CreamApi:
      return EmulatorSource.CreamApi;
    case SourceId.GreenLuma:
      return EmulatorSource.GreenLuma;
    case SourceId.Reloaded:
      return EmulatorSource.Reloaded;
    default:
      return EmulatorSource.None;
  }
}

export function resolveAppIdFromPath(fullPath: string, watchRoot: string): string | null {
  const root = normalizeWindowsPath(watchRoot);
  const target = normalizeWindowsPath(fullPath);
  if (!root || !target || !isStrictlyInside(root, target)) return null;

  let cursor = normalizeWindowsPath(path.dirname(target));
  if (!cursor || !isSameOrUnderRoot(root, cursor)) return null;

  while (cursor) {
    const leaf = path.basename(cursor);
    if (isNumericAppId(leaf)) return leaf;
    if (pathsEqual(cursor, root)) return null;
    const parent = normalizeWindowsPath(path.dirname(cursor));
    if (!parent || pathsEqual(parent, cursor) || !isSameOrUnderRoot(root, parent)) return null;
    cursor = parent;
  }

  return null;
}

export function isNumericAppId(value: string): boolean {
  return /^\d+$/.test(value);
}

export function dedupeNormalizedPaths(paths: string[]): string[] {
  const seen = new Set<string>();
  const result: string[] = [];
  for (const rawPath of paths) {
    const normalized = normalizeWindowsPath(rawPath);
    if (!normalized) continue;
    const key = normalized.toLowerCase();
    if (seen.has(key)) continue;
    seen.add(key);
    result.push(normalized);
  }
  return result;
}

export function normalizeWindowsPath(input: string): string | null {
  if (!input || input.trim().length === 0) return null;
  try {
    const absolute = path.win32.resolve(input.trim());
    return absolute.replace(/[\\/]+$/, '');
  } catch {
    return null;
  }
}

export function isStrictlyInside(root: string, child: string): boolean {
  if (child.length <= root.length) return false;
  if (!child.toLowerCase().startsWith(root.toLowerCase())) return false;
  const next = child[root.length];
  return next === '\\' || next === '/';
}

function isSameOrUnderRoot(root: string, candidate: string): boolean {
  return pathsEqual(root, candidate) || isStrictlyInside(root, candidate);
}

function pathsEqual(a: string, b: string): boolean {
  return a.toLowerCase() === b.toLowerCase();
}

export function getEnabledSources(settings: AppSettings): Set<SourceId> {
  const list = settings.achievementEnabledSources ?? Object.values(SourceId).filter((s) => s !== SourceId.None);
  const enabled = new Set<SourceId>();
  for (const item of list) {
    if (item === SourceId.None) {
      continue;
    }
    if ((Object.values(SourceId) as string[]).includes(item)) {
      enabled.add(item);
    }
  }
  return enabled.size > 0
    ? enabled
    : new Set(Object.values(SourceId).filter((s) => s !== SourceId.None));
}

function buildScanPatterns(
  settings: AppSettings,
  enabled: Set<SourceId>,
  extraScanRoots: string[]
): ScanPattern[] {
  const roots = buildSourceRoots(settings, extraScanRoots);
  const patterns: ScanPattern[] = [];

  const add = (source: SourceId, fileName: string, includeSubdirectories = false): void => {
    if (!enabled.has(source)) return;
    for (const rootPath of roots[source] ?? []) {
      patterns.push({ source, fileName, rootPath, includeSubdirectories });
    }
  };

  add(SourceId.Goldberg, 'achievements.json');
  add(SourceId.GSE, 'achievements.json');
  add(SourceId.Empress, 'achievements.json', true);
  add(SourceId.Codex, 'achievements.ini');
  add(SourceId.Rune, 'achievements.ini');
  add(SourceId.OnlineFix, 'achievements.ini');
  add(SourceId.SmartSteamEmu, 'stats.bin');
  add(SourceId.Skidrow, 'achiev.ini');
  add(SourceId.Darksiders, 'achiev.ini');
  add(SourceId.Ali213, 'Achievements.Bin');
  add(SourceId.Hoodlum, 'hlm.ini');
  add(SourceId.CreamApi, 'CreamAPI.Achievements.cfg', true);
  add(SourceId.Reloaded, 'achievements.ini');

  return patterns;
}

async function scanPattern(pattern: ScanPattern): Promise<DiscoveryRecord[]> {
  if (!(await exists(pattern.rootPath))) return [];

  if (pattern.source === SourceId.Empress) {
    return scanEmpressRoot(pattern.rootPath);
  }

  const records: DiscoveryRecord[] = [];
  let subdirs: string[] = [];
  try {
    subdirs = await fs.readdir(pattern.rootPath);
  } catch {
    return records;
  }

  for (const name of subdirs) {
    if (!isNumericAppId(name)) continue;
    const appDir = path.join(pattern.rootPath, name);
    const expectedPath = pattern.includeSubdirectories
      ? await findFirstMatchingFile(appDir, pattern.fileName, 3)
      : path.join(appDir, pattern.fileName);
    if (!expectedPath || !(await exists(expectedPath))) continue;
    records.push({
      appId: name,
      source: pattern.source,
      kind: 'file',
      location: normalizeWindowsPath(expectedPath) ?? expectedPath
    });
  }

  return records;
}

async function scanEmpressRoot(rootPath: string): Promise<DiscoveryRecord[]> {
  const records: DiscoveryRecord[] = [];
  let entries: string[] = [];
  try {
    entries = await fs.readdir(rootPath);
  } catch {
    return records;
  }

  for (const appId of entries) {
    if (!isNumericAppId(appId)) continue;
    const filePath = path.join(rootPath, appId, 'remote', appId, 'achievements.json');
    if (!(await exists(filePath))) continue;
    records.push({
      appId,
      source: SourceId.Empress,
      kind: 'file',
      location: normalizeWindowsPath(filePath) ?? filePath
    });
  }

  return records;
}

async function scanGreenLumaRegistry(registry: RegistryAdapter): Promise<DiscoveryRecord[]> {
  const records: DiscoveryRecord[] = [];

  for (const baseKey of GREENLUMA_BASE_KEYS) {
    let appIds: string[] = [];
    try {
      appIds = await registry.listSubKeys(baseKey);
    } catch {
      continue;
    }

    for (const appId of appIds) {
      if (!isNumericAppId(appId)) continue;
      const appKeyPath = `${baseKey}\\${appId}`;
      const skipValue = await registry.getValue(appKeyPath, 'SkipStatsAndAchievements');
      if (skipValue === null) continue;
      const skip = Number(skipValue);
      if (!Number.isFinite(skip) || skip !== 0) continue;
      const achievementsPath = `${appKeyPath}\\Achievements`;
      records.push({
        appId,
        source: SourceId.GreenLuma,
        kind: 'registry',
        location: `HKCU|${achievementsPath}`
      });
    }
  }

  return records;
}

async function findFirstMatchingFile(baseDir: string, fileName: string, maxDepth: number): Promise<string | null> {
  if (maxDepth < 0 || !(await exists(baseDir))) return null;

  const queue: Array<{ dir: string; depth: number }> = [{ dir: baseDir, depth: 0 }];
  const target = fileName.toLowerCase();

  while (queue.length > 0) {
    const current = queue.shift();
    if (!current) break;

    let entries: Array<import('node:fs').Dirent> = [];
    try {
      entries = await fs.readdir(current.dir, { withFileTypes: true });
    } catch {
      continue;
    }

    for (const entry of entries) {
      const full = path.join(current.dir, entry.name);
      if (entry.isFile() && entry.name.toLowerCase() === target) {
        return full;
      }
      if (entry.isDirectory() && current.depth < maxDepth) {
        queue.push({ dir: full, depth: current.depth + 1 });
      }
    }
  }

  return null;
}

async function exists(filePath: string): Promise<boolean> {
  try {
    await fs.access(filePath);
    return true;
  } catch {
    return false;
  }
}

async function logUnresolved(source: SourceId, fullPath: string, rootPath: string, reason: string): Promise<void> {
  try {
    const { warn } = await import('../ipc/logger.ts');
    await warn(
      `[Discovery] unresolved change ignored source=${source} path="${fullPath}" root="${rootPath}" reason=${reason}`
    );
  } catch {
    /* Logger pulls Electron; ignore when unavailable (e.g. node:test). */
  }
}

function dedupeDiscoveryRecords(records: DiscoveryRecord[]): DiscoveryRecord[] {
  const seen = new Set<string>();
  const unique: DiscoveryRecord[] = [];
  for (const record of records) {
    const key = `${record.source}|${record.appId}|${record.kind}|${record.location.toLowerCase()}`;
    if (seen.has(key)) continue;
    seen.add(key);
    unique.push(record);
  }
  return unique;
}

function expandEnv(value: string): string {
  return value.replace(/%([^%]+)%/g, (_m, key: string) => process.env[key] ?? '');
}
