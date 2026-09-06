namespace Blog.Application.Common
{
    /// <summary>分页结果载体，与前端分页器字段对齐</summary>
    public class PagedResult<T>
    {
        public IReadOnlyList<T> Items { get; init; } = [];
        public int Page { get; init; }
        public int PageSize { get; init; }
        public int Total { get; init; }
        public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(Total / (double)PageSize);

        public static PagedResult<T> Empty(int page, int pageSize) =>
            new() { Page = page, PageSize = pageSize, Total = 0, Items = [] };

        public static PagedResult<T> Create(IReadOnlyList<T> items, int page, int pageSize, int total) =>
            new() { Items = items, Page = page, PageSize = pageSize, Total = total };
    }
}
