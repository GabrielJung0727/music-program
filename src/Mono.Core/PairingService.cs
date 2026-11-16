using System.Collections.Concurrent;
using System.Security.Cryptography;
using Mono.Shared;

namespace Mono.Core;

/// <summary>
/// 원격 Control 페어링. 루프백이 아닌 접속은 코드를 교환해 받은 세션 토큰이 있어야 명령을 낼 수 있다.
/// </summary>
public sealed class PairingService
{
    /// <summary>이만큼 틀리면 잠근다. 6자리 코드를 무작정 대입하는 걸 막는다.</summary>
    private const int MaxFailures = 8;
    private static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan Lockout = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, PairingToken> _codes = new();
    private readonly ConcurrentDictionary<string, string> _sessions = new();
    private readonly object _gate = new();
    private int _failures;
    private DateTimeOffset _windowStart;
    private DateTimeOffset _lockedUntil;

    public PairingToken Issue(string peerId)
    {
        // 만료된 코드는 여기서 치운다. 따로 치우는 곳이 없어서 발급할 때마다 쌓이기만 했다.
        var now = DateTimeOffset.UtcNow;
        foreach (var (code, old) in _codes)
        {
            if (old.ExpiresAt < now) _codes.TryRemove(code, out _);
        }

        var token = new PairingToken
        {
            Code = RandomNumberGenerator.GetInt32(100000, 999999).ToString(),
            PeerId = peerId,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10)
        };
        _codes[token.Code] = token;
        return token;
    }

    /// <summary>코드를 세션 토큰으로 바꾼다. 코드는 1회용.</summary>
    /// <remarks>
    /// redeem 은 hello 보다 먼저, 원격 어디서든 보낼 수 있다. 횟수 제한이 없으면 코드가 나와 있는
    /// 10분 동안 90만 가지를 전부 대입할 수 있다. 연달아 틀리면 잠시 잠그고, 나와 있던 코드도 버린다.
    /// </remarks>
    public (string? SessionToken, string? IssuedBy) Redeem(string code)
    {
        var now = DateTimeOffset.UtcNow;
        lock (_gate)
        {
            if (now < _lockedUntil) return (null, null);
        }

        if (string.IsNullOrWhiteSpace(code) || !_codes.TryRemove(code, out var token) || token.ExpiresAt < now)
        {
            lock (_gate)
            {
                if (now - _windowStart > FailureWindow)
                {
                    _windowStart = now;
                    _failures = 0;
                }

                if (++_failures >= MaxFailures)
                {
                    _failures = 0;
                    _lockedUntil = now + Lockout;
                    _codes.Clear();
                }
            }

            return (null, null);
        }

        var session = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        _sessions[session] = token.PeerId;
        return (session, token.PeerId);
    }

    public bool IsAuthorized(string? sessionToken)
        => !string.IsNullOrWhiteSpace(sessionToken) && _sessions.ContainsKey(sessionToken);

    public void Revoke(string sessionToken) => _sessions.TryRemove(sessionToken, out _);

    public int PendingCodes => _codes.Count;
}
