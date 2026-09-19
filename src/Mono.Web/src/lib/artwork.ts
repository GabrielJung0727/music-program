/**
 * 커버 아트가 없을 때 쓰는 기본 이미지.
 *
 * 빈 문자열을 <img src> 에 그대로 넣으면 브라우저가 깨진 이미지 아이콘을 그린다.
 * 라운지에서 엘범 없는 곡을 틀면 호스트 카드가 통째로 깨져 보이던 원인이다.
 * 파일을 하나 더 두는 대신 데이터 URI 로 둔다 — 오프라인에서도 뜨고, 요청이 없으니
 * 404 가 다시 깨진 아이콘으로 돌아올 일도 없다.
 */
const PLACEHOLDER = `<svg xmlns="http://www.w3.org/2000/svg" width="240" height="240" viewBox="0 0 240 240">
  <defs>
    <linearGradient id="g" x1="0" y1="0" x2="1" y2="1">
      <stop offset="0%" stop-color="#3f3f46"/>
      <stop offset="100%" stop-color="#18181b"/>
    </linearGradient>
  </defs>
  <rect width="240" height="240" fill="url(#g)"/>
  <circle cx="120" cy="120" r="54" fill="none" stroke="#71717a" stroke-width="2"/>
  <circle cx="120" cy="120" r="34" fill="none" stroke="#52525b" stroke-width="1.5"/>
  <circle cx="120" cy="120" r="9" fill="#a1a1aa"/>
</svg>`

export const FALLBACK_ART = `data:image/svg+xml;utf8,${encodeURIComponent(PLACEHOLDER)}`

/** 비어 있으면 기본 이미지로 바꾼다. */
export function albumArt(src?: string | null): string {
  return src && src.trim().length > 0 ? src : FALLBACK_ART
}

/**
 * 경로는 있는데 파일이 사라진 경우까지 막는다. onError 는 한 번만 갈아끼운다 —
 * 기본 이미지마저 실패하면 무한 루프가 된다.
 */
export function onArtError(e: { currentTarget: HTMLImageElement }): void {
  const img = e.currentTarget
  if (img.dataset.fallbackApplied === "1") return
  img.dataset.fallbackApplied = "1"
  img.src = FALLBACK_ART
}

/**
 * 앱 전체에 커버 아트 안전망을 건다.
 *
 * 커버를 그리는 자리가 수십 군데라 하나씩 onError 를 다는 방식은 새 화면이 생길 때마다
 * 다시 빠진다. 이미지 로드 실패는 버블링하지 않으므로 캡처 단계에서 한 번만 잡는다.
 */
export function installArtworkFallback(): void {
  document.addEventListener(
    "error",
    (event) => {
      const target = event.target as HTMLElement | null
      if (!target || target.tagName !== "IMG") return
      const img = target as HTMLImageElement
      if (img.dataset.noFallback === "1" || img.dataset.fallbackApplied === "1") return
      img.dataset.fallbackApplied = "1"
      img.src = FALLBACK_ART
    },
    true,
  )
}
