using System.Text.Json;
using Microsoft.Azure.Cosmos;

namespace Veda.Storage;

/// <summary>
/// 使用 System.Text.Json 的 CosmosDB 序列化器，确保 [JsonPropertyName] 属性生效。
/// CosmosDB SDK 默认使用 Newtonsoft.Json，会忽略 STJ 的属性注解导致字段名不匹配。
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
