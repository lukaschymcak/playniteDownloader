import {
  EmulatorSource,
  SourceId,
  type EmulatorSourceMask,
  type RawAchievement
} from '../../shared/types.ts';

/**
 * Merge order is fixed so combining sources is deterministic and testable; OR/max folding is
 * commutative, but a stable order preserves predictability if rules tighten later (e.g. tie-breaks).
 */
export const SOURCE_MERGE_ORDER: SourceId[] = [
  SourceId.None,
  SourceId.Hoodlum,
  SourceId.Ali213,
  SourceId.Skidrow,
  SourceId.Darksiders,
  SourceId.SmartSteamEmu,
  SourceId.OnlineFix,
  SourceId.Rune,
  SourceId.Reloaded,
  SourceId.CreamApi,
  SourceId.Codex,
  SourceId.Empress,
  SourceId.GSE,
  SourceId.Goldberg,
  SourceId.GreenLuma
];

function sourceMergeIndex(source: SourceId): number {
  const i = SOURCE_MERGE_ORDER.indexOf(source);
  return i < 0 ? SOURCE_MERGE_ORDER.length : i;
}

function sourceIdToMask(id: SourceId): EmulatorSourceMask {
  switch (id) {
    case SourceId.None:
      return EmulatorSource.None;
    case SourceId.Goldberg:
      return EmulatorSource.Goldberg;
    case SourceId.GSE:
      return EmulatorSource.GSE;
    case SourceId.Codex:
      return EmulatorSource.Codex;
    case SourceId.Rune:
      return EmulatorSource.Rune;
    case SourceId.Empress:
      return EmulatorSource.Empress;
    case SourceId.OnlineFix:
      return EmulatorSource.OnlineFix;
    case SourceId.SmartSteamEmu:
      return EmulatorSource.SmartSteamEmu;
    case SourceId.Skidrow:
      return EmulatorSource.Skidrow;
    case SourceId.Darksiders:
      return EmulatorSource.Darksiders;
    case SourceId.Ali213:
      return EmulatorSource.Ali213;
    case SourceId.Hoodlum:
      return EmulatorSource.Hoodlum;
    case SourceId.CreamApi:
      return EmulatorSource.CreamApi;
    case SourceId.GreenLuma:
      return EmulatorSource.GreenLuma;
    case SourceId.Reloaded:
      return EmulatorSource.Reloaded;
    default:
      return EmulatorSource.None;
  }
}

export function mergeRawAchievements(
  rows: Array<{ source: SourceId; raw: Record<string, RawAchievement> }>
): { merged: Record<string, RawAchievement>; sourceMask: EmulatorSourceMask } {
  let sourceMask = EmulatorSource.None;
  for (const row of rows) {
    sourceMask |= sourceIdToMask(row.source);
  }

  const sorted = [...rows].sort((a, b) => {
    const d = sourceMergeIndex(a.source) - sourceMergeIndex(b.source);
    if (d !== 0) return d;
    return a.source.localeCompare(b.source);
  });

  const merged: Record<string, RawAchievement> = {};
  for (const row of sorted) {
    for (const [apiName, next] of Object.entries(row.raw)) {
      const prev = merged[apiName];
      if (!prev) {
        merged[apiName] = {
          achieved: next.achieved,
          unlockTime: next.achieved ? next.unlockTime : 0,
          curProgress: next.curProgress,
          maxProgress: next.maxProgress
        };
        continue;
      }

      const achieved = prev.achieved || next.achieved;
      merged[apiName] = {
        achieved,
        unlockTime: achieved ? Math.max(prev.unlockTime, next.unlockTime) : 0,
        curProgress: Math.max(prev.curProgress, next.curProgress),
        maxProgress: Math.max(prev.maxProgress, next.maxProgress)
      };
    }
  }

  return { merged, sourceMask };
}
