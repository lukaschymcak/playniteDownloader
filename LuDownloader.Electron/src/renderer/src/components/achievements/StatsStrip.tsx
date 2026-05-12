import type { GameAchievements } from '../../../../shared/types';
import type { PlayerStats, MonthBarBucket, RecentUnlock } from '../../lib/achievementStats';
import { GradeIcon } from './GradeIcon';
import { MonthlyBars } from './MonthlyChart';

interface StatsStripProps {
  games: GameAchievements[];
  stats: PlayerStats;
  monthlyData: MonthBarBucket[];
  recentUnlocks: RecentUnlock[];
  onSelectGame: (appId: string) => void;
}

const XP_PER_TIER: { tier: 'platinum' | 'gold' | 'silver' | 'bronze'; label: string; xp: number }[] = [
  { tier: 'bronze',   label: 'Bronze',   xp: 50  },
  { tier: 'silver',   label: 'Silver',   xp: 100 },
  { tier: 'gold',     label: 'Gold',     xp: 200 },
  { tier: 'platinum', label: 'Platinum', xp: 500 },
];

function DonutRing({ pct, size = 100, stroke = 10 }: { pct: number; size?: number; stroke?: number }): JSX.Element {
  const r = (size - stroke) / 2;
  const c = 2 * Math.PI * r;
  const dash = c * (pct / 100);
  return (
    <svg width={size} height={size} viewBox={`0 0 ${size} ${size}`} className="donut-ring">
      <circle cx={size / 2} cy={size / 2} r={r} fill="none" stroke="var(--surface-3)" strokeWidth={stroke} />
      <circle
        cx={size / 2} cy={size / 2} r={r}
        fill="none"
        stroke="var(--accent)"
        strokeWidth={stroke}
        strokeDasharray={`${dash} ${c}`}
        strokeLinecap="round"
        transform={`rotate(-90 ${size / 2} ${size / 2})`}
      />
      <text
        x="50%" y="50%"
        textAnchor="middle"
        dominantBaseline="central"
        style={{ fontSize: size * 0.2, fontWeight: 700, fill: 'var(--fg-1)' }}
      >
        {pct}%
      </text>
    </svg>
  );
}

export function StatsStrip({ games: _games, stats, monthlyData, recentUnlocks, onSelectGame }: StatsStripProps): JSX.Element {
  const { trophyCounts, totalUnlocked, totalAchievements, level, levelXp, levelSize, toNext, totalPoints } = stats;
  const pct = totalAchievements > 0 ? Math.round((totalUnlocked / totalAchievements) * 100) : 0;
  const tierRows: { tier: 'platinum' | 'gold' | 'silver' | 'bronze'; count: number; pct: number }[] = [
    { tier: 'platinum', count: trophyCounts.platinum, pct: totalUnlocked > 0 ? Math.round((trophyCounts.platinum / totalUnlocked) * 100) : 0 },
    { tier: 'gold',     count: trophyCounts.gold,     pct: totalUnlocked > 0 ? Math.round((trophyCounts.gold     / totalUnlocked) * 100) : 0 },
    { tier: 'silver',   count: trophyCounts.silver,   pct: totalUnlocked > 0 ? Math.round((trophyCounts.silver   / totalUnlocked) * 100) : 0 },
    { tier: 'bronze',   count: trophyCounts.bronze,   pct: totalUnlocked > 0 ? Math.round((trophyCounts.bronze   / totalUnlocked) * 100) : 0 },
  ];

  const lvlPct = Math.round((levelXp / levelSize) * 100);
  const prevCount = monthlyData.length >= 2 ? monthlyData[monthlyData.length - 2].count : 0;
  const curCount  = monthlyData.length >= 1 ? monthlyData[monthlyData.length - 1].count : 0;
  const delta = prevCount > 0 ? Math.round(((curCount - prevCount) / prevCount) * 100) : 0;

  return (
    <section className="bento">
      {/* Trophy progress */}
      <div className="bento-cell prog">
        <div className="b-k">Trophy progress</div>
        <div className="b-prog">
          <div className="b-prog-num">
            <span className="b-prog-v">{totalUnlocked.toLocaleString()}</span>
            <span className="b-prog-tot">/ {totalAchievements.toLocaleString()} unlocked</span>
          </div>
          <DonutRing pct={pct} size={100} />
        </div>
        <div className="b-prog-tiers">
          {tierRows.map((t) => (
            <div key={t.tier} className="b-tier">
              <GradeIcon grade={t.tier} size={12} />
              <span className="b-tier-c">{t.count}</span>
              <span className="b-tier-p">{t.pct}%</span>
            </div>
          ))}
        </div>
      </div>

      {/* Level */}
      <div className="bento-cell lvl">
        <div className="b-k">Level</div>
        <div className="b-level">
          <div className="b-lvl-num">{level}</div>
          <div className="b-lvl-meta">
            <div className="b-lvl-xp">{totalPoints.toLocaleString()} XP</div>
            <div className="b-lvl-bar"><span style={{ width: `${lvlPct}%` }} /></div>
            <div className="b-lvl-next">{toNext} / {levelSize} XP to Lv {level + 1}</div>
          </div>
        </div>
      </div>

      {/* XP per trophy */}
      <div className="bento-cell xpcard">
        <div className="b-k">XP per trophy</div>
        <div className="b-xp-grid">
          {XP_PER_TIER.map((t) => (
            <div key={t.tier} className="b-xp-cell">
              <GradeIcon grade={t.tier} size={12} />
              <span className="b-xp-tier">{t.label}</span>
              <span className="b-xp-val">{t.xp}</span>
            </div>
          ))}
        </div>
      </div>

      {/* Trophies earned bar chart */}
      <div className="bento-cell graph">
        <div className="b-k">
          <span>Trophies earned</span>
          {delta !== 0 && (
            <span className="b-delta">{delta > 0 ? '▲' : '▼'} {Math.abs(delta)}% vs last month</span>
          )}
        </div>
        <MonthlyBars data={monthlyData} />
      </div>

      {/* Recent unlocks */}
      <div className="bento-cell recent">
        <div className="b-k">Recent unlocks</div>
        <div className="b-recent">
          {recentUnlocks.length === 0 ? (
            <div style={{ color: 'var(--fg-4)', fontSize: 12, padding: '12px 0' }}>No unlocks yet.</div>
          ) : recentUnlocks.map((u, i) => (
            <div key={i} className="b-recent-row" onClick={() => onSelectGame(u.appId)}>
              <div
                className="b-recent-art"
                style={{ background: `oklch(0.38 0.10 ${(parseInt(u.appId, 10) * 137) % 360})` }}
              >
                <GradeIcon grade={u.grade} size={12} />
              </div>
              <div className="b-recent-body" style={{ minWidth: 0 }}>
                <div className="b-recent-name">{u.displayName}</div>
                <div className="b-recent-meta">{u.gameName} · {u.globalPercentage.toFixed(1)}% rarity</div>
              </div>
              <div className="b-recent-xp">+{u.xp}</div>
            </div>
          ))}
        </div>
      </div>
    </section>
  );
}
