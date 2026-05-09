import path from 'node:path';
import type { AppSettings } from '../../shared/types';
import { userDataRoot } from './paths';
import { readJson, writeJson } from './jsonStore';

export const defaultSettings: AppSettings = {
  apiKey: '',
  downloadPath: '',
  maxDownloads: 20,
  steamUsername: '',
  steamWebApiKey: '',
  igdbClientId: '',
  igdbClientSecret: '',
  goldbergFilesPath: '',
  goldbergAccountName: '',
  goldbergSteamId: '',
  cloudServerUrl: '',
  cloudApiKey: ''
};

export function settingsPath(): string {
  return path.join(userDataRoot(), 'settings.json');
}

export async function loadSettings(): Promise<AppSettings> {
  const loaded = await readJson<Record<string, unknown>>(settingsPath(), {});
  const mapped = {
    apiKey: pickString(loaded, 'apiKey', 'ApiKey'),
    downloadPath: pickString(loaded, 'downloadPath', 'DownloadPath'),
    maxDownloads: pickNumber(loaded, 'maxDownloads', 'MaxDownloads', defaultSettings.maxDownloads),
    steamUsername: pickString(loaded, 'steamUsername', 'SteamUsername'),
    steamWebApiKey: pickString(loaded, 'steamWebApiKey', 'SteamWebApiKey'),
    igdbClientId: pickString(loaded, 'igdbClientId', 'IgdbClientId'),
    igdbClientSecret: pickString(loaded, 'igdbClientSecret', 'IgdbClientSecret'),
    goldbergFilesPath: pickString(loaded, 'goldbergFilesPath', 'GoldbergFilesPath'),
    goldbergAccountName: pickString(loaded, 'goldbergAccountName', 'GoldbergAccountName'),
    goldbergSteamId: pickString(loaded, 'goldbergSteamId', 'GoldbergSteamId'),
    cloudServerUrl: pickString(loaded, 'cloudServerUrl', 'CloudServerUrl'),
    cloudApiKey: pickString(loaded, 'cloudApiKey', 'CloudApiKey')
  };
  return {
    ...defaultSettings,
    ...mapped,
    maxDownloads: Math.max(1, Math.min(64, Number(mapped.maxDownloads || defaultSettings.maxDownloads)))
  };
}

export async function saveSettings(settings: AppSettings): Promise<AppSettings> {
  const merged = { ...defaultSettings, ...settings };
  merged.maxDownloads = Math.max(1, Math.min(64, Number(merged.maxDownloads || 20)));
  await writeJson(settingsPath(), {
    ApiKey: merged.apiKey,
    DownloadPath: merged.downloadPath,
    MaxDownloads: merged.maxDownloads,
    SteamUsername: merged.steamUsername,
    SteamWebApiKey: merged.steamWebApiKey,
    IgdbClientId: merged.igdbClientId,
    IgdbClientSecret: merged.igdbClientSecret,
    GoldbergFilesPath: merged.goldbergFilesPath,
    GoldbergAccountName: merged.goldbergAccountName,
    GoldbergSteamId: merged.goldbergSteamId,
    CloudServerUrl: merged.cloudServerUrl,
    CloudApiKey: merged.cloudApiKey
  });
  return merged;
}

function pickString(source: Record<string, unknown>, camel: string, pascal: string): string {
  return String(source[camel] ?? source[pascal] ?? '');
}

function pickNumber(source: Record<string, unknown>, camel: string, pascal: string, fallback: number): number {
  return Number(source[camel] ?? source[pascal] ?? fallback);
}
