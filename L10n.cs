using System.Globalization;
using System.Text.Json;
using System.Windows;

namespace Soundpad;

internal static class L10n
{
    internal static readonly Dictionary<string,Dictionary<string,string>> Entries=Load();
    static readonly Dictionary<string,string> Keys=Entries.ToDictionary(e=>e.Value["ru"],e=>e.Key,StringComparer.Ordinal);
    public static string Current {get;private set;}=Detect(CultureInfo.CurrentUICulture.Name);
    static Dictionary<string,Dictionary<string,string>> Load()
    {
        using var stream=typeof(L10n).Assembly.GetManifestResourceStream("OpenPad.Translations.json")!;
        return JsonSerializer.Deserialize<Dictionary<string,Dictionary<string,string>>>(stream)!;
    }
    public static string Detect(string? name)
    {
        string code=(name ?? "").Split('-','_')[0].ToLowerInvariant();
        return code is "uk" or "ua"?"uk":code=="ru"?"ru":"en";
    }
    public static void Set(string? language)
    {
        Current=language=="ua"?"uk":language is "uk" or "en" or "ru"?language:Detect(CultureInfo.CurrentUICulture.Name);
        if(Application.Current!=null)
            foreach(var entry in Entries)Application.Current.Resources[entry.Key]=entry.Value[Current];
    }
    public static string T(string source)=>Keys.TryGetValue(source,out var key)?Entries[key][Current]:source;
    public static string F(string source,params object[] args)=>string.Format(CultureInfo.GetCultureInfo(Current),T(source),args);
}
