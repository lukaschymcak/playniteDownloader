import { sourceIdToEmulatorMask } from './discoveryService.ts';
import type { CacheService } from './cacheService.ts';
import { loadAchievementEnrichment } from './achievementEnrichment.ts';
import { buildAchievementGameContext } from './gameInstallContext.ts';
import { parseAchievementsBySource } from './parsers/parseBySource.ts';
import { mergeRawAchievements } from './rawAchievementMerge.ts';
import type { SteamSchemaAchievementDef } from './steamWebApiAchievement.ts';
import type {
  Achievement,
  AchievementGameContext,
  AppSettings,
  DiscoveryRecord,
  GameAchievements,
  InstalledGame,
  RawAchievement
} from '../../shared/types.ts';
import { EmulatorSource, SourceId, type EmulatorSourceMask } from '../../shared/types.ts';

function iconStorageName(hash: string): string {
  const h = hash.trim();
  if (!h) return '';
  if (/\.(jpe?g|png)$/i.test(h)) {
    return h.replace(/[^a-zA-Z0-9._-]/g, '_');
  }
  return `${h.replace(/[^a-zA-Z0-9._-]/g, '_')}.jpg`;
}

function discoverySourceMask(rows: DiscoveryRecord[]): EmulatorSourceMask {
  let m = EmulatorSource.None;
  for (const r of rows) {
    m |= sourceIdToEmulatorMask(r.source);
  }
  return m;
}

function sortApiNames(keys: Set<string>, defByName: Map<string, SteamSchemaAchievementDef>): string[] {
  return [...keys].sort((a, b) => {
    const da = defByName.get(a)?.displayName ?? a;
    const db = defByName.get(b)?.displayName ?? b;
    const c = da.localeCompare(db, undefined, { sensitivity: 'base' });
    if (c !== 0) return c;
    return a.localeCompare(b);
  });
}

function buildAchievementList(
  appId: string,
  merged: Record<string, RawAchievement>,
  enrichment: Awaited<ReturnType<typeof loadAchievementEnrichment>>,
  ctx: AchievementGameContext,
  cache: CacheService
): Achievement[] {
  const defByName = new Map<string, SteamSchemaAchievementDef>();
  for (const d of enrichment.achievements) {
    defByName.set(d.name, d);
  }

  const keys = new Set<string>();
  for (const k of Object.keys(merged)) {
    keys.add(k);
  }
  for (const d of enrichment.achievements) {
    keys.add(d.name);
  }

  const baseGameName = enrichment.gameName ?? ctx.gameName ?? appId;
  const ordered = sortApiNames(keys, defByName);
  const list: Achievement[] = [];

  for (const apiName of ordered) {
    const def = defByName.get(apiName);
    const raw = merged[apiName] ?? {
      achieved: false,
      unlockTime: 0,
      curProgress: 0,
      maxProgress: 0
    };

    const iconFile = def?.icon ? iconStorageName(def.icon) : '';
    const grayFile = def?.icongray ? iconStorageName(def.icongray) : '';
    const iconPath = iconFile && cache.iconExists(appId, iconFile) ? cache.getIconPath(appId, iconFile) : null;
    const iconGrayPath =
      grayFile && cache.iconExists(appId, grayFile) ? cache.getIconPath(appId, grayFile) : null;

    const iconUrl =
      !iconPath && def?.icon
        ? `https://cdn.akamai.steamstatic.com/steamcommunity/public/images/apps/${appId}/${iconStorageName(def.icon)}`
        : null;
    const iconGrayUrl =
      !iconGrayPath && def?.icongray
        ? `https://cdn.akamai.steamstatic.com/steamcommunity/public/images/apps/${appId}/${iconStorageName(def.icongray)}`
        : null;

    list.push({
      apiName,
      gameName: baseGameName,
      displayName: def?.displayName ?? apiName,
      description: def?.description ?? '',
      hidden: def?.hidden ?? false,
      iconPath,
      iconGrayPath,
      iconUrl,
      iconGrayUrl,
      achieved: raw.achieved,
      unlockTime: raw.unlockTime,
      curProgress: raw.curProgress,
      maxProgress: raw.maxProgress,
      globalPercentage: enrichment.percentages[apiName] ?? 0
    });
  }

  return list;
}

async function parseRowToRaw(row: DiscoveryRecord): Promise<{ source: SourceId; raw: Record<string, RawAchievement> }> {
  if (row.kind === 'file') {
    return { source: row.source, raw: parseAchievementsBySource(row.source, row.location) };
  }

  if (row.source === SourceId.GreenLuma && row.kind === 'registry') {
    // Dynamic import avoids pulling `winreg` into node:test runs that never hit this branch.
    const { parseGreenLumaAchievements, createGreenLumaRegistryAccess } = await import(
      './parsers/greenLumaParser.ts'
    );
    const { WinRegAdapter } = await import('./registryAdapter.ts');
    const reg = createGreenLumaRegistryAccess(new WinRegAdapter());
    const raw = await parseGreenLumaAchievements(row.location, reg);
    return { source: row.source, raw };
  }

  return { source: row.source, raw: {} };
}

export async function processAppId(
  appId: string,
  discoveryRows: DiscoveryRecord[],
  settings: Pick<AppSettings, 'steamWebApiKey' | 'achievementScanOfficialSteamLibraries'>,
  installedGames: InstalledGame[],
  cache: CacheService,
  fetchFn: typeof fetch = globalThis.fetch.bind(globalThis)
): Promise<GameAchievements> {
  const ctx = await buildAchievementGameContext(appId, installedGames, settings);
  const rows: Array<{ source: SourceId; raw: Record<string, RawAchievement> }> = [];
  for (const row of discoveryRows) {
    rows.push(await parseRowToRaw(row));
  }

  const { merged } = mergeRawAchievements(rows);
  const enrichment = await loadAchievementEnrichment(appId, settings.steamWebApiKey ?? '', cache, fetchFn);
  const list = buildAchievementList(appId, merged, enrichment, ctx, cache);

  const unlockedCount = list.filter((a) => a.achieved).length;
  const totalCount = list.length;
  const percentage = totalCount > 0 ? (unlockedCount / totalCount) * 100 : 0;

  const sourceMask = discoverySourceMask(discoveryRows);
  const storeHeaderImageUrl = `https://cdn.akamai.steamstatic.com/steam/apps/${appId}/header.jpg`;

  return {
    appId,
    gameName: enrichment.gameName ?? ctx.gameName ?? appId,
    storeHeaderImageUrl,
    installDir: ctx.installDir ?? null,
    hasUsableInstallDirectory: ctx.hasUsableInstallDirectory,
    source: sourceMask,
    list,
    unlockedCount,
    totalCount,
    percentage,
    hasPlatinum: false
  };
}
