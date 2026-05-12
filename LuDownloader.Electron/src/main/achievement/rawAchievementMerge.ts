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

const SOURCE_MASK: Record<SourceId, EmulatorSourceMask> = {
  [SourceId.None]:          EmulatorSource.None,
  [SourceId.Goldberg]:      EmulatorSource.Goldberg,
  [SourceId.GSE]:           EmulatorSource.GSE,
  [SourceId.Codex]:         EmulatorSource.Codex,
  [SourceId.Rune]:          EmulatorSource.Rune,
  [SourceId.Empress]:       EmulatorSource.Empress,
  [SourceId.OnlineFix]:     EmulatorSource.OnlineFix,
  [SourceId.SmartSteamEmu]: EmulatorSource.SmartSteamEmu,
  [SourceId.Skidrow]:       EmulatorSource.Skidrow,
  [SourceId.Darksiders]:    EmulatorSource.Darksiders,
  [SourceId.Ali213]:        EmulatorSource.Ali213,
  [SourceId.Hoodlum]:       EmulatorSource.Hoodlum,
  [SourceId.CreamApi]:      EmulatorSource.CreamApi,
  [SourceId.GreenLuma]:     EmulatorSource.GreenLuma,
  [SourceId.Reloaded]:      EmulatorSource.Reloaded,
};

export function mergeRawAchievements(
  rows: Array<{ source: SourceId; raw: Record<string, RawAchievement> }>
): { merged: Record<string, RawAchievement>; sourceMask: EmulatorSourceMask } {
  let sourceMask = EmulatorSource.None;
  for (const row of rows) {
    sourceMask |= SOURCE_MASK[row.source] ?? EmulatorSource.None;
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
