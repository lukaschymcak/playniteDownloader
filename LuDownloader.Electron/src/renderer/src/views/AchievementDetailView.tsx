import { useState } from 'react';
import type { Achievement, GameAchievements, UserProfile } from '../../../shared/types';
import { gradeAchievement } from '../lib/achievementStats';
import { GradeIcon } from '../components/achievements/GradeIcon';
import { AchievementRow } from '../components/achievements/AchievementRow';
import { Icon } from '../icons';

type SortKey = 'time' | 'grade' | 'rarity' | 'name';

interface AchievementDetailViewProps {
  game: GameAchievements;
  profile: UserProfile;
  onSaveProfile: (profile: UserProfile) => Promise<void>;
  onBack: () => void;
}

export function AchievementDetailView({ game, profile, onSaveProfile, onBack }: AchievementDetailViewProps): JSX.Element {
  const [sort, setSort] = useState<SortKey>('time');
  const [showHidden, setShowHidden] = useState(true);
  const [showRarity, setShowRarity] = useState(true);

  const featuredIds: string[] = profile.featuredTrophiesByGame[game.appId] ?? [];

  async function togglePin(apiName: string): Promise<void> {
    const current = profile.featuredTrophiesByGame[game.appId] ?? [];
    const next = current.includes(apiName)
      ? current.filter((n) => n !== apiName)
      : [...current.slice(0, 3), apiName];
    await onSaveProfile({
      ...profile,
      featuredTrophiesByGame: { ...profile.featuredTrophiesByGame, [game.appId]: next }
    });
  }

  const unlocked = game.list.filter((a) => a.achieved);
  const locked = game.list.filter((a) => !a.achieved && (showHidden || !a.hidden));
  const featured = unlocked.filter((a) => featuredIds.includes(a.apiName));
  const unfeaturedUnlocked = unlocked.filter((a) => !featuredIds.includes(a.apiName));

  function sortAch(list: Achievement[]): Achievement[] {
    return [...list].sort((a, b) => {
      if (sort === 'time') return (b.unlockTime ?? 0) - (a.unlockTime ?? 0);
      if (sort === 'grade') return gradeRank(a.globalPercentage) - gradeRank(b.globalPercentage);
      if (sort === 'rarity') return a.globalPercentage - b.globalPercentage;
      return a.displayName.localeCompare(b.displayName);
    });
  }

  return (
    <div className="ach-detail-page">
      <div className="ach-detail-header">
        <button className="btn icon" onClick={onBack} title="Back">
          <Icon name="back" size={16} />
        </button>
        <span className="ach-detail-title">{game.gameName}</span>
        <span className="ach-pct-chip">{game.percentage.toFixed(1)}%</span>
      </div>

      <div className="ach-detail-toolbar">
        <div className="ach-sort-group">
          {(['time', 'grade', 'rarity', 'name'] as SortKey[]).map((k) => (
            <button key={k} className={`ach-sort-btn${sort === k ? ' active' : ''}`} onClick={() => setSort(k)}>
              {k.charAt(0).toUpperCase() + k.slice(1)}
            </button>
          ))}
        </div>
        <button
          className={`ach-toggle-btn${showHidden ? ' active' : ''}`}
          onClick={() => setShowHidden((v) => !v)}
        >
          Hidden
        </button>
        <button
          className={`ach-toggle-btn${showRarity ? ' active' : ''}`}
          onClick={() => setShowRarity((v) => !v)}
        >
          % Rarity
        </button>
      </div>

      <div className="ach-detail-scroll">
        {game.hasPlatinum && (
          <div className={`ach-platinum-row${game.percentage >= 100 ? ' unlocked' : ''}`}>
            <div className="ach-platinum-icon">
              <GradeIcon grade="platinum" size={32} />
            </div>
            <div>
              <div style={{ fontSize: 13, fontWeight: 700, color: 'var(--grade-platinum)' }}>Platinum Trophy</div>
              <div style={{ fontSize: 11, color: 'var(--fg-2)', marginTop: 2 }}>
                {game.percentage >= 100 ? 'All achievements unlocked!' : `${game.unlockedCount} / ${game.totalCount} remaining`}
              </div>
            </div>
          </div>
        )}

        {featured.length > 0 && (
          <>
            <div className="ach-section-label">Featured</div>
            {featured.map((a) => (
              <AchievementRow
                key={a.apiName}
                achievement={a}
                pinned
                onUnpin={() => void togglePin(a.apiName)}
                showRarity={showRarity}
              />
            ))}
          </>
        )}

        {unfeaturedUnlocked.length > 0 && (
          <>
            <div className="ach-section-label">Unlocked — {unlocked.length}</div>
            {sortAch(unfeaturedUnlocked).map((a) => (
              <AchievementRow
                key={a.apiName}
                achievement={a}
                pinned={false}
                onPin={() => void togglePin(a.apiName)}
                showRarity={showRarity}
              />
            ))}
          </>
        )}

        {locked.length > 0 && (
          <>
            <div className="ach-section-label">Locked — {locked.length}</div>
            {sortAch(locked).map((a) => (
              <AchievementRow key={a.apiName} achievement={a} showRarity={showRarity} />
            ))}
          </>
        )}
      </div>
    </div>
  );
}

function gradeRank(pct: number): number {
  const g = gradeAchievement(pct);
  if (g === 'gold') return 0;
  if (g === 'silver') return 1;
  return 2;
}
