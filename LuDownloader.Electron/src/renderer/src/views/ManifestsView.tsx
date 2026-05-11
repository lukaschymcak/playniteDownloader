import { useEffect, useMemo, useState } from 'react';
import type { ManifestCacheEntry } from '../../../shared/types';
import { Icon } from '../icons';
import { Empty } from './LibraryView';

export function ManifestsView(): JSX.Element {
  const [rows, setRows] = useState<ManifestCacheEntry[]>([]);
  const [error, setError] = useState('');
  const totalSize = useMemo(() => rows.reduce((sum, row) => sum + Number(row.size || 0), 0), [rows]);

  const refresh = async (): Promise<void> => {
    setError('');
    try {
      setRows(await window.api.manifest.listCached());
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    }
  };

  const remove = async (path: string): Promise<void> => {
    setError('');
    try {
      await window.api.manifest.deleteCached(path);
      await refresh();
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    }
  };

  useEffect(() => { void refresh(); }, []);

  return (
    <div className="manifests-page">
      <div className="page-heading">
        <h1>Manifests</h1>
        <button className="btn" onClick={refresh}><Icon name="refresh" /> Refresh</button>
      </div>

      {error && <div className="update-banner"><strong>{error}</strong></div>}

      {rows.length === 0 ? (
        <Empty title="No saved manifests yet" body="Fetch a game from the downloader to cache a manifest ZIP." />
      ) : (
        <section className="manifest-table-panel">
          <div className="manifest-table">
            <div className="manifest-table-head">
              <span>App ID</span>
              <span>Game</span>
              <span>File</span>
              <span>Size</span>
              <span>Version</span>
              <span>Modified</span>
              <span>Actions</span>
            </div>
            {rows.map((row) => (
              <ManifestTableRow key={row.path} row={row} onDelete={remove} />
            ))}
          </div>
          <div className="manifest-table-foot">
            <span>{rows.length} manifest{rows.length === 1 ? '' : 's'}</span>
            <span>Total Size: {formatSize(totalSize)}</span>
          </div>
        </section>
      )}
    </div>
  );
}

function ManifestTableRow({ row, onDelete }: { row: ManifestCacheEntry; onDelete: (path: string) => Promise<void> }): JSX.Element {
  const display = parseManifestDisplay(row);
  return (
    <div className="manifest-table-row">
      <div className="manifest-app">
        <Icon name="zip" size={20} />
        <span>{row.appId}</span>
      </div>
      <span className="manifest-game">{display.game}</span>
      <span className="manifest-file" title={row.file}>{row.file}</span>
      <span>{formatSize(row.size)}</span>
      <span>{display.version}</span>
      <span>{formatDate(row.modifiedAt)}</span>
      <div className="manifest-row-actions">
        <button className="btn sm danger" onClick={() => void onDelete(row.path)}>Delete</button>
        <button className="btn sm" onClick={() => void window.api.system.showItemInFolder(row.path)}>Open</button>
      </div>
    </div>
  );
}

function parseManifestDisplay(row: ManifestCacheEntry): { game: string; version: string } {
  const base = row.file.replace(/\.zip$/i, '');
  const withoutApp = base.replace(new RegExp(`^${row.appId}[_-]?`, 'i'), '');
  const version = withoutApp.match(/(?:^|[_-])v?([0-9][\w.-]*)$/i)?.[1] || '-';
  return {
    game: row.appId ? `App ${row.appId}` : '-',
    version
  };
}

function formatDate(value: string): string {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return '-';
  return date.toLocaleString([], { year: 'numeric', month: 'numeric', day: 'numeric', hour: 'numeric', minute: '2-digit' });
}

function formatSize(bytes: number): string {
  if (!bytes) return '0 B';
  const units = ['B', 'KB', 'MB', 'GB', 'TB'];
  let value = bytes;
  let unit = 0;
  while (value >= 1024 && unit < units.length - 1) { value /= 1024; unit++; }
  return `${value.toFixed(unit < 2 ? 0 : 2)} ${units[unit]}`;
}
