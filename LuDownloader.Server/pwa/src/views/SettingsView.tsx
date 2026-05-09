import { useState, useEffect } from 'react'
import { loadPwaSettings, savePwaSettings, api } from '../api'
import type { PwaSettings } from '../api'

const empty: PwaSettings = { serverUrl: '', apiKey: '' }

export default function SettingsView(): JSX.Element {
  const [settings, setSettings] = useState<PwaSettings>(empty)
  const [saved, setSaved] = useState(false)
  const [testResult, setTestResult] = useState('')

  useEffect(() => {
    const s = loadPwaSettings()
    if (s) setSettings(s)
  }, [])

  const set = (key: keyof PwaSettings, value: string): void =>
    setSettings((prev) => ({ ...prev, [key]: value }))

  const save = (): void => {
    savePwaSettings(settings)
    setSaved(true)
    setTimeout(() => setSaved(false), 2000)
  }

  const testConnection = async (): Promise<void> => {
    setTestResult('Testing…')
    try {
      // Temporarily save to make api.ts pick up the values
      savePwaSettings(settings)
      const result = await api.health()
      setTestResult(result.ok ? '✓ Connected' : '✗ Unexpected response')
    } catch (err) {
      setTestResult(`✗ ${err instanceof Error ? err.message : String(err)}`)
    }
  }

  return (
    <div style={{ padding: '1rem', maxWidth: 480, margin: '0 auto' }}>
      <h2 style={{ color: '#fff', marginBottom: '1.5rem' }}>Settings</h2>
      <label style={labelStyle}>
        <span style={spanStyle}>Server URL</span>
        <input
          style={inputStyle}
          value={settings.serverUrl}
          onChange={(e) => set('serverUrl', e.target.value)}
          placeholder="https://your-app.railway.app"
        />
      </label>
      <label style={labelStyle}>
        <span style={spanStyle}>API Key</span>
        <input
          style={inputStyle}
          type="password"
          value={settings.apiKey}
          onChange={(e) => set('apiKey', e.target.value)}
          placeholder="shared secret"
        />
      </label>
      <div style={{ display: 'flex', gap: '0.75rem', marginTop: '1.5rem' }}>
        <button style={btnStyle('#3b82f6')} onClick={save}>{saved ? 'Saved!' : 'Save'}</button>
        <button style={btnStyle('#374151')} onClick={() => void testConnection()}>Test</button>
      </div>
      {testResult && <p style={{ color: testResult.startsWith('✓') ? '#4ade80' : '#f87171', marginTop: '0.75rem' }}>{testResult}</p>}
    </div>
  )
}

const labelStyle: React.CSSProperties = { display: 'flex', flexDirection: 'column', gap: '0.25rem', marginBottom: '1rem' }
const spanStyle: React.CSSProperties = { color: '#9ca3af', fontSize: '0.875rem' }
const inputStyle: React.CSSProperties = { background: '#1f2937', border: '1px solid #374151', borderRadius: 6, padding: '0.5rem 0.75rem', color: '#fff', fontSize: '1rem', width: '100%', boxSizing: 'border-box' }
const btnStyle = (bg: string): React.CSSProperties => ({ background: bg, color: '#fff', border: 'none', borderRadius: 6, padding: '0.5rem 1.25rem', fontSize: '1rem', cursor: 'pointer' })
