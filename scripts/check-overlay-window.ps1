# 오버레이 창이 실제로 떴는지 확인한다.
#
# 프로세스가 살아 있는 것만 보면 안 된다. XAML 로드가 실패하면(없는 StaticResource 를 참조하는
# 등) 프로세스는 남고 창만 안 뜨는데, 그 상태를 "실행 확인"으로 착각한 적이 있다. WPF 바인딩과
# 리소스 참조는 컴파일 때 검증되지 않으므로 빌드 성공도 근거가 되지 못한다.
#
#   dotnet build src/SephPlanner.Overlay -c Release
#   src/SephPlanner.Overlay/bin/Release/net10.0-windows/SephPlanner.Overlay.exe --preview
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts/check-overlay-window.ps1

# 프로세스가 아니라 '보이는 창'이 있는지 센다. 프로세스만 보면 XAML 로드 실패를 놓친다.
Add-Type @"
using System;
using System.Text;
using System.Runtime.InteropServices;
public class Win {
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc p, IntPtr l);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern int GetWindowThreadProcessId(IntPtr h, out int pid);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }
}
"@
$target = (Get-Process SephPlanner.Overlay -ErrorAction SilentlyContinue | ForEach-Object Id)
if (-not $target) { Write-Output "프로세스 없음"; exit 1 }
$found = 0
[Win]::EnumWindows({ param($h,$l)
  $pid2 = 0
  [void][Win]::GetWindowThreadProcessId($h, [ref]$pid2)
  if ($target -contains $pid2 -and [Win]::IsWindowVisible($h)) {
    $r = New-Object Win+RECT
    [void][Win]::GetWindowRect($h, [ref]$r)
    $sb = New-Object System.Text.StringBuilder 256
    [void][Win]::GetWindowTextW($h, $sb, 256)
    $w = $r.R - $r.L; $ht = $r.B - $r.T
    if ($w -gt 0 -and $ht -gt 0) {
      Write-Output ("보이는 창: pid=$pid2 위치=($($r.L),$($r.T)) 크기=${w}x${ht} 제목='$($sb.ToString())'")
      $script:found++
    }
  }
  return $true
}, [IntPtr]::Zero) | Out-Null
if ($script:found -eq 0) { Write-Output "보이는 창 없음 - 프로세스는 있으나 창이 안 떴다" }
