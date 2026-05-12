/**
 * Steam Web API transport for achievement schema and global unlock percentages.
 * No disk caching here — callers use CacheService.
 */

export interface SteamSchemaAchievementDef {
  name: string;
  displayName: string;
  description: string;
  hidden: boolean;
  icon: string;
  icongray: string;
}

export interface SteamSchemaFetchResult {
  gameName: string;
  achievements: SteamSchemaAchievementDef[];
}

function schemaUrl(appId: string, apiKey: string): string {
  const base = 'https://api.steampowered.com/ISteamUserStats/GetSchemaForGame/v2/';
  const u = new URL(base);
  u.searchParams.set('key', apiKey);
  u.searchParams.set('appid', appId);
  return u.toString();
}

function percentagesUrl(appId: string, apiKey: string): string {
  const base = 'https://api.steampowered.com/ISteamUserStats/GetGlobalAchievementPercentagesForApp/v2/';
  const u = new URL(base);
  u.searchParams.set('gameid', appId);
  u.searchParams.set('key', apiKey);
  return u.toString();
}

function asRecord(value: unknown): Record<string, unknown> | null {
  if (!value || typeof value !== 'object' || Array.isArray(value)) {
    return null;
  }
  return value as Record<string, unknown>;
}

function parseHidden(raw: unknown): boolean {
  if (typeof raw === 'boolean') return raw;
  if (typeof raw === 'number') return raw !== 0;
  return false;
}

export function parseSchemaResponseBody(body: unknown): SteamSchemaFetchResult | null {
  const root = asRecord(body);
  if (!root) return null;

  const game = asRecord(root.game);
  if (!game) return null;

  const gameName = typeof game.gameName === 'string' ? game.gameName : '';
  const stats = asRecord(game.availableGameStats);
  const rawList = stats && Array.isArray(stats.achievements) ? stats.achievements : [];

  const achievements: SteamSchemaAchievementDef[] = [];
  for (const item of rawList) {
    const row = asRecord(item);
    if (!row) continue;
    const name = typeof row.name === 'string' ? row.name.trim() : '';
    if (!name) continue;

    const displayName = typeof row.displayName === 'string' ? row.displayName : name;
    const description = typeof row.description === 'string' ? row.description : '';
    const icon = typeof row.icon === 'string' ? row.icon : '';
    const icongray = typeof row.icongray === 'string' ? row.icongray : '';

    achievements.push({
      name,
      displayName,
      description,
      hidden: parseHidden(row.hidden),
      icon,
      icongray
    });
  }

  return { gameName, achievements };
}

export function parsePercentagesResponseBody(body: unknown): Record<string, number> | null {
  const root = asRecord(body);
  if (!root) return null;

  const ap = asRecord(root.achievementpercentages);
  if (!ap || !Array.isArray(ap.achievements)) {
    return null;
  }

  const out: Record<string, number> = {};
  for (const item of ap.achievements) {
    const row = asRecord(item);
    if (!row) continue;
    const name = typeof row.name === 'string' ? row.name.trim() : '';
    if (!name) continue;
    const p = row.percent;
    const num = typeof p === 'number' ? p : typeof p === 'string' ? Number.parseFloat(p) : NaN;
    if (!Number.isFinite(num)) continue;
    out[name] = num;
  }
  return out;
}

export async function fetchSteamAchievementSchema(
  appId: string,
  apiKey: string,
  fetchFn: typeof fetch = globalThis.fetch.bind(globalThis)
): Promise<SteamSchemaFetchResult | null> {
  const key = apiKey.trim();
  const aid = appId.trim();
  if (!key || !aid) {
    return null;
  }

  let res: Response;
  try {
    res = await fetchFn(schemaUrl(aid, key), { method: 'GET' });
  } catch {
    return null;
  }
  if (!res.ok) {
    return null;
  }

  let body: unknown;
  try {
    body = await res.json();
  } catch {
    return null;
  }
  return parseSchemaResponseBody(body);
}

export async function fetchSteamGlobalAchievementPercentages(
  appId: string,
  apiKey: string,
  fetchFn: typeof fetch = globalThis.fetch.bind(globalThis)
): Promise<Record<string, number> | null> {
  const key = apiKey.trim();
  const aid = appId.trim();
  if (!key || !aid) {
    return null;
  }

  let res: Response;
  try {
    res = await fetchFn(percentagesUrl(aid, key), { method: 'GET' });
  } catch {
    return null;
  }
  if (!res.ok) {
    return null;
  }

  let body: unknown;
  try {
    body = await res.json();
  } catch {
    return null;
  }
  return parsePercentagesResponseBody(body);
}
