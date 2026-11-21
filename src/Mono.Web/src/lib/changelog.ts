// 이번 버전에서 바뀐 것.
//
// 릴리스 노트는 저장소의 CHANGELOG.md 한 곳에만 있다(GitHub 릴리스 본문도 publish.ps1 이 거기서 꺼낸다).
// 화면은 빌드할 때 그 파일을 통째로 싣고, 자기 버전의 "## <버전>" 절을 꺼내 보여 준다.
// 인터넷이 없어도 되고, 앱에 실린 노트와 앱의 버전이 어긋날 일이 없다.
import changelog from "../../../../CHANGELOG.md?raw"

export const APP_VERSION = __MONO_VERSION__

const SEEN_KEY = "mono.seenReleaseNotes"

/** CHANGELOG.md 의 "## <version>" 절. 없으면 null. */
export function releaseNotes(version: string = APP_VERSION): string | null {
  const lines = changelog.split(/\r?\n/)
  const start = lines.findIndex((l) => l.trim() === `## ${version}`)
  if (start < 0) return null
  const body: string[] = []
  for (let i = start + 1; i < lines.length; i++) {
    if (lines[i].startsWith("## ")) break
    body.push(lines[i])
  }
  const text = body.join("\n").trim()
  return text || null
}

function seen(): string | null {
  try { return localStorage.getItem(SEEN_KEY) } catch { return null }
}

/** 이 버전의 노트를 봤다(또는 보여 줄 필요가 없다). */
export function markReleaseNotesSeen(version: string = APP_VERSION) {
  try { localStorage.setItem(SEEN_KEY, version) } catch { /* 저장이 막혔으면 다음 실행에 한 번 더 보인다 */ }
}

/** 업데이트한 뒤 처음 켰는가 — 이 버전의 노트가 있고 아직 보지 않았다. */
export function hasUnseenReleaseNotes(version: string = APP_VERSION): boolean {
  return seen() !== version && releaseNotes(version) !== null
}
