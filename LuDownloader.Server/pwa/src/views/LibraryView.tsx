import { useState, useEffect } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../api'
import type { InstalledGame, SavedLibraryGame } from '../api'

type Tab = 'installed' | 'saved'

function formatSize(bytes: number | null | undefined): string {
  if (!bytes) return ''
  if (bytes >= 1_000_000_000) return `${(bytes / 1_000_000_000).toFixed(1)} GB`
  return `${(bytes / 1_000_000).toFixed(0)} MB`
}

export default function LibraryView(): JSX.Element {
  const [installed, setInstalled] = useState<InstalledGame[]>([])
  const [saved, setSaved] = useState<SavedLibraryGame[]>([])
  const [tab, setTab] = useState<Tab>('installed')
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')

  useEffect(() => {
    api.library.list()
      .then((data) => { setInstalled(data.installed); setSaved(data.saved) })
      .catch((err: unknown) => setError(err instanceof Error ? err.message : String(err)))
      .finally(() => setLoading(false))
  }, [])

  if (loading) return <p style={msgStyle}>Loading library…</p>
  if (error) return <p style={{ ...msgStyle, color: '#f87171' }}>{error}</p>

  const installedAppIds = new Set(installed.map((g) => g.appId))

  return (
    <div style={{ padding: '1rem' }}>
      <div style={{ display: 'flex', gap: '0.5rem', marginBottom: '1rem' }}>
        <TabBtn active={tab === 'installed'} onClick={() => setTab('installed')}>Installed ({installed.length})</TabBtn>
        <TabBtn active={tab === 'saved'} onClick={() => setTab('saved')}>Saved ({saved.length})</TabBtn>
      </div>

      {tab === 'installed' && (
        <div style={gridStyle}>
          {installed.length === 0 && <p style={msgStyle}>No installed games synced yet.</p>}
          {installed.map((g) => (
            <div key={g.appId} style={cardStyle}>
              {g.headerImageUrl && <img src={g.headerImageUrl} alt="" style={imgStyle} />}
              <div style={{ padding: '0.5rem' }}>
                <div style={{ color: '#fff', fontWeight: 600 }}>{g.gameName}</div>
                {g.sizeOnDisk != null && <div style={{ color: '#9ca3af', fontSize: '0.75rem' }}>{formatSize(g.sizeOnDisk)}</div>}
              </div>
            </div>
          ))}
        </div>
      )}

      {tab === 'saved' && (
        <div style={gridStyle}>
          {saved.length === 0 && <p style={msgStyle}>No saved games.</p>}
          {saved.map((g) => (
            <div key={g.appId} style={cardStyle}>
              {g.headerImageUrl && <img src={g.headerImageUrl} alt="" style={imgStyle} />}
              <div style={{ padding: '0.5rem' }}>
                <div style={{ color: '#fff', fontWeight: 600 }}>{g.gameName}</div>
                {!installedAppIds.has(g.appId) && (
                  <Link to={`/queue/${g.appId}`} style={{ color: '#3b82f6', fontSize: '0.75rem', textDecoration: 'none' }}>
                    Queue Download →
                  </Link>
                )}
              </div>
            </div>
          ))}
        </div>
      )}
    </div>
  )
}

function TabBtn({ active, onClick, children }: { active: boolean; onClick: () => void; children: React.ReactNode }): JSX.Element {
  return (
    <button
      onClick={onClick}
      style={{ background: active ? '#3b82f6' : '#1f2937', color: '#fff', border: 'none', borderRadius: 6, padding: '0.4rem 1rem', cursor: 'pointer', fontSize: '0.875rem' }}
    >{children}</button>
  )
}

const msgStyle: React.CSSProperties = { color: '#9ca3af', padding: '2rem 1rem', textAlign: 'center' }
const gridStyle: React.CSSProperties = { display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(160px, 1fr))', gap: '0.75rem' }
const cardStyle: React.CSSProperties = { background: '#111318', borderRadius: 8, overflow: 'hidden' }
const imgStyle: React.CSSProperties = { width: '100%', aspectRatio: '460/215', objectFit: 'cover', display: 'block' }
