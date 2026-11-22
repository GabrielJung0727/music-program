/**
 * 스트리밍 서비스를 지금 연결할 수 있는가, 어느 키로 연결하는가.
 *
 * Core 의 streaming_accounts 가 서비스마다 `available` 과 `keySource` 를 준다. 키 값은 오지 않는다.
 * 이 파일은 상태(state/)를 모른다 — 모양만 안다. 화면 · 모달이 같은 규칙을 쓰게 하려는 자리다.
 */

export interface ServiceStatus {
  available?: boolean
  keySource?: "override" | "build" | "default" | "none"
}

/**
 * 이 서비스를 지금 연결할 수 있는가. Core 가 `available: false` 라고 하면 "준비 중"이다.
 * 값을 보내지 않는 옛 Core 는 연결할 수 있는 것으로 본다.
 */
export function serviceReady(account: ServiceStatus | undefined): boolean {
  return account?.available !== false
}

export const NOT_READY_BLURB = "준비 중입니다. 연결이 열리면 이 자리에서 바로 연결할 수 있습니다."

const KEY_SOURCE_LABEL: Record<NonNullable<ServiceStatus["keySource"]>, string> = {
  override: "사용자 지정 키",
  build: "내장 키",
  default: "시험용 기본 키",
  none: "키 없음",
}

/** 설정 화면에 보일 "어느 키로 연결하는가". 키 값은 화면에 오지 않는다. */
export function keySourceLabel(account: ServiceStatus | undefined): string | null {
  return account?.keySource ? KEY_SOURCE_LABEL[account.keySource] : null
}
