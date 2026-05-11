import { contextBridge, ipcRenderer } from 'electron';
import { IPC } from '../shared/ipc';
import type {
  AppSettings,
  DownloadCancelMode,
  DownloadStartRequest,
  GameData,
  InstalledGame,
  ManifestCacheEntry,
  SavedLibraryGame
} from '../shared/types';

const invoke = <T>(channel: string, ...args: unknown[]): Promise<T> => ipcRenderer.invoke(channel, ...args) as Promise<T>;

const lu = {
  settings: {
    load: () => invoke<AppSettings>(IPC.settingsLoad),
    save: (settings: AppSettings) => invoke<AppSettings>(IPC.settingsSave, settings)
  },
  games: {
    getAll: () => invoke<InstalledGame[]>(IPC.gamesList),
    save: (game: InstalledGame) => invoke<InstalledGame>(IPC.gamesSave, game),
    remove: (appId: string) => invoke<void>(IPC.gamesRemove, appId),
    reconcile: () => invoke(IPC.gamesReconcile),
    linkFolder: (appId: string, folderPath: string, gameName?: string) => invoke(IPC.gamesLinkFolder, appId, folderPath, gameName),
    setGoldbergState: (appId: string, state: 'required' | 'applied' | 'not_needed') =>
      invoke<InstalledGame>(IPC.gamesSetGoldbergState, appId, state)
  },
  library: {
    rows: () => invoke(IPC.libraryRows),
    getAll: () => invoke<SavedLibraryGame[]>(IPC.libraryList),
    add: (game: Pick<SavedLibraryGame, 'appId' | 'gameName' | 'headerImageUrl'>) => invoke<SavedLibraryGame>(IPC.libraryAdd, game),
    remove: (appId: string) => invoke<void>(IPC.libraryRemove, appId)
  },
  morrenus: {
    health: () => invoke<boolean>(IPC.morrenusHealth),
    getUserStats: () => invoke(IPC.morrenusStats),
    search: (query: string, mode: 'games' | 'dlc' = 'games') => invoke(IPC.morrenusSearch, query, mode),
    downloadManifest: (appId: string, destination: string, channelId?: string) => invoke<string>(IPC.morrenusDownloadManifest, appId, destination, channelId)
  },
  zip: {
    process: (zipPath: string) => invoke<GameData>(IPC.zipProcess, zipPath)
  },
  manifest: {
    listCached: () => invoke<ManifestCacheEntry[]>(IPC.manifestListCached),
    cachePath: (appId: string) => invoke<string>(IPC.manifestCachePath, appId),
    deleteCached: (filePath: string) => invoke<void>(IPC.manifestDeleteCached, filePath),
    check: (appIds: string[]) => invoke(IPC.manifestCheck, appIds)
  },
  updates: {
    check: (appIds?: string[]) => invoke(IPC.updatesCheck, appIds),
    fetchGids: (appIds: string[]) => invoke(IPC.updatesFetchGids, appIds)
  },
  download: {
    start: (request: DownloadStartRequest) => invoke<void>(IPC.downloadStart, request),
    cancel: (channelId: string, mode: DownloadCancelMode = 'keep') => invoke<void>(IPC.downloadCancel, channelId, mode),
    pause: (channelId: string) => invoke<void>(IPC.downloadPause, channelId),
    resume: (channelId: string) => invoke<void>(IPC.downloadResume, channelId)
  },
  depot: {
    authenticate: (username: string, password: string, channelId: string) => invoke<void>(IPC.depotAuthenticate, username, password, channelId)
  },
  steam: {
    getLibraries: () => invoke<string[]>(IPC.steamLibraries),
    writeAcf: (gameData: GameData, libraryPath: string) => invoke<string>(IPC.steamWriteAcf, gameData, libraryPath),
    addToSteam: (appId: string) => invoke<InstalledGame>(IPC.steamAddToSteam, appId),
    openFolder: (target: string) => invoke<void>(IPC.steamOpenFolder, target)
  },
  steamless: {
    run: (gameDir: string, channelId: string) => invoke<void>(IPC.steamlessRun, gameDir, channelId)
  },
  goldberg: {
    run: (
      gameDir: string,
      appId: string,
      channelId: string,
      mode: 'full' | 'achievements_only' = 'full',
      copyFiles = true,
      arch: 'auto' | 'x32' | 'x64' = 'auto'
    ) => invoke<void>(IPC.goldbergRun, gameDir, appId, channelId, mode, copyFiles, arch)
  },
  igdb: {
    search: (query: string) => invoke(IPC.igdbSearch, query),
    getMetadata: (query: string) => invoke(IPC.igdbMetadata, query)
  },
  system: {
    pickFile: (filters?: Electron.FileFilter[]) => invoke<string | null>(IPC.systemPickFile, filters),
    pickFolder: () => invoke<string | null>(IPC.systemPickFolder),
    openPath: (target: string) => invoke<void>(IPC.systemOpenPath, target),
    showItemInFolder: (target: string) => invoke<void>(IPC.systemShowItem, target),
    getDotnetVersion: () => invoke<string | null>(IPC.systemDotnet),
    getDiskFreeSpace: (target: string) => invoke<number | null>(IPC.systemDiskFree, target)
  },
  window: {
    minimize: () => invoke<void>(IPC.windowMinimize),
    maximize: () => invoke<void>(IPC.windowMaximize),
    close: () => invoke<void>(IPC.windowClose)
  },
  on: (channel: string, cb: (data: unknown) => void) => {
    const listener = (_event: Electron.IpcRendererEvent, data: unknown) => cb(data);
    ipcRenderer.on(channel, listener);
    return () => ipcRenderer.removeListener(channel, listener);
  }
};

contextBridge.exposeInMainWorld('api', lu);

export type AppApi = typeof lu;
