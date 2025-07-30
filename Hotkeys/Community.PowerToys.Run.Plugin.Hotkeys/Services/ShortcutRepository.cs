#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Community.PowerToys.Run.Plugin.Hotkeys.Models;
using ManagedCommon;

namespace Community.PowerToys.Run.Plugin.Hotkeys.Services
{
    public class ShortcutRepository : IShortcutRepository
    {
        private readonly string _shortcutsDirectory;
        private readonly ILogger _logger;
        private readonly Subject<ShortcutCollectionChangedEventArgs> _shortcutsChangedSubject;
        private readonly ConcurrentDictionary<string, List<ShortcutInfo>> _shortcutsBySource;
        private readonly ConcurrentDictionary<string, ShortcutInfo> _allShortcuts;
        private readonly SemaphoreSlim _reloadSemaphore;

        private FileSystemWatcher? _watcher;
        private bool _disposed;

        public ShortcutRepository(string shortcutsDirectory, ILogger logger)
        {
            _shortcutsDirectory = shortcutsDirectory ?? throw new ArgumentNullException(nameof(shortcutsDirectory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _shortcutsChangedSubject = new Subject<ShortcutCollectionChangedEventArgs>();
            _shortcutsBySource = new ConcurrentDictionary<string, List<ShortcutInfo>>();
            _allShortcuts = new ConcurrentDictionary<string, ShortcutInfo>();
            _reloadSemaphore = new SemaphoreSlim(1, 1);
        }

        public IObservable<ShortcutCollectionChangedEventArgs> ShortcutsChanged => _shortcutsChangedSubject.AsObservable();

        public async Task<List<ShortcutInfo>> GetAllShortcutsAsync(CancellationToken cancellationToken = default)
        {
            await EnsureInitializedAsync(cancellationToken);
            return _allShortcuts.Values.ToList();
        }

        public async Task<Dictionary<string, List<ShortcutInfo>>> GetShortcutsBySourceAsync(CancellationToken cancellationToken = default)
        {
            await EnsureInitializedAsync(cancellationToken);
            return _shortcutsBySource.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToList());
        }

        public async Task<List<ShortcutInfo>> SearchShortcutsAsync(string query, string? appFilter = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(query))
                return new List<ShortcutInfo>();

            await EnsureInitializedAsync(cancellationToken);

            var searchQuery = query.ToLowerInvariant().Trim();
            var shortcuts = _allShortcuts.Values.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(appFilter))
            {
                var filter = appFilter.ToLowerInvariant();
                shortcuts = shortcuts.Where(s => s.Source.ToLowerInvariant().Contains(filter));
            }

            return shortcuts
                .Where(s => MatchesQuery(s, searchQuery))
                .OrderByDescending(s => CalculateRelevanceScore(s, searchQuery, appFilter))
                .Take(50)
                .ToList();
        }

        public async Task ReloadShortcutsAsync(CancellationToken cancellationToken = default)
        {
            await _reloadSemaphore.WaitAsync(cancellationToken);
            try
            {
                await LoadAllShortcutsInternalAsync(cancellationToken);
                _shortcutsChangedSubject.OnNext(new ShortcutCollectionChangedEventArgs(ShortcutChangeType.Reloaded, "All"));
            }
            finally
            {
                _reloadSemaphore.Release();
            }
        }

        private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
        {
            if (_allShortcuts.IsEmpty)
            {
                await ReloadShortcutsAsync(cancellationToken);
                SetupFileWatcher();
            }
        }

        private async Task LoadAllShortcutsInternalAsync(CancellationToken cancellationToken)
        {
            _allShortcuts.Clear();
            _shortcutsBySource.Clear();

            if (!Directory.Exists(_shortcutsDirectory))
            {
                _logger?.LogWarning($"Shortcuts directory does not exist: {_shortcutsDirectory}");
                return;
            }

            var jsonFiles = Directory.GetFiles(_shortcutsDirectory, "*.json", SearchOption.AllDirectories);
            var loadTasks = jsonFiles.Select(file => LoadShortcutsFromFileAsync(file, cancellationToken));

            await Task.WhenAll(loadTasks);
        }

        private async Task LoadShortcutsFromFileAsync(string filePath, CancellationToken cancellationToken)
        {
            try
            {
                var json = await File.ReadAllTextAsync(filePath, cancellationToken);
                var shortcuts = JsonSerializer.Deserialize<List<ShortcutInfo>>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (shortcuts?.Count > 0)
                {
                    var source = Path.GetFileNameWithoutExtension(filePath);

                    foreach (var shortcut in shortcuts)
                    {
                        shortcut.Source = source;
                        shortcut.NormalizedShortcut = NormalizeShortcut(shortcut.Shortcut);

                        var key = $"{source}_{shortcut.Shortcut}_{shortcut.Description}";
                        _allShortcuts.TryAdd(key, shortcut);
                    }

                    _shortcutsBySource.AddOrUpdate(source, shortcuts, (key, existing) => shortcuts);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Failed to load shortcuts from {filePath}: {ex.Message}", ex);
            }
        }

        private void SetupFileWatcher()
        {
            if (_watcher != null || !Directory.Exists(_shortcutsDirectory))
                return;

            _watcher = new FileSystemWatcher(_shortcutsDirectory)
            {
                Filter = "*.json",
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName
            };

            _watcher.Changed += OnFileChanged;
            _watcher.Created += OnFileChanged;
            _watcher.Deleted += OnFileChanged;
            _watcher.Renamed += OnFileChanged;
            _watcher.EnableRaisingEvents = true;
        }

        private async void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            try
            {
                // Debounce file changes
                await Task.Delay(500);
                await ReloadShortcutsAsync();
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Error handling file change: {ex.Message}", ex);
            }
        }

        private static string NormalizeShortcut(string shortcut)
        {
            if (string.IsNullOrEmpty(shortcut)) return "";

            return shortcut
                .Replace("Ctrl", "Control")
                .Replace("Win", "Windows")
                .Replace("Alt", "Alt")
                .Replace("+", " ")
                .ToLowerInvariant()
                .Trim();
        }

        private static bool MatchesQuery(ShortcutInfo shortcut, string query) =>
            shortcut.Shortcut.ToLowerInvariant().Contains(query) ||
            shortcut.Description.ToLowerInvariant().Contains(query) ||
            shortcut.Keywords.Any(k => k.ToLowerInvariant().Contains(query)) ||
            shortcut.Category.ToLowerInvariant().Contains(query) ||
            shortcut.NormalizedShortcut.Contains(query.Replace(" ", string.Empty));

        private static int CalculateRelevanceScore(ShortcutInfo shortcut, string query, string? appFilter = null)
        {
            var score = 0;

            if (!string.IsNullOrWhiteSpace(appFilter))
            {
                var filter = appFilter.ToLowerInvariant();
                score += shortcut.Source.ToLowerInvariant() switch
                {
                    var s when s == filter => 200,
                    var s when s.Contains(filter) => 100,
                    _ => 0
                };
            }

            score += shortcut.Shortcut.ToLowerInvariant() switch
            {
                var s when s == query => 1000,
                var s when s.Contains(query) => 800,
                _ => 0
            };

            score += shortcut.Description.ToLowerInvariant() switch
            {
                var d when d == query => 900,
                var d when d.StartsWith(query) => 700,
                var d when d.Contains(query) => 500,
                _ => 0
            };

            if (shortcut.Keywords.Any())
            {
                score += shortcut.Keywords.Any(k => k.ToLowerInvariant() == query) ? 600 :
                         shortcut.Keywords.Any(k => k.ToLowerInvariant().Contains(query)) ? 300 : 0;
            }

            var popularApps = new[] { "chrome", "firefox", "vscode", "word", "excel", "windows", "photoshop" };
            if (popularApps.Contains(shortcut.Source.ToLowerInvariant()))
                score += 50;

            return score;
        }

        public void Dispose()
        {
            if (_disposed) return;

            _watcher?.Dispose();
            _shortcutsChangedSubject?.Dispose();
            _reloadSemaphore?.Dispose();

            _disposed = true;
        }
    }
}
