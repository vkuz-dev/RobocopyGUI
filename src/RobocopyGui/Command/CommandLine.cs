using System.Collections.Generic;
using System.Text;

namespace RobocopyGui.Command
{
    /// <summary>
    /// One argument of a command line. Remembers exactly how it was written so that
    /// untouched arguments can be written back verbatim.
    /// </summary>
    internal sealed class CommandToken
    {
        public CommandToken(string leading, string raw, string value)
        {
            Leading = leading;
            Raw = raw;
            Value = value;
        }

        /// <summary>Whitespace that preceded the token in the original text.</summary>
        public string Leading { get; set; }

        /// <summary>The token exactly as written, including quotes.</summary>
        public string Raw { get; }

        /// <summary>The argument value the program receives (quotes and escapes removed).</summary>
        public string Value { get; }
    }

    /// <summary>
    /// Splits and quotes command lines using the Microsoft C runtime rules, which is
    /// how robocopy.exe itself parses its arguments.
    /// </summary>
    internal static class CommandLine
    {
        public static List<CommandToken> Tokenize(string text, out string trailing)
        {
            var tokens = new List<CommandToken>();
            text = text ?? string.Empty;
            int i = 0;
            int n = text.Length;

            while (true)
            {
                int whitespaceStart = i;
                while (i < n && IsWhitespace(text[i]))
                    i++;
                string leading = text.Substring(whitespaceStart, i - whitespaceStart);

                if (i >= n)
                {
                    trailing = leading;
                    return tokens;
                }

                int start = i;
                var value = new StringBuilder();
                bool inQuotes = false;

                while (i < n)
                {
                    char c = text[i];

                    if (c == '\\')
                    {
                        int backslashes = 0;
                        while (i < n && text[i] == '\\')
                        {
                            backslashes++;
                            i++;
                        }

                        if (i < n && text[i] == '"')
                        {
                            // 2n backslashes + quote -> n backslashes, quote toggles quoting.
                            // 2n+1 backslashes + quote -> n backslashes + literal quote.
                            value.Append('\\', backslashes / 2);
                            if (backslashes % 2 == 1)
                            {
                                value.Append('"');
                                i++;
                            }
                        }
                        else
                        {
                            value.Append('\\', backslashes);
                        }
                        continue;
                    }

                    if (c == '"')
                    {
                        if (inQuotes && i + 1 < n && text[i + 1] == '"')
                        {
                            value.Append('"');
                            i += 2;
                            continue;
                        }
                        inQuotes = !inQuotes;
                        i++;
                        continue;
                    }

                    if (!inQuotes && IsWhitespace(c))
                        break;

                    value.Append(c);
                    i++;
                }

                tokens.Add(new CommandToken(leading, text.Substring(start, i - start), value.ToString()));
            }
        }

        /// <summary>Quotes a value so that <see cref="Tokenize"/> (and robocopy) read it back unchanged.</summary>
        public static string Quote(string value)
        {
            value = value ?? string.Empty;
            if (value.Length > 0 && value.IndexOfAny(new[] { ' ', '\t', '"' }) < 0)
                return value;

            var sb = new StringBuilder("\"");
            int backslashes = 0;
            foreach (char c in value)
            {
                if (c == '\\')
                {
                    backslashes++;
                    continue;
                }

                if (c == '"')
                {
                    sb.Append('\\', backslashes * 2 + 1);
                    sb.Append('"');
                }
                else
                {
                    sb.Append('\\', backslashes);
                    sb.Append(c);
                }
                backslashes = 0;
            }

            // Backslashes before the closing quote must be doubled ("C:\Dir\\").
            sb.Append('\\', backslashes * 2);
            sb.Append('"');
            return sb.ToString();
        }

        private static bool IsWhitespace(char c)
        {
            return c == ' ' || c == '\t' || c == '\r' || c == '\n';
        }
    }
}
