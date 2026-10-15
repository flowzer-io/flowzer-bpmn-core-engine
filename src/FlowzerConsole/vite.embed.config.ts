import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';
import { fileURLToPath, URL } from 'node:url';
/** Eigenes klassisches IIFE: ein opaque Frame benötigt keine Credential-/ESM-Lader. */
export default defineConfig({
  plugins: [react(), tailwindcss()], publicDir: false,
  define: { 'process.env.NODE_ENV': JSON.stringify('production') },
  resolve: { alias: [
    { find: '@/lib/api/queries', replacement: fileURLToPath(new URL('./src/embed/consoleBoundary.ts', import.meta.url)) },
    { find: '@/lib/api/client', replacement: fileURLToPath(new URL('./src/embed/consoleBoundary.ts', import.meta.url)) },
    { find: '@', replacement: fileURLToPath(new URL('./src', import.meta.url)) },
  ] },
  build: { outDir: 'dist/embed-assets', emptyOutDir: true,
    lib: { entry: 'src/embed/startEmbed.tsx', name: 'FlowzerEmbeddedForm', formats: ['iife'], fileName: () => 'embed.js', cssFileName: 'embed' },
    rollupOptions: { output: { inlineDynamicImports: true } },
  },
});
