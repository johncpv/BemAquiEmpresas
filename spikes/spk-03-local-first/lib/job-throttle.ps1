# Roda um comando dentro de um Job Object do Windows com CPU limitada (afinidade de 2 núcleos lógicos e teto de uso).
# Diferente do throttling do CDP, isto desacelera TODOS os processos do Chromium (página, workers e o processo
# que guarda o IndexedDB). Simulação de máquina fraca; não substitui o aparelho real.
# Uso: powershell -File lib\job-throttle.ps1 -Slowdown 4 -- node run-bench.mjs --rates=1 --label=job4x
param(
  [Parameter(Mandatory = $true)][double]$Slowdown,
  [int]$Cores = 2,
  [Parameter(ValueFromRemainingArguments = $true)][string[]]$Command
)
$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class Job {
  [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr CreateJobObject(IntPtr a, string n);
  [DllImport("kernel32.dll")] public static extern bool SetInformationJobObject(IntPtr j, int c, IntPtr i, uint l);
  [DllImport("kernel32.dll")] public static extern bool AssignProcessToJobObject(IntPtr j, IntPtr p);
  [StructLayout(LayoutKind.Sequential)] public struct BASIC {
    public long PerProcessUserTimeLimit; public long PerJobUserTimeLimit; public uint LimitFlags;
    public UIntPtr MinimumWorkingSetSize; public UIntPtr MaximumWorkingSetSize; public uint ActiveProcessLimit;
    public UIntPtr Affinity; public uint PriorityClass; public uint SchedulingClass; }
  [StructLayout(LayoutKind.Sequential)] public struct IOC { public ulong a, b, c, d, e, f; }
  [StructLayout(LayoutKind.Sequential)] public struct EXT {
    public BASIC Basic; public IOC Io; public UIntPtr ProcessMemoryLimit; public UIntPtr JobMemoryLimit;
    public UIntPtr PeakProcessMemoryUsed; public UIntPtr PeakJobMemoryUsed; }
  [StructLayout(LayoutKind.Sequential)] public struct CPU { public uint ControlFlags; public uint CpuRate; }
  public static IntPtr Make(ulong affinity, uint cpuRate) {
    IntPtr j = CreateJobObject(IntPtr.Zero, null);
    var e = new EXT();
    e.Basic.LimitFlags = 0x10 | 0x2000; // AFFINITY | KILL_ON_JOB_CLOSE
    e.Basic.Affinity = new UIntPtr(affinity);
    IntPtr p = Marshal.AllocHGlobal(Marshal.SizeOf(e)); Marshal.StructureToPtr(e, p, false);
    if (!SetInformationJobObject(j, 9, p, (uint)Marshal.SizeOf(e))) throw new Exception("affinity " + Marshal.GetLastWin32Error());
    var c = new CPU(); c.ControlFlags = 0x1 | 0x4; c.CpuRate = cpuRate; // ENABLE | HARD_CAP; unidade = 1/100 de %
    IntPtr q = Marshal.AllocHGlobal(Marshal.SizeOf(c)); Marshal.StructureToPtr(c, q, false);
    if (!SetInformationJobObject(j, 15, q, (uint)Marshal.SizeOf(c))) throw new Exception("cpu rate " + Marshal.GetLastWin32Error());
    return j;
  }
}
'@
$logical = [Environment]::ProcessorCount
$affinity = [uint64]([math]::Pow(2, $Cores) - 1)
# teto = (núcleos permitidos / núcleos da máquina) / fator de lentidão
$rate = [uint32][math]::Round(10000 * ($Cores / $logical) / $Slowdown)
Write-Host "job: $Cores núcleos lógicos de $logical, teto $($rate/100)% da máquina (lentidão ${Slowdown}x)"
$job = [Job]::Make($affinity, $rate)
$exe = $Command[0]
$args2 = if ($Command.Length -gt 1) { $Command[1..($Command.Length - 1)] } else { @() }
$p = Start-Process -FilePath $exe -ArgumentList $args2 -NoNewWindow -PassThru
if (-not [Job]::AssignProcessToJobObject($job, $p.Handle)) { throw "assign falhou" }
$p.WaitForExit()
exit $p.ExitCode
