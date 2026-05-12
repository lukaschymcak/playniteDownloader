import type { GameAchievements } from '../../../shared/types';

const now = Math.floor(Date.now() / 1000);
const d = (daysAgo: number): number => now - daysAgo * 86400;

export const MOCK_GAMES: GameAchievements[] = [
  // ── Elden Ring — partial (40%), best = gold ────────────────────────
  {
    appId: 'mock-9901',
    gameName: 'Elden Ring',
    storeHeaderImageUrl: null,
    installDir: null,
    hasUsableInstallDirectory: false,
    source: 0,
    totalCount: 15,
    unlockedCount: 6,
    percentage: 40,
    hasPlatinum: false,
    list: [
      { apiName: 'er_elden_lord', gameName: 'Elden Ring', displayName: 'Elden Lord', description: 'Become the Elden Lord.', hidden: false, achieved: true, unlockTime: d(12), curProgress: 0, maxProgress: 0, globalPercentage: 8.2 },
      { apiName: 'er_malenia', gameName: 'Elden Ring', displayName: 'Malenia Slain', description: 'Defeat Malenia, Blade of Miquella.', hidden: false, achieved: true, unlockTime: d(10), curProgress: 0, maxProgress: 0, globalPercentage: 6.8 },
      { apiName: 'er_margit', gameName: 'Elden Ring', displayName: 'Margit Slain', description: 'Defeat Margit the Fell Omen.', hidden: false, achieved: true, unlockTime: d(45), curProgress: 0, maxProgress: 0, globalPercentage: 42.3 },
      { apiName: 'er_godrick', gameName: 'Elden Ring', displayName: 'Godrick Slain', description: 'Defeat Godrick the Grafted.', hidden: false, achieved: true, unlockTime: d(43), curProgress: 0, maxProgress: 0, globalPercentage: 38.1 },
      { apiName: 'er_lichdragon', gameName: 'Elden Ring', displayName: 'Lichdragon Felled', description: 'Defeat Lichdragon Fortissax.', hidden: false, achieved: true, unlockTime: d(22), curProgress: 0, maxProgress: 0, globalPercentage: 18.4 },
      { apiName: 'er_shardbearers', gameName: 'Elden Ring', displayName: 'Shardbearers Slain', description: 'Defeat all shardbearers.', hidden: false, achieved: true, unlockTime: d(15), curProgress: 0, maxProgress: 0, globalPercentage: 11.5 },
      { apiName: 'er_rennala', gameName: 'Elden Ring', displayName: 'Rennala Slain', description: 'Defeat Rennala, Queen of the Full Moon.', hidden: false, achieved: false, unlockTime: 0, curProgress: 0, maxProgress: 0, globalPercentage: 55.2 },
      { apiName: 'er_mohg', gameName: 'Elden Ring', displayName: 'Mohg Slain', description: 'Defeat Mohg, Lord of Blood.', hidden: false, achieved: false, unlockTime: 0, curProgress: 0, maxProgress: 0, globalPercentage: 24.6 },
      { apiName: 'er_radahn', gameName: 'Elden Ring', displayName: 'Radahn Slain', description: 'Defeat Starscourge Radahn.', hidden: false, achieved: false, unlockTime: 0, curProgress: 0, maxProgress: 0, globalPercentage: 33.7 },
      { apiName: 'er_rykard', gameName: 'Elden Ring', displayName: 'Rykard Slain', description: 'Defeat Rykard, Lord of Blasphemy.', hidden: false, achieved: false, unlockTime: 0, curProgress: 0, maxProgress: 0, globalPercentage: 29.1 },
      { apiName: 'er_ancestor', gameName: 'Elden Ring', displayName: 'Ancestor Spirit', description: 'Defeat the Ancestor Spirit.', hidden: false, achieved: false, unlockTime: 0, curProgress: 0, maxProgress: 0, globalPercentage: 22.8 },
      { apiName: 'er_astel', gameName: 'Elden Ring', displayName: 'Astel Slain', description: 'Defeat Astel, Naturalborn of the Void.', hidden: false, achieved: false, unlockTime: 0, curProgress: 0, maxProgress: 0, globalPercentage: 16.3 },
      { apiName: 'er_dragon', gameName: 'Elden Ring', displayName: 'Dragon Communion', description: 'Acquire a Dragon Communion incantation.', hidden: false, achieved: false, unlockTime: 0, curProgress: 0, maxProgress: 0, globalPercentage: 45.9 },
      { apiName: 'er_legendary', gameName: 'Elden Ring', displayName: 'Legendary Armaments', description: 'Acquire all legendary armaments.', hidden: false, achieved: false, unlockTime: 0, curProgress: 0, maxProgress: 0, globalPercentage: 7.4 },
      { apiName: 'er_god_arm', gameName: 'Elden Ring', displayName: 'God Slaying Armament', description: 'Forge a god-slaying armament.', hidden: false, achieved: false, unlockTime: 0, curProgress: 0, maxProgress: 0, globalPercentage: 13.2 },
    ],
  },

  // ── Hollow Knight — good progress (67%), best = silver ─────────────
  {
    appId: 'mock-9902',
    gameName: 'Hollow Knight',
    storeHeaderImageUrl: null,
    installDir: null,
    hasUsableInstallDirectory: false,
    source: 0,
    totalCount: 12,
    unlockedCount: 8,
    percentage: 66.7,
    hasPlatinum: false,
    list: [
      { apiName: 'hk_hollow_knight', gameName: 'Hollow Knight', displayName: 'Hollow Knight', description: 'Seal the Hollow Knight in the Black Egg.', hidden: false, achieved: true, unlockTime: d(60), curProgress: 0, maxProgress: 0, globalPercentage: 26.8 },
      { apiName: 'hk_true_ending', gameName: 'Hollow Knight', displayName: 'Sealed Siblings', description: 'Witness the true ending.', hidden: false, achieved: true, unlockTime: d(55), curProgress: 0, maxProgress: 0, globalPercentage: 19.2 },
      { apiName: 'hk_radiance', gameName: 'Hollow Knight', displayName: 'Embrace the Void', description: 'Defeat the Radiance.', hidden: false, achieved: true, unlockTime: d(50), curProgress: 0, maxProgress: 0, globalPercentage: 14.1 },
      { apiName: 'hk_dream_nail', gameName: 'Hollow Knight', displayName: 'Awakening', description: 'Awaken the Dream Nail.', hidden: false, achieved: true, unlockTime: d(75), curProgress: 0, maxProgress: 0, globalPercentage: 38.5 },
      { apiName: 'hk_grubs', gameName: 'Hollow Knight', displayName: 'Grubsong', description: 'Rescue all grubs.', hidden: false, achieved: true, unlockTime: d(65), curProgress: 0, maxProgress: 0, globalPercentage: 22.4 },
      { apiName: 'hk_charms', gameName: 'Hollow Knight', displayName: 'Charm Seeker', description: 'Acquire all charms.', hidden: false, achieved: true, unlockTime: d(58), curProgress: 0, maxProgress: 0, globalPercentage: 17.9 },
      { apiName: 'hk_colosseum', gameName: 'Hollow Knight', displayName: 'Trial of the Fool', description: 'Complete the Trial of the Fool.', hidden: false, achieved: true, unlockTime: d(48), curProgress: 0, maxProgress: 0, globalPercentage: 11.7 },
      { apiName: 'hk_pantheon', gameName: 'Hollow Knight', displayName: 'Pantheon Climber', description: 'Complete Pantheon 4.', hidden: false, achieved: true, unlockTime: d(40), curProgress: 0, maxProgress: 0, globalPercentage: 8.3 },
      { apiName: 'hk_all_bosses', gameName: 'Hollow Knight', displayName: 'Nightmare King', description: 'Defeat Grimm in his nightmare form.', hidden: false, achieved: false, unlockTime: 0, curProgress: 0, maxProgress: 0, globalPercentage: 15.6 },
      { apiName: 'hk_steel_soul', gameName: 'Hollow Knight', displayName: 'Steel Soul', description: 'Complete Steel Soul mode.', hidden: false, achieved: false, unlockTime: 0, curProgress: 0, maxProgress: 0, globalPercentage: 6.1 },
      { apiName: 'hk_godmaster', gameName: 'Hollow Knight', displayName: 'Godmaster', description: 'Bind and conquer all Pantheons.', hidden: false, achieved: false, unlockTime: 0, curProgress: 0, maxProgress: 0, globalPercentage: 3.8 },
      { apiName: 'hk_map', gameName: 'Hollow Knight', displayName: 'Map Maker', description: 'Complete the full map of Hallownest.', hidden: false, achieved: false, unlockTime: 0, curProgress: 0, maxProgress: 0, globalPercentage: 42.1 },
    ],
  },

  // ── Celeste — 100%, PLATINUM ───────────────────────────────────────
  {
    appId: 'mock-9903',
    gameName: 'Celeste',
    storeHeaderImageUrl: null,
    installDir: null,
    hasUsableInstallDirectory: false,
    source: 0,
    totalCount: 10,
    unlockedCount: 10,
    percentage: 100,
    hasPlatinum: true,
    list: [
      { apiName: 'cel_summit', gameName: 'Celeste', displayName: 'Summit', description: 'Reach the summit of Celeste Mountain.', hidden: false, achieved: true, unlockTime: d(8), curProgress: 0, maxProgress: 0, globalPercentage: 34.7 },
      { apiName: 'cel_core', gameName: 'Celeste', displayName: 'Heart of the Mountain', description: 'Clear the Core.', hidden: false, achieved: true, unlockTime: d(6), curProgress: 0, maxProgress: 0, globalPercentage: 21.3 },
      { apiName: 'cel_farewell', gameName: 'Celeste', displayName: 'Farewell', description: 'Complete Chapter 9.', hidden: false, achieved: true, unlockTime: d(4), curProgress: 0, maxProgress: 0, globalPercentage: 12.6 },
      { apiName: 'cel_full_clear', gameName: 'Celeste', displayName: 'Gotta Go Fast', description: 'Clear a chapter in under 1 minute.', hidden: false, achieved: true, unlockTime: d(14), curProgress: 0, maxProgress: 0, globalPercentage: 8.9 },
      { apiName: 'cel_all_berries', gameName: 'Celeste', displayName: 'Berry Collector', description: 'Collect 175 strawberries.', hidden: false, achieved: true, unlockTime: d(7), curProgress: 0, maxProgress: 0, globalPercentage: 5.4 },
      { apiName: 'cel_hearts', gameName: 'Celeste', displayName: 'Crystal Heart', description: 'Collect all crystal hearts.', hidden: false, achieved: true, unlockTime: d(5), curProgress: 0, maxProgress: 0, globalPercentage: 9.2 },
      { apiName: 'cel_winged_gold', gameName: 'Celeste', displayName: 'Golden Berry', description: 'Collect a golden strawberry.', hidden: false, achieved: true, unlockTime: d(3), curProgress: 0, maxProgress: 0, globalPercentage: 3.1 },
      { apiName: 'cel_assist', gameName: 'Celeste', displayName: 'No Assists', description: 'Complete the game without Assist Mode.', hidden: false, achieved: true, unlockTime: d(9), curProgress: 0, maxProgress: 0, globalPercentage: 26.8 },
      { apiName: 'cel_dashless', gameName: 'Celeste', displayName: 'Dashless', description: 'Clear a chapter without dashing.', hidden: false, achieved: true, unlockTime: d(11), curProgress: 0, maxProgress: 0, globalPercentage: 7.7 },
      { apiName: 'cel_deathless', gameName: 'Celeste', displayName: 'Deathless', description: 'Clear a chapter without dying.', hidden: false, achieved: true, unlockTime: d(13), curProgress: 0, maxProgress: 0, globalPercentage: 14.2 },
    ],
  },
];
