using System.Collections.Concurrent;
using System.Security.Cryptography;
using Mono.Shared;

namespace Mono.Core;

/// <summary>
/// 원격 Control 페어링. 루프백이 아닌 접속은 코드를 교환해 받은 세션 토큰이 있어야 명령을 낼 수 있다.
/// </summary>
public sealed class PairingService
{
    private readonly ConcurrentDictionary<string, PairingToken> _codes = new();
    private readonly ConcurrentDictionary<string, string> _sessions = new();

    public PairingToken Issue(string peerId)
    {
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
    public (string? SessionToken, string? IssuedBy) Redeem(string code)
    {
        if (!_codes.TryRemove(code, out var token) || token.ExpiresAt < DateTimeOffset.UtcNow)
        {
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
