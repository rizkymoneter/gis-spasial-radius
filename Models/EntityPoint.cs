namespace GISSpatialChecker.Models
{
    public class EntityPoint
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Group { get; set; } = string.Empty; // "A" atau "B"
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string Description { get; set; } = string.Empty;

        public override string ToString() => $"{Name} ({Latitude}, {Longitude})";
    }
}
