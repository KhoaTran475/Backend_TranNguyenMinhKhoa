using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Any;
using Swashbuckle.AspNetCore.SwaggerGen;
namespace AttendanceApi.Services;

public class EnumSchemaFilter : ISchemaFilter { public void Apply(OpenApiSchema schema, SchemaFilterContext context) { if (!context.Type.IsEnum) return; schema.Type = "string"; schema.Format = null; schema.Enum = Enum.GetNames(context.Type).Select(x => (IOpenApiAny)new OpenApiString(x)).ToList(); } }
