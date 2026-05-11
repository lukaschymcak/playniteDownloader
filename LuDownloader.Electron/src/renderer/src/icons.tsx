import type { SVGProps } from 'react';

type IconName =
  | 'library' | 'search' | 'layers' | 'settings' | 'download' | 'play' | 'folder'
  | 'refresh' | 'plus' | 'more' | 'trash' | 'close' | 'minus' | 'square'
  | 'zip' | 'upload' | 'box' | 'check' | 'arrow' | 'steam' | 'warn';

interface IconProps extends SVGProps<SVGSVGElement> {
  name: IconName;
  size?: number;
  strokeW?: number;
}

export function Icon({ name, size = 16, strokeW = 1.7, ...rest }: IconProps): JSX.Element | null {
  const paths: Record<IconName, JSX.Element> = {
    library: <><rect x="3" y="3" width="7" height="18" rx="1.5"/><rect x="12" y="3" width="3.5" height="18" rx="1"/><path d="M17.5 5.2l3 .8-3 14.4-3-.8z"/></>,
    search: <><circle cx="11" cy="11" r="7"/><path d="M20 20l-3.5-3.5"/></>,
    layers: <><path d="M12 3l9 5-9 5-9-5 9-5z"/><path d="M3 13l9 5 9-5"/><path d="M3 18l9 5 9-5"/></>,
    settings: <><circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 0 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-4 0v-.09a1.65 1.65 0 0 0-1-1.51 1.65 1.65 0 0 0-1.82.33l-.06.06A2 2 0 1 1 4.27 16.96l.06-.06a1.65 1.65 0 0 0 .33-1.82 1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1 0-4h.09a1.65 1.65 0 0 0 1.51-1 1.65 1.65 0 0 0-.33-1.82l-.06-.06A2 2 0 1 1 7.04 4.27l.06.06a1.65 1.65 0 0 0 1.82.33h0a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 4 0v.09a1.65 1.65 0 0 0 1 1.51h0a1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 0 1 2.83 2.83l-.06.06a1.65 1.65 0 0 0-.33 1.82v0a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1z"/></>,
    download: <><path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/><polyline points="7 10 12 15 17 10"/><line x1="12" y1="15" x2="12" y2="3"/></>,
    play: <><polygon points="6 4 20 12 6 20 6 4"/></>,
    folder: <><path d="M22 19a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5l2 3h9a2 2 0 0 1 2 2z"/></>,
    refresh: <><polyline points="23 4 23 10 17 10"/><polyline points="1 20 1 14 7 14"/><path d="M3.5 9a9 9 0 0 1 14.85-3.36L23 10M1 14l4.65 4.36A9 9 0 0 0 20.5 15"/></>,
    plus: <><path d="M12 5v14M5 12h14"/></>,
    more: <><circle cx="12" cy="6" r="1.4"/><circle cx="12" cy="12" r="1.4"/><circle cx="12" cy="18" r="1.4"/></>,
    trash: <><polyline points="3 6 5 6 21 6"/><path d="M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6"/><path d="M10 11v6M14 11v6"/><path d="M9 6V4a1 1 0 0 1 1-1h4a1 1 0 0 1 1 1v2"/></>,
    close: <><path d="M18 6L6 18M6 6l12 12"/></>,
    minus: <><path d="M5 12h14"/></>,
    square: <><rect x="4" y="4" width="16" height="16" rx="1"/></>,
    zip: <><path d="M21 8v13a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V3a1 1 0 0 1 1-1h11l6 6z"/><path d="M14 2v6h6"/><path d="M10 12h2v2h-2zM10 16h2v2h-2z"/></>,
    upload: <><path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/><polyline points="17 8 12 3 7 8"/><line x1="12" y1="3" x2="12" y2="15"/></>,
    box: <><path d="M21 16V8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16z"/><polyline points="3.27 6.96 12 12.01 20.73 6.96"/><line x1="12" y1="22.08" x2="12" y2="12"/></>,
    check: <><polyline points="20 6 9 17 4 12"/></>,
    arrow: <><line x1="5" y1="12" x2="19" y2="12"/><polyline points="12 5 19 12 12 19"/></>,
    steam: <><circle cx="12" cy="12" r="9"/><path d="M3.5 14l5 2.2c.5-.7 1.4-1.2 2.4-1.2 1.6 0 3 1.3 3 3 0 .1 0 .3-.1.4l3.6-2.6c2-1.5 1.7-4.6-.6-5.6-1.7-.7-3.6 0-4.5 1.5l-2.7-.4c-1 .9-2.4 1.2-3.6.7L3.5 14z"/></>,
    warn: <><path d="M12 3l10 18H2L12 3z"/><line x1="12" y1="9" x2="12" y2="14"/><circle cx="12" cy="18" r="1"/></>
  };

  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor"
      strokeWidth={strokeW} strokeLinecap="round" strokeLinejoin="round" {...rest}>
      {paths[name]}
    </svg>
  );
}
