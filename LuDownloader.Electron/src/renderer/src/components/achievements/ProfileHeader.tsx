import { useState } from 'react';
import type { UserProfile } from '../../../../shared/types';
import type { PlayerStats } from '../../lib/achievementStats';
import { Icon } from '../../icons';

interface ProfileHeaderProps {
  profile: UserProfile;
  stats: PlayerStats;
  filter: string;
  onFilterChange: (v: string) => void;
  sort: string;
  onSortChange: (v: string) => void;
  onSave: (profile: UserProfile) => Promise<void>;
}

export function ProfileHeader({
  profile,
  stats,
  filter,
  onFilterChange,
  sort,
  onSortChange,
  onSave
}: ProfileHeaderProps): JSX.Element {
  const [editing, setEditing] = useState(false);
  const [draftName, setDraftName] = useState(profile.name);
  const [draftAvatar, setDraftAvatar] = useState(profile.avatarPath ?? '');
  const [saving, setSaving] = useState(false);

  function startEdit(): void {
    setDraftName(profile.name);
    setDraftAvatar(profile.avatarPath ?? '');
    setEditing(true);
  }

  async function handleSave(): Promise<void> {
    setSaving(true);
    try {
      await onSave({ ...profile, name: draftName.trim() || 'Player', avatarPath: draftAvatar.trim() || null });
      setEditing(false);
    } finally {
      setSaving(false);
    }
  }

  async function browseAvatar(): Promise<void> {
    const picked = await window.api.profile.pickAvatar();
    if (picked) setDraftAvatar(picked);
  }

  const avatarSrc = profile.avatarPath
    ? `file:///${profile.avatarPath.replace(/\\/g, '/')}`
    : null;

  return (
    <header className="v3-head">
      <div className="v3-id">
        <div className="v3-avatar">
          {avatarSrc ? (
            <img src={avatarSrc} alt="" />
          ) : (
            <svg viewBox="0 0 40 40" width="40" height="40" aria-hidden="true">
              <defs>
                <linearGradient id="v3av" x1="0" y1="0" x2="1" y2="1">
                  <stop offset="0%" stopColor="oklch(0.42 0.05 240)" />
                  <stop offset="100%" stopColor="oklch(0.24 0.04 240)" />
                </linearGradient>
              </defs>
              <rect width="40" height="40" rx="10" fill="url(#v3av)" />
              <path
                d="M20 12a5 5 0 0 1 5 5v1a5 5 0 0 1-10 0v-1a5 5 0 0 1 5-5zM10 33c1.5-5 5.5-7.5 10-7.5s8.5 2.5 10 7.5"
                fill="none"
                stroke="oklch(0.78 0.12 195 / 0.9)"
                strokeWidth="1.6"
                strokeLinecap="round"
                strokeLinejoin="round"
              />
            </svg>
          )}
          <span className="v3-avatar-dot" />
        </div>

        {editing ? (
          <div className="v3-edit-form">
            <div className="v3-edit-row">
              <input
                type="text"
                value={draftName}
                onChange={(e) => setDraftName(e.target.value)}
                placeholder="Player name"
                maxLength={40}
                autoFocus
              />
            </div>
            <div className="v3-edit-row">
              <input
                type="text"
                value={draftAvatar}
                onChange={(e) => setDraftAvatar(e.target.value)}
                placeholder="Avatar path"
                style={{ flex: 1 }}
              />
              <button className="btn sm" onClick={() => void browseAvatar()}>Browse</button>
            </div>
            <div className="v3-edit-actions">
              <button className="btn sm" onClick={() => setEditing(false)} disabled={saving}>Cancel</button>
              <button className="btn sm accent" onClick={() => void handleSave()} disabled={saving}>Save</button>
            </div>
          </div>
        ) : (
          <div>
            <div className="v3-eye">Profile</div>
            <h1 className="v3-title">
              {profile.name}
              <span className="v3-lvl">Lv {stats.level}</span>
              <button className="v3-edit-btn" onClick={startEdit} title="Edit profile">
                <Icon name="edit" size={12} />
              </button>
            </h1>
          </div>
        )}
      </div>

      <div className="v3-actions">
        <div className="v3-search-wrap">
          <Icon name="search" size={13} />
          <input
            className="v3-search-input"
            placeholder="Search games…"
            value={filter}
            onChange={(e) => onFilterChange(e.target.value)}
          />
        </div>
        <select
          className="v3-sort-select"
          value={sort}
          onChange={(e) => onSortChange(e.target.value)}
        >
          <option value="completion">Completion</option>
          <option value="recent">Recent</option>
          <option value="name">Name</option>
        </select>
      </div>
    </header>
  );
}
