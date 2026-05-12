import type { MonthBucket } from '../../lib/achievementStats';

interface MonthlyChartProps {
  data: MonthBucket[];
}

export function MonthlyChart({ data }: MonthlyChartProps): JSX.Element {
  const W = 520;
  const H = 130;
  const PAD_B = 20;
  const chartH = H - PAD_B;
  const maxCount = Math.max(...data.map((b) => b.count), 1);
  const n = data.length;

  const x = (i: number): number => (i / Math.max(n - 1, 1)) * W;
  const y = (count: number): number => chartH - (count / maxCount) * (chartH - 4);

  const points = data.map((b, i) => `${x(i)},${y(b.count)}`).join(' ');
  const areaPoints = [
    `0,${chartH}`,
    ...data.map((b, i) => `${x(i)},${y(b.count)}`),
    `${W},${chartH}`
  ].join(' ');

  return (
    <svg viewBox={`0 0 ${W} ${H}`} preserveAspectRatio="none" className="monthly-chart-svg">
      <polygon points={areaPoints} fill="var(--accent-soft)" stroke="none" />
      <polyline points={points} fill="none" stroke="var(--accent)" strokeWidth="2" strokeLinejoin="round" />
      {data.map((b, i) => (
        <circle key={i} cx={x(i)} cy={y(b.count)} r="3" fill="var(--accent)" />
      ))}
      {data.map((b, i) => (
        <text
          key={`l${i}`}
          x={x(i)}
          y={H - 2}
          textAnchor="middle"
          fontSize="9"
          fill="var(--fg-3)"
        >
          {b.label}
        </text>
      ))}
    </svg>
  );
}
