import path from 'node:path';
import { SourceId, type AppSettings } from '../../shared/types';
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
  cloudApiKey: '',
  achievementEnabledSources: [
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
    SourceId.GreenLuma,
    SourceId.Reloaded
  ],
  achievementSourceRoots: {},
  hoodlumSavePath: ''
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
    cloudApiKey: pickString(loaded, 'cloudApiKey', 'CloudApiKey'),
    achievementEnabledSources: pickSourceList(
      loaded,
      'achievementEnabledSources',
      'AchievementEnabledSources',
      defaultSettings.achievementEnabledSources
    ),
    achievementSourceRoots: pickSourceRoots(loaded, 'achievementSourceRoots', 'AchievementSourceRoots'),
    hoodlumSavePath: pickString(loaded, 'hoodlumSavePath', 'HoodlumSavePath')
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
    CloudApiKey: merged.cloudApiKey,
    AchievementEnabledSources: merged.achievementEnabledSources,
    AchievementSourceRoots: merged.achievementSourceRoots,
    HoodlumSavePath: merged.hoodlumSavePath
  });
  return merged;
}

function pickString(source: Record<string, unknown>, camel: string, pascal: string): string {
  return String(source[camel] ?? source[pascal] ?? '');
}

function pickNumber(source: Record<string, unknown>, camel: string, pascal: string, fallback: number): number {
  return Number(source[camel] ?? source[pascal] ?? fallback);
}

function pickSourceList(
  source: Record<string, unknown>,
  camel: string,
  pascal: string,
  fallback: SourceId[]
): SourceId[] {
  const raw = source[camel] ?? source[pascal];
  if (!Array.isArray(raw)) return fallback;
  const allowed = new Set(Object.values(SourceId));
  const values = raw
    .map((item) => String(item))
    .filter((item): item is SourceId => allowed.has(item as SourceId));
  return values.length > 0 ? values : fallback;
}

function pickSourceRoots(
  source: Record<string, unknown>,
  camel: string,
  pascal: string
): Partial<Record<SourceId, string[]>> {
  const raw = source[camel] ?? source[pascal];
  if (!raw || typeof raw !== 'object' || Array.isArray(raw)) return {};
  const allowed = new Set(Object.values(SourceId));
  const result: Partial<Record<SourceId, string[]>> = {};
  for (const [key, value] of Object.entries(raw as Record<string, unknown>)) {
    if (!allowed.has(key as SourceId) || !Array.isArray(value)) continue;
    const paths = value.map((item) => String(item)).filter((item) => item.trim().length > 0);
    if (paths.length > 0) {
      result[key as SourceId] = paths;
    }
  }
  return result;
}
