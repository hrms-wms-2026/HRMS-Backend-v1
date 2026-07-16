namespace ONEVO.Application.Common.Models;

public class PagedRequest
{
    private int _pageSize = 20;

    public int PageNumber { get; set; } = 1;

    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = value > 100 ? 100 : value < 1 ? 1 : value;
    }

    public string? SortBy { get; set; }
    public string SortDirection { get; set; } = "asc";
}
