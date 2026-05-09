# LuDownloader Server — Agent Handoff

## What this is

A Node.js backend (Hono v4 + Drizzle + Postgres) deployed on Railway free tier.
It serves two things from the same process:
1. A REST API (`/api/*`) for the mobile PWA and desktop Electron app
2. A Vite-built React SPA (`pwa/dist/`) as static files

## Directory structure

```
LuDownloader.Server/
  src/
    index.ts              ← Hono app entry, route registration, server start
    middleware/
      auth.ts             ← X-Api-Key header check
    routes/
      library.ts          ← GET /api/library, POST /api/library/sync
      tasks.ts            ← CRUD for /api/tasks
      search.ts           ← GET /api/search?q= (proxies to Morrenus)
    db/
      schema.ts           ← Drizzle table definitions (tasks, installed_games, saved_library)
      client.ts           ← pg.Pool + drizzle(), throws if DATABASE_URL unset
      ensureTables.ts     ← CREATE TABLE IF NOT EXISTS on startup
    types.ts              ← InstalledGame, SavedLibraryGame, Task interfaces
  pwa/
    src/
      api.ts              ← Typed fetch client, reads serverUrl+apiKey from localStorage
      App.tsx             ← BrowserRouter + bottom nav
      views/
        LibraryView.tsx   ← calls GET /api/library
        TasksView.tsx     ← calls GET /api/tasks, polls every 10s
        SearchView.tsx    ← calls GET /api/search?q=
        SettingsView.tsx  ← saves {serverUrl, apiKey} to localStorage key "ludownloader-settings"
    index.html
    vite.config.ts
    tsconfig.json
    package.json
  railway.json            ← buildCommand: npm install && npm run build
  package.json            ← build script: tsc && cd pwa && npm install && npm run build
  tsconfig.json
```

## API routes

All `/api/*` routes except `/api/health` require `X-Api-Key: <secret>` header.

| Method | Path | Description |
|--------|------|-------------|
| GET | /api/health | Health check, no auth required |
| GET | /api/library | Returns `{ installed: InstalledGame[], saved: SavedLibraryGame[] }` |
| POST | /api/library/sync | Desktop pushes library state, upserts+deletes |
| GET | /api/tasks | List tasks, optional `?status=pending` filter |
| POST | /api/tasks | Create task `{ type, payload }` |
| PATCH | /api/tasks/:id | Update task `{ status?, progress?, logTail?, error? }` |
| DELETE | /api/tasks/:id | Cancel pending task |
| GET | /api/search?q= | Proxy to Morrenus game search |

## SPA routes (client-side, react-router-dom v6 BrowserRouter)

| Path | View |
|------|------|
| / | LibraryView |
| /tasks | TasksView |
| /search | SearchView |
| /settings | SettingsView |

## Authentication

- Server reads `API_KEY` from Railway env vars
- PWA stores `{ serverUrl, apiKey }` in `localStorage["ludownloader-settings"]`
- All fetch calls in `pwa/src/api.ts` send `X-Api-Key: <apiKey>` header

## Known issue being debugged

`GET /api/library` and `GET /api/tasks` return HTML (the SPA's `index.html`) instead of JSON.
`GET /api/health` works fine and returns `{"ok":true,"version":"0.1.0"}`.

### What we know

- Server is running on Railway (health check passes, PWA loads)
- `/api/health` in the browser returns correct JSON
- `/api/library` and `/api/tasks` return `<!doctype html>` with status 200
- These routes ARE registered: `app.route('/api/library', libraryRouter)` and `app.route('/api/tasks', tasksRouter)`
- Sub-routers use `new Hono({ strict: false })` so trailing slash is optional
- The `notFound` handler returns HTML for non-`/api/` paths and JSON 404 for `/api/` paths
- The notFound handler uses `new URL(c.req.url).pathname` to check the path (not `c.req.path`)
- The SPA fetch calls go to the correct URLs: `${serverUrl}/api/library`, `${serverUrl}/api/tasks`

### Suspected root cause

Hono v4 (`^4.7.0`) with `@hono/node-server` (`^1.14.0`) — the `app.route('/api/library', libraryRouter)` + `libraryRouter.get('/')` combination is somehow still not matching, causing the request to fall through to the `notFound` handler which returns `index.html`.

Alternatively: the `app.use('/api/*', apiKeyAuth)` middleware (registered after `app.get('/api/health', ...)` but before `app.route(...)`) is interfering with sub-router matching in Hono v4.

### Things tried

1. `serveStatic('/*')` → replaced with `serveStatic('/assets/*')` + `notFound` handler
2. `app.get('*', spaFallback)` → replaced with `app.notFound()`
3. `c.req.path.startsWith('/api/')` → replaced with `new URL(c.req.url).pathname.startsWith('/api/')`
4. `new Hono()` on sub-routers → changed to `new Hono({ strict: false })`

None fixed the `/api/library` and `/api/tasks` returning HTML issue.

## Railway environment

Required env vars:
- `DATABASE_URL` — auto-injected by Railway Postgres plugin
- `API_KEY` — manually set, shared secret for auth
- `PORT` — auto-injected by Railway
- `MORRENUS_API_KEY` — optional, for search proxy

## Build + deploy

Railway root directory: `LuDownloader.Server/`
Build command: `npm install && npm run build`  
Start command: `node dist/index.js`
Health check: `GET /api/health`

The build script (`package.json`) runs:
1. `tsc` — compiles server TypeScript to `dist/`
2. `cd pwa && npm install && npm run build` — builds PWA to `pwa/dist/`

The running server serves `pwa/dist/` as static files.

## Key file contents

### src/index.ts (current)

```typescript
const app = new Hono()

app.use('*', logger())
app.use('*', cors())

app.get('/api/health', (c) => c.json({ ok: true, version: '0.1.0' }))

app.use('/api/*', apiKeyAuth)
app.route('/api/library', libraryRouter)
app.route('/api/tasks', tasksRouter)
app.route('/api/search', searchRouter)

app.use('/assets/*', serveStatic({ root: './pwa/dist' }))

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
```

### src/routes/tasks.ts (current)

```typescript
export const tasksRouter = new Hono({ strict: false })

tasksRouter.get('/', async (c) => { /* lists tasks */ })
tasksRouter.post('/', async (c) => { /* creates task */ })
tasksRouter.patch('/:id', async (c) => { /* updates task */ })
tasksRouter.delete('/:id', async (c) => { /* cancels task */ })
```

### pwa/src/api.ts request function

```typescript
async function request<T>(method: string, path: string, body?: unknown): Promise<T> {
  const { serverUrl, headers } = getConfig()  // reads localStorage
  const reqHeaders: Record<string, string> = { 'X-Api-Key': headers['X-Api-Key'] }
  if (body !== undefined) reqHeaders['Content-Type'] = 'application/json'
  const response = await fetch(`${serverUrl}${path}`, { method, headers: reqHeaders, body: ... })
  const text = await response.text()
  if (!response.ok) throw new Error(`API error ${response.status}: ${text}`)
  try {
    return JSON.parse(text) as T
  } catch {
    throw new Error(`Bad response from ${path}: ${text.slice(0, 120)}`)
  }
}

export const api = {
  health: () => request('GET', '/api/health'),
  library: { list: () => request('GET', '/api/library') },
  tasks: { list: (status?) => request('GET', `/api/tasks${...}`) },
  ...
}
```
