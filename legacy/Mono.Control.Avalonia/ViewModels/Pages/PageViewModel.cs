using CommunityToolkit.Mvvm.ComponentModel;
using Mono.Control.Services;
using Mono.Protocol;

namespace Mono.Control.ViewModels.Pages;

/// <summary>
/// 화면 하나의 상태와 명령. 셸이 스냅샷을 밀어 넣고, 페이지는 CoreSession에만 의존한다.
/// 페이지끼리는 서로 참조하지 않는다.
/// </summary>
public abstract partial class PageViewModel : ObservableObject
{
    protected readonly CoreSession Session;

    protected PageViewModel(CoreSession session) => Session = session;

    /// <summary>셸이 룸 스냅샷을 받을 때마다 호출한다.</summary>
    public virtual void ApplySnapshot(RoomSnapshot snapshot) { }

    /// <summary>명령 실패가 UI를 죽이지 않게 감싼다. 정책 위반 사유는 Core가 error로 돌려준다.</summary>
    protected async Task Safe(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) { StatusText = ex.Message; }
    }

    [ObservableProperty] private string _statusText = "";
}
