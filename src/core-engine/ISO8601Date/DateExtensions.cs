using System.Text.RegularExpressions;

namespace core_engine.ISO8601Date;

public static class DateExtensions
{
    public static TimeSpan ParseIso8601Duration(string duration)
    {
        Regex regex = new Regex(@"P(?:(\d+)Y)?(?:(\d+)M)?(?:(\d+)W)?(?:(\d+)D)?(?:T(?:(\d+)H)?(?:(\d+)M)?(?:(\d+)S)?)?");
        Match match = regex.Match(duration);
        
        if (!match.Success)
        {
            throw new ArgumentException("Invalid ISO 8601 duration format.");
        }

        int years = match.Groups[1].Success ? int.Parse(match.Groups[1].Value) : 0;
        int months = match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : 0;
        int weeks = match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : 0;
        int days = match.Groups[4].Success ? int.Parse(match.Groups[4].Value) : 0;
        int hours = match.Groups[5].Success ? int.Parse(match.Groups[5].Value) : 0;
        int minutes = match.Groups[6].Success ? int.Parse(match.Groups[6].Value) : 0;
        int seconds = match.Groups[7].Success ? int.Parse(match.Groups[7].Value) : 0;

        // Konvertiere Jahre und Monate in Tage
        days += years * 365 + months * 30 + weeks * 7;

        return new TimeSpan(days, hours, minutes, seconds);
    }
}