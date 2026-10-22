using AttendanceApi.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
namespace AttendanceApi.Services;

public class ErrorResponseFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (context.MethodInfo.GetCustomAttributes(true).OfType<AuthorizeAttribute>().Any() ||
            context.MethodInfo.DeclaringType!.GetCustomAttributes(true).OfType<AuthorizeAttribute>().Any())
        {
            operation.Security = new List<OpenApiSecurityRequirement>
            {
                new() { { new OpenApiSecurityScheme { Reference = new() { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }, Array.Empty<string>() } }
            };
        }
        foreach (var code in new[] { 400, 401, 403, 404, 409, 429, 500, 503 }) operation.Responses[code.ToString()] = new() { Description = code switch { 400 => "Validation / business rule", 401 => "Missing/expired token or wrong credentials", 403 => "Forbidden / email unverified", 404 => "Not found", 409 => "Conflict / record in use", 429 => "Rate limit / OTP cooldown", 503 => "SMTP unavailable (data may contain ChallengeView)", _ => "Server error" }, Content = new Dictionary<string, OpenApiMediaType> { { "application/json", new() { Schema = context.SchemaGenerator.GenerateSchema(typeof(ApiResponse), context.SchemaRepository) } } } };
    }
}
