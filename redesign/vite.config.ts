import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const here = path.dirname(fileURLToPath(import.meta.url))

// Mono Control UI — 디자인 리뉴얼 빌드.
//
// 원본(src/Mono.Web)은 건드리지 않는다. 이 폴더는 같은 Core 규약을 쓰는 별도 프런트엔드다.
// CORE 환경 변수로 붙을 Core 를 고른다. 기본은 모의 Core(mock/core.mjs, 7802).
//   CORE=http://127.0.0.1:7702 npm run dev   → 진짜 Core 에 붙는다
const core = process.env.CORE ?? 'http://127.0.0.1:7802'

export default defineConfig({
  base: './',
  build: { outDir: path.resolve(here, 'dist'), emptyOutDir: true, sourcemap: false },
  plugins: [react()],
  resolve: { alias: { '@': path.resolve(here, './src') } },
  server: {
    port: 5373,
    strictPort: true,
    proxy: {
      '/api': { target: core, changeOrigin: true },
      '/hub': { target: core, ws: true, changeOrigin: true },
      '/ws': { target: core.replace(/^http/, 'ws'), ws: true },
    },
  },
})
