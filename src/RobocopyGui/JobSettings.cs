using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using RobocopyGui.Command;

namespace RobocopyGui
{
    internal enum FolderMode
    {
        IncludeSelected,
        ExcludeSelected,
    }

    /// <summary>
    /// Everything that Import/Export Settings stores. The robocopy configuration is the
    /// command itself; the other properties are application settings robocopy cannot express.
    /// </summary>
    internal sealed class JobSettings
    {
        private const string FormatName = "RobocopyGUI";
        private const int FormatVersion = 1;

        public string Command { get; set; } = RobocopyCommand.DefaultCommand;

        public FolderMode FolderMode { get; set; } = FolderMode.IncludeSelected;

        public bool StartEnabled { get; set; }

        public TimeSpan StartTime { get; set; } = new TimeSpan(22, 0, 0);

        public bool StopEnabled { get; set; }

        public TimeSpan StopTime { get; set; } = new TimeSpan(7, 0, 0);

        public bool VerificationEnabled { get; set; } = true;

        /// <summary>Null means the default log folder.</summary>
        public string LogDirectory { get; set; }

        public string ToJson()
        {
            var sb = new StringBuilder();
            sb.Append("{\r\n");
            sb.Append("  \"format\": ").Append(Str(FormatName)).Append(",\r\n");
            sb.Append("  \"version\": ").Append(FormatVersion).Append(",\r\n");
            sb.Append("  \"command\": ").Append(Str(Command)).Append(",\r\n");
            sb.Append("  \"folderMode\": ").Append(Str(FolderMode == FolderMode.ExcludeSelected ? "exclude" : "include")).Append(",\r\n");
            sb.Append("  \"schedule\": {\r\n");
            sb.Append("    \"startEnabled\": ").Append(Bool(StartEnabled)).Append(",\r\n");
            sb.Append("    \"start\": ").Append(Str(FormatTime(StartTime))).Append(",\r\n");
            sb.Append("    \"stopEnabled\": ").Append(Bool(StopEnabled)).Append(",\r\n");
            sb.Append("    \"stop\": ").Append(Str(FormatTime(StopTime))).Append("\r\n");
            sb.Append("  },\r\n");
            sb.Append("  \"verification\": {\r\n");
            sb.Append("    \"enabled\": ").Append(Bool(VerificationEnabled)).Append("\r\n");
            sb.Append("  }");
            if (!string.IsNullOrEmpty(LogDirectory))
                sb.Append(",\r\n  \"logDirectory\": ").Append(Str(LogDirectory));
            sb.Append("\r\n}\r\n");
            return sb.ToString();
        }

        public static JobSettings FromJson(string json)
        {
            var root = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
            if (root == null)
                throw new InvalidDataException("The file does not contain a settings object.");

            string command = GetString(root, "command");
            if (command == null)
                throw new InvalidDataException("The file does not contain a \"command\" property.");

            var settings = new JobSettings { Command = command };

            string mode = GetString(root, "folderMode");
            if (string.Equals(mode, "exclude", StringComparison.OrdinalIgnoreCase))
                settings.FolderMode = FolderMode.ExcludeSelected;

            var schedule = Get(root, "schedule") as Dictionary<string, object>;
            if (schedule != null)
            {
                settings.StartEnabled = GetBool(schedule, "startEnabled") ?? false;
                settings.StartTime = ParseTime(GetString(schedule, "start")) ?? settings.StartTime;
                settings.StopEnabled = GetBool(schedule, "stopEnabled") ?? false;
                settings.StopTime = ParseTime(GetString(schedule, "stop")) ?? settings.StopTime;
            }

            var verification = Get(root, "verification") as Dictionary<string, object>;
            if (verification != null)
                settings.VerificationEnabled = GetBool(verification, "enabled") ?? true;

            string logDirectory = GetString(root, "logDirectory");
            if (!string.IsNullOrWhiteSpace(logDirectory))
                settings.LogDirectory = logDirectory;

            return settings;
        }

        public static string FormatTime(TimeSpan time)
        {
            return time.Hours.ToString("00", CultureInfo.InvariantCulture) + ":" + time.Minutes.ToString("00", CultureInfo.InvariantCulture);
        }

        public static TimeSpan? ParseTime(string text)
        {
            DateTime parsed;
            if (text != null && DateTime.TryParseExact(text.Trim(), new[] { "H:mm", "HH:mm" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
                return parsed.TimeOfDay;
            return null;
        }

        private static object Get(Dictionary<string, object> obj, string key)
        {
            object value;
            return obj.TryGetValue(key, out value) ? value : null;
        }

        private static string GetString(Dictionary<string, object> obj, string key)
        {
            return Get(obj, key) as string;
        }

        private static bool? GetBool(Dictionary<string, object> obj, string key)
        {
            return Get(obj, key) as bool?;
        }

        private static string Bool(bool value)
        {
            return value ? "true" : "false";
        }

        /// <summary>JSON string literal. Unlike the framework serializers, '/' is not escaped, so commands stay readable.</summary>
        private static string Str(string value)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in value ?? string.Empty)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }
    }
}
