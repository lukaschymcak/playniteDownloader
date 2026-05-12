import type { GameAchievements } from '../../../../shared/types';
import { GameCard } from './GameCard';

interface GameGridProps {
  games: GameAchievements[];
  filter: string;
  onSelect: (game: GameAchievements) => void;
}

export function GameGrid({ games, filter, onSelect }: GameGridProps): JSX.Element {
  const q = filter.trim().toLowerCase();
  const filtered = q ? games.filter((g) => g.gameName.toLowerCase().includes(q)) : games;
  const sorted = [...filtered].sort((a, b) => b.percentage - a.percentage);

  if (sorted.length === 0) {
    return <div className="ach-empty">{q ? 'No games match filter.' : 'No achievement data found.'}</div>;
  }

  return (
    <div className="ach-game-grid">
      {sorted.map((game) => (
        <GameCard key={game.appId} game={game} onClick={() => onSelect(game)} />
      ))}
    </div>
  );
}
