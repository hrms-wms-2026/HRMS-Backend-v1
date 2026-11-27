using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Application.Common.Json;

namespace ONEVO.Api.Extensions;

public static class ApiJsonExtensions
{
    /// <summary>
    /// JSON settings for every controller response. Task 10 (DEC-070): null properties are left
    /// out, which shrinks large lists; a client reads a missing field as null (C#) or undefined
    /// (TypeScript).
    /// </summary>
    public static JsonOptions ConfigureApiJson(this JsonOptions options)
    {
        options.JsonSerializerOptions.Converters.Add(new TimeOnlyHhMmJsonConverter());
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        return options;
    }
}
