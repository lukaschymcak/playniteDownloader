import { execFile, spawn, type ChildProcessWithoutNullStreams } from 'node:child_process';
import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { promisify } from 'node:util';
import type { BrowserWindow } from 'electron';
import type { DownloadCancelMode, DownloadHealthState, DownloadStartRequest, GameData, InstalledGame } from '../../shared/types';
import { depPath, findDotnet } from './paths';
import { manifestFilePath } from './zip';
import { saveInstalled } from './games';
import { getDirectorySize, safeError } from './logger';

const execFileAsync = promisify(execFile);
const DOWNLOADS_PROGRESS_CHANNEL = 'downloads:progress';
const PERCENTAGE_REGEX = /(\d{1,3}(?:\.\d{1,2})?)%/;
const RECENT_WINDOW_MS = 60_000;

type RunningTelemetry = {
  interval?: NodeJS.Timeout;
  lastSizeBytes: number;
  lastRxBytes: number;
  smoothSpeedBps: number;
  knownPct: number;
};

type RuntimeState = {
  depots: string[];
  depotSizes: number[];
  totalBytes: number;
  completedBytes: number;
  currentDepotBytes: number;
  outputDir: string;
  tempManifestFiles: string[];
  telemetry: RunningTelemetry;
  lastLogFlushAt: number;
  logBuffer: string[];
  charBuffer: string[];
  healthState: DownloadHealthState;
  lastHealthMessage?: string;
  retryTimestamps: number[];
};

type ActiveDownload = {
  proc?: ChildProcessWithoutNullStreams;
  pid?: number;
  running: boolean;
  cancelRequested: boolean;
  cancelMode: DownloadCancelMode;
  paused: boolean;
  termination: Promise<void>;
  resolveTermination: () => void;
  runtime: RuntimeState;
};

const active = new Map<string, ActiveDownload>();

class DownloadCanceledError extends Error {
  constructor(message = 'Download canceled.') {
    super(message);
    this.name = 'DownloadCanceledError';
  }
}

export async function startDownload(request: DownloadStartRequest, window: BrowserWindow): Promise<void> {
  const dotnet = findDotnet();
  if (!dotnet) throw new Error('dotnet not found.');
  const depotDll = depPath('DepotDownloader.dll');
  const outputDir = request.outputPath || path.join(request.libraryPath, 'steamapps', 'common', safeInstallDir(request.gameData));
  const installDir = request.outputPath ? path.basename(outputDir) : safeInstallDir(request.gameData);
  await fs.mkdir(outputDir, { recursive: true });
  const depotKeys = await writeDepotKeys(request.gameData);

  const depots = request.selectedDepots.length ? request.selectedDepots : Object.keys(request.gameData.depots);
  const depotSizes = depots.map((depotId) => Number(request.gameData.depots[depotId]?.size || 0));
  const totalBytes = depotSizes.reduce((sum, size) => sum + size, 0);
  const tempManifestFiles = depots
    .map((depotId) => request.gameData.manifests[depotId] ? manifestFilePath(depotId, request.gameData.manifests[depotId]) : '')
    .filter((v) => Boolean(v));

  const state = createOrResetActive(request.channelId, depots, depotSizes, totalBytes, outputDir, tempManifestFiles);
  const emit = (payload: Record<string, unknown>): void => {
    window.webContents.send(DOWNLOADS_PROGRESS_CHANNEL, payload);
  };

  emit({
    channelId: request.channelId,
    appId: request.gameData.appId,
    gameName: request.gameData.gameName,
    headerImageUrl: request.gameData.headerImageUrl,
    done: false,
    status: 'Starting...',
    pct: 0,
    totalBytes,
    speedBps: 0,
    diskBps: null,
    etaSec: null,
    startedAt: Date.now(),
    indeterminate: true,
    healthState: 'stable',
    retryCountRecent: 0
  });

  startTelemetryLoop(request.channelId, request.gameData, emit);

  try {
    for (let index = 0; index < depots.length; index++) {
      ensureNotCanceled(state);
      state.runtime.currentDepotBytes = depotSizes[index] || 0;
      state.runtime.telemetry.knownPct = 0;

      const depotId = depots[index];
      const manifest = request.gameData.manifests[depotId];
      if (!manifest) {
        const msg = `Skipping depot ${depotId}: missing manifest GID.`;
        pushLog(state, request.channelId, window, emit, msg, true);
        state.runtime.completedBytes += state.runtime.currentDepotBytes;
        continue;
      }

      pushLog(state, request.channelId, window, emit, `--- Depot ${index + 1}/${depots.length}: ${depotId} ---`, true);
      emitWeightedProgress(state, request.channelId, request.gameData, emit);

      const args = [
        depotDll,
        '-app', request.gameData.appId,
        '-depot', depotId,
        '-manifest', manifest,
        '-manifestfile', manifestFilePath(depotId, manifest),
        '-depotkeys', depotKeys,
        '-max-downloads', String(request.maxDownloads || 20),
        '-dir', outputDir,
        '-validate'
      ];
      if (request.steamUsername) {
        args.splice(1, 0, '-username', request.steamUsername, '-remember-password');
      }

      await runProcess(dotnet, args, request.channelId, window, emit);
      flushLogs(state, request.channelId, window, emit);
      ensureNotCanceled(state);

      state.runtime.completedBytes += state.runtime.currentDepotBytes;
      state.runtime.telemetry.knownPct = 0;
      emitWeightedProgress(state, request.channelId, request.gameData, emit);
    }
  } catch (err) {
    stopTelemetryLoop(state);
    if (isCanceledError(err) || state.cancelRequested) {
      if (state.cancelMode === 'delete') {
        await cleanupCanceledArtifacts(state).catch(() => undefined);
      }
      const msg = state.cancelMode === 'delete'
        ? 'Download canceled and partial files cleaned.'
        : 'Download canceled. Partial files were kept.';
      window.webContents.send(request.channelId, { done: true, canceled: true, status: 'Canceled', terminalReason: 'canceled', log: msg });
      emit({
        channelId: request.channelId,
        appId: request.gameData.appId,
        gameName: request.gameData.gameName,
        headerImageUrl: request.gameData.headerImageUrl,
        done: true,
        canceled: true,
        status: 'Canceled',
        terminalReason: 'canceled',
        indeterminate: false
      });
      return;
    }
    const message = safeError(err);
    window.webContents.send(request.channelId, { error: message, log: `ERROR: ${message}` });
    emit({ channelId: request.channelId, log: `ERROR: ${message}` });
    emit({ channelId: request.channelId, done: true, error: message, status: 'Failed', indeterminate: false, terminalReason: 'failed' });
    throw err;
  } finally {
    stopTelemetryLoop(state);
    await fs.unlink(depotKeys).catch(() => undefined);
    const current = active.get(request.channelId);
    if (current && !current.running) active.delete(request.channelId);
  }

  const installed: InstalledGame = {
    appId: request.gameData.appId,
    gameName: request.gameData.gameName,
    installPath: outputDir,
    libraryPath: request.libraryPath,
    installDir,
    installedDate: new Date().toISOString(),
    sizeOnDisk: await getDirectorySize(outputDir),
    selectedDepots: depots,
    manifestGIDs: Object.fromEntries(depots.map((id) => [id, request.gameData.manifests[id]]).filter(([, gid]) => gid)),
    drmStripped: false,
    registeredWithSteam: false,
    gseSavesCopied: false,
    goldbergState: 'required',
    headerImageUrl: request.gameData.headerImageUrl,
    steamBuildId: request.gameData.buildId || '0',
    manifestZipPath: request.gameData.manifestZipPath
  };
  await saveInstalled(installed);
  window.webContents.send(request.channelId, { done: true, log: 'Download completed.', status: 'Complete', terminalReason: 'completed' });
  emit({
    channelId: request.channelId,
    appId: request.gameData.appId,
    gameName: request.gameData.gameName,
    headerImageUrl: request.gameData.headerImageUrl,
    done: true,
    indeterminate: false,
    status: 'Complete',
    terminalReason: 'completed',
    pct: 100,
    speedBps: null,
    diskBps: null,
    etaSec: null
  });
}

export async function cancelDownload(channelId: string, mode: DownloadCancelMode = 'keep'): Promise<void> {
  const state = active.get(channelId);
  if (!state) return;
  state.cancelRequested = true;
  state.cancelMode = mode;
  pushLog(state, channelId, undefined, undefined, `Cancellation requested (${mode}).`, true);

  if (state.proc?.pid) {
    await terminateProcessTree(state.proc.pid);
    await waitForTermination(state, 1500);
    if (state.running && state.proc?.pid) {
      await terminateProcessTree(state.proc.pid);
      await waitForTermination(state, 1500);
    }
  }
  if (!state.running) active.delete(channelId);
}

export async function authenticateSteam(username: string, password: string, channelId: string, window: BrowserWindow): Promise<void> {
  if (!username.trim()) throw new Error('Enter your Steam username first.');
  if (!password) throw new Error('Enter your Steam password first.');

  const dotnet = findDotnet();
  if (!dotnet) throw new Error('dotnet not found.');
  const depotDll = depPath('DepotDownloader.dll');
  const authTempDir = path.join(os.tmpdir(), 'ludownloader_steam_auth');
  await fs.mkdir(authTempDir, { recursive: true });

  const args = [
    depotDll,
    '-username', username.trim(),
    '-password', password,
    '-remember-password',
    '-app', '228980',
    '-depot', '228981',
    '-manifest', '0',
    '-dir', authTempDir
  ];

  window.webContents.send(channelId, { log: 'Opening Steam authentication window...' });
  window.webContents.send(channelId, { log: 'Complete any Steam Guard prompt in the console, then close it.' });

  await new Promise<void>((resolve, reject) => {
    const proc = spawn(dotnet, args, { windowsHide: false, stdio: 'ignore' });
    proc.on('error', reject);
    proc.on('close', () => resolve());
  });

  window.webContents.send(channelId, { log: 'Authentication window closed. If successful, future downloads will use your Steam account.' });
}

export async function togglePauseDownload(channelId: string, pause: boolean): Promise<void> {
  const state = active.get(channelId);
  if (!state?.proc?.pid) return;
  state.paused = pause;
  await suspendOrResumeProcessTree(state.proc.pid, pause);
}

async function runProcess(
  command: string,
  args: string[],
  channelId: string,
  window: BrowserWindow,
  emit: (payload: Record<string, unknown>) => void
): Promise<void> {
  const state = active.get(channelId);
  if (!state) throw new Error(`No active download state for ${channelId}.`);
  ensureNotCanceled(state);

  return new Promise<void>((resolve, reject) => {
    const proc = spawn(command, args, { windowsHide: true });
    state.proc = proc;
    state.pid = proc.pid;
    state.running = true;

    const finish = (): void => {
      state.running = false;
      state.proc = undefined;
      state.pid = undefined;
      state.resolveTermination();
    };

    const timer = setTimeout(async () => {
      if (state.cancelRequested) return;
      await terminateProcessTree(proc.pid);
      reject(new Error('DepotDownloader did not exit within 2 hours.'));
    }, 2 * 60 * 60 * 1000);

    const onByte = (byte: number): void => {
      const ch = String.fromCharCode(byte);
      if (ch === '\r' || ch === '\n') {
        if (!state.runtime.charBuffer.length) return;
        const line = state.runtime.charBuffer.join('');
        state.runtime.charBuffer.length = 0;
        handleDownloaderOutput(state, channelId, window, emit, line);
        return;
      }
      state.runtime.charBuffer.push(ch);
    };

    proc.stdout.on('data', (chunk: Buffer) => {
      for (let i = 0; i < chunk.length; i++) onByte(chunk[i]);
    });
    proc.stderr.on('data', (chunk: Buffer) => {
      for (let i = 0; i < chunk.length; i++) onByte(chunk[i]);
    });
    proc.on('error', (err) => {
      clearTimeout(timer);
      flushLogs(state, channelId, window, emit);
      finish();
      if (state.cancelRequested) reject(new DownloadCanceledError());
      else reject(err);
    });
    proc.on('close', (code) => {
      clearTimeout(timer);
      flushLogs(state, channelId, window, emit);
      finish();
      if (state.cancelRequested) {
        reject(new DownloadCanceledError());
        return;
      }
      if (code === 0) resolve();
      else {
        pushLog(state, channelId, window, emit, `WARNING: DepotDownloader exited with code ${code}`, true);
        reject(new Error(`DepotDownloader exited with code ${code}`));
      }
    });
  });
}

function createOrResetActive(
  channelId: string,
  depots: string[],
  depotSizes: number[],
  totalBytes: number,
  outputDir: string,
  tempManifestFiles: string[]
): ActiveDownload {
  const existing = active.get(channelId);
  if (existing) {
    existing.cancelRequested = false;
    existing.cancelMode = 'keep';
    existing.paused = false;
    resetTermination(existing);
    existing.runtime = createRuntime(depots, depotSizes, totalBytes, outputDir, tempManifestFiles);
    return existing;
  }

  const state: ActiveDownload = {
    running: false,
    cancelRequested: false,
    cancelMode: 'keep',
    paused: false,
    termination: Promise.resolve(),
    resolveTermination: () => undefined,
    runtime: createRuntime(depots, depotSizes, totalBytes, outputDir, tempManifestFiles)
  };
  resetTermination(state);
  active.set(channelId, state);
  return state;
}

function createRuntime(
  depots: string[],
  depotSizes: number[],
  totalBytes: number,
  outputDir: string,
  tempManifestFiles: string[]
): RuntimeState {
  return {
    depots,
    depotSizes,
    totalBytes,
    completedBytes: 0,
    currentDepotBytes: depotSizes[0] || 0,
    outputDir,
    tempManifestFiles,
    telemetry: {
      lastSizeBytes: 0,
      lastRxBytes: 0,
      smoothSpeedBps: 0,
      knownPct: 0
    },
    lastLogFlushAt: 0,
    logBuffer: [],
    charBuffer: [],
    healthState: 'stable',
    retryTimestamps: []
  };
}

function ensureNotCanceled(state: ActiveDownload): void {
  if (state.cancelRequested) throw new DownloadCanceledError();
}

function isCanceledError(err: unknown): boolean {
  return err instanceof DownloadCanceledError;
}

async function terminateProcessTree(pid: number | undefined): Promise<void> {
  if (!pid) return;
  try {
    await execFileAsync('taskkill', ['/PID', String(pid), '/T', '/F'], { windowsHide: true });
  } catch {
    // Best effort.
  }
}

async function suspendOrResumeProcessTree(pid: number, pause: boolean): Promise<void> {
  if (process.platform !== 'win32') return;
  const verb = pause ? 'Suspend-Process' : 'Resume-Process';
  const script = `
$root = ${pid}
$ids = @($root)
try {
  $children = Get-CimInstance Win32_Process | Where-Object { $_.ParentProcessId -eq $root }
  while ($children.Count -gt 0) {
    $next = @()
    foreach ($c in $children) {
      $ids += $c.ProcessId
      $kids = Get-CimInstance Win32_Process | Where-Object { $_.ParentProcessId -eq $c.ProcessId }
      if ($kids) { $next += $kids }
    }
    $children = $next
  }
} catch {}
foreach ($id in ($ids | Select-Object -Unique)) {
  try { ${verb} -Id $id -ErrorAction SilentlyContinue } catch {}
}
`;
  try {
    await execFileAsync('powershell', ['-NoProfile', '-Command', script], { windowsHide: true });
  } catch {
    // Best effort.
  }
}

async function waitForTermination(state: ActiveDownload, timeoutMs: number): Promise<void> {
  if (!state.running) return;
  await Promise.race([
    state.termination,
    new Promise<void>((resolve) => setTimeout(resolve, timeoutMs))
  ]);
}

function resetTermination(state: ActiveDownload): void {
  state.termination = new Promise<void>((resolve) => {
    state.resolveTermination = resolve;
  });
}

async function writeDepotKeys(gameData: GameData): Promise<string> {
  const filePath = path.join(os.tmpdir(), `ludownloader_depotkeys_${gameData.appId}_${Date.now()}.txt`);
  const lines = Object.entries(gameData.depots).map(([id, depot]) => `${id};${depot.key}`);
  await fs.writeFile(filePath, lines.join('\n'), 'utf8');
  return filePath;
}

function safeInstallDir(data: GameData): string {
  if (data.installDir?.trim()) return data.installDir.trim();
  const safe = (data.gameName || '').replace(/[^\w\s-]/g, '').replace(/\s+/g, ' ').trim();
  return safe || `App_${data.appId}`;
}

function handleDownloaderOutput(
  state: ActiveDownload,
  channelId: string,
  window: BrowserWindow,
  emit: (payload: Record<string, unknown>) => void,
  rawLine: string
): void {
  const line = rawLine.trim();
  if (!line || line.startsWith('   at ')) return;

  const match = PERCENTAGE_REGEX.exec(line);
  if (match) {
    const pct = Number.parseFloat(match[1]);
    if (Number.isFinite(pct)) {
      state.runtime.telemetry.knownPct = Math.max(0, Math.min(100, pct));
      emitWeightedProgress(state, channelId, undefined, emit);
    }
  }

  updateHealthFromLine(state, line);
  pushLog(state, channelId, window, emit, line, /error|warning/i.test(line));
}

function updateHealthFromLine(state: ActiveDownload, line: string): void {
  const lower = line.toLowerCase();
  const now = Date.now();
  state.runtime.retryTimestamps = state.runtime.retryTimestamps.filter((t) => now - t <= RECENT_WINDOW_MS);

  if (lower.includes('connection timeout downloading chunk') || lower.includes('timeout')) {
    state.runtime.retryTimestamps.push(now);
    state.runtime.healthState = state.runtime.retryTimestamps.length >= 3 ? 'degraded' : 'retrying';
    state.runtime.lastHealthMessage = line;
    return;
  }
  if (lower.includes('warning')) {
    state.runtime.healthState = 'warning';
    state.runtime.lastHealthMessage = line;
    return;
  }
  if (lower.includes('connecting') || lower.includes('resum') || lower.includes('retry')) {
    state.runtime.healthState = 'retrying';
    state.runtime.lastHealthMessage = line;
    return;
  }
  if (state.runtime.healthState !== 'degraded' && state.runtime.healthState !== 'warning' && state.runtime.retryTimestamps.length === 0) {
    state.runtime.healthState = 'stable';
    state.runtime.lastHealthMessage = undefined;
  }
}

function pushLog(
  state: ActiveDownload,
  channelId: string,
  window?: BrowserWindow,
  emit?: (payload: Record<string, unknown>) => void,
  line?: string,
  forceFlush = false
): void {
  if (!line) return;
  state.runtime.logBuffer.push(line);
  const now = Date.now();
  const due = now - state.runtime.lastLogFlushAt >= 80;
  if (forceFlush || due) flushLogs(state, channelId, window, emit);
}

function flushLogs(
  state: ActiveDownload,
  channelId: string,
  window?: BrowserWindow,
  emit?: (payload: Record<string, unknown>) => void
): void {
  if (!state.runtime.logBuffer.length) return;
  const combined = state.runtime.logBuffer.join('\n');
  state.runtime.logBuffer.length = 0;
  state.runtime.lastLogFlushAt = Date.now();
  if (window) window.webContents.send(channelId, { log: combined });
  if (emit) emit({ channelId, log: combined });
}

function emitWeightedProgress(
  state: ActiveDownload,
  channelId: string,
  game?: Pick<GameData, 'appId' | 'gameName' | 'headerImageUrl'>,
  emit?: (payload: Record<string, unknown>) => void
): void {
  if (!emit) return;
  const currentBytes = state.runtime.currentDepotBytes * (state.runtime.telemetry.knownPct / 100);
  const totalBytesDone = state.runtime.completedBytes + currentBytes;
  const pct = state.runtime.totalBytes > 0
    ? Math.max(0, Math.min(100, (totalBytesDone / state.runtime.totalBytes) * 100))
    : state.runtime.telemetry.knownPct;
  emit({
    channelId,
    appId: game?.appId,
    gameName: game?.gameName,
    headerImageUrl: game?.headerImageUrl,
    pct,
    totalBytes: state.runtime.totalBytes
  });
}

function startTelemetryLoop(
  channelId: string,
  game: Pick<GameData, 'appId' | 'gameName' | 'headerImageUrl'>,
  emit: (payload: Record<string, unknown>) => void
): void {
  const state = active.get(channelId);
  if (!state) return;
  stopTelemetryLoop(state);
  state.runtime.telemetry.lastSizeBytes = 0;
  state.runtime.telemetry.lastRxBytes = 0;
  state.runtime.telemetry.smoothSpeedBps = 0;

  state.runtime.telemetry.interval = setInterval(() => {
    void (async () => {
      const live = active.get(channelId);
      if (!live || !live.running || live.cancelRequested) return;

      const [size, rxBytes] = await Promise.all([
        getDirectorySize(live.runtime.outputDir).catch(() => 0),
        getGlobalReceivedBytes().catch(() => null)
      ]);
      const diskDelta = Math.max(0, size - live.runtime.telemetry.lastSizeBytes);
      live.runtime.telemetry.lastSizeBytes = size;

      const rxDelta = rxBytes === null
        ? 0
        : Math.max(0, rxBytes - live.runtime.telemetry.lastRxBytes);
      live.runtime.telemetry.lastRxBytes = rxBytes ?? live.runtime.telemetry.lastRxBytes;

      const instantSpeed = live.paused ? 0 : rxDelta;
      const prev = live.runtime.telemetry.smoothSpeedBps;
      live.runtime.telemetry.smoothSpeedBps = prev > 0 ? (prev * 0.7) + (instantSpeed * 0.3) : instantSpeed;

      const currentBytes = live.runtime.currentDepotBytes * (live.runtime.telemetry.knownPct / 100);
      const downloadedBytesEstimate = live.runtime.completedBytes + currentBytes;
      const remaining = Math.max(0, live.runtime.totalBytes - downloadedBytesEstimate);
      const etaSec = !live.paused && live.runtime.telemetry.smoothSpeedBps > 32
        ? Math.round(remaining / live.runtime.telemetry.smoothSpeedBps)
        : null;
      const pct = live.runtime.totalBytes > 0
        ? Math.max(0, Math.min(100, (downloadedBytesEstimate / live.runtime.totalBytes) * 100))
        : live.runtime.telemetry.knownPct;

      emit({
        channelId,
        appId: game.appId,
        gameName: game.gameName,
        headerImageUrl: game.headerImageUrl,
        pct,
        totalBytes: live.runtime.totalBytes,
        speedBps: Math.round(live.runtime.telemetry.smoothSpeedBps),
        diskBps: Math.round(live.paused ? 0 : diskDelta),
        etaSec,
        status: live.paused ? 'Paused' : 'Downloading',
        indeterminate: false,
        healthState: live.runtime.healthState,
        retryCountRecent: live.runtime.retryTimestamps.length,
        lastHealthMessage: live.runtime.lastHealthMessage
      });
    })();
  }, 1000);
}

async function getGlobalReceivedBytes(): Promise<number | null> {
  if (process.platform !== 'win32') return null;
  const script = '(Get-NetAdapterStatistics -ErrorAction SilentlyContinue | Measure-Object -Property ReceivedBytes -Sum).Sum';
  try {
    const { stdout } = await execFileAsync('powershell', ['-NoProfile', '-Command', script], { windowsHide: true });
    const value = Number.parseInt(String(stdout || '').trim(), 10);
    return Number.isFinite(value) && value >= 0 ? value : null;
  } catch {
    return null;
  }
}

function stopTelemetryLoop(state: ActiveDownload): void {
  if (state.runtime.telemetry.interval) {
    clearInterval(state.runtime.telemetry.interval);
    state.runtime.telemetry.interval = undefined;
  }
}

async function cleanupCanceledArtifacts(state: ActiveDownload): Promise<void> {
  await fs.rm(state.runtime.outputDir, { recursive: true, force: true }).catch(() => undefined);
  await Promise.all(state.runtime.tempManifestFiles.map((m) => fs.rm(m, { force: true }).catch(() => undefined)));
}
