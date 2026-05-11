import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import AdmZip from 'adm-zip';
import type { DepotInfo, GameData } from '../../shared/types';

const descBlacklist = [
  'soundtrack', 'ost', 'original soundtrack', 'artbook', 'graphic novel',
  'demo', 'server', 'dedicated server', 'tool', 'sdk', '3d print model'
];

export async function processZip(zipPath: string): Promise<GameData> {
  const zip = new AdmZip(zipPath);
  const entries = zip.getEntries();
  const luaEntry = findSingleLuaEntry(entries);
  if (!luaEntry) {
    throw new Error('No .lua file found in the manifest ZIP.');
  }

  const gameData: GameData = {
    appId: '',
    gameName: '',
    depots: {},
    dlcs: {},
    manifests: {},
    selectedDepots: [],
    manifestZipPath: zipPath
  };

  const tempDir = path.join(os.tmpdir(), 'ludownloader_manifests');
  await fs.mkdir(tempDir, { recursive: true });
  for (const entry of entries) {
    if (!entry.entryName.toLowerCase().endsWith('.manifest')) continue;
    const fileName = path.basename(entry.entryName);
    await fs.writeFile(path.join(tempDir, fileName), entry.getData());
    const match = path.basename(fileName, '.manifest').match(/^(\d+)_(\d+)$/);
    if (match) {
      gameData.manifests[match[1]] = match[2];
    }
  }

  parseLua(luaEntry.getData().toString('utf8'), gameData);
  if (!gameData.gameName) {
    gameData.gameName = `App_${gameData.appId}`;
  }
  gameData.selectedDepots = Object.keys(gameData.depots);
  return gameData;
}

export function manifestFilePath(depotId: string, manifestGid: string): string {
  return path.join(os.tmpdir(), 'ludownloader_manifests', `${depotId}_${manifestGid}.manifest`);
}

export async function extractSingleLua(zipPath: string): Promise<{ fileName: string; path: string; content: string }> {
  const zip = new AdmZip(zipPath);
  const luaEntry = findSingleLuaEntry(zip.getEntries(), true);
  if (!luaEntry) {
    throw new Error('No .lua file found in the manifest ZIP.');
  }

  const fileName = path.basename(luaEntry.entryName);
  const target = path.join(os.tmpdir(), 'ludownloader_lua', fileName);
  const content = luaEntry.getData().toString('utf8');
  await fs.mkdir(path.dirname(target), { recursive: true });
  await fs.writeFile(target, content, 'utf8');
  return { fileName, path: target, content };
}

export function getSingleLuaFileName(zipPath: string): string {
  const zip = new AdmZip(zipPath);
  const luaEntry = findSingleLuaEntry(zip.getEntries(), true);
  if (!luaEntry) {
    throw new Error('No .lua file found in the manifest ZIP.');
  }
  return path.basename(luaEntry.entryName);
}

function parseLua(lua: string, data: GameData): void {
  const addAppRegex = /addappid\((.*?)\)(.*)/gim;
  const matches = [...lua.matchAll(addAppRegex)];
  if (matches.length === 0) {
    throw new Error('LUA file has no addappid entries.');
  }

  const firstArgs = splitLuaArgs(matches[0][1]);
  data.appId = firstArgs[0]?.trim() || '';
  data.gameName = matches[0][2].match(/--\s*(.*)/)?.[1]?.trim() || '';

  for (let i = 1; i < matches.length; i++) {
    const args = splitLuaArgs(matches[i][1]);
    const id = args[0]?.trim();
    if (!id) continue;
    const desc = matches[i][2].match(/--\s*(.*)/)?.[1]?.trim() || `Depot ${id}`;
    const key = args[2]?.trim().replace(/^"|"$/g, '');
    if (key) {
      if (!isBlacklisted(desc)) {
        data.depots[id] = { key, description: desc, size: 0 };
      }
    } else {
      data.dlcs[id] = desc;
    }
  }

  const sizeRegex = /setManifestid\(\s*(\d+)\s*,\s*".*?"\s*,\s*(\d+)\s*\)/gim;
  for (const match of lua.matchAll(sizeRegex)) {
    const depotId = match[1];
    const size = Number(match[2] || 0);
    const depot: DepotInfo | undefined = data.depots[depotId];
    if (depot) depot.size = size;
  }
}

function splitLuaArgs(raw: string): string[] {
  const args: string[] = [];
  let current = '';
  let inQuotes = false;
  for (const ch of raw) {
    if (ch === '"') inQuotes = !inQuotes;
    if (ch === ',' && !inQuotes) {
      args.push(current.trim());
      current = '';
      continue;
    }
    current += ch;
  }
  if (current.trim()) args.push(current.trim());
  return args;
}

function isBlacklisted(desc: string): boolean {
  const lower = desc.toLowerCase();
  return descBlacklist.some((word) => new RegExp(`\\b${escapeRegex(word)}\\b`, 'i').test(lower));
}

function escapeRegex(value: string): string {
  return value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

type ZipEntry = { entryName: string; isDirectory: boolean; getData: () => Buffer };

function findSingleLuaEntry(entries: ZipEntry[], requireSingle = false): ZipEntry | undefined {
  const luaEntries = entries.filter((entry) => !entry.isDirectory && entry.entryName.toLowerCase().endsWith('.lua'));
  if (requireSingle && luaEntries.length > 1) {
    throw new Error('Multiple .lua files found in the manifest ZIP.');
  }
  return luaEntries[0];
}
