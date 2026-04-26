namespace GISSpatialChecker.Models
{
    public class SpatialResult
    {
        public string EntityAId { get; set; } = string.Empty;
        public string EntityAName { get; set; } = string.Empty;
        public double EntityALat { get; set; }
        public double EntityALon { get; set; }

        public string EntityBId { get; set; } = string.Empty;
        public string EntityBName { get; set; } = string.Empty;
        public double EntityBLat { get; set; }
        public double EntityBLon { get; set; }

        public double JarakMeter { get; set; }
        public double RadiusSetting { get; set; }

        public bool DalamRadius => JarakMeter <= RadiusSetting;
        public string Status => DalamRadius ? "✅ DALAM" : "❌ LUAR";
        public string JarakFormatted => $"{JarakMeter:F2} m";
    }
}
