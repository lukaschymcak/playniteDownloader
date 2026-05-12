import { app } from 'electron';
import { existsSync } from 'node:fs';
import path from 'node:path';

export function userDataRoot(): string {
  return path.join(app.getPath('appData'), 'LuDownloader');
}

export function dataDir(): string {
  return path.join(userDataRoot(), 'data');
}

export function manifestCacheDir(): string {
  return path.join(userDataRoot(), 'manifest_cache');
}

export function achievementCacheDir(): string {
  return path.join(userDataRoot(), 'achievement_cache');
}

export function logPath(): string {
  return path.join(userDataRoot(), 'ludownloader.log');
}

export function resourcesRoot(): string {
  if (app.isPackaged) {
    return process.resourcesPath;
  }
  return path.resolve(app.getAppPath(), 'resources');
}

export function depPath(...parts: string[]): string {
  return path.join(resourcesRoot(), ...parts);
}

export function findDotnet(): string | null {
  const candidates = [
    'dotnet',
    path.join(process.env.LOCALAPPDATA || '', 'Microsoft', 'dotnet', 'dotnet.exe'),
    path.join(process.env.ProgramFiles || 'C:\\Program Files', 'dotnet', 'dotnet.exe')
  ];
  return candidates.find((candidate) => candidate === 'dotnet' || existsSync(candidate)) || null;
}
