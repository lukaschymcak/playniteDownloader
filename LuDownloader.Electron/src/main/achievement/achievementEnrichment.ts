import fs from 'node:fs/promises';
import path from 'node:path';
import {
  CacheService,
  iconStorageName,
  PERCENTAGES_KEY,
  PERCENTAGES_TTL_SECONDS,
  SCHEMA_KEY,
  SCHEMA_TTL_SECONDS
} from './cacheService.ts';
import {
  fetchSteamAchievementSchema,
  fetchSteamGlobalAchievementPercentages,
  type SteamSchemaFetchResult
} from './steamWebApiAchievement.ts';

export interface AchievementEnrichmentData {
  gameName: string | null;
  achievements: SteamSchemaFetchResult['achievements'];
  percentages: Record<string, number>;
}

async function writeBinaryAtomic(filePath: string, data: Buffer): Promise<void> {
  await fs.mkdir(path.dirname(filePath), { recursive: true });
  const tmp = `${filePath}.tmp`;
  await fs.writeFile(tmp, data);
  await fs.rename(tmp, filePath);
}

function steamCdnIconUrl(appId: string, fileName: string): string {
  return `https://cdn.akamai.steamstatic.com/steamcommunity/public/images/apps/${appId}/${fileName}`;
}

async function tryDownloadIcon(
  appId: string,
  iconHash: string,
  cache: CacheService,
  fetchFn: typeof fetch
): Promise<void> {
  const fileName = iconStorageName(iconHash);
  if (!fileName) return;
  if (cache.iconExists(appId, fileName)) return;

  const url = steamCdnIconUrl(appId, fileName);
  let res: Response;
  try {
    res = await fetchFn(url, { method: 'GET' });
  } catch {
    return;
  }
  if (!res.ok) return;

  let buf: ArrayBuffer;
  try {
    buf = await res.arrayBuffer();
  } catch {
    return;
  }

  const dest = cache.getIconPath(appId, fileName);
  try {
    await writeBinaryAtomic(dest, Buffer.from(buf));
  } catch {
    /* non-fatal, mirror cache JSON write tolerance */
  }
}

async function loadSchema(
  appId: string,
  apiKey: string,
  cache: CacheService,
  fetchFn: typeof fetch
): Promise<SteamSchemaFetchResult | null> {
  const hasKey = apiKey.trim().length > 0;
  if (hasKey) {
    const fresh = await cache.get<SteamSchemaFetchResult>(appId, SCHEMA_KEY, SCHEMA_TTL_SECONDS);
    if (fresh) {
      return fresh;
    }
    const live = await fetchSteamAchievementSchema(appId, apiKey, fetchFn);
    if (live) {
      await cache.set(appId, SCHEMA_KEY, live);
    }
    return live;
  }

  return await cache.get<SteamSchemaFetchResult>(appId, SCHEMA_KEY);
}

async function loadPercentages(
  appId: string,
  apiKey: string,
  cache: CacheService,
  fetchFn: typeof fetch
): Promise<Record<string, number>> {
  const hasKey = apiKey.trim().length > 0;
  if (hasKey) {
    const fresh = await cache.get<Record<string, number>>(appId, PERCENTAGES_KEY, PERCENTAGES_TTL_SECONDS);
    if (fresh) {
      return fresh;
    }
    const live = await fetchSteamGlobalAchievementPercentages(appId, apiKey, fetchFn);
    if (live !== null) {
      await cache.set(appId, PERCENTAGES_KEY, live);
      return live;
    }
    return {};
  }

  const stale = await cache.get<Record<string, number>>(appId, PERCENTAGES_KEY);
  return stale ?? {};
}

/**
 * Loads Steam schema + global percentages via CacheService; optionally downloads achievement icons when `apiKey` is set.
 */
export async function loadAchievementEnrichment(
  appId: string,
  apiKey: string,
  cache: CacheService,
  fetchFn: typeof fetch = globalThis.fetch.bind(globalThis)
): Promise<AchievementEnrichmentData> {
  const trimmed = apiKey.trim();
  const [schema, percentages] = await Promise.all([
    loadSchema(appId, trimmed, cache, fetchFn),
    loadPercentages(appId, trimmed, cache, fetchFn)
  ]);

  const achievements = schema?.achievements ?? [];
  const gameName =
    schema && typeof schema.gameName === 'string' && schema.gameName.trim().length > 0
      ? schema.gameName.trim()
      : null;

  if (trimmed.length > 0) {
    for (const def of achievements) {
      if (def.icon) {
        await tryDownloadIcon(appId, def.icon, cache, fetchFn);
      }
      if (def.icongray) {
        await tryDownloadIcon(appId, def.icongray, cache, fetchFn);
      }
    }
  }

  return {
    gameName,
    achievements,
    percentages
  };
}
