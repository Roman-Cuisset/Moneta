param(
    [string]$ExePath = (Join-Path $PSScriptRoot '../src/Moneta.App/bin/Release/net10.0-windows/Moneta.App.exe'),
    [switch]$Demo,
    [ValidateRange(30,600)][int]$Seconds = 60,
    [ValidateRange(5,120)][int]$WarmupSeconds = 15,
    [string]$OutputPath = (Join-Path ([IO.Path]::GetTempPath()) ('Moneta-idle-' + [guid]::NewGuid() + '.json'))
)
# Starts only its own empty/demo instance. No financial input, network flag or preferences are changed.
$ErrorActionPreference = 'Stop'
if (Test-Path -LiteralPath $OutputPath) { throw 'Diagnostic destination already exists; choose a new path.' }
$ExePath = [IO.Path]::GetFullPath($ExePath)
if (-not (Test-Path -LiteralPath $ExePath)) { throw 'Build Moneta in Release before measuring.' }
Add-Type -AssemblyName UIAutomationClient
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class MonetaIdleNative {
 [DllImport("kernel32.dll")] public static extern IntPtr OpenThread(uint a,bool i,int id);
 [DllImport("kernel32.dll")] public static extern int GetThreadDescription(IntPtr h,out IntPtr p);
 [DllImport("kernel32.dll")] public static extern IntPtr LocalFree(IntPtr p);
 [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
 [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h,int command);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h,uint m,IntPtr w,IntPtr l);
}
'@
function Read-Threads($process) {
    $result = @{}
    $process.Refresh()
    foreach ($thread in $process.Threads) {
        try {
            $name = ''
            $handle = [MonetaIdleNative]::OpenThread(0x800, $false, $thread.Id)
            if ($handle -ne [IntPtr]::Zero) {
                try {
                    $text = [IntPtr]::Zero
                    $status = [MonetaIdleNative]::GetThreadDescription($handle, [ref]$text)
                    if ($status -ge 0 -and $text -ne [IntPtr]::Zero) {
                        try { $name = [Runtime.InteropServices.Marshal]::PtrToStringUni($text) }
                        finally { [MonetaIdleNative]::LocalFree($text) | Out-Null }
                    }
                } finally { [MonetaIdleNative]::CloseHandle($handle) | Out-Null }
            }
            $result[$thread.Id] = @{ Name = $name; CpuMs = $thread.TotalProcessorTime.TotalMilliseconds }
        } catch { } # A thread can exit during enumeration; process CPU remains authoritative.
    }
    return $result
}
function Find-Id($root, [string]$id) {
    $root.FindFirst([Windows.Automation.TreeScope]::Descendants,
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty, $id))
}
$process = $null
try {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $process = [Diagnostics.Process]::Start([Diagnostics.ProcessStartInfo]@{ FileName = $ExePath; UseShellExecute = $false })
    $root = $null; $visible = $null
    do {
        $process.Refresh()
        if ($process.HasExited) { throw 'The owned instance exited before becoming interactive.' }
        if ($process.MainWindowHandle -ne [IntPtr]::Zero) {
            if ($null -eq $visible -and [MonetaIdleNative]::IsWindowVisible($process.MainWindowHandle)) { $visible = $clock.Elapsed.TotalMilliseconds }
            try {
                $candidate = [Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
                if (Find-Id $candidate 'MainMenu') { $root = $candidate }
            } catch { }
        }
        if ($null -eq $root) { Start-Sleep -Milliseconds 50 }
    } while ($null -eq $root -and $clock.Elapsed.TotalSeconds -lt 25)
    if ($null -eq $root) { throw 'No interactive main window within 25 seconds.' }
    $interactive = $clock.Elapsed.TotalMilliseconds
    if ($Demo) {
        $fileMenu = $root.FindFirst([Windows.Automation.TreeScope]::Descendants,
            [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty, 'Fichier'))
        $fileMenu.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
        (Find-Id $root 'DemoDocumentCommand').GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
        $ready = $false
        do {
            Start-Sleep -Milliseconds 100
            $ready = (Find-Id $root 'DevelopmentNotice').Current.Name -like '*fictives*'
        } while (-not $ready -and $clock.Elapsed.TotalSeconds -lt 45)
        if (-not $ready) { throw 'The demonstration did not load.' }
    }
    $notice = (Find-Id $root 'DevelopmentNotice').Current.Name
    $readyMs = $clock.Elapsed.TotalMilliseconds
    $dpi = [MonetaIdleNative]::GetDpiForWindow($process.MainWindowHandle)
    $samples = @()
    for ($index = 0; $index -lt [math]::Ceiling($Seconds / 5); $index++) {
        $process.Refresh(); $cpu = $process.TotalProcessorTime.TotalMilliseconds
        $before = Read-Threads $process
        $timer = [Diagnostics.Stopwatch]::StartNew()
        Start-Sleep -Seconds 5 # No UIA calls during the measured interval.
        $process.Refresh(); $delta = $process.TotalProcessorTime.TotalMilliseconds - $cpu
        $after = Read-Threads $process; $timer.Stop()
        $hot = foreach ($id in $after.Keys) {
            if ($before.ContainsKey($id)) {
                [pscustomobject]@{ Id = $id; Name = $after[$id].Name; CpuMs = [math]::Max(0, $after[$id].CpuMs - $before[$id].CpuMs) }
            }
        }
        $samples += [pscustomobject]@{ EndSeconds = ($index + 1) * 5; Warmup = ($index * 5 -lt $WarmupSeconds);
            Seconds = $timer.Elapsed.TotalSeconds; CpuOneCorePercent = 100 * $delta / $timer.Elapsed.TotalMilliseconds;
            PrivateBytes = $process.PrivateMemorySize64; ResidentBytes = $process.WorkingSet64;
            Threads = @($hot | Sort-Object CpuMs -Descending | Select-Object -First 8) }
    }
    [MonetaIdleNative]::ShowWindow($process.MainWindowHandle, 6) | Out-Null
    Start-Sleep -Seconds 2
    $process.Refresh(); $cpu = $process.TotalProcessorTime.TotalMilliseconds
    $timer = [Diagnostics.Stopwatch]::StartNew(); Start-Sleep -Seconds 10
    $process.Refresh(); $timer.Stop()
    $minimized = 100 * ($process.TotalProcessorTime.TotalMilliseconds - $cpu) / $timer.Elapsed.TotalMilliseconds
    [pscustomobject]@{ Exe = $ExePath; Pid = $process.Id; Demo = [bool]$Demo; Notice = $notice; Dpi = $dpi;
        VisibleHwndMs = $visible; InteractiveUiaMs = $interactive; FolderReadyMs = $readyMs;
        LogicalProcessors = [Environment]::ProcessorCount; Samples = $samples; MinimizedCpuOneCorePercent = $minimized;
        Limits = 'Visible HWND is not first composed pixel. Warm caches, short intervals, exited threads may not be attributed.'
    } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputPath -Encoding UTF8
    Write-Output ('Report: ' + $OutputPath)
} finally {
    if ($null -ne $process -and -not $process.HasExited) {
        [MonetaIdleNative]::ShowWindow($process.MainWindowHandle, 9) | Out-Null
        [MonetaIdleNative]::PostMessage($process.MainWindowHandle, 0x10, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
        if (-not $process.WaitForExit(20000)) { Write-Warning ('Own PID ' + $process.Id + ' remains open; it was not killed.') }
    }
}
