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
  achievementEnabledSources: SourceId[];
  achievementSourceRoots: Partial<Record<SourceId, string[]>>;
  hoodlumSavePath: string;
  achievementUserGameLibraryRoots: string[];
  achievementScanOfficialSteamLibraries: boolean;
  achievementFirstRunDismissed: boolean;
}

/** Emulator / save source id (string literals for JSON + node strip-types tests). */
export const SourceId = {
  None: 'None',
  Goldberg: 'Goldberg',
  GSE: 'GSE',
  Empress: 'Empress',
  Codex: 'Codex',
  Rune: 'Rune',
  OnlineFix: 'OnlineFix',
  SmartSteamEmu: 'SmartSteamEmu',
  Skidrow: 'Skidrow',
  Darksiders: 'Darksiders',
  Ali213: 'Ali213',
  Hoodlum: 'Hoodlum',
  CreamApi: 'CreamApi',
  GreenLuma: 'GreenLuma',
  Reloaded: 'Reloaded'
} as const;

export type SourceId = (typeof SourceId)[keyof typeof SourceId];

/** Bitmask parity with `LuiAchieve.EmulatorSource` ([Flags]). */
export type EmulatorSourceMask = number;

export const EmulatorSource = {
  None: 0,
  Goldberg: 1 << 0,
  GSE: 1 << 1,
  Codex: 1 << 2,
  Rune: 1 << 3,
  Empress: 1 << 4,
  OnlineFix: 1 << 5,
  SmartSteamEmu: 1 << 6,
  Skidrow: 1 << 7,
  Darksiders: 1 << 8,
  Ali213: 1 << 9,
  Hoodlum: 1 << 10,
  CreamApi: 1 << 11,
  GreenLuma: 1 << 12,
  Reloaded: 1 << 13,
  All: (1 << 14) - 1
} as const;

export interface RawAchievement {
  achieved: boolean;
  unlockTime: number;
  curProgress: number;
  maxProgress: number;
}

export interface Achievement {
  apiName: string;
  gameName: string;
  displayName: string;
  description: string;
  hidden: boolean;
  iconPath?: string | null;
  iconGrayPath?: string | null;
  iconUrl?: string | null;
  iconGrayUrl?: string | null;
  achieved: boolean;
  unlockTime: number;
  curProgress: number;
  maxProgress: number;
  globalPercentage: number;
}

export interface GameAchievements {
  appId: string;
  gameName: string;
  storeHeaderImageUrl?: string | null;
  installDir?: string | null;
  hasUsableInstallDirectory: boolean;
  source: EmulatorSourceMask;
  list: Achievement[];
  unlockedCount: number;
  totalCount: number;
  percentage: number;
  hasPlatinum: boolean;
}

export interface ScanResult {
  appId: string;
  source: EmulatorSourceMask;
  filePath: string;
}

export interface AchievementDiff {
  achievement: Achievement;
  isNewUnlock: boolean;
  isProgressMilestone: boolean;
  oldProgress: number;
  newProgress: number;
}

/** Minimal persisted achievement row for snapshot baselines (Step 8). */
export interface SnapshotAchievementState {
  achieved: boolean;
  unlockTime: number;
  curProgress: number;
  maxProgress: number;
}

export interface GameAchievementSnapshot {
  appId: string;
  capturedAtUnix: number;
  achievements: Record<string, SnapshotAchievementState>;
}

export interface UserProfile {
  name: string;
  avatarPath?: string | null;
  featuredTrophiesByGame: Record<string, string[]>;
}

export type DiscoveryKind = 'file' | 'registry';

export interface DiscoveryRecord {
  appId: string;
  source: SourceId;
  kind: DiscoveryKind;
  location: string;
}

export interface FileChangeEvent {
  source: SourceId;
  rootPath: string;
  fullPath: string;
  eventType: 'add' | 'change' | 'unlink' | 'rename';
  timestamp?: number;
}

export interface ResolvedChange {
  appId: string;
  source: SourceId;
  location: string;
  eventType: FileChangeEvent['eventType'];
  timestamp: number;
}

export interface AchievementGameContext {
  appId: string;
  installPath: string | null;
  libraryRoot: string | null;
  installDir: string | null;
  gameName: string | null;
  hasUsableInstallDirectory: boolean;
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
  goldbergState?: 'required' | 'applied' | 'not_needed';
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
  depotBuildId?: string;
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
  goldbergState?: 'required' | 'applied' | 'not_needed';
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
  outputPath?: string;
  steamUsername?: string;
  maxDownloads: number;
  channelId: string;
}

export type DownloadCancelMode = 'keep' | 'delete';
export type DownloadHealthState = 'stable' | 'retrying' | 'warning' | 'degraded';

export interface ProgressEvent {
  pct?: number;
  log?: string;
  received?: number;
  total?: number;
  status?: string;
  done?: boolean;
  error?: string;
  canceled?: boolean;
  terminalReason?: 'completed' | 'failed' | 'canceled';
  diskBps?: number | null;
  healthState?: DownloadHealthState;
  retryCountRecent?: number;
  lastHealthMessage?: string;
}

export type DownloadPhase = 'setup' | 'downloading' | 'complete' | 'failed' | 'canceled';

export interface DownloadSample {
  t: number;
  pct: number;
}

export interface DownloadSession {
  channelId: string;
  appId: string;
  gameName: string;
  headerImageUrl?: string;
  targetPct: number;
  displayPct: number;
  status: string;
  indeterminate: boolean;
  speedBps: number | null;
  diskBps?: number | null;
  etaSec: number | null;
  healthState?: DownloadHealthState;
  retryCountRecent?: number;
  lastHealthMessage?: string;
  totalBytes: number;
  startedAt: number;
  updatedAt: number;
  done: boolean;
  error?: string;
  phase: DownloadPhase;
  logs: string[];
  samples: DownloadSample[];
}

export interface ReconcileResult {
  added: number;
  updated: number;
  removed: number;
}
