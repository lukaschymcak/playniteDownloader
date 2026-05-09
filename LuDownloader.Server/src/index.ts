import 'dotenv/config'
import fsp from 'node:fs/promises'
import { serve } from '@hono/node-server'
import { serveStatic } from '@hono/node-server/serve-static'
import { Hono } from 'hono'
import { cors } from 'hono/cors'
import { logger } from 'hono/logger'
import { apiKeyAuth } from './middleware/auth.js'
import libraryRouter from './routes/library.js'
import tasksRouter from './routes/tasks.js'
import searchRouter from './routes/search.js'
import { ensureTables } from './db/ensureTables.js'

const app = new Hono()

app.use('*', logger())
app.use('*', cors())

app.get('/api/health', (c) => c.json({ ok: true, version: '0.1.0' }))

app.use('/api/*', apiKeyAuth)
app.route('/api/library', libraryRouter)
app.route('/api/tasks', tasksRouter)
app.route('/api/search', searchRouter)

// Static assets (Vite outputs to assets/)
app.use('/assets/*', serveStatic({ root: './pwa/dist' }))

// SPA fallback — only fires when no route matched
app.notFound(async (c) => {
  const pathname = new URL(c.req.url).pathname
  if (pathname.startsWith('/api/')) {
    return c.json({ error: 'Not found' }, 404)
  }
  try {
    const html = await fsp.readFile('./pwa/dist/index.html', 'utf-8')
    return c.html(html)
  } catch {
    return c.text('PWA not built', 404)
  }
})

async function main(): Promise<void> {
  if (!process.env.DATABASE_URL) throw new Error('DATABASE_URL is required')
  if (!process.env.API_KEY) throw new Error('API_KEY is required')
  await ensureTables()
  const port = Number(process.env.PORT ?? 3000)
  serve({ fetch: app.fetch, port }, () => {
    console.log(`LuDownloader Server on port ${port}`)
  })
}

main().catch((err) => { console.error(err); process.exit(1) })
