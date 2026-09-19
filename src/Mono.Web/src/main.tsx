import React from 'react'
import ReactDOM from 'react-dom/client'
import App from './App'
import { MonoProvider } from './state/MonoProvider'
import { installArtworkFallback } from './lib/artwork'
import './index.css'

// 커버가 없거나 파일이 사라진 곡에서 깨진 이미지 아이콘이 뜨지 않게 한다.
installArtworkFallback()

ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <MonoProvider>
      <App />
    </MonoProvider>
  </React.StrictMode>,
)
