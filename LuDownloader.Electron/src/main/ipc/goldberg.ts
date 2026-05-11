import { spawn } from 'node:child_process';
import fs from 'node:fs/promises';
import { existsSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import type { BrowserWindow } from 'electron';
import { listInstalled, saveInstalled } from './games';
import { loadSettings } from './settings';
import { safeError } from './logger';

type RunMode = 'full' | 'achievements_only';
type RunArch = 'auto' | 'x32' | 'x64';

export async function runGoldberg(
  gameDir: string,
  appId: string,
  channelId: string,
  window: BrowserWindow,
  mode: RunMode = 'full',
  copyFiles = true,
  arch: RunArch = 'auto'
): Promise<void> {
  const settings = await loadSettings();
  const goldbergRoot = settings.goldbergFilesPath?.trim();
  if (!goldbergRoot) throw new Error('Goldberg files path is not configured.');
  if (!existsSync(gameDir)) throw new Error(`Game directory not found: ${gameDir}`);

  const genDir = path.join(goldbergRoot, 'generate_emu_config');
  const genExe = path.join(genDir, 'generate_emu_config.exe');
  if (!existsSync(genExe)) throw new Error(`generate_emu_config.exe not found at: ${genExe}`);

  const installed = (await listInstalled()).find((g) => g.appId === appId);
  const gseSavesCopied = Boolean(installed?.gseSavesCopied);
  const detectedArch = await detectArch(gameDir);

  const log = (line: string): void => {
    window.webContents.send(channelId, { log: `[Goldberg] ${line}` });
  };

  log('=== Goldberg Emulator Setup ===');
  log(`Game dir: ${gameDir}`);
  log(`AppId: ${appId}`);
  const effectiveArch = arch === 'auto' ? (detectedArch || 'auto') : arch;
  log(`Arch: ${effectiveArch}`);
  log(`Mode: ${mode}`);
  log(`Copy: ${copyFiles ? 'yes (into game dir)' : 'no (output path only)'}`);

  if (settings.goldbergAccountName?.trim() || settings.goldbergSteamId?.trim()) {
    log('--- Step 1: Patching configs.user.ini ---');
    await patchUserConfig(
      path.join(genDir, '_DEFAULT', '1', 'steam_settings', 'configs.user.ini'),
      settings.goldbergAccountName,
      settings.goldbergSteamId,
      log
    );
    await patchUserConfig(
      path.join(goldbergRoot, '0. Files to put into GSE Saves folder', 'configs.user.ini'),
      settings.goldbergAccountName,
      settings.goldbergSteamId,
      log
    );
  } else {
    log('--- Step 1: Skipping configs.user.ini (no account set) ---');
  }

  if (!gseSavesCopied) {
    log('--- Step 2: Copying GSE Saves folder (first run) ---');
    await copyGseSavesFolder(goldbergRoot, log);
  } else {
    log('--- Step 2: Skipping GSE Saves (already copied) ---');
  }

  log('--- Step 3: Copying my_login.txt ---');
  const loginSrc = path.join(goldbergRoot, 'my_login.txt');
  const loginDest = path.join(genDir, 'my_login.txt');
  if (existsSync(loginSrc)) {
    await fs.copyFile(loginSrc, loginDest);
    log('Copied my_login.txt');
  } else {
    log('WARNING: my_login.txt not found, skipping.');
  }

  log(`--- Step 4: Running generate_emu_config -acw ${appId} ---`);
  await runGenerateEmuConfig(genExe, genDir, appId, log);

  const outputDir = path.resolve(path.join(genDir, '_OUTPUT', String(appId).trim()));
  if (!copyFiles) {
    log(`Skipping file copy (user chose output only). Generated files under: ${outputDir}`);
    log('=== Goldberg setup complete (no copy) ===');
    window.webContents.send(channelId, { done: true, log: `[Goldberg] Output generated: ${outputDir}` });
    return;
  }

  const dllDirs = await findDllDirectories(gameDir);
  if (!dllDirs.length) {
    log('No steam DLLs found in game dir. Copying to game root.');
    dllDirs.push(gameDir);
  }

  if (mode === 'full') {
    log('--- Step 5: Backing up original DLLs ---');
    await backupDlls(dllDirs, log);
    log('--- Step 6: Copying Goldberg files to game dir ---');
    await copyOutput(outputDir, dllDirs, true, effectiveArch, log);
  } else {
    log('--- Step 5: Skipping DLL backup (achievements-only mode) ---');
    log('--- Step 6: Copying steam_settings only ---');
    await copyOutput(outputDir, dllDirs, false, effectiveArch, log);
  }

  if (installed) {
    await saveInstalled({ ...installed, gseSavesCopied: true, goldbergState: 'applied' });
  }

  log('=== Goldberg setup complete ===');
  window.webContents.send(channelId, { done: true, log: '[Goldberg] Goldberg emulator applied successfully.' });
}

async function detectArch(gameDir: string): Promise<'x64' | 'x32' | null> {
  let has64 = false;
  let has32 = false;
  for await (const file of walkFiles(gameDir)) {
    const name = path.basename(file).toLowerCase();
    if (name === 'steam_api64.dll') has64 = true;
    if (name === 'steam_api.dll') has32 = true;
    if (has64 && has32) return null;
  }
  if (has64 && !has32) return 'x64';
  if (has32 && !has64) return 'x32';
  return null;
}

async function patchUserConfig(filePath: string, accountName: string, steamId: string, log: (line: string) => void): Promise<void> {
  if (!existsSync(filePath)) {
    log(`WARNING: configs.user.ini not found: ${filePath}`);
    return;
  }
  const source = await fs.readFile(filePath, 'utf8');
  const lines = source.split(/\r?\n/);
  const next = lines.map((line) => {
    const trimmed = line.trimStart().toLowerCase();
    if (accountName?.trim() && trimmed.startsWith('account_name=')) return `account_name=${accountName.trim()}`;
    if (steamId?.trim() && trimmed.startsWith('account_steamid=')) return `account_steamid=${steamId.trim()}`;
    return line;
  }).join(os.EOL);
  await fs.writeFile(filePath, next, 'utf8');
  log(`Patched: ${path.basename(filePath)}`);
}

async function copyGseSavesFolder(goldbergRoot: string, log: (line: string) => void): Promise<void> {
  const src = path.join(goldbergRoot, '0. Files to put into GSE Saves folder');
  const dest = path.join(process.env.APPDATA || path.join(os.homedir(), 'AppData', 'Roaming'), 'GSE Saves');
  if (!existsSync(src)) {
    log('WARNING: GSE Saves source folder not found, skipping.');
    return;
  }
  await fs.mkdir(dest, { recursive: true });
  const entries = await fs.readdir(src, { withFileTypes: true });
  for (const entry of entries) {
    if (!entry.isFile()) continue;
    const from = path.join(src, entry.name);
    const to = path.join(dest, entry.name);
    await fs.copyFile(from, to);
    log(`GSE Saves: copied ${entry.name}`);
  }
}

async function runGenerateEmuConfig(exePath: string, workingDirectory: string, appId: string, log: (line: string) => void): Promise<void> {
  if (!/^\d+$/.test(String(appId).trim())) throw new Error('Invalid Steam AppId for generate_emu_config.');
  await new Promise<void>((resolve, reject) => {
    const proc = spawn(exePath, ['-acw', String(appId).trim()], {
      cwd: workingDirectory,
      windowsHide: true
    });
    proc.stdout.on('data', (chunk: Buffer) => {
      const text = chunk.toString('utf8').trim();
      if (text) log(text);
    });
    proc.stderr.on('data', (chunk: Buffer) => {
      const text = chunk.toString('utf8').trim();
      if (text) log(`[stderr] ${text}`);
    });
    proc.on('error', reject);
    proc.on('close', (code) => {
      if (code && code !== 0) log(`WARNING: generate_emu_config exited with code ${code}`);
      resolve();
    });
  });
}

async function findDllDirectories(gameDir: string): Promise<string[]> {
  const names = new Set(['steam_api.dll', 'steam_api64.dll', 'steamclient.dll', 'steamclient64.dll']);
  const dirs = new Set<string>();
  for await (const file of walkFiles(gameDir)) {
    if (names.has(path.basename(file).toLowerCase())) {
      dirs.add(path.dirname(file));
    }
  }
  return [...dirs];
}

async function backupDlls(directories: string[], log: (line: string) => void): Promise<void> {
  const dlls = ['steam_api.dll', 'steam_api64.dll', 'steamclient.dll', 'steamclient64.dll'];
  for (const dir of directories) {
    for (const dll of dlls) {
      const src = path.join(dir, dll);
      const bak = `${src}.BAK`;
      if (!existsSync(src) || existsSync(bak)) continue;
      await fs.rename(src, bak);
      log(`Backed up: ${path.join(dir, dll)}`);
    }
  }
}

async function copyOutput(
  outputDir: string,
  dllDirs: string[],
  copyDlls: boolean,
  arch: 'auto' | 'x32' | 'x64',
  log: (line: string) => void
): Promise<void> {
  if (!existsSync(outputDir)) {
    log(`WARNING: _OUTPUT folder not found: ${outputDir}. generate_emu_config may have failed.`);
    return;
  }
  if (copyDlls) {
    const dlls = arch === 'x64'
      ? ['steam_api64.dll', 'steamclient64.dll']
      : arch === 'x32'
      ? ['steam_api.dll', 'steamclient.dll']
      : ['steam_api.dll', 'steam_api64.dll', 'steamclient.dll', 'steamclient64.dll'];
    for (const dll of dlls) {
      const src = path.join(outputDir, dll);
      if (!existsSync(src)) continue;
      for (const dir of dllDirs) {
        await fs.copyFile(src, path.join(dir, dll));
        log(`Copied: ${dll} -> ${dir}`);
      }
    }
  }
  const settingsSrc = path.join(outputDir, 'steam_settings');
  if (!existsSync(settingsSrc)) {
    log('WARNING: steam_settings folder not found in output.');
    return;
  }
  for (const dir of dllDirs) {
    const settingsDest = path.join(dir, 'steam_settings');
    await copyDirectory(settingsSrc, settingsDest);
    log(`Copied: steam_settings/ -> ${dir}`);
  }
}

async function copyDirectory(src: string, dest: string): Promise<void> {
  await fs.mkdir(dest, { recursive: true });
  const entries = await fs.readdir(src, { withFileTypes: true });
  for (const entry of entries) {
    const from = path.join(src, entry.name);
    const to = path.join(dest, entry.name);
    if (entry.isDirectory()) await copyDirectory(from, to);
    else if (entry.isFile()) await fs.copyFile(from, to);
  }
}

async function* walkFiles(root: string): AsyncGenerator<string> {
  const entries = await fs.readdir(root, { withFileTypes: true });
  for (const entry of entries) {
    const full = path.join(root, entry.name);
    if (entry.isDirectory()) {
      yield* walkFiles(full);
    } else if (entry.isFile()) {
      yield full;
    }
  }
}

export function formatGoldbergError(err: unknown): string {
  return `Goldberg error: ${safeError(err)}`;
}
