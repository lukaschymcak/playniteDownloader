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

function getRarity(pct: number): 'very-rare' | 'rare' | 'normal' {
  if (pct > 0 && pct < 15) return 'very-rare';
  if (pct >= 15 && pct < 30) return 'rare';
  return 'normal';
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
  const headerText =
    item.type === 'progress'
      ? 'Achievement Progress'
      : rarity === 'very-rare'
        ? 'Very Rare Achievement Unlocked!'
        : rarity === 'rare'
          ? 'Rare Achievement Unlocked!'
          : 'Achievement Unlocked!';

  const iconSrc = item.achievement.iconPath ?? item.achievement.iconUrl ?? '';
  const showImg = iconSrc.length > 0 && !imgBroken;

  return (
    <div
      className={`notification-card slide-${phase}-${slideDir}`}
      style={{ '--border-color': `var(--rarity-${rarity})` } as CSSProperties}
    >
      <div className="icon-slot">
        {showImg ? (
          <img src={iconSrc} width={64} height={64} alt="" onError={() => setImgBroken(true)} />
        ) : (
          <div className="icon-placeholder" />
        )}
      </div>
      <div>
        <div className="header">{headerText}</div>
        <div className="display-name">{item.achievement.displayName}</div>
        <div className="description">{item.achievement.description}</div>
        <div className="game-name">{item.achievement.gameName}</div>
        {item.achievement.globalPercentage > 0 && (
          <div className="rarity-badge">{item.achievement.globalPercentage.toFixed(1)}% unlocked</div>
        )}
        {item.progressMax > 0 && (
          <div className="progress-bar-track">
            <div
              className="progress-bar-fill"
              style={{ width: `${(item.progressCurrent / item.progressMax) * 100}%` }}
            />
          </div>
        )}
      </div>
    </div>
  );
}

createRoot(document.getElementById('root')!).render(<App />);
