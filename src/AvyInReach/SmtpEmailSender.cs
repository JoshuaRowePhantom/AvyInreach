using System.Net;
using System.Net.Mail;

namespace AvyInReach;

internal interface IEmailSender
{
    Task SendAsync(string toAddress, string subject, string body, CancellationToken cancellationToken);
}

internal sealed class SmtpEmailSender(SmtpConfigurationStore configurationStore) : IEmailSender
{
    public async Task SendAsync(string toAddress, string subject, string body, CancellationToken cancellationToken)
    {
        var configuration = await configurationStore.GetRequiredAsync(cancellationToken);
        var server = configuration.Server
            ?? throw new InvalidOperationException("SMTP server is not configured.");

        using var message = new MailMessage(configuration.FromAddress!, toAddress, subject, body);
        using var client = new SmtpClient(server.Host, server.Port)
        {
            EnableSsl = configuration.EnableSsl,
        };

        if (!string.IsNullOrWhiteSpace(configuration.Username))
        {
            client.UseDefaultCredentials = false;
            client.Credentials = new NetworkCredential(configuration.Username, configuration.Password);
        }
        else if (configuration.UseDefaultCredentials)
        {
            client.UseDefaultCredentials = true;
        }

        await client.SendMailAsync(message, cancellationToken);
    }
}
