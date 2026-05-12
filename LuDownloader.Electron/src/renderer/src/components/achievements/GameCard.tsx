import type { GameAchievements } from '../../../../shared/types';
import { Icon } from '../../icons';

interface GameCardProps {
  game: GameAchievements;
  onClick: () => void;
}

function rarityClass(game: GameAchievements): string {
  if (game.hasPlatinum) return 'rarity-platinum';
  if (game.percentage >= 50) return 'rarity-gold';
  if (game.percentage >= 25) return 'rarity-silver';
  return 'rarity-bronze';
}

export function GameCard({ game, onClick }: GameCardProps): JSX.Element {
  const imgSrc = game.storeHeaderImageUrl
    ?? `https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/${game.appId}/header.jpg`;

  return (
    <button className={`ach-game-card ${rarityClass(game)}`} onClick={onClick}>
      <img
        className="ach-game-card-img"
        src={imgSrc}
        alt=""
        onError={(e) => { (e.currentTarget as HTMLImageElement).style.display = 'none'; }}
      />
      <div className="ach-game-card-body">
        <div className="ach-game-card-name" title={game.gameName}>{game.gameName}</div>
        <div className="ach-game-card-progress-track">
          <div
            className="ach-game-card-progress-fill"
            style={{ width: `${Math.min(game.percentage, 100)}%` }}
          />
        </div>
        {game.hasPlatinum ? (
          <div className="ach-game-card-stats platinum">
            <Icon name="trophy" size={11} />
            <span>Platinum</span>
          </div>
        ) : (
          <div className="ach-game-card-stats">
            {game.unlockedCount}/{game.totalCount} · {game.percentage.toFixed(0)}%
          </div>
        )}
      </div>
    </button>
  );
}
