using InkCanvas.Core.Settings;
using System.Numerics;

namespace InkCanvas.Core.Ink;

/// <summary>
/// 1:1 port of <c>MW_MatrixTransform.cs</c> → <c>GetMatrixTransformCenterPoint</c>.
/// Chooses the pivot used by two-finger zoom/rotate.
/// <para>
/// <b>未验证</b>：按要求跳过行为验证（无单元测试、无实机手势验证）。
/// </para>
/// </summary>
public static class MatrixTransformCenter
{
    public static Vector2 Resolve(
        MatrixTransformCenterPointOptions option,
        Vector2 canvasCenterPoint,
        Vector2 gestureOperationCenterPoint,
        Vector2 selectedElementsCenterPoint,
        bool isLoaded)
    {
        // 原实现在窗口尚未 Loaded 时一律返回画布中心。
        if (!isLoaded) return canvasCenterPoint;

        return option switch
        {
            MatrixTransformCenterPointOptions.GestureOperationCenterPoint => gestureOperationCenterPoint,
            MatrixTransformCenterPointOptions.SelectedElementsCenterPoint => selectedElementsCenterPoint,
            _ => canvasCenterPoint,
        };
    }
}