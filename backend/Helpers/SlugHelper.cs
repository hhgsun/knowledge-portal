namespace KnowledgePortal.Api.Helpers;

public static class SlugHelper
{
    public static string Transliterate(string text)
    {
        ReadOnlySpan<(char From, char To)> map =
        [
            ('ş', 's'), ('ç', 'c'), ('ğ', 'g'), ('ü', 'u'), ('ö', 'o'), ('ı', 'i'),
            ('Ş', 's'), ('Ç', 'c'), ('Ğ', 'g'), ('Ü', 'u'), ('Ö', 'o'), ('İ', 'i'),
        ];

        var result = text.ToCharArray();
        for (var i = 0; i < result.Length; i++)
        {
            foreach (var (from, to) in map)
            {
                if (result[i] == from) { result[i] = to; break; }
            }
        }
        return new string(result);
    }

    /// <summary>
    /// Escapes LIKE wildcard characters (% and _) for safe use in EF.Functions.Like queries.
    /// </summary>
    public static string EscapeLikePattern(string input)
        => input.Replace("%", "\\%").Replace("_", "\\_");

    /// <summary>
    /// Wilson Score lower bound (95% confidence interval).
    /// </summary>
    public static double WilsonScore(int positive, int negative)
    {
        var n = positive + negative;
        if (n == 0) return 0;

        const double z = 1.96;
        var phat = (double)positive / n;
        var denominator = 1 + z * z / n;
        var centre = phat + z * z / (2 * n);
        var spread = z * Math.Sqrt((phat * (1 - phat) + z * z / (4 * n)) / n);

        return Math.Round((centre - spread) / denominator, 4);
    }
}
