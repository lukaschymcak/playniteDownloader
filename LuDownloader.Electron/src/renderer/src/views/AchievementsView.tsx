import { useMemo, useState } from 'react';
import type { GameAchievements, UserProfile } from '../../../shared/types';
import type { PlayerStats } from '../lib/achievementStats';
import { buildMonthlyBarData, buildRecentUnlocks } from '../lib/achievementStats';
import { ProfileHeader } from '../components/achievements/ProfileHeader';
import { StatsStrip } from '../components/achievements/StatsStrip';
import { GameGrid } from '../components/achievements/GameGrid';
import { AchievementDetailView } from './AchievementDetailView';

type SortMode = 'completion' | 'recent' | 'name';

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
  loading: _loading,
  onRefresh: _onRefresh,
  onSaveProfile
}: AchievementsViewProps): JSX.Element {
  const [filter, setFilter] = useState('');
  const [sort, setSort] = useState<SortMode>('completion');
  const [selected, setSelected] = useState<GameAchievements | null>(null);

  const monthlyData = useMemo(() => buildMonthlyBarData(games, 6), [games]);
  const recentUnlocks = useMemo(() => buildRecentUnlocks(games, 10), [games]);

  function handleSelectGame(appId: string): void {
    const game = games.find((g) => g.appId === appId) ?? null;
    if (game) setSelected(game);
  }

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
      <ProfileHeader
        profile={profile}
        stats={stats}
        filter={filter}
        onFilterChange={setFilter}
        sort={sort}
        onSortChange={(v) => setSort(v as SortMode)}
        onSave={onSaveProfile}
      />
      <StatsStrip
        games={games}
        stats={stats}
        monthlyData={monthlyData}
        recentUnlocks={recentUnlocks}
        onSelectGame={handleSelectGame}
      />
      <div className="v3-grid-head">
        <span>Your games <em>{games.length}</em></span>
      </div>
      <div className="v3-grid-scroll">
        <GameGrid
          games={games}
          filter={filter}
          sort={sort}
          onSelect={setSelected}
        />
      </div>
    </div>
  );
}
