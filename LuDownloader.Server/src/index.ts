import 'dotenv/config'
import { serve } from '@hono/node-server'
import app from './app.js'
import { ensureTables } from './db/ensureTables.js'

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
