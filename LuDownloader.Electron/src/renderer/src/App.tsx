import { useEffect, useMemo, useState } from 'react';
import type { DownloadSession, GameAchievements, LibraryRow, MorrenusUserStats, UserProfile } from '../../shared/types';
import { Icon } from './icons';
import { LibraryView } from './views/LibraryView';
import { SearchView } from './views/SearchView';
import { ManifestsView } from './views/ManifestsView';
import { DownloadView } from './views/DownloadView';
import { SettingsModal } from './views/SettingsModal';
import { AchievementsView } from './views/AchievementsView';
import { useAchievements } from './hooks/useAchievements';
import { computePlayerStats } from './lib/achievementStats';

type View = 'library' | 'search' | 'manifests' | 'download' | 'settings' | 'achievements';
type DownloadTarget =
  | { mode: 'new'; seed: { appId: string; name: string; headerImageUrl?: string } | null }
  | { mode: 'session'; channelId: string };

const DEFAULT_PROFILE: UserProfile = { name: 'Player', avatarPath: null, featuredTrophiesByGame: {} };

export function App(): JSX.Element {
  const [view, setView] = useState<View>('library');
  const [rows, setRows] = useState<LibraryRow[]>([]);
  const [stats, setStats] = useState<MorrenusUserStats>({ dailyUsage: 0, dailyLimit: 0 });
  const [downloadTarget, setDownloadTarget] = useState<DownloadTarget>({ mode: 'new', seed: null });
  const [morrenusOnline, setMorrenusOnline] = useState<boolean | null>(null);
  const [sessions, setSessions] = useState<Record<string, DownloadSession>>({});
  const [profile, setProfile] = useState<UserProfile>(DEFAULT_PROFILE);
  const { games: achGames, loading: achLoading, refresh: refreshAch } = useAchievements();
  const achMap = useMemo(() => new Map(achGames.map((g) => [g.appId, g])), [achGames]);
  const playerStats = useMemo(() => computePlayerStats(achGames), [achGames]);

  const refreshRows = async (): Promise<void> => setRows(await window.api.library.rows() as LibraryRow[]);

  useEffect(() => {
    void refreshRows();
    void window.api.morrenus.getUserStats().then((s) => setStats(s as MorrenusUserStats));
    void window.api.morrenus.health().then((ok) => setMorrenusOnline(ok as boolean));
    void window.api.updates.check().then(refreshRows);
    void window.api.profile.load().then((p) => setProfile(p as UserProfile));

    const offLibrary = window.api.on('library:changed', (data) => setRows(data as LibraryRow[]));
    const offUpdates = window.api.on('updates:changed', () => void refreshRows());
    const offDownloads = window.api.on('downloads:progress', (data) => {
      const evt = data as Partial<DownloadSession> & {
        channelId?: string;
        appId?: string;
        gameName?: string;
        headerImageUrl?: string;
        pct?: number;
        totalBytes?: number;
        status?: string;
        done?: boolean;
        error?: string;
        canceled?: boolean;
        terminalReason?: 'completed' | 'failed' | 'canceled';
        log?: string;
        startedAt?: number;
        speedBps?: number;
        etaSec?: number;
        diskBps?: number | null;
        healthState?: 'stable' | 'retrying' | 'warning' | 'degraded';
        retryCountRecent?: number;
        lastHealthMessage?: string;
      };
      if (!evt.channelId) return;
      const id = evt.channelId;
      setSessions((current) => {
        const now = Date.now();
        const prev = current[id] || {
          channelId: id,
          appId: evt.appId || '',
          gameName: evt.gameName || evt.appId || 'Download',
          headerImageUrl: evt.headerImageUrl,
          targetPct: 0,
          displayPct: 0,
          status: 'Starting...',
          indeterminate: Boolean(evt.indeterminate),
          speedBps: null,
          etaSec: null,
          totalBytes: Number(evt.totalBytes || 0),
          startedAt: evt.startedAt || now,
          updatedAt: now,
          done: false,
          phase: 'setup',
          logs: [],
          samples: []
        } satisfies DownloadSession;

        const targetPct = evt.pct ?? prev.targetPct;
        const nextSamples = evt.pct !== undefined
          ? [...prev.samples, { t: now, pct: evt.pct }].slice(-60)
          : prev.samples;
        const telemetry = {
          speedBps: evt.speedBps !== undefined ? evt.speedBps : prev.speedBps,
          etaSec: evt.etaSec !== undefined ? evt.etaSec : prev.etaSec
        };
        const nextPhase: DownloadSession['phase'] = evt.canceled || evt.terminalReason === 'canceled'
          ? 'canceled'
          : evt.error || evt.terminalReason === 'failed'
          ? 'failed'
          : evt.done
            ? 'complete'
            : targetPct > 0
              ? 'downloading'
              : prev.phase;
        const nextLogs = evt.log ? [...prev.logs, evt.log].slice(-600) : prev.logs;

        const next: DownloadSession = {
          ...prev,
          appId: evt.appId || prev.appId,
          gameName: evt.gameName || prev.gameName,
          headerImageUrl: evt.headerImageUrl || prev.headerImageUrl,
          targetPct,
          displayPct: targetPct,
          status: evt.status || prev.status,
          indeterminate: evt.indeterminate ?? prev.indeterminate,
          totalBytes: Number(evt.totalBytes ?? prev.totalBytes),
          speedBps: telemetry.speedBps,
          diskBps: evt.diskBps !== undefined ? evt.diskBps : prev.diskBps,
          etaSec: telemetry.etaSec,
          healthState: evt.healthState || prev.healthState,
          retryCountRecent: evt.retryCountRecent !== undefined ? evt.retryCountRecent : prev.retryCountRecent,
          lastHealthMessage: evt.lastHealthMessage || prev.lastHealthMessage,
          updatedAt: now,
          done: Boolean(evt.done || prev.done),
          error: evt.error || (evt.canceled ? undefined : prev.error),
          phase: nextPhase,
          logs: nextLogs,
          samples: nextSamples
        };

        return { ...current, [id]: next };
      });
      if (evt.done) {
        const ttl = evt.canceled || evt.terminalReason === 'canceled' ? 3_000 : 15_000;
        window.setTimeout(() => {
          setSessions((current) => {
            const session = current[id];
            if (!session?.done) return current;
            const next = { ...current };
            delete next[id];
            return next;
          });
        }, ttl);
      }
    });

    return () => {
      offLibrary();
      offUpdates();
      offDownloads();
    };
  }, []);

  const saveProfile = async (p: UserProfile): Promise<void> => {
    const saved = await window.api.profile.save(p);
    setProfile(saved as UserProfile);
  };

  const title = useMemo(() => {
    if (view === 'library') return 'Library';
    if (view === 'search') return 'Search';
    if (view === 'manifests') return 'Manifests';
    if (view === 'settings') return 'Settings';
    if (view === 'achievements') return 'Achievements';
    return 'Download';
  }, [view]);

  const summary = useMemo(() => {
    if (view === 'library') return `${rows.filter((r) => r.state === 'installed').length} installed - ${rows.filter((r) => r.state === 'saved').length} saved`;
    if (view === 'search') return 'Find, save, and download Steam titles';
    if (view === 'manifests') return 'Cached manifest ZIPs';
    if (view === 'settings') return 'Credentials, paths, and tools';
    if (view === 'achievements') return `${achGames.length} games tracked · Lv. ${playerStats.level}`;
    return 'Resolve manifests and install depots';
  }, [rows, view, achGames, playerStats]);

  const openNewDownload = (game: { appId: string; name: string; headerImageUrl?: string }): void => {
    setDownloadTarget({ mode: 'new', seed: game });
    setView('download');
  };

  const openSessionDownload = (channelId: string): void => {
    setDownloadTarget({ mode: 'session', channelId });
    setView('download');
  };

  const activeDownloads = useMemo(
    () => Object.values(sessions).sort((a, b) => b.updatedAt - a.updatedAt),
    [sessions]
  );

  const selectedSession = downloadTarget.mode === 'session' ? sessions[downloadTarget.channelId] : undefined;

  return (
    <div className="app-window">
      <header className="titlebar">
        <div className="brand"><span className="brand-mark" /> LuDownloader</div>
        <div className="titlebar-title">{title}</div>
        <div className="window-controls">
          <button className="window-btn" title="Minimize" onClick={() => window.api.window.minimize()}><Icon name="minus" /></button>
          <button className="window-btn" title="Maximize" onClick={() => window.api.window.maximize()}><Icon name="square" /></button>
          <button className="window-btn close" title="Close" onClick={() => window.api.window.close()}><Icon name="close" /></button>
        </div>
      </header>

      <div className="shell">
        <aside className="sidebar">
          <div className="side-label">WORKSPACE</div>
          <NavButton view="library" active={view === 'library'} label="Library" count={rows.length} onClick={setView} icon="library" />
          <NavButton view="search" active={view === 'search'} label="Search" onClick={setView} icon="search" />
          <NavButton view="manifests" active={view === 'manifests'} label="Manifests" onClick={setView} icon="layers" />
          <NavButton view="achievements" active={view === 'achievements'} label="Achievements" onClick={setView} icon="trophy" />
          <div className="side-label system-label">SYSTEM</div>
          <NavButton view="settings" active={view === 'settings'} label="Settings" onClick={setView} icon="settings" />

          <div className="downloads-dock">
            <div className="downloads-title">Current Downloads</div>
            {activeDownloads.length === 0 && <div className="downloads-empty">No active downloads</div>}
            {activeDownloads.map((item) => (
              (() => {
                const stabilized = stabilizeSpeedAndEta(item.speedBps, item.etaSec, item.startedAt, item.displayPct, item.totalBytes, item.phase);
                return (
              <button
                key={item.channelId}
                className="download-chip"
                onClick={() => openSessionDownload(item.channelId)}
                title={item.status || 'Open download'}
              >
                <div className="download-chip-name">{item.gameName}</div>
                <div className="download-chip-meta">
                  {Math.round(item.displayPct)}% - {formatEta(stabilized.etaSec)} - {formatSpeed(stabilized.speedBps)}
                </div>
                {item.healthState && item.healthState !== 'stable' && (
                  <div className="download-chip-status" title={item.lastHealthMessage || item.healthState}>
                    {formatHealth(item.healthState, item.retryCountRecent)}
                  </div>
                )}
                {item.status && item.phase !== 'downloading' && <div className="download-chip-status">{item.status}</div>}
              </button>
                );
              })()
            ))}
          </div>

          <div className="sidebar-spacer" />

          <div className="user-card">
            <div className="avatar">{(stats.username || 'L').slice(0, 1).toUpperCase()}</div>
            <div>
              <div className="user-name">{stats.username || 'LuDownloader'}</div>
              <div className="subtle">Morrenus - {stats.dailyUsage}/{stats.dailyLimit || 0} today</div>
            </div>
          </div>
        </aside>

        <main className="main">
          <div className="page-meta">
            <div className="page-summary">{summary}</div>
            <div className="status-pill">
              <span className="status-dot" style={morrenusOnline === false ? { background: 'var(--danger)', boxShadow: 'none' } : undefined} />
              Morrenus {morrenusOnline === false ? 'offline' : 'online'}
            </div>
            <div className="quota">Daily: <strong>{stats.dailyUsage}/{stats.dailyLimit || 0}</strong></div>
          </div>

          <section className="content">
            {view === 'library' && <LibraryView rows={rows} refreshRows={refreshRows} openDownload={openNewDownload} achievementsMap={achMap} />}
            {view === 'search' && <SearchView openDownload={openNewDownload} refreshRows={refreshRows} />}
            {view === 'manifests' && <ManifestsView />}
            {view === 'download' && (
              <DownloadView
                target={downloadTarget}
                session={selectedSession}
                onBack={() => setView('library')}
                refreshRows={refreshRows}
                openSettings={() => setView('settings')}
              />
            )}
            {view === 'settings' && <SettingsModal onClose={() => setView('library')} embedded />}
            {view === 'achievements' && (
              <AchievementsView
                games={achGames}
                profile={profile}
                stats={playerStats}
                loading={achLoading}
                onRefresh={refreshAch}
                onSaveProfile={saveProfile}
              />
            )}
          </section>
        </main>
      </div>
    </div>
  );
}

const SPEED_WARMUP_MS = 3_000;
const SPEED_MIN_BYTES = 8 * 1024 * 1024;
const SPEED_DISPLAY_MAX_BPS = 300 * 1024 * 1024;

function stabilizeSpeedAndEta(
  speedBps: number | null,
  etaSec: number | null,
  startedAt: number,
  pct: number,
  totalBytes: number,
  phase: DownloadSession['phase']
): { speedBps: number | null; etaSec: number | null } {
  if (phase !== 'downloading') {
    return { speedBps, etaSec };
  }
  const elapsed = Math.max(0, Date.now() - (startedAt || 0));
  const estimatedBytes = totalBytes > 0 && pct > 0 ? totalBytes * (pct / 100) : 0;
  if (elapsed < SPEED_WARMUP_MS || estimatedBytes < SPEED_MIN_BYTES) {
    return { speedBps: null, etaSec: null };
  }
  if (!speedBps || !Number.isFinite(speedBps) || speedBps <= 0) {
    return { speedBps: null, etaSec: null };
  }
  return { speedBps: Math.min(speedBps, SPEED_DISPLAY_MAX_BPS), etaSec };
}

function formatHealth(state?: string, retries?: number): string {
  if (!state) return '';
  if (state === 'retrying') return `Retrying${retries ? ` (${retries})` : ''}`;
  if (state === 'degraded') return `Timeouts${retries ? ` (${retries})` : ''}`;
  if (state === 'warning') return 'Warning';
  return 'Stable';
}

function formatSpeed(bytesPerSecond: number | null): string {
  if (!bytesPerSecond || !Number.isFinite(bytesPerSecond) || bytesPerSecond <= 0) return '--';
  if (bytesPerSecond >= 1024 * 1024) return `${(bytesPerSecond / (1024 * 1024)).toFixed(1)} MB/s`;
  if (bytesPerSecond >= 1024) return `${(bytesPerSecond / 1024).toFixed(1)} KB/s`;
  return `${Math.round(bytesPerSecond)} B/s`;
}

function formatEta(seconds: number | null): string {
  if (!seconds || !Number.isFinite(seconds) || seconds < 1) return '--';
  if (seconds >= 3600) {
    const h = Math.floor(seconds / 3600);
    const m = Math.floor((seconds % 3600) / 60);
    return `${h}h ${m}m`;
  }
  if (seconds >= 60) {
    const m = Math.floor(seconds / 60);
    const s = Math.floor(seconds % 60);
    return `${m}m ${s}s`;
  }
  return `${Math.floor(seconds)}s`;
}

function NavButton(props: {
  view: View;
  active: boolean;
  label: string;
  icon: 'library' | 'search' | 'layers' | 'settings' | 'trophy';
  count?: number;
  onClick: (view: View) => void;
}): JSX.Element {
  return (
    <button className={`nav-btn ${props.active ? 'active' : ''}`} title={props.label} onClick={() => props.onClick(props.view)}>
      <Icon name={props.icon} />
      <span>{props.label}</span>
      {typeof props.count === 'number' && <span className="nav-count">{props.count}</span>}
    </button>
  );
}
