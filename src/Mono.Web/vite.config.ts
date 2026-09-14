import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const here = path.dirname(fileURLToPath(import.meta.url))

// Mono Control UI.
// 산출물은 Mono.Core 의 wwwroot 로 떨어지고, Core 가 그대로 정적 서빙한다.
// Control(WebView2 셸)은 http://127.0.0.1:7702 를 띄우기만 한다.
export default defineConfig({
  base: './',
  build: {
    outDir: path.resolve(here, '../Mono.Core/wwwroot'),
    emptyOutDir: true,
    sourcemap: false,
  },
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: { '@': path.resolve(here, './src') },
  },
  server: {
    port: 5273,
    strictPort: true,
    // 개발 서버에서도 Core 의 REST/WS 를 그대로 쓴다.
    proxy: {
      '/api': { target: 'http://127.0.0.1:7702', changeOrigin: true },
      '/hub': { target: 'http://127.0.0.1:7702', ws: true, changeOrigin: true },
      '/ws': { target: 'ws://127.0.0.1:7702', ws: true },
    },
  },
})
