import { Icon } from '../../icons';

interface FilterBarProps {
  filter: string;
  onFilterChange: (v: string) => void;
  onRefresh: () => Promise<void>;
  loading: boolean;
}

export function FilterBar({ filter, onFilterChange, onRefresh, loading }: FilterBarProps): JSX.Element {
  return (
    <div className="ach-filter-bar">
      <Icon name="filter" size={14} style={{ color: 'var(--fg-3)', flexShrink: 0 }} />
      <input
        className="ach-filter-input"
        type="text"
        placeholder="Filter games..."
        value={filter}
        onChange={(e) => onFilterChange(e.target.value)}
      />
      <button
        className="btn sm"
        onClick={() => void onRefresh()}
        disabled={loading}
        title="Refresh"
      >
        <Icon name="refresh" size={14} />
      </button>
    </div>
  );
}
