import fsp from 'node:fs/promises'
import { serveStatic } from '@hono/node-server/serve-static'
import { Hono } from 'hono'
import { cors } from 'hono/cors'
import { logger } from 'hono/logger'
import { registerLibraryRoutes } from './routes/library.js'
import { registerTasksRoutes } from './routes/tasks.js'
import { registerSearchRoutes } from './routes/search.js'

const app = new Hono()

app.use('*', logger())
app.use('*', cors())

app.get('/api/health', (c) => c.json({ ok: true, version: '0.1.0' }))

registerLibraryRoutes(app)
registerTasksRoutes(app)
registerSearchRoutes(app)

app.all('/api/*', (c) => c.json({ error: 'Not found' }, 404))

// Static assets (Vite outputs to assets/)
app.use('/assets/*', serveStatic({ root: './pwa/dist' }))

// SPA fallback - only fires when no route matched
app.notFound(async (c) => {
  try {
    const html = await fsp.readFile('./pwa/dist/index.html', 'utf-8')
    return c.html(html)
  } catch {
    return c.text('PWA not built', 404)
  }
})

export default app
