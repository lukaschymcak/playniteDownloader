import test from 'node:test';
import assert from 'node:assert/strict';
import { SourceId } from '../../shared/types.ts';
import { mergeRawAchievements, SOURCE_MERGE_ORDER } from './rawAchievementMerge.ts';

test('mergeRawAchievements OR achieved and max progress', () => {
  const { merged, sourceMask } = mergeRawAchievements([
    {
      source: SourceId.Goldberg,
      raw: {
        A: { achieved: false, unlockTime: 0, curProgress: 1, maxProgress: 5 },
        B: { achieved: true, unlockTime: 10, curProgress: 0, maxProgress: 0 }
      }
    },
    {
      source: SourceId.Codex,
      raw: {
        A: { achieved: true, unlockTime: 20, curProgress: 4, maxProgress: 5 }
      }
    }
  ]);

  assert.equal(merged.A!.achieved, true);
  assert.equal(merged.A!.unlockTime, 20);
  assert.equal(merged.A!.curProgress, 4);
  assert.equal(merged.A!.maxProgress, 5);
  assert.equal(merged.B!.achieved, true);
  assert.ok(sourceMask > 0);
});

test('mergeRawAchievements unlockTime zero when not achieved', () => {
  const { merged } = mergeRawAchievements([
    { source: SourceId.Goldberg, raw: { Z: { achieved: false, unlockTime: 99, curProgress: 0, maxProgress: 0 } } },
    { source: SourceId.GSE, raw: { Z: { achieved: false, unlockTime: 5, curProgress: 0, maxProgress: 0 } } }
  ]);
  assert.equal(merged.Z!.achieved, false);
  assert.equal(merged.Z!.unlockTime, 0);
});

test('SOURCE_MERGE_ORDER has no duplicate entries', () => {
  assert.equal(new Set(SOURCE_MERGE_ORDER).size, SOURCE_MERGE_ORDER.length);
});
