using System;

namespace SaveTracker.Resources.HELPERS
{
    public static class StringUtils
    {
        /// <summary>
        /// Calculates the Levenshtein distance between two strings
        /// </summary>
        public static int ComputeLevenshteinDistance(string s, string t)
        {
            if (string.IsNullOrEmpty(s))
            {
                return string.IsNullOrEmpty(t) ? 0 : t.Length;
            }

            if (string.IsNullOrEmpty(t))
            {
                return s.Length;
            }

            int n = s.Length;
            int m = t.Length;
            int[,] d = new int[n + 1, m + 1];

            // Initialize
            for (int i = 0; i <= n; d[i, 0] = i++) { }
            for (int j = 0; j <= m; d[0, j] = j++) { }

            for (int i = 1; i <= n; i++)
            {
                for (int j = 1; j <= m; j++)
                {
                    int cost = (t[j - 1] == s[i - 1]) ? 0 : 1;
                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost);
                }
            }

            return d[n, m];
        }

        /// <summary>
        /// Calculates a similarity score between 0.0 and 1.0
        /// </summary>
        public static double CalculateSimilarity(string source, string target)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(target)) return 0.0;

            // Normalize
            string s = source.ToLowerInvariant().Replace(" ", "").Replace("'", "").Replace("-", "").Replace(":", "");
            string t = target.ToLowerInvariant().Replace(" ", "").Replace("'", "").Replace("-", "").Replace(":", "");

            if (s == t) return 1.0;
            if (s.Contains(t) || t.Contains(s)) return 0.8 + (0.2 * Math.Min(s.Length, t.Length) / Math.Max(s.Length, t.Length));

            int steps = ComputeLevenshteinDistance(s, t);
            int maxLength = Math.Max(s.Length, t.Length);

            return 1.0 - ((double)steps / maxLength);
        }
    }
}
