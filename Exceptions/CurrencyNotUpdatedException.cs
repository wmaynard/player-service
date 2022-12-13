using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Exceptions;

namespace PlayerService.Exceptions;

public class CurrencyNotUpdatedException : PlatformException
{
    public string AccountId { get; init; }
    public string CurrencyName { get; init; }
    
    public CurrencyNotUpdatedException(string accountId, string currencyName) : base("No records found.  This could be a concurrency collision with the component version.", code: ErrorCode.MongoRecordNotFound)
    {
        AccountId = accountId;
        CurrencyName = currencyName;
    }
}