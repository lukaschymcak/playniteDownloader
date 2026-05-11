import { spawn } from 'node:child_process';
import fs from 'node:fs/promises';
import path from 'node:path';
import type { GameData, InstalledGame, ManifestCacheEntry, ManifestCheckResult, UpdateStatus } from '../../shared/types';
import { depPath, manifestCacheDir } from './paths';
import { listInstalled, saveInstalled } from './games';
import { safeError, warn } from './logger';

function isUsableBuildId(value?: string): boolean {
  const raw = String(value || '').trim();
  if (!/^\d+$/.test(raw)) return false;
  const n = Number.parseInt(raw, 10);
  return Number.isFinite(n) && n > 1;
}

function resolvePreferredBuildId(game: InstalledGame, rows: ManifestCheckResult[]): string | undefined {
  const selected = game.selectedDepots?.length ? game.selectedDepots : Object.keys(game.manifestGIDs || {});

  for (const depotId of selected) {
    const expectedManifest = game.manifestGIDs?.[depotId];
    const row = rows.find((r) => r.depotId === depotId && (!expectedManifest || r.manifestGid === expectedManifest));
    if (row && isUsableBuildId(row.depotBuildId)) {
      return String(row.depotBuildId).trim();
    }
  }

  for (const depotId of selected) {
    const row = rows.find((r) => r.depotId === depotId);
    if (row && isUsableBuildId(row.buildId)) {
      return String(row.buildId).trim();
    }
  }

  const fallback = rows.find((row) => isUsableBuildId(row.buildId))?.buildId;
  return fallback ? String(fallback).trim() : undefined;
}

export async function runManifestChecker(appIds: string[], timeoutMs = 30000): Promise<ManifestCheckResult[]> {
  const ids = appIds.map((id) => id.trim()).filter(Boolean);
  if (!ids.length) return [];
  for (const id of ids) {
    if (!/^\d+$/.test(id)) throw new Error(`Invalid AppID for manifest check: ${id}`);
  }

  const exe = depPath('ManifestChecker.exe');
  return new Promise((resolve, reject) => {
    const proc = spawn(exe, ids, { windowsHide: true });
    let stdout = '';
    let stderr = '';
    const timer = setTimeout(() => {
      proc.kill();
      reject(new Error('ManifestChecker.exe timed out after 30 seconds.'));
    }, timeoutMs);

    proc.stdout.on('data', (chunk) => { stdout += chunk.toString(); });
    proc.stderr.on('data', (chunk) => { stderr += chunk.toString(); });
    proc.on('error', (err) => {
      clearTimeout(timer);
      reject(err);
    });
    proc.on('close', (code) => {
      clearTimeout(timer);
      if (code !== 0) {
        try {
          const parsed = JSON.parse(stderr) as { error?: string };
          reject(new Error(parsed.error || stderr || `ManifestChecker failed (${code})`));
        } catch {
          reject(new Error(stderr || `ManifestChecker failed (${code})`));
        }
        return;
      }
      resolve(JSON.parse(stdout || '[]') as ManifestCheckResult[]);
    });
  });
}

export async function checkUpdates(appIds?: string[]): Promise<UpdateStatus[]> {
  const installed = await listInstalled();
  const candidates = installed.filter((g) => {
    if (appIds?.length && !appIds.includes(g.appId)) return false;
    return g.appId && g.manifestGIDs && Object.keys(g.manifestGIDs).length > 0;
  });
  if (!candidates.length) return [];

  try {
    const current = await runManifestChecker([...new Set(candidates.map((g) => g.appId))]);
    const byApp = new Map<string, ManifestCheckResult[]>();
    for (const row of current) {
      const list = byApp.get(row.appId) || [];
      list.push(row);
      byApp.set(row.appId, list);
    }

    const statuses: UpdateStatus[] = [];
    for (const game of candidates) {
      const rows = byApp.get(game.appId) || [];
      if (!rows.length) {
        statuses.push({
          appId: game.appId,
          status: 'cannot_determine',
          error: 'ManifestChecker returned no manifest rows for this app.'
        });
        continue;
      }

      let update = false;
      let comparedAnyDepot = false;
      for (const row of rows) {
        const saved = game.manifestGIDs[row.depotId];
        if (!saved) {
          continue;
        }
        comparedAnyDepot = true;
        if (saved !== row.manifestGid) {
          update = true;
          break;
        }
      }
      const firstBuild = resolvePreferredBuildId(game, rows);
      if (firstBuild && firstBuild !== game.steamBuildId) {
        await saveInstalled({ ...game, steamBuildId: firstBuild });
      }
      if (!comparedAnyDepot) {
        statuses.push({
          appId: game.appId,
          status: 'cannot_determine',
          buildId: firstBuild,
          error: 'No returned depot matched the saved manifest GIDs.'
        });
        continue;
      }
      statuses.push({ appId: game.appId, status: update ? 'update_available' : 'up_to_date', buildId: firstBuild });
    }
    return statuses;
  } catch (err) {
    await warn(`Update check failed: ${safeError(err)}`);
    return candidates.map((game) => ({ appId: game.appId, status: 'cannot_determine', error: safeError(err) }));
  }
}

export async function fetchManifestGids(appIds: string[]): Promise<UpdateStatus[]> {
  const requested = [...new Set(appIds.map((id) => id.trim()).filter(Boolean))];
  if (!requested.length) return [];

  const installed = await listInstalled();
  const byInstalledApp = new Map(installed.map((game) => [game.appId, game]));
  const candidates = requested
    .map((appId) => byInstalledApp.get(appId))
    .filter((game): game is InstalledGame => Boolean(game));
  if (!candidates.length) return [];

  try {
    const current = await runManifestChecker(requested);
    const byApp = new Map<string, ManifestCheckResult[]>();
    for (const row of current) {
      const list = byApp.get(row.appId) || [];
      list.push(row);
      byApp.set(row.appId, list);
    }

    const statuses: UpdateStatus[] = [];
    for (const game of candidates) {
      const rows = byApp.get(game.appId) || [];
      if (!rows.length) {
        statuses.push({
          appId: game.appId,
          status: 'cannot_determine',
          error: 'ManifestChecker returned no manifest rows for this app.'
        });
        continue;
      }

      const manifestGIDs = { ...game.manifestGIDs };
      for (const row of rows) {
        if (row.depotId && row.manifestGid) {
          manifestGIDs[row.depotId] = row.manifestGid;
        }
      }

      const firstBuild = resolvePreferredBuildId(game, rows);
      await saveInstalled({
        ...game,
        manifestGIDs,
        steamBuildId: firstBuild || game.steamBuildId
      });
      statuses.push({ appId: game.appId, status: 'up_to_date', buildId: firstBuild });
    }
    return statuses;
  } catch (err) {
    await warn(`Manifest GID fetch failed: ${safeError(err)}`);
    return candidates.map((game) => ({ appId: game.appId, status: 'cannot_determine', error: safeError(err) }));
  }
}

export async function listCachedManifests(): Promise<ManifestCacheEntry[]> {
  await fs.mkdir(manifestCacheDir(), { recursive: true });
  const files = await fs.readdir(manifestCacheDir());
  const rows: ManifestCacheEntry[] = [];
  for (const file of files.filter((f) => f.toLowerCase().endsWith('.zip'))) {
    const full = path.join(manifestCacheDir(), file);
    const stat = await fs.stat(full);
    rows.push({
      appId: file.match(/^(\d+)/)?.[1] || path.basename(file, '.zip'),
      file,
      path: full,
      size: stat.size,
      modifiedAt: stat.mtime.toISOString()
    });
  }
  return rows.sort((a, b) => b.modifiedAt.localeCompare(a.modifiedAt));
}

export async function getCachedManifestPath(appId: string): Promise<string> {
  const clean = appId.trim();
  if (!/^\d+$/.test(clean)) throw new Error(`Invalid AppID for manifest cache: ${appId}`);
  await fs.mkdir(manifestCacheDir(), { recursive: true });
  return path.join(manifestCacheDir(), `${clean}.zip`);
}

export async function ensureManifestGids(game: InstalledGame, zipGameData: GameData): Promise<Record<string, string>> {
  const existing = { ...zipGameData.manifests, ...game.manifestGIDs };
  if (Object.keys(existing).length > 0) {
    return existing;
  }

  const rows = await runManifestChecker([game.appId]);
  if (!rows.length) {
    throw new Error('ManifestChecker returned no manifest rows for this app.');
  }

  return Object.fromEntries(rows
    .filter((row) => row.depotId && row.manifestGid)
    .map((row) => [row.depotId, row.manifestGid]));
}

export async function deleteCachedManifest(filePath: string): Promise<void> {
  const root = path.resolve(manifestCacheDir());
  const full = path.resolve(path.isAbsolute(filePath) ? filePath : path.join(root, filePath));
  const relative = path.relative(root, full);
  if (!relative || relative.startsWith('..') || path.isAbsolute(relative)) {
    throw new Error('Refusing to delete a file outside manifest_cache.');
  }
  await fs.unlink(full);
}
