import { useEffect, useState } from 'react';
import { SourceId, type AppSettings } from '../../../shared/types';
import { Icon } from '../icons';

const empty: AppSettings = {
  apiKey: '', downloadPath: '', maxDownloads: 20, steamUsername: '', steamWebApiKey: '',
  igdbClientId: '', igdbClientSecret: '', goldbergFilesPath: '', goldbergAccountName: '', goldbergSteamId: '',
  cloudServerUrl: '', cloudApiKey: '',
  achievementEnabledSources: Object.values(SourceId).filter((s) => s !== SourceId.None),
  achievementSourceRoots: {},
  hoodlumSavePath: '',
  achievementUserGameLibraryRoots: [],
  achievementScanOfficialSteamLibraries: true,
  achievementFirstRunDismissed: false
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
