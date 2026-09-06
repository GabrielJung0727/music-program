using System.Runtime.InteropServices;

namespace Mono.Output;

/// <summary>
/// 실시간 오디오 렌더 스레드를 윈도우 멀티미디어 클래스 스케줄러(MMCSS)에 등록한다.
/// 등록하지 않으면 DPC/ISR 인터럽트에 밀려 렌더 스레드가 늦게 깨어나고, 그 지연이
/// 그대로 버퍼 언더런(딸깍 소리)이 된다.
/// </summary>
public static class Mmcss
{
    private const int AvrtPriorityCritical = 2;

    [DllImport("avrt.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr AvSetMmThreadCharacteristicsW(string taskName, ref uint taskIndex);

    [DllImport("avrt.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AvSetMmThreadPriority(IntPtr handle, int priority);

    [DllImport("avrt.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AvRevertMmThreadCharacteristics(IntPtr handle);

    /// <summary>
    /// 현재 스레드를 "Pro Audio" 클래스로 올린다. 실패해도 재생은 계속돼야 하므로
    /// 예외를 던지지 않고 실패 사유만 돌려준다.
    /// </summary>
    public static IntPtr Register(out string status)
    {
        try
        {
            uint index = 0;
            var handle = AvSetMmThreadCharacteristicsW("Pro Audio", ref index);
            if (handle == IntPtr.Zero)
            {
                status = $"MMCSS 등록 실패 (win32={Marshal.GetLastWin32Error()})";
                return IntPtr.Zero;
            }

            var raised = AvSetMmThreadPriority(handle, AvrtPriorityCritical);
            status = raised ? "MMCSS Pro Audio · priority=critical" : "MMCSS Pro Audio · priority 상향 실패";
            return handle;
        }
        catch (DllNotFoundException)
        {
            status = "MMCSS 사용 불가 (avrt.dll 없음)";
            return IntPtr.Zero;
        }
        catch (EntryPointNotFoundException)
        {
            status = "MMCSS 사용 불가 (진입점 없음)";
            return IntPtr.Zero;
        }
    }

    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static extern uint TimeBeginPeriod(uint ms);

    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    private static extern uint TimeEndPeriod(uint ms);

    /// <summary>
    /// 시스템 타이머 해상도를 1ms 로 올린다. 기본값(~15.6ms)에서는 렌더 루프가
    /// 청크 하나보다 늦게 깨어나 버퍼가 바닥을 친다.
    /// </summary>
    public static bool RaiseTimerResolution()
    {
        try { return TimeBeginPeriod(1) == 0; }
        catch { return false; }
    }

    public static void RestoreTimerResolution()
    {
        try { TimeEndPeriod(1); } catch { /* 프로세스 종료로 정리된다 */ }
    }

    public static void Revert(IntPtr handle)
    {
        if (handle == IntPtr.Zero) return;
        try { AvRevertMmThreadCharacteristics(handle); }
        catch { /* 되돌리지 못해도 프로세스 종료로 정리된다 */ }
    }
}
