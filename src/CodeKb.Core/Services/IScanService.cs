using CodeKb.Contracts;

namespace CodeKb.Core.Services;

public interface IScanService
{
    Task<ScanResult> ScanAsync(ScanRequest request, CancellationToken ct);
}

public interface ISearchService
{
    Task<IReadOnlyList<SearchHit>> AskAsync(SearchRequest request, CancellationToken ct);
}
