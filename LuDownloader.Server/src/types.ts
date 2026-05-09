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
