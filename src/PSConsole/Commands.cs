using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Localization.Settings;

namespace PSConsole
{
    internal static class ItemDb
    {
        private static List<string> ids;
        private static Dictionary<string, string> lookup;
        private static Dictionary<string, string> names;
        private static bool namesUnavailable;
        private static readonly Dictionary<string, string> realNames =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        internal static void Refresh()
        {
            List<string> raw;
            try
            {
                raw = DirectoryMaster.GetIdentifierList<GameItem>();
            }
            catch
            {
                raw = new List<string>();
            }

            ids = raw
                .Where(id => !string.IsNullOrEmpty(id) && !ItemSpawnerHiddenList.IsHidden(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToList();

            lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < ids.Count; i++)
            {
                lookup[ids[i]] = ids[i];
            }

            names = null;
            namesUnavailable = false;
        }

        internal static List<string> All()
        {
            if (ids == null)
            {
                Refresh();
            }
            return ids;
        }

        /// <summary>
        /// Best-effort bulk pass over the game's "Item" string table. Most items use
        /// item_&lt;id&gt;_name, but plenty use hand-written keys (bandage_item is
        /// item_bandage_name), so this only fills the cheap half of the cache.
        /// </summary>
        internal static void EnsureNames()
        {
            if (names != null || namesUnavailable)
            {
                return;
            }

            try
            {
                var table = LocalizationSettings.StringDatabase.GetTable("Item");
                if (table == null)
                {
                    namesUnavailable = true;
                    return;
                }

                List<string> all = All();
                var map = new Dictionary<string, string>(names ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < all.Count; i++)
                {
                    string id = all[i];
                    string trimmed = TrimSuffix(id, "_item");
                    string value = Entry(table, "item_" + id + "_name")
                                ?? Entry(table, id + "_name")
                                ?? Entry(table, "item_" + trimmed + "_name")
                                ?? Entry(table, trimmed + "_name")
                                ?? Entry(table, id);
                    if (!string.IsNullOrEmpty(value))
                    {
                        map[id] = value;
                    }
                }
                names = map;
            }
            catch
            {
                namesUnavailable = true;
            }
        }

        private static string TrimSuffix(string value, string suffix)
        {
            return value != null && value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                ? value.Substring(0, value.Length - suffix.Length)
                : value;
        }

        private static string Entry(object table, string key)
        {
            try
            {
                var localisationTable = table as UnityEngine.Localization.Tables.StringTable;
                return localisationTable == null ? null : localisationTable.GetEntry(key)?.Value;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Cheap name lookup used for searching: the "Item" string table when the
        /// key exists, otherwise the prettified id. Never instantiates anything.
        /// </summary>
        internal static string CheapName(string id)
        {
            string cached;
            if (realNames.TryGetValue(id, out cached))
            {
                return cached;
            }

            if (names != null)
            {
                string value;
                if (names.TryGetValue(id, out value))
                {
                    return value;
                }
            }
            return Prettify(id);
        }

        /// <summary>
        /// The name the game actually shows. Falls back to building one throwaway
        /// item (destroyed immediately) because several items use bespoke
        /// localisation keys that cannot be derived from the id.
        /// </summary>
        internal static string Name(string id)
        {
            string cached;
            if (realNames.TryGetValue(id, out cached))
            {
                return cached;
            }

            if (names != null)
            {
                string tableName;
                if (names.TryGetValue(id, out tableName) && !string.IsNullOrEmpty(tableName))
                {
                    realNames[id] = tableName;
                    return tableName;
                }
            }

            try
            {
                GameItem probe = DirectoryMaster.Item(id);
                if (probe != null)
                {
                    string live = probe.GetDisplayName();
                    probe.Destroy();

                    if (!string.IsNullOrEmpty(live) && !string.Equals(live, id, StringComparison.OrdinalIgnoreCase))
                    {
                        realNames[id] = live;
                        return live;
                    }
                }
            }
            catch
            {
                // fall through to the prettified id
            }

            string pretty = Prettify(id);
            realNames[id] = pretty;
            return pretty;
        }

        private static string Prettify(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return string.Empty;
            }

            string[] parts = id.Split('_');
            var builder = new StringBuilder();
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i];
                if (part.Length == 0)
                {
                    continue;
                }
                if (builder.Length > 0)
                {
                    builder.Append(' ');
                }
                builder.Append(char.ToUpperInvariant(part[0]));
                if (part.Length > 1)
                {
                    builder.Append(part.Substring(1));
                }
            }
            return builder.ToString();
        }

        internal static string ExactId(string token)
        {
            if (ids == null)
            {
                Refresh();
            }

            string exact;
            return lookup.TryGetValue(token, out exact) ? exact : null;
        }

        /// <summary>Matches against the id and the display name; returns at most max hits.</summary>
        internal static List<string> Match(string token, int max, out int total)
        {
            List<string> all = All();
            EnsureNames();

            var hits = new List<string>(Mathf.Min(max, 16));
            total = 0;

            for (int i = 0; i < all.Count; i++)
            {
                string id = all[i];
                bool hit = id.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0
                        || CheapName(id).IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
                if (!hit)
                {
                    continue;
                }

                total++;
                if (hits.Count < max)
                {
                    hits.Add(id);
                }
            }
            return hits;
        }

        internal static string FirstMatch(string prefix)
        {
            if (string.IsNullOrEmpty(prefix))
            {
                return null;
            }

            List<string> all = All();
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return all[i];
                }
            }
            return null;
        }
    }

    internal static class Commands
    {
        private static readonly Dictionary<string, string> FactionAliases =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "lower", "FACTION_LOWER_LEVEL" },
                { "low", "FACTION_LOWER_LEVEL" },
                { "upper", "FACTION_UPPER_LEVEL" },
                { "up", "FACTION_UPPER_LEVEL" },
                { "security", "FACTION_SECURITY" },
                { "sec", "FACTION_SECURITY" },
                { "blackmarket", "FACTION_BLACK_MARKET" },
                { "black", "FACTION_BLACK_MARKET" },
                { "bm", "FACTION_BLACK_MARKET" },
                { "revolution", "FACTION_REVOLUTION" },
                { "rev", "FACTION_REVOLUTION" },
                { "tourist", "FACTION_TOURIST" },
                { "tourists", "FACTION_TOURIST" },
                { "church", "FACTION_CHURCH" },
                { "cartel", "FACTION_CARTEL" }
            };

        internal static void RegisterAll()
        {
            CommandProcessor.Register("help", "[command]", "list every command, or describe one", Help);
            CommandProcessor.Register("stat", "", "print cash, favor, day, rent, loan, health and reputation", Stat);
            CommandProcessor.Register("cash", "<+N | -N | =N>", "add, subtract or set player cash", Cash);
            CommandProcessor.Register("favor", "<+N | -N | =N>", "Wild's favor", Favor);
            CommandProcessor.Register("rep", "[list | <faction> <+N | =N> | all <N|+N>]", "faction reputation (-200..200)", Rep);
            CommandProcessor.Register("item", "<id | name> [count] [-f|-b|-d|-t]", "spawn items into the backpack (-f front counter, -b back, -d display, -t trash)", Item);
            CommandProcessor.Register("give", "<id | name> [count] [-f|-b|-d|-t]", "alias of item", Item);
            CommandProcessor.Register("items", "[filter]", "search item ids and display names", ListItems);
            CommandProcessor.Register("heal", "", "clear wounds", Heal);
            CommandProcessor.Register("luck", "<N>", "set luck (0..100)", Luck);
            CommandProcessor.Register("day", "<+N | =N>", "change the day counter", Day);
            CommandProcessor.Register("rent", "<+N | -N | =N>", "change the rent due", Rent);
            CommandProcessor.Register("loan", "<+N | -N | =N>", "change the loan due", Loan);
            CommandProcessor.Register("attract", "<+N | -N | =N>", "change base store attractiveness", Attract);
            CommandProcessor.Register("clear", "", "clear the console", Clear);
            CommandProcessor.Register("close", "", "close the console", Close);
        }

        // ------------------------------------------------------------- plumbing

        private static PlayerStore Store()
        {
            return PlayerStore.Instance;
        }

        private static bool Apply(string arg, int current, out int result)
        {
            result = current;
            if (string.IsNullOrEmpty(arg))
            {
                return false;
            }

            char sign = arg[0];
            int value;

            if (sign == '+' || sign == '-')
            {
                if (!int.TryParse(arg.Substring(1), out value))
                {
                    return false;
                }
                result = current + (sign == '-' ? -value : value);
                return true;
            }

            if (sign == '=')
            {
                arg = arg.Substring(1);
            }

            if (!int.TryParse(arg, out value))
            {
                return false;
            }
            result = value;
            return true;
        }

        private static void Usage(string example)
        {
            ConsoleBehaviour.Err("usage: " + example);
        }

        // ------------------------------------------------------------- commands

        private static void Help(string[] args)
        {
            if (args.Length > 0)
            {
                ConsoleCommand command = CommandProcessor.Find(args[0]);
                if (command == null)
                {
                    ConsoleBehaviour.Err("unknown command '" + args[0] + "'");
                    return;
                }
                ConsoleBehaviour.Print(command.Name + " " + command.Args);
                ConsoleBehaviour.Print("    " + command.Help);
                return;
            }

            IReadOnlyList<ConsoleCommand> all = CommandProcessor.All;
            for (int i = 0; i < all.Count; i++)
            {
                ConsoleCommand c = all[i];
                string line = "  " + c.Name.PadRight(10) + c.Args;
                ConsoleBehaviour.Print(line);
                ConsoleBehaviour.Print("      " + c.Help);
            }
            ConsoleBehaviour.Print("modifiers: +N adds, -N subtracts, =N sets (bare N sets too).");
        }

        private static void Stat(string[] args)
        {
            PlayerStore store = Store();
            ConsoleBehaviour.Print("cash         : " + store.playerCash);
            ConsoleBehaviour.Print("wild favor   : " + store.wildFavor);
            ConsoleBehaviour.Print("day          : " + StoreStation.Instance.dayCounter);
            ConsoleBehaviour.Print("rent due     : " + store.rentValue + " (in " + store.dayUntilRent + " day(s))");
            ConsoleBehaviour.Print("loan due     : " + store.loanValue + " (in " + store.dayUntilLoan + " day(s))");
            ConsoleBehaviour.Print("attractiveness: " + store.baseStoreAttractiveness);

            if (store.healthData != null)
            {
                ConsoleBehaviour.Print("wound / luck : " + store.healthData.woundState + " / " + store.healthData.luck);
            }

            List<StoreReputation> reputations = store.storeReputations;
            if (reputations == null || reputations.Count == 0)
            {
                ConsoleBehaviour.Print("reputation   : (not initialised yet)");
                return;
            }

            for (int i = 0; i < reputations.Count; i++)
            {
                StoreReputation rep = reputations[i];
                ConsoleBehaviour.Print("rep " + (rep.factionDisplay ?? rep.factionId).PadRight(16) + rep.GetReputation());
            }
        }

        private static void Cash(string[] args)
        {
            if (args.Length != 1)
            {
                Usage("cash +500   |   cash -200   |   cash =1000");
                return;
            }

            PlayerStore store = Store();
            int current = store.playerCash;
            int target;
            if (!Apply(args[0], current, out target))
            {
                Usage("cash +500");
                return;
            }

            int delta = target - current;
            try
            {
                store.ModCash(delta, true);
            }
            catch
            {
                store.playerCash = target;
            }

            ConsoleBehaviour.Ok("cash " + current + " -> " + store.playerCash);
        }

        private static void Favor(string[] args)
        {
            if (args.Length != 1)
            {
                Usage("favor +10   |   favor =50");
                return;
            }

            PlayerStore store = Store();
            int current = store.wildFavor;
            int target;
            if (!Apply(args[0], current, out target))
            {
                Usage("favor +10");
                return;
            }

            store.wildFavor = target;
            ConsoleBehaviour.Ok("wild favor " + current + " -> " + store.wildFavor);
        }

        private static void Rep(string[] args)
        {
            PlayerStore store = Store();
            List<StoreReputation> list = store.storeReputations;

            if (list == null || list.Count == 0)
            {
                ConsoleBehaviour.Err("reputation is not initialised yet — load a save first.");
                return;
            }

            if (args.Length == 0 || (args.Length == 1 && args[0].Equals("list", StringComparison.OrdinalIgnoreCase)))
            {
                for (int i = 0; i < list.Count; i++)
                {
                    StoreReputation rep = list[i];
                    ConsoleBehaviour.Print((rep.factionDisplay ?? rep.factionId).PadRight(18)
                        + rep.GetReputation().ToString().PadLeft(5) + "   [" + rep.factionId + "]");
                }
                ConsoleBehaviour.Print("usage: rep <faction> <+N | =N>   |   rep all <N|+N>");
                return;
            }

            if (args.Length < 2)
            {
                Usage("rep security +50");
                return;
            }

            string factionArg = args[0];
            string amountArg = args[1];

            if (factionArg.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                int dummy;
                if (!Apply(amountArg, 0, out dummy))
                {
                    Usage("rep all =100");
                    return;
                }

                for (int i = 0; i < list.Count; i++)
                {
                    ApplyRep(list[i], amountArg);
                }
                ConsoleBehaviour.Ok("applied " + amountArg + " to all factions");
                return;
            }

            string factionId;
            if (!FactionAliases.TryGetValue(factionArg, out factionId))
            {
                factionId = factionArg.ToUpperInvariant();
            }

            StoreReputation target = null;
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i].factionId, factionId, StringComparison.OrdinalIgnoreCase))
                {
                    target = list[i];
                    break;
                }
            }

            if (target == null)
            {
                ConsoleBehaviour.Err("unknown faction '" + factionArg + "'. known: "
                    + string.Join(", ", list.Select(r => r.factionId).ToArray()));
                return;
            }

            ApplyRep(target, amountArg);
        }

        private static void ApplyRep(StoreReputation rep, string amountArg)
        {
            int before = rep.GetReputation();
            int target;
            if (!Apply(amountArg, before, out target))
            {
                Usage("rep security +50");
                return;
            }

            try
            {
                rep.SetReputation(target);
            }
            catch (Exception e)
            {
                ConsoleBehaviour.Err("could not set reputation: " + e.Message);
                return;
            }

            ConsoleBehaviour.Ok("rep " + (rep.factionDisplay ?? rep.factionId) + " " + before + " -> " + rep.GetReputation());
        }

        private static void Item(string[] args)
        {
            if (args.Length < 1)
            {
                Usage("item health_pack 3");
                return;
            }

            string token = args[0];
            int count = 1;
            char destination = 'i';

            for (int i = 1; i < args.Length; i++)
            {
                string arg = args[i].ToLowerInvariant();
                if (arg == "-f" || arg == "--front")
                {
                    destination = 'f';
                }
                else if (arg == "-b" || arg == "--back")
                {
                    destination = 'b';
                }
                else if (arg == "-d" || arg == "--display")
                {
                    destination = 'd';
                }
                else if (arg == "-t" || arg == "--trash")
                {
                    destination = 't';
                }
                else if (!int.TryParse(arg, out count))
                {
                    ConsoleBehaviour.Err("unexpected argument '" + arg + "'");
                    return;
                }
            }

            if (EmporiumEntry.Instance == null)
            {
                ConsoleBehaviour.Err("no running store — load a save before spawning items.");
                return;
            }

            ItemDb.Refresh();

            string id = ItemDb.ExactId(token);
            if (id == null)
            {
                int total;
                List<string> hits = ItemDb.Match(token, 6, out total);
                if (total == 0)
                {
                    ConsoleBehaviour.Err("nothing matches '" + token + "' (id or name) — try: items " + token);
                    return;
                }
                if (total > 1)
                {
                    ConsoleBehaviour.Err("'" + token + "' is ambiguous (" + total + "), try an id:");
                    for (int i = 0; i < hits.Count; i++)
                    {
                        ConsoleBehaviour.Print("  " + hits[i].PadRight(26) + ItemDb.Name(hits[i]));
                    }
                    return;
                }
                id = hits[0];
            }

            count = Mathf.Clamp(count, 1, 200);
            int spawned = 0;
            string display = "";

            for (int i = 0; i < count; i++)
            {
                GameItem item;
                try
                {
                    item = ModHelper.SpawnItem(id);
                }
                catch (Exception e)
                {
                    ConsoleBehaviour.Err("spawn failed: " + e.Message);
                    break;
                }

                if (item == null)
                {
                    ConsoleBehaviour.Err("spawn returned null for '" + id + "'");
                    break;
                }

                try
                {
                    Place(item, destination);
                }
                catch (Exception e)
                {
                    ConsoleBehaviour.Err("could not move the item: " + e.Message);
                    break;
                }

                display = item.GetDisplayName();
                spawned++;
            }

            if (spawned > 0)
            {
                ConsoleBehaviour.Ok("spawned " + spawned + " x " + id
                    + (display.Length > 0 ? " (" + display + ")" : "")
                    + " -> " + DestinationName(destination));
            }
        }

        private static void Place(GameItem item, char destination)
        {
            switch (destination)
            {
                case 'f':
                    ModHelper.MoveItemToFrontCounter(item);
                    break;
                case 'b':
                    ModHelper.MoveItemToBackCounter(item);
                    break;
                case 'd':
                    ModHelper.MoveItemToDisplayCase(item);
                    break;
                case 't':
                    ModHelper.MoveItemToTrash(item);
                    break;
                default:
                    ModHelper.MoveItemToInventory(item);
                    break;
            }
        }

        private static string DestinationName(char destination)
        {
            switch (destination)
            {
                case 'f': return "front counter";
                case 'b': return "back counter";
                case 'd': return "display case";
                case 't': return "trash";
                default: return "backpack";
            }
        }

        private static void ListItems(string[] args)
        {
            ItemDb.Refresh();
            ItemDb.EnsureNames();

            string filter = args.Length > 0 ? args[0] : string.Empty;
            List<string> matches;
            int total;

            if (filter.Length == 0)
            {
                matches = ItemDb.All();
                total = matches.Count;
                if (matches.Count > 40)
                {
                    matches = matches.GetRange(0, 40);
                }
            }
            else
            {
                matches = ItemDb.Match(filter, 40, out total);
            }

            if (total == 0)
            {
                ConsoleBehaviour.Err("nothing matches '" + filter + "' (id or name)");
                return;
            }

            for (int i = 0; i < matches.Count; i++)
            {
                string id = matches[i];
                ConsoleBehaviour.Print("  " + id.PadRight(30) + ItemDb.Name(id));
            }

            if (total > matches.Count)
            {
                ConsoleBehaviour.Print("... " + (total - matches.Count) + " more, narrow it with: items <filter>");
            }
            ConsoleBehaviour.Print(total + " item(s) — spawn with: item <id 或名字> [count] [-f|-b|-d|-t]");
        }

        private static void Heal(string[] args)
        {
            PlayerStore store = Store();
            if (store.healthData == null)
            {
                ConsoleBehaviour.Err("no health data yet — load a save.");
                return;
            }

            store.healthData.woundState = 0;
            store.healthData.isWoundStable = true;
            store.healthData.isWoundedFresh = false;
            ConsoleBehaviour.Ok("wounds cleared");
        }

        private static void Luck(string[] args)
        {
            PlayerStore store = Store();
            if (store.healthData == null)
            {
                ConsoleBehaviour.Err("no health data yet — load a save.");
                return;
            }

            int value;
            if (args.Length != 1 || !int.TryParse(args[0], out value))
            {
                Usage("luck 80");
                return;
            }

            int before = store.healthData.luck;
            store.healthData.luck = Mathf.Clamp(value, 0, 100);
            ConsoleBehaviour.Ok("luck " + before + " -> " + store.healthData.luck);
        }

        private static void Day(string[] args)
        {
            if (args.Length != 1)
            {
                Usage("day +1   |   day =12");
                return;
            }

            int current = StoreStation.Instance.dayCounter;
            int target;
            if (!Apply(args[0], current, out target))
            {
                Usage("day +1");
                return;
            }

            StoreStation.Instance.dayCounter = Mathf.Max(1, target);
            ConsoleBehaviour.Ok("day " + current + " -> " + StoreStation.Instance.dayCounter);
            ConsoleBehaviour.Print("note: jumping days can confuse scheduled events and rent timers.");
        }

        private static void Rent(string[] args)
        {
            if (args.Length != 1)
            {
                Usage("rent -200   |   rent =0");
                return;
            }

            PlayerStore store = Store();
            int current = store.rentValue;
            int target;
            if (!Apply(args[0], current, out target))
            {
                Usage("rent -200");
                return;
            }

            store.rentValue = Mathf.Max(0, target);
            ConsoleBehaviour.Ok("rent " + current + " -> " + store.rentValue);
        }

        private static void Loan(string[] args)
        {
            if (args.Length != 1)
            {
                Usage("loan -200   |   loan =0");
                return;
            }

            PlayerStore store = Store();
            int current = store.loanValue;
            int target;
            if (!Apply(args[0], current, out target))
            {
                Usage("loan -200");
                return;
            }

            store.loanValue = Mathf.Max(0, target);
            ConsoleBehaviour.Ok("loan " + current + " -> " + store.loanValue);
        }

        private static void Attract(string[] args)
        {
            if (args.Length != 1)
            {
                Usage("attract +100   |   attract =400");
                return;
            }

            PlayerStore store = Store();
            int current = store.baseStoreAttractiveness;
            int target;
            if (!Apply(args[0], current, out target))
            {
                Usage("attract +100");
                return;
            }

            store.baseStoreAttractiveness = Mathf.Max(0, target);
            ConsoleBehaviour.Ok("attractiveness " + current + " -> " + store.baseStoreAttractiveness);
        }

        private static void Clear(string[] args)
        {
            ConsoleBehaviour.ClearAll();
        }

        private static void Close(string[] args)
        {
            ConsoleBehaviour.RequestClose();
        }
    }
}
