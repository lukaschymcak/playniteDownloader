import { spawn } from 'node:child_process';
import fs from 'node:fs/promises';
import path from 'node:path';
import type { BrowserWindow } from 'electron';
import { depPath, findDotnet } from './paths';
import { safeError } from './logger';

export async function runSteamless(gameDir: string, channelId: string, window: BrowserWindow): Promise<void> {
  const dotnet = findDotnet();
  if (!dotnet) throw new Error('dotnet not found.');
  const steamlessDll = depPath('Steamless', 'Steamless.CLI.dll');
  const exes = await findExecutables(gameDir);
  for (const exe of exes) {
    window.webContents.send(channelId, { log: `Running Steamless on: ${path.basename(exe)}` });
    await runOne(dotnet, steamlessDll, exe, channelId, window);
  }
  window.webContents.send(channelId, { done: true, log: 'Steamless finished.' });
}

async function findExecutables(root: string): Promise<string[]> {
  const blocked = /(^unins|setup|redist|vc_|directx|dxsetup)/i;
  const result: string[] = [];
  async function walk(dir: string): Promise<void> {
    for (const entry of await fs.readdir(dir, { withFileTypes: true })) {
      const full = path.join(dir, entry.name);
      if (entry.isDirectory()) await walk(full);
      else if (entry.isFile() && entry.name.toLowerCase().endsWith('.exe') && !blocked.test(entry.name)) result.push(full);
    }
  }
  await walk(root);
  return result;
}

async function runOne(dotnet: string, dll: string, exe: string, channelId: string, window: BrowserWindow): Promise<void> {
  await new Promise<void>((resolve) => {
    const proc = spawn(dotnet, [dll, '-f', exe, '--quiet', '--realign', '--recalcchecksum'], {
      cwd: path.dirname(dll),
      windowsHide: true
    });
    proc.stdout.on('data', (chunk) => window.webContents.send(channelId, { log: chunk.toString().trim() }));
    proc.stderr.on('data', (chunk) => window.webContents.send(channelId, { log: chunk.toString().trim() }));
    proc.on('error', (err) => {
      window.webContents.send(channelId, { log: `ERROR: ${safeError(err)}` });
      resolve();
    });
    proc.on('close', (code) => {
      window.webContents.send(channelId, { log: code === 0 ? `Steamless OK: ${path.basename(exe)}` : `WARNING: Steamless error (${code}): ${path.basename(exe)}` });
      resolve();
    });
  });
}
