using System.Globalization;

namespace Lobby.Web.Services;

// Small formatting helpers shared by views. Times are stored in UTC and shown in São Paulo time.
public static class Display
{
    public static readonly TimeZoneInfo BrazilTime = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
    public static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static DateTimeOffset ToBrazil(DateTimeOffset utc) => TimeZoneInfo.ConvertTime(utc, BrazilTime);

    public static string Initials(string displayName)
    {
        // Only words that start with a letter or digit: "Clube do livro (demo)" -> "CL", not "C(".
        var parts = displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(word => char.IsLetterOrDigit(word[0])).ToArray();
        return parts.Length switch
        {
            0 => "?",
            1 => parts[0][..1].ToUpperInvariant(),
            _ => (parts[0][..1] + parts[^1][..1]).ToUpperInvariant(),
        };
    }

    // A stable colour per user. string.GetHashCode() is randomised per process,
    // so it would change colours on every restart; this simple sum does not.
    public static int Hue(string userId)
    {
        var sum = 0;
        foreach (var c in userId) sum = (sum * 31 + c) % 360;
        return sum;
    }

    public static string AvatarUrl(string fileName) => $"/avatars/{fileName}";

    public static string DayKey(DateTimeOffset utc) => ToBrazil(utc).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string DayLabel(DateTimeOffset utc, DateTimeOffset nowUtc)
    {
        var day = ToBrazil(utc).Date;
        var today = ToBrazil(nowUtc).Date;
        if (day == today) return "Hoje";
        if (day == today.AddDays(-1)) return "Ontem";
        return day.ToString("d 'de' MMMM 'de' yyyy", PtBr);
    }

    // Sidebar timestamp: "14:30" today, "ontem", otherwise "03/11".
    public static string ShortTime(DateTimeOffset utc, DateTimeOffset nowUtc)
    {
        var day = ToBrazil(utc).Date;
        var today = ToBrazil(nowUtc).Date;
        if (day == today) return Time(utc);
        if (day == today.AddDays(-1)) return "ontem";
        return ToBrazil(utc).ToString("dd/MM", PtBr);
    }

    public static string Time(DateTimeOffset utc) => ToBrazil(utc).ToString("HH:mm", PtBr);

    public static string FullDateTime(DateTimeOffset utc) => ToBrazil(utc).ToString("dddd, d 'de' MMMM 'de' yyyy 'às' HH:mm", PtBr);

    public static string LastSeen(DateTimeOffset? lastSeenUtc, DateTimeOffset nowUtc)
    {
        if (lastSeenUtc is null) return "offline";
        var ago = nowUtc - lastSeenUtc.Value;
        if (ago < TimeSpan.FromMinutes(1)) return "visto agora há pouco";
        if (ago < TimeSpan.FromHours(1)) return $"visto há {(int)ago.TotalMinutes} min";
        if (ago < TimeSpan.FromDays(1) && ToBrazil(lastSeenUtc.Value).Date == ToBrazil(nowUtc).Date)
            return $"visto hoje às {Time(lastSeenUtc.Value)}";
        return $"visto em {ToBrazil(lastSeenUtc.Value).ToString("d 'de' MMM", PtBr)}";
    }
}
