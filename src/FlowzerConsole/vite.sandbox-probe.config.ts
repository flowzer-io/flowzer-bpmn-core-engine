import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import { fileURLToPath, URL } from 'node:url';
const blocked = fileURLToPath(new URL('../../tests/form-embedding/fixtures/blockedConsoleApi.ts', import.meta.url));
export default defineConfig({
  plugins: [react()],
  // Vite-Library-Builds ersetzen dies anders als der normale App-Build nicht automatisch.
  define: { 'process.env.NODE_ENV': JSON.stringify('production') },
  resolve: { alias: [
    { find: '@/lib/api/queries', replacement: blocked },
    { find: '@/lib/api/client', replacement: blocked },
    { find: '@', replacement: fileURLToPath(new URL('./src', import.meta.url)) },
  ] },
  build: { outDir: '../../tests/form-embedding/.probe-dist', emptyOutDir: true,
    lib: { entry: 'src/embed-probe.tsx', name: 'FlowzerSandboxProbe', formats: ['iife'], fileName: () => 'probe.js' },
    rollupOptions: { output: { inlineDynamicImports: true } },
  },
});
