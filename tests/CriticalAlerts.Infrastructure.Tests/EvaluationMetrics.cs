namespace CriticalAlerts.Infrastructure.Tests;

internal static class EvaluationMetrics
{
    public static int WordErrors(string reference, string actual)
    {
        var expected = Words(reference); var observed = Words(actual);
        var previous = Enumerable.Range(0, observed.Length + 1).ToArray();
        for (var i = 1; i <= expected.Length; i++)
        {
            var current = new int[observed.Length + 1]; current[0] = i;
            for (var j = 1; j <= observed.Length; j++) current[j] = Math.Min(previous[j] + 1,
                Math.Min(current[j - 1] + 1, previous[j - 1] + (expected[i - 1] == observed[j - 1] ? 0 : 1)));
            previous = current;
        }
        return previous[observed.Length];
    }
    public static string[] Words(string value) => value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
    public static int ExactMatches(IEnumerable<string> expected, IEnumerable<string> actual)
    {
        var remaining = actual.ToList(); var count = 0;
        foreach (var item in expected) { var index = remaining.IndexOf(item); if (index >= 0) { count++; remaining.RemoveAt(index); } }
        return count;
    }
    public static decimal? Rate(int count, int denominator) => denominator == 0 ? null : (decimal)count / denominator;
}
