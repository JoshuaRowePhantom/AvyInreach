namespace AvyInReach;

internal interface IAppLog
{
    void Info(string message);

    void Warn(string message);

    void Error(string message);
}

internal sealed class ConsoleLog : IAppLog
{
    public void Info(string message) => Console.WriteLine(message);

    public void Warn(string message) => Console.WriteLine($"WARN: {message}");

    public void Error(string message) => Console.Error.WriteLine($"ERROR: {message}");
}
