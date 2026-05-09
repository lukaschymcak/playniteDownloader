import { app, BrowserWindow, Menu, dialog, ipcMain, shell } from 'electron';
import fsp from 'node:fs/promises';
import path from 'node:path';
import { is } from '@electron-toolkit/utils';
import { IPC } from '../shared/ipc';
import type { DownloadStartRequest, GameData, InstalledGame, SavedLibraryGame, UpdateStatus } from '../shared/types';
import { loadSettings, saveSettings } from './ipc/settings';
import { userDataRoot, findDotnet } from './ipc/paths';
import { info, safeError } from './ipc/logger';
import {
  addSavedLibrary,
  buildLibraryRows,
  linkFolder,
  listInstalled,
  listSavedLibrary,
  reconcileInstalled,
  removeInstalled,
  removeSavedLibrary,
  saveInstalled
} from './ipc/games';
import { checkHealth, downloadManifest, getUserStats, searchGames } from './ipc/morrenus';
import { processZip } from './ipc/zip';
import { checkUpdates, deleteCachedManifest, fetchManifestGids, getCachedManifestPath, listCachedManifests, runManifestChecker } from './ipc/manifest';
import { authenticateSteam, cancelDownload, startDownload } from './ipc/depot';
import {
  addToSteam,
  getSteamLibraries,
  openPath,
  showItemInFolder,
  writeAcf
} from './ipc/steam';
import { runSteamless } from './ipc/steamless';
import { igdbSearch } from './ipc/igdb';
import { CloudSyncAgent } from './sync/cloudSync';

let mainWindow: BrowserWindow | null = null;
let updateStatuses: Record<string, UpdateStatus> = {};
const cloudSync = new CloudSyncAgent();

function createWindow(): void {
  Menu.setApplicationMenu(null);
  mainWindow = new BrowserWindow({
    width: 1100,
    height: 750,
    minWidth: 680,
    minHeight: 560,
    frame: false,
    autoHideMenuBar: true,
    show: false,
    title: 'LuDownloader',
    backgroundColor: '#0B0C0F',
    webPreferences: {
      preload: path.join(__dirname, '../preload/index.mjs'),
      sandbox: false,
      contextIsolation: true,
      nodeIntegration: false
    }
  });

  mainWindow.on('ready-to-show', () => mainWindow?.show());
  mainWindow.on('closed', () => { mainWindow = null; });
  mainWindow.once('ready-to-show', () => {
    if (mainWindow) cloudSync.setWindow(mainWindow);
    void cloudSync.onStartup();
  });

  if (is.dev && process.env.ELECTRON_RENDERER_URL) {
    void mainWindow.loadURL(process.env.ELECTRON_RENDERER_URL);
  } else {
    void mainWindow.loadFile(path.join(__dirname, '../renderer/index.html'));
  }
}

app.whenReady().then(async () => {
  await info(`LuDownloader Electron starting. Data root: ${userDataRoot()}`);
  registerIpc();
  createWindow();
  app.on('activate', () => {
    if (BrowserWindow.getAllWindows().length === 0) createWindow();
  });
});

app.on('before-quit', () => cloudSync.stop());

app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') app.quit();
});

function registerIpc(): void {
  ipcMain.handle(IPC.settingsLoad, () => loadSettings());
  ipcMain.handle(IPC.settingsSave, (_e, settings) => saveSettings(settings));

  ipcMain.handle(IPC.gamesList, () => listInstalled());
  ipcMain.handle(IPC.gamesSave, (_e, game: InstalledGame) => saveInstalled(game));
  ipcMain.handle(IPC.gamesRemove, (_e, appId: string) => removeInstalled(appId, true));
  ipcMain.handle(IPC.gamesReconcile, async () => {
    const result = await reconcileInstalled();
    await sendLibraryChanged();
    return result;
  });
  ipcMain.handle(IPC.gamesLinkFolder, async (_e, appId: string, folderPath: string, gameName?: string) => {
    const game = await linkFolder(appId, folderPath, gameName);
    await sendLibraryChanged();
    return game;
  });

  ipcMain.handle(IPC.libraryList, () => listSavedLibrary());
  ipcMain.handle(IPC.libraryRows, () => buildLibraryRows(updateStatuses));
  ipcMain.handle(IPC.libraryAdd, async (_e, game: Pick<SavedLibraryGame, 'appId' | 'gameName' | 'headerImageUrl'>) => {
    const saved = await addSavedLibrary(game);
    await sendLibraryChanged();
    return saved;
  });
  ipcMain.handle(IPC.libraryRemove, async (_e, appId: string) => {
    await removeSavedLibrary(appId);
    await sendLibraryChanged();
  });

  ipcMain.handle(IPC.morrenusHealth, () => checkHealth());
  ipcMain.handle(IPC.morrenusStats, () => getUserStats());
  ipcMain.handle(IPC.morrenusSearch, (_e, query: string, mode: 'games' | 'dlc' = 'games') => searchGames(query, mode));
  ipcMain.handle(IPC.morrenusDownloadManifest, (event, appId: string, destination: string, channelId?: string) =>
    downloadManifest(appId, destination, channelId, BrowserWindow.fromWebContents(event.sender) || undefined));

  ipcMain.handle(IPC.zipProcess, (_e, zipPath: string) => processZip(zipPath));
  ipcMain.handle(IPC.manifestListCached, () => listCachedManifests());
  ipcMain.handle(IPC.manifestCachePath, (_e, appId: string) => getCachedManifestPath(appId));
  ipcMain.handle(IPC.manifestDeleteCached, async (_e, filePath: string) => {
    await deleteCachedManifest(filePath);
    await sendLibraryChanged();
  });
  ipcMain.handle(IPC.manifestCheck, (_e, appIds: string[]) => runManifestChecker(appIds));
  ipcMain.handle(IPC.updatesCheck, async (_e, appIds?: string[]) => {
    const statuses = await checkUpdates(appIds);
    updateStatuses = mergeStatuses(updateStatuses, statuses, !appIds?.length);
    mainWindow?.webContents.send('updates:changed', updateStatuses);
    await sendLibraryChanged();
    return statuses;
  });
  ipcMain.handle(IPC.updatesFetchGids, async (_e, appIds: string[]) => {
    const statuses = await fetchManifestGids(appIds);
    updateStatuses = mergeStatuses(updateStatuses, statuses, false);
    mainWindow?.webContents.send('updates:changed', updateStatuses);
    await sendLibraryChanged();
    return statuses;
  });

  ipcMain.handle(IPC.downloadStart, async (event, request: DownloadStartRequest) => {
    await startDownload(request, BrowserWindow.fromWebContents(event.sender) || mainWindow!);
    await sendLibraryChanged();
  });
  ipcMain.handle(IPC.downloadCancel, (_e, channelId: string) => cancelDownload(channelId));
  ipcMain.handle(IPC.depotAuthenticate, (event, username: string, password: string, channelId: string) =>
    authenticateSteam(username, password, channelId, BrowserWindow.fromWebContents(event.sender) || mainWindow!));

  ipcMain.handle(IPC.steamLibraries, () => getSteamLibraries());
  ipcMain.handle(IPC.steamWriteAcf, async (_e, gameData: GameData, libraryPath: string) => writeAcf(gameData, libraryPath));
  ipcMain.handle(IPC.steamAddToSteam, async (_e, appId: string) => {
    const game = await addToSteam(appId);
    await sendLibraryChanged();
    return game;
  });
  ipcMain.handle(IPC.steamOpenFolder, (_e, target: string) => openPath(target));
  ipcMain.handle(IPC.steamlessRun, (event, gameDir: string, channelId: string) =>
    runSteamless(gameDir, channelId, BrowserWindow.fromWebContents(event.sender) || mainWindow!));

  ipcMain.handle(IPC.igdbSearch, async (_e, query: string) => igdbSearch(query, await loadSettings()));
  ipcMain.handle(IPC.igdbMetadata, async (_e, query: string) => igdbSearch(query, await loadSettings()));

  ipcMain.handle(IPC.systemPickFile, async (_e, filters?: Electron.FileFilter[]) => {
    const result = await dialog.showOpenDialog(mainWindow!, { properties: ['openFile'], filters });
    return result.canceled ? null : result.filePaths[0];
  });
  ipcMain.handle(IPC.systemPickFolder, async () => {
    const result = await dialog.showOpenDialog(mainWindow!, { properties: ['openDirectory'] });
    return result.canceled ? null : result.filePaths[0];
  });
  ipcMain.handle(IPC.systemOpenPath, (_e, target: string) => shell.openPath(target));
  ipcMain.handle(IPC.systemShowItem, (_e, target: string) => showItemInFolder(target));
  ipcMain.handle(IPC.systemDotnet, () => findDotnet());
  ipcMain.handle(IPC.systemDiskFree, (_e, target: string) => getDiskFreeSpace(target));
  ipcMain.handle(IPC.windowMinimize, (event) => BrowserWindow.fromWebContents(event.sender)?.minimize());
  ipcMain.handle(IPC.windowMaximize, (event) => {
    const win = BrowserWindow.fromWebContents(event.sender);
    if (!win) return;
    if (win.isMaximized()) win.unmaximize();
    else win.maximize();
  });
  ipcMain.handle(IPC.windowClose, (event) => BrowserWindow.fromWebContents(event.sender)?.close());
}

async function getDiskFreeSpace(target: string): Promise<number | null> {
  if (!target) return null;
  try {
    const root = path.parse(path.resolve(target)).root || target;
    const stats = await fsp.statfs(root);
    return Number(stats.bavail) * Number(stats.bsize);
  } catch {
    return null;
  }
}

async function sendLibraryChanged(): Promise<void> {
  try {
    mainWindow?.webContents.send('library:changed', await buildLibraryRows(updateStatuses));
    void cloudSync.pushLibrary().catch(() => undefined);
  } catch (err) {
    mainWindow?.webContents.send('app:error', safeError(err));
  }
}

function mergeStatuses(
  existing: Record<string, UpdateStatus>,
  statuses: UpdateStatus[],
  replaceAll: boolean
): Record<string, UpdateStatus> {
  const next = replaceAll ? {} : { ...existing };
  for (const status of statuses) {
    next[status.appId] = status;
  }
  return next;
}
