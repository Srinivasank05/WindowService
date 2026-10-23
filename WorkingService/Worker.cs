using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using WorkingService;
using WorkingService.Service;

public class Worker : BackgroundService
{
    // These are created once and kept for the whole life of the service.
    // Because of this, _sentIds inside them is remembered between cycles.
    private readonly Modality _modality;
    private readonly PatientDemography _patientDemography;
    private readonly int _intervalSeconds;

    
    public Worker(IConfiguration config)
    {
       
        _intervalSeconds = config.GetValue("Service:IntervalSeconds", 60);

       
        var http = new HttpClient();

        
        _modality = new Modality(http);
        _patientDemography = new PatientDemography(http);
    }

    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogWriter.LogInfo("Service Started with 2 class(es)");

        // Repeat until someone stops the service.
        while (!stoppingToken.IsCancellationRequested)
        {
            // Job 1: run Modality (errors are handled inside RunSafeAsync)
            await RunSafeAsync("Modality",
                () => _modality.GetModalityDetails(),
                stoppingToken);

            // Job 2: run PatientDemography
            // This runs even if Modality failed above.
            await RunSafeAsync("PatientDemography",
                () => _patientDemography.getPatientDemography(),
                stoppingToken);

            // Wait before the next cycle.
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_intervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Service was stopped while waiting, so leave the loop.
                break;
            }
        }

        LogWriter.LogInfo("Service Stopped");
    }

    // ---------- HELPER: runs one job safely ----------
    // name   = job name for the log message
    // action = the job method to run
    // ct     = the stop signal
    private static async Task RunSafeAsync(string name, Func<Task> action, CancellationToken ct)
    {
        try
        {
            await action();   // run the job
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Service is stopping. This is normal, so don't log an error.
        }
        catch (Exception ex)
        {
            // Any other problem: log it with the job name and keep the service alive.
            LogWriter.LogError($"[{name}] Error: {ex.Message}", ex);
        }
    }
}