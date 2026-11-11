using System;
using System.Collections.Generic;
using System.Text;

namespace PSConsole
{
    internal class ConsoleCommand
    {
        internal string Name;
        internal string Args;
        internal string Help;
        internal Action<string[]> Run;
    }

    internal static class CommandProcessor
    {
        private static readonly List<ConsoleCommand> registry = new List<ConsoleCommand>();

        internal static IReadOnlyList<ConsoleCommand> All { get { return registry; } }

        internal static void Register(string name, string args, string help, Action<string[]> run)
        {
            registry.Add(new ConsoleCommand { Name = name, Args = args, Help = help, Run = run });
        }

        internal static ConsoleCommand Find(string name)
        {
            for (int i = 0; i < registry.Count; i++)
            {
                if (string.Equals(registry[i].Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return registry[i];
                }
            }
            return null;
        }

        internal static void Execute(string raw)
        {
            ConsoleBehaviour.Echo(raw);

            string[] parts = Split(raw);
            if (parts.Length == 0)
            {
                return;
            }

            ConsoleCommand command = Find(parts[0]);
            if (command == null)
            {
                ConsoleBehaviour.Err("unknown command '" + parts[0] + "' — type 'help'.");
                return;
            }

            string[] args = new string[parts.Length - 1];
            Array.Copy(parts, 1, args, 0, args.Length);

            try
            {
                command.Run(args);
            }
            catch (Exception e)
            {
                ConsoleBehaviour.Err(e.GetType().Name + ": " + e.Message);
            }
        }

        /// <summary>Tab completion: command names first, then item ids for item-taking commands.</summary>
        internal static string Suggest(string raw)
        {
            string text = raw ?? string.Empty;
            bool trailingSpace = text.Length > 0 && text[text.Length - 1] == ' ';
            string[] parts = Split(text);

            if (parts.Length == 0)
            {
                return null;
            }

            if (parts.Length == 1 && !trailingSpace)
            {
                string prefix = parts[0];
                string match = FirstMatch(CommandNames(), prefix);
                if (match != null)
                {
                    return match + " ";
                }
                return null;
            }

            string head = parts[0].ToLowerInvariant();
            if (head != "item" && head != "give" && head != "items")
            {
                return null;
            }

            string token = trailingSpace ? string.Empty : parts[parts.Length - 1];
            if (token.Length == 0)
            {
                return null;
            }

            string item = ItemDb.FirstMatch(token);
            if (item == null)
            {
                return null;
            }

            var builder = new StringBuilder();
            int keep = trailingSpace ? parts.Length : parts.Length - 1;
            for (int i = 0; i < keep; i++)
            {
                builder.Append(parts[i]).Append(' ');
            }
            builder.Append(item);
            if (!trailingSpace)
            {
                // completed an id; leave the caret after it
            }
            else
            {
                builder.Append(' ');
            }
            return builder.ToString();
        }

        private static List<string> CommandNames()
        {
            var names = new List<string>(registry.Count);
            for (int i = 0; i < registry.Count; i++)
            {
                names.Add(registry[i].Name);
            }
            return names;
        }

        private static string FirstMatch(List<string> candidates, string prefix)
        {
            if (string.IsNullOrEmpty(prefix))
            {
                return null;
            }

            string best = null;
            for (int i = 0; i < candidates.Count; i++)
            {
                string c = candidates[i];
                if (c.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    if (best == null || string.CompareOrdinal(c, best) < 0)
                    {
                        best = c;
                    }
                }
            }
            return best;
        }

        internal static string[] Split(string line)
        {
            var parts = new List<string>();
            var current = new StringBuilder();
            bool quoted = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    quoted = !quoted;
                    continue;
                }
                if (!quoted && char.IsWhiteSpace(c))
                {
                    if (current.Length > 0)
                    {
                        parts.Add(current.ToString());
                        current.Length = 0;
                    }
                    continue;
                }
                current.Append(c);
            }

            if (current.Length > 0)
            {
                parts.Add(current.ToString());
            }

            return parts.ToArray();
        }
    }
}
