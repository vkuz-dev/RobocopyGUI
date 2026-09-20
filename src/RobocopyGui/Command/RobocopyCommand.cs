using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RobocopyGui.Command
{
    /// <summary>
    /// An editable robocopy command line. This is the canonical job configuration:
    /// the GUI reads supported parameters from it and edits only the tokens it owns.
    /// Every other token keeps its exact original text and position.
    /// </summary>
    /// <remarks>
    /// Structure, as robocopy parses it: the executable, then switches (tokens starting
    /// with '/') and plain arguments in any order. Plain arguments that follow a
    /// multi-value switch (/XF, /XD, /IF) are that switch's values. All other plain
    /// arguments are positional: source, destination, then file specs.
    /// </remarks>
    internal sealed class RobocopyCommand
    {
        public const string DefaultOptions = "/E /COPY:DATS /DCOPY:DAT /R:2 /W:5 /MT:16 /Z /XJ /NP";
        public const string DefaultCommand = "robocopy " + DefaultOptions;

        public const string DefaultCopyFlags = "DAT";
        public const string DefaultDirectoryCopyFlags = "DA";
        public const int DefaultRetries = 1000000;
        public const int DefaultWaitSeconds = 30;
        public const int DefaultThreadsWhenUnspecified = 8;

        private static readonly string[] MultiValueSwitches = { "/XF", "/XD", "/IF" };
        private static readonly string[] CopyFlagSwitches = { "/COPY", "/SEC", "/COPYALL", "/NOCOPY" };
        private static readonly string[] DirectoryCopyFlagSwitches = { "/DCOPY", "/NODCOPY" };

        private readonly List<CommandToken> _tokens;
        private readonly string _trailing;

        private RobocopyCommand(List<CommandToken> tokens, string trailing)
        {
            _tokens = tokens;
            _trailing = trailing;
        }

        public static RobocopyCommand Parse(string text)
        {
            string trailing;
            var tokens = CommandLine.Tokenize(text, out trailing);
            return new RobocopyCommand(tokens, trailing);
        }

        public override string ToString()
        {
            return string.Concat(_tokens.Select(t => t.Leading + t.Raw)) + _trailing;
        }

        // ------------------------------------------------------------------
        // Reading
        // ------------------------------------------------------------------

        /// <summary>True when the first token is robocopy / robocopy.exe (optionally with a path).</summary>
        public bool HasExecutable
        {
            get { return _tokens.Count > 0 && IsRobocopyExecutable(_tokens[0].Value); }
        }

        public string Executable
        {
            get { return HasExecutable ? _tokens[0].Value : null; }
        }

        /// <summary>Everything after the executable, exactly as written.</summary>
        public string Arguments
        {
            get
            {
                int first = FirstArgumentIndex;
                return string.Concat(_tokens.Skip(first).Select(t => t.Leading + t.Raw)).Trim();
            }
        }

        public string Source
        {
            get { return PositionalValue(0); }
        }

        public string Destination
        {
            get { return PositionalValue(1); }
        }

        /// <summary>File name patterns to include (positional arguments after the destination).</summary>
        public IList<string> FileSpecs
        {
            get { return PositionalTokens().Skip(2).Select(t => t.Value).ToList(); }
        }

        public bool HasSwitch(string name)
        {
            return SwitchTokens(name).Any();
        }

        /// <summary>
        /// Returns null if the switch is absent, "" if present without an argument
        /// (e.g. "/MT"), otherwise the text after the colon. The last occurrence wins.
        /// </summary>
        public string GetSwitchArgument(string name)
        {
            var token = SwitchTokens(name).LastOrDefault();
            return token == null ? null : SwitchArgument(token);
        }

        /// <summary>All values of a multi-value switch such as /XF or /XD, across every occurrence.</summary>
        public IList<string> GetValues(string name)
        {
            return FindGroups(name).SelectMany(g => g.Values).Select(t => t.Value).ToList();
        }

        /// <summary>Effective /COPY flags, taking /SEC, /COPYALL and /NOCOPY into account.</summary>
        public string CopyFlags
        {
            get
            {
                string flags = DefaultCopyFlags;
                foreach (var token in SwitchTokens(CopyFlagSwitches))
                {
                    switch (SwitchName(token))
                    {
                        case "/COPY": flags = SwitchArgument(token).ToUpperInvariant(); break;
                        case "/SEC": flags = "DATS"; break;
                        case "/COPYALL": flags = "DATSOU"; break;
                        case "/NOCOPY": flags = string.Empty; break;
                    }
                }
                return flags;
            }
        }

        /// <summary>Effective /DCOPY flags, taking /NODCOPY into account.</summary>
        public string DirectoryCopyFlags
        {
            get
            {
                string flags = DefaultDirectoryCopyFlags;
                foreach (var token in SwitchTokens(DirectoryCopyFlagSwitches))
                    flags = SwitchName(token) == "/NODCOPY" ? string.Empty : SwitchArgument(token).ToUpperInvariant();
                return flags;
            }
        }

        public int? GetIntArgument(string name)
        {
            int value;
            string arg = GetSwitchArgument(name);
            return arg != null && int.TryParse(arg, out value) ? value : (int?)null;
        }

        // ------------------------------------------------------------------
        // Editing (only ever touches the tokens named by the caller)
        // ------------------------------------------------------------------

        /// <summary>Sets the source (0) or destination (1) path, inserting "" placeholders if needed.</summary>
        public void SetPositional(int position, string value)
        {
            EnsurePositionalCount(position + 1);
            var token = PositionalTokens()[position];
            Replace(token, CommandLine.Quote(value), value ?? string.Empty);
        }

        /// <summary>Replaces all file specs. They are written directly after the destination.</summary>
        public void SetFileSpecs(IEnumerable<string> specs)
        {
            var list = specs.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
            foreach (var token in PositionalTokens().Skip(2).ToList())
                Remove(token);

            if (list.Count == 0)
                return;

            EnsurePositionalCount(2);
            int index = _tokens.IndexOf(PositionalTokens()[1]) + 1;
            foreach (var spec in list)
                InsertAt(index++, CommandLine.Quote(spec), spec);
        }

        /// <summary>Adds or removes a switch without argument, such as /MIR or /Z.</summary>
        public void SetSwitch(string name, bool present)
        {
            if (present)
            {
                if (!HasSwitch(name))
                    Append(name, name);
            }
            else
            {
                foreach (var token in SwitchTokens(name).ToList())
                    Remove(token);
            }
        }

        /// <summary>
        /// Sets a switch with an argument, e.g. SetSwitch("/MT", "16") -> /MT:16.
        /// An empty argument writes the bare switch, null removes it.
        /// </summary>
        public void SetSwitch(string name, string argument)
        {
            ReplaceSwitches(new[] { name }, argument == null ? null : FormatSwitch(name, argument));
        }

        /// <summary>
        /// Replaces the first occurrence of any switch in <paramref name="family"/> with
        /// <paramref name="newSwitch"/> (keeping its position) and removes the others.
        /// Appends if none is present. A null <paramref name="newSwitch"/> removes them all.
        /// </summary>
        public void ReplaceSwitches(string[] family, string newSwitch)
        {
            var existing = SwitchTokens(family).ToList();
            if (newSwitch == null)
            {
                foreach (var token in existing)
                    Remove(token);
                return;
            }

            if (existing.Count == 0)
            {
                Append(newSwitch, newSwitch);
                return;
            }

            if (!string.Equals(existing[0].Raw, newSwitch, StringComparison.OrdinalIgnoreCase))
                Replace(existing[0], newSwitch, newSwitch);
            foreach (var token in existing.Skip(1))
                Remove(token);
        }

        /// <summary>Writes /COPY:flags in place of /COPY, /SEC, /COPYALL or /NOCOPY. Empty flags mean /NOCOPY.</summary>
        public void SetCopyFlags(string flags)
        {
            ReplaceSwitches(CopyFlagSwitches, string.IsNullOrEmpty(flags) ? "/NOCOPY" : "/COPY:" + flags);
        }

        /// <summary>Writes /DCOPY:flags in place of /DCOPY or /NODCOPY. Empty flags mean /NODCOPY.</summary>
        public void SetDirectoryCopyFlags(string flags)
        {
            ReplaceSwitches(DirectoryCopyFlagSwitches, string.IsNullOrEmpty(flags) ? "/NODCOPY" : "/DCOPY:" + flags);
        }

        /// <summary>
        /// Edits the values of a multi-value switch (/XF, /XD). Values for which
        /// <paramref name="isOwned"/> returns true are removed and replaced by
        /// <paramref name="ownedValues"/>; all other values stay where they are.
        /// New values go into the first existing group, or a new group at the end.
        /// </summary>
        public void SetValues(string name, Func<string, bool> isOwned, IEnumerable<string> ownedValues)
        {
            var newValues = ownedValues.Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
            var groups = FindGroups(name);
            var emptied = new List<Group>();

            foreach (var group in groups)
            {
                var owned = group.Values.Where(v => isOwned(v.Value)).ToList();
                if (owned.Count == 0)
                    continue;
                foreach (var value in owned)
                {
                    RemoveToken(value);
                    group.Values.Remove(value);
                }
                if (group.Values.Count == 0)
                    emptied.Add(group);
            }

            Group target = groups.FirstOrDefault();
            if (newValues.Count > 0)
            {
                int index;
                if (target == null)
                {
                    Append(name, name);
                    index = _tokens.Count;
                }
                else
                {
                    emptied.Remove(target);
                    index = _tokens.IndexOf(target.Values.LastOrDefault() ?? target.Switch) + 1;
                }

                foreach (var value in newValues)
                    InsertAt(index++, CommandLine.Quote(value), value);
            }

            foreach (var group in emptied)
                Remove(group.Switch);
        }

        // ------------------------------------------------------------------
        // Structure helpers
        // ------------------------------------------------------------------

        public static bool IsRobocopyExecutable(string value)
        {
            if (string.IsNullOrEmpty(value) || value.StartsWith("/", StringComparison.Ordinal))
                return false;
            try
            {
                return string.Equals(Path.GetFileNameWithoutExtension(value), "robocopy", StringComparison.OrdinalIgnoreCase);
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        private int FirstArgumentIndex
        {
            get { return HasExecutable ? 1 : 0; }
        }

        private static bool IsSwitch(CommandToken token)
        {
            return token.Value.StartsWith("/", StringComparison.Ordinal);
        }

        private static bool IsMultiValueSwitch(CommandToken token)
        {
            return IsSwitch(token) && MultiValueSwitches.Contains(SwitchName(token));
        }

        private static string SwitchName(CommandToken token)
        {
            string value = token.Value;
            int colon = value.IndexOf(':');
            return (colon < 0 ? value : value.Substring(0, colon)).ToUpperInvariant();
        }

        private static string SwitchArgument(CommandToken token)
        {
            int colon = token.Value.IndexOf(':');
            return colon < 0 ? string.Empty : token.Value.Substring(colon + 1);
        }

        private static string FormatSwitch(string name, string argument)
        {
            return argument.Length == 0 ? name : name + ":" + argument;
        }

        private IEnumerable<CommandToken> SwitchTokens(params string[] names)
        {
            return _tokens.Skip(FirstArgumentIndex)
                .Where(t => IsSwitch(t) && names.Contains(SwitchName(t), StringComparer.OrdinalIgnoreCase));
        }

        /// <summary>Plain arguments that are not values of a multi-value switch.</summary>
        private List<CommandToken> PositionalTokens()
        {
            var result = new List<CommandToken>();
            bool inGroup = false;
            foreach (var token in _tokens.Skip(FirstArgumentIndex))
            {
                if (IsSwitch(token))
                    inGroup = IsMultiValueSwitch(token);
                else if (!inGroup)
                    result.Add(token);
            }
            return result;
        }

        private string PositionalValue(int position)
        {
            var positional = PositionalTokens();
            return position < positional.Count ? positional[position].Value : null;
        }

        private void EnsurePositionalCount(int count)
        {
            var positional = PositionalTokens();
            while (positional.Count < count)
            {
                int index = positional.Count == 0
                    ? FirstArgumentIndex
                    : _tokens.IndexOf(positional[positional.Count - 1]) + 1;
                InsertAt(index, "\"\"", string.Empty);
                positional = PositionalTokens();
            }
        }

        private sealed class Group
        {
            public CommandToken Switch;
            public readonly List<CommandToken> Values = new List<CommandToken>();
        }

        private List<Group> FindGroups(string name)
        {
            var groups = new List<Group>();
            Group current = null;
            foreach (var token in _tokens.Skip(FirstArgumentIndex))
            {
                if (IsSwitch(token))
                {
                    current = null;
                    if (IsMultiValueSwitch(token) && string.Equals(SwitchName(token), name, StringComparison.OrdinalIgnoreCase))
                    {
                        current = new Group { Switch = token };
                        groups.Add(current);
                    }
                }
                else if (current != null)
                {
                    current.Values.Add(token);
                }
            }
            return groups;
        }

        // ------------------------------------------------------------------
        // Low-level token edits (preserve surrounding whitespace)
        // ------------------------------------------------------------------

        private void Append(string raw, string value)
        {
            InsertAt(_tokens.Count, raw, value);
        }

        private void InsertAt(int index, string raw, string value)
        {
            string leading = index == 0 ? string.Empty : " ";
            if (index == 0 && _tokens.Count > 0 && _tokens[0].Leading.Length == 0)
                _tokens[0].Leading = " ";
            _tokens.Insert(index, new CommandToken(leading, raw, value));
        }

        private void Replace(CommandToken token, string raw, string value)
        {
            int index = _tokens.IndexOf(token);
            _tokens[index] = new CommandToken(token.Leading, raw, value);
        }

        private void RemoveToken(CommandToken token)
        {
            int index = _tokens.IndexOf(token);
            _tokens.RemoveAt(index);
            if (index == 0 && _tokens.Count > 0)
                _tokens[0].Leading = string.Empty;
        }

        /// <summary>
        /// Removes a token. If a removed switch was followed by plain arguments that
        /// would now be swallowed by a preceding /XF, /XD or /IF, those arguments are
        /// moved in front of that multi-value switch so that their meaning is unchanged.
        /// </summary>
        private void Remove(CommandToken token)
        {
            int index = _tokens.IndexOf(token);
            RemoveToken(token);
            if (!IsSwitch(token))
                return;

            int runEnd = index;
            while (runEnd < _tokens.Count && !IsSwitch(_tokens[runEnd]))
                runEnd++;
            if (runEnd == index)
                return;

            int governing = index - 1;
            while (governing >= FirstArgumentIndex && !IsSwitch(_tokens[governing]))
                governing--;
            if (governing < FirstArgumentIndex || !IsMultiValueSwitch(_tokens[governing]))
                return;

            var run = _tokens.GetRange(index, runEnd - index);
            _tokens.RemoveRange(index, runEnd - index);
            _tokens.InsertRange(governing, run);
        }
    }
}
