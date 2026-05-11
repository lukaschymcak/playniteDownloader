import type { AppSettings } from '../../shared/types';

let cachedToken = '';
let tokenExpiresAt = 0;

export async function igdbSearch(query: string, settings: AppSettings): Promise<unknown[]> {
  const token = await getToken(settings);
  const response = await fetch('https://api.igdb.com/v4/games', {
    method: 'POST',
    headers: {
      'Client-ID': settings.igdbClientId,
      Authorization: `Bearer ${token}`,
      Accept: 'application/json'
    },
    body: `search "${query.replace(/"/g, '\\"')}"; fields name,summary,cover.image_id,first_release_date; limit 10;`
  });
  if (!response.ok) throw new Error(`IGDB search failed (${response.status})`);
  return await response.json() as unknown[];
}

export async function getToken(settings: AppSettings): Promise<string> {
  if (cachedToken && Date.now() < tokenExpiresAt - 60000) return cachedToken;
  if (!settings.igdbClientId || !settings.igdbClientSecret) throw new Error('IGDB credentials are not configured.');
  const url = new URL('https://id.twitch.tv/oauth2/token');
  url.searchParams.set('client_id', settings.igdbClientId);
  url.searchParams.set('client_secret', settings.igdbClientSecret);
  url.searchParams.set('grant_type', 'client_credentials');
  const response = await fetch(url, { method: 'POST' });
  if (!response.ok) throw new Error(`IGDB auth failed (${response.status})`);
  const json = await response.json() as { access_token: string; expires_in: number };
  cachedToken = json.access_token;
  tokenExpiresAt = Date.now() + json.expires_in * 1000;
  return cachedToken;
}
