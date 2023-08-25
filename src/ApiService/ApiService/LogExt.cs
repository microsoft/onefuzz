using System.Net;
using Microsoft.Extensions.Logging;

namespace Microsoft.OneFuzz.Service;
public static class LogExt {
    /// <summary>
    /// 
    /// </summary>
    /// <param name="logger"></param>
    /// <param name="name"></param>
    /// <param name="value"></param>
    public static void LogOneFuzzError(this ILogger logger, Error err) {
        var errors = err.Errors ?? new List<string>();
        logger.LogError("Error: Code = {Code}, Errors = {errorsString}", err.Code, string.Join(';', errors));
    }


    public static void AddHttpStatus(this ILogger logger, (HttpStatusCode Status, string Reason) result) {
        logger.AddTag("StatusCode", ((int)result.Status).ToString());
        logger.AddTag("ReasonPhrase", result.Reason);
    }


}
