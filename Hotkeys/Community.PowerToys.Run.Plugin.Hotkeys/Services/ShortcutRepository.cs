// ===== 3. ShortcutRepository.cs =====
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
using ManagedCommon;
using Community.PowerToys.Run.Plugin.Hotkeys.Models;

namespace Community.PowerToys.Run.Plugin.Hotkeys.Services
{
    public class ShortcutRepository : IShortcutRepository, IDisposable
    {
        private readonly string _shortcutsDirectory;
        private readonly ILogger _logger;
        private readonly Subject<ShortcutCollectionChangedEventArgs> _shortcutsChangedSubject;
        private readonly ConcurrentDictionary<string, List<ShortcutInfo>> _shortcutsBySource;
        private readonly ConcurrentDictionary<string, ShortcutInfo> _allShortcuts;
        private readonly SemaphoreSlim _reloadSemaphore;

        private FileSystemWatcher _watcher;
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

        public async Task<List<ShortcutInfo>> SearchShortcutsAsync(string query, string appFilter = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(query))
                return new List<ShortcutInfo>();

            await EnsureInitializedAsync(cancellationToken);

            var searchQuery = query.ToLowerInvariant().Trim();
            var shortcuts = _allShortcuts.Values.AsEnumerable();

            // Apply app filter if specified
            if (!string.IsNullOrWhiteSpace(appFilter))
            {
                var filter = appFilter.ToLowerInvariant();
                shortcuts = shortcuts.Where(s => s.Source?.ToLowerInvariant().Contains(filter) == true);
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

        private static bool MatchesQuery(ShortcutInfo shortcut, string query)
        {
            return (shortcut.Shortcut?.ToLowerInvariant().Contains(query) == true) ||
                   (shortcut.Description?.ToLowerInvariant().Contains(query) == true) ||
                   (shortcut.Keywords?.Any(k => k.ToLowerInvariant().Contains(query)) == true) ||
                   (shortcut.Category?.ToLowerInvariant().Contains(query) == true) ||
                   (shortcut.NormalizedShortcut?.Contains(query.Replace(" ", "")) == true);
        }

        private static int CalculateRelevanceScore(ShortcutInfo shortcut, string query, string appFilter = null)
        {
            int score = 0;

            // App filter bonus
            if (!string.IsNullOrWhiteSpace(appFilter))
            {
                var filter = appFilter.ToLowerInvariant();
                if (shortcut.Source?.ToLowerInvariant() == filter)
                    score += 200;
                else if (shortcut.Source?.ToLowerInvariant().Contains(filter) == true)
                    score += 100;
            }

            // Exact matches get highest priority
            if (shortcut.Shortcut?.ToLowerInvariant() == query)
                score += 1000;
            else if (shortcut.Shortcut?.ToLowerInvariant().Contains(query) == true)
                score += 800;

            // Description matches
            if (shortcut.Description?.ToLowerInvariant() == query)
                score += 900;
            else if (shortcut.Description?.ToLowerInvariant().StartsWith(query) == true)
                score += 700;
            else if (shortcut.Description?.ToLowerInvariant().Contains(query) == true)
                score += 500;

            // Keyword matches
            if (shortcut.Keywords?.Any(k => k.ToLowerInvariant() == query) == true)
                score += 600;
            else if (shortcut.Keywords?.Any(k => k.ToLowerInvariant().Contains(query)) == true)
                score += 300;

            // Popular apps bonus
            var popularApps = new[] { "chrome", "firefox", "vscode", "word", "excel", "windows", "photoshop" };
            if (popularApps.Contains(shortcut.Source?.ToLowerInvariant()))
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
