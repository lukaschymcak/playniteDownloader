import type { TrophyGrade } from '../../lib/achievementStats';

interface GradeIconProps {
  grade: TrophyGrade | 'platinum';
  size?: number;
}

/** Platinum: trophy cup · Gold: 6-point star · Silver: shield · Bronze: diamond */
export function GradeIcon({ grade, size = 14 }: GradeIconProps): JSX.Element {
  const cls = `grade-icon-${grade}`;

  if (grade === 'platinum') {
    return (
      <svg width={size} height={size} viewBox="0 0 16 16" fill="currentColor" className={cls}>
        {/* cup bowl */}
        <path d="M3 1h10l-1.5 7C11 10.3 9.7 12 8 12S5 10.3 4.5 8L3 1z" />
        {/* left handle */}
        <path d="M3 3H1.5A1.5 1.5 0 0 0 0 4.5v.5A2.5 2.5 0 0 0 2.5 7.5H3" />
        {/* right handle */}
        <path d="M13 3h1.5A1.5 1.5 0 0 1 16 4.5v.5A2.5 2.5 0 0 1 13.5 7.5H13" />
        {/* stem */}
        <rect x="7" y="12" width="2" height="2" rx="0.3" />
        {/* base */}
        <rect x="4.5" y="14" width="7" height="1.5" rx="0.5" />
      </svg>
    );
  }

  if (grade === 'gold') {
    return (
      <svg width={size} height={size} viewBox="0 0 16 16" fill="currentColor" className={cls}>
        {/* 6-point star */}
        <path d="M8 1 L9.5 5.5 L14.5 5.5 L10.5 8.5 L12 13 L8 10.5 L4 13 L5.5 8.5 L1.5 5.5 L6.5 5.5 Z" />
      </svg>
    );
  }

  if (grade === 'silver') {
    return (
      <svg width={size} height={size} viewBox="0 0 16 16" fill="currentColor" className={cls}>
        {/* shield */}
        <path d="M8 1 L14.5 3.5 V8.5 C14.5 12 11.5 14.5 8 16 C4.5 14.5 1.5 12 1.5 8.5 V3.5 Z" />
      </svg>
    );
  }

  /* bronze — diamond */
  return (
    <svg width={size} height={size} viewBox="0 0 16 16" fill="currentColor" className={cls}>
      <path d="M8 1 L15 8 L8 15 L1 8 Z" />
    </svg>
  );
}
