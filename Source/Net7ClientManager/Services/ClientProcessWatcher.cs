namespace Net7ClientManager.Services;

using System.Diagnostics;

public sealed class ClientProcessWatcher : IDisposable
{
    private const string ProcessName = "client";

    private readonly Action<int> processStarted;
    private readonly Action<int> processStopped;
    private readonly System.Windows.Forms.Timer timer;
    private readonly HashSet<int> runningProcessIds = [];

    public ClientProcessWatcher(Action<int> processStarted, Action<int> processStopped)
    {
        this.processStarted = processStarted;
        this.processStopped = processStopped;

        this.timer = new System.Windows.Forms.Timer
        {
            Interval = 1000,
        };

        this.timer.Tick += this.Timer_OnTick;
    }

    public void Start()
    {
        this.CheckProcesses();
        this.timer.Start();
    }

    public void Dispose()
    {
        this.timer.Stop();
        this.timer.Tick -= this.Timer_OnTick;
        this.timer.Dispose();
    }

    private void Timer_OnTick(object? sender, EventArgs e)
    {
        this.CheckProcesses();
    }

    private void CheckProcesses()
    {
        var currentProcessIds = Process.GetProcessesByName(ProcessName)
            .Select(process => process.Id)
            .ToHashSet();

        var startedProcessIds = currentProcessIds
            .Except(this.runningProcessIds)
            .ToList();

        var stoppedProcessIds = this.runningProcessIds
            .Except(currentProcessIds)
            .ToList();

        this.runningProcessIds.Clear();

        foreach (var processId in currentProcessIds)
        {
            this.runningProcessIds.Add(processId);
        }

        foreach (var processId in stoppedProcessIds)
        {
            this.processStopped(processId);
        }

        foreach (var processId in startedProcessIds)
        {
            this.processStarted(processId);
        }
    }
}
