namespace Net7ClientManager.SkillPlanning;

using System.Text.Json;
using System.Text.Json.Serialization;

internal static class SkillBuildDocumentCodec
{
    private static readonly JsonSerializerOptions options = CreateOptions();

    public static string SerializeBuild(
        SkillBuildDocument build)
    {
        ArgumentNullException.ThrowIfNull(build);
        return JsonSerializer.Serialize(build, options);
    }

    public static bool TryDeserializeBuild(
        string json,
        out SkillBuildDocument build,
        out string error)
    {
        if (!TryDeserialize(json, out build, out error))
        {
            return false;
        }

        if (build.SchemaVersion != SkillBuildDocument.CurrentSchemaVersion)
        {
            error = $"Unsupported build schema {build.SchemaVersion}.";
            build = default!;
            return false;
        }

        return true;
    }

    private static bool TryDeserialize<T>(
        string json,
        out T document,
        out string error)
        where T : class
    {
        document = default!;

        if (string.IsNullOrWhiteSpace(json))
        {
            error = "The skill-build document is empty.";
            return false;
        }

        try
        {
            document = JsonSerializer.Deserialize<T>(json, options) ??
                throw new JsonException(
                    "The skill-build document contains no object.");

            error = "";
            return true;
        }
        catch (JsonException exception)
        {
            error = exception.Message;
            return false;
        }
        catch (NotSupportedException exception)
        {
            error = exception.Message;
            return false;
        }
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var result = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            WriteIndented = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };

        result.Converters.Add(
            new JsonStringEnumConverter(
                JsonNamingPolicy.CamelCase));

        return result;
    }
}
