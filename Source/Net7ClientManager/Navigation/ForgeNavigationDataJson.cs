namespace Net7ClientManager.Navigation;

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

internal static class ForgeNavigationDataJson
{
    public static JsonSerializerOptions ReadOptions { get; } = CreateReadOptions();

    public static JsonSerializerOptions WriteOptions { get; } = CreateWriteOptions();

    public static JsonSerializerOptions DistributionReadOptions { get; } =
        CreateDistributionReadOptions();

    public static byte[] SerializeCanonical<T>(T value)
    {
        var json = JsonSerializer.Serialize(value, WriteOptions);
        return Encoding.UTF8.GetBytes(string.Concat(json, "\n"));
    }

    private static JsonSerializerOptions CreateReadOptions()
    {
        return new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };
    }

    private static JsonSerializerOptions CreateDistributionReadOptions()
    {
        return new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
        };
    }

    private static JsonSerializerOptions CreateWriteOptions()
    {
        return new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            WriteIndented = false,
        };
    }
}
