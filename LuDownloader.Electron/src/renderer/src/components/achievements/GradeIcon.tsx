import type { TrophyGrade } from '../../lib/achievementStats';

interface GradeIconProps {
  grade: TrophyGrade | 'platinum';
  size?: number;
}

export function GradeIcon({ grade, size = 14 }: GradeIconProps): JSX.Element {
  return (
    <span style={{
      display: 'inline-flex',
      width: size,
      height: size,
      borderRadius: '50%',
      background: `var(--grade-${grade})`,
      boxShadow: `inset 0 0 0 2px color-mix(in oklab, var(--grade-${grade}) 60%, #000)`,
      flexShrink: 0,
    }} />
  );
}
