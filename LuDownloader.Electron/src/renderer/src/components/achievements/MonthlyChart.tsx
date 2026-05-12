import type { MonthBarBucket } from '../../lib/achievementStats';

interface MonthlyBarsProps {
  data: MonthBarBucket[];
}

export function MonthlyBars({ data }: MonthlyBarsProps): JSX.Element {
  const max = Math.max(...data.map((b) => b.count), 1);

  return (
    <div className="mbars-wrap">
      <div className="mbars">
        {data.map((b, i) => (
          <div key={i} className={`mbar${b.highlight ? ' on' : ''}`}>
            <div className="mbar-v">{b.count > 0 ? b.count : ''}</div>
            <div className="mbar-track">
              <div className="mbar-fill" style={{ height: `${(b.count / max) * 100}%` }} />
            </div>
            <div className="mbar-l">{b.label}</div>
          </div>
        ))}
      </div>
    </div>
  );
}
