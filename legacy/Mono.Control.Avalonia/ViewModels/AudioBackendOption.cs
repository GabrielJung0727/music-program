namespace Mono.Control.ViewModels;

/// <summary>
/// 출력 드라이버 선택지.
///
/// 장치마다 되는 게 다르다 — 일반 USB DAC 은 WASAPI 배타로 요청한 레이트를 따라오지만,
/// RME 같은 전문 인터페이스는 하드웨어 클럭이 설정 패널에서 고정돼 다른 레이트의
/// 배타 요청을 거절한다. 그런 장치에서 비트퍼펙트를 얻으려면 ASIO 로 가야 한다.
/// </summary>
public sealed record AudioBackendOption(string Id, string Label, string Description);
