using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;

namespace MiHome.WinUI.Infrastructure;

internal sealed partial class AutomationTriggers
{
    private readonly DispatcherQueueTimer sampleTimer;
    private readonly int sessionId = CurrentSessionId();
    private readonly Dictionary<Guid, DateOnly> lastScheduleDate = [];
    private AutomationRule[] sampledRules = [];
    private string[] processTargets = [];
    private HashSet<string>? runningPrograms;
    private double? idleMinutes;
    private DateTime lastMinute;
    private long lastTick;
    private int samplingRevision;
    private bool readingProcesses;

    private void ConfigurePcSampling(List<AutomationRule> enabled)
    {
        sampledRules = enabled.Where(rule => rule.Trigger is "idle" or "active" or "schedule").ToArray();
        var paths = enabled.Where(rule => rule.Trigger is "process-start" or "process-exit").Select(rule => rule.Argument)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (!paths.SequenceEqual(processTargets, StringComparer.OrdinalIgnoreCase))
        {
            processTargets = paths;
            runningPrograms = null;
            samplingRevision++;
        }
        var existingIds = engine.Rules.Select(rule => rule.Id).ToHashSet();
        foreach (var id in lastScheduleDate.Keys.Where(id => !existingIds.Contains(id)).ToArray()) lastScheduleDate.Remove(id);
        if (sampledRules.Length == 0) ResetTimeSampling();
        UpdateSampleTimer();
    }

    private void UpdateSampleTimer()
    {
        var needed = !suspended && (sampledRules.Length != 0 || processTargets.Length != 0 || recovery.Count != 0);
        if (needed && !sampleTimer.IsRunning) sampleTimer.Start();
        if (!needed && sampleTimer.IsRunning) sampleTimer.Stop();
    }

    private void ResetTimeSampling()
    {
        lastMinute = default;
        lastTick = 0;
        idleMinutes = null;
    }

    private void ResetPcSampling()
    {
        ResetTimeSampling();
        runningPrograms = null;
        samplingRevision++;
    }

    private void SamplePcState()
    {
        if (suspended || disposed) return;
        if (sampledRules.Length != 0) SampleTimeAndInput();
        if (processTargets.Length != 0 && !readingProcesses) _ = SampleProcessesAsync();
        if (recovery.Count != 0) _ = DispatchRecoveryAsync();
    }

    private void SampleTimeAndInput()
    {
        var local = DateTime.Now;
        var minute = local.AddTicks(-(local.Ticks % TimeSpan.TicksPerMinute));
        var tick = Environment.TickCount64;
        var due = minute - lastMinute == TimeSpan.FromMinutes(1) && tick - lastTick <= 5_000;
        lastMinute = minute;
        lastTick = tick;
        var previousIdle = idleMinutes;
        idleMinutes = sampledRules.Any(rule => rule.Trigger is "idle" or "active") ? ReadIdleMinutes() : null;
        var day = DateOnly.FromDateTime(local);
        var triggered = new HashSet<Guid>();
        foreach (var rule in sampledRules)
        {
            if (rule.Trigger == "schedule" && due && rule.IsScheduledAt(local) &&
                (!lastScheduleDate.TryGetValue(rule.Id, out var last) || last != day))
            {
                lastScheduleDate[rule.Id] = day;
                triggered.Add(rule.Id);
            }
            if (rule.Trigger is not ("idle" or "active") || previousIdle is not { } before || idleMinutes is not { } after ||
                !AutomationRule.TryThreshold(rule.Argument, 240, out var threshold)) continue;
            if (rule.Trigger == "idle" ? AutomationRule.CrossedThreshold(before, after, threshold, rising: true) :
                before >= threshold && after < threshold)
                triggered.Add(rule.Id);
        }
        if (triggered.Count != 0) _ = engine.TriggerAsync(rule => triggered.Contains(rule.Id));
    }

    private async Task SampleProcessesAsync()
    {
        readingProcesses = true;
        var revision = samplingRevision;
        var paths = processTargets;
        try
        {
            var current = await Task.Run(() => FindRunningPrograms(paths, sessionId));
            if (disposed || suspended || revision != samplingRevision) return;
            var previous = runningPrograms;
            runningPrograms = current;
            if (previous is null || previous.SetEquals(current)) return;
            _ = engine.TriggerAsync(rule => rule.Trigger switch
            {
                "process-start" => current.Contains(rule.Argument) && !previous.Contains(rule.Argument),
                "process-exit" => previous.Contains(rule.Argument) && !current.Contains(rule.Argument),
                _ => false
            });
        }
        catch (Win32Exception) { /* Preserve the previous sample if process enumeration fails. */ }
        finally { readingProcesses = false; }
    }

    private static HashSet<string> FindRunningPrograms(string[] paths, int session)
    {
        var requested = paths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var running = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var names = paths.Select(Path.GetFileNameWithoutExtension).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (!names.Contains(process.ProcessName) || process.SessionId != session) continue;
                    var path = ProcessPath(process.Id);
                    if (requested.Contains(path)) running.Add(path);
                }
                catch (InvalidOperationException) { /* The process exited during sampling. */ }
                catch (Win32Exception) { /* The process cannot be inspected. */ }
            }
        }
        return running;
    }

    private static string ProcessPath(int id)
    {
        using var handle = OpenProcess(0x1000, false, id); // PROCESS_QUERY_LIMITED_INFORMATION
        if (handle.IsInvalid) return "";
        var path = new System.Text.StringBuilder(1025);
        var length = (uint)path.Capacity;
        return QueryFullProcessImageName(handle, 0, path, ref length) ? path.ToString() : "";
    }

    private static int CurrentSessionId()
    {
        using var process = Process.GetCurrentProcess();
        return process.SessionId;
    }

    private static double? ReadIdleMinutes()
    {
        var input = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref input)) return null;
        var elapsed = unchecked(GetTickCount() - input.Tick);
        return elapsed <= int.MaxValue ? elapsed / 60_000d : null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo { public uint Size, Tick; }

    [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LastInputInfo info);
    [DllImport("kernel32.dll")] private static extern uint GetTickCount();
    [DllImport("kernel32.dll")] private static extern Microsoft.Win32.SafeHandles.SafeProcessHandle OpenProcess(uint access, bool inherit, int processId);
    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(Microsoft.Win32.SafeHandles.SafeProcessHandle process, uint flags,
        System.Text.StringBuilder name, ref uint size);
}
