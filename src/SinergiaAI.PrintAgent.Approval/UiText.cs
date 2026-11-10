using System.Globalization;
namespace SinergiaAI.PrintAgent.Approval;

/// <summary>Desktop UI follows the current Windows user's display language.</summary>
internal static class UiText
{
    internal static bool IsEnglish => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("en",StringComparison.OrdinalIgnoreCase);
    internal static string Get(string english,string portuguese) => IsEnglish ? english : portuguese;
}
