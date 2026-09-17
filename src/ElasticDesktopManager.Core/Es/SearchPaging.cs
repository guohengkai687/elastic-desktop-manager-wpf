namespace ElasticDesktopManager.Core.Es;

/// <summary>
/// 搜索页服务端分页的纯计算部分（与 UI 无关，便于单测）。
///
/// 背景：ES 的 <c>_search</c> 默认只返回 10 条，真正的翻页必须把
/// <c>from</c>/<c>size</c> 发到服务端（`from = (页号-1) × 每页条数`），
/// 客户端无法从这 10 条里"翻"出其余命中。JavaFX 原版（`ClusterSearchController.getQueryConditionsParms`
/// + `PagingControl`）就是这么做的，本类对齐它的行为与上限。
/// </summary>
public static class SearchPaging
{
    /// <summary>每页条数候选（与源项目 PagingControl 的 10/20/30/50/100 一致）。</summary>
    public static readonly int[] PageSizes = { 10, 20, 30, 50, 100 };

    public const int DefaultPageSize = 10;

    /// <summary>
    /// <c>from</c> 的上限。ES 的 <c>index.max_result_window</c> 默认 10000，
    /// 超过会直接返回 400（"Result window is too large"）。源项目取 5000，
    /// 这样即便每页 100 条，<c>from + size</c> 最多 5100 也仍在窗口内 —— 这里保持一致。
    /// </summary>
    public const int MaxFrom = 5000;

    /// <summary>页码 → 服务端 <c>from</c>。页码从 1 开始；非法输入一律收敛到 0，不抛异常。</summary>
    public static int FromOf(int pageNum, int pageSize)
    {
        if (pageNum <= 1) return 0;
        long from = (long)(pageNum - 1) * Math.Max(0, pageSize);
        return from > int.MaxValue ? int.MaxValue : (int)from;
    }

    /// <summary>总页数（至少 1 页：总命中 0 时界面也应显示"第 1 / 1 页"而不是"第 1 / 0 页"）。</summary>
    public static int TotalPages(long totalHits, int pageSize)
    {
        if (pageSize <= 0) return 1;
        if (totalHits <= 0) return 1;
        long pages = (totalHits + pageSize - 1) / pageSize;
        return pages > int.MaxValue ? int.MaxValue : (int)pages;
    }

    /// <summary>把页码收敛到 [1, totalPages]（用于"前往 N 页"输入超界与结果变少后的回退）。</summary>
    public static int ClampPage(int pageNum, int totalPages)
    {
        if (pageNum < 1) return 1;
        return pageNum > totalPages ? totalPages : pageNum;
    }

    /// <summary>该页的起始偏移是否已超出 <see cref="MaxFrom"/>（超出时应在发请求前就给出可读错误）。</summary>
    public static bool ExceedsWindow(int pageNum, int pageSize) => FromOf(pageNum, pageSize) > MaxFrom;

    /// <summary>
    /// 是否还有下一页。注意 <paramref name="totalHitsIsLowerBound"/> 为真时
    /// ES 只给了命中数的下限（`track_total_hits` 关闭时为 10000），此时按"可能还有"处理，
    /// 不能因为 <c>from + size >= total</c> 就禁用下一页。
    /// </summary>
    public static bool HasNext(int pageNum, int pageSize, long totalHits, bool totalHitsIsLowerBound)
    {
        if (ExceedsWindow(pageNum + 1, pageSize)) return false;
        if (totalHitsIsLowerBound) return pageSize > 0;
        return (long)pageNum * Math.Max(0, pageSize) < totalHits;
    }
}
