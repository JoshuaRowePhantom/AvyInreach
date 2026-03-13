namespace AvyInReach.Tests;

public sealed class CopilotCliRunnerTests
{
    [Fact]
    public async Task RunPromptAsync_uses_default_model_when_not_configured()
    {
        var processRunner = new RecordingProcessRunner();
        var paths = new AppPathsForTests();
        var runner = new CopilotCliRunner(processRunner, new CopilotConfigurationStore(paths));

        await runner.RunPromptAsync("hello", CancellationToken.None);

        Assert.Equal("copilot", processRunner.FileName);
        Assert.Contains("--model", processRunner.Arguments);
        Assert.Contains("gpt-5-mini", processRunner.Arguments);
    }

    [Fact]
    public async Task RunPromptAsync_uses_configured_model()
    {
        var processRunner = new RecordingProcessRunner();
        var paths = new AppPathsForTests();
        var store = new CopilotConfigurationStore(paths);
        await store.ConfigureAsync("gpt-5.4", CancellationToken.None);
        var runner = new CopilotCliRunner(processRunner, store);

        await runner.RunPromptAsync("hello", CancellationToken.None);

        Assert.Contains("gpt-5.4", processRunner.Arguments);
    }

    private sealed class RecordingProcessRunner : IProcessRunner
    {
        public string FileName { get; private set; } = string.Empty;

        public IReadOnlyList<string> Arguments { get; private set; } = [];

        public Task<ProcessRunResult> RunAsync(string fileName, IEnumerable<string> arguments, CancellationToken cancellationToken)
        {
            FileName = fileName;
            Arguments = arguments.ToList();
            return Task.FromResult(new ProcessRunResult(0, "ok", string.Empty));
        }
    }
}
