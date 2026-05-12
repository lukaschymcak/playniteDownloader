import { existsSync } from 'node:fs';
import fs from 'node:fs/promises';
import path from 'node:path';

/** Root JSON shape for every cache file (matches LuiAchieve `CacheEntry`). */
export interface CacheEntry<T> {
  cachedAt: number;
  data: T;
}

export const SCHEMA_KEY = 'schema';
export const PERCENTAGES_KEY = 'percentages';
export const STORE_DISPLAY_NAME_KEY = 'store_display_name';

export function iconStorageName(hash: string): string {
  const h = hash.trim();
  if (!h) return '';
  if (/\.(jpe?g|png)$/i.test(h)) return h.replace(/[^a-zA-Z0-9._-]/g, '_');
  return `${h.replace(/[^a-zA-Z0-9._-]/g, '_')}.jpg`;
}

/** Default schema freshness: 7 days (see `CacheService.SchemaTtl` in LuiAchieve). */
export const SCHEMA_TTL_SECONDS = 7 * 24 * 60 * 60;

/** Default global-% freshness: 24 hours (see `CacheService.PercentagesTtl`). */
export const PERCENTAGES_TTL_SECONDS = 24 * 60 * 60;

async function writeTextFileAtomic(filePath: string, content: string): Promise<void> {
  await fs.mkdir(path.dirname(filePath), { recursive: true });
  const tmp = `${filePath}.tmp`;
  await fs.writeFile(tmp, content, 'utf8');
  await fs.rename(tmp, filePath);
}

async function tryDeleteFile(filePath: string): Promise<void> {
  try {
    await fs.unlink(filePath);
  } catch {
    /* ignore */
  }
}

function nowUnixSeconds(): number {
  return Math.floor(Date.now() / 1000);
}

export class CacheService {
  private readonly cacheRoot: string;

  constructor(cacheRoot: string) {
    this.cacheRoot = cacheRoot;
  }

  get cacheRootPath(): string {
    return this.cacheRoot;
  }

  private cacheFilePath(appId: string, cacheKey: string): string {
    return path.join(this.cacheRoot, appId, `${cacheKey}.json`);
  }

  /**
   * When `ttlSeconds` is set, entries older than this many seconds are treated as absent (`null`).
   * When omitted, any successfully parsed entry is returned (mirror LuiAchieve optional TTL).
   */
  async get<T>(appId: string, cacheKey: string, ttlSeconds?: number): Promise<T | null> {
    if (!appId.trim() || !cacheKey.trim()) {
      return null;
    }

    const filePath = this.cacheFilePath(appId.trim(), cacheKey.trim());
    if (!existsSync(filePath)) {
      return null;
    }

    try {
      const raw = await fs.readFile(filePath, 'utf8');
      if (!raw.trim()) {
        return null;
      }
      const entry = JSON.parse(raw) as CacheEntry<T>;
      if (!entry || typeof entry.cachedAt !== 'number' || !('data' in entry)) {
        await tryDeleteFile(filePath);
        return null;
      }
      if (ttlSeconds !== undefined && ttlSeconds >= 0) {
        const stale = nowUnixSeconds() - entry.cachedAt > ttlSeconds;
        if (stale) {
          return null;
        }
      }
      return entry.data;
    } catch {
      await tryDeleteFile(filePath);
      return null;
    }
  }

  async set<T>(appId: string, cacheKey: string, value: T): Promise<void> {
    if (!appId.trim() || !cacheKey.trim()) {
      return;
    }

    const filePath = this.cacheFilePath(appId.trim(), cacheKey.trim());
    const entry: CacheEntry<T> = {
      cachedAt: nowUnixSeconds(),
      data: value
    };

    try {
      await writeTextFileAtomic(filePath, `${JSON.stringify(entry, null, 2)}\n`);
    } catch {
      /* cache write failure is non-fatal */
    }
  }

  async clearAllCache(): Promise<void> {
    if (existsSync(this.cacheRoot)) {
      await fs.rm(this.cacheRoot, { recursive: true, force: true });
    }
    await fs.mkdir(this.cacheRoot, { recursive: true });
  }

  getIconPath(appId: string, iconFileName: string): string {
    return path.join(this.cacheRoot, appId, 'icons', iconFileName);
  }

  iconExists(appId: string, iconFileName: string): boolean {
    return existsSync(this.getIconPath(appId, iconFileName));
  }

  getGridHeaderImagePath(appId: string): string | null {
    if (!appId.trim()) {
      return null;
    }
    return path.join(this.cacheRoot, appId.trim(), 'grid_header.jpg');
  }
}
