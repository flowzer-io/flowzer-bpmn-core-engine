import type { ReactNode } from 'react';
import { Toaster } from 'sonner';

import { useApplyAppearance, useResolvedTheme } from '@/stores/appearance';

// Der Import registriert den Benutzerkontext beim API-Client.
import '@/stores/session';

/**
 * Hüllt die Anwendung in globale Nebenwirkungen: Darstellungseinstellungen an
 * das <html>-Element schreiben und den Toast-Bereich bereitstellen.
 */
export function AppProviders({ children }: { children: ReactNode }) {
  useApplyAppearance();
  const theme = useResolvedTheme();

  return (
    <>
      {children}
      <Toaster
        position="bottom-center"
        theme={theme}
        richColors={false}
        closeButton
        toastOptions={{
          style: {
            background: 'var(--surface)',
            border: '1px solid var(--border)',
            color: 'var(--text)',
            borderRadius: '22px',
            boxShadow: 'var(--shadow-lg)',
            fontFamily: 'var(--font-ui)',
          },
        }}
      />
    </>
  );
}
