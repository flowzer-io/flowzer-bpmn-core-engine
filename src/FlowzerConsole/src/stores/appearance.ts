import { useEffect } from 'react';
import { create } from 'zustand';
import { persist } from 'zustand/middleware';

export const ACCENTS = ['iris', 'teal', 'emerald', 'amber', 'rose'] as const;
export type Accent = (typeof ACCENTS)[number];

export type Theme = 'light' | 'dark' | 'system';
export type Density = 'comfortable' | 'compact';
export type SidebarMode = 'full' | 'rail';

interface AppearanceState {
  theme: Theme;
  accent: Accent;
  density: Density;
  sidebar: SidebarMode;
  setTheme: (theme: Theme) => void;
  toggleTheme: () => void;
  setAccent: (accent: Accent) => void;
  setDensity: (density: Density) => void;
  toggleSidebar: () => void;
}

/**
 * Darstellungseinstellungen. Sie werden lokal gespeichert und als data-Attribute
 * an <html> geschrieben — die Tokens in `tokens.css` reagieren darauf ohne Re-Render.
 */
export const useAppearance = create<AppearanceState>()(
  persist(
    (set) => ({
      theme: 'system',
      accent: 'iris',
      density: 'comfortable',
      sidebar: 'full',
      setTheme: (theme) => set({ theme }),
      toggleTheme: () =>
        set((state) => ({ theme: resolveTheme(state.theme) === 'dark' ? 'light' : 'dark' })),
      setAccent: (accent) => set({ accent }),
      setDensity: (density) => set({ density }),
      toggleSidebar: () => set((state) => ({ sidebar: state.sidebar === 'full' ? 'rail' : 'full' })),
    }),
    { name: 'flowzer-console-appearance' },
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
  const { theme, accent, density, sidebar } = useAppearance();

  useEffect(() => {
    const root = document.documentElement;

    const apply = () => {
      root.dataset.theme = resolveTheme(theme);
      root.dataset.accent = accent;
      root.dataset.density = density;
      root.dataset.sidebar = sidebar;
    };

    apply();

    if (theme !== 'system') return;

    // Bei "system" muss die Oberfläche auf Wechsel im Betriebssystem reagieren.
    const media = window.matchMedia('(prefers-color-scheme: dark)');
    media.addEventListener('change', apply);
    return () => media.removeEventListener('change', apply);
  }, [theme, accent, density, sidebar]);
}
