import { useCallback, useEffect, useState } from 'react';
import type { GameAchievements } from '../../../shared/types';

export interface UseAchievementsResult {
  games: GameAchievements[];
  loading: boolean;
  refresh: () => Promise<void>;
}

export function useAchievements(): UseAchievementsResult {
  const [map, setMap] = useState<Map<string, GameAchievements>>(new Map());
  const [loading, setLoading] = useState(true);

  const refresh = useCallback(async () => {
    setLoading(true);
    try {
      const list = await window.api.achievements.listGames();
      setMap(new Map(list.map((g) => [g.appId, g])));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void refresh();
    const unsub = window.api.on('achievements:changed', (data) => {
      const game = data as GameAchievements;
      setMap((prev) => new Map(prev).set(game.appId, game));
    });
    return () => { unsub(); };
  }, [refresh]);

  return { games: [...map.values()], loading, refresh };
}
