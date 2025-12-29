using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Community.PowerToys.Run.Plugin.Hotkeys.Models
{
    public class ShortcutInfo
    {
        public string Shortcut { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<string> Keywords { get; set; } = new();
        public string Category { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public string Language { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;

        /// <summary>
        /// Normalized shortcut for better search matching (not stored in JSON)
        /// </summary>
        [JsonIgnore]
        public string NormalizedShortcut { get; set; } = string.Empty;

        /// <summary>
        /// Usage frequency for improved ranking (optional)
        /// </summary>
        [JsonIgnore]
        public int UsageCount { get; set; }

        /// <summary>
        /// Alternative shortcut names for better search matching
        /// </summary>
        public List<string> Aliases { get; set; } = new();

        /// <summary>
        /// Notes or additional information
        /// </summary>
        public string Notes { get; set; } = string.Empty;

        /// <summary>
        /// Whether this shortcut is universal (works in many applications)
        /// </summary>
        public bool IsGlobal { get; set; }

        /// <summary>
        /// Platform (Windows, Mac, Linux)
        /// </summary>
        public string Platform { get; set; } = "Windows";

        /// <summary>
        /// Application version for which this shortcut is relevant
        /// </summary>
        public string Version { get; set; } = string.Empty;

        /// <summary>
        /// Difficulty level (Beginner, Intermediate, Advanced)
        /// </summary>
        public string Difficulty { get; set; } = "Beginner";
    }
}