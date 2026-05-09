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
  localStorage.setItem(SETTINGS_KEY, JSON.stringify(settings))
}

// === Internal request handler ===

function getConfig(): { serverUrl: string; headers: Record<string, string> } {
  const s = loadPwaSettings()
  if (!s?.serverUrl || !s?.apiKey) {
    throw new Error('Server URL and API key are not configured. Go to Settings.')
  }
  return {
    serverUrl: s.serverUrl.replace(/\/$/, ''),
    headers: { 'X-Api-Key': s.apiKey, 'Content-Type': 'application/json' },
  }
}

async function request<T>(method: string, path: string, body?: unknown): Promise<T> {
  const { serverUrl, headers } = getConfig()
  const response = await fetch(`${serverUrl}${path}`, {
    method,
    headers,
    body: body !== undefined ? JSON.stringify(body) : undefined,
  })
  if (!response.ok) {
    const text = await response.text().catch(() => '')
    throw new Error(`API error ${response.status}: ${text}`)
  }
  return response.json() as Promise<T>
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
    delete: (id: string) => request<{ ok: boolean }>('DELETE', `/api/tasks/${id}`),
  },

  search: (query: string) =>
    request<MorrenusSearchResponse>('GET', `/api/search?q=${encodeURIComponent(query)}`),
}
