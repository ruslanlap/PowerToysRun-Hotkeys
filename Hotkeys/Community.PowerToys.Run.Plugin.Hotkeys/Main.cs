// ===== 8. Refactored Main.cs =====
using ManagedCommon;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Wox.Plugin;
using PowerToysRun.ShortcutFinder.Plugin.Helpers;
using Community.PowerToys.Run.Plugin.Hotkeys.Services;
using Community.PowerToys.Run.Plugin.Hotkeys.Models;

namespace Community.PowerToys.Run.Plugin.Hotkeys
{
    public class Main : IPlugin, IContextMenu, IDisposable
    {
        public static string PluginID => "4BDC7426E3404ECDB2D502B7B3CEAD9F";

        public string Name => "Hotkeys";
        public string Description => "Find and copy keyboard shortcuts for any app";

        private PluginInitContext Context { get; set; }
        private string IconPath { get; set; }
        private bool Disposed { get; set; }

        // Dependency services
        private IShortcutRepository _shortcutRepository;
        private IQueryProcessor _queryProcessor;
        private ILogger _logger;

        public void Init(PluginInitContext context)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            Context.API.ThemeChanged += OnThemeChanged;
            UpdateIconPath(Context.API.GetCurrentTheme());

            InitializeServices();
        }

        private void InitializeServices()
        {
            try
            {
                var shortcutsDirectory = Path.Combine(Context.CurrentPluginMetadata.PluginDirectory, "Shortcuts");

                // Initialize logger
                _logger = new PowerToysLogger();

                // Initialize repository
                _shortcutRepository = new ShortcutRepository(shortcutsDirectory, _logger);

                // Initialize query processor
                _queryProcessor = new QueryProcessor(_shortcutRepository, _logger, Context);

                _logger.LogInfo("Hotkeys plugin services initialized successfully");
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Failed to initialize plugin services: {ex.Message}", ex);
                throw;
            }
        }

        public List<Result> Query(Query query)
        {
            try
            {
                // Since PowerToys Run expects synchronous results, we need to handle async calls carefully
                var task = _queryProcessor.ProcessQueryAsync(query, IconPath);

                // Use ConfigureAwait(false) to avoid deadlocks and wait with timeout
                if (task.Wait(TimeSpan.FromSeconds(5)))
                {
                    return task.Result;
                }
                else
                {
                    _logger?.LogWarning($"Query processing timed out for: {query.Search}");
                    return CreateTimeoutResult(query.Search);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Error in Query method: {ex.Message}", ex);
                return CreateErrorResult(query.Search, ex.Message);
            }
        }

        private List<Result> CreateTimeoutResult(string search)
        {
            return new List<Result>
            {
                new Result
                {
                    IcoPath = IconPath,
                    Title = "Query timed out",
                    SubTitle = $"Search for '{search}' took too long. Please try again.",
                    Action = _ => true
                }
            };
        }

        private List<Result> CreateErrorResult(string search, string errorMessage)
        {
            return new List<Result>
            {
                new Result
                {
                    IcoPath = IconPath,
                    Title = "Error occurred",
                    SubTitle = $"Failed to process '{search}': {errorMessage}",
                    Action = _ => true
                }
            };
        }

        public List<ContextMenuResult> LoadContextMenus(Result selectedResult)
        {
            if (selectedResult.ContextData is ShortcutInfo shortcut)
            {
                return new List<ContextMenuResult>
                {
                    new ContextMenuResult
                    {
                        PluginName = Name,
                        Title = "Copy shortcut to clipboard (Ctrl+C)",
                        FontFamily = "Segoe MDL2 Assets",
                        Glyph = "\xE8C8",
                        AcceleratorKey = Key.C,
                        AcceleratorModifiers = ModifierKeys.Control,
                        Action = _ =>
                        {
                            ClipboardHelper.SetClipboard(shortcut.Shortcut);
                            Context.API.ShowMsg("Copied", $"'{shortcut.Shortcut}' copied to clipboard", string.Empty);
                            return true;
                        }
                    },
                    new ContextMenuResult
                    {
                        PluginName = Name,
                        Title = "Copy description",
                        FontFamily = "Segoe MDL2 Assets",
                        Glyph = "\xE8C8",
                        Action = _ =>
                        {
                            ClipboardHelper.SetClipboard(shortcut.Description);
                            Context.API.ShowMsg("Copied", $"Description copied to clipboard", string.Empty);
                            return true;
                        }
                    },
                    new ContextMenuResult
                    {
                        PluginName = Name,
                        Title = $"Show all {shortcut.Source} shortcuts",
                        FontFamily = "Segoe MDL2 Assets",
                        Glyph = "\xE8FD",
                        Action = _ =>
                        {
                            Context.API.ChangeQuery($"hk list:{shortcut.Source}", true);
                            return false;
                        }
                    }
                };
            }
            else if (selectedResult.ContextData is string search)
            {
                return new List<ContextMenuResult>
                {
                    new ContextMenuResult
                    {
                        PluginName = Name,
                        Title = "Copy query to clipboard",
                        FontFamily = "Segoe MDL2 Assets",
                        Glyph = "\xE8C8",
                        AcceleratorKey = Key.C,
                        AcceleratorModifiers = ModifierKeys.Control,
                        Action = _ =>
                        {
                            ClipboardHelper.SetClipboard(search);
                            return true;
                        }
                    }
                };
            }

            return new List<ContextMenuResult>();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (Disposed || !disposing)
                return;

            try
            {
                if (Context?.API != null)
                    Context.API.ThemeChanged -= OnThemeChanged;

                _shortcutRepository?.Dispose();
                _logger?.LogInfo("Hotkeys plugin disposed successfully");
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Error during disposal: {ex.Message}", ex);
            }
            finally
            {
                Disposed = true;
            }
        }

        private void UpdateIconPath(Theme theme) => IconPath = theme == Theme.Light || theme == Theme.HighContrastWhite
            ? "Images/hotkeys.light.png"
            : "Images/hotkeys.dark.png";

        private void OnThemeChanged(Theme currentTheme, Theme newTheme) => UpdateIconPath(newTheme);
    }
}