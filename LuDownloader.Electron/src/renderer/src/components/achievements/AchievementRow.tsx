import type { Achievement } from '../../../../shared/types';
import { gradeAchievement } from '../../lib/achievementStats';
import { GradeIcon } from './GradeIcon';
import { Icon } from '../../icons';

interface AchievementRowProps {
  achievement: Achievement;
  pinned?: boolean;
  onPin?: () => void;
  onUnpin?: () => void;
  showRarity?: boolean;
}

export function AchievementRow({ achievement: ach, pinned, onPin, onUnpin, showRarity = true }: AchievementRowProps): JSX.Element {
  const grade = gradeAchievement(ach.globalPercentage);
  const iconSrc = ach.achieved ? (ach.iconUrl ?? ach.iconPath) : (ach.iconGrayUrl ?? ach.iconGrayPath ?? ach.iconUrl ?? ach.iconPath);

  const unlockDate = ach.achieved && ach.unlockTime
    ? new Date(ach.unlockTime * 1000).toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' })
    : null;

  const gradeClass = ach.achieved ? `grade-${grade}` : 'locked';

  return (
    <div className={`ach-row ${gradeClass}`}>
      <div className="ach-row-icon">
        {iconSrc ? (
          <img src={iconSrc.startsWith('http') ? iconSrc : `file:///${iconSrc.replace(/\\/g, '/')}`} alt="" onError={(e) => { (e.currentTarget as HTMLImageElement).style.opacity = '0'; }} />
        ) : (
          <span className="ach-row-icon-placeholder">?</span>
        )}
      </div>

      <div className="ach-row-body">
        <div className="ach-row-name">{ach.hidden && !ach.achieved ? 'Hidden Achievement' : ach.displayName}</div>
        <div className="ach-row-desc">
          {ach.hidden && !ach.achieved ? 'Continue playing to unlock.' : ach.description}
        </div>
        {ach.maxProgress > 0 && (
          <div className="ach-row-progress-track">
            <div className="ach-row-progress-fill" style={{ width: `${Math.min((ach.curProgress / ach.maxProgress) * 100, 100)}%` }} />
            <span className="ach-row-progress-label">{ach.curProgress} / {ach.maxProgress}</span>
          </div>
        )}
      </div>

      <div className="ach-row-meta">
        {unlockDate && <span className="ach-row-date">{unlockDate}</span>}
        {showRarity && ach.globalPercentage > 0 && (
          <span className="ach-row-rarity">
            <GradeIcon grade={grade} size={12} />
            {ach.globalPercentage.toFixed(1)}%
          </span>
        )}
      </div>

      {(onPin || onUnpin) && (
        <button
          className="ach-row-pin-btn"
          title={pinned ? 'Unpin' : 'Pin to featured'}
          onClick={pinned ? onUnpin : onPin}
        >
          <Icon name="pin" size={12} />
        </button>
      )}
    </div>
  );
}
