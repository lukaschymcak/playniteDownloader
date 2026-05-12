import { useEffect, useRef, useState } from 'react';
import { SourceId, type AppSettings } from '../../../shared/types';
import { Icon } from '../icons';

const SOURCE_LABELS: Record<string, string> = {
  Goldberg: 'Goldberg Emulator', GSE: 'GoldbergSteamEmu', Empress: 'Empress',
  Codex: 'CODEX', Rune: 'Rune', OnlineFix: 'OnlineFix.me', SmartSteamEmu: 'SmartSteamEmu',
  Skidrow: 'SKIDROW', Darksiders: 'Darksiders', Ali213: 'Ali213', Hoodlum: 'HOODLUM',
  CreamApi: 'CreamAPI', GreenLuma: 'GreenLuma', Reloaded: 'RELOADED'
};

const empty: AppSettings = {
  apiKey: '', downloadPath: '', maxDownloads: 20, steamUsername: '', steamWebApiKey: '',
  igdbClientId: '', igdbClientSecret: '', goldbergFilesPath: '', goldbergAccountName: '', goldbergSteamId: '',
  cloudServerUrl: '', cloudApiKey: '',
  achievementEnabledSources: Object.values(SourceId).filter((s) => s !== SourceId.None),
  achievementSourceRoots: {},
  hoodlumSavePath: '',
  achievementUserGameLibraryRoots: [],
  achievementScanOfficialSteamLibraries: true,
  achievementFirstRunDismissed: false,
  notificationEnabled: true,
  notificationPosition: 'bottom-right',
  notificationDurationSeconds: 5
};

export function SettingsModal({ onClose, embedded = false }: { onClose: () => void; embedded?: boolean }): JSX.Element {
  const [settings, setSettings] = useState<AppSettings>(empty);
  const [error, setError] = useState('');
  const [saving, setSaving] = useState(false);
  const [goldbergEnabled, setGoldbergEnabled] = useState(true);
  const [maxSpeed, setMaxSpeed] = useState('Unlimited');

  useEffect(() => { void window.api.settings.load().then(setSettings); }, []);

  const set = (key: keyof AppSettings, value: string | number): void => setSettings((state) => ({ ...state, [key]: value }));

  const save = async (): Promise<void> => {
    setSaving(true);
    setError('');
    try {
      await window.api.settings.save(settings);
      onClose();
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    } finally {
      setSaving(false);
    }
  };

  const body = (
    <div className={embedded ? 'settings-page settings-screen' : 'modal settings-screen'}>
      <div className="settings-shell">
        <div className="settings-main-card">
          <SettingsCard title="General">
            <FolderField label="Download Path" value={settings.downloadPath} onChange={(value) => set('downloadPath', value)} />

            <div className="settings-two-col compact">
              <SelectField
                label="Max Connections"
                value={String(settings.maxDownloads)}
                onChange={(value) => set('maxDownloads', Number(value) || 20)}
                options={['1', '2', '4', '8', '16', '20', '32', '64']}
              />
              <SelectField
                label="Max Download Speed"
                value={maxSpeed}
                onChange={setMaxSpeed}
                options={['Unlimited', '50 MB/s', '25 MB/s', '10 MB/s']}
              />
            </div>

            <div className="settings-divider" />

            <TextField label="Steam Username" value={settings.steamUsername} onChange={(value) => set('steamUsername', value)} />
            <div className="settings-help">Used for Add to Steam functionality.</div>

            <div className="settings-divider" />

            <label className="settings-check">
              <input type="checkbox" checked={goldbergEnabled} onChange={(event) => setGoldbergEnabled(event.target.checked)} />
              <span>Enable Goldberg Emulator</span>
            </label>
            <FolderField label="Emulator Path" value={settings.goldbergFilesPath} onChange={(value) => set('goldbergFilesPath', value)} />
          </SettingsCard>
        </div>

        <div className="settings-side-stack">
          <SettingsCard title="API">
            <SecretField label="Steam Web API Key" value={settings.steamWebApiKey} onChange={(value) => set('steamWebApiKey', value)} />
            <div className="settings-help">Get your API key from <span>https://steamcommunity.com/dev/apikey</span></div>
          </SettingsCard>

          <SettingsCard title="IGDB (Optional)">
            <SecretField label="Client ID" value={settings.igdbClientId} onChange={(value) => set('igdbClientId', value)} />
            <SecretField label="Client Secret" value={settings.igdbClientSecret} onChange={(value) => set('igdbClientSecret', value)} />
            <div className="settings-help">Get your credentials from <span>https://api.igdb.com</span></div>
          </SettingsCard>

          <SettingsCard title="Cloud Sync (Optional)">
            <TextField label="Server URL" value={settings.cloudServerUrl} onChange={(value) => set('cloudServerUrl', value)} />
            <div className="settings-help">Railway app URL, e.g. https://your-app.railway.app</div>
            <SecretField label="API Key" value={settings.cloudApiKey} onChange={(value) => set('cloudApiKey', value)} />
            <div className="settings-help">Shared secret set as API_KEY env var on Railway.</div>
          </SettingsCard>
        </div>
      </div>

      <AchievementsCard settings={settings} setSettings={setSettings} />

      {error && <div className="settings-error">{error}</div>}

      <div className="settings-footer">
        <button className="btn ghost" disabled={saving} onClick={onClose}>{embedded ? 'Cancel' : 'Cancel'}</button>
        <button className="btn primary" disabled={saving} onClick={() => void save()}>{saving ? 'Saving...' : 'Save'}</button>
      </div>
    </div>
  );

  if (embedded) return body;
  return <div className="modal-backdrop">{body}</div>;
}

function SettingsCard({ title, children }: { title: string; children: React.ReactNode }): JSX.Element {
  return (
    <section className="settings-card">
      <h2>{title}</h2>
      {children}
    </section>
  );
}

function TextField({ label, value, onChange }: { label: string; value: string; onChange: (value: string) => void }): JSX.Element {
  return (
    <label className="settings-field">
      <span>{label}</span>
      <input value={value} onChange={(event) => onChange(event.target.value)} />
    </label>
  );
}

function SelectField({ label, value, options, onChange }: { label: string; value: string; options: string[]; onChange: (value: string) => void }): JSX.Element {
  return (
    <label className="settings-field">
      <span>{label}</span>
      <select value={value} onChange={(event) => onChange(event.target.value)}>
        {options.map((option) => <option key={option} value={option}>{option}</option>)}
      </select>
    </label>
  );
}

function FolderField({ label, value, onChange }: { label: string; value: string; onChange: (value: string) => void }): JSX.Element {
  return (
    <label className="settings-field">
      <span>{label}</span>
      <div className="settings-field-row">
        <input value={value} onChange={(event) => onChange(event.target.value)} />
        <button type="button" className="btn icon" onClick={async () => {
          const folder = await window.api.system.pickFolder();
          if (folder) onChange(folder);
        }}><Icon name="folder" /></button>
      </div>
    </label>
  );
}

function SecretField({ label, value, onChange }: { label: string; value: string; onChange: (value: string) => void }): JSX.Element {
  const [visible, setVisible] = useState(false);
  return (
    <label className="settings-field">
      <span>{label}</span>
      <div className="settings-field-row">
        <input type={visible ? 'text' : 'password'} value={value} onChange={(event) => onChange(event.target.value)} />
        <button type="button" className="btn icon" title={visible ? 'Hide value' : 'Show value'} onClick={() => setVisible((current) => !current)}>
          <Icon name="search" />
        </button>
      </div>
    </label>
  );
}

function ConfirmButton({ label, onConfirm }: { label: string; onConfirm: () => Promise<void> }): JSX.Element {
  const [phase, setPhase] = useState<'idle' | 'confirm' | 'busy'>('idle');
  async function run(): Promise<void> {
    if (phase === 'idle') { setPhase('confirm'); return; }
    setPhase('busy');
    try { await onConfirm(); } finally { setPhase('idle'); }
  }
  return (
    <button className="btn sm ghost" disabled={phase === 'busy'} onClick={() => void run()}>
      {phase === 'confirm' ? 'Confirm?' : phase === 'busy' ? 'Working...' : label}
    </button>
  );
}

function AchievementsCard({
  settings,
  setSettings
}: {
  settings: AppSettings;
  setSettings: React.Dispatch<React.SetStateAction<AppSettings>>;
}): JSX.Element {
  const allSources = Object.values(SourceId).filter((s) => s !== SourceId.None);
  const newRootRef = useRef<HTMLInputElement>(null);
  const [newRoot, setNewRoot] = useState('');

  function toggleSource(source: SourceId): void {
    setSettings((prev) => {
      const enabled = prev.achievementEnabledSources.includes(source)
        ? prev.achievementEnabledSources.filter((s) => s !== source)
        : [...prev.achievementEnabledSources, source];
      return { ...prev, achievementEnabledSources: enabled };
    });
  }

  function addRoot(): void {
    const val = newRoot.trim();
    if (!val) return;
    setSettings((prev) => ({ ...prev, achievementUserGameLibraryRoots: [...(prev.achievementUserGameLibraryRoots ?? []), val] }));
    setNewRoot('');
    newRootRef.current?.focus();
  }

  function removeRoot(idx: number): void {
    setSettings((prev) => ({
      ...prev,
      achievementUserGameLibraryRoots: (prev.achievementUserGameLibraryRoots ?? []).filter((_, i) => i !== idx)
    }));
  }

  async function browseRoot(): Promise<void> {
    const folder = await window.api.system.pickFolder();
    if (folder) setNewRoot(folder);
  }

  return (
    <SettingsCard title="Achievements">
      <div className="settings-help">Sources to scan for achievement files.</div>
      <div className="ach-settings-sources">
        {allSources.map((src) => (
          <label key={src} className="settings-check">
            <input
              type="checkbox"
              checked={settings.achievementEnabledSources.includes(src)}
              onChange={() => toggleSource(src)}
            />
            <span>{SOURCE_LABELS[src] ?? src}</span>
          </label>
        ))}
      </div>

      <div className="settings-divider" />

      <label className="settings-check">
        <input
          type="checkbox"
          checked={settings.achievementScanOfficialSteamLibraries}
          onChange={(e) => setSettings((prev) => ({ ...prev, achievementScanOfficialSteamLibraries: e.target.checked }))}
        />
        <span>Scan official Steam libraries</span>
      </label>

      <div className="settings-divider" />

      <FolderField
        label="Hoodlum Save Path"
        value={settings.hoodlumSavePath ?? ''}
        onChange={(v) => setSettings((prev) => ({ ...prev, hoodlumSavePath: v }))}
      />

      <div className="settings-divider" />

      <div className="settings-field">
        <span>Custom Library Roots</span>
        {(settings.achievementUserGameLibraryRoots ?? []).map((root, i) => (
          <div key={i} className="settings-field-row" style={{ marginTop: 4 }}>
            <input value={root} readOnly />
            <button type="button" className="btn icon" onClick={() => removeRoot(i)}>
              <Icon name="close" size={12} />
            </button>
          </div>
        ))}
        <div className="settings-field-row" style={{ marginTop: 4 }}>
          <input
            ref={newRootRef}
            value={newRoot}
            onChange={(e) => setNewRoot(e.target.value)}
            onKeyDown={(e) => { if (e.key === 'Enter') addRoot(); }}
            placeholder="Add folder path..."
          />
          <button type="button" className="btn icon" onClick={() => void browseRoot()}>
            <Icon name="folder" />
          </button>
          <button type="button" className="btn sm" onClick={addRoot}>Add</button>
        </div>
      </div>

      <div className="settings-divider" />

      <div className="settings-field-row" style={{ gap: 8 }}>
        <ConfirmButton label="Clear Cache" onConfirm={() => window.api.achievements.clearCache()} />
        <ConfirmButton label="Clear Snapshots" onConfirm={() => window.api.achievements.clearSnapshots()} />
      </div>
      <div className="settings-help">Cache = enrichment data. Snapshots = diff baselines. Both are rebuilt automatically.</div>
    </SettingsCard>
  );
}
