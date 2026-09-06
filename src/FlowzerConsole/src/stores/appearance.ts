import { useEffect } from 'react';
import { create } from 'zustand';
import { persist } from 'zustand/middleware';

import { getRuntimeConfig } from '@/lib/config/runtime';

const THEMES = ['light', 'dark', 'system'] as const;
const SIDEBAR_MODES = ['full', 'rail'] as const;

export type Theme = (typeof THEMES)[number];
export type SidebarMode = (typeof SIDEBAR_MODES)[number];

function isTheme(value: unknown): value is Theme {
  return typeof value === 'string' && (THEMES as readonly string[]).includes(value);
}

function isSidebarMode(value: unknown): value is SidebarMode {
  return typeof value === 'string' && (SIDEBAR_MODES as readonly string[]).includes(value);
}

interface AppearanceState {
  theme: Theme;
  sidebar: SidebarMode;
  setTheme: (theme: Theme) => void;
  toggleTheme: () => void;
  toggleSidebar: () => void;
}

/**
 * Persönliche Darstellungseinstellungen.
 *
 * Bewusst nur zwei: Hell oder dunkel, und ob die Seitenleiste ausgeklappt ist. Beides
 * ist eine Frage des Arbeitsplatzes und der Tageszeit. Die Akzentfarbe gehört nicht
 * dazu — sie ist Teil des Erscheinungsbilds des Unternehmens und kommt deshalb aus der
 * Bereitstellung (`config.json`), nicht aus einem Menü im Browser.
 */
export const useAppearance = create<AppearanceState>()(
  persist(
    (set) => ({
      theme: 'system',
      sidebar: 'full',
      setTheme: (theme) => set({ theme }),
      toggleTheme: () =>
        set((state) => ({ theme: resolveTheme(state.theme) === 'dark' ? 'light' : 'dark' })),
      toggleSidebar: () => set((state) => ({ sidebar: state.sidebar === 'full' ? 'rail' : 'full' })),
    }),
    {
      name: 'flowzer-console-appearance',
      /*
       * Version 1 raeumt auf: Bis dahin lagen Akzentfarbe, Dichte und Umfang der Ansicht
       * mit im gespeicherten Zustand. Ohne diesen Schritt behielten alle, die die Konsole
       * schon benutzt haben, die alten Felder als toten Ballast im Browser — und die
       * Aussage "nur zwei Einstellungen" waere fuer sie schlicht falsch.
       */
      version: 1,
      migrate: (persisted) => {
        // Geprueft statt blind uebernommen: Was im Browser liegt, kann alt, von Hand
        // veraendert oder beschaedigt sein. Ein unbekannter Wert wuerde als data-Attribut
        // landen, zu dem es keine Tokens gibt — die Oberflaeche stuende ohne Farben da.
        const { theme, sidebar } = (persisted ?? {}) as Record<string, unknown>;
        return {
          theme: isTheme(theme) ? theme : 'system',
          sidebar: isSidebarMode(sidebar) ? sidebar : 'full',
        } as AppearanceState;
      },
    },
  ),
);

function prefersDark(): boolean {
  return typeof window !== 'undefined' && window.matchMedia('(prefers-color-scheme: dark)').matches;
}

/** Löst `system` gegen die Betriebssystem-Einstellung auf. */
export function resolveTheme(theme: Theme): 'light' | 'dark' {
  return theme === 'system' ? (prefersDark() ? 'dark' : 'light') : theme;
}

export function useResolvedTheme(): 'light' | 'dark' {
  const theme = useAppearance((state) => state.theme);
  return resolveTheme(theme);
}

/** Schreibt die Einstellungen als data-Attribute an das <html>-Element. */
export function useApplyAppearance(): void {
  const { theme, sidebar } = useAppearance();
  // Aus der Bereitstellung, nicht aus dem Zustand: Die Akzentfarbe ist für alle gleich.
  const accent = getRuntimeConfig().accent;

  useEffect(() => {
    const root = document.documentElement;

    const apply = () => {
      root.dataset.theme = resolveTheme(theme);
      root.dataset.accent = accent;
      root.dataset.sidebar = sidebar;
    };

    apply();

    if (theme !== 'system') return;

    // Bei "system" muss die Oberfläche auf Wechsel im Betriebssystem reagieren.
    const media = window.matchMedia('(prefers-color-scheme: dark)');
    media.addEventListener('change', apply);
    return () => media.removeEventListener('change', apply);
  }, [theme, accent, sidebar]);
}
