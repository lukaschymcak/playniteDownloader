import { Hono } from 'hono'
import { apiKeyAuth } from '../middleware/auth.js'

const MORRENUS_BASE = 'https://manifest.morrenus.xyz/api/v1'

export function registerSearchRoutes(app: Hono): void {
  app.get('/api/search', apiKeyAuth, async (c) => {
    const q = c.req.query('q')
    const mode = c.req.query('mode') ?? 'games'
    if (!q) return c.json({ error: 'Missing q parameter' }, 400)

    const apiKey = process.env.MORRENUS_API_KEY
    if (!apiKey) return c.json({ error: 'Search not configured on server' }, 503)

    const endpoint = mode === 'dlc' ? 'search/dlc' : 'search/games'
    const res = await fetch(`${MORRENUS_BASE}/${endpoint}?q=${encodeURIComponent(q)}`, {
      method: 'POST',
      headers: { Authorization: `Bearer ${apiKey}` },
    }).catch(() => null)

    if (!res?.ok) return c.json({ error: 'Morrenus upstream error' }, 502)
    return c.json(await res.json())
  })
}
