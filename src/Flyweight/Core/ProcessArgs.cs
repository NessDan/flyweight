using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Flyweight.Core
{
    public static class ProcessArgs
    {
        /// <summary>Quotes one argument by the rules Windows programs use to read their command line.</summary>
        public static string Quote(string arg)
        {
            if (arg.Length > 0 && arg.IndexOfAny(new[] { ' ', '\t', '"' }) < 0) return arg;

            var quoted = new StringBuilder("\"");
            int backslashes = 0;
            foreach (char c in arg)
            {
                if (c == '\\')
                {
                    backslashes++;
                }
                else if (c == '"')
                {
                    quoted.Append('\\', backslashes * 2 + 1).Append('"');
                    backslashes = 0;
                }
                else
                {
                    quoted.Append('\\', backslashes).Append(c);
                    backslashes = 0;
                }
            }
            return quoted.Append('\\', backslashes * 2).Append('"').ToString();
        }

        public static string Join(IEnumerable<string> args)
        {
            return string.Join(" ", args.Select(Quote));
        }

        /// <summary>Splits typed-in options on spaces, treating double-quoted text as one piece.</summary>
        public static List<string> Split(string text)
        {
            var parts = new List<string>();
            if (string.IsNullOrWhiteSpace(text)) return parts;

            var current = new StringBuilder();
            bool inQuotes = false;
            bool hasPart = false;

            foreach (char c in text)
            {
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                    hasPart = true;
                }
                else if (char.IsWhiteSpace(c) && !inQuotes)
                {
                    if (hasPart) parts.Add(current.ToString());
                    current.Clear();
                    hasPart = false;
                }
                else
                {
                    current.Append(c);
                    hasPart = true;
                }
            }
            if (hasPart) parts.Add(current.ToString());
            return parts;
        }
    }
}
