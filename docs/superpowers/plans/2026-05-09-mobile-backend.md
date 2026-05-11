# Mobile Backend Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a cloud backend (Hono + Postgres on Railway) + PWA so the user can browse their game library and queue downloads from a phone, with the desktop Electron app executing queued tasks on startup.

**Architecture:** Cloud backend is a message broker — phone queues tasks via REST, desktop polls and executes them. Desktop owns the source of truth; cloud is a sync target. Three new packages: `LuDownloader.Server/` (Hono API), `LuDownloader.Server/pwa/` (Vite React PWA), and `LuDownloader.Electron/src/main/sync/cloudSync.ts`.

**Tech Stack:** Hono v4 + @hono/node-server, Drizzle ORM + pg (Postgres), Vite + React 18 + react-router-dom v6, Railway (cloud host), TypeScript throughout.

---

## File Map

**Create:**
- `LuDownloader.Server/package.json`
- `LuDownloader.Server/tsconfig.json`
- `LuDownloader.Server/.env.example`
- `LuDownloader.Server/railway.json`
- `LuDownloader.Server/src/index.ts`
- `LuDownloader.Server/src/types.ts`
- `LuDownloader.Server/src/db/schema.ts`
- `LuDownloader.Server/src/db/client.ts`
- `LuDownloader.Server/src/db/ensureTables.ts`
- `LuDownloader.Server/src/middleware/auth.ts`
- `LuDownloader.Server/src/routes/library.ts`
- `LuDownloader.Server/src/routes/tasks.ts`
- `LuDownloader.Server/src/routes/search.ts`
- `LuDownloader.Server/pwa/package.json`
- `LuDownloader.Server/pwa/index.html`
- `LuDownloader.Server/pwa/vite.config.ts`
- `LuDownloader.Server/pwa/tsconfig.json`
- `LuDownloader.Server/pwa/src/main.tsx`
- `LuDownloader.Server/pwa/src/App.tsx`
- `LuDownloader.Server/pwa/src/api.ts`
- `LuDownloader.Server/pwa/src/views/LibraryView.tsx`
- `LuDownloader.Server/pwa/src/views/TasksView.tsx`
- `LuDownloader.Server/pwa/src/views/SearchView.tsx`
- `LuDownloader.Server/pwa/src/views/SettingsView.tsx`
- `LuDownloader.Electron/src/main/sync/cloudSync.ts`

**Modify:**
- `LuDownloader.Electron/src/shared/types.ts` — add `cloudServerUrl`, `cloudApiKey` to `AppSettings`
- `LuDownloader.Electron/src/main/ipc/settings.ts` — include new fields in load/save
- `LuDownloader.Electron/src/renderer/src/views/SettingsModal.tsx` — add Cloud section
- `LuDownloader.Electron/src/main/index.ts` — extract `addToSteam`+`ensureManifestGids`, wire sync agent

---

## Phase 1: Backend Server

### Task 1: Backend Project Scaffold

**Files:**
- Create: `LuDownloader.Server/package.json`
- Create: `LuDownloader.Server/tsconfig.json`
- Create: `LuDownloader.Server/.env.example`
- Create: `LuDownloader.Server/.gitignore`

- [ ] **Step 1: Create package.json**

```json
{
  "name": "ludownloader-server",
  "version": "0.1.0",
  "type": "module",
  "main": "dist/index.js",
  "scripts": {
    "dev": "tsx watch src/index.ts",
    "build": "tsc && cd pwa && npm run build",
    "start": "node dist/index.js"
  },
  "dependencies": {
    "@hono/node-server": "^1.14.0",
    "drizzle-orm": "^0.44.0",
    "hono": "^4.7.0",
    "pg": "^8.14.1",
    "dotenv": "^16.5.0"
  },
  "devDependencies": {
    "@types/pg": "^8.11.13",
    "drizzle-kit": "^0.31.0",
    "tsx": "^4.19.4",
    "typescript": "^5.7.3"
  }
}
```

- [ ] **Step 2: Create tsconfig.json**

```json
{
  "compilerOptions": {
    "target": "ESNext",
    "module": "ESNext",
    "moduleResolution": "bundler",
    "outDir": "dist",
    "rootDir": "src",
    "strict": true,
    "skipLibCheck": true,
    "esModuleInterop": true
  },
  "include": ["src/**/*"],
  "exclude": ["node_modules", "dist", "pwa"]
}
```

- [ ] **Step 3: Create .env.example**

```
DATABASE_URL=postgresql://user:password@host:5432/dbname
API_KEY=change-me-to-a-long-random-secret
MORRENUS_API_KEY=your-morrenus-api-key
PORT=3000
```

- [ ] **Step 4: Create .gitignore**

```
node_modules/
dist/
pwa/dist/
.env
```

- [ ] **Step 5: Install deps**

```bash
cd LuDownloader.Server && npm install
```

Expected: `node_modules/` created, no errors.

- [ ] **Step 6: Commit**

```bash
git add LuDownloader.Server/package.json LuDownloader.Server/tsconfig.json LuDownloader.Server/.env.example LuDownloader.Server/.gitignore
git commit -m "feat(server): scaffold LuDownloader.Server package"
```

---

### Task 2: Database Schema + Client

**Files:**
- Create: `LuDownloader.Server/src/db/schema.ts`
- Create: `LuDownloader.Server/src/db/client.ts`
- Create: `LuDownloader.Server/src/db/ensureTables.ts`

- [ ] **Step 1: Create src/db/schema.ts**

```typescript
import {
  pgTable, text, uuid, integer, jsonb, boolean, bigint, timestamp
} from 'drizzle-orm/pg-core'

export const tasks = pgTable('tasks', {
  id:          uuid('id').primaryKey().defaultRandom(),
  type:        text('type').notNull(),
  payload:     jsonb('payload').notNull(),
  status:      text('status').notNull().default('pending'),
  progress:    integer('progress').default(0),
  logTail:     text('log_tail').array().default([]),
  error:       text('error'),
  createdAt:   timestamp('created_at').defaultNow().notNull(),
  startedAt:   timestamp('started_at'),
  completedAt: timestamp('completed_at'),
})

export const installedGames = pgTable('installed_games', {
  appId:               text('app_id').primaryKey(),
  gameName:            text('game_name').notNull(),
  installPath:         text('install_path'),
  libraryPath:         text('library_path'),
  installDir:          text('install_dir'),
  installedDate:       text('installed_date'),
  sizeOnDisk:          bigint('size_on_disk', { mode: 'number' }),
  selectedDepots:      text('selected_depots').array(),
  drmStripped:         boolean('drm_stripped').default(false),
  registeredWithSteam: boolean('registered_with_steam').default(false),
  headerImageUrl:      text('header_image_url'),
  executablePath:      text('executable_path'),
  syncedAt:            timestamp('synced_at').defaultNow(),
})

export const savedLibrary = pgTable('saved_library', {
  appId:          text('app_id').primaryKey(),
  gameName:       text('game_name').notNull(),
  addedDate:      text('added_date'),
  headerImageUrl: text('header_image_url'),
})
```

- [ ] **Step 2: Create src/db/client.ts**

```typescript
import 'dotenv/config'
import { drizzle } from 'drizzle-orm/node-postgres'
import pg from 'pg'
import * as schema from './schema.js'

const pool = new pg.Pool({ connectionString: process.env.DATABASE_URL })
export const db = drizzle(pool, { schema })
```

- [ ] **Step 3: Create src/db/ensureTables.ts**

```typescript
import { db } from './client.js'
import { sql } from 'drizzle-orm'

export async function ensureTables(): Promise<void> {
  await db.execute(sql`
    CREATE TABLE IF NOT EXISTS tasks (
      id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
      type TEXT NOT NULL,
      payload JSONB NOT NULL,
      status TEXT NOT NULL DEFAULT 'pending',
      progress INTEGER DEFAULT 0,
      log_tail TEXT[] DEFAULT '{}',
      error TEXT,
      created_at TIMESTAMPTZ DEFAULT NOW() NOT NULL,
      started_at TIMESTAMPTZ,
      completed_at TIMESTAMPTZ
    )
  `)

  await db.execute(sql`
    CREATE TABLE IF NOT EXISTS installed_games (
      app_id TEXT PRIMARY KEY,
      game_name TEXT NOT NULL,
      install_path TEXT,
      library_path TEXT,
      install_dir TEXT,
      installed_date TEXT,
      size_on_disk BIGINT,
      selected_depots TEXT[],
      drm_stripped BOOLEAN DEFAULT FALSE,
      registered_with_steam BOOLEAN DEFAULT FALSE,
      header_image_url TEXT,
      executable_path TEXT,
      synced_at TIMESTAMPTZ DEFAULT NOW()
    )
  `)

  await db.execute(sql`
    CREATE TABLE IF NOT EXISTS saved_library (
      app_id TEXT PRIMARY KEY,
      game_name TEXT NOT NULL,
      added_date TEXT,
      header_image_url TEXT
    )
  `)

  console.log('DB tables ensured')
}
```

- [ ] **Step 4: Commit**

```bash
git add LuDownloader.Server/src/db/
git commit -m "feat(server): add Drizzle schema, db client, and ensureTables"
```

---

### Task 3: Shared Types

**Files:**
- Create: `LuDownloader.Server/src/types.ts`

- [ ] **Step 1: Create src/types.ts**

```typescript
// Subset of LuDownloader.Electron/src/shared/types.ts — keep in sync manually.

export interface InstalledGame {
  appId: string
  gameName: string
  installPath: string
  libraryPath: string
  installDir: string
  installedDate: string
  sizeOnDisk: number
  selectedDepots: string[]
  manifestGIDs: Record<string, string>
  drmStripped: boolean
  registeredWithSteam: boolean
  gseSavesCopied: boolean
  headerImageUrl?: string
  steamBuildId?: string
  manifestZipPath?: string
  executablePath?: string
}

export interface SavedLibraryGame {
  appId: string
  gameName: string
  addedDate: string
  headerImageUrl?: string
}

export interface Task {
  id: string
  type: string
  payload: unknown
  status: 'pending' | 'running' | 'completed' | 'failed'
  progress: number | null
  logTail: string[] | null
  error: string | null
  createdAt: Date
  startedAt: Date | null
  completedAt: Date | null
}
```

- [ ] **Step 2: Commit**

```bash
git add LuDownloader.Server/src/types.ts
git commit -m "feat(server): add shared types"
```

---

### Task 4: Auth Middleware

**Files:**
- Create: `LuDownloader.Server/src/middleware/auth.ts`

- [ ] **Step 1: Create src/middleware/auth.ts**

```typescript
import type { MiddlewareHandler } from 'hono'

export const apiKeyAuth: MiddlewareHandler = async (c, next) => {
  const key = c.req.header('X-Api-Key')
  if (!process.env.API_KEY || key !== process.env.API_KEY) {
    return c.json({ error: 'Unauthorized' }, 401)
  }
  await next()
}
```

- [ ] **Step 2: Commit**

```bash
git add LuDownloader.Server/src/middleware/
git commit -m "feat(server): add API key auth middleware"
```

---

### Task 5: Library Routes

**Files:**
- Create: `LuDownloader.Server/src/routes/library.ts`

- [ ] **Step 1: Create src/routes/library.ts**

```typescript
import { Hono } from 'hono'
import { db } from '../db/client.js'
import { installedGames, savedLibrary } from '../db/schema.js'
import { notInArray } from 'drizzle-orm'
import type { InstalledGame, SavedLibraryGame } from '../types.js'

export const libraryRouter = new Hono()

libraryRouter.get('/', async (c) => {
  const [installed, saved] = await Promise.all([
    db.select().from(installedGames),
    db.select().from(savedLibrary),
  ])
  return c.json({ installed, saved })
})

libraryRouter.post('/sync', async (c) => {
  const { installed, saved } = await c.req.json<{
    installed: InstalledGame[]
    saved: SavedLibraryGame[]
  }>()

  await db.transaction(async (tx) => {
    if (installed.length > 0) {
      for (const game of installed) {
        await tx.insert(installedGames).values({
          appId: game.appId,
          gameName: game.gameName,
          installPath: game.installPath,
          libraryPath: game.libraryPath,
          installDir: game.installDir,
          installedDate: game.installedDate,
          sizeOnDisk: game.sizeOnDisk,
          selectedDepots: game.selectedDepots,
          drmStripped: game.drmStripped,
          registeredWithSteam: game.registeredWithSteam,
          headerImageUrl: game.headerImageUrl,
          executablePath: game.executablePath,
        }).onConflictDoUpdate({
          target: installedGames.appId,
          set: {
            gameName: game.gameName,
            installPath: game.installPath,
            libraryPath: game.libraryPath,
            installDir: game.installDir,
            installedDate: game.installedDate,
            sizeOnDisk: game.sizeOnDisk,
            selectedDepots: game.selectedDepots,
            drmStripped: game.drmStripped,
            registeredWithSteam: game.registeredWithSteam,
            headerImageUrl: game.headerImageUrl,
            executablePath: game.executablePath,
          },
        })
      }
      const ids = installed.map((g) => g.appId)
      await tx.delete(installedGames).where(notInArray(installedGames.appId, ids))
    } else {
      await tx.delete(installedGames)
    }

    if (saved.length > 0) {
      for (const game of saved) {
        await tx.insert(savedLibrary).values({
          appId: game.appId,
          gameName: game.gameName,
          addedDate: game.addedDate,
          headerImageUrl: game.headerImageUrl,
        }).onConflictDoUpdate({
          target: savedLibrary.appId,
          set: {
            gameName: game.gameName,
            addedDate: game.addedDate,
            headerImageUrl: game.headerImageUrl,
          },
        })
      }
      const ids = saved.map((g) => g.appId)
      await tx.delete(savedLibrary).where(notInArray(savedLibrary.appId, ids))
    } else {
      await tx.delete(savedLibrary)
    }
  })

  return c.json({ ok: true })
})

export default libraryRouter
```

- [ ] **Step 2: Commit**

```bash
git add LuDownloader.Server/src/routes/library.ts
git commit -m "feat(server): add library sync routes"
```

---

### Task 6: Tasks Routes

**Files:**
- Create: `LuDownloader.Server/src/routes/tasks.ts`

- [ ] **Step 1: Create src/routes/tasks.ts**

```typescript
import { Hono } from 'hono'
import { db } from '../db/client.js'
import { tasks } from '../db/schema.js'
import { and, eq } from 'drizzle-orm'

export const tasksRouter = new Hono()

tasksRouter.get('/', async (c) => {
  const status = c.req.query('status')
  const rows = status
    ? await db.select().from(tasks).where(eq(tasks.status, status))
    : await db.select().from(tasks)
  return c.json(rows)
})

tasksRouter.post('/', async (c) => {
  const { type, payload } = await c.req.json<{ type: string; payload: unknown }>()
  const [task] = await db
    .insert(tasks)
    .values({ type, payload: payload as Record<string, unknown>, status: 'pending', progress: 0, logTail: [] })
    .returning()
  return c.json(task, 201)
})

tasksRouter.patch('/:id', async (c) => {
  const id = c.req.param('id')
  const body = await c.req.json<{
    status?: string
    progress?: number
    logTail?: string[]
    error?: string
  }>()

  const updates: Record<string, unknown> = {}
  if (body.status !== undefined) {
    updates.status = body.status
    if (body.status === 'running') updates.startedAt = new Date()
    if (body.status === 'completed' || body.status === 'failed') updates.completedAt = new Date()
  }
  if (body.progress !== undefined) updates.progress = body.progress
  if (body.logTail !== undefined) updates.logTail = body.logTail
  if (body.error !== undefined) updates.error = body.error

  const [task] = await db.update(tasks).set(updates).where(eq(tasks.id, id)).returning()
  if (!task) return c.json({ error: 'Not found' }, 404)
  return c.json(task)
})

tasksRouter.delete('/:id', async (c) => {
  const id = c.req.param('id')
  const [task] = await db
    .delete(tasks)
    .where(and(eq(tasks.id, id), eq(tasks.status, 'pending')))
    .returning()
  if (!task) return c.json({ error: 'Task not found or not cancellable' }, 404)
  return c.json({ ok: true })
})

export default tasksRouter
```

- [ ] **Step 2: Commit**

```bash
git add LuDownloader.Server/src/routes/tasks.ts
git commit -m "feat(server): add task queue CRUD routes"
```

---

### Task 7: Search Proxy Route

**Files:**
- Create: `LuDownloader.Server/src/routes/search.ts`

Morrenus API key is stored in the backend env var `MORRENUS_API_KEY`. The PWA calls this proxy instead of Morrenus directly (PWA doesn't have credentials).

- [ ] **Step 1: Create src/routes/search.ts**

```typescript
import { Hono } from 'hono'

const MORRENUS_BASE = 'https://manifest.morrenus.xyz/api/v1'

export const searchRouter = new Hono()

searchRouter.get('/', async (c) => {
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

export default searchRouter
```

- [ ] **Step 2: Commit**

```bash
git add LuDownloader.Server/src/routes/search.ts
git commit -m "feat(server): add Morrenus search proxy route"
```

---

### Task 8: Server Entry Point

**Files:**
- Create: `LuDownloader.Server/src/index.ts`

- [ ] **Step 1: Create src/index.ts**

```typescript
import 'dotenv/config'
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

// Serve PWA from pwa/dist (built separately)
app.use('/*', serveStatic({ root: './pwa/dist' }))

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
```

- [ ] **Step 2: Verify it compiles**

```bash
cd LuDownloader.Server && npx tsc --noEmit
```

Expected: no errors.

- [ ] **Step 3: Commit**

```bash
git add LuDownloader.Server/src/index.ts
git commit -m "feat(server): add Hono server entry point"
```

---

### Task 9: Railway Config + Local Test

**Files:**
- Create: `LuDownloader.Server/railway.json`

- [ ] **Step 1: Create railway.json**

```json
{
  "$schema": "https://railway.app/railway.schema.json",
  "build": {
    "builder": "NIXPACKS"
  },
  "deploy": {
    "startCommand": "node dist/index.js"
  }
}
```

- [ ] **Step 2: Copy .env.example to .env and fill in a local Postgres URL**

Create `LuDownloader.Server/.env`:
```
DATABASE_URL=postgresql://postgres:postgres@localhost:5432/ludownloader
API_KEY=test-secret
MORRENUS_API_KEY=<your key>
PORT=3000
```

(Use any Postgres instance you have, or Railway's generated URL for testing.)

- [ ] **Step 3: Start dev server**

```bash
cd LuDownloader.Server && npm run dev
```

Expected output:
```
DB tables ensured
LuDownloader Server on port 3000
```

- [ ] **Step 4: Test health endpoint**

```bash
curl http://localhost:3000/api/health
```

Expected: `{"ok":true,"version":"0.1.0"}`

- [ ] **Step 5: Test auth**

```bash
curl http://localhost:3000/api/library
```

Expected: `{"error":"Unauthorized"}` (401)

```bash
curl -H "X-Api-Key: test-secret" http://localhost:3000/api/library
```

Expected: `{"installed":[],"saved":[]}`

- [ ] **Step 6: Test task queue**

```bash
curl -X POST http://localhost:3000/api/tasks \
  -H "X-Api-Key: test-secret" \
  -H "Content-Type: application/json" \
  -d '{"type":"check_updates","payload":{}}'
```

Expected: task object with `"status":"pending"`.

```bash
curl "http://localhost:3000/api/tasks?status=pending" -H "X-Api-Key: test-secret"
```

Expected: array with that task.

- [ ] **Step 7: Commit**

```bash
git add LuDownloader.Server/railway.json
git commit -m "feat(server): add Railway deploy config"
```

---

## Phase 2: PWA

### Task 10: PWA Project Scaffold

**Files:**
- Create: `LuDownloader.Server/pwa/package.json`
- Create: `LuDownloader.Server/pwa/index.html`
- Create: `LuDownloader.Server/pwa/vite.config.ts`
- Create: `LuDownloader.Server/pwa/tsconfig.json`

- [ ] **Step 1: Create pwa/package.json**

```json
{
  "name": "ludownloader-pwa",
  "version": "0.1.0",
  "type": "module",
  "scripts": {
    "dev": "vite",
    "build": "tsc && vite build",
    "preview": "vite preview"
  },
  "dependencies": {
    "react": "^18.3.1",
    "react-dom": "^18.3.1",
    "react-router-dom": "^6.28.0"
  },
  "devDependencies": {
    "@types/react": "^18.3.0",
    "@types/react-dom": "^18.3.0",
    "@vitejs/plugin-react": "^4.3.4",
    "typescript": "^5.7.3",
    "vite": "^6.0.7"
  }
}
```

- [ ] **Step 2: Create pwa/index.html**

```html
<!DOCTYPE html>
<html lang="en">
  <head>
    <meta charset="UTF-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <meta name="theme-color" content="#0B0C0F" />
    <title>LuDownloader</title>
  </head>
  <body style="margin:0;background:#0B0C0F">
    <div id="root"></div>
    <script type="module" src="/src/main.tsx"></script>
  </body>
</html>
```

- [ ] **Step 3: Create pwa/vite.config.ts**

```typescript
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  build: { outDir: 'dist' },
})
```

- [ ] **Step 4: Create pwa/tsconfig.json**

```json
{
  "compilerOptions": {
    "target": "ESNext",
    "useDefineForClassFields": true,
    "lib": ["ESNext", "DOM", "DOM.Iterable"],
    "module": "ESNext",
    "skipLibCheck": true,
    "moduleResolution": "bundler",
    "allowImportingTsExtensions": true,
    "resolveJsonModule": true,
    "isolatedModules": true,
    "noEmit": true,
    "jsx": "react-jsx",
    "strict": true
  },
  "include": ["src"]
}
```

- [ ] **Step 5: Install deps**

```bash
cd LuDownloader.Server/pwa && npm install
```

- [ ] **Step 6: Commit**

```bash
git add LuDownloader.Server/pwa/
git commit -m "feat(pwa): scaffold Vite React PWA"
```

---

### Task 11: PWA API Client

**Files:**
- Create: `LuDownloader.Server/pwa/src/api.ts`

- [ ] **Step 1: Create pwa/src/api.ts**

```typescript
export interface Task {
  id: string
  type: string
  payload: unknown
  status: 'pending' | 'running' | 'completed' | 'failed'
  progress: number | null
  log_tail: string[] | null
  error: string | null
  created_at: string
  started_at: string | null
  completed_at: string | null
}

export interface InstalledGame {
  app_id: string
  game_name: string
  install_path: string | null
  library_path: string | null
  install_dir: string | null
  installed_date: string | null
  size_on_disk: number | null
  selected_depots: string[] | null
  drm_stripped: boolean | null
  registered_with_steam: boolean | null
  header_image_url: string | null
  executable_path: string | null
}

export interface SavedGame {
  app_id: string
  game_name: string
  added_date: string | null
  header_image_url: string | null
}

export interface LibraryResponse {
  installed: InstalledGame[]
  saved: SavedGame[]
}

export interface MorrenusSearchResult {
  gameId: string
  gameName: string
  headerImageUrl?: string
  relevanceScore?: number
}

export interface MorrenusSearchResponse {
  results: MorrenusSearchResult[]
  total: number
  label: string
}

function config(): { serverUrl: string; apiKey: string } {
  return {
    serverUrl: (localStorage.getItem('serverUrl') ?? '').replace(/\/$/, ''),
    apiKey: localStorage.getItem('apiKey') ?? '',
  }
}

async function apiFetch<T>(path: string, init?: RequestInit): Promise<T> {
  const { serverUrl, apiKey } = config()
  if (!serverUrl) throw new Error('Server URL not configured. Go to Settings.')
  const res = await fetch(`${serverUrl}${path}`, {
    ...init,
    headers: {
      'Content-Type': 'application/json',
      'X-Api-Key': apiKey,
      ...init?.headers,
    },
  })
  if (!res.ok) {
    const body = await res.text().catch(() => '')
    throw new Error(`${res.status}: ${body || res.statusText}`)
  }
  return res.json() as Promise<T>
}

export const api = {
  health: () => apiFetch<{ ok: boolean; version: string }>('/api/health'),
  library: () => apiFetch<LibraryResponse>('/api/library'),
  tasks: (status?: string) =>
    apiFetch<Task[]>(`/api/tasks${status ? `?status=${encodeURIComponent(status)}` : ''}`),
  createTask: (type: string, payload: unknown) =>
    apiFetch<Task>('/api/tasks', {
      method: 'POST',
      body: JSON.stringify({ type, payload }),
    }),
  cancelTask: (id: string) =>
    apiFetch<{ ok: boolean }>(`/api/tasks/${id}`, { method: 'DELETE' }),
  search: (q: string, mode: 'games' | 'dlc' = 'games') =>
    apiFetch<MorrenusSearchResponse>(`/api/search?q=${encodeURIComponent(q)}&mode=${mode}`),
}
```

- [ ] **Step 2: Commit**

```bash
git add LuDownloader.Server/pwa/src/api.ts
git commit -m "feat(pwa): add typed API client"
```

---

### Task 12: PWA Settings View

**Files:**
- Create: `LuDownloader.Server/pwa/src/views/SettingsView.tsx`

- [ ] **Step 1: Create pwa/src/views/SettingsView.tsx**

```tsx
import { useState, useEffect } from 'react'
import { api } from '../api'

const S = {
  page: { padding: 16 } as React.CSSProperties,
  label: { display: 'block', marginBottom: 4, fontSize: 13, color: '#9ca3af' } as React.CSSProperties,
  input: {
    width: '100%', padding: '8px 12px', background: '#141519',
    border: '1px solid #1e2029', borderRadius: 6, color: '#e2e8f0',
    outline: 'none', fontSize: 14, marginBottom: 12, boxSizing: 'border-box' as const,
  } as React.CSSProperties,
  btn: {
    padding: '10px 20px', background: '#3b82f6', border: 'none',
    borderRadius: 6, color: '#fff', cursor: 'pointer', fontSize: 14,
  } as React.CSSProperties,
}

export default function SettingsView() {
  const [serverUrl, setServerUrl] = useState('')
  const [apiKey, setApiKey] = useState('')
  const [status, setStatus] = useState<string | null>(null)

  useEffect(() => {
    setServerUrl(localStorage.getItem('serverUrl') ?? '')
    setApiKey(localStorage.getItem('apiKey') ?? '')
  }, [])

  const save = () => {
    localStorage.setItem('serverUrl', serverUrl.replace(/\/$/, ''))
    localStorage.setItem('apiKey', apiKey)
    setStatus('Saved.')
    setTimeout(() => setStatus(null), 2000)
  }

  const test = async () => {
    setStatus('Testing...')
    try {
      const res = await api.health()
      setStatus(`Connected — server v${res.version}`)
    } catch (e) {
      setStatus(`Failed: ${e instanceof Error ? e.message : String(e)}`)
    }
  }

  return (
    <div style={S.page}>
      <h1 style={{ fontSize: 18, fontWeight: 600, marginBottom: 20 }}>Settings</h1>
      <label style={S.label}>Server URL</label>
      <input style={S.input} value={serverUrl} onChange={e => setServerUrl(e.target.value)} placeholder="https://your-app.railway.app" />
      <label style={S.label}>API Key</label>
      <input style={{ ...S.input, fontFamily: 'monospace' }} type="password" value={apiKey} onChange={e => setApiKey(e.target.value)} placeholder="your-secret-key" />
      <div style={{ display: 'flex', gap: 8 }}>
        <button style={S.btn} onClick={save}>Save</button>
        <button style={{ ...S.btn, background: '#1e2029', color: '#9ca3af' }} onClick={test}>Test Connection</button>
      </div>
      {status && <div style={{ marginTop: 12, fontSize: 13, color: '#9ca3af' }}>{status}</div>}
    </div>
  )
}
```

- [ ] **Step 2: Commit**

```bash
git add LuDownloader.Server/pwa/src/views/SettingsView.tsx
git commit -m "feat(pwa): add Settings view"
```

---

### Task 13: PWA Library View

**Files:**
- Create: `LuDownloader.Server/pwa/src/views/LibraryView.tsx`

- [ ] **Step 1: Create pwa/src/views/LibraryView.tsx**

```tsx
import { useEffect, useState } from 'react'
import { api, type InstalledGame, type SavedGame } from '../api'

const S = {
  card: {
    background: '#141519', borderRadius: 8, padding: 12,
    marginBottom: 8, display: 'flex', gap: 12, alignItems: 'center',
  } as React.CSSProperties,
  tab: (active: boolean): React.CSSProperties => ({
    padding: '6px 16px', borderRadius: 6, border: 'none', cursor: 'pointer',
    background: active ? '#3b82f6' : '#1e2029',
    color: active ? '#fff' : '#9ca3af', fontSize: 13,
  }),
}

function formatBytes(n: number | null): string {
  if (!n) return ''
  return `${Math.round(n / 1e9 * 10) / 10} GB`
}

export default function LibraryView() {
  const [installed, setInstalled] = useState<InstalledGame[]>([])
  const [saved, setSaved] = useState<SavedGame[]>([])
  const [tab, setTab] = useState<'installed' | 'saved'>('installed')
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    api.library()
      .then(data => { setInstalled(data.installed); setSaved(data.saved) })
      .catch(e => setError(e.message))
      .finally(() => setLoading(false))
  }, [])

  if (loading) return <div style={{ padding: 20, color: '#6b7280' }}>Loading...</div>
  if (error) return <div style={{ padding: 20, color: '#ef4444' }}>Error: {error}</div>

  const items = tab === 'installed' ? installed : saved

  return (
    <div style={{ padding: 16 }}>
      <h1 style={{ fontSize: 18, fontWeight: 600, marginBottom: 12 }}>Library</h1>
      <div style={{ display: 'flex', gap: 8, marginBottom: 16 }}>
        <button style={S.tab(tab === 'installed')} onClick={() => setTab('installed')}>
          Installed ({installed.length})
        </button>
        <button style={S.tab(tab === 'saved')} onClick={() => setTab('saved')}>
          Saved ({saved.length})
        </button>
      </div>

      {items.map(g => {
        const name = 'game_name' in g ? g.game_name : ''
        const img = 'header_image_url' in g ? g.header_image_url : null
        const sub = tab === 'installed'
          ? formatBytes((g as InstalledGame).size_on_disk)
          : (g as SavedGame).app_id
        return (
          <div key={g.app_id} style={S.card}>
            {img && <img src={img} alt="" style={{ width: 80, height: 45, objectFit: 'cover', borderRadius: 4 }} />}
            <div>
              <div style={{ fontWeight: 500 }}>{name}</div>
              <div style={{ fontSize: 12, color: '#6b7280' }}>{sub}</div>
            </div>
          </div>
        )
      })}

      {items.length === 0 && (
        <div style={{ color: '#6b7280', textAlign: 'center', marginTop: 40 }}>
          {tab === 'installed' ? 'No games synced yet. Start the desktop app.' : 'No saved games.'}
        </div>
      )}
    </div>
  )
}
```

- [ ] **Step 2: Commit**

```bash
git add LuDownloader.Server/pwa/src/views/LibraryView.tsx
git commit -m "feat(pwa): add Library view"
```

---

### Task 14: PWA Tasks View

**Files:**
- Create: `LuDownloader.Server/pwa/src/views/TasksView.tsx`

- [ ] **Step 1: Create pwa/src/views/TasksView.tsx**

```tsx
import { useCallback, useEffect, useState } from 'react'
import { api, type Task } from '../api'

const STATUS_COLOR: Record<string, string> = {
  pending: '#f59e0b',
  running: '#3b82f6',
  completed: '#10b981',
  failed: '#ef4444',
}

export default function TasksView() {
  const [tasks, setTasks] = useState<Task[]>([])
  const [loading, setLoading] = useState(true)

  const refresh = useCallback(() => {
    api.tasks()
      .then(setTasks)
      .catch(console.error)
      .finally(() => setLoading(false))
  }, [])

  useEffect(() => {
    refresh()
    const id = setInterval(refresh, 10_000)
    return () => clearInterval(id)
  }, [refresh])

  const cancel = async (id: string) => {
    await api.cancelTask(id).catch(console.error)
    refresh()
  }

  if (loading) return <div style={{ padding: 20, color: '#6b7280' }}>Loading...</div>

  const sorted = [...tasks].sort(
    (a, b) => new Date(b.created_at).getTime() - new Date(a.created_at).getTime()
  )

  return (
    <div style={{ padding: 16 }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 12 }}>
        <h1 style={{ fontSize: 18, fontWeight: 600 }}>Tasks</h1>
        <button onClick={refresh} style={{ padding: '4px 12px', background: '#1e2029', border: 'none', color: '#9ca3af', borderRadius: 6, cursor: 'pointer', fontSize: 13 }}>
          Refresh
        </button>
      </div>

      {sorted.map(task => (
        <div key={task.id} style={{ background: '#141519', borderRadius: 8, padding: 12, marginBottom: 8 }}>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
            <span style={{ fontWeight: 500 }}>{task.type.replace(/_/g, ' ')}</span>
            <span style={{ fontSize: 12, color: STATUS_COLOR[task.status] ?? '#6b7280', textTransform: 'capitalize' }}>
              {task.status}
            </span>
          </div>
          {task.status === 'running' && (
            <div style={{ marginTop: 8, background: '#0f1015', borderRadius: 4, overflow: 'hidden', height: 6 }}>
              <div style={{ width: `${task.progress ?? 0}%`, height: '100%', background: '#3b82f6', transition: 'width 0.3s' }} />
            </div>
          )}
          {task.log_tail && task.log_tail.length > 0 && (
            <div style={{ fontSize: 11, color: '#6b7280', marginTop: 6, fontFamily: 'monospace', wordBreak: 'break-all' }}>
              {task.log_tail[task.log_tail.length - 1]}
            </div>
          )}
          {task.error && <div style={{ fontSize: 12, color: '#ef4444', marginTop: 4 }}>{task.error}</div>}
          {task.status === 'pending' && (
            <button onClick={() => cancel(task.id)} style={{ marginTop: 8, padding: '4px 10px', background: '#1e2029', border: 'none', color: '#ef4444', borderRadius: 4, cursor: 'pointer', fontSize: 12 }}>
              Cancel
            </button>
          )}
          <div style={{ fontSize: 11, color: '#374151', marginTop: 4 }}>
            {new Date(task.created_at).toLocaleString()}
          </div>
        </div>
      ))}

      {tasks.length === 0 && (
        <div style={{ color: '#6b7280', textAlign: 'center', marginTop: 40 }}>No tasks yet.</div>
      )}
    </div>
  )
}
```

- [ ] **Step 2: Commit**

```bash
git add LuDownloader.Server/pwa/src/views/TasksView.tsx
git commit -m "feat(pwa): add Tasks view with progress bars"
```

---

### Task 15: PWA Search View

**Files:**
- Create: `LuDownloader.Server/pwa/src/views/SearchView.tsx`

The Search view proxies through the backend's `/api/search` route (which uses the server's `MORRENUS_API_KEY`). Queuing a game creates a `download_manifest` task — the desktop will download the manifest ZIP and add the game to the saved library.

- [ ] **Step 1: Create pwa/src/views/SearchView.tsx**

```tsx
import { useState } from 'react'
import { api, type MorrenusSearchResult } from '../api'

export default function SearchView() {
  const [query, setQuery] = useState('')
  const [results, setResults] = useState<MorrenusSearchResult[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [queued, setQueued] = useState<Set<string>>(new Set())

  const search = async () => {
    if (!query.trim()) return
    setLoading(true)
    setError(null)
    try {
      const data = await api.search(query)
      setResults(data.results ?? [])
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Search failed')
    } finally {
      setLoading(false)
    }
  }

  const queue = async (game: MorrenusSearchResult) => {
    try {
      await api.createTask('download_manifest', {
        appId: game.gameId,
        gameName: game.gameName,
        headerImageUrl: game.headerImageUrl,
      })
      setQueued(prev => new Set([...prev, game.gameId]))
    } catch (e) {
      alert(e instanceof Error ? e.message : 'Failed to queue')
    }
  }

  return (
    <div style={{ padding: 16 }}>
      <h1 style={{ fontSize: 18, fontWeight: 600, marginBottom: 12 }}>Search</h1>
      <div style={{ display: 'flex', gap: 8, marginBottom: 16 }}>
        <input
          value={query}
          onChange={e => setQuery(e.target.value)}
          onKeyDown={e => e.key === 'Enter' && search()}
          placeholder="Search games..."
          style={{
            flex: 1, padding: '8px 12px', background: '#141519',
            border: '1px solid #1e2029', borderRadius: 6, color: '#e2e8f0', outline: 'none',
          }}
        />
        <button
          onClick={search}
          disabled={loading}
          style={{ padding: '8px 16px', background: '#3b82f6', border: 'none', borderRadius: 6, color: '#fff', cursor: 'pointer' }}
        >
          {loading ? '…' : 'Search'}
        </button>
      </div>

      {error && <div style={{ color: '#ef4444', marginBottom: 12, fontSize: 14 }}>{error}</div>}

      {results.map(game => (
        <div key={game.gameId} style={{ background: '#141519', borderRadius: 8, padding: 12, marginBottom: 8, display: 'flex', gap: 12, alignItems: 'center' }}>
          {game.headerImageUrl && (
            <img src={game.headerImageUrl} alt="" style={{ width: 80, height: 45, objectFit: 'cover', borderRadius: 4, flexShrink: 0 }} />
          )}
          <div style={{ flex: 1, minWidth: 0 }}>
            <div style={{ fontWeight: 500, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{game.gameName}</div>
            <div style={{ fontSize: 12, color: '#6b7280' }}>{game.gameId}</div>
          </div>
          <button
            onClick={() => queue(game)}
            disabled={queued.has(game.gameId)}
            style={{
              padding: '6px 12px', border: 'none', borderRadius: 6, fontSize: 13, cursor: queued.has(game.gameId) ? 'default' : 'pointer',
              background: queued.has(game.gameId) ? '#1e2029' : '#3b82f6',
              color: queued.has(game.gameId) ? '#6b7280' : '#fff',
              flexShrink: 0,
            }}
          >
            {queued.has(game.gameId) ? 'Queued' : 'Queue'}
          </button>
        </div>
      ))}

      {results.length === 0 && !loading && query && (
        <div style={{ color: '#6b7280', textAlign: 'center', marginTop: 40 }}>No results</div>
      )}
    </div>
  )
}
```

- [ ] **Step 2: Commit**

```bash
git add LuDownloader.Server/pwa/src/views/SearchView.tsx
git commit -m "feat(pwa): add Search view"
```

---

### Task 16: PWA App Shell + Local Test

**Files:**
- Create: `LuDownloader.Server/pwa/src/main.tsx`
- Create: `LuDownloader.Server/pwa/src/App.tsx`

- [ ] **Step 1: Create pwa/src/main.tsx**

```tsx
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import App from './App'

const style = document.createElement('style')
style.textContent = `
  * { box-sizing: border-box; margin: 0; padding: 0; }
  body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif; color: #e2e8f0; }
  input, button, select { font-family: inherit; }
`
document.head.appendChild(style)

createRoot(document.getElementById('root')!).render(
  <StrictMode><App /></StrictMode>
)
```

- [ ] **Step 2: Create pwa/src/App.tsx**

```tsx
import { BrowserRouter, NavLink, Route, Routes } from 'react-router-dom'
import LibraryView from './views/LibraryView'
import TasksView from './views/TasksView'
import SearchView from './views/SearchView'
import SettingsView from './views/SettingsView'

function navStyle(isActive: boolean): React.CSSProperties {
  return {
    flex: 1, padding: '12px 0', textAlign: 'center',
    color: isActive ? '#e2e8f0' : '#6b7280',
    textDecoration: 'none', fontSize: 12, display: 'block',
  }
}

export default function App() {
  return (
    <BrowserRouter>
      <div style={{ background: '#0B0C0F', minHeight: '100dvh', paddingBottom: 60 }}>
        <Routes>
          <Route path="/" element={<LibraryView />} />
          <Route path="/tasks" element={<TasksView />} />
          <Route path="/search" element={<SearchView />} />
          <Route path="/settings" element={<SettingsView />} />
        </Routes>
        <nav style={{ display: 'flex', position: 'fixed', bottom: 0, left: 0, right: 0, background: '#0f1015', borderTop: '1px solid #1e2029' }}>
          <NavLink to="/" style={({ isActive }) => navStyle(isActive)} end>Library</NavLink>
          <NavLink to="/tasks" style={({ isActive }) => navStyle(isActive)}>Tasks</NavLink>
          <NavLink to="/search" style={({ isActive }) => navStyle(isActive)}>Search</NavLink>
          <NavLink to="/settings" style={({ isActive }) => navStyle(isActive)}>Settings</NavLink>
        </nav>
      </div>
    </BrowserRouter>
  )
}
```

- [ ] **Step 3: Run PWA dev server**

```bash
cd LuDownloader.Server/pwa && npm run dev
```

Open `http://localhost:5173` in a browser. Expected: dark-themed app with 4 nav tabs. Library tab shows "No games synced yet."

- [ ] **Step 4: Go to Settings, enter the local backend URL (`http://localhost:3000`) and API key, click Test Connection**

Expected: "Connected — server v0.1.0"

- [ ] **Step 5: Commit**

```bash
git add LuDownloader.Server/pwa/src/
git commit -m "feat(pwa): add App shell with nav and all views wired up"
```

---

## Phase 3: Electron Integration

### Task 17: Add Cloud Settings to AppSettings

**Files:**
- Modify: `LuDownloader.Electron/src/shared/types.ts`
- Modify: `LuDownloader.Electron/src/main/ipc/settings.ts`

- [ ] **Step 1: Add fields to AppSettings in types.ts**

In `LuDownloader.Electron/src/shared/types.ts`, add to `AppSettings`:

```typescript
export interface AppSettings {
  apiKey: string;
  downloadPath: string;
  maxDownloads: number;
  steamUsername: string;
  steamWebApiKey: string;
  igdbClientId: string;
  igdbClientSecret: string;
  goldbergFilesPath: string;
  goldbergAccountName: string;
  goldbergSteamId: string;
  cloudServerUrl: string;   // ← new
  cloudApiKey: string;      // ← new
}
```

- [ ] **Step 2: Update defaultSettings in settings.ts**

In `LuDownloader.Electron/src/main/ipc/settings.ts`, add to `defaultSettings`:

```typescript
export const defaultSettings: AppSettings = {
  apiKey: '',
  downloadPath: '',
  maxDownloads: 20,
  steamUsername: '',
  steamWebApiKey: '',
  igdbClientId: '',
  igdbClientSecret: '',
  goldbergFilesPath: '',
  goldbergAccountName: '',
  goldbergSteamId: '',
  cloudServerUrl: '',    // ← new
  cloudApiKey: '',       // ← new
};
```

- [ ] **Step 3: Update loadSettings() in settings.ts**

Add these two lines to the `mapped` object in `loadSettings()`:

```typescript
cloudServerUrl: pickString(loaded, 'cloudServerUrl', 'CloudServerUrl'),
cloudApiKey: pickString(loaded, 'cloudApiKey', 'CloudApiKey'),
```

- [ ] **Step 4: Update saveSettings() in settings.ts**

Add to the `writeJson` call in `saveSettings()`:

```typescript
CloudServerUrl: merged.cloudServerUrl,
CloudApiKey: merged.cloudApiKey,
```

- [ ] **Step 5: Verify types compile**

```bash
cd LuDownloader.Electron && npx tsc --noEmit
```

Expected: no errors.

- [ ] **Step 6: Commit**

```bash
git add LuDownloader.Electron/src/shared/types.ts LuDownloader.Electron/src/main/ipc/settings.ts
git commit -m "feat(electron): add cloudServerUrl and cloudApiKey to AppSettings"
```

---

### Task 18: Add Cloud Settings UI in SettingsModal

**Files:**
- Modify: `LuDownloader.Electron/src/renderer/src/views/SettingsModal.tsx`

- [ ] **Step 1: Read the full SettingsModal.tsx to find the right insertion point**

Read `LuDownloader.Electron/src/renderer/src/views/SettingsModal.tsx` in full to find where the last `<SettingsCard>` ends in the `settings-side-stack` div.

- [ ] **Step 2: Add Cloud section**

After the last `<SettingsCard>` inside `settings-side-stack` (the one for "IGDB (Optional)"), add:

```tsx
<SettingsCard title="Cloud Sync">
  <TextField label="Server URL" value={settings.cloudServerUrl} onChange={(value) => set('cloudServerUrl', value)} placeholder="https://your-app.railway.app" />
  <SecretField label="API Key" value={settings.cloudApiKey} onChange={(value) => set('cloudApiKey', value)} />
  <div className="settings-help">Used by the desktop app to sync with your Railway backend. Leave empty to disable cloud sync.</div>
</SettingsCard>
```

Also update the `empty` const at the top of SettingsModal.tsx to include the new fields:

```typescript
const empty: AppSettings = {
  apiKey: '', downloadPath: '', maxDownloads: 20, steamUsername: '', steamWebApiKey: '',
  igdbClientId: '', igdbClientSecret: '', goldbergFilesPath: '', goldbergAccountName: '', goldbergSteamId: '',
  cloudServerUrl: '', cloudApiKey: '',   // ← add these
};
```

- [ ] **Step 3: Verify renderer compiles**

```bash
cd LuDownloader.Electron && npx tsc --noEmit
```

Expected: no errors.

- [ ] **Step 4: Commit**

```bash
git add LuDownloader.Electron/src/renderer/src/views/SettingsModal.tsx
git commit -m "feat(electron): add Cloud Sync section to settings UI"
```

---

### Task 19: Extract addToSteam from index.ts

`addToSteam` and `ensureManifestGids` are currently private functions in `index.ts`. The sync agent needs to call them directly without going through IPC.

**Files:**
- Modify: `LuDownloader.Electron/src/main/index.ts`
- Modify: `LuDownloader.Electron/src/main/ipc/steam.ts`
- Modify: `LuDownloader.Electron/src/main/ipc/manifest.ts` (only if `ensureManifestGids` moves there)

- [ ] **Step 1: Move ensureManifestGids to manifest.ts**

In `LuDownloader.Electron/src/main/ipc/manifest.ts`, add at the end of the file:

```typescript
import type { InstalledGame, GameData } from '../../shared/types';

export async function ensureManifestGids(game: InstalledGame, zipGameData: GameData): Promise<Record<string, string>> {
  const existing = { ...zipGameData.manifests, ...game.manifestGIDs };
  if (Object.keys(existing).length > 0) {
    return existing;
  }
  const rows = await runManifestChecker([game.appId]);
  if (!rows.length) {
    throw new Error('ManifestChecker returned no manifest rows for this app.');
  }
  return Object.fromEntries(rows
    .filter((row) => row.depotId && row.manifestGid)
    .map((row) => [row.depotId, row.manifestGid]));
}
```

- [ ] **Step 2: Move addToSteam to steam.ts**

In `LuDownloader.Electron/src/main/ipc/steam.ts`, add at the end of the file:

```typescript
import type { InstalledGame, GameData } from '../../shared/types';
import { listInstalled, saveInstalled } from './games';
import { processZip } from './zip';
import { ensureManifestGids } from './manifest';

export async function addToSteam(appId: string): Promise<InstalledGame> {
  const installed = await listInstalled();
  const game = installed.find((entry) => entry.appId === appId);
  if (!game) throw new Error('Installed game record was not found.');

  const zipPath = await resolveManifestZip(game);
  if (!zipPath) throw new Error('Manifest ZIP was not found for this game.');

  const zipGameData = await processZip(zipPath);
  await copyLuaToSteamConfig(zipPath);

  const manifestGIDs = await ensureManifestGids(game, zipGameData);
  const selectedDepots = game.selectedDepots?.length ? game.selectedDepots : Object.keys(manifestGIDs);
  if (!selectedDepots.length) throw new Error('No depot manifests are available for this game.');

  const libraryPath = game.libraryPath || resolveSteamLibraryRoot(game.installPath);
  if (!libraryPath) throw new Error('Steam library path could not be resolved for this install.');

  const gameData: GameData = {
    appId: game.appId,
    gameName: game.gameName || zipGameData.gameName || game.appId,
    installDir: game.installDir,
    buildId: game.steamBuildId || zipGameData.buildId,
    depots: zipGameData.depots || {},
    dlcs: zipGameData.dlcs || {},
    manifests: manifestGIDs,
    selectedDepots,
    manifestZipPath: zipPath,
    headerImageUrl: game.headerImageUrl,
  };
  await writeAcf(gameData, libraryPath);

  return saveInstalled({
    ...game,
    libraryPath,
    manifestZipPath: zipPath,
    manifestGIDs,
    selectedDepots,
    steamBuildId: game.steamBuildId || zipGameData.buildId,
    registeredWithSteam: Boolean(await findAcfPath({ ...game, libraryPath })),
  });
}
```

- [ ] **Step 3: Update index.ts to import from modules and remove local copies**

In `LuDownloader.Electron/src/main/index.ts`:

1. Add `addToSteam` to the import from `./ipc/steam`:
   ```typescript
   import {
     addToSteam,           // ← add this
     copyLuaToSteamConfig,
     findAcfPath,
     getSteamLibraries,
     openPath,
     resolveManifestZip,
     resolveSteamLibraryRoot,
     showItemInFolder,
     writeAcf
   } from './ipc/steam';
   ```

2. Add `ensureManifestGids` to the import from `./ipc/manifest`:
   ```typescript
   import { checkUpdates, deleteCachedManifest, ensureManifestGids, fetchManifestGids, getCachedManifestPath, listCachedManifests, runManifestChecker } from './ipc/manifest';
   ```

3. Delete the local `addToSteam` function (lines 198-248) and the local `ensureManifestGids` function (lines 250-264) from `index.ts`.

- [ ] **Step 4: Verify compilation**

```bash
cd LuDownloader.Electron && npx tsc --noEmit
```

Expected: no errors.

- [ ] **Step 5: Commit**

```bash
git add LuDownloader.Electron/src/main/index.ts LuDownloader.Electron/src/main/ipc/steam.ts LuDownloader.Electron/src/main/ipc/manifest.ts
git commit -m "refactor(electron): extract addToSteam and ensureManifestGids to modules"
```

---

### Task 20: Cloud Sync Agent

**Files:**
- Create: `LuDownloader.Electron/src/main/sync/cloudSync.ts`

- [ ] **Step 1: Create src/main/sync/cloudSync.ts**

```typescript
import type { BrowserWindow } from 'electron';
import { listInstalled, listSavedLibrary, addSavedLibrary } from '../ipc/games';
import { downloadManifest } from '../ipc/morrenus';
import { getCachedManifestPath, checkUpdates } from '../ipc/manifest';
import { addToSteam } from '../ipc/steam';
import { runSteamless } from '../ipc/steamless';
import type { AppSettings } from '../../shared/types';

interface RemoteTask {
  id: string;
  type: string;
  payload: unknown;
  status: string;
}

export class CloudSyncAgent {
  private readonly baseUrl: string;
  private readonly apiKey: string;
  private readonly window: BrowserWindow | null;
  private pollTimer: ReturnType<typeof setInterval> | null = null;

  constructor(settings: Pick<AppSettings, 'cloudServerUrl' | 'cloudApiKey'>, window: BrowserWindow | null = null) {
    this.baseUrl = settings.cloudServerUrl.replace(/\/$/, '');
    this.apiKey = settings.cloudApiKey;
    this.window = window;
  }

  private async fetch<T>(path: string, init?: RequestInit): Promise<T> {
    const res = await fetch(`${this.baseUrl}${path}`, {
      ...init,
      headers: {
        'Content-Type': 'application/json',
        'X-Api-Key': this.apiKey,
        ...init?.headers,
      },
    });
    if (!res.ok) throw new Error(`Cloud API ${res.status} on ${path}`);
    return res.json() as Promise<T>;
  }

  private patchTask(id: string, update: { status?: string; progress?: number; logTail?: string[]; error?: string }): Promise<void> {
    return this.fetch(`/api/tasks/${id}`, { method: 'PATCH', body: JSON.stringify(update) });
  }

  async pushLibrary(): Promise<void> {
    const [installed, saved] = await Promise.all([listInstalled(), listSavedLibrary()]);
    await this.fetch('/api/library/sync', {
      method: 'POST',
      body: JSON.stringify({ installed, saved }),
    });
  }

  async onStartup(): Promise<void> {
    await this.pushLibrary().catch((err) => console.error('Cloud sync push failed:', err));
    const tasks = await this.fetch<RemoteTask[]>('/api/tasks?status=pending').catch(() => []);
    await this.executePendingTasks(tasks);
    this.pollTimer = setInterval(() => void this.poll(), 60_000);
  }

  private async poll(): Promise<void> {
    const tasks = await this.fetch<RemoteTask[]>('/api/tasks?status=pending').catch(() => []);
    await this.executePendingTasks(tasks);
  }

  private async executePendingTasks(tasks: RemoteTask[]): Promise<void> {
    for (const task of tasks) {
      await this.patchTask(task.id, { status: 'running' });
      try {
        await this.executeTask(task);
        await this.patchTask(task.id, { status: 'completed', progress: 100 });
      } catch (err) {
        const error = err instanceof Error ? err.message : String(err);
        await this.patchTask(task.id, { status: 'failed', error });
      }
    }
    if (tasks.length > 0) {
      await this.pushLibrary().catch(console.error);
    }
  }

  private async executeTask(task: RemoteTask): Promise<void> {
    switch (task.type) {
      case 'download_manifest': {
        const { appId, gameName, headerImageUrl } = task.payload as { appId: string; gameName: string; headerImageUrl?: string };
        const dest = getCachedManifestPath(appId);
        await downloadManifest(appId, dest);
        await addSavedLibrary({ appId, gameName, headerImageUrl });
        break;
      }
      case 'check_updates': {
        const { appIds } = (task.payload ?? {}) as { appIds?: string[] };
        await checkUpdates(appIds);
        break;
      }
      case 'add_to_steam': {
        const { appId } = task.payload as { appId: string };
        await addToSteam(appId);
        break;
      }
      case 'steamless': {
        const { gameDir } = task.payload as { gameDir: string };
        if (!this.window) throw new Error('Steamless requires the desktop window to be open');
        await runSteamless(gameDir, `cloud:${task.id}`, this.window);
        break;
      }
      default:
        throw new Error(`Unknown task type: ${task.type}`);
    }
  }

  stop(): void {
    if (this.pollTimer !== null) clearInterval(this.pollTimer);
    this.pollTimer = null;
  }
}
```

- [ ] **Step 2: Verify compilation**

```bash
cd LuDownloader.Electron && npx tsc --noEmit
```

Expected: no errors. If `runSteamless` requires a non-null `BrowserWindow`, you may need to check its signature and wrap the call in a try/catch or adjust. See `src/main/ipc/steamless.ts` and adjust the `null as never` cast if needed.

- [ ] **Step 3: Commit**

```bash
git add LuDownloader.Electron/src/main/sync/cloudSync.ts
git commit -m "feat(electron): add CloudSyncAgent class"
```

---

### Task 21: Wire Sync Agent into Main Process

**Files:**
- Modify: `LuDownloader.Electron/src/main/index.ts`

- [ ] **Step 1: Add CloudSyncAgent import to index.ts**

At the top of `LuDownloader.Electron/src/main/index.ts`, add:

```typescript
import { CloudSyncAgent } from './sync/cloudSync';
```

- [ ] **Step 2: Add cloudSync variable**

After the `let updateStatuses` declaration, add:

```typescript
let cloudSync: CloudSyncAgent | null = null;
```

- [ ] **Step 3: Initialize sync agent in app.whenReady**

In the `app.whenReady().then(async () => { ... })` block, add the sync agent initialization **after** `createWindow()` (so `mainWindow` is set):

```typescript
await info(`LuDownloader Electron starting. Data root: ${userDataRoot()}`);
registerIpc();
createWindow();
// ← add after createWindow:
const settings = await loadSettings();
if (settings.cloudServerUrl && settings.cloudApiKey) {
  cloudSync = new CloudSyncAgent(settings, mainWindow);
  cloudSync.onStartup().catch((err) => console.error('CloudSync startup error:', err));
}
```

- [ ] **Step 4: Stop sync agent on quit**

After the `app.on('window-all-closed', ...)` block, add:

```typescript
app.on('before-quit', () => {
  cloudSync?.stop();
});
```

- [ ] **Step 5: Push library after every library mutation**

In `sendLibraryChanged()` (around line 266), add a cloud push after the `mainWindow?.webContents.send(...)` line:

```typescript
async function sendLibraryChanged(): Promise<void> {
  try {
    mainWindow?.webContents.send('library:changed', await buildLibraryRows(updateStatuses));
    cloudSync?.pushLibrary().catch(console.error);  // ← add this line
  } catch (err) {
    mainWindow?.webContents.send('app:error', safeError(err));
  }
}
```

- [ ] **Step 6: Verify full compilation**

```bash
cd LuDownloader.Electron && npx tsc --noEmit
```

Expected: no errors.

- [ ] **Step 7: Commit**

```bash
git add LuDownloader.Electron/src/main/index.ts
git commit -m "feat(electron): wire CloudSyncAgent into main process"
```

---

## End-to-End Verification

- [ ] **1. Start the backend**
  ```bash
  cd LuDownloader.Server && npm run dev
  ```

- [ ] **2. Configure Electron**

  Launch the Electron app in dev mode (`npm run dev` in `LuDownloader.Electron`). Open Settings → Cloud Sync, enter `http://localhost:3000` and your API key. Save and restart.

- [ ] **3. Check library synced**

  ```bash
  curl -H "X-Api-Key: test-secret" http://localhost:3000/api/library
  ```

  Expected: your installed/saved games appear.

- [ ] **4. Queue a task from PWA**

  Open `http://localhost:5173` in a browser. Go to Search, search for a game, click Queue. Expected: task appears in Tasks tab with status `pending`.

- [ ] **5. Restart Electron — task executes**

  Quit and relaunch the Electron app. The sync agent picks up the pending task, executes `download_manifest` (fetches manifest ZIP), marks it `completed`. Check Tasks tab in PWA — status changes to `completed`. Check Library — game appears in Saved.

- [ ] **6. Deploy to Railway**

  Push repo to GitHub. In Railway: New Project → Deploy from GitHub → select `LuDownloader.Server/` root. Add env vars: `DATABASE_URL` (from Railway Postgres add-on), `API_KEY`, `MORRENUS_API_KEY`. After deploy, open the Railway URL on phone, go to Settings, enter the Railway URL + API key. Test connection.
