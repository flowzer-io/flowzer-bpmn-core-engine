/// <reference types="vitest/config" />
import { fileURLToPath, URL } from 'node:url';

import tailwindcss from '@tailwindcss/vite';
import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';

// Die API-Basis-URL ist zur Laufzeit über VITE_FLOWZER_API_URL konfigurierbar.
// Im Dev-Betrieb proxen wir stattdessen `/api`, damit CORS und Cookies neutral bleiben.
const DEV_API_TARGET = process.env.FLOWZER_API_URL ?? 'http://localhost:5182';

export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  optimizeDeps: {
    // bpmn-js wird über mehrere Einstiegspunkte geladen (Viewer, NavigatedViewer,
    // Modeler). Ohne diese Liste bündelt Vite sie getrennt vor und zieht dabei
    // mehrere Kopien von diagram-js — die Registries der Kopien kennen sich
    // gegenseitig nicht, und der Import bricht mit "rootElement required" ab.
    include: [
      'bpmn-js/lib/Viewer',
      'bpmn-js/lib/NavigatedViewer',
      'bpmn-js/lib/Modeler',
      'bpmn-js-properties-panel',
      '@bpmn-io/properties-panel',
      'camunda-bpmn-js-behaviors/lib/camunda-cloud',
      'diagram-js',
    ],
  },
  server: {
    port: 5273,
    proxy: {
      '/api': {
        target: DEV_API_TARGET,
        changeOrigin: true,
        rewrite: (path) => path.replace(/^\/api/, ''),
      },
    },
  },
  build: {
    target: 'es2022',
    sourcemap: true,
    rollupOptions: {
      output: {
        // bpmn-js und Form.io sind groß und werden nur auf einzelnen Routen gebraucht.
        manualChunks: {
          bpmn: ['bpmn-js', 'bpmn-js-properties-panel', '@bpmn-io/properties-panel'],
          formio: ['@formio/js'],
        },
      },
    },
  },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/test/setup.ts'],
    css: false,
  },
});
