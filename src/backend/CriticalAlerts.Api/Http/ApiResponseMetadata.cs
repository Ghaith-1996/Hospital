using Microsoft.OpenApi;

namespace CriticalAlerts.Api.Http;

internal static class ApiResponseMetadata
{
    public static RouteHandlerBuilder WithIdempotencyHeader(this RouteHandlerBuilder endpoint)
        => endpoint.AddOpenApiOperationTransformer((operation, _, _) =>
        {
            operation.Parameters ??= [];
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = "Idempotency-Key",
                In = ParameterLocation.Header,
                Required = true,
                Schema = new OpenApiSchema { Type = JsonSchemaType.String },
            });
            return Task.CompletedTask;
        });

    public static RouteHandlerBuilder WithDirectoryImportForm(this RouteHandlerBuilder endpoint, bool apply)
        => endpoint.AddOpenApiOperationTransformer((operation, _, _) =>
        {
            var schema = new OpenApiSchema
            {
                Type = JsonSchemaType.Object,
                Required = new HashSet<string> { "file" },
                Properties = new Dictionary<string, IOpenApiSchema>
                {
                    ["file"] = new OpenApiSchema { Type = JsonSchemaType.String, Format = "binary" },
                },
            };
            if (apply)
            {
                schema.Properties["preview_token"] = new OpenApiSchema { Type = JsonSchemaType.String };
                schema.Required.Add("preview_token");
            }

            operation.RequestBody = new OpenApiRequestBody
            {
                Required = true,
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["multipart/form-data"] = new OpenApiMediaType { Schema = schema },
                },
            };
            return Task.CompletedTask;
        });

    public static RouteHandlerBuilder WithApiErrors(this RouteHandlerBuilder endpoint, params int[] statuses)
    {
        endpoint.ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status429TooManyRequests);
        foreach (var status in statuses)
        {
            endpoint.ProducesProblem(status);
        }

        return endpoint;
    }
}
