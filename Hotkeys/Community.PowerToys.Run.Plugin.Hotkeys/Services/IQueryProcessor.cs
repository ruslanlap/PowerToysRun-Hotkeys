#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Wox.Plugin;

namespace Community.PowerToys.Run.Plugin.Hotkeys.Services
{
    public interface IQueryProcessor
    {
        Task<List<Result>> ProcessQueryAsync(Query query, string iconPath, CancellationToken cancellationToken = default);
    }

    public enum FilterType
    {
        None,
        App,
        Category,
        Keyword,
        Source
    }

    public readonly record struct ParsedQuery(
        string Command,
        string? AppFilter,
        string? SearchTerm,
        FilterType FilterType = FilterType.None,
        string? FilterValue = null);
}