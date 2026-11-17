# 인수인계 2026-11-17: 재생 엔진, 랜딩 사이트

2026-11-17 회사 PC(NT751)에서 한 작업을 다른 PC 에서 이어 가기 위한 문서다.
앞선 인수인계는 [`08-인수인계.md`](08-인수인계.md), 출력 경로의 배경은 [`02-아키텍처.md`](02-아키텍처.md).

**가장 중요한 한 줄: 이 브랜치의 C# 은 빌드만 됐다. 테스트는 한 건도 돌지 않았고, 소리는 한 번도 내 보지 않았다.**
회사 PC 의 애플리케이션 제어 정책이 새로 빌드한 테스트 DLL 을 막았고(0x800711C7), ASIO 장치도 없었다.
그러니 아래 0장과 3장을 끝내기 전에는 릴리스하지 않는다.

같은 날 만든 랜딩 사이트(`landing/`)와 그 배포는 6장에 따로 적었다. 앱 릴리스와는 따로 올릴 수 있다.

## 0. 맨 먼저 할 일

```powershell
git fetch origin
git checkout feature/playback-engine
git pull
$env:DOTNET_ROLL_FORWARD = "Major"   # .NET 8 런타임이 없는 PC 라면
dotnet test Mono.slnx
```

새로 쓴 테스트 파일은 다섯 개다. 전부 **처음 실행되는 것**이므로, 실패하면 코드가 틀렸을 수도 있고 테스트가 틀렸을 수도 있다.

| 파일 | 지키는 것 | 실패했을 때 먼저 볼 곳 |
| --- | --- | --- |
| `PcmMathTests` | 정수 PCM 이 float 를 거쳐도 그대로인가, 디더, 볼륨 경사 | `FlacDecodesToTheExactSampleValue` 는 픽스처(`seek-markers.flac`)의 값이 정확히 n/8 이라고 가정한다. 픽스처가 다르게 만들어졌다면 테스트 쪽을 고친다 |
| `DspChainTests` | 조각으로 나눠 넣어도 한 번에 넣은 것과 같은가, 업샘플러 품질 | 거울상 -70dB 기준. 파이썬 검산에서는 -99dB 였다 |
| `FadeProviderTests` | 평소에는 바이트를 건드리지 않는가, 끊을 때 0 까지 내려가는가 | |
| `LocalPlayerTests` | 갭리스 이음이 샘플 단위로 정확한가, 크로스페이드, Core 세대 전환과 맞물리기 | 가장 복잡한 부분. 여기가 실패하면 `LocalPlayer.cs` 를 의심한다 |
| `TrackTransitionTests` | Core 가 다음 곡을 미리 알리고, 그 곡으로 넘어가고, 이어 가도 되는 세대로 표시하는가 | 시각에 기대는 범위 검사(`InRange(..., 100, 400)`)가 느린 PC 에서 빗나갈 수 있다 |

앞 작업의 `QueueEditTests`(8개)도 회사 PC 에서는 돌지 못했다. `LocalSeekTests`(16개)만 실제로 통과를 확인했다.

## 1. 지금 상태

| 브랜치 | 내용 | 상태 |
| --- | --- | --- |
| `development` | 탐색 시 끊김 · 대기열 삭제 중 자동 재생 수정(PR #13)까지 들어 있다 | 기준 |
| `feature/playback-engine` | 이 문서의 작업. `development` 보다 커밋 7개 앞, 머지 커밋 1개 뒤 | **이어서 할 곳. 아직 합치지 않는다** |

커밋(오래된 순):

1. `Keep PCM exact through float, and carry DSP state across chunks.` 공용 수학(`Mono.Shared/PcmMath.cs`, `DspChain.cs`)과 Core 의 DSP 입구.
2. `Announce the next track and mark natural advances as seamless.` Core · 프로토콜. 다음 곡 예고, `seamlessEpoch`, `set_transition`.
3. `Join tracks in the output worker: gapless, crossfade, and clean cuts.` 출력 워커 전부.
4. `Add track transition and ASIO output channel settings.` 셸과 설정 화면.
5. 이 문서.
6. `Make the UI say what the signal path does.` 4.1 의 세 곳.
7. `Stop claiming conversions, modes and latencies that are not real.` 4.2 의 세 곳.

회사 PC 에서 확인한 것: 솔루션 빌드 오류 0, 웹 타입 검사와 프로덕션 빌드, 설정 화면(모의 Core + 헤드리스 Chrome 으로 눌러 봄), 업샘플러 설계(파이썬 검산).

## 2. 무엇이 바뀌었나

| 항목 | 한 일 | 주로 볼 파일 |
| --- | --- | --- |
| 비트퍼펙트 | 8388608 로 나누고 8388607 을 곱해 버리던 것을 같은 눈금 + 반올림으로. MF 디코드는 float 를 거치지 않고 정수로 읽는다 | `PcmMath.cs`, `OutputRuntime.cs` 의 `MfStreamingLocalRenderer` |
| 갭리스 | 출력 워커가 다음 곡을 미리 열고, 디코더가 실제로 끝에 닿는 자리에서 잇는다 | `LocalPlayer.cs`, `RoomManager.cs` 의 `PlannedNext` · `AdvanceFinished` |
| 크로스페이드 | 등전력(cos/sin) 곡선, 1~12초, 두 곡 길이의 절반까지 | `LocalPlayer.ReadOverlap`, `RoomManager.CrossfadeOverlapMs` |
| DSP | 혼자 듣기에서도 걸린다. 필터 상태를 조각 사이에 이어 간다. 95탭 하프밴드 업샘플러 | `DspChain.cs`, `LocalPlayer.Finish` |
| 딸깍 소리 | 끊을 때 8ms 페이드아웃, 곡 중간에서 다시 시작할 때 8ms 페이드인 | `FadeProvider.cs`, 두 렌더러의 `Flush(bool)` |
| 볼륨 | ASIO 는 float 단계에서, WASAPI 는 TPDF 디더로. 조각 사이를 경사로 잇는다 | `PcmGain`, `AsioAudioRenderer.PushSamples` |
| ASIO 채널 | 첫 쌍이 아닌 출력 쌍 선택 (`--asio-channel`, `--list-asio-channels`) | `AsioAudioRenderer.cs`, `ProcessSupervisor.cs` |
| 설정 화면 | 설정 › 오디오 엔진 › 곡 전환, ASIO 출력 채널 | `Settings.tsx`, `lib/setup.ts` |

요청에 없었는데 고친 것 하나: 팬아웃 룸에서 ASIO 를 쓰면 디지털 볼륨이 Core 와 출력 워커에서 두 번 걸렸다. 워커 쪽을 뺐다(`Program.cs` 의 `fanOutVolume`). 의도와 다르면 되돌린다.

### 이어서 고칠 사람이 알아야 할 구조

- **잇는 자리는 출력 워커가 정한다.** Core 의 타임라인은 벽시계로 흐르고 출력은 버퍼만큼 늦게 나가므로, Core 가 세대를 바꾸는 순간에 맞춰 자르면 꼬리와 머리가 잘린다. 그래서 `LocalPlayer` 가 디코더 기준으로 잇고, Core 타임라인과의 차이를 `_biasMs` 로 들고 있다. 화면의 진행 표시와 소리 사이에 그만큼의 일정한 차이가 남는다.
- **세대 전환은 두 순서로 온다.** 출력이 먼저 넘어가 있으면 `_ahead`, Core 가 먼저 넘어갔으면 `_behind`. `AdoptSeamless` 가 둘을 가른다. 둘 다 아니면 다른 곡이므로 끊는다.
- **Core 는 `seamlessEpoch == epoch` 로 "끊지 말라"고 알린다.** 곡이 끝까지 재생되어 넘어간 경우에만 같다. 넘기기 · 탐색 · 재동기화는 다르다.
- **셔플은 다음 곡을 미리 뽑아 둔다**(`ShufflePlannedItemId`). 예고한 곡과 트는 곡이 달라지면 출력이 열어 둔 곡이 쓸모없어진다.
- 문턱값은 `RenderGate.LocalCatchUpMs`(150), `LocalAheadLimitMs`(500), `LocalPlayer.HeadAnchorMs`(2000), `PrepareLeadMs`(10000), `FadeProvider.RampMs`(8).

## 3. 실제 장치로 확인할 것 (Babyface, ASIO)

로그는 `%LOCALAPPDATA%\MonoData\mono.log`. 출력 워커의 줄에서 `asio open:`, `dsp →`, `render:`(예외), 5초마다 찍히는 `resync= drop=` 을 본다.
화면 없이 바꾸려면 `Mono.Cli` 에서 `transition <off|gapless|crossfade> [초]`.

### 곡 전환

- [ ] 끊김 없이: 라이브 앨범이나 악장이 이어지는 음반(같은 샘플레이트의 FLAC)에서 곡 경계에 틈도 딸깍도 없다.
- [ ] 곡이 넘어간 뒤에도 진행 바 · 가사가 크게 어긋나지 않는다. 앨범 한 장을 끝까지 틀어 어긋남이 쌓이는지 본다.
- [ ] 크로스페이드 5초 · 12초: 겹치는 동안 소리가 꺼지거나 튀지 않는다. **음량이 큰 두 곡에서 클립이 나는지** 듣는다(지금은 합이 풀스케일을 넘으면 그냥 잘린다).
- [ ] 셔플을 켠 채 곡이 끝까지 가면 예고된 곡으로 끊김 없이 넘어간다.
- [ ] 한 곡 반복이 틈 없이 돈다.
- [ ] 샘플레이트가 다른 두 곡(44.1 → 96): 앞 곡이 끝까지 나고, 다음 곡이 **처음부터** 시작한다.
- [ ] 큐의 마지막 곡이 끝까지 난다(꼬리가 잘리지 않는다).
- [ ] 곡 전환을 끄면 예전처럼 끊고 넘어간다.
- [ ] **MP3 · AAC 의 곡 경계에 남는 무음이 얼마나 되는가.** 인코더 지연 · 패딩 보정을 넣지 않았다. 재서 적어 둔다.
- [ ] TIDAL 곡(http 소스)에서도 다음 곡을 미리 여는가, 여는 동안 지금 곡이 끊기지 않는가.

### 끊고 잇기

- [ ] 일시정지 · 탐색 · 다음 곡에서 딸깍 소리가 없다. 다시 재생하면 멈춘 자리에서 이어진다.
- [ ] 곡을 처음부터 틀면 첫 음이 깎이지 않는다(페이드인이 걸리면 안 되는 자리다).
- [ ] 탐색 직후 소리가 뚝뚝 끊기지 않는다(PR #13 의 수정이 새 재생기에서도 살아 있는지).

### 비트퍼펙트 · 볼륨 · DSP

- [ ] 볼륨 100%, DSP 끔, 곡 전환 "끊김 없이"에서 FLAC 이 비트 그대로 나가는가. TotalMix 루프백이 된다면 녹음해서 원본과 빼 본다.
- [ ] 볼륨을 끄는 동안 지퍼 소리가 없다. 작은 볼륨에서 거칠어지지 않는다.
- [ ] 혼자 듣기에서 DSP 를 켜면 실제로 소리가 바뀐다. 파라메트릭 EQ 를 켰을 때 잡음이 얹히지 않는다.
- [ ] 헤드폰 · 스피커 프리셋: 44.1kHz 곡이 88.2kHz 로 열린다(로그의 `asio open:`). 192kHz 곡은 384kHz 를 못 받으므로 올리지 않고 그대로 나야 한다.
- [ ] 설정 › 출력 장치에 "ASIO 출력 채널"이 뜨고, 3/4 를 고르면 헤드폰 단자로 소리가 옮겨 간다.

### 라운지(팬아웃)

- [ ] 공개 라운지에서 재생 · 곡 넘김 · 볼륨이 예전처럼 된다. 볼륨 50% 가 예전보다 크게 들리면 그게 "두 번 걸리던 것"을 고친 결과다.

## 4. 남은 일

재판(`/court`)에서 "최고의 음향 기술과 기능 기술을 구현해 뒀는가"를 물었고 **12 대 0 으로 아니라는 평결**이 나왔다. 이유는 하나였다: 돌려 보지도 들어 보지도 않았다. 뒤집는 조건이 그대로 할 일 목록이다.

1. 0장: `dotnet test` 전부 통과.
2. 3장: Babyface 로 듣고, 갭 · 클립 · 비트 일치를 재서 적는다.
3. ~~화면이 실제 동작과 다르게 말하는 세 곳을 고친다.~~ 같은 날 고쳤다(아래 4.1).

### 4.1 화면을 실제 동작에 맞춘 세 곳

| 어긋나던 곳 | 고친 방식 |
| --- | --- |
| 곡 전환 스위치가 팬아웃 룸에서도 켜진 것처럼 보였다 | 방의 `sourceMode` 가 팬아웃이면 설정의 곡 전환 그룹 맨 위에 "이 방에서는 적용되지 않는다"는 경고를 띄운다. 고른 값은 저장되어 혼자 들을 때 쓰인다 |
| "버퍼 크기"(64~1024 샘플)가 저장만 되고 워커에 전달되지 않았다 | 고르는 단추를 설정과 마법사에서 뺐다. 그 자리에 버퍼를 실제로 누가 정하는지 적었다(ASIO 는 드라이버, WASAPI 는 배타 10ms · 공유 20ms). **연결하지 않고 뺀 것이다**: ASIO 는 호스트가 버퍼 크기를 정할 수 없고, WASAPI 지연을 설정값으로 바꾸는 건 장치 없이 검증할 수 없는 변경이라 하지 않았다 |
| 헤드폰 · 스피커 프리셋이면 무조건 SRC 로 표시했다 | 출력 워커가 `output_hello` 에 장치의 샘플레이트 목록(`sampleRates`)을 싣고, Core 의 `RoomManager.UpsamplingApplies` 가 워커와 같은 기준으로 판정한다. 신호 경로 팝오버는 "2배 업샘플링 · 44.1 → 88.2 kHz"라고 쓰고, 올리지 않았으면 DSP 단계에 그렇다고 쓴다 |

확인할 것:

- [ ] 공개 라운지를 새로 열고(혼자 듣던 방을 공개로 바꾼 것 말고) 설정 › 오디오 엔진을 보면 곡 전환 그룹에 경고가 뜬다.
- [ ] Babyface(최대 192kHz)에서 헤드폰 EQ 를 켜고 96kHz 곡을 틀면 팝오버에 "96 → 192 kHz"가 뜨고, 192kHz 곡을 틀면 SRC 단계가 사라지고 DSP 단계에 "384 kHz 를 받지 않아 업샘플링은 하지 않습니다"가 뜬다. 로그의 `asio open:` 레이트와 맞는지 본다.
- [ ] `SignalPathTruthTests` 통과.

### 4.2 같은 종류의 어긋남 세 곳 더

위 작업을 하다가 본 것들이고, 이어서 고쳤다(커밋 `Stop claiming conversions, modes and latencies that are not real.`).

| 어긋나던 곳 | 고친 방식 |
| --- | --- |
| 장치의 최대 규격이 곡보다 낮으면 "샘플레이트 변환 · 출력 장치가 받을 수 있는 규격으로 맞춤"이라고 했다. 저장소에 리샘플러는 없다 | **리샘플러를 만들지 않고 표시를 사실에 맞췄다.** `SrcApplied` 는 Mono 가 실제로 하는 변환(DSP 프리셋의 2배 업샘플링)에만 켜진다. 그 장치의 출력 항목에는 "이 장치는 24/192 를 그대로 받지 못합니다. Mono 는 규격을 바꿔 주지 않습니다…"라는 안내가 붙고, 워커가 실제 오류를 보고하면 그 문구가 대신 나온다 |
| ASIO 출력에도 "WASAPI 배타 모드", 공유로 내려간 뒤에도 "배타" | 워커가 `output_hello` 에 드라이버 종류(`driver`)를 싣는다. 화면은 드라이버는 그 값으로, 공유로 내려갔는지는 워커가 매초 보고하는 장치 상태(3 = 공유)로 쓴다(`lib/model.ts` 의 `outputMode`) |
| "지연 5 ms"는 장치를 열기 전의 자리표시값이었다 | 렌더러의 지연은 장치를 열기 전까지 0(모름)이다. 워커가 연 뒤의 값을 `clock_report` 에 싣고 Core 가 받아 적는다. 화면은 값이 있을 때만 지연을 쓴다(`outputLatency`) |

첫째를 고치려면 "비트퍼펙트"가 언제 참인지도 정해야 했다. 변환했다는 표시를 빼기만 하면 받지 못하는 장치에 초록 "Bit-Perfect" 가 켜지기 때문이다. `RoomManager.OutputsPassAudioUntouched` 가 듣고 있는 모든 출력에 대해 넷을 본다. 하나라도 아니면 비트퍼펙트가 아니다.

- 장치가 곡의 규격을 그대로 받는다(샘플레이트는 장치가 보고한 목록으로 본다).
- 배타로 붙었다. 공유로 골랐거나, 배타가 거절되어 공유로 내려갔으면 아니다.
- 하드웨어 볼륨이 없는 장치(ASIO)에서 볼륨이 100% 다. 디지털로 줄이면 샘플이 바뀐다.
- (앞서부터) DSP 와 업샘플링이 꺼져 있다.

**그래서 달라지는 것**: ASIO 에서 볼륨을 100% 아래로 두면 플레이어 바의 칩이 초록 "BIT-PERFECT" 에서 "재생 중"으로 바뀐다. 예전 표시가 틀렸던 것이다.

확인할 것:

- [ ] Babyface 에서 볼륨 100% · DSP 끔: 칩이 `BIT-PERFECT`. 볼륨을 80% 로 내리면 `재생 중`, 다시 100% 로 올리면 돌아온다.
- [ ] 신호 경로 팝오버의 출력 단계가 "ASIO · 지연 N ms"로 나온다. N 은 로그의 `asio open: … latency=…(N ms)` 와 같다. 재생 전에는 지연이 아예 나오지 않는다.
- [ ] WASAPI 배타를 거절하는 장치에서: 워커가 공유로 내려가면 "WASAPI 공유 모드"로 바뀌고 칩이 `BIT-PERFECT` 에서 내려간다.
- [ ] 44.1/48kHz 만 받는 장치에 96kHz 곡: SRC 단계가 뜨지 않고, 출력 항목에 받지 못한다는 안내나 워커의 오류가 뜬다.
- [ ] `SignalPathTruthTests`, 이름을 바꾼 `RoomAndClockTests.LowestCommonFormatKeepsTheOutputButClaimsNoConversion` 통과.

남은 결정 하나: 장치가 받지 못하는 규격을 Mono 가 직접 내려 주는 리샘플링을 넣을지. 넣는다면 `LocalPlayer.Finish` 가 자리다. 지금은 넣지 않았고, 화면은 넣지 않았다는 사실을 말한다. `QualityPolicyEngine.CommonFormat` 은 그때를 위해 남아 있다(지금은 테스트만 부른다).

그 밖에 하지 않은 것:

- 팬아웃 경로의 갭리스 · 크로스페이드.
- 손실 포맷의 인코더 지연 · 패딩 보정.
- 크로스페이드 · EQ 합이 풀스케일을 넘을 때의 헤드룸 보호(지금은 하드 클립).
- ReplayGain, 노이즈 셰이핑 디더, 로그 볼륨 곡선, DSD.
- 버전 번호. `CHANGELOG.md` 에는 이번 변경을 `## 다음 버전` 이라는 임시 제목으로 적어 두었다. `publish.ps1` 은 `## <버전>` 절을 찾으므로, 릴리스할 때 이 제목을 실제 버전으로 바꿔야 한다.

## 5. 회사 PC 의 사정

- .NET SDK 가 없다. `dotnet-install.ps1 -Channel 10.0` 으로 임시 폴더에 받아 빌드했다.
- 새로 빌드한 DLL 은 애플리케이션 제어 정책에 막힌다. 파일마다, 빌드마다 다르게 걸려서 오전에는 `Mono.Output` 만 쓰는 테스트가 돌았고 오후에는 테스트 DLL 자체가 막혔다. 우회하지 않는다.
- `src/Mono.Web` 은 `npm ci` 뒤 타입 검사 · 빌드 · 모의 Core 검증이 된다.
- 저장소 폴더에 `.court/`(재판 기록)와 `redesign/`(예전 캡처)이 추적되지 않은 채 남아 있다. 저장소에 넣지 않았다.
- Windows의 "애니메이션 효과"가 꺼져 있다. 브라우저가 "움직임 줄이기"로 인식해서, 랜딩 페이지의 모션은 줄인 버전(페이드와 색 전환만)으로 보인다. 전체 모션은 그 설정을 켜거나 Chrome 개발자 도구 Rendering 탭에서 `prefers-reduced-motion` 을 `no-preference` 로 바꿔야 보인다.
- 도구에서 실행하는 `git fetch` · `git push` 는 github.com 에 붙지 못한다. 푸시는 GitHub Desktop으로 한다.

## 6. 랜딩 사이트 (`landing/`)

같은 날 오후에 Mono를 소개하고 설치 파일로 안내하는 한 장짜리 사이트를 만들었다. 재생 엔진과는 따로 도는 정적 페이지다.

### 지금 상태

| 무엇 | 어디까지 |
| --- | --- |
| 첫 버전 (`acd523e`), 예전 `site/` 삭제 (`54448d9`) | PR #19로 `development` 에 들어갔다 |
| Hero 2단 · 모션 · 문구 교정 (`353dd85`) | `feature/landing-site` 에 푸시됨. **아직 `development` 에 없다** |
| 배포 워크플로, 이 문서 | `feature/landing-site` 의 로컬 커밋. 푸시와 PR이 남았다 |
| 공개 주소 | 아직 없다. 아래 "배포" 순서를 마치면 `https://gabrieljung0727.github.io/music-program/` |

### 구성

- 빌드 단계가 없다. `landing/index.html` 하나에 문구가 전부 있고, `assets/css/site.css`, `assets/js/site.js`(의존성 없음), 글꼴, `public/screenshots/` 가 붙는다. 실행법과 파일별 설명은 `landing/README.md`.
- 색 · 글꼴 · 모서리는 앱의 `tokens.css` 값 그대로다. 앱에 없는 값은 `site.css` 에 "파생"이라고 적었다. 섹션 표면 `#101014` 와 모션 값은 사용자가 이 페이지용으로 정해 준 것이다.
- 앱 화면은 전부 현재 UI를 모의 Core로 다시 찍은 캡처다. 다시 찍는 법과 장면 이름은 `landing/README.md` 의 표에 있다.
- 모션은 CSS와 `IntersectionObserver` 뿐이다. 값은 `landing/README.md` 의 "모션" 표에 있다.

### 페이지가 말하지 않는 것

만들 때 받은 스펙에는 사실과 다른 내용이 있었고, 페이지에는 넣지 않았다. 고칠 때도 지켜야 한다.

- macOS용은 없다. 메뉴와 설치 탭에 "아직 없음"으로 적었다. CoreAudio도 적지 않았다.
- DSD는 재생하지 않는다. 지원 형식 표에 "라이브러리에 표시, 재생은 아직 지원하지 않음"이라고 적었다. 다만 캡처 속 예시 데이터에 "DSD Only Club" 방과 `DSD128` 배지가 보인다. 어긋나 보이면 모의 데이터에서 빼고 다시 찍는다.
- 설치 파일은 `Mono-win-Setup.exe` 다. 링크는 `releases/latest/download/` 라서 새 릴리스를 내도 바꿀 것이 없다. `index.html` 의 `v0.6.0` 두 군데만 고친다.

### 확인한 것

- 헤드리스 Chrome으로 1440 · 820 · 390px, 그리고 "움직임 줄이기" 상태에서 탭과 토글을 하나씩 켜 가며 쟀다. 가로 스크롤, 깨진 이미지, 실패한 요청, 콘솔 오류가 없다.
- 1440×900에서 첫 화면이 좌 45% / 우 55%이고 앱 화면이 화면 높이 안에 다 들어온다.
- 모션은 흉내 낸 설정에서 숫자로 쟀다: 등장 도중의 위치와 배율, 카드의 순차 투명도, DSP 전환 150ms 시점의 중간값, 버튼 호버 배율.
- 저장소는 공개이고, 설치 파일 링크는 열린다(2026-11-17, 200 응답, 약 250MB).
- **하지 않은 것**: 실제 브라우저에서 손으로 눌러 보기, 전체 모션을 눈으로 보기, Safari와 Firefox, 실제 휴대폰.

### 배포

GitHub Pages로 올린다. `.github/workflows/landing.yml` 이 `development` 에 `landing/` 이 바뀐 커밋이 들어올 때 폴더를 그대로 올린다. 이 PC에서는 푸시와 저장소 설정을 할 수 없어서 아래는 사람이 한다.

- [ ] `feature/landing-site` 를 푸시하고 `development` 로 PR을 열어 합친다.
- [ ] 저장소 Settings → Pages → Build and deployment → Source 를 **GitHub Actions** 로 바꾼다(처음 한 번). 합치기 전에 해 두면 첫 실행이 바로 성공한다. 순서가 바뀌었으면 Actions → landing → Run workflow 로 다시 돌린다.
- [ ] Actions의 `landing` 실행이 초록인지 보고, 주소를 열어 첫 화면 · 다운로드 버튼 · 이미지가 뜨는지 확인한다.
- [ ] 워크플로의 액션 버전(`checkout@v7`, `configure-pages@v6`, `upload-pages-artifact@v5`, `deploy-pages@v5`)은 2026-11-17의 최신 주 버전을 적은 것이고 돌려 보지는 못했다. 첫 실행에서 버전 문제로 실패하면 여기부터 본다.

### 남은 것

- 푸터의 개인정보처리방침 · 이용약관 링크가 아직 `#` 다.
- 자체 도메인을 붙이면 `index.html` 의 `og:url` · `og:image` 를 바꾼다.
- 랜딩의 문구는 영문 뒤 조사를 붙여 쓴다(`Mono는`, `DAC까지`). 앱 UI와 다른 문서는 아직 띄어 쓴다(`Mono 가`). 앱 쪽도 맞출지는 정하지 않았다.
