namespace AvyInReach;

internal static class AspectFormat
{
    public static string Normalize(string value) =>
        value.Trim().ToLowerInvariant() switch
        {
            "n" or "north" => "N",
            "ne" or "northeast" => "NE",
            "e" or "east" => "E",
            "se" or "southeast" => "SE",
            "s" or "south" => "S",
            "sw" or "southwest" => "SW",
            "w" or "west" => "W",
            "nw" or "northwest" => "NW",
            _ => value.Trim().ToUpperInvariant(),
        };
}
