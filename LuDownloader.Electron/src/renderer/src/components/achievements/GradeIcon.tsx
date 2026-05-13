import type { TrophyGrade } from '../../lib/achievementStats';
import { Icon } from '../../icons';

interface GradeIconProps {
  grade: TrophyGrade | 'platinum';
  size?: number;
}

export function GradeIcon({ grade, size = 14 }: GradeIconProps): JSX.Element {
  return (
    <Icon name={grade as any} size={size} />
  );
}
