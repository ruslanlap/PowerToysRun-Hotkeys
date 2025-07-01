// ===== 1. Models/ShortcutInfo.cs =====
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Community.PowerToys.Run.Plugin.Hotkeys.Models
{
    public class ShortcutInfo
    {
        public string Shortcut { get; set; }
        public string Description { get; set; }
        public List<string> Keywords { get; set; } = new List<string>();
        public string Category { get; set; }
        public string Source { get; set; }
        public string Language { get; set; }

        /// <summary>
        /// Нормалізований shortcut для кращого пошуку (не зберігається в JSON)
        /// </summary>
        [JsonIgnore]
        public string NormalizedShortcut { get; set; }

        /// <summary>
        /// Частота використання для покращення ранжування (опціонально)
        /// </summary>
        [JsonIgnore]
        public int UsageCount { get; set; } = 0;

        /// <summary>
        /// Альтернативні назви shortcut для кращого пошуку
        /// </summary>
        public List<string> Aliases { get; set; } = new List<string>();

        /// <summary>
        /// Примітки або додаткова інформація
        /// </summary>
        public string Notes { get; set; }

        /// <summary>
        /// Чи є цей shortcut універсальним (працює в багатьох програмах)
        /// </summary>
        public bool IsGlobal { get; set; } = false;

        /// <summary>
        /// Платформа (Windows, Mac, Linux)
        /// </summary>
        public string Platform { get; set; } = "Windows";

        /// <summary>
        /// Версія програми, для якої актуальний shortcut
        /// </summary>
        public string Version { get; set; }

        /// <summary>
        /// Рівень складності (Beginner, Intermediate, Advanced)
        /// </summary>
        public string Difficulty { get; set; } = "Beginner";
    }
}