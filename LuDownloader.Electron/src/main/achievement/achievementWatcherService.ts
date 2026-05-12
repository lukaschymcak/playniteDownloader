import { watch, existsSync, type FSWatcher } from 'node:fs';
import path from 'node:path';
import type { AchievementDiff, AppSettings, DiscoveryRecord, FileChangeEvent, GameAchievements, InstalledGame, SourceId } from '../../shared/types.ts';
import {
  achievementFileDiscoverySources,
  buildSourceRoots,
  collectExtraDiscoveryRoots,
  getEnabledSources,
  resolveChangedPath,
  scanAllSources as scanAllSourcesDefault
} from './discoveryService.ts';
import type { CacheService } from './cacheService.ts';

export const ACHIEVEMENTS_CHANGED_CHANNEL = 'achievements:changed';
export const ACHIEVEMENTS_DIFF_CHANNEL = 'achievements:diff';

export type AchievementEventSink = (channel: string, payload: unknown) => void;

type WatcherPipeline = {
  scanAllSources: typeof scanAllSourcesDefault;
  listInstalled: () => Promise<InstalledGame[]>;
  processAppId: (
    appId: string,
    discoveryRows: DiscoveryRecord[],
    settings: Pick<AppSettings, 'steamWebApiKey' | 'achievementScanOfficialSteamLibraries'>,
    installedGames: InstalledGame[],
    cache: CacheService,
    fetchFn?: typeof fetch
  ) => Promise<GameAchievements>;
  diffAndUpdateSnapshot: (snapshotRoot: string, current: GameAchievements) => Promise<AchievementDiff[]>;
};

const DEBOUNCE_MS_DEFAULT = 600;
let debounceMs = DEBOUNCE_MS_DEFAULT;
let watchImpl: typeof watch = watch;

async function loadSettingsProduction(): Promise<AppSettings> {
  const { loadSettings } = await import('../ipc/settings.ts');
  return loadSettings();
}

let loadSettingsImpl: () => Promise<AppSettings> = loadSettingsProduction;

function createDefaultPipeline(): WatcherPipeline {
  return {
    scanAllSources: scanAllSourcesDefault,
    listInstalled: async () => {
      const { listInstalled } = await import('../ipc/games.ts');
      return listInstalled();
    },
    processAppId: async (...args) => {
      const { processAppId } = await import('./processAppId.ts');
      return processAppId(...args);
    },
    diffAndUpdateSnapshot: async (...args) => {
      const { diffAndUpdateSnapshot } = await import('./achievementSnapshotService.ts');
      return diffAndUpdateSnapshot(...args);
    }
  };
}

let pipeline: WatcherPipeline = createDefaultPipeline();

let eventSink: AchievementEventSink | null = null;
let serviceStarted = false;
let serviceStopped = true;

const rootWatchers = new Map<string, FSWatcher>();
const debounceTimers = new Map<string, ReturnType<typeof setTimeout>>();
const appProcessChains = new Map<string, Promise<void>>();

let achievementDirsForTests: { cacheDir: string; snapshotDir: string } | null = null;

async function resolveAchievementDirs(): Promise<{ cacheDir: string; snapshotDir: string }> {
  if (achievementDirsForTests) {
    return achievementDirsForTests;
  }
  const { achievementCacheDir, achievementSnapshotDir } = await import('../ipc/paths.ts');
  return { cacheDir: achievementCacheDir(), snapshotDir: achievementSnapshotDir() };
}

function watcherKey(source: SourceId, root: string): string {
  return `${source}\u0000${root.toLowerCase()}`;
}

// All sources collapse to one debounce chain per appId.
function debounceKey(_source: SourceId, appId: string): string {
  return appId;
}

export function setAchievementWatcherDirsForTests(dirs: { cacheDir: string; snapshotDir: string } | null): void {
  achievementDirsForTests = dirs;
}

export function setAchievementEventSink(sink: AchievementEventSink | null): void {
  eventSink = sink;
}

export function setLoadSettingsForTests(fn: (() => Promise<AppSettings>) | null): void {
  loadSettingsImpl = fn ?? loadSettingsProduction;
}

export function setAchievementWatcherPipelineForTests(partial: Partial<WatcherPipeline> | null): void {
  if (partial === null) {
    pipeline = createDefaultPipeline();
    return;
  }
  const base = createDefaultPipeline();
  pipeline = {
    scanAllSources: partial.scanAllSources ?? base.scanAllSources,
    listInstalled: partial.listInstalled ?? base.listInstalled,
    processAppId: partial.processAppId ?? base.processAppId,
    diffAndUpdateSnapshot: partial.diffAndUpdateSnapshot ?? base.diffAndUpdateSnapshot
  };
}

export function setAchievementWatchDebounceMsForTests(ms: number): void {
  debounceMs = ms;
}

export function resetAchievementWatchTestState(): void {
  achievementDirsForTests = null;
  debounceMs = DEBOUNCE_MS_DEFAULT;
  watchImpl = watch;
  loadSettingsImpl = loadSettingsProduction;
  pipeline = createDefaultPipeline();
  eventSink = null;
  serviceStopped = true;
  serviceStarted = false;
  clearAllDebouncers();
  closeAllWatchers();
  appProcessChains.clear();
}

export function setFsWatchForTests(fn: typeof watch): void {
  watchImpl = fn;
}

// node:fs.watch only fires 'change'/'rename'; 'add'/'unlink' are reserved for a future chokidar integration.
function mapFsEventToChangeKind(eventType: string): FileChangeEvent['eventType'] {
  return eventType === 'rename' ? 'rename' : 'change';
}

function closeAllWatchers(): void {
  for (const w of rootWatchers.values()) {
    w.close();
  }
  rootWatchers.clear();
}

function clearAllDebouncers(): void {
  for (const t of debounceTimers.values()) {
    clearTimeout(t);
  }
  debounceTimers.clear();
}

async function logWatcherError(err: unknown): Promise<void> {
  try {
    const { safeError, warn } = await import('../ipc/logger.ts');
    await warn(`[AchievementWatcher] ${safeError(err)}`);
  } catch {
    /* Logger pulls Electron; ignore when unavailable. */
  }
}

async function logInfo(message: string): Promise<void> {
  try {
    const { info } = await import('../ipc/logger.ts');
    await info(message);
  } catch {
    /* ignore */
  }
}

async function rebuildWatchers(): Promise<void> {
  closeAllWatchers();
  const settings = await loadSettingsImpl();
  const enabled = getEnabledSources(settings);
  const extras = await collectExtraDiscoveryRoots(settings);
  const rootsBySource = buildSourceRoots(settings, extras);

  for (const source of achievementFileDiscoverySources()) {
    if (!enabled.has(source)) {
      continue;
    }
    for (const root of rootsBySource[source] ?? []) {
      if (!existsSync(root)) {
        continue;
      }
      const key = watcherKey(source, root);
      if (rootWatchers.has(key)) {
        continue;
      }
      try {
        /* `recursive` is used on win32 so one watcher covers nested appId folders; GreenLuma stays registry-based. */
        const watcher = watchImpl(root, { recursive: true }, (eventType, fileName) => {
          void handleFsWatchCallback(source, root, eventType, fileName);
        });
        rootWatchers.set(key, watcher);
      } catch {
        /* Missing permissions or unsupported root: skip. */
      }
    }
  }
}

async function handleFsWatchCallback(
  source: SourceId,
  root: string,
  eventType: string,
  fileName: string | Buffer | null
): Promise<void> {
  if (serviceStopped) {
    return;
  }
  if (fileName == null) {
    return;
  }
  const rel = typeof fileName === 'string' ? fileName : fileName.toString('utf8');
  if (!rel) {
    return;
  }
  const basename = path.basename(rel);
  if (/\.(tmp|temp)$/i.test(basename) || basename === '.writable') {
    return;
  }
  // Steam-internal files; never contain emulator achievement data.
  if (/^(localconfig\.vdf|config|remotecache\.vdf)$/i.test(basename)) {
    return;
  }
  const fullPath = path.resolve(root, rel);
  const settings = await loadSettingsImpl();
  const change: FileChangeEvent = {
    source,
    rootPath: root,
    fullPath,
    eventType: mapFsEventToChangeKind(eventType),
    timestamp: Date.now()
  };
  const resolved = await resolveChangedPath(change, settings);
  if (!resolved) {
    return;
  }
  void logInfo(`[Watcher] change detected appId=${resolved.appId} source=${source} file="${basename}" — debouncing ${debounceMs}ms`);
  scheduleDebouncedProcessing(source, resolved.appId);
}

function scheduleDebouncedProcessing(source: SourceId, appId: string): void {
  const key = debounceKey(source, appId);
  const prior = debounceTimers.get(key);
  if (prior) {
    clearTimeout(prior);
  }
  const timer = setTimeout(() => {
    debounceTimers.delete(key);
    enqueueAppProcessing(appId);
  }, debounceMs);
  debounceTimers.set(key, timer);
}

function enqueueAppProcessing(appId: string): void {
  const next = (appProcessChains.get(appId) ?? Promise.resolve())
    .then(() => processResolvedAppId(appId))
    .catch((err: unknown) => logWatcherError(err));
  appProcessChains.set(appId, next);
  void next.finally(() => {
    if (appProcessChains.get(appId) === next) {
      appProcessChains.delete(appId);
    }
  });
}

async function processResolvedAppId(appId: string): Promise<void> {
  if (serviceStopped) {
    return;
  }
  const t0 = Date.now();
  void logInfo(`[Watcher] processing appId=${appId} start`);
  const settings: AppSettings = await loadSettingsImpl();
  const records = await pipeline.scanAllSources(settings);
  const rows = records.filter((r) => r.appId === appId);
  if (rows.length === 0) {
    void logInfo(`[Watcher] skipping appId=${appId} — no discovery rows found`);
    return;
  }
  const installedGames = await pipeline.listInstalled();
  const { cacheDir, snapshotDir } = await resolveAchievementDirs();
  const { CacheService } = await import('./cacheService.ts');
  const cache = new CacheService(cacheDir);
  void logInfo(`[Watcher] calling processAppId appId=${appId} rows=${rows.length}`);
  const gameAchievements: GameAchievements = await pipeline.processAppId(
    appId,
    rows,
    {
      steamWebApiKey: settings.steamWebApiKey,
      achievementScanOfficialSteamLibraries: settings.achievementScanOfficialSteamLibraries
    },
    installedGames,
    cache
  );
  void logInfo(`[Watcher] processAppId done appId=${appId} elapsed=${Date.now() - t0}ms`);
  const diffs: AchievementDiff[] = await pipeline.diffAndUpdateSnapshot(snapshotDir, gameAchievements);
  void logInfo(`[Watcher] diffs appId=${appId} count=${diffs.length} elapsed=${Date.now() - t0}ms`);
  eventSink?.(ACHIEVEMENTS_CHANGED_CHANNEL, gameAchievements);
  eventSink?.(ACHIEVEMENTS_DIFF_CHANNEL, { appId, diffs });
  const dest = path.join(cacheDir, gameAchievements.appId, 'game_achievements.json');
  const tmp = `${dest}.tmp`;
  try {
    const { mkdir, writeFile, rename } = await import('node:fs/promises');
    await mkdir(path.dirname(dest), { recursive: true });
    await writeFile(tmp, JSON.stringify(gameAchievements));
    await rename(tmp, dest);
  } catch {
    /* non-fatal */
  }
}

export async function processAchievementWatchTestEvent(
  source: SourceId,
  root: string,
  eventType: string,
  fileName: string | Buffer | null
): Promise<void> {
  return handleFsWatchCallback(source, root, eventType, fileName);
}

async function initialSweep(): Promise<void> {
  try {
    const settings = await loadSettingsImpl();
    const records = await pipeline.scanAllSources(settings);
    const appIds = [...new Set(records.map((r) => r.appId))];
    void logInfo(`[Watcher] initial sweep: ${appIds.length} apps discovered`);
    for (const appId of appIds) {
      enqueueAppProcessing(appId);
    }
  } catch (err) {
    await logWatcherError(err);
  }
}

export async function startAchievementWatchers(): Promise<void> {
  if (serviceStarted) {
    return;
  }
  serviceStarted = true;
  serviceStopped = false;
  await rebuildWatchers();
  void initialSweep();
}

export async function stopAchievementWatchers(): Promise<void> {
  serviceStopped = true;
  serviceStarted = false;
  clearAllDebouncers();
  closeAllWatchers();
}

export async function restartAchievementWatchers(): Promise<void> {
  await stopAchievementWatchers();
  serviceStopped = false;
  serviceStarted = false;
  await startAchievementWatchers();
}
