using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.OneFuzz.Service.Auth;
namespace Microsoft.OneFuzz.Service.Functions;

public class ValidateScriban {
    private readonly ILogger _log;
    private readonly IOnefuzzContext _context;
    public ValidateScriban(ILogger<ValidateScriban> log, IOnefuzzContext context) {
        _log = log;
        _context = context;
    }

    [Function("ValidateScriban")]
    [Authorize(Allow.User)]
    public async Async.Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "POST")]
        HttpRequestData req) {
        var request = await RequestHandling.ParseRequest<TemplateValidationPost>(req);
        if (!request.IsOk) {
            return await _context.RequestHandling.NotOk(req, request.ErrorV, "ValidateTemplate");
        }

        try {
            return await RequestHandling.Ok(req, await JinjaTemplateAdapter.ValidateScribanTemplate(_context, _log, request.OkV.Context, request.OkV.Template));

        } catch (Exception e) {
            return await new RequestHandling(_log).NotOk(
                req,
                RequestHandling.ConvertError(e),
                $"Template failed to render due to: `{e.Message}`"
            );
        }
    }
}
