import { useState } from 'react'
import { api } from '../api'
import type { MorrenusSearchResult } from '../api'

export default function SearchView(): JSX.Element {
  const [query, setQuery] = useState('')
  const [results, setResults] = useState<MorrenusSearchResult[]>([])
  const [label, setLabel] = useState('')
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  const [queued, setQueued] = useState<Set<string>>(new Set())

  const search = async (): Promise<void> => {
    if (!query.trim()) return
    setLoading(true)
    setError('')
    setResults([])
    try {
      const data = await api.search(query.trim())
      setResults(data.results)
      setLabel(data.label)
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
    } finally {
      setLoading(false)
    }
  }

  const queue = async (result: MorrenusSearchResult): Promise<void> => {
    try {
      await api.tasks.create('add_to_library', {
        appId: result.gameId,
        gameName: result.gameName,
        headerImageUrl: result.headerImageUrl,
      })
      setQueued((prev) => new Set([...prev, result.gameId]))
    } catch (err) {
      alert(err instanceof Error ? err.message : String(err))
    }
  }

  return (
    <div style={{ padding: '1rem' }}>
      <div style={{ display: 'flex', gap: '0.5rem', marginBottom: '0.75rem' }}>
        <input
          style={{ flex: 1, background: '#1f2937', border: '1px solid #374151', borderRadius: 6, padding: '0.5rem 0.75rem', color: '#fff', fontSize: '1rem' }}
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          onKeyDown={(e) => e.key === 'Enter' && void search()}
          placeholder="Search games…"
        />
        <button
          style={{ background: '#3b82f6', color: '#fff', border: 'none', borderRadius: 6, padding: '0.5rem 1rem', cursor: 'pointer', fontSize: '1rem' }}
          onClick={() => void search()}
          disabled={loading}
        >{loading ? '…' : 'Search'}</button>
      </div>

      {label && <p style={{ color: '#9ca3af', fontSize: '0.8rem', marginBottom: '0.75rem' }}>{label}</p>}
      {error && <p style={{ color: '#f87171', marginBottom: '0.75rem' }}>{error}</p>}

      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(160px, 1fr))', gap: '0.75rem' }}>
        {results.map((result) => (
          <div key={result.gameId} style={{ background: '#111318', borderRadius: 8, overflow: 'hidden' }}>
            {result.headerImageUrl && (
              <img src={result.headerImageUrl} alt="" style={{ width: '100%', aspectRatio: '460/215', objectFit: 'cover', display: 'block' }} />
            )}
            <div style={{ padding: '0.5rem' }}>
              <div style={{ color: '#fff', fontWeight: 600, fontSize: '0.875rem', marginBottom: '0.25rem' }}>{result.gameName}</div>
              {result.releaseYear && <div style={{ color: '#6b7280', fontSize: '0.75rem', marginBottom: '0.25rem' }}>{result.releaseYear}</div>}
              <button
                onClick={() => void queue(result)}
                disabled={queued.has(result.gameId)}
                style={{ background: queued.has(result.gameId) ? '#374151' : '#3b82f6', color: queued.has(result.gameId) ? '#9ca3af' : '#fff', border: 'none', borderRadius: 4, padding: '0.3rem 0.6rem', cursor: queued.has(result.gameId) ? 'default' : 'pointer', fontSize: '0.75rem', width: '100%' }}
              >{queued.has(result.gameId) ? 'Queued' : 'Add to Library'}</button>
            </div>
          </div>
        ))}
      </div>
    </div>
  )
}
