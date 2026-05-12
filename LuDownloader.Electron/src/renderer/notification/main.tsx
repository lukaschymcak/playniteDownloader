import { useEffect, useState, type CSSProperties } from 'react';
import { createRoot } from 'react-dom/client';
import type { Achievement } from '../../shared/types';
import './notification.css';

interface NotificationPayload {
  achievement: Achievement;
  type: 'unlock' | 'progress';
  progressCurrent: number;
  progressMax: number;
  notificationPosition?: string;
}

type Rarity = 'very-rare' | 'rare' | 'normal';

function getRarity(pct: number): Rarity {
  if (pct > 0 && pct < 15) return 'very-rare';
  if (pct >= 15 && pct < 30) return 'rare';
  return 'normal';
}

function rarityLabel(rarity: Rarity, pct: number): string {
  const pctStr = pct > 0 ? ` — ${pct.toFixed(1)}%` : '';
  if (rarity === 'very-rare') return `Very Rare${pctStr}`;
  if (rarity === 'rare') return `Rare${pctStr}`;
  return pct > 0 ? `Common${pctStr}` : 'Common';
}

function headerText(type: NotificationPayload['type'], rarity: Rarity): string {
  if (type === 'progress') return 'Achievement Progress';
  if (rarity === 'very-rare') return 'Very Rare Achievement Unlocked!';
  if (rarity === 'rare') return 'Rare Achievement Unlocked!';
  return 'Achievement Unlocked!';
}

function App(): JSX.Element {
  const [item, setItem] = useState<NotificationPayload | null>(null);
  const [phase, setPhase] = useState<'in' | 'out' | 'hidden'>('hidden');
  const [imgBroken, setImgBroken] = useState(false);

  useEffect(() => {
    const offShow = window.api.on('notification:show', (data: unknown) => {
      setItem(data as NotificationPayload);
      setImgBroken(false);
      setPhase('in');
    });
    const offHide = window.api.on('notification:hide', () => {
      setPhase('out');
    });
    return () => {
      offShow();
      offHide();
    };
  }, []);

  useEffect(() => {
    if (phase !== 'out') return;
    const t = window.setTimeout(() => {
      setPhase('hidden');
      setItem(null);
    }, 300);
    return () => window.clearTimeout(t);
  }, [phase]);

  if (!item || phase === 'hidden') return <></>;

  const rarity = getRarity(item.achievement.globalPercentage);
  const slideDir = (item.notificationPosition ?? 'bottom-right').includes('left') ? 'left' : 'right';
  const iconSrc = item.achievement.iconPath ?? item.achievement.iconUrl ?? '';
  const showImg = iconSrc.length > 0 && !imgBroken;
  const hasProgress = item.progressMax > 0;

  return (
    <div className={`notification-wrapper slide-${phase}-${slideDir}`}>
      <div className={`notification-card rarity-${rarity}`}>
        <div className="icon-slot">
          {showImg ? (
            <img src={iconSrc} width={52} height={52} alt="" onError={() => setImgBroken(true)} />
          ) : (
            <div className="icon-placeholder" />
          )}
        </div>
        <div className="content">
          <div className="header">{headerText(item.type, rarity)}</div>
          <div className="display-name">{item.achievement.displayName}</div>
          {item.achievement.description && (
            <div className="description">{item.achievement.description}</div>
          )}
          {item.achievement.gameName && (
            <div className="game-name">{item.achievement.gameName}</div>
          )}
          <div className="rarity-badge">{rarityLabel(rarity, item.achievement.globalPercentage)}</div>
          {hasProgress && (
            <div className="progress-row">
              <div className="progress-bar-track">
                <div
                  className="progress-bar-fill"
                  style={{ width: `${(item.progressCurrent / item.progressMax) * 100}%` } as CSSProperties}
                />
              </div>
              <div className="progress-text">{item.progressCurrent} / {item.progressMax}</div>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

createRoot(document.getElementById('root')!).render(<App />);
