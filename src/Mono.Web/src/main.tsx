import React from 'react'
import ReactDOM from 'react-dom/client'
// 글꼴은 전부 번들에 싣는다. 데스크톱 앱은 오프라인에서도 같은 모습이어야 한다.
import '@fontsource-variable/plus-jakarta-sans'
import 'wanted-sans/fonts/webfonts/variable/split/WantedSansVariable.css'
import '@fontsource-variable/inter'
import 'pretendard/dist/web/variable/pretendardvariable-dynamic-subset.css'
import '@fontsource-variable/jetbrains-mono'
import './styles/tokens.css'
import './styles/base.css'
import './styles/shell.css'
import './styles/pages.css'
import App from './App'
import { MonoProvider } from './state/MonoProvider'

ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <MonoProvider>
      <App />
    </MonoProvider>
  </React.StrictMode>,
)
