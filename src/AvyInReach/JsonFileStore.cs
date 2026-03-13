using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AvyInReach;

internal static class JsonFileStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static async Task<T> ReadAsync<T>(string path, T fallback, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return fallback;
        }

        await using var stream = File.OpenRead(path);
        var value = await JsonSerializer.DeserializeAsync<T>(stream, SerializerOptions, cancellationToken);
        return value ?? fallback;
    }

    public static Task<IAsyncDisposable> AcquireLockAsync(string rootDirectory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            throw new InvalidOperationException("Store root directory cannot be empty.");
        }

        var semaphore = new Semaphore(initialCount: 1, maximumCount: 1, name: GetMutexName(rootDirectory));
        try
        {
            var signaledIndex = WaitHandle.WaitAny([semaphore, cancellationToken.WaitHandle]);
            if (signaledIndex == 1)
            {
                semaphore.Dispose();
                throw new OperationCanceledException(cancellationToken);
            }

            return Task.FromResult<IAsyncDisposable>(new SemaphoreLease(semaphore));
        }
        catch
        {
            semaphore.Dispose();
            throw;
        }
    }

    public static async Task WriteAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var tempPath = $"{path}.tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, value, SerializerOptions, cancellationToken);
        }

        File.Move(tempPath, path, overwrite: true);
    }

    internal static string GetMutexName(string rootDirectory)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(rootDirectory).ToUpperInvariant()));
        var suffix = Convert.ToHexString(bytes);
        return $"Local\\AvyInReach-Store-{suffix}";
    }

    private sealed class SemaphoreLease(Semaphore semaphore) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            semaphore.Release();
            semaphore.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
