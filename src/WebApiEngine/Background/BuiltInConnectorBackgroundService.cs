using WebApiEngine.Connectors;
using WebApiEngine.Jobs;

namespace WebApiEngine.Background;

/// <summary>
/// Der Host der mitgelieferten Konnektoren. Er ist ein Worker wie jeder andere, nur ohne
/// HTTP-Umweg: Er holt Auftraege seines Typs ueber <see cref="ServiceTaskJobService"/>,
/// arbeitet sie ab und meldet sie ueber dieselben Wege zurueck. Die Engine bekommt dadurch
/// keinen Sonderpfad fuer eingebaute Arbeit.
///
/// Ein gescheiterter Auftrag darf den Dienst nicht beenden: Er wird gemeldet, gezaehlt und
/// beim naechsten Durchgang neu aufgenommen.
/// </summary>
public sealed class BuiltInConnectorBackgroundService : BackgroundService
{
    private readonly IBuiltInConnector[] _connectors;
    private readonly ServiceTaskJobService _jobService;
    private readonly FlowzerConnectorOptions _options;
    private readonly ConnectorDiagnosticsState _diagnostics;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<BuiltInConnectorBackgroundService> _logger;

    public BuiltInConnectorBackgroundService(
        IEnumerable<IBuiltInConnector> connectors,
        ServiceTaskJobService jobService,
        FlowzerConnectorOptions options,
        ConnectorDiagnosticsState diagnostics,
        TimeProvider timeProvider,
        ILogger<BuiltInConnectorBackgroundService> logger)
    {
        _connectors = connectors.ToArray();
        _jobService = jobService;
        _options = options;
        _diagnostics = diagnostics;
        _timeProvider = timeProvider;
        _logger = logger;

        // Schon beim Bauen anmelden, nicht erst im ersten Durchgang: Die Betriebssicht soll
        // ab dem Start jeden Konnektor nennen, auch die abgeschalteten. Wuerde das erst der
        // Hintergrundlauf tun, waere der Abschnitt kurz nach dem Start je nach Zeitpunkt leer.
        foreach (var connector in _connectors)
        {
            _diagnostics.MarkConfigured(connector.Name, connector.JobType, connector.Enabled);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var active = _connectors.Where(connector => connector.Enabled).ToArray();
        if (active.Length == 0)
        {
            _logger.LogInformation("Kein mitgelieferter Konnektor ist aktiviert.");
            return;
        }

        _logger.LogInformation(
            "Mitgelieferte Konnektoren aktiv: {Connectors}.",
            string.Join(", ", active.Select(connector => connector.JobType)));

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.ResolvedPollIntervalSeconds));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            foreach (var connector in active)
            {
                try
                {
                    await RunOnce(connector, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    // Ein Durchgang darf scheitern, der Dienst nicht: Sonst stuende nach einem
                    // einzelnen kaputten Auftrag die ganze Auftragsbearbeitung still.
                    _logger.LogError(
                        exception,
                        "Durchgang des Konnektors {Connector} fehlgeschlagen.",
                        connector.Name);
                    _diagnostics.MarkFailed(connector.Name, exception.Message);
                }
            }
        }
    }

    /// <summary>Holt einen Schwung Auftraege und arbeitet sie nebenlaeufig ab.</summary>
    internal async Task RunOnce(IBuiltInConnector connector, CancellationToken cancellationToken)
    {
        _diagnostics.MarkConfigured(connector.Name, connector.JobType, connector.Enabled);
        if (!connector.Enabled)
        {
            // Der Durchgang endet vor dem Abholen: Ein abgeschalteter Konnektor darf einen
            // Auftrag nicht einmal sperren, sonst waere er fuer einen externen Worker blockiert.
            return;
        }

        var workerId = BuiltInConnectorIdentity.WorkerId(connector.Name);
        var lease = TimeSpan.FromSeconds(_options.ResolvedLeaseSeconds);

        var jobs = await _jobService.FetchAndLock(
            connector.JobType,
            BuiltInConnectorIdentity.UserId,
            workerId,
            _options.ResolvedMaxConcurrentJobs,
            lease);

        _diagnostics.MarkRun(connector.Name, _timeProvider.GetUtcNow().UtcDateTime);
        if (jobs.Count == 0)
        {
            return;
        }

        await Task.WhenAll(jobs.Select(job => Execute(connector, job, workerId, lease, cancellationToken)));
    }

    private async Task Execute(
        IBuiltInConnector connector,
        ServiceTaskJob job,
        string workerId,
        TimeSpan lease,
        CancellationToken cancellationToken)
    {
        using var heartbeatSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var heartbeat = KeepLeaseAlive(job, workerId, lease, heartbeatSource.Token);

        ConnectorOutcome? outcome;
        try
        {
            outcome = await connector.Execute(job, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Der Host faehrt herunter. Die Lease laeuft ab und der Auftrag wird neu vergeben.
            outcome = null;
        }
        catch (Exception exception)
        {
            // Die rohe Ausnahmemeldung bleibt im Log: Sie kann Adressen oder aufgeloeste Werte
            // tragen und gehoert weder an den Auftrag noch in die Betriebssicht.
            _logger.LogError(
                exception,
                "Konnektor {Connector} konnte Auftrag {JobId} nicht ausfuehren.",
                connector.Name,
                job.Id);
            outcome = new ConnectorOutcome.Failed(
                $"The {connector.Name} connector could not run this job.",
                TimeSpan.FromSeconds(_options.ResolvedPollIntervalSeconds));
        }
        finally
        {
            await heartbeatSource.CancelAsync();
            await heartbeat;
        }

        if (outcome is not null)
        {
            await Report(connector, job, workerId, outcome);
        }
    }

    private async Task Report(IBuiltInConnector connector, ServiceTaskJob job, string workerId, ConnectorOutcome outcome)
    {
        try
        {
            switch (outcome)
            {
                case ConnectorOutcome.Completed completed:
                    await _jobService.Complete(job.Id, BuiltInConnectorIdentity.UserId, workerId, completed.Variables);
                    _diagnostics.MarkProcessed(connector.Name);
                    return;

                case ConnectorOutcome.BpmnError error:
                    await _jobService.ThrowError(
                        job.Id,
                        BuiltInConnectorIdentity.UserId,
                        workerId,
                        error.ErrorCode,
                        error.ErrorMessage,
                        error.Variables);
                    // Ein fachlicher Fehler ist ein gueltiges Ergebnis, kein Betriebsproblem:
                    // Das Modell hat dafuer einen eigenen Weg.
                    _diagnostics.MarkProcessed(connector.Name);
                    return;

                case ConnectorOutcome.Failed failed:
                    await _jobService.Fail(
                        job.Id,
                        BuiltInConnectorIdentity.UserId,
                        workerId,
                        failed.Message,
                        null,
                        failed.Backoff);
                    _diagnostics.MarkFailed(connector.Name, failed.Message);
                    return;
            }
        }
        catch (Exception exception)
        {
            // Die Rueckmeldung selbst kann scheitern, etwa wenn der Token inzwischen nicht mehr
            // wartet. Der Auftrag faellt dann ueber die ablaufende Lease zurueck.
            _logger.LogError(
                exception,
                "Rueckmeldung des Konnektors {Connector} fuer Auftrag {JobId} fehlgeschlagen.",
                connector.Name,
                job.Id);
            _diagnostics.MarkFailed(connector.Name, $"The {connector.Name} connector could not report this job.");
        }
    }

    /// <summary>
    /// Verlaengert die Sperre waehrend eines langen Laufs. Ohne diesen Herzschlag bekaeme ein
    /// zweiter Bezieher denselben Auftrag, waehrend der erste noch arbeitet, und ein
    /// Service-Task mit Seiteneffekt liefe doppelt.
    /// </summary>
    private async Task KeepLeaseAlive(ServiceTaskJob job, string workerId, TimeSpan lease, CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, lease.TotalSeconds / 2));

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(interval, _timeProvider, cancellationToken);
                var outcome = await _jobService.RenewLease(job.Id, BuiltInConnectorIdentity.UserId, workerId, lease);
                if (outcome.Status != JobOperationResult.Ok)
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Regulaeres Ende: Der Auftrag ist fertig oder der Host faehrt herunter.
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Lease-Verlaengerung fuer Auftrag {JobId} fehlgeschlagen.", job.Id);
        }
    }
}
