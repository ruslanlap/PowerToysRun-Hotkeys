#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Community.PowerToys.Run.Plugin.Hotkeys.Models;
using ManagedCommon;
using PowerToysRun.ShortcutFinder.Plugin.Helpers;
using Wox.Plugin;

namespace Community.PowerToys.Run.Plugin.Hotkeys.Services
{
    public class QueryProcessor : IQueryProcessor
    {
        private readonly IShortcutRepository _repository;
        private readonly ILogger _logger;
        private readonly PluginInitContext _context;

        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, CacheEntry> _searchCache = new();
        private readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(5);

        private readonly record struct CacheEntry(List<Result> Results, DateTime Timestamp);

        public QueryProcessor(IShortcutRepository repository, ILogger logger, PluginInitContext context)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public async Task<List<Result>> ProcessQueryAsync(Query query, string iconPath, CancellationToken cancellationToken = default)
        {
            var search = query.Search?.Trim();

            if (string.IsNullOrWhiteSpace(search))
            {
                return GetHelpResults(iconPath);
            }

            var parsedQuery = ParseQuery(search);

            try
            {
                return parsedQuery.Command.ToLowerInvariant() switch
                {
                    "list" => await GetAppShortcutsAsync(parsedQuery.AppFilter ?? parsedQuery.SearchTerm ?? string.Empty, iconPath, cancellationToken),
                    "apps" => await GetAvailableAppsAsync(iconPath, cancellationToken),
                    _ => await SearchShortcutsAsync(parsedQuery.SearchTerm, parsedQuery.AppFilter, search, iconPath, parsedQuery.FilterType, parsedQuery.FilterValue, cancellationToken)
                };
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Error processing query '{search}': {ex.Message}", ex);
                return GetErrorResults(search, iconPath, ex.Message);
            }
        }

        private ParsedQuery ParseQuery(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return new ParsedQuery("search", null, string.Empty);

            query = query.Trim();

            if (query.Equals("apps", StringComparison.OrdinalIgnoreCase))
                return new ParsedQuery("apps", null, null);

            if (query.StartsWith("list:", StringComparison.OrdinalIgnoreCase))
            {
                var app = query[5..].Trim();
                return new ParsedQuery("list", app, app);
            }

            // Handle special filter prefixes: app:, category:, keyword:, source:
            var filterPrefixes = new[] { "app:", "category:", "keyword:", "source:" };
            foreach (var prefix in filterPrefixes)
            {
                if (query.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    var filterType = prefix[..^1] switch
                    {
                        var s when s.Equals("app", StringComparison.OrdinalIgnoreCase) => FilterType.App,
                        var s when s.Equals("category", StringComparison.OrdinalIgnoreCase) => FilterType.Category,
                        var s when s.Equals("keyword", StringComparison.OrdinalIgnoreCase) => FilterType.Keyword,
                        var s when s.Equals("source", StringComparison.OrdinalIgnoreCase) => FilterType.Source,
                        _ => FilterType.None
                    };

                    var parts = query[prefix.Length..].Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                    var filterValue = parts.Length > 0 ? parts[0].Trim() : string.Empty;
                    var searchTerm = parts.Length > 1 ? parts[1].Trim() : string.Empty;

                    // If only filter value, treat as list command for that filter
                    if (string.IsNullOrWhiteSpace(searchTerm) && filterType == FilterType.App)
                    {
                        return new ParsedQuery("list", filterValue, filterValue, filterType, filterValue);
                    }

                    return new ParsedQuery("search", null, searchTerm, filterType, filterValue);
                }
            }

            // Handle /appname at the beginning
            if (query.StartsWith('/'))
            {
                var parts = query[1..].Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 1)
                {
                    // Just "/appname" - list command
                    return new ParsedQuery("list", parts[0].Trim(), parts[0].Trim(), FilterType.App, parts[0].Trim());
                }
                else
                {
                    // "/appname searchterm" - search with filter
                    return new ParsedQuery("search", parts[0].Trim(), parts[1].Trim(), FilterType.App, parts[0].Trim());
                }
            }

            // Handle /appname anywhere in the query
            var slashIndex = query.IndexOf('/');
            if (slashIndex > 0)
            {
                var beforeSlash = query[..slashIndex].Trim();
                var afterSlash = query[(slashIndex + 1)..].Trim();

                // "searchterm /appname" format
                return new ParsedQuery("search", afterSlash, beforeSlash, FilterType.App, afterSlash);
            }

            // No slash found - regular search
            return new ParsedQuery("search", null, query);
        }

        private async Task<List<Result>> SearchShortcutsAsync(string? searchTerm, string? appFilter, string originalQuery, string iconPath, FilterType filterType, string? filterValue, CancellationToken cancellationToken)
        {
            var cacheKey = $"{searchTerm}|{appFilter}|{filterType}|{filterValue}";
            if (_searchCache.TryGetValue(cacheKey, out var cached) &&
                DateTime.UtcNow - cached.Timestamp < _cacheDuration)
            {
                return cached.Results;
            }

            var shortcuts = await _repository.SearchShortcutsAsync(searchTerm ?? string.Empty, appFilter, filterType, filterValue, cancellationToken);
            var results = shortcuts.Select(shortcut => CreateShortcutResult(shortcut, searchTerm, appFilter, originalQuery, iconPath)).ToList();

            if (results.Count == 0)
            {
                results.Add(CreateNoResultsFound(searchTerm, appFilter, originalQuery, iconPath));
            }

            var ordered = results.OrderByDescending(r => r.Score).ToList();
            _searchCache[cacheKey] = new CacheEntry(ordered, DateTime.UtcNow);
            CleanCache();
            return ordered;
        }

        private async Task<List<Result>> GetAvailableAppsAsync(string iconPath, CancellationToken cancellationToken)
        {
            var shortcutsBySource = await _repository.GetShortcutsBySourceAsync(cancellationToken);
            var results = new List<Result>();

            foreach (var app in shortcutsBySource.Keys.OrderBy(k => k))
            {
                var count = shortcutsBySource[app].Count;
                results.Add(new Result
                {
                    IcoPath = iconPath,
                    Title = $"{app} ({count} shortcuts)",
                    SubTitle = $"Click to see all {app} shortcuts",
                    Action = _ =>
                    {
                        _context.API.ChangeQuery($"hk list:{app}", true);
                        return false;
                    },
                    ContextData = app
                });
            }

            return results;
        }

        private async Task<List<Result>> GetAppShortcutsAsync(string appName, string iconPath, CancellationToken cancellationToken)
        {
            var shortcutsBySource = await _repository.GetShortcutsBySourceAsync(cancellationToken);
            var results = new List<Result>();

            var matchingApps = shortcutsBySource.Keys
                .Where(k => k.ToLowerInvariant().Contains(appName.ToLowerInvariant()))
                .ToList();

            foreach (var app in matchingApps)
            {
                var shortcuts = shortcutsBySource[app];

                foreach (var shortcut in shortcuts.OrderBy(s => s.Category).ThenBy(s => s.Description))
                {
                    results.Add(new Result
                    {
                        IcoPath = iconPath,
                        Title = FormatTitle(shortcut),
                        SubTitle = FormatSubTitle(shortcut),
                        ToolTipData = new ToolTipData(shortcut.Description, $"{shortcut.Shortcut}\n\nCategory: {shortcut.Category}"),
                        Action = _ => CopyShortcutAction(shortcut),
                        ContextData = shortcut
                    });
                }
            }

            if (results.Count == 0)
            {
                results.Add(new Result
                {
                    IcoPath = iconPath,
                    Title = $"No app found matching '{appName}'",
                    SubTitle = "Type 'apps' to see all available applications",
                    Action = _ =>
                    {
                        _context.API.ChangeQuery("hk apps", true);
                        return false;
                    }
                });
            }

            return results;
        }

        private List<Result> GetHelpResults(string iconPath) => new()
        {
            new Result
            {
                IcoPath = iconPath,
                Title = "Search hotkeys by keyword",
                SubTitle = "Example: 'copy', 'paste', 'ctrl+c'",
                Action = _ => true
            },
            new Result
            {
                IcoPath = iconPath,
                Title = "List all available apps",
                SubTitle = "Type: apps",
                Action = _ =>
                {
                    _context.API.ChangeQuery("hk apps", true);
                    return false;
                }
            },
            new Result
            {
                IcoPath = iconPath,
                Title = "List shortcuts for specific app",
                SubTitle = "Type: list:appname (e.g., 'list:chrome')",
                Action = _ => true
            }
        };

        private List<Result> GetErrorResults(string query, string iconPath, string errorMessage) => new()
        {
            new Result
            {
                IcoPath = iconPath,
                Title = "Error processing query",
                SubTitle = $"Query: '{query}' - {errorMessage}",
                Action = _ => true
            }
        };

        private bool CopyShortcutAction(ShortcutInfo shortcut)
        {
            try
            {
                ClipboardHelper.SetClipboard(shortcut.Shortcut);
                _context.API.ShowMsg("Hotkey Copied", $"'{shortcut.Shortcut}' copied to clipboard", string.Empty);
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Failed to copy shortcut: {ex.Message}", ex);
                _context.API.ShowMsg("Error", "Failed to copy shortcut to clipboard", string.Empty);
                return false;
            }
        }

        private Result CreateShortcutResult(ShortcutInfo shortcut, string? searchTerm, string? appFilter, string originalQuery, string iconPath)
        {
            var score = CalculateScore(shortcut, searchTerm ?? string.Empty, appFilter);

            return new Result
            {
                QueryTextDisplay = originalQuery,
                IcoPath = iconPath,
                Title = FormatTitle(shortcut),
                SubTitle = FormatSubTitle(shortcut, appFilter),
                ToolTipData = new ToolTipData(shortcut.Description, $"{shortcut.Shortcut}\n\nSource: {shortcut.Source}\nCategory: {shortcut.Category}"),
                Score = score,
                Action = _ => CopyShortcutAction(shortcut),
                ContextData = shortcut
            };
        }

        private Result CreateNoResultsFound(string? searchTerm, string? appFilter, string originalQuery, string iconPath)
        {
            string message = !string.IsNullOrWhiteSpace(appFilter)
                ? $"No hotkeys found for '{searchTerm}' in {appFilter}"
                : $"No hotkeys found for '{searchTerm}'";

            string suggestion = !string.IsNullOrWhiteSpace(appFilter)
                ? $"Try removing /{appFilter} filter or check app name"
                : "Try: 'apps' to see available apps, or 'search /appname' to filter by app";

            return new Result
            {
                QueryTextDisplay = originalQuery,
                IcoPath = iconPath,
                Title = message,
                SubTitle = suggestion,
                Action = _ => true,
                ContextData = originalQuery
            };
        }

        private static string FormatTitle(ShortcutInfo shortcut)
        {
            return $"{shortcut.Shortcut} - {shortcut.Description}";
        }

        private static string FormatSubTitle(ShortcutInfo shortcut, string? appFilter = null)
        {
            string subtitle = $"{shortcut.Source} | {shortcut.Category ?? "General"}";

            if (!string.IsNullOrWhiteSpace(appFilter))
            {
                subtitle = $"📍 {subtitle} (filtered by {appFilter})";
            }

            return subtitle;
        }

        private static int CalculateScore(ShortcutInfo shortcut, string query, string? appFilter = null)
        {
            if (string.IsNullOrWhiteSpace(query)) return 0;

            int score = 0;
            string q = query.ToLowerInvariant();

            if (!string.IsNullOrWhiteSpace(appFilter))
            {
                string filter = appFilter.ToLowerInvariant();
                if (shortcut.Source?.ToLowerInvariant() == filter)
                    score += 200;
                else if (shortcut.Source?.ToLowerInvariant().Contains(filter) == true)
                    score += 100;
            }

            if (shortcut.Shortcut?.ToLowerInvariant() == q)
                score += 1000;
            else if (shortcut.Shortcut?.ToLowerInvariant().Contains(q) == true)
                score += 800;

            if (shortcut.Description?.ToLowerInvariant() == q)
                score += 900;
            else if (shortcut.Description?.ToLowerInvariant().StartsWith(q) == true)
                score += 700;
            else if (shortcut.Description?.ToLowerInvariant().Contains(q) == true)
                score += 500;

            if (shortcut.Keywords?.Any(k => k.ToLowerInvariant() == q) == true)
                score += 600;
            else if (shortcut.Keywords?.Any(k => k.ToLowerInvariant().Contains(q)) == true)
                score += 300;

            var popularApps = new[] { "chrome", "firefox", "vscode", "word", "excel", "windows", "photoshop" };
            if (popularApps.Contains(shortcut.Source?.ToLowerInvariant()))
                score += 50;

            return score;
        }
        private void CleanCache()
        {
            foreach (var entry in _searchCache.ToArray())
            {
                if (DateTime.UtcNow - entry.Value.Timestamp > _cacheDuration)
                {
                    _searchCache.TryRemove(entry.Key, out _);
                }
            }

            if (_searchCache.Count > 100)
            {
                var oldest = _searchCache.OrderBy(kvp => kvp.Value.Timestamp).First();
                _searchCache.TryRemove(oldest.Key, out _);
            }
        }
    }
}