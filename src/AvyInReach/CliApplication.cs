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
            var deliveryConfigurationStore = new DeliveryConfigurationStore(appPaths);
            var recipientConfigurationStore = new RecipientConfigurationStore(appPaths);
            var scheduleStore = new ScheduleStore(appPaths);
            var smtpConfigurationStore = new SmtpConfigurationStore(appPaths);
            var garminConfigurationStore = new GarminConfigurationStore(appPaths);
            var recipientConfigurationValidator = new RecipientConfigurationValidator(
                recipientConfigurationStore,
                garminConfigurationStore);
            var processRunner = new ProcessRunner();
            var provider = new AvalancheCanadaProvider(httpClient, processRunner);
            var providerRegistry = new ProviderRegistry([provider]);
            var summarizer = new CopilotCliSummarizer(processRunner);
            var emailSender = new RoutingEmailSender(
                new SmtpEmailSender(smtpConfigurationStore),
                new GarminInReachEmailSender(httpClient, smtpConfigurationStore, garminConfigurationStore));
            var clock = new SystemClock();
            var updateService = new ForecastUpdateService(
                providerRegistry,
                summarizer,
                emailSender,
                deliveryConfigurationStore,
                recipientConfigurationStore,
                stateStore,
                clock,
                _log);
            var scheduler = new WindowsTaskScheduler(processRunner, _log);

            switch (command)
            {
                case HelpCommand:
                    _log.Info(CommandText.HelpText);
                    return 0;

                case GarminConfigureCommand garminConfigureCommand:
                    await HandleGarminConfigureAsync(
                        garminConfigurationStore,
                        recipientConfigurationStore,
                        garminConfigureCommand,
                        cancellationToken);
                    return 0;

                case RecipientConfigureCommand recipientConfigureCommand:
                    await HandleRecipientConfigureAsync(
                        recipientConfigurationStore,
                        recipientConfigureCommand,
                        cancellationToken);
                    return 0;

                case DeliveryConfigureCommand deliveryConfigureCommand:
                    await HandleDeliveryConfigureAsync(deliveryConfigurationStore, deliveryConfigureCommand, cancellationToken);
                    return 0;

                case SmtpConfigureCommand smtpConfigureCommand:
                    await HandleSmtpConfigureAsync(smtpConfigurationStore, smtpConfigureCommand, cancellationToken);
                    return 0;

                case RegionsCommand regionsCommand:
                    await HandleRegionsAsync(providerRegistry, regionsCommand.Provider, cancellationToken);
                    return 0;

                case PreviewCommand previewCommand:
                    _log.Info(await updateService.GenerateSummaryAsync(
                        previewCommand.RecipientAddress,
                        previewCommand.Provider,
                        previewCommand.Region,
                        cancellationToken));
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
                        appPaths,
                        recipientConfigurationValidator,
                        scheduler,
                        scheduleCommand,
                        cancellationToken);
                    return 0;

                case ScheduleLogCommand scheduleLogCommand:
                    await HandleScheduleLogAsync(scheduleStore, scheduleLogCommand.Id, cancellationToken);
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

    private async Task HandleSmtpConfigureAsync(
        SmtpConfigurationStore configurationStore,
        SmtpConfigureCommand command,
        CancellationToken cancellationToken)
    {
        await configurationStore.ConfigureAsync(command.Server, command.FromAddress, cancellationToken);
        _log.Info("SMTP configuration saved.");
        _log.Info($"Server: {command.Server.Value}");
        _log.Info($"From: {command.FromAddress}");
    }

    private async Task HandleGarminConfigureAsync(
        GarminConfigurationStore configurationStore,
        RecipientConfigurationStore recipientConfigurationStore,
        GarminConfigureCommand command,
        CancellationToken cancellationToken)
    {
        await configurationStore.ConfigureAsync(command.InReachAddress, command.ReplyLink, command.MaxMessages, cancellationToken);
        var recipientSettings = await recipientConfigurationStore.ConfigureAsync(
            command.InReachAddress,
            RecipientTransport.InReach,
            summaryCharacterBudget: null,
            cancellationToken);
        _log.Info("Garmin reply link saved.");
        _log.Info($"InReach: {command.InReachAddress}");
        _log.Info($"Reply link: {command.ReplyLink}");
        _log.Info($"Max messages: {command.MaxMessages}");
        _log.Info($"Summary budget: {recipientSettings.SummaryCharacterBudget}");
    }

    private async Task HandleRecipientConfigureAsync(
        RecipientConfigurationStore configurationStore,
        RecipientConfigureCommand command,
        CancellationToken cancellationToken)
    {
        var settings = await configurationStore.ConfigureAsync(
            command.RecipientAddress,
            command.Transport,
            command.SummaryCharacterBudget,
            cancellationToken);
        _log.Info("Recipient settings saved.");
        _log.Info($"Recipient: {settings.RecipientAddress}");
        _log.Info($"Transport: {settings.Transport.ToConfigValue()}");
        _log.Info($"Summary budget: {settings.SummaryCharacterBudget}");
    }

    private async Task HandleDeliveryConfigureAsync(
        DeliveryConfigurationStore configurationStore,
        DeliveryConfigureCommand command,
        CancellationToken cancellationToken)
    {
        await configurationStore.ConfigureAsync(command.MaxReportsPer24Hours, cancellationToken);
        _log.Info("Delivery configuration saved.");
        _log.Info($"Max reports per 24 hours: {command.MaxReportsPer24Hours}");
    }

    private static string ReadPassword(string prompt)
    {
        Console.Write(prompt);
        if (Console.IsInputRedirected)
        {
            Console.WriteLine();
            return Console.ReadLine() ?? string.Empty;
        }

        var password = new System.Text.StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return password.ToString();
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (password.Length > 0)
                {
                    password.Length--;
                }

                continue;
            }

            if (!char.IsControl(key.KeyChar))
            {
                password.Append(key.KeyChar);
            }
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
        AppPaths appPaths,
        RecipientConfigurationValidator recipientConfigurationValidator,
        WindowsTaskScheduler scheduler,
        ScheduleCommand command,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Scheduling is supported only on Windows.");
        }

        await recipientConfigurationValidator.EnsureScheduledRecipientConfiguredAsync(
            command.InReachAddress,
            cancellationToken);

        var provider = registry.GetByName(command.Provider);
        var region = await provider.ResolveRegionAsync(command.Region, cancellationToken);
        if (region is null)
        {
            throw new InvalidOperationException(
                $"Region '{command.Region}' was not found for provider '{provider.Id}'.");
        }

        var scheduleId = $"{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..20];
        var taskName = $"AvyInReach-{scheduleId}";
        Directory.CreateDirectory(appPaths.ScheduleLogDirectory);
        var logPath = Path.Combine(appPaths.ScheduleLogDirectory, $"{scheduleId}.log");
        var invocation = ScheduledInvocation.ForCurrentProcessWithLog(
            logPath,
            "update",
            command.InReachAddress,
            provider.Id,
            command.Region);
        var taskCredentials = new ScheduledTaskCredentials(
            ReadRequiredLine("Task Scheduler username: "),
            ReadPassword("Task Scheduler password: "));

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
            LogPath = logPath,
            CreatedUtc = DateTimeOffset.UtcNow,
        };

        await scheduler.RegisterAsync(record, taskCredentials, cancellationToken);
        await scheduleStore.UpsertAsync(record, cancellationToken);

        _log.Info($"Installed schedule {record.Id}");
        _log.Info($"Task name: {record.WindowsTaskName}");
        _log.Info($"Range: {record.StartDate.ToString("M/d/yyyy", CultureInfo.InvariantCulture)} - {record.EndDate.ToString("M/d/yyyy", CultureInfo.InvariantCulture)}");
        _log.Info($"Last-run log: {record.LogPath}");
    }

    private async Task HandleScheduleLogAsync(
        ScheduleStore scheduleStore,
        string scheduleId,
        CancellationToken cancellationToken)
    {
        var schedule = await scheduleStore.GetByIdAsync(scheduleId, cancellationToken);
        if (schedule is null)
        {
            throw new InvalidOperationException($"Schedule '{scheduleId}' was not found.");
        }

        if (string.IsNullOrWhiteSpace(schedule.LogPath) || !File.Exists(schedule.LogPath))
        {
            throw new InvalidOperationException($"No run log is available for schedule '{scheduleId}' yet.");
        }

        _log.Info(await File.ReadAllTextAsync(schedule.LogPath, cancellationToken));
    }

    private static string ReadRequiredLine(string prompt)
    {
        Console.Write(prompt);
        var value = Console.ReadLine()?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException("Value cannot be empty.");
        }

        return value;
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
                $"{schedule.StartDate:M/d/yyyy}-{schedule.EndDate:M/d/yyyy} | {schedule.WindowsTaskName} | {schedule.LogPath}");
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
