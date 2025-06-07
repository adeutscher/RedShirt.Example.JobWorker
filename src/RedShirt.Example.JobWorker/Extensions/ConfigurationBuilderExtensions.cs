using Microsoft.Extensions.Configuration;
using System.Collections;
using System.Text.RegularExpressions;

namespace RedShirt.Example.JobWorker.Extensions;

public static class ConfigurationBuilderExtensions
{
    private static Dictionary<string, string> ParseParts(Dictionary<string, string> rawEnvironmentVariables)
    {
        var result = new Dictionary<string, string>();

        foreach (var kvp in rawEnvironmentVariables)
        {
            var keySet = new HashSet<string>();

            result.Add(kvp.Key, kvp.Value);
            keySet.Add(kvp.Key);

            var simplified = kvp.Key.Replace("_", "");
            if (keySet.Add(simplified))
            {
                result.Add(simplified, kvp.Value);
            }

            var parsed = Regex.Replace(kvp.Key, @"__+", ":");
            // ReSharper disable once InvertIf
            if (keySet.Add(parsed))
            {
                result.Add(parsed, kvp.Value);

                var simplified2 = parsed.Replace("_", "");
                if (keySet.Add(simplified2))
                {
                    result.Add(simplified2, kvp.Value);
                }
            }
        }

        return result;
    }

    private static Dictionary<string, string> GetRawEnvironmentVariables()
    {
        var variables = Environment.GetEnvironmentVariables();
        var result = new Dictionary<string, string>();

        foreach (DictionaryEntry variable in variables)
        {
            result[(string) variable.Key] = (string) variable.Value!;
        }

        return result;
    }

    public static IConfigurationBuilder AddEnvironmentVariablesWithSegmentSupport(this IConfigurationBuilder builder)
    {
        var rawEnvironmentVariables = GetRawEnvironmentVariables();
        var processedEnvironmentVariables = ParseParts(rawEnvironmentVariables);
        return builder.AddInMemoryCollection(processedEnvironmentVariables!);
    }
}