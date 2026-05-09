import type { BrowserWindow } from 'electron'
import type { DownloadStartRequest, InstalledGame, SavedLibraryGame } from '../../shared/types'
import { startDownload } from '../ipc/depot'
import { addToSteam } from '../ipc/steam'
import { runSteamless } from '../ipc/steamless'
import { checkUpdates } from '../ipc/manifest'
import { addSavedLibrary, listInstalled, listSavedLibrary } from '../ipc/games'
import { loadSettings } from '../ipc/settings'
import { info, warn } from '../ipc/logger'

export class CloudSyncAgent {
  private pollTimer: NodeJS.Timeout | null = null
  private window: BrowserWindow | null

  constructor(window: BrowserWindow | null = null) {
    this.window = window
  }

  setWindow(window: BrowserWindow): void {
    this.window = window
  }

  async onStartup(): Promise<void> {
    try {
      await this.pushLibrary()
      await this.executePendingTasks()
    } catch (err) {
      await warn(`CloudSync startup error: ${err instanceof Error ? err.message : String(err)}`)
    }
    this.startPoll()
  }

  async pushLibrary(): Promise<void> {
    const config = await this.getConfig()
    if (!config) return

    const [installed, saved] = await Promise.all([listInstalled(), listSavedLibrary()])
    await this.post(config, '/api/library/sync', { installed, saved })
  }

  stop(): void {
    if (this.pollTimer) {
      clearInterval(this.pollTimer)
      this.pollTimer = null
    }
  }

  private startPoll(): void {
    this.stop()
    this.pollTimer = setInterval(() => {
      void this.executePendingTasks().catch(async (err: unknown) => {
        await warn(`CloudSync poll error: ${err instanceof Error ? err.message : String(err)}`)
      })
    }, 60_000)
  }

  private async executePendingTasks(): Promise<void> {
    const config = await this.getConfig()
    if (!config) return

    const tasks = await this.get<Task[]>(config, '/api/tasks?status=pending')
    if (!tasks.length) return

    await info(`CloudSync: ${tasks.length} pending task(s)`)

    for (const task of tasks) {
      await this.executeTask(config, task)
    }
  }

  private async executeTask(config: Config, task: Task): Promise<void> {
    await this.patch(config, `/api/tasks/${task.id}`, { status: 'running' })
    try {
      await this.runTask(task)
      await this.patch(config, `/api/tasks/${task.id}`, { status: 'completed', progress: 100 })
      await this.pushLibrary()
    } catch (err) {
      const error = err instanceof Error ? err.message : String(err)
      await warn(`CloudSync task ${task.id} (${task.type}) failed: ${error}`)
      await this.patch(config, `/api/tasks/${task.id}`, { status: 'failed', error })
    }
  }

  private async runTask(task: Task): Promise<void> {
    switch (task.type) {
      case 'download': {
        if (!this.window) throw new Error('No window available for download task')
        const payload = task.payload as DownloadStartRequest
        await startDownload(payload, this.window)
        break
      }
      case 'add_to_library': {
        const payload = task.payload as { appId: string; gameName?: string; headerImageUrl?: string }
        if (!payload.appId) throw new Error('Missing appId for add_to_library task')
        await addSavedLibrary({
          appId: payload.appId,
          gameName: payload.gameName || payload.appId,
          headerImageUrl: payload.headerImageUrl,
        })
        break
      }
      case 'add_to_steam': {
        const payload = task.payload as { appId: string }
        await addToSteam(payload.appId)
        break
      }
      case 'steamless': {
        if (!this.window) throw new Error('No window available for steamless task')
        const payload = task.payload as { gameDir: string }
        await runSteamless(payload.gameDir, `cloud-${task.id}`, this.window)
        break
      }
      case 'check_updates': {
        const payload = task.payload as { appIds?: string[] }
        await checkUpdates(payload.appIds)
        break
      }
      default:
        throw new Error(`Unknown task type: ${task.type}`)
    }
  }

  private async getConfig(): Promise<Config | null> {
    const settings = await loadSettings()
    if (!settings.cloudServerUrl || !settings.cloudApiKey) return null
    return {
      serverUrl: normalizeServerUrl(settings.cloudServerUrl),
      headers: { 'X-Api-Key': settings.cloudApiKey, 'Content-Type': 'application/json' }
    }
  }

  private async get<T>(config: Config, path: string): Promise<T> {
    const response = await fetch(`${config.serverUrl}${path}`, { headers: config.headers })
    if (!response.ok) throw new Error(`Cloud API GET ${path} failed: ${response.status}`)
    return response.json() as Promise<T>
  }

  private async post(config: Config, path: string, body: unknown): Promise<void> {
    const response = await fetch(`${config.serverUrl}${path}`, {
      method: 'POST',
      headers: config.headers,
      body: JSON.stringify(body)
    })
    if (!response.ok) throw new Error(`Cloud API POST ${path} failed: ${response.status}`)
  }

  private async patch(config: Config, path: string, body: unknown): Promise<void> {
    const response = await fetch(`${config.serverUrl}${path}`, {
      method: 'PATCH',
      headers: config.headers,
      body: JSON.stringify(body)
    })
    if (!response.ok) throw new Error(`Cloud API PATCH ${path} failed: ${response.status}`)
  }
}

interface Config {
  serverUrl: string
  headers: Record<string, string>
}

interface Task {
  id: string
  type: string
  payload: unknown
  status: string
  progress: number | null
}

function normalizeServerUrl(value: string): string {
  const trimmed = value.trim()
  if (!trimmed) return ''

  const withProtocol = /^[a-z][a-z\d+.-]*:\/\//i.test(trimmed) ? trimmed : `https://${trimmed}`
  try {
    return new URL(withProtocol).origin
  } catch {
    return trimmed.replace(/\/+$/, '')
  }
}
