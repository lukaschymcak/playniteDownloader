import { useState } from 'react';
import type { GameAchievements, UserProfile } from '../../../shared/types';
import type { PlayerStats } from '../lib/achievementStats';
import { buildMonthlyUnlockData } from '../lib/achievementStats';
import { ProfileHeader } from '../components/achievements/ProfileHeader';
import { StatsStrip } from '../components/achievements/StatsStrip';
import { FilterBar } from '../components/achievements/FilterBar';
import { GameGrid } from '../components/achievements/GameGrid';
import { AchievementDetailView } from './AchievementDetailView';

interface AchievementsViewProps {
  games: GameAchievements[];
  profile: UserProfile;
  stats: PlayerStats;
  loading: boolean;
  onRefresh: () => Promise<void>;
  onSaveProfile: (profile: UserProfile) => Promise<void>;
}

export function AchievementsView({
  games,
  profile,
  stats,
  loading,
  onRefresh,
  onSaveProfile
}: AchievementsViewProps): JSX.Element {
  const [filter, setFilter] = useState('');
  const [selected, setSelected] = useState<GameAchievements | null>(null);

  const monthlyData = buildMonthlyUnlockData(games);

  if (selected) {
    const live = games.find((g) => g.appId === selected.appId) ?? selected;
    return (
      <AchievementDetailView
        game={live}
        profile={profile}
        onSaveProfile={onSaveProfile}
        onBack={() => setSelected(null)}
      />
    );
  }

  return (
    <div className="achievements-page">
      <ProfileHeader profile={profile} stats={stats} onSave={onSaveProfile} />
      <StatsStrip games={games} monthlyData={monthlyData} />
      <FilterBar filter={filter} onFilterChange={setFilter} onRefresh={onRefresh} loading={loading} />
      <div className="ach-game-grid-scroll">
        <GameGrid games={games} filter={filter} onSelect={setSelected} />
      </div>
    </div>
  );
}
