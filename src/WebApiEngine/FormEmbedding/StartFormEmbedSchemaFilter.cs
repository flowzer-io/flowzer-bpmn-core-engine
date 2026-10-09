using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;
using WebApiEngine.Shared;

namespace WebApiEngine.FormEmbedding;

/// <summary>
/// Lokal auf den neuen Startlink begrenzter Nullvertrag: Swashbuckle beschreibt nullable
/// Referenzproperties sonst nur als nackten $ref. Globale historische Schemas bleiben gleich.
/// </summary>
public sealed class StartFormEmbedSchemaFilter : ISchemaFilter
{
    public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
    {
        // Swashbuckle ruft Filter auch für nackte Member-$refs ohne Properties auf.
        // Nur der konkrete neue DTO-Schemaknoten erhält diese eine Anpassung.
        if (context.Type != typeof(StartFormEmbedLinkDto) || schema.Properties is not { } properties) return;
        var property = properties["formLink"];
        // In OpenAPI 3.0 benötigt nullable einen lokalen Schemaknoten neben dem $ref.
        properties["formLink"] = new OpenApiSchema
        {
            Type = JsonSchemaType.Object | JsonSchemaType.Null,
            AllOf = [property]
        };
    }
}
