using GISSpatialChecker.Models;
using System.Collections.Concurrent;

namespace GISSpatialChecker.Services
{
    public static class SpatialService
    {
        public static double HitungJarak(double lat1, double lon1, double lat2, double lon2)
        {
            const double R = 6371000;
            var dLat = (lat2 - lat1) * Math.PI / 180;
            var dLon = (lon2 - lon1) * Math.PI / 180;
            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                    Math.Cos(lat1 * Math.PI / 180) *
                    Math.Cos(lat2 * Math.PI / 180) *
                    Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        }

        // ─── Nearest Neighbor: tiap Entity A → cari Entity B terdekat ─
        public static SpatialSummary CekSpasial(
            List<EntityPoint> listA,
            List<EntityPoint> listB,
            double radiusMeter,
            IProgress<ProgressInfo>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var results = new ConcurrentBag<SpatialResult>();
            int processed = 0;
            int total = listA.Count;
            var startTime = DateTime.Now;
            var lastReport = DateTime.Now;

            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount,
                CancellationToken = cancellationToken
            };

            // Untuk setiap Entity A → cari Entity B yang PALING DEKAT
            Parallel.ForEach(listA, options, a =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                SpatialResult? nearest = null;
                double minJarak = double.MaxValue;

                foreach (var b in listB)
                {
                    var jarak = HitungJarak(a.Latitude, a.Longitude, b.Latitude, b.Longitude);
                    if (jarak < minJarak)
                    {
                        minJarak = jarak;
                        nearest = new SpatialResult
                        {
                            EntityAId = a.Id, EntityAName = a.Name,
                            EntityALat = a.Latitude, EntityALon = a.Longitude,
                            EntityBId = b.Id, EntityBName = b.Name,
                            EntityBLat = b.Latitude, EntityBLon = b.Longitude,
                            JarakMeter = jarak,
                            RadiusSetting = radiusMeter
                        };
                    }
                }

                if (nearest != null)
                    results.Add(nearest);

                int current = Interlocked.Increment(ref processed);
                if (progress != null && (DateTime.Now - lastReport).TotalMilliseconds > 300)
                {
                    lastReport = DateTime.Now;
                    var elapsed = DateTime.Now - startTime;
                    double rate = current / Math.Max(elapsed.TotalSeconds, 0.001);
                    double remaining = (total - current) / Math.Max(rate, 1);

                    progress.Report(new ProgressInfo
                    {
                        Current = current,
                        Total = total,
                        Elapsed = elapsed,
                        EstimatedRemaining = TimeSpan.FromSeconds(remaining),
                        Rate = (long)rate
                    });
                }
            });

            var allResults = results.ToList();
            long countDalam = allResults.Count(r => r.DalamRadius);
            long countLuar = allResults.Count(r => !r.DalamRadius);

            progress?.Report(new ProgressInfo
            {
                Current = total, Total = total,
                Elapsed = DateTime.Now - startTime,
                EstimatedRemaining = TimeSpan.Zero, Rate = 0,
                CountDalam = countDalam, CountLuar = countLuar
            });

            return new SpatialSummary
            {
                HasilDalam = allResults, // semua hasil (dalam & luar)
                TotalKombinasi = total,
                CountDalam = countDalam,
                CountLuar = countLuar
            };
        }
    }

    public class SpatialSummary
    {
        public List<SpatialResult> HasilDalam { get; set; } = new();
        public long TotalKombinasi { get; set; }
        public long CountDalam { get; set; }
        public long CountLuar { get; set; }
    }

    public class ProgressInfo
    {
        public long Current { get; set; }
        public long Total { get; set; }
        public TimeSpan Elapsed { get; set; }
        public TimeSpan EstimatedRemaining { get; set; }
        public long Rate { get; set; }
        public long CountDalam { get; set; }
        public long CountLuar { get; set; }

        public double Percentage => Total > 0 ? (double)Current / Total * 100 : 0;
        public string ElapsedFormatted => $"{(int)Elapsed.TotalMinutes:D2}:{Elapsed.Seconds:D2}";
        public string RemainingFormatted => EstimatedRemaining.TotalSeconds > 0
            ? $"{(int)EstimatedRemaining.TotalMinutes:D2}:{EstimatedRemaining.Seconds:D2}"
            : "00:00";
    }
}
