import { useEffect } from 'react';
import { create } from 'zustand';

export interface Crumb {
  label: string;
  /** Wenn gesetzt, wird der Eintrag als Link gerendert. */
  to?: string;
}

interface BreadcrumbState {
  trail: Crumb[] | null;
  setTrail: (trail: Crumb[] | null) => void;
}

/**
 * Detailseiten kennen ihren Titel erst nach dem Laden (z. B. den Workflow-Namen
 * einer Instanz). Sie melden ihren Pfad deshalb hier an, statt ihn aus der URL
 * zu erraten.
 */
export const useBreadcrumbStore = create<BreadcrumbState>((set) => ({
  trail: null,
  setTrail: (trail) => set({ trail }),
}));

/** Registriert den Brotkrumenpfad einer Seite für deren Lebensdauer. */
export function useBreadcrumbs(trail: Crumb[] | null): void {
  const setTrail = useBreadcrumbStore((state) => state.setTrail);

  // Die Abhängigkeit hängt am serialisierten Pfad, damit ein neu erzeugtes
  // Array bei gleichem Inhalt keine Endlosschleife auslöst.
  const serialized = JSON.stringify(trail);

  useEffect(() => {
    setTrail(trail);
    return () => setTrail(null);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [serialized, setTrail]);
}
