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
  cloudServerUrl: string;
  cloudApiKey: string;
}

export interface InstalledGame {
  appId: string;
  gameName: string;
  installPath: string;
  libraryPath: string;
  installDir: string;
  installedDate: string;
  sizeOnDisk: number;
  selectedDepots: string[];
  manifestGIDs: Record<string, string>;
  drmStripped: boolean;
  registeredWithSteam: boolean;
  gseSavesCopied: boolean;
  headerImageUrl?: string;
  steamBuildId?: string;
  manifestZipPath?: string;
  executablePath?: string;
}

export interface SavedLibraryGame {
  appId: string;
  gameName: string;
  addedDate: string;
  headerImageUrl?: string;
}

export interface DepotInfo {
  key: string;
  description: string;
  size: number;
}

export interface GameData {
  appId: string;
  gameName: string;
  installDir?: string;
  buildId?: string;
  depots: Record<string, DepotInfo>;
  dlcs: Record<string, string>;
  manifests: Record<string, string>;
  selectedDepots: string[];
  manifestZipPath?: string;
  headerImageUrl?: string;
}

export interface MorrenusSearchResult {
  gameId: string;
  gameName: string;
  headerImageUrl?: string;
  relevanceScore?: number;
  type?: 'game' | 'dlc';
  shortDescription?: string;
  releaseYear?: number;
  dlcCount?: number;
}

export interface MorrenusSearchResponse {
  mode: 'games' | 'dlc';
  results: MorrenusSearchResult[];
  total: number;
  label: string;
  baseGame?: MorrenusSearchResult;
}

export interface MorrenusUserStats {
  username?: string;
  dailyUsage: number;
  dailyLimit: number;
  error?: string;
}

export interface ManifestCheckResult {
  appId: string;
  depotId: string;
  manifestGid: string;
  buildId?: string;
}

export interface UpdateStatus {
  appId: string;
  status: 'up_to_date' | 'update_available' | 'cannot_determine';
  buildId?: string;
  error?: string;
}

export interface LibraryRow {
  appId: string;
  name: string;
  state: 'installed' | 'saved';
  update: UpdateStatus['status'];
  luaAdded?: boolean;
  manifestAdded?: boolean;
  steamIntegrationMessage?: string;
  size?: number;
  installedDate?: string;
  path?: string;
  headerImageUrl?: string;
  executablePath?: string;
}

export interface ManifestCacheEntry {
  appId: string;
  file: string;
  path: string;
  size: number;
  modifiedAt: string;
}

export interface DownloadStartRequest {
  gameData: GameData;
  selectedDepots: string[];
  libraryPath: string;
  steamUsername?: string;
  maxDownloads: number;
  channelId: string;
}

export interface ProgressEvent {
  pct?: number;
  log?: string;
  received?: number;
  total?: number;
  done?: boolean;
  error?: string;
}

export interface ReconcileResult {
  added: number;
  updated: number;
  removed: number;
}
