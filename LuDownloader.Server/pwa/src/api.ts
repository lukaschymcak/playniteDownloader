// === Type definitions ===

export interface InstalledGame {
  appId: string
  gameName: string
  installPath?: string | null
  libraryPath?: string | null
  installDir?: string | null
  installedDate?: string | null
  sizeOnDisk?: number | null
  selectedDepots?: string[] | null
  drmStripped?: boolean | null
  registeredWithSteam?: boolean | null
  headerImageUrl?: string | null
  executablePath?: string | null
}

export interface SavedLibraryGame {
  appId: string
  gameName: string
  addedDate?: string | null
  headerImageUrl?: string | null
}

export interface Task {
  id: string
  type: string
  payload: unknown
  status: string
  progress: number | null
  logTail: string[] | null
  error: string | null
  createdAt: string
  startedAt: string | null
  completedAt: string | null
}

export interface MorrenusSearchResult {
  gameId: string
  gameName: string
  headerImageUrl?: string
  type?: 'game' | 'dlc'
  shortDescription?: string
  releaseYear?: number
}

export interface MorrenusSearchResponse {
  mode: 'games' | 'dlc'
  results: MorrenusSearchResult[]
  total: number
  label: string
}

export interface PwaSettings {
  serverUrl: string
  apiKey: string
}

// === Settings management ===

const SETTINGS_KEY = 'ludownloader-settings'

export function normalizeServerUrl(value: string): string {
  const trimmed = value.trim()
  if (!trimmed) return ''

  const withProtocol = /^[a-z][a-z\d+.-]*:\/\//i.test(trimmed) ? trimmed : `https://${trimmed}`
  try {
    return new URL(withProtocol).origin
  } catch {
    return trimmed.replace(/\/+$/, '')
  }
}

export function loadPwaSettings(): PwaSettings | null {
  try {
    const raw = localStorage.getItem(SETTINGS_KEY)
    if (!raw) return null
    return JSON.parse(raw) as PwaSettings
  } catch {
    return null
  }
}

export function savePwaSettings(settings: PwaSettings): void {
  localStorage.setItem(SETTINGS_KEY, JSON.stringify({
    ...settings,
    serverUrl: normalizeServerUrl(settings.serverUrl),
  }))
}

// === Internal request handler ===

function getConfig(): { serverUrl: string; headers: Record<string, string> } {
  const s = loadPwaSettings()
  if (!s?.serverUrl || !s?.apiKey) {
    throw new Error('Server URL and API key are not configured. Go to Settings.')
  }
  return {
    serverUrl: normalizeServerUrl(s.serverUrl),
    headers: { 'X-Api-Key': s.apiKey, 'Content-Type': 'application/json' },
  }
}

async function request<T>(method: string, path: string, body?: unknown): Promise<T> {
  const { serverUrl, headers } = getConfig()
  const reqHeaders: Record<string, string> = { 'X-Api-Key': headers['X-Api-Key'] }
  if (body !== undefined) reqHeaders['Content-Type'] = 'application/json'

  const requestUrl = `${serverUrl}${path}`
  const response = await fetch(requestUrl, {
    method,
    headers: reqHeaders,
    body: body !== undefined ? JSON.stringify(body) : undefined,
  })
  const text = await response.text()
  const contentType = response.headers.get('content-type') ?? ''

  if (contentType.includes('text/html')) {
    throw new Error(`Got HTML from ${requestUrl}. Use only the deployment origin as Server URL, for example https://your-app.railway.app, and make sure the server rebuild is deployed.`)
  }
  if (!response.ok) {
    throw new Error(`API error ${response.status}: ${text}`)
  }
  try {
    return JSON.parse(text) as T
  } catch {
    throw new Error(`Bad response from ${path}: ${text.slice(0, 120)}`)
  }
}

// === API client ===

export const api = {
  health: () => request<{ ok: boolean; version: string }>('GET', '/api/health'),

  library: {
    list: () =>
      request<{ installed: InstalledGame[]; saved: SavedLibraryGame[] }>('GET', '/api/library'),
  },

  tasks: {
    list: (status?: string) =>
      request<Task[]>('GET', `/api/tasks${status ? `?status=${encodeURIComponent(status)}` : ''}`),
    create: (type: string, payload: unknown) => request<Task>('POST', '/api/tasks', { type, payload }),
    update: (
      id: string,
      patch: { status?: string; progress?: number; logTail?: string[]; error?: string },
    ) => request<Task>('PATCH', `/api/tasks/${id}`, patch),
    delete: (id: string) => request<{ ok: true }>('DELETE', `/api/tasks/${id}`),
  },

  search: (query: string) =>
    request<MorrenusSearchResponse>('GET', `/api/search?q=${encodeURIComponent(query)}`),
}
