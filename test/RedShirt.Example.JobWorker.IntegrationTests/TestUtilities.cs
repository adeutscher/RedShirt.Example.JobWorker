namespace RedShirt.Example.JobWorker.IntegrationTests;

public static class TestUtilities
{
    public static void WrapEnvironment(Dictionary<string, string> variables, Action callback)
    {
        var oldVariables = new Dictionary<string, string>();

        try
        {
            foreach (var kvp in variables)
            {
                if (Environment.GetEnvironmentVariable(kvp.Key) is { } envVar)
                {
                    oldVariables[kvp.Key] = envVar;
                }
                else
                {
                    oldVariables[kvp.Key] = string.Empty;
                }

                Environment.SetEnvironmentVariable(kvp.Key, kvp.Value);
            }

            callback();
        }
        finally
        {
            foreach (var kvp in oldVariables)
            {
                Environment.SetEnvironmentVariable(kvp.Key, kvp.Value);
            }
        }
    }
}