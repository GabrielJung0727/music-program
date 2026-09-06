# Mono Control — 문서 100% 패리티 설계

작성 2026-09-05 · 대상 브랜치 `cursor/avalonia-native-control`

기준 문서: [`01-기획명세서`](../../01-기획명세서.md) · [`02-아키텍처`](../../02-아키텍처.md) · [`03-구현현황`](../../03-구현현황.md) · [`04-UIUX기획서`](../../04-UIUX기획서.md)

---

## 1. 목표

`Mono.Control.exe` 한 창에서 기획 문서가 정의한 기능과 화면을 빠짐없이 쓸 수 있게 한다.
Core·Output·MATP·룸 상태머신은 이미 동작하므로 **재작성하지 않는다.** 이 작업의 결과물은
(1) Control의 화면·명령 배선 완성, (2) 그 화면을 채우기 위해 Core에 없는 데이터 모델의 보강이다.

### 성공 기준

- `MessageTypes`가 정의한 사용자 대상 명령 중 GUI에서 도달 불가능한 것이 없다.
- `04-UIUX기획서` §3.1 사이드바 항목과 §4 화면 스펙이 전부 실재한다.
- `dotnet build` 경고 0, `dotnet test` 전량 통과.
- 콘솔 창 0개, 브라우저 자동 실행 없음 (§5.7 체크리스트 유지).

### 범위 밖

- Roon 전 기능 패리티. 문서에 적힌 것까지가 범위다.
- 파트너 키가 필요한 Tidal/Qobuz 실계정 QA, 물리 기기 2대 LAN 실측, ASIO/네이티브 DSD 청음.
  이들은 코드가 아니라 현장·계약에 달렸다(`03-구현현황` §6).
- MATP 프레이밍·클럭 알고리즘 변경.

---

## 2. 현재 상태 진단

### 2.1 프로토콜은 준비됐고 UI가 없다

`RoomManager.Snapshot`은 이미 다음을 매 브로드캐스트마다 실어 보낸다:

```
pins · heatmap · reactions · reactionCounts · allowedReactionEmoji
members(role·isOutput·spectator·stats) · outputs(caps·volume·badge·note)
requests · spectators · queue · albumTracks · relatedArtists · lyrics
inviteCode · inviteExpiresAt · 정책 플래그 전량 · autoplay 후보
```

`MonoMessage`에도 `InviteAction` `TargetPeerId` `Member` `Volume` `Policy` `SourceMode`
`PairingCode` `PlaylistId` `Index` `Delta`가 이미 있다.

Control은 이 스냅샷을 `JsonNode`로 손파싱하며 일부만 읽고 나머지를 버린다.
**따라서 대부분의 작업은 서버 확장이 아니라 Control의 소비·발신 배선이다.**

### 2.2 GUI에서 도달 불가능한 명령

전송·내부용(`clock_*` `matp_audio` `output_hello` `caps` `snapshot` `timeline` `welcome` `error` `room_state` `autoplay_candidates`)을 제외한 실측 목록:

| 영역 | 명령 |
| --- | --- |
| 룸 관리자 | `invite` `kick` `transfer_host` `set_role` `spectate` `set_policy` `set_room_flags` `set_source_mode` |
| 큐 협업 | `request_track` `approve_request` `reject_request` `remove_queue` `move_queue` `jump_to` |
| 소셜 | `pin` `remove_pin` `seek_pin` `reaction_heatmap` `follow_artist` `graph` |
| 플레이리스트·아카이브 | `create_playlist` `load_playlist` `export_m3u` `archive` `share_session` |
| 존 | `rename_zone` `set_zone_mode` `zone_add_member` `zone_remove_member` `delete_zone` |
| 재생 기본 | `volume` `set_volume` |
| 원격 | `pair` `redeem` |

### 2.3 문서 대비 화면 누락

- **하단 바** (§3.2): 볼륨 · 존/장치 선택 · ♥ · 큐 토글 · 시크바 히트맵/핀 레이어가 전부 없다.
- **사이드바** (§3.1): `Composers` `Compositions` `Folders` 항목 자체가 없다.
  `History` `Playlists`는 nav만 있고 본문은 트랙 그리드를 재활용한다.
- **Now Playing** (§4.4): 스펙 5모드 중 3모드(가사·아티스트·크레딧)만. 앨범·연혁(위키) 없음.
  몰입 모드에 히트맵·핀·오토플레이 카드가 없다.
- **설정** (§4.7): 스펙 10트리 중 실재는 표시 이름·라이브러리 경로·업데이트 3개.
- **단축키** (§6): `Space` `/` `Esc` 미구현.
- **검색**: Core의 별칭 기반 다국어 검색(`search`)을 두고 Control은 클라이언트 `Contains()`만 쓴다.
  `요네즈 켄시 → 米津玄師`가 GUI에서 동작하지 않는다 — 기획의 핵심 기능이 죽어 있다.

### 2.4 데이터가 없어서 화면을 못 만드는 것

UI만으로 해결되지 않고 Core 확장이 선행돼야 하는 항목:

| 항목 | 현재 | 필요 |
| --- | --- | --- |
| Genre | **Core에 필드 없음.** Control이 아티스트명에 `Coltrane\|Miles\|Brubeck\|Hiromi` 포함 여부로 Jazz 판정 | `Track.Genres` 태그 스캔·저장·집계 |
| Composer | 도메인·스캔·저장 전무 | `Track.Composers` + Composer 노드 |
| Composition | 없음 | 작품(Work) 그룹핑 — 같은 작품의 여러 연주 비교 |
| Folders | 단일 `Mono:LibraryRoot` | 복수 스캔 루트 등록/제거/상태 |

### 2.5 구조 문제

`MainViewModel.cs` 969줄 + `MainWindow.axaml` 675줄에 9개 화면이 `IsVisible` 플래그로 눌려 담겨 있다.
여기에 위 항목을 더하면 유지가 불가능하다.

---

## 3. 접근

**페이지 분해 + 스냅샷 타입화.** 전면 재작성(검증된 로직을 위험에 빠뜨림)이나 현행 단일 VM
유지(3,000줄+ 단일 파일)를 택하지 않는다.

```
Models/RoomSnapshot.cs        Core 스냅샷 전체의 타입 DTO
Services/CoreSession.*.cs     partial 분리: Room / Queue / Dsp / Zone / Library / Admin
ViewModels/ShellViewModel     내비 · 하단바 · 세션 · 테마 · 업데이트 · 단축키
ViewModels/Pages/*.cs         화면별 VM
Views/Pages/*.axaml           화면별 UserControl. MainWindow는 셸 + 하단바만
```

리팩터링은 **기존 동작을 바꾸지 않는다.** 화면을 옮긴 뒤 기능을 얹는다.

### 단위 경계

각 페이지 VM은 자기 화면의 상태와 명령만 소유하고, 공유가 필요한 것(현재 룸 스냅샷, 연결
상태, 카탈로그)은 셸이 보유해 주입한다. 페이지 VM은 `CoreSession`에만 의존하고 서로를
참조하지 않는다.

---

## 4. 단계별 설계

각 단계는 `dotnet build`(경고 0) + `dotnet test` 통과로 닫고 커밋한다.

### 단계 1 — 기반

- `RoomSnapshot` DTO: `System.Text.Json` 소스 생성 대신 기존 옵션(`LineFraming.JsonOptions`)과
  호환되는 POCO. 스냅샷의 모든 필드를 수용하되 **알 수 없는 필드는 무시**해 Core 선행 배포를 견딘다.
- `ApplyRoomState`를 `JsonNode` 손파싱에서 DTO 역직렬화로 교체.
- `CoreSession`에 §2.2의 명령 전체 추가.
- 화면을 `Views/Pages/*.axaml`로 이전, `MainViewModel`을 셸로 축소.
- **테스트**: 실제 `RoomManager.Snapshot` 출력을 직렬화 → `RoomSnapshot` 역직렬화 라운드트립으로
  Core↔Control 계약을 고정. 필드 누락 시 실패한다.

### 단계 2 — 하단 바 완성

- 볼륨: `outputs[].hardwareVolume` `volumePercent` 반영. 하드웨어 볼륨을 우선하고,
  정책이 디지털 감쇠를 막으면(Audiophile/Bit-perfect 전용) 슬라이더를 비활성 + 사유 툴팁.
- 존/장치 선택 팝업: 스냅샷의 `outputs`와 `list_zones` 응답을 합쳐 현재 출력 대상을 전환한다
  (존은 룸 스냅샷에 실리지 않으므로 별도 조회다).
- ♥ 토글, 큐 드로어 토글.
- **시크바 레이어**: 트랙 + Point 채움 + 핀 마커(`pins.onCurrentTrack`) + 반응 히트맵(`heatmap`).
  커스텀 `Control`로 그린다(마커가 수백 개여도 60fps 유지).
- 시그널 패스 팝오버: 소스 → DSP 체인 → 장치 + Bit-perfect 판정.

### 단계 3 — 라이브러리

**Core 선행 작업**

- `Track.Genres: List<string>`, `Track.Composers: List<string>` 추가. `LibraryScanner`가
  TagLib `Tag.Genres` / `Tag.Composers`에서 채운다.
- `Composition` 도메인: `(작곡가, 정규화된 작품명)`으로 트랙을 묶는다. 작품명 정규화는
  괄호 안 편성·조성·번호 표기를 보존하되 연주자/앨범 표기를 제거하는 보수적 규칙 —
  **확신이 없으면 묶지 않는다**(잘못된 병합이 미병합보다 나쁘다).
- `CatalogStore` 스키마 확장 + 마이그레이션. 기존 `catalog.db`는 재스캔 없이 열려야 하고,
  새 컬럼은 빈 값으로 시작한다.
- 스캔 루트 복수화: `Mono:LibraryRoots` 배열. 기존 단일 `Mono:LibraryRoot`도 계속 읽는다.

**Control 화면**

- `Genres`: 하드코딩 타일 제거, 실제 장르 집계로 타일 생성. Hi-Res/DSD는 장르가 아니라
  **품질 필터**이므로 별도 칩으로 분리한다(문서의 "HI-RES 장르 진입" 의도 유지).
- `Composers`: 원형 아바타 그리드, 사진 없으면 이니셜.
- `Compositions`: 작품명·작곡가·연주 수 테이블, 펼치면 같은 작품의 여러 연주 비교.
- `Folders`: 스캔 루트별 트리 + 트랙 수 + 스캔 상태.
- `History` `Playlists`: 전용 뷰. 히스토리는 날짜 타임라인 + 다시 재생,
  플레이리스트는 자동 3종 + 믹스 + M3U 내보내기.
- 앨범 상세: 트랙 목록 · 라이너 · 크레딧.
- **검색을 서버측 `search`로 전환** — 다국어 별칭 복구. 입력 디바운스 250ms.

### 단계 4 — 라운지

- 관리자 패널: 초대 발급/연장/폐기, 킥, 역할 지정, 호스트 위임, 품질 정책, 소스 모드,
  룸 플래그(시킹·코멘트·채팅·큐 잠금·자동 다음곡·스마트 오토플레이·DSP 잠금·최대 인원), 참관 전환.
  **호스트가 아니면 패널을 숨기지 않고 비활성 + 사유를 표시한다.**
- 큐 협업: Request → 승인/거절, 순서 변경(드래그), 제거, 바로 이동.
- 타임스탬프 핀: 생성·삭제·클릭 시 이동(정책 준수).
- 반응 4종(❤️🎉👏🔥) — 곡당 3회 제한을 UI에서도 안내.
- 뮤지션 관계도: `graph` 조회 + 관련 아티스트 탐색 + "따라가기".
- 세션 종료 → 아카이브 동의 → 플레이리스트 생성.

### 단계 5 — 마무리

- Now Playing 5모드(가사·아티스트·앨범·연혁·크레딧) + 몰입 모드 내 히트맵·핀·오토플레이.
- 설정 10트리: General · Storage · Services · Audio · Library · Lounge · DSP · Backups · About · Shortcuts.
- 단축키: `Space` 재생/일시정지, `/` 검색 포커스, `Esc` 몰입·모달 닫기.
- 존 관리: 생성·이름변경·모드(Sync/Independent)·멤버 추가/제거·삭제.
- 원격 Control 페어링(`pair`/`redeem`).
- 백업: `catalog.db`/`history.db` 내보내기 (음원 파일 제외).

---

## 5. 에러 처리

Core는 정책 위반을 `error` 필드로 돌려준다. Control은 **버튼을 숨기지 않는다.**
비활성 + 사유 툴팁으로 노출한다 — 문서 §6 "신뢰 UI"의 요구다.

| 상황 | 표시 |
| --- | --- |
| 볼륨 잠금(Bit-perfect 전용) | 슬라이더 비활성 · "이 룸은 디지털 볼륨을 막습니다 — 하드웨어 볼륨을 쓰세요" |
| 시킹 금지 | 시크바 비활성 · "호스트가 시킹을 잠갔습니다" |
| 큐 잠금 | 추가 버튼 → Request 버튼으로 전환 |
| 스트리밍 미구독 | 큐 추가 차단 + 연동 CTA |
| 포맷 불가 | "이 룸은 DSD256 고정 — 기기가 지원하지 않음" + 참관 옵션 |
| Output 오프라인 | 장치 목록 Offline 배지 + 재생 시 경고 |

연결 끊김은 기존 `ConnectionChanged`를 유지하되, 재연결 시 스냅샷을 다시 받아 화면을 복구한다.

---

## 6. 테스트

Avalonia 뷰의 헤드리스 테스트는 비용 대비 효용이 낮다. 대신 **계약과 로직**을 고정한다.

| 대상 | 방법 |
| --- | --- |
| Core↔Control 스냅샷 계약 | `RoomManager.Snapshot` → JSON → `RoomSnapshot` 라운드트립. 필드 누락 시 실패 |
| 명령 직렬화 | 각 `CoreSession` 명령이 Core가 파싱 가능한 `MonoMessage`를 만드는지 |
| 작품명 정규화 | 병합해야 할 쌍 / 병합하면 안 되는 쌍 표 기반 |
| 장르·작곡가 스캔 | TagLib 태그 → `Track.Genres`/`Composers` 매핑 |
| 카탈로그 마이그레이션 | 구 스키마 `catalog.db`를 열어 새 컬럼이 빈 값으로 시작하는지 |
| 기존 44개 | 회귀 가드로 유지 — 변경 금지 |

---

## 7. 위험

| 위험 | 완화 |
| --- | --- |
| 리팩터링 중 기존 동작 손실 | 단계 1은 기능 추가 없이 이동만. 스냅샷 계약 테스트로 고정 |
| `catalog.db` 스키마 변경으로 기존 사용자 재스캔 강요 | 컬럼 추가만, 기존 행 보존. 마이그레이션 테스트 |
| 작품 그룹핑 오병합 | 보수적 규칙 — 확신 없으면 분리 유지 |
| 화면 증가로 Core 왕복 폭증 | 페이지 진입 시 1회 조회 + 스냅샷 구독. 검색은 디바운스 |
| 히트맵·핀 마커 렌더 비용 | 커스텀 `Control` 직접 렌더, 요소당 `Border` 생성 금지 |

---

## 8. 문서 갱신

구현이 끝나면 `03-구현현황.md`를 실제에 맞춘다. 현재 문서는 GUI가 없는 기능을 ✅로 적어
사실과 어긋난다 — 이번 작업의 재발 방지책으로 **"Core 구현"과 "GUI 도달 가능"을 분리해 표기**한다.
