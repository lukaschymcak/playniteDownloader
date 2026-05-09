import { Hono } from 'hono'
import { apiKeyAuth } from '../middleware/auth.js'

type SearchMode = 'games' | 'dlc'
type SteamSearchItem = { appId: string; name: string; index: number }
type SteamDetails = {
  appId: string
  type: string
  name: string
  shortDescription?: string
  headerImageUrl?: string
  releaseYear?: number
  dlcIds: string[]
}

type SearchResult = {
  gameId: string
  gameName: string
  headerImageUrl?: string
  relevanceScore?: number
  type?: 'game' | 'dlc'
  shortDescription?: string
  releaseYear?: number
  dlcCount?: number
}

type SearchResponse = {
  mode: SearchMode
  results: SearchResult[]
  total: number
  label: string
  baseGame?: SearchResult
}

export function registerSearchRoutes(app: Hono): void {
  app.get('/api/search', apiKeyAuth, async (c) => {
    const q = c.req.query('q')
    const mode = c.req.query('mode') === 'dlc' ? 'dlc' : 'games'
    if (!q) return c.json({ error: 'Missing q parameter' }, 400)

    try {
      return c.json(await searchGames(q, mode))
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err)
      return c.json({ error: message }, 502)
    }
  })
}

async function searchGames(query: string, mode: SearchMode = 'games'): Promise<SearchResponse> {
  return mode === 'dlc' ? searchSteamDlc(query) : searchSteamGames(query)
}

async function searchSteamGames(query: string): Promise<SearchResponse> {
  const items = await searchSteamStore(query, 0, 50)
  const details = await Promise.all(items.map((item) => getSteamDetails(item.appId)))
  const results = details
    .map((detail, index) => ({ detail, item: items[index] }))
    .filter((row): row is { detail: SteamDetails; item: SteamSearchItem } => row.detail?.type === 'game')
    .map((row) => toSearchResult(row.detail, relevanceScore(query, row.detail.name || row.item.name, row.item.index)))
    .sort((a, b) => (a.relevanceScore ?? 0) - (b.relevanceScore ?? 0))

  return {
    mode: 'games',
    results,
    total: results.length,
    label: results.length === 1 ? '1 game result' : `${results.length} game results`,
  }
}

async function searchSteamDlc(query: string): Promise<SearchResponse> {
  const baseGame = await findDlcBaseGame(query)
  if (!baseGame) {
    return { mode: 'dlc', results: [], total: 0, label: 'No DLC found.' }
  }

  if (baseGame.dlcIds.length === 0) {
    return {
      mode: 'dlc',
      results: [],
      total: 0,
      label: `No DLC found for ${baseGame.name}.`,
      baseGame: toSearchResult(baseGame, 0),
    }
  }

  const details = await Promise.all(baseGame.dlcIds.map((appId) => getSteamDetails(appId)))
  const results = details
    .filter((detail): detail is SteamDetails => Boolean(detail))
    .map((detail, index) => toSearchResult(detail, relevanceScore(query, detail.name, index)))
    .sort((a, b) => (a.relevanceScore ?? 0) - (b.relevanceScore ?? 0))

  return {
    mode: 'dlc',
    results,
    total: results.length,
    label: results.length === 0 ? `No DLC details could be loaded for ${baseGame.name}.` : `${results.length} DLC for ${baseGame.name}`,
    baseGame: toSearchResult(baseGame, 0),
  }
}

async function findDlcBaseGame(query: string): Promise<SteamDetails | undefined> {
  const candidates = await searchSteamStore(query, 0, 5)
  let firstGame: SteamDetails | undefined

  for (const candidate of candidates) {
    const detail = await getSteamDetails(candidate.appId)
    if (!detail || detail.type !== 'game') continue
    if (!firstGame) firstGame = detail
    if (detail.dlcIds.length > 0) return detail
  }

  return firstGame
}

async function searchSteamStore(query: string, start: number, count: number): Promise<SteamSearchItem[]> {
  const url = `https://store.steampowered.com/api/storesearch/?term=${encodeURIComponent(query)}&l=english&cc=US&start=${start}&count=${count}`
  const response = await fetch(url)
  if (!response.ok) throw new Error(`Steam search failed (${response.status})`)

  const json = await response.json() as { items?: Array<{ id?: unknown; name?: unknown }> }
  return (Array.isArray(json.items) ? json.items : [])
    .map((item, index) => ({
      appId: String(item.id ?? ''),
      name: String(item.name ?? ''),
      index,
    }))
    .filter((item) => /^\d+$/.test(item.appId) && item.name.trim())
}

async function getSteamDetails(appId: string): Promise<SteamDetails | undefined> {
  if (!/^\d+$/.test(appId)) return undefined

  try {
    const url = `https://store.steampowered.com/api/appdetails?appids=${encodeURIComponent(appId)}&filters=basic,release_date,short_description,dlc`
    const response = await fetch(url)
    if (!response.ok) return undefined

    const json = await response.json() as Record<string, {
      success?: boolean
      data?: {
        type?: unknown
        name?: unknown
        header_image?: unknown
        short_description?: unknown
        release_date?: { date?: unknown }
        dlc?: unknown[]
      }
    }>
    const data = json[appId]?.success ? json[appId]?.data : undefined
    if (!data) return undefined
    const header = typeof data.header_image === 'string' && data.header_image.trim() ? data.header_image.trim() : undefined
    const releaseDate = typeof data.release_date?.date === 'string' ? data.release_date.date : ''
    const yearMatch = releaseDate.match(/\b(19|20)\d{2}\b/g)
    return {
      appId,
      type: typeof data.type === 'string' ? data.type : '',
      name: typeof data.name === 'string' ? data.name : '',
      shortDescription: typeof data.short_description === 'string' ? data.short_description : undefined,
      headerImageUrl: header,
      releaseYear: yearMatch?.length ? Number(yearMatch[yearMatch.length - 1]) : undefined,
      dlcIds: Array.isArray(data.dlc) ? data.dlc.map((value) => String(value)).filter((value) => /^\d+$/.test(value)) : [],
    }
  } catch {
    return undefined
  }
}

function toSearchResult(detail: SteamDetails, score: number): SearchResult {
  return {
    gameId: detail.appId,
    gameName: detail.name || detail.appId,
    headerImageUrl: detail.headerImageUrl || steamHeaderFallback(detail.appId),
    relevanceScore: score,
    type: detail.type === 'dlc' ? 'dlc' : 'game',
    shortDescription: detail.shortDescription,
    releaseYear: detail.releaseYear,
    dlcCount: detail.dlcIds.length,
  }
}

function relevanceScore(query: string, name: string, originalIndex: number): number {
  const q = normalizeSearchText(query)
  const n = normalizeSearchText(name)
  const words = n.split(/\s+/).filter(Boolean)
  if (!q || !n) return 1000 + originalIndex
  if (n === q) return 0
  if (n.startsWith(`${q} `) || n.startsWith(`${q}:`) || n.startsWith(`${q}-`)) return 10 + lengthPenalty(q, n)
  if (words.some((word) => word === q)) return 20 + lengthPenalty(q, n)
  if (words.some((word) => word.startsWith(q))) return 30 + lengthPenalty(q, n)
  const index = n.indexOf(q)
  if (index >= 0) return 100 + index + lengthPenalty(q, n)
  return 500 + originalIndex
}

function lengthPenalty(query: string, name: string): number {
  return Math.min(30, Math.max(0, name.length - query.length) / 10)
}

function normalizeSearchText(value: string): string {
  return value
    .toLowerCase()
    .replace(/[™®©]/g, '')
    .replace(/[^a-z0-9]+/g, ' ')
    .trim()
}

function steamHeaderFallback(appId: string): string | undefined {
  if (!/^\d+$/.test(appId)) return undefined
  return `https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/${appId}/header.jpg`
}
