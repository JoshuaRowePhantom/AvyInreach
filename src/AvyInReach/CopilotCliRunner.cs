namespace AvyInReach;

internal interface ICopilotCliRunner
{
    Task<ProcessRunResult> RunPromptAsync(string prompt, CancellationToken cancellationToken);
}

internal sealed class CopilotCliRunner(
    IProcessRunner processRunner,
    CopilotConfigurationStore configurationStore) : ICopilotCliRunner
{
    public async Task<ProcessRunResult> RunPromptAsync(string prompt, CancellationToken cancellationToken)
    {
        var configuration = await configurationStore.GetAsync(cancellationToken);
        return await processRunner.RunAsync(
            "copilot",
            [
                "-p",
                prompt,
                "--model",
                configuration.Model,
                "--allow-all",
                "--silent",
                "--output-format",
                "text",
                "--no-color",
            ],
            cancellationToken);
    }
}
