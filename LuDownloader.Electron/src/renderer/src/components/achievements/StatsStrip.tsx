import type { GameAchievements } from '../../../../shared/types';
import type { MonthBucket } from '../../lib/achievementStats';
import { MonthlyChart } from './MonthlyChart';

interface StatsStripProps {
  games: GameAchievements[];
  monthlyData: MonthBucket[];
}

export function StatsStrip({ games, monthlyData }: StatsStripProps): JSX.Element {
  const totalUnlocked = games.reduce((s, g) => s + g.unlockedCount, 0);
  const totalAch = games.reduce((s, g) => s + g.totalCount, 0);
  const pct = totalAch > 0 ? Math.round((totalUnlocked / totalAch) * 100) : 0;

  return (
    <div className="stats-strip">
      <div className="stats-strip-chart">
        <MonthlyChart data={monthlyData} />
      </div>
      <div className="stats-strip-overall">
        <div className="stats-strip-label">Overall</div>
        <div className="stats-strip-counts">{totalUnlocked} / {totalAch}</div>
        <div className="stats-strip-track">
          <div className="stats-strip-fill" style={{ width: `${pct}%` }} />
        </div>
        <div className="stats-strip-pct">{pct}%</div>
      </div>
    </div>
  );
}
