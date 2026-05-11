import { useEffect, useMemo, useState } from 'react';
import type { AppSettings, DownloadSession, GameData } from '../../../shared/types';
import { Icon } from '../icons';

type DownloadPhase = 'setup' | 'downloading' | 'complete' | 'failed' | 'canceled';
type SpaceState = { loading: boolean; bytes: number | null };
type LiveTelemetry = {
  pct: number | null;
  speedBps: number | null;
  diskBps: number | null;
  etaSec: number | null;
  healthState?: 'stable' | 'retrying' | 'warning' | 'degraded';
  retryCountRecent?: number;
  lastHealthMessage?: string;
};

export function DownloadView(props: {
  target:
  | { mode: 'new'; seed: { appId: string; name: string; headerImageUrl?: string } | null }
  | { mode: 'session'; channelId: string };
  session?: DownloadSession;
  onBack: () => void;
  refreshRows: () => Promise<void>;
  openSettings?: () => void;
}): JSX.Element {
  const seed = props.target.mode === 'new' ? props.target.seed : null;
  const isSessionView = props.target.mode === 'session';
  const [zipPath, setZipPath] = useState('');
  const [gameData, setGameData] = useState<GameData | null>(null);
  const [libraryPath, setLibraryPath] = useState('');
  const [libraries, setLibraries] = useState<string[]>([]);
  const [selected, setSelected] = useState<Record<string, boolean>>({});
  const [logs, setLogs] = useState<string[]>([]);
  const [phase, setPhase] = useState<DownloadPhase>('setup');
  const [settings, setSettings] = useState<AppSettings | null>(null);
  const [steamUsername, setSteamUsername] = useState('');
  const [connections, setConnections] = useState(8);
  const [maxSpeed, setMaxSpeed] = useState('Unlimited');
  const [space, setSpace] = useState<SpaceState>({ loading: false, bytes: null });
  const [authOpen, setAuthOpen] = useState(false);
  const [authPassword, setAuthPassword] = useState('');
  const [authBusy, setAuthBusy] = useState(false);
  const [authError, setAuthError] = useState('');
  const [terminalStatus, setTerminalStatus] = useState<string>('');
  const [telemetry, setTelemetry] = useState<LiveTelemetry>({ pct: null, speedBps: null, diskBps: null, etaSec: null });
  const [downloadStartedAt, setDownloadStartedAt] = useState<number | null>(props.session?.startedAt || null);
  const [isPaused, setIsPaused] = useState(false);
  const [cancelPromptOpen, setCancelPromptOpen] = useState(false);
  const [goldbergPromptOpen, setGoldbergPromptOpen] = useState(false);
  const [goldbergMode, setGoldbergMode] = useState<'full' | 'achievements_only'>('full');
  const [goldbergCopyFiles, setGoldbergCopyFiles] = useState(true);
  const [goldbergArch, setGoldbergArch] = useState<'auto' | 'x64' | 'x32'>('auto');
  const [localChannelId] = useState(() => `download:${crypto.randomUUID()}`);
  const channelId = props.target.mode === 'session' ? props.target.channelId : localChannelId;
  const manifestChannelId = useMemo(() => `manifest:${crypto.randomUUID()}`, []);

  const currentGame = {
    appId: gameData?.appId || props.session?.appId || seed?.appId || '',
    name: gameData?.gameName || props.session?.gameName || seed?.name || 'Download',
    headerImageUrl: gameData?.headerImageUrl || props.session?.headerImageUrl || seed?.headerImageUrl
  };
  const selectedDepotIds = Object.keys(selected).filter((id) => selected[id]);
  const depotCount = gameData ? Object.keys(gameData.depots).length : 0;
  const selectedSize = gameData ? selectedDepotIds.reduce((sum, id) => sum + Number(gameData.depots[id]?.size || 0), 0) : 0;
  const installFolder = gameData ? installDir(gameData) : safeName(currentGame.name);
  const installPath = libraryPath && currentGame.appId
    ? `${libraryPath}\\steamapps\\common\\${installFolder}`
    : '';
  const setupLocked = phase === 'downloading';

  useEffect(() => {
    if (!isSessionView && !logs.length) setLogs(initialLogs(seed));
  }, [isSessionView, logs.length, seed]);

  useEffect(() => {
    void window.api.steam.getLibraries().then((libs) => {
      setLibraries(libs);
      setLibraryPath(libs[0] || '');
    });
    void window.api.settings.load().then((loaded) => {
      setSettings(loaded);
      setSteamUsername(loaded.steamUsername || '');
      setConnections(loaded.maxDownloads || 8);
    });
  }, []);

  useEffect(() => {
    if (!libraryPath) {
      setSpace({ loading: false, bytes: null });
      return;
    }
    let cancelled = false;
    setSpace({ loading: true, bytes: null });
    void window.api.system.getDiskFreeSpace(libraryPath).then((bytes) => {
      if (!cancelled) setSpace({ loading: false, bytes });
    }).catch(() => {
      if (!cancelled) setSpace({ loading: false, bytes: null });
    });
    return () => { cancelled = true; };
  }, [libraryPath]);

  useEffect(() => {
    if (isSessionView && props.session) {
      if (props.session.phase === 'complete') setPhase('complete');
      else if (props.session.phase === 'canceled') setPhase('canceled');
      else if (props.session.phase === 'failed') setPhase('failed');
      else if (props.session.phase === 'downloading') setPhase('downloading');
      else setPhase('setup');
      setLogs(props.session.logs || []);
      setTerminalStatus(props.session.status || '');
      setTelemetry({
        pct: Number.isFinite(props.session.displayPct) ? props.session.displayPct : null,
        speedBps: props.session.speedBps ?? null,
        diskBps: props.session.diskBps ?? null,
        etaSec: props.session.etaSec ?? null
      });
      setDownloadStartedAt(props.session.startedAt || null);
      setIsPaused((props.session.status || '').toLowerCase().includes('paused'));
    }
  }, [isSessionView, props.session]);

  useEffect(() => {
    if (isSessionView) return;
    const off = window.api.on(channelId, (event) => {
      const data = event as {
        log?: string;
        error?: string;
        done?: boolean;
        canceled?: boolean;
        status?: string;
        terminalReason?: 'completed' | 'failed' | 'canceled';
        pct?: number;
        speedBps?: number;
        diskBps?: number;
        etaSec?: number;
        healthState?: 'stable' | 'retrying' | 'warning' | 'degraded';
        retryCountRecent?: number;
        lastHealthMessage?: string;
        startedAt?: number;
      };
      if (data.log) appendLog(data.log);
      if (data.error) appendLog(`ERROR: ${data.error}`);
      if (data.startedAt) setDownloadStartedAt(data.startedAt);
      if (data.pct !== undefined || data.speedBps !== undefined || data.diskBps !== undefined || data.etaSec !== undefined || data.healthState !== undefined) {
        setTelemetry((prev) => ({
          pct: data.pct !== undefined ? data.pct : prev.pct,
          speedBps: data.speedBps !== undefined ? data.speedBps : prev.speedBps,
          diskBps: data.diskBps !== undefined ? data.diskBps : prev.diskBps,
          etaSec: data.etaSec !== undefined ? data.etaSec : prev.etaSec,
          healthState: data.healthState || prev.healthState,
          retryCountRecent: data.retryCountRecent !== undefined ? data.retryCountRecent : prev.retryCountRecent,
          lastHealthMessage: data.lastHealthMessage || prev.lastHealthMessage
        }));
      }
      if (data.status) setIsPaused(data.status.toLowerCase().includes('paused'));
      if (data.done) {
        const canceled = Boolean(data.canceled || data.terminalReason === 'canceled');
        const failed = Boolean(data.error || data.terminalReason === 'failed');
        setTerminalStatus(data.status || (canceled ? 'Canceled' : failed ? 'Failed' : 'Complete'));
        setPhase(canceled ? 'canceled' : failed ? 'failed' : 'complete');
        appendLog(canceled ? 'Download canceled.' : failed ? 'Download failed.' : 'Download complete.');
        void props.refreshRows();
      }
    });
    return () => { off(); };
  }, [channelId, isSessionView]);

  useEffect(() => {
    const off = window.api.on('downloads:progress', (event) => {
      const data = event as {
        channelId?: string;
        pct?: number;
        speedBps?: number | null;
        diskBps?: number | null;
        etaSec?: number | null;
        status?: string;
        healthState?: 'stable' | 'retrying' | 'warning' | 'degraded';
        retryCountRecent?: number;
        lastHealthMessage?: string;
      };
      if (data.channelId !== channelId) return;
      if (
        data.pct === undefined &&
        data.speedBps === undefined &&
        data.diskBps === undefined &&
        data.etaSec === undefined &&
        data.healthState === undefined &&
        data.status === undefined
      ) return;
      setTelemetry((prev) => ({
        pct: data.pct !== undefined ? data.pct : prev.pct,
        speedBps: data.speedBps !== undefined ? data.speedBps : prev.speedBps,
        diskBps: data.diskBps !== undefined ? data.diskBps : prev.diskBps,
        etaSec: data.etaSec !== undefined ? data.etaSec : prev.etaSec,
        healthState: data.healthState || prev.healthState,
        retryCountRecent: data.retryCountRecent !== undefined ? data.retryCountRecent : prev.retryCountRecent,
        lastHealthMessage: data.lastHealthMessage || prev.lastHealthMessage
      }));
      if (data.status) setIsPaused(data.status.toLowerCase().includes('paused'));
    });
    return () => { off(); };
  }, [channelId]);

  useEffect(() => {
    const off = window.api.on(manifestChannelId, (event) => {
      const data = event as { received?: number; total?: number; done?: boolean; pct?: number };
      if (data.pct !== undefined && !data.done) {
        appendLog(`Manifest fetch progress: ${data.pct}%`);
      }
      if (data.done) {
        appendLog('Manifest ZIP saved to cache.');
      }
    });
    return () => { off(); };
  }, [manifestChannelId]);

  const appendLog = (line: string): void => {
    setLogs((items) => [...items, line]);
  };

  const loadZip = async (path?: string): Promise<void> => {
    const picked = path || await window.api.system.pickFile([{ name: 'Manifest ZIP', extensions: ['zip'] }]);
    if (!picked) return;
    setZipPath(picked);
    const data = await window.api.zip.process(picked);
    setGameData({ ...data, headerImageUrl: seed?.headerImageUrl || data.headerImageUrl });
    setSelected(Object.fromEntries(Object.keys(data.depots).map((id) => [id, true])));
    setPhase('setup');
    appendLog(`Manifest loaded from ${picked}.`);
    appendLog(`${Object.keys(data.depots).length} depots found.`);
  };

  const fetchFromMorrenus = async (): Promise<void> => {
    if (!seed?.appId) return;
    const appId = seed.appId;
    const cached = (await window.api.manifest.listCached()).find((entry) => entry.appId === appId);
    if (cached) {
      try {
        appendLog('Loading manifest from cache...');
        await loadZip(cached.path);
        appendLog('Loaded cached manifest without calling Morrenus.');
        return;
      } catch (err) {
        appendLog(`Cached manifest was invalid and will be refreshed: ${err instanceof Error ? err.message : String(err)}`);
        await window.api.manifest.deleteCached(cached.path).catch(() => undefined);
      }
    }
    const target = await window.api.manifest.cachePath(appId);
    appendLog('Fetching manifest from Morrenus and saving it to cache...');
    await window.api.morrenus.downloadManifest(appId, target, manifestChannelId);
    await loadZip(target);
  };

  const saveRuntimeSettings = async (): Promise<AppSettings> => {
    const loaded = settings || await window.api.settings.load();
    const next = {
      ...loaded,
      steamUsername,
      maxDownloads: Math.max(1, Math.min(64, Number(connections) || 8))
    };
    const saved = await window.api.settings.save(next);
    setSettings(saved);
    return saved;
  };

  const start = async (): Promise<void> => {
    if (isSessionView) return;
    if (!gameData || !libraryPath || selectedDepotIds.length === 0) return;
    const saved = await saveRuntimeSettings();
    setTerminalStatus('');
    setTelemetry({ pct: 0, speedBps: null, diskBps: null, etaSec: null });
    setDownloadStartedAt(Date.now());
    setIsPaused(false);
    setPhase('downloading');
    appendLog(`Steam library: ${libraryPath}`);
    appendLog(`Steam account: ${saved.steamUsername || 'anonymous'}`);
    appendLog(`Max speed: ${maxSpeed}`);
    appendLog(`Connections: ${saved.maxDownloads}`);
    appendLog(`Starting DepotDownloader for ${selectedDepotIds.length} depot(s)...`);
    try {
      await window.api.download.start({
        gameData: { ...gameData, selectedDepots: selectedDepotIds, headerImageUrl: currentGame.headerImageUrl },
        selectedDepots: selectedDepotIds,
        libraryPath,
        steamUsername: saved.steamUsername,
        maxDownloads: saved.maxDownloads,
        channelId
      });
    } catch (err) {
      appendLog(`ERROR: ${err instanceof Error ? err.message : String(err)}`);
    }
  };

  const cancel = async (mode: 'keep' | 'delete' = 'keep'): Promise<void> => {
    appendLog(`Cancellation requested (${mode}). Waiting for DepotDownloader to terminate...`);
    setCancelPromptOpen(false);
    await window.api.download.cancel(channelId, mode);
  };

  const togglePause = async (): Promise<void> => {
    if (isPaused) {
      await window.api.download.resume(channelId);
      appendLog('Resuming download...');
    } else {
      await window.api.download.pause(channelId);
      appendLog('Pausing download...');
    }
  };

  const openSteamAuth = (): void => {
    if (!steamUsername.trim()) {
      appendLog('Enter your Steam username first.');
      return;
    }
    setAuthPassword('');
    setAuthError('');
    setAuthOpen(true);
  };

  const authenticateSteam = async (): Promise<void> => {
    if (!authPassword) {
      setAuthError('Enter your Steam password first.');
      return;
    }
    setAuthBusy(true);
    setAuthError('');
    try {
      await saveRuntimeSettings();
      appendLog('--- Steam Authentication ---');
      appendLog('A console window will open. Complete any Steam Guard prompt there, then close it.');
      await window.api.depot.authenticate(steamUsername.trim(), authPassword, channelId);
      setAuthOpen(false);
      setAuthPassword('');
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      setAuthError(message);
      appendLog(`ERROR: ${message}`);
    } finally {
      setAuthBusy(false);
    }
  };

  const runSteamless = async (): Promise<void> => {
    if (!installPath) return;
    appendLog('Running Steamless...');
    await window.api.steamless.run(installPath, channelId);
  };

  const applyGoldberg = async (): Promise<void> => {
    if (!settings?.goldbergFilesPath) {
      appendLog('Goldberg is not configured. Open Settings to set the Goldberg files path.');
      props.openSettings?.();
      return;
    }
    if (!installPath || !currentGame.appId) {
      appendLog('Cannot apply Goldberg: missing install path or app id.');
      return;
    }
    setGoldbergPromptOpen(true);
  };

  const confirmGoldberg = async (): Promise<void> => {
    appendLog('Starting Goldberg apply...');
    setGoldbergPromptOpen(false);
    try {
      await window.api.goldberg.run(
        installPath,
        currentGame.appId,
        channelId,
        goldbergMode,
        goldbergCopyFiles,
        goldbergArch
      );
    } catch (err) {
      appendLog(`ERROR: ${err instanceof Error ? err.message : String(err)}`);
    }
  };

  return (
    <div className="download-page">
      <section className="download-hero">
        <HeroCover appId={currentGame.appId} imageUrl={currentGame.headerImageUrl} name={currentGame.name} />
        <div className="download-title-block">
          <div className="download-title-row">
            <h1>{currentGame.name}</h1>
            <div className="download-statuses">
              <span className={`mini-status ${gameData ? 'ok' : ''}`}>{gameData ? 'Resolved' : 'Waiting'}</span>
              <span className={`mini-status ${zipPath ? 'ok' : ''}`}>{zipPath ? 'Manifest cached' : 'No manifest'}</span>
              {phase === 'downloading' && <span className="mini-status warn">Downloading</span>}
              {phase === 'complete' && <span className="mini-status ok">Complete</span>}
              {phase === 'canceled' && <span className="mini-status">Canceled</span>}
              {phase === 'failed' && <span className="mini-status warn">Failed</span>}
            </div>
          </div>
          <div className="download-meta">
            <span>App ID: {currentGame.appId || '-'}</span>
            <span>Version: {gameData?.buildId || 'unknown'}</span>
            <span>Size: {formatSize(selectedSize)}</span>
          </div>
        </div>
      </section>

      <Stepper phase={phase} hasManifest={Boolean(gameData || isSessionView)} />

      <div className="download-content">
        <section className="download-panel depot-panel">
          <div className="panel-head">
            <h2>Depots</h2>
            <span>{selectedDepotIds.length} / {depotCount} selected</span>
          </div>
          {!gameData ? (
            <div className="manifest-loader">
              <Icon name="zip" size={28} />
              <h3>No manifest loaded</h3>
              <p>Fetch the manifest from Morrenus or load a cached ZIP before selecting depots.</p>
              <div className="manifest-loader-actions">
                <button className="btn primary" disabled={!seed?.appId || setupLocked} onClick={fetchFromMorrenus}><Icon name="download" /> Fetch</button>
                <button className="btn" disabled={setupLocked || isSessionView} onClick={() => loadZip()}><Icon name="upload" /> Load ZIP</button>
              </div>
            </div>
          ) : (
            <>
              <div className="download-depot-list">
                {Object.entries(gameData.depots).map(([id, depot]) => (
                  <label className="download-depot-row" key={id}>
                    <input disabled={setupLocked} type="checkbox" checked={Boolean(selected[id])} onChange={(event) => setSelected((state) => ({ ...state, [id]: event.target.checked }))} />
                    <span className="depot-label">{id} - {depot.description}</span>
                    <span className="depot-weight">{formatSize(depot.size)}</span>
                  </label>
                ))}
              </div>
              <div className="download-total-row">
                <span>Selected size</span>
                <strong>{formatSize(selectedSize)}</strong>
              </div>
            </>
          )}
        </section>

        <section className="download-panel settings-panel">
          {phase === 'downloading' ? (
            <DownloadProgress
              installPath={installPath}
              connections={connections}
              selectedSize={selectedSize}
              telemetry={telemetry}
              phase={phase}
              startedAt={downloadStartedAt}
            />
          ) : phase === 'complete' ? (
            <CompletePanel installPath={installPath} selectedSize={selectedSize} />
          ) : phase === 'canceled' || phase === 'failed' ? (
            <TerminalPanel phase={phase} status={terminalStatus || (phase === 'canceled' ? 'Canceled' : 'Failed')} />
          ) : (
            <SetupPanel
              libraryPath={libraryPath}
              libraries={libraries}
              installFolder={installFolder}
              maxSpeed={maxSpeed}
              connections={connections}
              steamUsername={steamUsername}
              selectedSize={selectedSize}
              spaceAvailable={space.bytes}
              spaceLoading={space.loading}
              setupLocked={setupLocked}
              setLibraryPath={setLibraryPath}
              setMaxSpeed={setMaxSpeed}
              setConnections={setConnections}
              setSteamUsername={setSteamUsername}
              onSteamLogin={openSteamAuth}
              appendLog={appendLog}
            />
          )}
        </section>
      </div>

      <section className="download-panel log-panel">
        <div className="panel-head">
          <h2>Log</h2>
          <button className="btn ghost sm" onClick={() => setLogs([])}>Clear</button>
        </div>
        <div className="download-log">
          {logs.length ? logs.map((line, index) => <div key={`${line}-${index}`}>{line}</div>) : <div>Logs will appear here.</div>}
        </div>
      </section>

      <ActionBar
        phase={phase}
        canStart={!isSessionView && Boolean(gameData && libraryPath && selectedDepotIds.length)}
        installPath={installPath}
        isPaused={isPaused}
        onBack={props.onBack}
        onStart={start}
        onPauseResume={togglePause}
        onCancel={() => setCancelPromptOpen(true)}
        onRunSteamless={runSteamless}
        onApplyGoldberg={applyGoldberg}
      />

      {authOpen && (
        <div className="modal-backdrop">
          <div className="modal small-modal auth-modal">
            <div className="modal-head">
              <Icon name="steam" />
              <div>
                <div style={{ color: 'var(--fg-0)', fontWeight: 700 }}>Steam Password</div>
                <div className="subtle">Password for {steamUsername.trim()}</div>
              </div>
            </div>
            <div className="modal-body">
              <label className="form-group">
                <span className="form-label">Password</span>
                <input
                  className="form-input"
                  type="password"
                  autoFocus
                  value={authPassword}
                  onChange={(event) => setAuthPassword(event.target.value)}
                  onKeyDown={(event) => { if (event.key === 'Enter') void authenticateSteam(); }}
                />
              </label>
              {authError && <div className="card-error modal-error">{authError}</div>}
            </div>
            <div className="modal-foot">
              <button className="btn ghost" disabled={authBusy} onClick={() => setAuthOpen(false)}>Cancel</button>
              <button className="btn primary" disabled={authBusy || !authPassword} onClick={() => void authenticateSteam()}>
                {authBusy ? 'Waiting...' : 'Open Auth Window'}
              </button>
            </div>
          </div>
        </div>
      )}

      {cancelPromptOpen && (
        <div className="modal-backdrop">
          <div className="modal small-modal auth-modal">
            <div className="modal-head">
              <Icon name="close" />
              <div>
                <div style={{ color: 'var(--fg-0)', fontWeight: 700 }}>Cancel Download</div>
                <div className="subtle">Choose what to do with partial files.</div>
              </div>
            </div>
            <div className="modal-foot">
              <button className="btn ghost" onClick={() => setCancelPromptOpen(false)}>Keep Downloading</button>
              <button className="btn" onClick={() => void cancel('keep')}>Keep Files</button>
              <button className="btn danger" onClick={() => void cancel('delete')}>Delete Files</button>
            </div>
          </div>
        </div>
      )}

      {goldbergPromptOpen && (
        <div className="modal-backdrop">
          <div className="modal small-modal auth-modal">
            <div className="modal-head">
              <Icon name="box" />
              <div>
                <div style={{ color: 'var(--fg-0)', fontWeight: 700 }}>Goldberg Options</div>
                <div className="subtle">Choose install mode before applying.</div>
              </div>
            </div>
            <div className="modal-body">
              <label className="download-field">
                <span>Architecture</span>
                <select value={goldbergArch} onChange={(event) => setGoldbergArch(event.target.value as 'auto' | 'x64' | 'x32')}>
                  <option value="auto">Auto detect</option>
                  <option value="x64">64-bit (steam_api64.dll)</option>
                  <option value="x32">32-bit (steam_api.dll)</option>
                </select>
              </label>
              <label className="download-field">
                <span>Install Mode</span>
                <select value={goldbergMode} onChange={(event) => setGoldbergMode(event.target.value as 'full' | 'achievements_only')}>
                  <option value="full">Full install (DLLs + achievements)</option>
                  <option value="achievements_only">Achievements only (steam_settings)</option>
                </select>
              </label>
              <label className="download-field">
                <span>Copy Files To Game</span>
                <select value={goldbergCopyFiles ? 'yes' : 'no'} onChange={(event) => setGoldbergCopyFiles(event.target.value === 'yes')}>
                  <option value="yes">Yes</option>
                  <option value="no">No (generate output only)</option>
                </select>
              </label>
            </div>
            <div className="modal-foot">
              <button className="btn ghost" onClick={() => setGoldbergPromptOpen(false)}>Cancel</button>
              <button className="btn primary" onClick={() => void confirmGoldberg()}>Apply</button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

function SetupPanel(props: {
  libraryPath: string;
  libraries: string[];
  installFolder: string;
  maxSpeed: string;
  connections: number;
  steamUsername: string;
  selectedSize: number;
  spaceAvailable: number | null;
  spaceLoading: boolean;
  setupLocked: boolean;
  setLibraryPath: (value: string) => void;
  setMaxSpeed: (value: string) => void;
  setConnections: (value: number) => void;
  setSteamUsername: (value: string) => void;
  onSteamLogin: () => void;
  appendLog: (line: string) => void;
}): JSX.Element {
  const spaceKnown = props.spaceAvailable !== null;
  const hasEnoughSpace = spaceKnown && (props.spaceAvailable ?? 0) >= props.selectedSize;

  return (
    <>
      <div className="panel-head">
        <h2>Download Settings</h2>
      </div>

      <label className="download-field">
        <span>Steam Library</span>
        <div className="field-with-button">
          <select value={props.libraryPath} disabled={props.setupLocked} onChange={(event) => props.setLibraryPath(event.target.value)}>
            {props.libraries.length ? props.libraries.map((library) => <option key={library} value={library}>{library}</option>) : <option value="">No Steam libraries detected</option>}
          </select>
          <button className="btn icon" disabled={props.setupLocked} onClick={async () => {
            const picked = await window.api.system.pickFolder();
            if (picked) props.setLibraryPath(picked);
          }}><Icon name="folder" /></button>
        </div>
      </label>

      <label className="download-field">
        <span>Install Folder</span>
        <input value={`steamapps\\common\\${props.installFolder}`} readOnly />
      </label>

      <div className="settings-two-col">
        <label className="download-field">
          <span>Max Download Speed</span>
          <select disabled={props.setupLocked} value={props.maxSpeed} onChange={(event) => props.setMaxSpeed(event.target.value)}>
            <option>Unlimited</option>
            <option>50 MB/s</option>
            <option>25 MB/s</option>
            <option>10 MB/s</option>
          </select>
        </label>
        <label className="download-field">
          <span>Connections</span>
          <select disabled={props.setupLocked} value={props.connections} onChange={(event) => props.setConnections(Number(event.target.value))}>
            {[1, 2, 4, 8, 16, 32, 64].map((value) => <option key={value} value={value}>{value}</option>)}
          </select>
        </label>
      </div>

      <label className="download-field">
        <span>Steam Login</span>
        <div className="field-with-button">
          <input disabled={props.setupLocked} value={props.steamUsername} onChange={(event) => props.setSteamUsername(event.target.value)} placeholder="Steam username (optional)" />
          <button className="btn" disabled={props.setupLocked} onClick={props.onSteamLogin}>Login</button>
        </div>
      </label>

      <div className="space-row">
        <span>Space Required:</span>
        <strong>{formatSize(props.selectedSize)}</strong>
      </div>
      <div className="space-row available">
        <span>Space Available:</span>
        <strong className={!spaceKnown ? 'unknown' : hasEnoughSpace ? 'ok' : 'bad'}>
          {props.spaceLoading ? 'Checking...' : spaceKnown ? formatSize(props.spaceAvailable || 0) : 'Unknown'}
        </strong>
      </div>
    </>
  );
}

function DownloadProgress(props: {
  installPath: string;
  connections: number;
  selectedSize: number;
  telemetry: LiveTelemetry;
  phase: DownloadPhase;
  startedAt: number | null;
}): JSX.Element {
  const pct = props.telemetry.pct;
  const progressKnown = pct !== null && Number.isFinite(pct);
  const clamped = progressKnown ? Math.max(0, Math.min(100, pct || 0)) : 0;
  const stabilized = stabilizeSpeedAndEta(
    props.telemetry.speedBps,
    props.telemetry.etaSec,
    props.startedAt,
    clamped,
    props.selectedSize,
    props.phase
  );
  return (
    <>
      <div className="panel-head">
        <h2>Download Progress</h2>
      </div>
      <div className="progress-number">{progressKnown ? `${clamped.toFixed(2)}%` : '...'}</div>
      <div className={`download-progress-track ${progressKnown ? '' : 'indeterminate'}`}>
        <div style={progressKnown ? { width: `${clamped}%` } : undefined} />
      </div>
      <div className="progress-stats">
        <span>Size</span><strong>{formatSize(props.selectedSize)}</strong>
        <span>Connections</span><strong>{props.connections}</strong>
        <span>Speed</span><strong>{formatSpeed(stabilized.speedBps)}</strong>
        <span>Disk I/O</span><strong>{formatSpeed(props.telemetry.diskBps)}</strong>
        <span>ETA</span><strong>{formatEta(stabilized.etaSec)}</strong>
        <span>Health</span><strong title={props.telemetry.lastHealthMessage || ''}>{formatHealth(props.telemetry.healthState, props.telemetry.retryCountRecent)}</strong>
      </div>
      <label className="download-field">
        <span>Install Path</span>
        <input value={props.installPath} readOnly />
      </label>
    </>
  );
}

function CompletePanel(props: { installPath: string; selectedSize: number }): JSX.Element {
  return (
    <>
      <div className="panel-head">
        <h2>Complete</h2>
      </div>
      <div className="complete-mark"><Icon name="check" size={36} /></div>
      <div className="complete-copy">
        <strong>Download finished.</strong>
        <span>{formatSize(props.selectedSize)} installed into the selected Steam library.</span>
      </div>
      <label className="download-field">
        <span>Install Path</span>
        <input value={props.installPath} readOnly />
      </label>
    </>
  );
}

function ActionBar(props: {
  phase: DownloadPhase;
  canStart: boolean;
  installPath: string;
  isPaused: boolean;
  onBack: () => void;
  onStart: () => Promise<void>;
  onPauseResume: () => Promise<void>;
  onCancel: () => void;
  onRunSteamless: () => Promise<void>;
  onApplyGoldberg: () => Promise<void>;
}): JSX.Element {
  if (props.phase === 'downloading') {
    return (
      <div className="download-actionbar">
        <button className="btn" onClick={() => void props.onPauseResume()}>
          <Icon name="play" /> {props.isPaused ? 'Resume Download' : 'Pause Download'}
        </button>
        <button className="btn danger" onClick={props.onCancel}><Icon name="close" /> Cancel Download</button>
      </div>
    );
  }

  if (props.phase === 'complete') {
    return (
      <div className="download-actionbar complete-actions">
        <button className="btn" onClick={() => props.installPath && window.api.system.openPath(props.installPath)}><Icon name="folder" /> Open Folder</button>
        <button className="btn primary" onClick={() => void props.onRunSteamless()}><Icon name="play" /> Run Steamless</button>
        <button className="btn" onClick={() => void props.onApplyGoldberg()}><Icon name="box" /> Apply Goldberg</button>
        <button className="btn ghost" onClick={props.onBack}>Back to Library</button>
      </div>
    );
  }

  if (props.phase === 'canceled' || props.phase === 'failed') {
    return (
      <div className="download-actionbar">
        <button className="btn ghost" onClick={props.onBack}>Back to Library</button>
      </div>
    );
  }

  return (
    <div className="download-actionbar">
      <button className="btn primary" disabled={!props.canStart} onClick={() => void props.onStart()}><Icon name="play" /> Start Download</button>
    </div>
  );
}

function Stepper({ phase, hasManifest }: { phase: DownloadPhase; hasManifest: boolean }): JSX.Element {
  const current = phase === 'complete' || phase === 'canceled' || phase === 'failed'
    ? 3
    : phase === 'downloading'
      ? 2
      : hasManifest
        ? 1
        : 0;
  const items = ['Manifest', 'Depots', 'Download', 'Complete'];
  return (
    <div className="download-stepper">
      {items.map((item, index) => (
        <div key={item} className={`download-step ${index < current ? 'done' : ''} ${index === current ? 'current' : ''}`}>
          <span>{index < current ? <Icon name="check" size={12} /> : index + 1}</span>
          <strong>{item}</strong>
        </div>
      ))}
    </div>
  );
}

function TerminalPanel(props: { phase: 'canceled' | 'failed'; status: string }): JSX.Element {
  return (
    <>
      <div className="panel-head">
        <h2>{props.phase === 'canceled' ? 'Canceled' : 'Failed'}</h2>
      </div>
      <div className="complete-copy">
        <strong>{props.status}</strong>
        <span>
          {props.phase === 'canceled'
            ? 'DepotDownloader was terminated. Partial files may remain in the install folder.'
            : 'DepotDownloader exited with an error. Check the log for details.'}
        </span>
      </div>
    </>
  );
}

function HeroCover({ appId, imageUrl, name }: { appId: string; imageUrl?: string; name: string }): JSX.Element {
  const fallback = appId ? `https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/${appId}/header.jpg` : '';
  const [src, setSrc] = useState(imageUrl || fallback);
  const [failed, setFailed] = useState(false);
  if (!src || failed) return <div className="cover placeholder download-cover"><span>{name.slice(0, 3)}</span></div>;
  return <div className="cover download-cover"><img src={src} alt="" onError={() => src !== fallback ? setSrc(fallback) : setFailed(true)} /></div>;
}

function initialLogs(seed: { appId: string; name: string } | null): string[] {
  return [
    `Selected ${seed?.name || 'game'} (${seed?.appId || '-'}).`,
    'Load a manifest to choose depots.'
  ];
}

function installDir(gameData: GameData): string {
  if (gameData.installDir?.trim()) return gameData.installDir.trim();
  return safeName(gameData.gameName) || `App_${gameData.appId}`;
}

function safeName(value: string): string {
  return (value || '').replace(/[^\w\s-]/g, '').replace(/\s+/g, ' ').trim();
}

function formatSize(bytes: number): string {
  if (!bytes) return '0 B';
  const units = ['B', 'KB', 'MB', 'GB', 'TB'];
  let value = bytes;
  let unit = 0;
  while (value >= 1024 && unit < units.length - 1) { value /= 1024; unit++; }
  return `${value.toFixed(unit < 2 ? 0 : 1)} ${units[unit]}`;
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

function formatHealth(state?: string, retries?: number): string {
  if (!state) return 'Stable';
  if (state === 'retrying') return `Retrying${retries ? ` (${retries})` : ''}`;
  if (state === 'degraded') return `Timeouts${retries ? ` (${retries})` : ''}`;
  if (state === 'warning') return 'Warning';
  return 'Stable';
}

const SPEED_WARMUP_MS = 3_000;
const SPEED_MIN_BYTES = 8 * 1024 * 1024;
const SPEED_DISPLAY_MAX_BPS = 300 * 1024 * 1024;

function stabilizeSpeedAndEta(
  speedBps: number | null,
  etaSec: number | null,
  startedAt: number | null,
  pct: number,
  totalBytes: number,
  phase: DownloadPhase
): { speedBps: number | null; etaSec: number | null } {
  if (phase !== 'downloading') return { speedBps, etaSec };
  const since = startedAt || 0;
  const elapsed = since > 0 ? Math.max(0, Date.now() - since) : 0;
  const estimatedBytes = totalBytes > 0 && pct > 0 ? totalBytes * (pct / 100) : 0;
  if (elapsed < SPEED_WARMUP_MS || estimatedBytes < SPEED_MIN_BYTES) {
    return { speedBps: null, etaSec: null };
  }
  if (!speedBps || !Number.isFinite(speedBps) || speedBps <= 0) {
    return { speedBps: null, etaSec: null };
  }
  return { speedBps: Math.min(speedBps, SPEED_DISPLAY_MAX_BPS), etaSec };
}
