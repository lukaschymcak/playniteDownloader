import { useState, useEffect, useCallback } from 'react'
import { api } from '../api'
import type { Task } from '../api'

type Filter = 'all' | 'pending' | 'running' | 'completed' | 'failed'

const STATUS_COLOR: Record<string, string> = {
  pending: '#facc15',
  running: '#3b82f6',
  completed: '#4ade80',
  failed: '#f87171',
}

function relativeTime(iso: string): string {
  const diff = Date.now() - new Date(iso).getTime()
  if (diff < 60_000) return 'just now'
  if (diff < 3_600_000) return `${Math.floor(diff / 60_000)}m ago`
  if (diff < 86_400_000) return `${Math.floor(diff / 3_600_000)}h ago`
  return `${Math.floor(diff / 86_400_000)}d ago`
}

export default function TasksView(): JSX.Element {
  const [tasks, setTasks] = useState<Task[]>([])
  const [filter, setFilter] = useState<Filter>('all')
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')

  const refresh = useCallback((): void => {
    api.tasks
      .list()
      .then((data) => {
        setTasks(data.sort((a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime()))
        setError('')
      })
      .catch((err: unknown) => setError(err instanceof Error ? err.message : String(err)))
      .finally(() => setLoading(false))
  }, [])

  useEffect(() => {
    refresh()
    const interval = setInterval(refresh, 10_000)
    return () => clearInterval(interval)
  }, [refresh])

  const cancel = async (id: string): Promise<void> => {
    await api.tasks.delete(id).catch(() => undefined)
    refresh()
  }

  const filtered = filter === 'all' ? tasks : tasks.filter((t) => t.status === filter)

  return (
    <div style={{ padding: '1rem' }}>
      <div style={{ display: 'flex', gap: '0.4rem', flexWrap: 'wrap', marginBottom: '1rem' }}>
        {(['all', 'pending', 'running', 'completed', 'failed'] as Filter[]).map((f) => (
          <button
            key={f}
            onClick={() => setFilter(f)}
            style={{
              background: filter === f ? '#3b82f6' : '#1f2937',
              color: '#fff',
              border: 'none',
              borderRadius: 6,
              padding: '0.3rem 0.75rem',
              cursor: 'pointer',
              fontSize: '0.8rem',
              textTransform: 'capitalize',
            }}
          >
            {f}
          </button>
        ))}
      </div>

      {loading && <p style={msgStyle}>Loading tasks…</p>}
      {error && <p style={{ ...msgStyle, color: '#f87171' }}>{error}</p>}
      {!loading && !error && filtered.length === 0 && <p style={msgStyle}>No tasks.</p>}

      {filtered.map((task) => (
        <div
          key={task.id}
          style={{ background: '#111318', borderRadius: 8, padding: '0.75rem', marginBottom: '0.75rem' }}
        >
          <div
            style={{
              display: 'flex',
              justifyContent: 'space-between',
              alignItems: 'center',
              marginBottom: '0.4rem',
            }}
          >
            <span style={{ color: '#fff', fontWeight: 600, textTransform: 'capitalize' }}>
              {task.type.replace(/_/g, ' ')}
            </span>
            <span
              style={{
                color: STATUS_COLOR[task.status] ?? '#9ca3af',
                fontSize: '0.8rem',
                textTransform: 'capitalize',
              }}
            >
              {task.status}
            </span>
          </div>

          {task.status === 'running' && task.progress != null && (
            <div style={{ background: '#374151', borderRadius: 4, height: 4, marginBottom: '0.5rem', overflow: 'hidden' }}>
              <div
                style={{ background: '#3b82f6', height: '100%', width: `${task.progress}%`, transition: 'width 0.3s' }}
              />
            </div>
          )}

          {task.error && (
            <p style={{ color: '#f87171', fontSize: '0.8rem', margin: '0.25rem 0' }}>{task.error}</p>
          )}

          {task.logTail && task.logTail.length > 0 && (
            <pre
              style={{
                color: '#9ca3af',
                fontSize: '0.7rem',
                margin: '0.25rem 0',
                whiteSpace: 'pre-wrap',
                wordBreak: 'break-all',
              }}
            >
              {task.logTail.slice(-5).join('\n')}
            </pre>
          )}

          <div
            style={{
              display: 'flex',
              justifyContent: 'space-between',
              alignItems: 'center',
              marginTop: '0.4rem',
            }}
          >
            <span style={{ color: '#6b7280', fontSize: '0.75rem' }}>{relativeTime(task.createdAt)}</span>
            {task.status !== 'running' && (
              <button
                onClick={() => {
                  void cancel(task.id)
                }}
                style={{
                  background: '#7f1d1d',
                  color: '#fca5a5',
                  border: 'none',
                  borderRadius: 4,
                  padding: '0.2rem 0.6rem',
                  cursor: 'pointer',
                  fontSize: '0.75rem',
                }}
              >
                {task.status === 'pending' ? 'Cancel' : 'Delete'}
              </button>
            )}
          </div>
        </div>
      ))}
    </div>
  )
}

const msgStyle: React.CSSProperties = { color: '#9ca3af', textAlign: 'center', padding: '2rem 1rem' }
