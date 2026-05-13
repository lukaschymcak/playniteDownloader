import { useEffect, useState } from 'react';
import { createPortal } from 'react-dom';
import type { GameAchievements, GameData, InstalledGame, LibraryRow } from '../../../shared/types';
import { Icon } from '../icons';

type LibraryFilter = 'all' | 'outdated';
type InlineAction =
  | null
  | 'menu'
  | 'remove'
  | 'update';

export function LibraryView(props: {
  rows: LibraryRow[];
  refreshRows: () => Promise<void>;
  openDownload: (game: { appId: string; name: string; headerImageUrl?: string }) => void;
  achievementsMap?: Map<string, GameAchievements>;
}): JSX.Element {
  const [filter, setFilter] = useState('');
  const [stateFilter, setStateFilter] = useState<LibraryFilter>('all');
  const [pageError, setPageError] = useState('');
  const [checkingUpdates, setCheckingUpdates] = useState(false);

  const search = filter.trim().toLowerCase();
  const filtered = props.rows.filter((game) => {
    const matchesState = stateFilter === 'all' || game.update === 'update_available';
    const matchesSearch = !search || `${game.name} ${game.appId}`.toLowerCase().includes(search);
    return matchesState && matchesSearch;
  });

  const refreshLibrary = async (): Promise<void> => props.refreshRows();

  const checkUpdates = async (): Promise<void> => {
    setPageError('');
    setCheckingUpdates(true);
    try {
      await props.refreshRows();
      await window.api.updates.check();
      await props.refreshRows();
    } catch (err) {
      setPageError(err instanceof Error ? err.message : String(err));
    } finally {
      setCheckingUpdates(false);
    }
  };

  return (
    <div className="library-page">
      <div className="library-heading">
        <h1>Library</h1>
        <div className="library-heading-actions">
          <button className="btn" onClick={refreshLibrary}><Icon name="refresh" /> Refresh</button>
          <button className="btn" disabled={checkingUpdates} onClick={checkUpdates}><Icon name="check" /> {checkingUpdates ? 'Checking...' : 'Check Updates'}</button>
        </div>
      </div>

      {pageError && <div className="update-banner"><strong>{pageError}</strong></div>}

      <div className="library-control-row">
        <div className="seg library-seg">
          <button className={stateFilter === 'all' ? 'on' : ''} onClick={() => setStateFilter('all')}>All</button>
          <button className={stateFilter === 'outdated' ? 'on' : ''} onClick={() => setStateFilter('outdated')}>Outdated</button>
        </div>
        <div className="filter-search library-search">
          <Icon name="search" size={18} />
          <input className="input" placeholder="Search games..." value={filter} onChange={(event) => setFilter(event.target.value)} />
        </div>
      </div>

      <div className="games-grid">
        {filtered.map((row) => (
          <LibraryCard key={row.appId} game={row} openDownload={props.openDownload} refreshRows={props.refreshRows} achData={props.achievementsMap?.get(row.appId)} />
        ))}
        {filtered.length === 0 && <Empty title="No games match this filter" body="Clear the filter, or use Search to add a new game to your library." />}
      </div>
    </div>
  );
}

function LibraryCard(props: {
  game: LibraryRow;
  openDownload: (game: { appId: string; name: string; headerImageUrl?: string }) => void;
  refreshRows: () => Promise<void>;
  achData?: GameAchievements;
}): JSX.Element {
  const [inlineAction, setInlineAction] = useState<InlineAction>(null);
  const [error, setError] = useState('');
  const [updating, setUpdating] = useState(false);
  const [rerunningManifest, setRerunningManifest] = useState(false);
  const [goldbergModalOpen, setGoldbergModalOpen] = useState(false);
  const [goldbergMode, setGoldbergMode] = useState<'full' | 'achievements_only'>('full');
  const [goldbergCopyFiles, setGoldbergCopyFiles] = useState(true);
  const [goldbergArch, setGoldbergArch] = useState<'auto' | 'x64' | 'x32'>('auto');
  const installed = props.game.state === 'installed';
  const updateAvailable = props.game.update === 'update_available';
  const steamComplete = Boolean(props.game.luaAdded && props.game.manifestAdded);
  const needsGoldberg = installed && props.game.goldbergState === 'required';

  const closePanels = (): void => setInlineAction(null);

  const removeGame = async (): Promise<void> => {
    setError('');
    try {
      if (installed) await window.api.games.remove(props.game.appId);
      else await window.api.library.remove(props.game.appId);
      await props.refreshRows();
      closePanels();
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    }
  };

  const chooseInstallFolder = async (): Promise<void> => {
    setError('');
    try {
      const folderPath = await window.api.system.pickFolder();
      if (!folderPath) return;
      const linkedGame = await window.api.games.linkFolder(props.game.appId, folderPath, props.game.name) as InstalledGame;
      await props.refreshRows();
      try {
        const target = await window.api.manifest.cachePath(props.game.appId);
        await window.api.morrenus.downloadManifest(props.game.appId, target);
        const data = await window.api.zip.process(target) as GameData;
        await window.api.games.save({
          ...linkedGame,
          manifestZipPath: target,
          manifestGIDs: data.manifests,
          selectedDepots: Object.keys(data.manifests),
          steamBuildId: data.buildId || linkedGame.steamBuildId,
          headerImageUrl: props.game.headerImageUrl || linkedGame.headerImageUrl
        });
        await window.api.updates.check([props.game.appId]);
      } catch (err) {
        setError(`Folder linked, but manifest metadata could not be prepared: ${err instanceof Error ? err.message : String(err)}`);
      }
      await props.refreshRows();
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    }
  };

  const selectExecutable = async (): Promise<void> => {
    setError('');
    try {
      const executablePath = await window.api.system.pickFile([{ name: 'Executable', extensions: ['exe'] }]);
      if (!executablePath) return;
      const allInstalled = await window.api.games.getAll() as InstalledGame[];
      const installedGame = allInstalled.find((entry) => entry.appId === props.game.appId);
      if (!installedGame) throw new Error('Installed game record was not found.');
      await window.api.games.save({ ...installedGame, executablePath });
      await props.refreshRows();
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    }
  };

  const runExecutable = async (): Promise<void> => {
    if (!props.game.executablePath) return selectExecutable();
    setError('');
    try {
      await window.api.system.openPath(props.game.executablePath);
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    }
  };

  const updateGame = async (): Promise<void> => {
    setError('');
    setUpdating(true);
    try {
      const allInstalled = await window.api.games.getAll() as InstalledGame[];
      const installedGame = allInstalled.find((entry) => entry.appId === props.game.appId);
      if (!installedGame) throw new Error('Installed game record was not found.');
      if (!installedGame.installPath) throw new Error('Installed game path was not found.');
      const target = await window.api.manifest.cachePath(props.game.appId);
      await window.api.morrenus.downloadManifest(props.game.appId, target);
      const data = await window.api.zip.process(target) as GameData;
      if (data.appId !== props.game.appId) throw new Error(`Manifest ZIP is for AppID ${data.appId}, not ${props.game.appId}.`);
      const knownDepots = installedGame.selectedDepots.length ? installedGame.selectedDepots : Object.keys(installedGame.manifestGIDs);
      const changedDepots = knownDepots.filter((depotId) => {
        const freshManifest = data.manifests[depotId];
        return freshManifest && freshManifest !== installedGame.manifestGIDs[depotId];
      });
      if (changedDepots.length === 0) {
        await window.api.updates.check([props.game.appId]);
        await props.refreshRows();
        return;
      }
      const settings = await window.api.settings.load();
      await window.api.download.start({
        gameData: { ...data, installDir: installedGame.installDir || data.installDir, selectedDepots: changedDepots, manifestZipPath: target, headerImageUrl: props.game.headerImageUrl || installedGame.headerImageUrl },
        selectedDepots: changedDepots,
        libraryPath: installedGame.libraryPath,
        outputPath: installedGame.installPath,
        steamUsername: settings.steamUsername,
        maxDownloads: settings.maxDownloads || 8,
        channelId: `update:${crypto.randomUUID()}`
      });
      const afterDownload = (await window.api.games.getAll() as InstalledGame[]).find((entry) => entry.appId === props.game.appId);
      if (afterDownload) {
        await window.api.games.save({
          ...afterDownload,
          drmStripped: installedGame.drmStripped,
          registeredWithSteam: installedGame.registeredWithSteam,
          gseSavesCopied: installedGame.gseSavesCopied,
          goldbergState: installedGame.goldbergState,
          executablePath: installedGame.executablePath,
          installedDate: installedGame.installedDate || afterDownload.installedDate
        });
      }
      await window.api.updates.check([props.game.appId]);
      await props.refreshRows();
      setInlineAction('menu');
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    } finally {
      setUpdating(false);
    }
  };

  const addToSteam = async (): Promise<void> => {
    setError('');
    try {
      await window.api.steam.addToSteam(props.game.appId);
      await props.refreshRows();
      closePanels();
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    }
  };

  const rerunManifest = async (): Promise<void> => {
    setError('');
    setRerunningManifest(true);
    try {
      const allInstalled = await window.api.games.getAll() as InstalledGame[];
      const installedGame = allInstalled.find((entry) => entry.appId === props.game.appId);
      if (!installedGame) throw new Error('Installed game record was not found.');
      if (!installedGame.libraryPath) throw new Error('Steam library path was not found.');
      if (!steamComplete) throw new Error('Add to Steam must be completed first.');

      await window.api.updates.fetchGids([props.game.appId]);
      // Reuse the hardened Add-to-Steam flow so buildId resolution is consistent.
      await window.api.steam.addToSteam(props.game.appId);

      await props.refreshRows();
      closePanels();
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    } finally {
      setRerunningManifest(false);
    }
  };

  const setGoldbergState = async (state: 'required' | 'not_needed'): Promise<void> => {
    setError('');
    try {
      await window.api.games.setGoldbergState(props.game.appId, state);
      await props.refreshRows();
      closePanels();
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    }
  };

  const applyGoldbergFromLibrary = async (): Promise<void> => {
    setError('');
    try {
      const settings = await window.api.settings.load();
      if (!settings.goldbergFilesPath?.trim()) throw new Error('Goldberg is not configured in Settings.');
      const allInstalled = await window.api.games.getAll() as InstalledGame[];
      const installedGame = allInstalled.find((entry) => entry.appId === props.game.appId);
      if (!installedGame?.installPath) throw new Error('Installed game path was not found.');
      const channelId = `goldberg:${props.game.appId}:${crypto.randomUUID()}`;
      await window.api.goldberg.run(installedGame.installPath, props.game.appId, channelId, goldbergMode, goldbergCopyFiles, goldbergArch);
      await props.refreshRows();
      setGoldbergModalOpen(false);
      if (inlineAction === null) return;
      setInlineAction('menu');
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    }
  };

  const renderInlinePanel = (): JSX.Element | null => {
    if (!inlineAction) return null;
    if (inlineAction === 'menu') {
      return (
        <div className="confirm-inline">
          <div className="confirm-glyph"><Icon name="more" size={18} /></div>
          <div className="confirm-msg confirm-msg-form">
            <strong>More actions</strong>
            <div className="inline-action-list">
              {updateAvailable && <button className="btn" disabled={updating} onClick={() => setInlineAction('update')}><Icon name="download" /> {updating ? 'Updating...' : 'Update'}</button>}
              <button className="btn" disabled={!steamComplete || rerunningManifest} title={!steamComplete ? 'Complete Add to Steam first.' : 'Refresh manifest GIDs and rewrite ACF.'} onClick={() => void rerunManifest()}>
                <Icon name="refresh" /> {rerunningManifest ? 'Rerunning...' : 'Rerun Manifest'}
              </button>
              <button className="btn" onClick={() => setGoldbergModalOpen(true)}><Icon name="box" /> Apply Goldberg</button>
              {props.game.goldbergState === 'not_needed'
                ? <button className="btn" onClick={() => void setGoldbergState('required')}><Icon name="check" /> Enable Goldberg</button>
                : <button className="btn" onClick={() => void setGoldbergState('not_needed')}><Icon name="minus" /> Disable Goldberg</button>}
              <button className="btn" disabled={steamComplete} onClick={() => void addToSteam()}><Icon name="steam" /> Add to Steam</button>
              <button className="btn" onClick={() => { if (props.game.path) void window.api.system.openPath(props.game.path); closePanels(); }}><Icon name="folder" /> Open Folder</button>
              <button className="btn danger" onClick={() => setInlineAction('remove')}><Icon name="trash" /> Uninstall</button>
            </div>
          </div>
          <div className="confirm-actions"><button className="btn ghost" onClick={closePanels}>Close</button></div>
        </div>
      );
    }
    if (inlineAction === 'remove') {
      return <ConfirmBlock icon="trash" title={`Uninstall ${props.game.name}?`} text="This deletes the installed files and removes the Library record." onBack={() => setInlineAction('menu')} onConfirm={() => void removeGame()} confirmText="Yes" />;
    }
    if (inlineAction === 'update') {
      return <ConfirmBlock icon="download" title={`Update ${props.game.name}?`} text="This downloads only changed depots and preserves your current local metadata." onBack={() => setInlineAction('menu')} onConfirm={() => void updateGame()} confirmText={updating ? 'Updating...' : 'Update'} confirmDisabled={updating} />;
    }
    return null;
  };

  return (
    <div className={`game-card ${installed ? 'installed' : 'saved'}${updateAvailable ? ' update' : ''}`}>
      <Cover row={props.game} />
      <div className="card-info">
        <div className="card-title-row">
          <div className="card-title">{props.game.name}</div>
          {needsGoldberg && <button className="goldberg-warning-btn" title="Goldberg not applied" onClick={() => setGoldbergModalOpen(true)}><Icon name="warn" size={14} /> Goldberg not applied</button>}
          {installed && props.game.goldbergState === 'applied' && <span className="badge good goldberg-applied-badge"><Icon name="check" size={14} /> Goldberg applied</span>}
        </div>
        <div className="card-badges">
          {props.game.update === 'up_to_date' && <span className="badge good"><Icon name="check" size={15} strokeW={2.3} /> Up to date</span>}
          {updateAvailable && <span className="badge warn">Outdated</span>}
          {props.game.luaAdded && <span className="badge lua"><Icon name="box" size={15} strokeW={2} /> Lua Added</span>}
          {props.game.manifestAdded && <span className="badge manifest"><Icon name="zip" size={15} strokeW={2} /> Manifest Added</span>}
          {!installed && <span className="badge info">Saved</span>}
          {props.achData && (
            <span className={`badge achievements${props.achData.hasPlatinum ? ' platinum' : ''}`}>
              <Icon name="trophy" size={15} />
              {props.achData.hasPlatinum ? 'Platinum' : `${props.achData.unlockedCount}/${props.achData.totalCount}`}
            </span>
          )}
        </div>
        <div className="meta-row"><Icon name="steam" size={18} /><span>Steam App ID: {props.game.appId}</span><span className="dot-sep">-</span><span>{props.game.update === 'cannot_determine' ? 'Version unknown' : 'Latest manifest'}</span><span className="dot-sep">-</span><span>{installed ? 'Installed' : 'Not installed'}</span></div>
      </div>

      {installed ? (
        <>
          <div className="card-primary-actions">
            <div className="run-split">
              <button className="btn primary run-main" onClick={runExecutable}><Icon name="play" /> {props.game.executablePath ? 'Run' : 'Select Executable'}</button>
              {props.game.executablePath && <button className="btn run-change" title="Change executable" onClick={selectExecutable}><Icon name="settings" /></button>}
            </div>
            <button className="btn card-action menu-button icon-only" title="More actions" onClick={() => setInlineAction('menu')}><Icon name="more" /></button>
          </div>
        </>
      ) : (
        <div className="card-actions">
          <button className="btn primary card-action-main" onClick={() => props.openDownload(props.game)}><Icon name="download" /> Download</button>
          <button className="btn card-action" onClick={() => void chooseInstallFolder()}><Icon name="folder" /> Choose Install Folder</button>
          <button className="btn danger card-action" onClick={() => void removeGame()}><Icon name="trash" /> Remove</button>
        </div>
      )}

      {error && <div className="card-error">{error}</div>}
      {renderInlinePanel()}
      {goldbergModalOpen && createPortal(
        <div className="modal-backdrop">
          <div className="modal small-modal auth-modal">
            <div className="modal-head">
              <Icon name="box" />
              <div>
                <div style={{ color: 'var(--fg-0)', fontWeight: 700 }}>Apply Goldberg</div>
                <div className="subtle">Choose options and apply to this game.</div>
              </div>
            </div>
            <div className="modal-body">
              <div className="goldberg-inline-grid">
                <label><span>Architecture</span><select value={goldbergArch} onChange={(event) => setGoldbergArch(event.target.value as 'auto' | 'x64' | 'x32')}><option value="auto">Auto detect</option><option value="x64">64-bit (steam_api64.dll)</option><option value="x32">32-bit (steam_api.dll)</option></select></label>
                <label><span>Install Mode</span><select value={goldbergMode} onChange={(event) => setGoldbergMode(event.target.value as 'full' | 'achievements_only')}><option value="full">Full install (DLLs + achievements)</option><option value="achievements_only">Achievements only (steam_settings)</option></select></label>
                <label><span>Copy Files</span><select value={goldbergCopyFiles ? 'yes' : 'no'} onChange={(event) => setGoldbergCopyFiles(event.target.value === 'yes')}><option value="yes">Yes</option><option value="no">No (generate output only)</option></select></label>
              </div>
            </div>
            <div className="modal-foot">
              <button className="btn ghost" onClick={() => setGoldbergModalOpen(false)}>Cancel</button>
              <button className="btn primary" onClick={() => void applyGoldbergFromLibrary()}>Apply</button>
            </div>
          </div>
        </div>,
        document.body
      )}
    </div>
  );
}

function ConfirmBlock(props: {
  icon: 'steam' | 'trash' | 'download' | 'folder' | 'check' | 'minus';
  title: string;
  text: string;
  onBack: () => void;
  onConfirm: () => void;
  confirmText: string;
  confirmDisabled?: boolean;
}): JSX.Element {
  return (
    <div className="confirm-inline">
      <div className="confirm-glyph"><Icon name={props.icon} size={18} /></div>
      <div className="confirm-msg"><strong>{props.title}</strong><span style={{ color: 'var(--fg-2)' }}> {props.text}</span></div>
      <div className="confirm-actions">
        <button className="btn ghost" onClick={props.onBack}>Back</button>
        <button className="btn primary" disabled={props.confirmDisabled} onClick={props.onConfirm}>{props.confirmText}</button>
      </div>
    </div>
  );
}

function Cover({ row }: { row: LibraryRow }): JSX.Element {
  const fallback = `https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/${row.appId}/header.jpg`;
  const [src, setSrc] = useState(row.headerImageUrl || fallback);
  const [failed, setFailed] = useState(false);
  useEffect(() => { setSrc(row.headerImageUrl || fallback); setFailed(false); }, [fallback, row.headerImageUrl]);
  const onError = (): void => { if (src !== fallback) setSrc(fallback); else setFailed(true); };
  if (failed) return <div className="cover placeholder"><span>{row.name.slice(0, 3)}</span></div>;
  return <div className="cover"><img src={src} alt="" onError={onError} /></div>;
}

export function Empty({ title, body }: { title: string; body: string }): JSX.Element {
  return <div className="empty"><div className="empty-icon"><Icon name="library" size={22} /></div><h3>{title}</h3><p>{body}</p></div>;
}
