import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { RouterProvider } from '@tanstack/react-router';
import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';

import './styles/app.css';

import { AppProviders } from '@/AppProviders';
import { applyRuntimeConfig } from '@/lib/api/client';
import { loadRuntimeConfig } from '@/lib/config/runtime';
import { router } from '@/router';
import { initialiseSession } from '@/stores/session';

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      // Netzwerkfehler einmal wiederholen, fachliche Fehler (4xx) nicht.
      retry: 1,
      refetchOnWindowFocus: true,
      staleTime: 5_000,
    },
  },
});

const container = document.getElementById('root');
if (!container) throw new Error('Das Wurzelelement #root fehlt in index.html.');

/**
 * Adresse der API und des Identity Providers stehen erst zur Laufzeit fest. Beides
 * muss geladen sein, bevor die Anwendung das erste Mal zeichnet: Sonst liefe der
 * erste Aufruf gegen die falsche Adresse oder ganz ohne Anmeldung.
 */
async function start() {
  await loadRuntimeConfig();
  applyRuntimeConfig();
  initialiseSession();

  createRoot(container!).render(
    <StrictMode>
      <QueryClientProvider client={queryClient}>
        <AppProviders>
          <RouterProvider router={router} />
        </AppProviders>
      </QueryClientProvider>
    </StrictMode>,
  );
}

void start();
