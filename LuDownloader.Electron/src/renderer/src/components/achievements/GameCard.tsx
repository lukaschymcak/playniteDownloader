import type { GameAchievements } from '../../../../shared/types';
import { GradeIcon } from './GradeIcon';

interface GameCardProps {
  game: GameAchievements;
  onClick: () => void;
}

export function GameCard({ game, onClick }: GameCardProps): JSX.Element {
  const imgSrc = game.storeHeaderImageUrl
    ?? `https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/${game.appId}/header.jpg`;

  return (
    <button className={`v3-card${game.hasPlatinum ? ' is-platinum' : ''}`} onClick={onClick}>
      <div className="v3-card-cover">
        <img
          src={imgSrc}
          alt=""
          onError={(e) => { (e.currentTarget as HTMLImageElement).style.display = 'none'; }}
        />
        {game.hasPlatinum && (
          <div className="v3-card-plat-banner">
            <GradeIcon grade="platinum" size={10} /> Platinum
          </div>
        )}
      </div>
      <div className="v3-card-foot">
        <div className="v3-card-titlerow">
          <span className="v3-card-name" title={game.gameName}>{game.gameName}</span>
          <span className="v3-card-pct">{game.percentage.toFixed(0)}%</span>
        </div>
        <div className="v3-card-bar">
          <span style={{ width: `${Math.min(game.percentage, 100)}%` }} />
        </div>
        <div className="v3-card-meta">
          <span>{game.unlockedCount}/{game.totalCount}</span>
          {game.hasPlatinum ? (
            <span className="v3-card-plat"><GradeIcon grade="platinum" size={10} /> Platinum</span>
          ) : (
            <span className="v3-card-recent">{game.percentage.toFixed(0)}% done</span>
          )}
        </div>
      </div>
    </button>
  );
}
