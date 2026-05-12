import type { GameAchievements } from '../../../../shared/types';
import { GameCard } from './GameCard';

type SortMode = 'completion' | 'recent' | 'name';

interface GameGridProps {
  games: GameAchievements[];
  filter: string;
  sort: SortMode;
  onSelect: (game: GameAchievements) => void;
}

export function GameGrid({ games, filter, sort, onSelect }: GameGridProps): JSX.Element {
  const q = filter.trim().toLowerCase();
  const filtered = q ? games.filter((g) => g.gameName.toLowerCase().includes(q)) : games;

  const sorted = [...filtered].sort((a, b) => {
    if (sort === 'name') return a.gameName.localeCompare(b.gameName);
    if (sort === 'recent') {
      const latestA = Math.max(0, ...a.list.filter(x => x.achieved).map(x => x.unlockTime));
      const latestB = Math.max(0, ...b.list.filter(x => x.achieved).map(x => x.unlockTime));
      return latestB - latestA;
    }
    return b.percentage - a.percentage;
  });

  if (sorted.length === 0) {
    return <div className="v3-empty">{q ? 'No games match filter.' : 'No achievement data found.'}</div>;
  }

  return (
    <div className="v3-grid">
      {sorted.map((game) => (
        <GameCard key={game.appId} game={game} onClick={() => onSelect(game)} />
      ))}
    </div>
  );
}
