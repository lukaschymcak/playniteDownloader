import { useMemo, useState } from 'react';
import type { Achievement, GameAchievements, UserProfile } from '../../../shared/types';
import { achievementPoints, appIdToHue, gradeAchievement } from '../lib/achievementStats';
import { Icon } from '../icons';
import { GradeIcon } from '../components/achievements/GradeIcon';

type SortKey = 'date' | 'rarity' | 'xp' | 'name';
type TabKey = 'all' | 'unlocked' | 'locked' | 'featured';
type RarityFilter = 'all' | 'gold' | 'silver' | 'bronze';

const TIER_LABELS = {
  platinum: 'Platinum',
  gold: 'Gold',
  silver: 'Silver',
  bronze: 'Bronze'
} as const;

interface AchievementDetailViewProps {
  game: GameAchievements;
  profile: UserProfile;
  onSaveProfile: (profile: UserProfile) => Promise<void>;
  onBack: () => void;
}

export function AchievementDetailView({ game, profile, onSaveProfile, onBack }: AchievementDetailViewProps): JSX.Element {
  const [sort, setSort] = useState<SortKey>('date');
  const [tab, setTab] = useState<TabKey>('all');
  const [query, setQuery] = useState('');
  const [rarityFilter, setRarityFilter] = useState<RarityFilter>('all');
  const [showHidden, setShowHidden] = useState(true);

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

  const normalizedQuery = query.trim().toLowerCase();
  const allAchievements = useMemo(() => {
    let list = game.list;

    if (normalizedQuery) {
      list = list.filter((a) => {
        const visibleName = a.hidden && !a.achieved ? 'hidden achievement' : a.displayName;
        const visibleDesc = a.hidden && !a.achieved ? 'continue playing to unlock.' : a.description;
        return (
          visibleName.toLowerCase().includes(normalizedQuery)
          || visibleDesc.toLowerCase().includes(normalizedQuery)
          || a.apiName.toLowerCase().includes(normalizedQuery)
        );
      });
    }

    if (!showHidden) {
      list = list.filter((a) => !(a.hidden && !a.achieved));
    }

    if (rarityFilter !== 'all') {
      list = list.filter((a) => gradeAchievement(a.globalPercentage) === rarityFilter);
    }

    return list;
  }, [game.list, normalizedQuery, showHidden, rarityFilter]);

  const unlocked = allAchievements.filter((a) => a.achieved);
  const locked = allAchievements.filter((a) => !a.achieved);
  const featured = unlocked.filter((a) => featuredIds.includes(a.apiName));
  const unlockedRegular = unlocked.filter((a) => !featuredIds.includes(a.apiName));

  const tierCounts = useMemo(() => {
    let gold = 0;
    let silver = 0;
    let bronze = 0;
    for (const a of game.list) {
      if (!a.achieved) continue;
      const g = gradeAchievement(a.globalPercentage);
      if (g === 'gold') gold++;
      else if (g === 'silver') silver++;
      else bronze++;
    }
    return { platinum: game.hasPlatinum ? 1 : 0, gold, silver, bronze };
  }, [game.hasPlatinum, game.list]);

  const gameXp = useMemo(() => {
    let total = game.hasPlatinum ? 500 : 0;
    for (const a of game.list) {
      if (!a.achieved) continue;
      total += achievementPoints(a.globalPercentage);
    }
    return total;
  }, [game.hasPlatinum, game.list]);

  function sortAchievements(list: Achievement[]): Achievement[] {
    return [...list].sort((a, b) => {
      if (sort === 'date') return (b.unlockTime ?? 0) - (a.unlockTime ?? 0);
      if (sort === 'rarity') return a.globalPercentage - b.globalPercentage;
      if (sort === 'xp') return achievementPoints(b.globalPercentage) - achievementPoints(a.globalPercentage);
      return a.displayName.localeCompare(b.displayName);
    });
  }

  const listAll = sortAchievements(allAchievements);
  const listFeatured = sortAchievements(featured);
  const listUnlocked = sortAchievements(unlockedRegular);
  const listLocked = sortAchievements(locked);
  const selectedList = tab === 'all'
    ? listAll
    : tab === 'featured'
      ? listFeatured
      : tab === 'locked'
        ? listLocked
        : listUnlocked;

  const rarest = game.list.reduce<Achievement | null>((min, current) => {
    if (!min) return current;
    return current.globalPercentage < min.globalPercentage ? current : min;
  }, null);

  const hue = appIdToHue(game.appId);

  return (
    <div className="ach-detail-page achievements-page">
      <header className="dt-head">
        <button className="dt-back" onClick={onBack}>
          <Icon name="back" size={14} />
          <span>Back to Achievements</span>
        </button>
        <div className="dt-crumb">Achievements / <strong>{game.gameName}</strong></div>
      </header>

      <section className="bento dt-bento">
        <div className="bento-cell dt-hero">
          <div className="dt-hero-cover">
            {game.storeHeaderImageUrl ? (
              <img
                className="ach-game-card-img"
                src={game.storeHeaderImageUrl}
                alt=""
                onError={(e) => { (e.currentTarget as HTMLImageElement).style.display = 'none'; }}
              />
            ) : (
              <div className="ach-game-card-img-placeholder">{game.gameName.slice(0, 1).toUpperCase()}</div>
            )}
          </div>
          <div className="dt-hero-body">
            <div className="dt-hero-titlerow">
              <h1 className="dt-hero-name">{game.gameName}</h1>
              <span className="dt-platform" title="Steam"><Icon name="steam" size={14} /></span>
            </div>
            <div className="dt-hero-meta">
              <span>{game.unlockedCount} unlocked</span>
              <span className="v4-dot" />
              <span>{game.totalCount - game.unlockedCount} locked</span>
              <span className="v4-dot" />
              <span className="dt-rarest">
                Rarest · {rarest ? `${rarest.displayName} (${rarest.globalPercentage.toFixed(1)}%)` : '—'}
              </span>
            </div>
            <div className="dt-hero-progress">
              <div className="dt-hero-pgrow">
                <span className="mono dt-hero-counts">{game.unlockedCount} / {game.totalCount} <span style={{ color: 'var(--fg-4)' }}>trophies</span></span>
                <span className="dt-hero-pct">{game.percentage.toFixed(1)}% Complete</span>
              </div>
              <div className="dt-hero-bar"><span style={{ width: `${Math.min(game.percentage, 100)}%` }} /></div>
            </div>
            <div className="dt-hero-actions">
              <button className="btn ghost sm" onClick={() => setTab('featured')}>Manage featured</button>
            </div>
          </div>
        </div>

        <div className="bento-cell dt-donut">
          <div className="b-k">Completion</div>
          <div className="dt-donut-body">
            <DonutRing pct={Math.round(game.percentage)} size={120} stroke={10} />
          </div>
          <div className="dt-donut-foot">
            <span className="mono">{game.unlockedCount}/{game.totalCount}</span>
            {game.hasPlatinum && <span className="dt-plat-pill">Platinum earned</span>}
          </div>
        </div>

        <div className="bento-cell dt-tiers">
          <div className="b-k">Tier breakdown</div>
          <div className="dt-tier-grid">
            {(['platinum', 'gold', 'silver', 'bronze'] as const).map((tier) => (
              <div key={tier} className="dt-tier-cell">
                <GradeIcon grade={tier} size={14} />
                <div className="dt-tier-meta">
                  <span className="dt-tier-c mono">{tierCounts[tier]}</span>
                  <span className="dt-tier-l">{TIER_LABELS[tier]}</span>
                </div>
                <span className="dt-tier-xp mono">+{tierXp(tier)}</span>
              </div>
            ))}
          </div>
        </div>
      </section>

      <div className="dt-filterbar">
        <div className="dt-filterbar-main">
          <div className="v4-pill-row">
            <button className={`pill${tab === 'all' ? ' active' : ''}`} onClick={() => setTab('all')}>All <em>{allAchievements.length}</em></button>
            <button className={`pill${tab === 'unlocked' ? ' active' : ''}`} onClick={() => setTab('unlocked')}>Unlocked <em>{unlocked.length}</em></button>
            <button className={`pill${tab === 'locked' ? ' active' : ''}`} onClick={() => setTab('locked')}>Locked <em>{locked.length}</em></button>
            <button className={`pill${tab === 'featured' ? ' active' : ''}`} onClick={() => setTab('featured')}>Featured <em>{featured.length}</em></button>
          </div>
          <div className="dt-filter-right">
            <div className="input slim">
              <Icon name="search" size={13} />
              <input placeholder="Search trophies" value={query} onChange={(e) => setQuery(e.target.value)} />
            </div>
            <div className="dt-sort">
              <span className="dt-sort-l">Sort:</span>
              <select className="v3-sort-select" value={sort} onChange={(e) => setSort(e.target.value as SortKey)}>
                <option value="date">Date unlocked</option>
                <option value="rarity">Rarity</option>
                <option value="xp">XP</option>
                <option value="name">Name</option>
              </select>
            </div>
          </div>
        </div>
        <div className="dt-filterbar-extra">
          <span className="dt-filter-label">Rarity:</span>
          {(['gold', 'silver', 'bronze'] as const).map((grade) => (
            <button
              key={grade}
              className={`dt-grade-btn${rarityFilter === grade ? ' active' : ''}`}
              onClick={() => setRarityFilter(rarityFilter === grade ? 'all' : grade)}
            >
              <GradeIcon grade={grade} size={10} />
              {grade.charAt(0).toUpperCase() + grade.slice(1)}
            </button>
          ))}
          <div className="dt-filter-sep" />
          <button
            className={`dt-toggle-btn${showHidden ? ' active' : ''}`}
            onClick={() => setShowHidden(!showHidden)}
          >
            <Icon name="star" size={11} />
            Secret
          </button>
        </div>
      </div>

      <div className="dt-list-scroll">
        {tab !== 'locked' && featured.length > 0 && (
          <>
            <div className="dt-section-label">
              <Icon name="pin" size={11} />
              <span>Featured</span>
              <em>{featured.length}</em>
            </div>
            {listFeatured.map((a) => (
              <TrophyRow key={a.apiName} achievement={a} hue={hue} pinned onUnpin={() => void togglePin(a.apiName)} />
            ))}
          </>
        )}

        {(tab === 'all' || tab === 'unlocked') && (
          <>
            <div className="dt-section-label">
              <span className="dt-label-bar" />
              <span>Unlocked</span>
              <em>{unlocked.length}</em>
            </div>
            {listUnlocked.map((a) => (
              <TrophyRow key={a.apiName} achievement={a} hue={hue} onPin={() => void togglePin(a.apiName)} />
            ))}
          </>
        )}

        {(tab === 'all' || tab === 'locked') && (
          <>
            <div className="dt-section-label">
              <span className="dt-label-bar" />
              <span>Locked</span>
              <em>{locked.length}</em>
            </div>
            {listLocked.map((a) => (
              <TrophyRow key={a.apiName} achievement={a} hue={hue} locked />
            ))}
          </>
        )}

        {selectedList.length === 0 && (
          <div className="dt-locked-empty">
            <div className="dt-empty-mark"><Icon name="trophy" size={22} /></div>
            <div>
              <div className="dt-empty-title">No trophies in this filter</div>
              <div className="dt-empty-sub">Try a different filter or search query.</div>
            </div>
            <div className="dt-empty-xp mono">+{gameXp.toLocaleString()} XP earned</div>
          </div>
        )}
      </div>
    </div>
  );
}

function tierLabel(tier: 'gold' | 'silver' | 'bronze'): string {
  if (tier === 'gold') return 'Gold';
  if (tier === 'silver') return 'Silver';
  return 'Bronze';
}

function tierXp(tier: 'platinum' | 'gold' | 'silver' | 'bronze'): number {
  if (tier === 'platinum') return 500;
  if (tier === 'gold') return 200;
  if (tier === 'silver') return 100;
  return 50;
}

function tierFromAchievement(achievement: Achievement): 'gold' | 'silver' | 'bronze' {
  return gradeAchievement(achievement.globalPercentage);
}

function TrophyRow(
  {
    achievement,
    hue = 38,
    pinned = false,
    locked = false,
    onPin,
    onUnpin
  }: {
    achievement: Achievement;
    hue?: number;
    pinned?: boolean;
    locked?: boolean;
    onPin?: () => void;
    onUnpin?: () => void;
  }
): JSX.Element {
  const tier = tierFromAchievement(achievement);
  const unlockedDate = achievement.unlockTime
    ? new Date(achievement.unlockTime * 1000)
    : null;
  const dateText = unlockedDate
    ? unlockedDate.toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' })
    : '—';
  const timeText = unlockedDate
    ? unlockedDate.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' })
    : '';
  const xp = tierXp(tier);
  const name = achievement.hidden && locked ? 'Hidden Achievement' : achievement.displayName;
  const desc = achievement.hidden && locked ? 'Continue playing to unlock.' : achievement.description;
  const iconSrc = achievement.achieved
    ? (achievement.iconPath ?? achievement.iconUrl ?? '')
    : (achievement.iconGrayPath ?? achievement.iconGrayUrl ?? achievement.iconPath ?? achievement.iconUrl ?? '');

  const artBg = locked
    ? 'linear-gradient(135deg, oklch(0.24 0.01 240), oklch(0.18 0.01 240))'
    : `linear-gradient(135deg, oklch(0.4 0.12 ${hue}), oklch(0.22 0.08 ${hue}))`;

  return (
    <article className={`dt-row${pinned ? ' is-featured' : ''}${locked ? '' : ` tier-${tier}`}`}>
      <div className="t-art" style={{ width: 64, height: 64, background: artBg }}>
        {iconSrc ? (
          <img
            src={iconSrc.startsWith('http') ? iconSrc : `file:///${iconSrc.replace(/\\/g, '/')}`}
            alt=""
            style={{ width: '100%', height: '100%', objectFit: 'cover' }}
            onError={(e) => { (e.currentTarget as HTMLImageElement).style.display = 'none'; }}
          />
        ) : null}
        {locked && <Icon name="lock" size={20} />}
        {!locked && !iconSrc && (
          <span className="t-art-chip" style={{ background: `var(--${tier})`, color: `var(--${tier})` }} />
        )}
      </div>
      <div className="dt-row-body">
        <div className="dt-row-titlerow">
          <span className="dt-row-name">{name}</span>
          {pinned && <span className="dt-pin" title="Featured trophy"><Icon name="pin" size={10} /></span>}
        </div>
        <div className="dt-row-desc">{desc}</div>
      </div>
      <div className="dt-row-rarity">
        <span className={`dt-rarity-l tier-${tier}`}>{tierLabel(tier)}</span>
        <span className="mono dt-rarity-pct">{achievement.globalPercentage.toFixed(1)}%</span>
      </div>
      <div className="dt-row-when">
        <Icon name="edit" size={11} />
        <div>
          <div className="dt-when-d mono">{dateText}</div>
          {timeText && <div className="dt-when-t mono">{timeText}</div>}
        </div>
      </div>
      {(onPin || onUnpin) ? (
        <button className={`dt-xp tier-${tier}`} onClick={pinned ? onUnpin : onPin}>{pinned ? 'Unpin' : '+Pin'}</button>
      ) : (
        <span className={`dt-xp tier-${tier}`}>+{xp} XP</span>
      )}
    </article>
  );
}

function DonutRing({ pct = 62, size = 120, stroke = 10 }: { pct?: number; size?: number; stroke?: number }): JSX.Element {
  const r = (size - stroke) / 2;
  const c = 2 * Math.PI * r;
  const dash = c * (pct / 100);
  return (
    <svg width={size} height={size} viewBox={`0 0 ${size} ${size}`}>
      <circle cx={size / 2} cy={size / 2} r={r} fill="none" stroke="var(--surface-3)" strokeWidth={stroke} />
      <circle
        cx={size / 2}
        cy={size / 2}
        r={r}
        fill="none"
        stroke="var(--accent)"
        strokeWidth={stroke}
        strokeDasharray={`${dash} ${c}`}
        strokeDashoffset={c / 4}
        strokeLinecap="round"
        transform={`rotate(-90 ${size / 2} ${size / 2})`}
      />
      <text
        x="50%"
        y="50%"
        textAnchor="middle"
        dominantBaseline="central"
        style={{ fontFamily: 'Geist, system-ui, sans-serif', fontWeight: 600, fontSize: size * 0.22, fill: 'var(--fg-1)' }}
      >
        {pct}%
      </text>
    </svg>
  );
}
