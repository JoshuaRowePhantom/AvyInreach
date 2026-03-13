namespace AvyInReach.Tests;

public sealed class SmtpConfigurationStoreTests
{
    [Fact]
    public async Task ConfigureAsync_writes_server_from_and_defaults()
    {
        var paths = new AppPathsForTests();
        var store = new SmtpConfigurationStore(paths);

        await store.ConfigureAsync(new SmtpServer("smtp.example.com", 25), "avyinreach@example.com", CancellationToken.None);

        var configuration = await store.GetRequiredAsync(CancellationToken.None);

        Assert.Equal("smtp.example.com", configuration.Server!.Host);
        Assert.Equal(25, configuration.Server.Port);
        Assert.Equal("avyinreach@example.com", configuration.FromAddress);
        Assert.False(configuration.EnableSsl);
        Assert.True(configuration.UseDefaultCredentials);
        Assert.Null(configuration.Username);
        Assert.Null(configuration.Password);
        Assert.True(File.Exists(paths.SmtpConfigurationPath));
    }

    [Fact]
    public async Task ConfigureAsync_preserves_existing_optional_settings()
    {
        var paths = new AppPathsForTests();
        var store = new SmtpConfigurationStore(paths);

        await JsonFileStore.WriteAsync(
            paths.SmtpConfigurationPath,
            new SmtpConfiguration
            {
                Server = new SmtpServer("old.host", 587),
                FromAddress = "old@example.com",
                EnableSsl = true,
                UseDefaultCredentials = false,
                Username = "domain\\svc-avy",
                Password = "secret",
            },
            CancellationToken.None);

        await store.ConfigureAsync(new SmtpServer("new.host", 25), "new@example.com", CancellationToken.None);

        var configuration = await store.GetRequiredAsync(CancellationToken.None);

        Assert.Equal("new.host", configuration.Server!.Host);
        Assert.Equal(25, configuration.Server.Port);
        Assert.Equal("new@example.com", configuration.FromAddress);
        Assert.True(configuration.EnableSsl);
        Assert.False(configuration.UseDefaultCredentials);
        Assert.Equal("domain\\svc-avy", configuration.Username);
        Assert.Equal("secret", configuration.Password);
    }

    [Fact]
    public async Task GetRequiredAsync_throws_when_configuration_missing()
    {
        var store = new SmtpConfigurationStore(new AppPathsForTests());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => store.GetRequiredAsync(CancellationToken.None));

        Assert.Contains("smtp server <host:port> from <address>", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
