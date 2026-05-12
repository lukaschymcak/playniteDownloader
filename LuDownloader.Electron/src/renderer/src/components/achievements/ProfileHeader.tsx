import { useState } from 'react';
import type { UserProfile } from '../../../../shared/types';
import type { PlayerStats } from '../../lib/achievementStats';
import { GradeIcon } from './GradeIcon';
import { Icon } from '../../icons';

interface ProfileHeaderProps {
  profile: UserProfile;
  stats: PlayerStats;
  onSave: (profile: UserProfile) => Promise<void>;
}

export function ProfileHeader({ profile, stats, onSave }: ProfileHeaderProps): JSX.Element {
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
      await onSave({
        ...profile,
        name: draftName.trim() || 'Player',
        avatarPath: draftAvatar.trim() || null
      });
      setEditing(false);
    } finally {
      setSaving(false);
    }
  }

  async function browsAvatar(): Promise<void> {
    const picked = await window.api.profile.pickAvatar();
    if (picked) setDraftAvatar(picked);
  }

  const initials = profile.name.charAt(0).toUpperCase();

  return (
    <div className="profile-header">
      <div className="profile-avatar">
        {profile.avatarPath ? (
          <img src={`file:///${profile.avatarPath.replace(/\\/g, '/')}`} alt="" />
        ) : (
          initials
        )}
      </div>

      {editing ? (
        <div className="profile-edit-form">
          <div className="profile-edit-row">
            <input
              type="text"
              value={draftName}
              onChange={(e) => setDraftName(e.target.value)}
              placeholder="Player name"
              maxLength={40}
              autoFocus
            />
          </div>
          <div className="profile-edit-row">
            <input
              type="text"
              value={draftAvatar}
              onChange={(e) => setDraftAvatar(e.target.value)}
              placeholder="Avatar image path"
              style={{ flex: 1 }}
            />
            <button className="btn sm" onClick={() => void browsAvatar()}>Browse</button>
          </div>
          <div className="profile-edit-actions">
            <button className="btn sm" onClick={() => setEditing(false)} disabled={saving}>Cancel</button>
            <button className="btn sm accent" onClick={() => void handleSave()} disabled={saving}>Save</button>
          </div>
        </div>
      ) : (
        <div className="profile-info">
          <div className="profile-name-row">
            <span className="profile-name">{profile.name}</span>
            <span className="profile-level">Lv. {stats.level}</span>
          </div>
          <div className="profile-trophy-row">
            <div className="profile-trophy-item">
              <GradeIcon grade="platinum" size={14} />
              <span>{stats.trophyCounts.platinum}</span>
            </div>
            <div className="profile-trophy-item">
              <GradeIcon grade="gold" size={14} />
              <span>{stats.trophyCounts.gold}</span>
            </div>
            <div className="profile-trophy-item">
              <GradeIcon grade="silver" size={14} />
              <span>{stats.trophyCounts.silver}</span>
            </div>
            <div className="profile-trophy-item">
              <GradeIcon grade="bronze" size={14} />
              <span>{stats.trophyCounts.bronze}</span>
            </div>
          </div>
        </div>
      )}

      {!editing && (
        <>
          <span className="profile-points">★ {stats.totalPoints.toLocaleString()} pts</span>
          <button className="profile-edit-btn" onClick={startEdit} title="Edit profile">
            <Icon name="edit" size={14} />
          </button>
        </>
      )}
    </div>
  );
}
