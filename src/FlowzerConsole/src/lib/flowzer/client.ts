import { FlowzerClient } from '@flowzer/sdk';

import {
  API_BASE_URL,
  consoleAuthenticatedFetch,
  getConsoleCsrfToken,
  reportUnauthorized,
} from '@/lib/api/client';
import { getRuntimeConfig } from '@/lib/config/runtime';

/**
 * Baut den hostneutralen Client mit den ausschließlich in der Console erlaubten
 * BFF-Details. Weder BFF-Endpunkte noch Development-Header sind Teil von
 * `@flowzer/sdk` und werden daher auch nicht von eingebetteten Hosts geerbt.
 */
export function createConsoleFlowzerClient(): FlowzerClient {
  return new FlowzerClient({
    baseUrl: API_BASE_URL,
    ...(getRuntimeConfig().bffEnabled
      ? { auth: { kind: 'cookie' as const, getCsrfToken: getConsoleCsrfToken } }
      : {}),
    fetch: consoleAuthenticatedFetch,
    onUnauthorized: reportUnauthorized,
  });
}
