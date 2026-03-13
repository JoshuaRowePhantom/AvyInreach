namespace AvyInReach;

internal class AppPaths
{
    public AppPaths(string? rootDirectory = null)
    {
        var root = rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AvyInReach");

        RootDirectory = root;
        DeliveryStatePath = Path.Combine(root, "delivery-state.json");
        DeliveryConfigurationPath = Path.Combine(root, "delivery.json");
        SchedulePath = Path.Combine(root, "schedules.json");
        SmtpConfigurationPath = Path.Combine(root, "smtp.json");
        GarminConfigurationPath = Path.Combine(root, "garmin.json");
    }

    public string RootDirectory { get; }

    public string DeliveryStatePath { get; }

    public string DeliveryConfigurationPath { get; }

    public string SchedulePath { get; }

    public string SmtpConfigurationPath { get; }

    public string GarminConfigurationPath { get; }
}
