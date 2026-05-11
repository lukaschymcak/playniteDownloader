import { useEffect, useMemo, useState } from 'react';
import type { InstalledGame, MorrenusSearchResponse, MorrenusSearchResult, SavedLibraryGame } from '../../../shared/types';
import { Icon } from '../icons';
import { Empty } from './LibraryView';

const pageSize = 9;

type SearchMode = 'games' | 'dlc';

const emptyResponse = (mode: SearchMode): MorrenusSearchResponse => ({
  mode,
  results: [],
  total: 0,
  label: mode === 'dlc' ? 'No DLC loaded.' : 'No games loaded.'
});

export function SearchView(props: {
  openDownload: (game: { appId: string; name: string; headerImageUrl?: string }) => void;
  refreshRows: () => Promise<void>;
}): JSX.Element {
  const [term, setTerm] = useState('');
  const [lastSearch, setLastSearch] = useState('');
  const [mode, setMode] = useState<SearchMode>('games');
  const [games, setGames] = useState<MorrenusSearchResponse>(emptyResponse('games'));
  const [dlc, setDlc] = useState<MorrenusSearchResponse>(emptyResponse('dlc'));
  const [loading, setLoading] = useState(false);
  const [dlcLoading, setDlcLoading] = useState(false);
  const [error, setError] = useState('');
  const [page, setPage] = useState(1);
  const [libraryIds, setLibraryIds] = useState<Set<string>>(() => new Set());

  const loadLibraryIds = async (): Promise<void> => {
    const [saved, installed] = await Promise.all([
      window.api.library.getAll() as Promise<SavedLibraryGame[]>,
      window.api.games.getAll() as Promise<InstalledGame[]>
    ]);
    setLibraryIds(new Set([
      ...saved.map((game) => game.appId),
      ...installed.map((game) => game.appId)
    ]));
  };

  useEffect(() => {
    void loadLibraryIds().catch(() => undefined);
  }, []);

  const active = mode === 'games' ? games : dlc;
  const sortedResults = useMemo(() => {
    return active.results
      .map((result, index) => ({ result, index }))
      .sort((a, b) => (a.result.relevanceScore ?? a.index) - (b.result.relevanceScore ?? b.index) || a.index - b.index)
      .map((item) => item.result);
  }, [active.results]);

  const pageCount = Math.max(1, Math.ceil(sortedResults.length / pageSize));
  const currentPage = Math.min(page, pageCount);
  const pagedResults = sortedResults.slice((currentPage - 1) * pageSize, currentPage * pageSize);

  const fetchResults = async (query: string, requestedMode: SearchMode, visible: boolean): Promise<MorrenusSearchResponse> => {
    if (visible) setLoading(true);
    else setDlcLoading(true);
    try {
      return await window.api.morrenus.search(query, requestedMode) as MorrenusSearchResponse;
    } finally {
      if (visible) setLoading(false);
      else setDlcLoading(false);
    }
  };

  const search = async (): Promise<void> => {
    const clean = term.trim();
    if (!clean) return;

    setError('');
    setPage(1);
    setMode('games');
    setLastSearch(clean);
    setDlc(emptyResponse('dlc'));

    try {
      const gameRows = await fetchResults(clean, 'games', true);
      setGames(gameRows);

      void fetchResults(clean, 'dlc', false)
        .then(setDlc)
        .catch(() => setDlc({ mode: 'dlc', results: [], total: 0, label: 'DLC load failed.' }));
    } catch (err) {
      setGames(emptyResponse('games'));
      setError(err instanceof Error ? err.message : String(err));
    }
  };

  const switchMode = async (nextMode: SearchMode): Promise<void> => {
    setMode(nextMode);
    setPage(1);
    if (nextMode === 'games' || !lastSearch || dlc.results.length > 0 || dlc.label !== 'No DLC loaded.') return;

    try {
      setDlc(await fetchResults(lastSearch, 'dlc', true));
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    }
  };

  return (
    <div className="search-page">
      <div className="search-topbar">
        <div className="search-input-panel">
          <Icon name="search" size={18} />
          <input
            className="search-input"
            placeholder="Search for games, e.g. Resident Evil 2..."
            value={term}
            onChange={(event) => setTerm(event.target.value)}
            onKeyDown={(event) => { if (event.key === 'Enter') void search(); }}
          />
        </div>
        <button className="btn primary search-submit" onClick={search}><Icon name="search" /> {loading ? 'Searching...' : 'Search'}</button>
      </div>

      {error && <div className="update-banner"><strong>{error}</strong></div>}

      {lastSearch ? (
        <>
          <div className="search-results-head">
            <span>{active.label}{lastSearch ? ` for "${lastSearch}"` : ''}</span>
            <div className="mode-switch" role="tablist" aria-label="Search result type">
              <button className={mode === 'games' ? 'active' : ''} onClick={() => void switchMode('games')}>Games</button>
              <button className={mode === 'dlc' ? 'active' : ''} onClick={() => void switchMode('dlc')}>DLC{dlcLoading ? '...' : ''}</button>
            </div>
          </div>

          {active.baseGame && (
            <div className="dlc-context">
              <Icon name="steam" size={16} />
              <span>DLC shown for <strong>{active.baseGame.gameName}</strong></span>
            </div>
          )}

          {pagedResults.length > 0 ? (
            <>
              <div className="search-results-grid">
                {pagedResults.map((result) => (
                  <SearchTile
                    key={result.gameId}
                    result={result}
                    inLibrary={libraryIds.has(result.gameId)}
                    openDownload={props.openDownload}
                    refreshRows={props.refreshRows}
                    refreshLibraryIds={loadLibraryIds}
                  />
                ))}
              </div>

              <div className="search-pagination">
                <div>Showing {(currentPage - 1) * pageSize + 1}-{Math.min(currentPage * pageSize, sortedResults.length)} of {sortedResults.length} results</div>
                <div className="pager">
                  {Array.from({ length: pageCount }, (_, index) => index + 1).slice(0, 6).map((item) => (
                    <button key={item} className={item === currentPage ? 'active' : ''} onClick={() => setPage(item)}>{item}</button>
                  ))}
                  {pageCount > 6 && <span>...</span>}
                  <button disabled={currentPage === pageCount} onClick={() => setPage((value) => Math.min(pageCount, value + 1))}>›</button>
                </div>
              </div>
            </>
          ) : (
            <Empty title={mode === 'dlc' ? 'No DLC found' : 'No games found'} body={active.label} />
          )}
        </>
      ) : (
        <Empty title="Search for a game" body="Search Steam by title or AppID. DLC can be switched on after a game search." />
      )}
    </div>
  );
}

function SearchTile(props: {
  result: MorrenusSearchResult;
  inLibrary: boolean;
  openDownload: (game: { appId: string; name: string; headerImageUrl?: string }) => void;
  refreshRows: () => Promise<void>;
  refreshLibraryIds: () => Promise<void>;
}): JSX.Element {
  const fallback = `https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/${props.result.gameId}/header.jpg`;
  const game = {
    appId: props.result.gameId,
    name: props.result.gameName || props.result.gameId,
    headerImageUrl: props.result.headerImageUrl || fallback
  };
  const [src, setSrc] = useState(game.headerImageUrl);
  const [imgFailed, setImgFailed] = useState(false);

  const onError = (): void => {
    if (src !== fallback) setSrc(fallback);
    else setImgFailed(true);
  };

  return (
    <div className="search-tile">
      {imgFailed
        ? <div className="cover placeholder search-cover"><span>{game.name.slice(0, 3)}</span></div>
        : <div className="cover search-cover"><img src={src} alt="" onError={onError} /></div>
      }
      <div className="tile-body">
        <div className="tile-meta">App ID: {game.appId}{props.result.releaseYear ? ` · ${props.result.releaseYear}` : ''}</div>
        <div className="tile-title">{game.name}</div>
        {props.result.shortDescription && <div className="tile-desc">{props.result.shortDescription}</div>}
        <div className="tile-dlc">{props.result.dlcCount || 0} DLC{props.result.dlcCount === 1 ? '' : 's'}</div>
      </div>
      <div className="tile-actions">
        <button className="btn add-library" disabled={props.inLibrary} title={props.inLibrary ? 'Already in Library' : 'Add to Library without downloading'} onClick={async () => {
          if (props.inLibrary) return;
          await window.api.library.add({ appId: game.appId, gameName: game.name, headerImageUrl: game.headerImageUrl });
          await props.refreshRows();
          await props.refreshLibraryIds();
        }}><Icon name={props.inLibrary ? 'check' : 'plus'} /> {props.inLibrary ? 'In Library' : 'Add to Library'}</button>
        <button className="btn primary" onClick={() => props.openDownload(game)}><Icon name="download" /> Download</button>
      </div>
    </div>
  );
}
