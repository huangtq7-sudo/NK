using System.Net;
using Naraka.Server.LegacyNetworkV1;

namespace Naraka.Server.Host;

public sealed class LegacyNetworkHostedService(
    LegacyNetworkTransport transport,
    IConfiguration configuration,
    ILogger<LegacyNetworkHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var section = configuration.GetSection("Naraka:LegacyNetwork");
        var addressText = section["ListenAddress"] ?? IPAddress.Loopback.ToString();
        if (!IPAddress.TryParse(addressText, out var address))
        {
            throw new InvalidOperationException($"Invalid legacy network listen address: {addressText}.");
        }

        var port = section.GetValue("ListenPort", 8011);
        logger.LogInformation("LegacyNetworkV1 starting on {Address}:{Port}", address, port);

        try
        {
            await transport.RunAsync(address, port, stoppingToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Only a listener fault reaches this point; connection faults are isolated in the
            // transport. The host stops after this, so the reason has to be in the log file.
            logger.LogCritical(
                exception,
                "LegacyNetworkV1 listener stopped on {Address}:{Port}. The host is shutting down.",
                address,
                port);
            throw;
        }
    }
}
