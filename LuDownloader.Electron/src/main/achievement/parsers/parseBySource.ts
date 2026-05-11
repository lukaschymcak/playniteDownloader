import { SourceId, type RawAchievement } from '../../../shared/types.ts';
import { parseAli213Achievements } from './ali213Parser.ts';
import { parseCodexAchievements } from './codexParser.ts';
import { parseCreamApiAchievements } from './creamApiParser.ts';
import { parseGoldbergAchievements } from './goldbergParser.ts';
import { parseHoodlumAchievements } from './hoodlumParser.ts';
import { parseOnlineFixAchievements } from './onlineFixParser.ts';
import { parseReloadedAchievements } from './reloadedParser.ts';
import { parseSkidrowAchievements } from './skidrowParser.ts';
import { parseSmartSteamEmuAchievements } from './smartSteamEmuParser.ts';

/**
 * Parses a discovered save file for `source`. GreenLuma uses the registry; use `parseGreenLumaAchievements` instead.
 */
export function parseAchievementsBySource(source: SourceId, filePath: string): Record<string, RawAchievement> {
  switch (source) {
    case SourceId.Goldberg:
    case SourceId.GSE:
    case SourceId.Empress:
      return parseGoldbergAchievements(filePath);
    case SourceId.Codex:
    case SourceId.Rune:
      return parseCodexAchievements(filePath);
    case SourceId.OnlineFix:
      return parseOnlineFixAchievements(filePath);
    case SourceId.SmartSteamEmu:
      return parseSmartSteamEmuAchievements(filePath);
    case SourceId.Skidrow:
      return parseSkidrowAchievements(filePath);
    case SourceId.Darksiders:
    case SourceId.Hoodlum:
      return parseHoodlumAchievements(filePath);
    case SourceId.Ali213:
      return parseAli213Achievements(filePath);
    case SourceId.CreamApi:
      return parseCreamApiAchievements(filePath);
    case SourceId.Reloaded:
      return parseReloadedAchievements(filePath);
    case SourceId.GreenLuma:
    case SourceId.None:
    default:
      return {};
  }
}
