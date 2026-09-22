using DuetDiagram.App.Services;
using DuetDiagram.App.ViewModels;
using DuetDiagram.Render;

namespace DuetDiagram.App.Interaction;

/// <summary>
/// 把画布的指针手势翻译成"选中 + 预览 + 提交"。
/// </summary>
/// <remarks>
/// <para>
/// 画布只认指针事件，不认文档；文档那一侧（选中谁、固定位置写哪）在 <see cref="DiagramSession"/>。
/// 这一层是两者的桥：按下时问会话"这个点能不能拖"，能拖就开预览；
/// 移动时把偏移喂给画布让它只改屏幕上的临时位置；松手时让会话一次性把结果落定。
/// </para>
/// <para>
/// **拖动过程中不碰文档、不碰布局。** 预览只是画布把选中节点的绘制指令整体挪一个偏移，
/// 见 <see cref="CanvasViewModel.BeginDrag"/>。结构是松手那一刻由会话按"一条操作"交出去的，
/// 这样撤销栈里一次拖动就是一步，而不是被几百条逐像素的命令塞满。
/// </para>
/// </remarks>
public sealed class DragController
{
    private readonly DiagramSession _session;
    private readonly CanvasViewModel _model;
    private bool _active;

    public DragController(DiagramSession session, CanvasViewModel model)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(model);

        _session = session;
        _model = model;
    }

    /// <summary>这一拖是否真的开始了（按下的不是节点时为空）。</summary>
    public bool IsActive => _active;

    /// <summary>
    /// 按下。返回是否进入了拖拽（空表示按在空白或边上，应当走普通选中）。
    /// </summary>
    public bool Press(string? hitId, bool additive, DrawPoint startDoc)
    {
        var preview = _session.BeginDrag(hitId, additive, startDoc);

        if (preview is null)
        {
            return false;
        }

        _model.BeginDrag(preview.NodeIds, preview.EdgeIds);
        _active = true;

        return true;
    }

    /// <summary>移动。只在拖拽进行中有效。</summary>
    public void Move(DrawPoint currentDoc)
    {
        if (!_active)
        {
            return;
        }

        var delta = _session.UpdateDrag(currentDoc);

        _model.UpdateDrag(delta);
    }

    /// <summary>
    /// 松手。把这一拖作为一条操作落定（固定位置，或者落在一个兄弟节点上时是一条层内次序）。
    /// </summary>
    /// <param name="dropTargetId">
    /// 松手时指针底下的节点。落在空白处时为空——那一档落定的是绝对位置。
    /// </param>
    public void Release(string? dropTargetId = null)
    {
        if (!_active)
        {
            return;
        }

        _session.CommitDrag(dropTargetId);
        _model.EndDrag();
        _active = false;
    }

    /// <summary>
    /// 手势被打断（指针被抢走、窗口失焦）。不落定，直接回到原始位置。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="Release"/> 相反：松手是"用户确认了这一拖"，打断是"这一拖作废"。
    /// 作废时若还把位置写进去，用户一次意外的失焦就会把节点挪到一个他没打算去的地方。
    /// </remarks>
    public void Cancel()
    {
        if (!_active)
        {
            return;
        }

        _session.CancelDrag();
        _model.EndDrag();
        _active = false;
    }
}
