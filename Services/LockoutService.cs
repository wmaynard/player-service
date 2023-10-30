using System;
using System.Linq;
using PlayerService.Exceptions.Login;
using PlayerService.Models.Login;
using Rumble.Platform.Common.Minq;
using Rumble.Platform.Common.Models;
using Rumble.Platform.Common.Services;
using Rumble.Platform.Common.Utilities;

namespace PlayerService.Services;

public class LockoutService : MinqService<IpAccessLog>
{
    public static int Threshold => DynamicConfig.Instance?.Optional<int>("ipLockoutThreshold") ?? 5;
    public static int Cooldown => Math.Max(1, DynamicConfig.Instance?.Optional<int>("ipLockoutMinutes") ?? 5);
    public static int AttemptsToKeep => Math.Min(100, Threshold * 5);

    public LockoutService() : base("lockouts") { }

    /// <summary>
    /// Guarantees that the provided email / IP address are okay to continue with login.  Will throw an exception if not.
    /// </summary>
    /// <param name="email"></param>
    /// <param name="ip"></param>
    /// <returns></returns>
    /// <exception cref="LockoutException"></exception>
    public bool EnsureNotLockedOut(string email, string ip)
    {
        // This should be quite rare, but we can't enforce account locks without the IpAddress.
        // If the threshold is not a positive number, the lockout is disabled.
        if (string.IsNullOrWhiteSpace(ip) || Threshold <= 0)
            return true;

        long[] attempts = mongo
            .Where(query => query
                .EqualTo(log => log.Email, email)
                .EqualTo(log => log.IpAddress, ip)
            )
            .Project(log => log.Timestamps)
            ?.FirstOrDefault()
            ?.Where(val => val > Timestamp.Now - Cooldown * 60)
            .ToArray()
            ?? Array.Empty<long>();

        if (attempts.Length >= Threshold)
            throw new LockoutException(email, ip, waitTime: attempts.OrderByDescending(_ => _).Take(Threshold).Last());

        return true;
    }

    public IpAccessLog RegisterError(string email, string ip) => mongo
        .Where(query => query
            .EqualTo(log => log.Email, email)
            .EqualTo(log => log.IpAddress, ip)
        )
        .Upsert(query => query.AddItems(log => log.Timestamps, limitToKeep: AttemptsToKeep, Timestamp.Now));

    public override long ProcessGdprRequest(TokenInfo token, string dummyText) => mongo
        .Where(query => query.EqualTo(log => log.Email, token.Email))
        .Update(query => query
            .Set(log => log.Email, dummyText)
            .Set(log => log.IpAddress, "0.0.0.0")
        );
}