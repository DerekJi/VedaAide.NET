using System.Text.Json;
using Microsoft.Azure.Cosmos;

namespace Veda.Storage;

/// <summary>
/// CosmosDB serializer using System.Text.Json so that [JsonPropertyName] attributes take effect.
/// The CosmosDB SDK uses Newtonsoft.Json by default, which ignores STJ property annotations and causes field-name mismatches.
/// </summary>
internal sealed class SystemTextJsonCosmosSerializer(JsonSerializerOptions options) : CosmosSerializer
{
    public override T FromStream<T>(Stream stream)
    {
        using var sr = new StreamReader(stream);
        return JsonSerializer.Deserialize<T>(sr.ReadToEnd(), options)!;
    }

    public override Stream ToStream<T>(T input)
    {
        var ms = new MemoryStream();
        JsonSerializer.Serialize(ms, input, options);
        ms.Position = 0;
        return ms;
    }
}
