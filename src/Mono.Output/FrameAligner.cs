namespace Mono.Output;

/// <summary>
/// 장치 큐에는 온전한 프레임만 넣는다.
///
/// 반 프레임이 그대로 들어가면 그 뒤의 모든 샘플이 몇 바이트씩 밀려서 좌우 채널이
/// 뒤바뀌고, 한 번 밀리면 스트림이 끝날 때까지 복구되지 않는다 — 음악이 깔린 채로
/// 지지직거리는 잡음이 얹히는 전형적인 증상이 이것이다.
/// 남은 조각은 버리지 않고 다음 청크 앞에 붙여 이어 준다.
/// </summary>
public sealed class FrameAligner
{
    private byte[] _carry = [];

    /// <summary>아직 내보내지 못하고 들고 있는 바이트 수.</summary>
    public int Pending => _carry.Length;

    /// <summary>
    /// 이번에 장치로 넘길 온전한 프레임 구간을 돌려준다. 남는 꼬리는 다음 호출로 넘긴다.
    /// </summary>
    public byte[] Take(byte[] payload, int blockAlign)
    {
        var align = Math.Max(1, blockAlign);

        if (_carry.Length > 0)
        {
            var joined = new byte[_carry.Length + payload.Length];
            _carry.CopyTo(joined, 0);
            payload.CopyTo(joined, _carry.Length);
            payload = joined;
            _carry = [];
        }

        var whole = payload.Length / align * align;
        if (whole == payload.Length) return payload;

        _carry = payload[whole..];
        return payload[..whole];
    }

    /// <summary>포맷이 바뀌거나 버퍼를 비울 때. 이전 포맷의 꼬리를 다음 포맷에 붙이면 안 된다.</summary>
    public void Reset() => _carry = [];
}
