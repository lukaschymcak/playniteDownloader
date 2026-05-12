import test from 'node:test';
import assert from 'node:assert/strict';
import {
  fetchSteamAchievementSchema,
  fetchSteamGlobalAchievementPercentages,
  parsePercentagesResponseBody,
  parseSchemaResponseBody
} from './steamWebApiAchievement.ts';

test('parseSchemaResponseBody maps game and achievements', () => {
  const body = {
    game: {
      gameName: 'Demo',
      availableGameStats: {
        achievements: [
          {
            name: 'ACH1',
            displayName: 'First',
            description: 'd',
            hidden: 0,
            icon: 'abc',
            icongray: 'gr'
          }
        ]
      }
    }
  };
  const r = parseSchemaResponseBody(body);
  assert.ok(r);
  assert.equal(r!.gameName, 'Demo');
  assert.equal(r!.achievements.length, 1);
  assert.equal(r!.achievements[0]!.name, 'ACH1');
  assert.equal(r!.achievements[0]!.displayName, 'First');
  assert.equal(r!.achievements[0]!.hidden, false);
});

test('parsePercentagesResponseBody maps name to percent', () => {
  const body = {
    achievementpercentages: {
      achievements: [{ name: 'ACH1', percent: 12.5 }]
    }
  };
  const r = parsePercentagesResponseBody(body);
  assert.deepEqual(r, { ACH1: 12.5 });
});

test('fetchSteamAchievementSchema returns null when key empty', async () => {
  const r = await fetchSteamAchievementSchema('480', '  ', () => {
    throw new Error('fetch should not run');
  });
  assert.equal(r, null);
});

test('fetchSteamGlobalAchievementPercentages uses stub fetch', async () => {
  const stub: typeof fetch = async () =>
    new Response(
      JSON.stringify({
        achievementpercentages: {
          achievements: [{ name: 'X', percent: 1 }]
        }
      }),
      { status: 200 }
    );
  const r = await fetchSteamGlobalAchievementPercentages('480', 'k', stub);
  assert.deepEqual(r, { X: 1 });
});

test('fetchSteamAchievementSchema parses stub JSON', async () => {
  const stub: typeof fetch = async () =>
    new Response(
      JSON.stringify({
        game: {
          gameName: 'FromStub',
          availableGameStats: {
            achievements: [
              {
                name: 'ACH_STUB',
                displayName: 'StubLabel',
                description: '',
                hidden: 1,
                icon: '',
                icongray: ''
              }
            ]
          }
        }
      }),
      { status: 200 }
    );
  const r = await fetchSteamAchievementSchema('999', 'secret', stub);
  assert.ok(r);
  assert.equal(r!.gameName, 'FromStub');
  assert.equal(r!.achievements[0]!.displayName, 'StubLabel');
  assert.equal(r!.achievements[0]!.hidden, true);
});
