namespace TransitTracker.Framework;

public static class AttributesExtensions
{
    public static string GetStringAttribute(this IDictionary<string, object?> dictionary, string key)
    {
        if (dictionary.TryGetValue(key, out var value) && value is string strValue)
        {
            return strValue;
        }
        return string.Empty;
    }

    public static int GetIntAttribute(this IDictionary<string, object?> dictionary, string key)
    {
        if (dictionary.TryGetValue(key, out var value) && value is int intValue)
        {
            return intValue;
        }
        return default;
    }
}
