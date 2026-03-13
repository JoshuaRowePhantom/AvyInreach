using System.Globalization;
namespace AvyInReach;

internal sealed class CliApplication
{
    private readonly ConsoleLog _log = new();

    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        ParsedCommand command;

        try
        {
            command = CommandParser.Parse(args);
        }
        catch (CliUsageException ex)
        {
            _log.Error(ex.Message);
            _log.Info(string.Empty);
            _log.Info(CommandText.HelpText);
            return 1;
        }

        try
        {
            using var httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30),
            };

            var appPaths = new AppPaths();
            var stateStore = new DeliveryStateStore(appPaths);
            var scheduleStore = new ScheduleStore(appPaths);
            var processRunner = new ProcessRunner();
            var provider = new AvalancheCanadaProvider(httpClient);
            var providerRegistry = new ProviderRegistry([provider]);
            var summarizer = new CopilotCliSummarizer(processRunner);
            var emailSender = new SmtpEmailSender();
            var clock = new SystemClock();
            var updateService = new ForecastUpdateService(
                providerRegistry,
                summarizer,
                emailSender,
                stateStore,
                clock,
                _log);
            var scheduler = new WindowsTaskScheduler(processRunner, _log);

            switch (command)
            {
                case HelpCommand:
                    _log.Info(CommandText.HelpText);
                    return 0;

                case RegionsCommand regionsCommand:
                    await HandleRegionsAsync(providerRegistry, regionsCommand.Provider, cancellationToken);
                    return 0;

                case SendCommand sendCommand:
                    await updateService.ProcessAsync(
                        DeliveryMode.Send,
                        sendCommand.InReachAddress,
                        sendCommand.Provider,
                        sendCommand.Region,
                        cancellationToken);
                    return 0;

                case UpdateCommand updateCommand:
                    await updateService.ProcessAsync(
                        DeliveryMode.Update,
                        updateCommand.InReachAddress,
                        updateCommand.Provider,
                        updateCommand.Region,
                        cancellationToken);
                    return 0;

                case ScheduleCommand scheduleCommand:
                    await HandleScheduleAsync(
                        providerRegistry,
                        scheduleStore,
                        scheduler,
                        scheduleCommand,
                        cancellationToken);
                    return 0;

                case SchedulesCommand:
                    await HandleSchedulesAsync(scheduleStore, cancellationToken);
                    return 0;

                case UnscheduleCommand unscheduleCommand:
                    await HandleUnscheduleAsync(scheduleStore, scheduler, unscheduleCommand.Id, cancellationToken);
                    return 0;

                default:
                    throw new InvalidOperationException($"Unsupported command type: {command.GetType().Name}");
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex.Message);
            return 1;
        }
    }

    private async Task HandleRegionsAsync(
        ProviderRegistry registry,
        string? providerName,
        CancellationToken cancellationToken)
    {
        var providers = providerName is null
            ? registry.GetSupportedProviders()
            : [registry.GetByName(providerName)];

        foreach (var provider in providers)
        {
            _log.Info(provider.Id);

            var regions = await provider.GetRegionsAsync(cancellationToken);
            foreach (var region in regions.OrderBy(region => region.DisplayName, StringComparer.OrdinalIgnoreCase))
            {
                _log.Info($"  {region.DisplayName}");
            }

            if (providers.Count > 1)
            {
                _log.Info(string.Empty);
            }
        }
    }

    private async Task HandleScheduleAsync(
        ProviderRegistry registry,
        ScheduleStore scheduleStore,
        WindowsTaskScheduler scheduler,
        ScheduleCommand command,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Scheduling is supported only on Windows.");
        }

        var provider = registry.GetByName(command.Provider);
        var region = await provider.ResolveRegionAsync(command.Region, cancellationToken);
        if (region is null)
        {
            throw new InvalidOperationException(
                $"Region '{command.Region}' was not found for provider '{provider.Id}'.");
        }

        var scheduleId = $"{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..20];
        var taskName = $"AvyInReach-{scheduleId}";
        var invocation = ScheduledInvocation.ForCurrentProcess(
            "update",
            command.InReachAddress,
            provider.Id,
            region.DisplayName);

        var record = new ScheduleRecord
        {
            Id = scheduleId,
            Provider = provider.Id,
            Region = region.DisplayName,
            InReachAddress = command.InReachAddress,
            StartDate = command.StartDate,
            EndDate = command.EndDate,
            WindowsTaskName = taskName,
            ExecutePath = invocation.ExecutePath,
            Arguments = invocation.Arguments,
            CreatedUtc = DateTimeOffset.UtcNow,
        };

        await scheduler.RegisterAsync(record, cancellationToken);
        await scheduleStore.UpsertAsync(record, cancellationToken);

        _log.Info($"Installed schedule {record.Id}");
        _log.Info($"Task name: {record.WindowsTaskName}");
        _log.Info($"Range: {record.StartDate.ToString("M/d/yyyy", CultureInfo.InvariantCulture)} - {record.EndDate.ToString("M/d/yyyy", CultureInfo.InvariantCulture)}");
    }

    private async Task HandleSchedulesAsync(ScheduleStore scheduleStore, CancellationToken cancellationToken)
    {
        var schedules = await scheduleStore.ListAsync(cancellationToken);
        if (schedules.Count == 0)
        {
            _log.Info("No schedules installed.");
            return;
        }

        foreach (var schedule in schedules.OrderBy(item => item.CreatedUtc))
        {
            _log.Info(
                $"{schedule.Id} | {schedule.Provider} | {schedule.Region} | {schedule.InReachAddress} | " +
                $"{schedule.StartDate:M/d/yyyy}-{schedule.EndDate:M/d/yyyy} | {schedule.WindowsTaskName}");
        }
    }

    private async Task HandleUnscheduleAsync(
        ScheduleStore scheduleStore,
        WindowsTaskScheduler scheduler,
        string scheduleId,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Scheduling is supported only on Windows.");
        }

        var schedule = await scheduleStore.GetByIdAsync(scheduleId, cancellationToken);
        if (schedule is null)
        {
            throw new InvalidOperationException($"Schedule '{scheduleId}' was not found.");
        }

        await scheduler.DeleteAsync(schedule.WindowsTaskName, cancellationToken);
        await scheduleStore.DeleteAsync(scheduleId, cancellationToken);

        _log.Info($"Removed schedule {scheduleId}");
    }
}
