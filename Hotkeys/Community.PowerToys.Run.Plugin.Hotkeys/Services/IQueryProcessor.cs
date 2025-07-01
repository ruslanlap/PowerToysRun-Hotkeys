// ===== 4. IQueryProcessor.cs =====
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

    public struct ParsedQuery
    {
        public string Command { get; }
        public string AppFilter { get; }
        public string SearchTerm { get; }

        public ParsedQuery(string command, string appFilter, string searchTerm)
        {
            Command = command;
            AppFilter = appFilter;
            SearchTerm = searchTerm;
        }
    }
}