// ===== 2. IShortcutRepository.cs =====
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Community.PowerToys.Run.Plugin.Hotkeys.Models;

namespace Community.PowerToys.Run.Plugin.Hotkeys.Services
{
    public interface IShortcutRepository
    {
        Task<List<ShortcutInfo>> GetAllShortcutsAsync(CancellationToken cancellationToken = default);
        Task<List<ShortcutInfo>> SearchShortcutsAsync(string query, string appFilter = null, CancellationToken cancellationToken = default);
        Task<Dictionary<string, List<ShortcutInfo>>> GetShortcutsBySourceAsync(CancellationToken cancellationToken = default);
        Task ReloadShortcutsAsync(CancellationToken cancellationToken = default);
        IObservable<ShortcutCollectionChangedEventArgs> ShortcutsChanged { get; }
        void Dispose();
    }

    public class ShortcutCollectionChangedEventArgs : EventArgs
    {
        public ShortcutChangeType ChangeType { get; }
        public string Source { get; }
        public List<ShortcutInfo> AffectedShortcuts { get; }

        public ShortcutCollectionChangedEventArgs(ShortcutChangeType changeType, string source, List<ShortcutInfo> affectedShortcuts = null)
        {
            ChangeType = changeType;
            Source = source;
            AffectedShortcuts = affectedShortcuts ?? new List<ShortcutInfo>();
        }
    }

    public enum ShortcutChangeType
    {
        Added,
        Removed,
        Modified,
        Reloaded
    }
}