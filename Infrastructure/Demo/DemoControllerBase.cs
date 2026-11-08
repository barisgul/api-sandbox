using Microsoft.AspNetCore.Mvc;

namespace AuthSandbox.Infrastructure.Demo;

/// <summary>
/// Base class for the domain demo APIs. Provides RFC 7807 problem helpers with a stable
/// machine-readable <c>code</c> so Quixa flows can assert on negative scenarios.
/// </summary>
[ApiController]
[Produces("application/json")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
public abstract class DemoControllerBase : ControllerBase
{
    protected ObjectResult Problem(int status, string code, string detail, object? extra = null)
    {
        var problem = new ProblemDetails
        {
            Status = status,
            Title = code switch
            {
                _ when status == 404 => "Resource not found",
                _ when status == 409 => "Conflict with current resource state",
                _ when status == 422 => "Business rule violation",
                _ when status == 403 => "Forbidden",
                _ => "Request failed",
            },
            Detail = detail,
            Type = $"https://quixa.io/problems/{code.ToLowerInvariant().Replace('_', '-')}",
            Instance = HttpContext?.Request.Path,
        };
        problem.Extensions["code"] = code;
        if (extra is not null) problem.Extensions["context"] = extra;
        return new ObjectResult(problem) { StatusCode = status, ContentTypes = { "application/problem+json" } };
    }

    protected ObjectResult NotFoundProblem(string resource, string id) =>
        Problem(404, $"{System.Text.RegularExpressions.Regex.Replace(resource, "(?<=[a-z])(?=[A-Z])", "_").ToUpperInvariant()}_NOT_FOUND",
            $"{resource} '{id}' does not exist.");

    protected ObjectResult ConflictProblem(string code, string detail, object? extra = null) => Problem(409, code, detail, extra);

    protected ObjectResult RuleProblem(string code, string detail, object? extra = null) => Problem(422, code, detail, extra);

    /// <summary>Validates an uploaded PDF/PNG/JPEG; returns 400 for empty files and 422 for type/size problems.</summary>
    protected async Task<(UploadedFileInfo? File, ActionResult? Error)> ReadUploadAsync(IFormFile file, string field)
    {
        var r = await Uploads.ReadAsync(file);
        if (r.File is not null) return (r.File, null);
        return (null, r.IsFormatError ? FieldProblem(field, r.ErrorDetail!) : RuleProblem(r.ErrorCode!, r.ErrorDetail!));
    }

    /// <summary>400 with a field-level errors dictionary (same shape as automatic model validation).</summary>
    protected ActionResult FieldProblem(string field, string message)
    {
        ModelState.AddModelError(field, message);
        return ValidationProblem(ModelState);
    }
}
