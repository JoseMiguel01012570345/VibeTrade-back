using System.Globalization;
using VibeTrade.Backend.Features.Statistics.Dtos;

namespace VibeTrade.Backend.Features.Statistics.Utils;

public static class StatisticsMath
{
    public static double Percentile(IReadOnlyList<double> sortedAsc, double percentile)
    {
        if (sortedAsc.Count == 0)
            return 0;
        if (sortedAsc.Count == 1)
            return sortedAsc[0];
        var p = Math.Clamp(percentile, 0, 1);
        var index = p * (sortedAsc.Count - 1);
        var lower = (int)Math.Floor(index);
        var upper = (int)Math.Ceiling(index);
        if (lower == upper)
            return sortedAsc[lower];
        var weight = index - lower;
        return sortedAsc[lower] * (1 - weight) + sortedAsc[upper] * weight;
    }

    public static IReadOnlyList<StatisticsTripKmBucket> BuildHistogram(
        IReadOnlyList<double> values,
        int bucketCount = 8)
    {
        if (values.Count == 0)
            return Array.Empty<StatisticsTripKmBucket>();
        var min = values.Min();
        var max = values.Max();
        if (Math.Abs(max - min) < 0.0001)
        {
            return
            [
                new StatisticsTripKmBucket($"{min:0.##} km", min, max, values.Count),
            ];
        }

        var step = (max - min) / bucketCount;
        var buckets = new int[bucketCount];
        foreach (var v in values)
        {
            var idx = (int)Math.Floor((v - min) / step);
            if (idx >= bucketCount)
                idx = bucketCount - 1;
            if (idx < 0)
                idx = 0;
            buckets[idx]++;
        }

        var result = new List<StatisticsTripKmBucket>(bucketCount);
        for (var i = 0; i < bucketCount; i++)
        {
            var bMin = min + step * i;
            var bMax = i == bucketCount - 1 ? max : min + step * (i + 1);
            result.Add(new StatisticsTripKmBucket($"{bMin:0.#}–{bMax:0.#} km", bMin, bMax, buckets[i]));
        }

        return result;
    }

    public static string FormatDate(DateTimeOffset dt) =>
        dt.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static IReadOnlyList<StatisticsDateSeriesPoint> FillDateGaps(
        DateTimeOffset from,
        DateTimeOffset to,
        IReadOnlyDictionary<string, int> counts)
    {
        var list = new List<StatisticsDateSeriesPoint>();
        var cursor = DateOnly.FromDateTime(from.UtcDateTime);
        var end = DateOnly.FromDateTime(to.UtcDateTime);
        while (cursor <= end)
        {
            var key = cursor.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            counts.TryGetValue(key, out var count);
            list.Add(new StatisticsDateSeriesPoint(key, count));
            cursor = cursor.AddDays(1);
        }

        return list;
    }

    public static IReadOnlyList<StatisticsRevenueAverageBucket> FillDecimalDateGaps(
        DateTimeOffset from,
        DateTimeOffset to,
        IReadOnlyDictionary<string, decimal> amounts)
    {
        var list = new List<StatisticsRevenueAverageBucket>();
        var cursor = DateOnly.FromDateTime(from.UtcDateTime);
        var end = DateOnly.FromDateTime(to.UtcDateTime);
        while (cursor <= end)
        {
            var key = cursor.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            amounts.TryGetValue(key, out var amount);
            list.Add(new StatisticsRevenueAverageBucket(key, amount));
            cursor = cursor.AddDays(1);
        }

        return list;
    }

    public static IReadOnlyList<StatisticsRevenueAverageBucket> FillMonthGaps(
        DateTimeOffset from,
        DateTimeOffset to,
        IReadOnlyDictionary<string, decimal> amounts)
    {
        var list = new List<StatisticsRevenueAverageBucket>();
        var cursor = new DateOnly(from.UtcDateTime.Year, from.UtcDateTime.Month, 1);
        var end = new DateOnly(to.UtcDateTime.Year, to.UtcDateTime.Month, 1);
        while (cursor <= end)
        {
            var key = cursor.ToString("yyyy-MM", CultureInfo.InvariantCulture);
            amounts.TryGetValue(key, out var amount);
            list.Add(new StatisticsRevenueAverageBucket(key, amount));
            cursor = cursor.AddMonths(1);
        }

        return list;
    }
}
