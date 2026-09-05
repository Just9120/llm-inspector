import { defineConfig } from 'vite';
import { svelte } from '@sveltejs/vite-plugin-svelte';

export default defineConfig({
  plugins: [svelte()],
  base: './',
  server: { host: '127.0.0.1', strictPort: true, port: 5173 },
  build: { target: 'es2022', sourcemap: false },
});
