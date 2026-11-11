# PS Console

An in-game console for **Probably Stolen**. Press `~`, type a command, done.

```
> stat
cash         : 1240
wild favor   : 12
day          : 9
...
> item bandage_item 5
spawned 5 x bandage_item (Bandage) -> backpack
```

Built against the game's **native mod loader** (`Mods/<id>/manifest.xml` + a DLL). No BepInEx, no MelonLoader, no `dev.txt`.

## Features

- **Items** — spawn any item the game knows about, into the backpack, front counter, back counter, display case or trash.
- **Names, not just ids** — `items bandage` searches ids *and* the in-game display name, and prints both side by side. `item Bandage 5` works too.
- **Numbers** — cash, Wild's favor, all six factions' reputation, rent, loan, day counter, store attractiveness, luck, wounds.
- **Readable state** — `stat` dumps the whole run on one screen.
- **Doesn't fight the game** — while the console is open the keyboard hotkeys are suppressed, but the mouse keeps working. A transparent blocker only over the panel stops clicks from falling through to the shelf behind it.

## Install

Grab `ps-console-1.1.0.zip` from [Releases](../../releases/latest) — it contains a ready-to-drop `ps_console` folder.

1. Drop the `ps_console` folder into `Probably Stolen/Mods/`.
2. Launch the game → **Mods** in the main menu → select **PS Console** → **Activate**.
3. Restart the game (newly activated mods load on the next start).
4. Press `~` (or `F10` if your keyboard layout has no backquote) once you are in a save.

## Commands

| Command | What it does |
| --- | --- |
| `help [command]` | List everything, or explain one command |
| `stat` | Cash, favor, day, rent, loan, attractiveness, health, reputation |
| `cash +500` / `cash -200` / `cash =1000` | Add, subtract or set cash |
| `favor +10` | Wild's favor |
| `rep` / `rep list` | List faction reputation |
| `rep security +50` | One faction |
| `rep all =100` | Every faction (the game clamps to -200..200) |
| `item <id or name> [count] [-f\|-b\|-d\|-t]` | Spawn items: `-f` front counter, `-b` back counter, `-d` display case, `-t` trash, default backpack |
| `give …` | Alias of `item` |
| `items [filter]` | Search ids and display names |
| `heal` | Clear wounds |
| `luck 80` | Set luck (0–100) |
| `day +1` | Move the day counter (can desync scheduled events) |
| `rent -200` / `loan -200` | Rent / loan due |
| `attract +100` | Base store attractiveness |
| `clear` / `close` | Clear the log / close the window |

**Number syntax:** `+N` adds, `-N` subtracts, `=N` sets, a bare `N` also sets.

**Faction shorthands:** `lower`, `upper`, `security` (`sec`), `blackmarket` (`bm`), `revolution` (`rev`), `tourist`, `church`, `cartel`.

**Keys:** `Tab` completes command names and item ids, `↑`/`↓` walk the history, `Esc` closes, `Enter` runs.

## How it works

### Keyboard yes, mouse no

Two Harmony postfixes make the game think a text field has focus while the console is open:

- `StoreUIManager.IsAnyTyping` — gates every keyboard action handler.
- `KeyBindHelper.get_SuppressHotkeys` — catches the shortcuts that read keys directly.

The mouse is deliberately left alone. The panel sits on a separate `Canvas` (`sortingOrder` 30000) with a transparent `Image` and a `GraphicRaycaster`, so clicks that land on the panel are absorbed before they reach the game's uGUI, while the rest of the screen stays fully playable.

> v1.0.0 instead skipped `InputActionManager.Update` and disabled the `EventSystem`; that killed the mouse. Don't do that.

### Item id ↔ display name

Item display names are **not** derived from ids. Most live in the `Item` localisation table under hand-written keys (`bandage_item` → `item_bandage_name`, `battery100` → `item_energy_credit_name`). `ItemDb.Name(id)` resolves in three steps:

1. Try `item_<id>_name`, `<id>_name`, and the `_item`-stripped variants in the table (cheap, prefilled in bulk).
2. Otherwise build one throwaway item via `DirectoryMaster.Item(id)`, read `GetDisplayName()`, and `Destroy()` it immediately. It never reaches an inventory. (`PermitHelper.OnNewItemCreated` only touches that temporary item's own contraband flag.)
3. Otherwise prettify the id (`Bandage Item`).

Results are cached per id; searching uses the cheap path so a single query never instantiates a thousand items.

### Game APIs used

Reverse-engineered from `Assembly-CSharp.dll` — the public modding guide doesn't document most of these.

| What | API |
| --- | --- |
| Cash | `PlayerStore.Instance.playerCash`, `ModCash(int, bool)` |
| Wild's favor | `PlayerStore.Instance.wildFavor` |
| Faction reputation | `PlayerStore.Instance.storeReputations`, `StoreReputation.GetReputation()/SetReputation(double)` |
| Day | `StoreStation.Instance.dayCounter` |
| All item ids | `DirectoryMaster.GetIdentifierList<GameItem>()` |
| Spawning | `ModHelper.SpawnItem(id)` + `ModHelper.MoveItemTo*` |
| Item containers | `EmporiumEntry.Instance` (null in the main menu, so spawns need a loaded save) |
| Health / luck | `PlayerStore.Instance.healthData` |

## Build from source

Requires the .NET SDK (6.0+) and a copy of the game.

```bash
cd src/PSConsole
dotnet build -c Release -p:GameDir="C:\Program Files (x86)\Steam\steamapps\common\Probably Stolen"
```

`GameDir` may also come from the `PS_GAME_DIR` environment variable, and defaults to the standard Steam path. A successful build copies `PSConsole.dll` and `manifest.xml` into `<GameDir>/Mods/ps_console/`.

Because the DLL changes on every build, the game asks you to **Review and enable** again — turn on **Developer mode** in the mod's detail pane once and it trusts every later rebuild.

### Layout

```
src/PSConsole/
├── ModEntry.cs          IMod entry point; creates the DontDestroyOnLoad console object
├── ConsoleBehaviour.cs  The IMGUI window: input, history, completion, scrolling, toggle
├── CommandProcessor.cs  Command registry, argument parsing, tab completion
├── Commands.cs          Command implementations + the item database
└── Patches.cs           The two Harmony postfixes
```

The loader applies `[HarmonyPatch]` classes itself, so the mod must **not** call `PatchAll` anywhere.

## Notes and limitations

- `rep`, `cash` and friends edit the **in-memory** run. Save the game to persist.
- `day` moves the counter directly and can desync scheduled events and rent timers. Save first.
- The console does not pause the game; time keeps running while it is open.
- Item name lookups match the current game language where the table has an entry; the rest fall back to the English id.

### Why there is no `<GameVersions>` in the manifest

The mod menu compares that list against `Application.version` after stripping everything but digits and dots. This build reports no usable version string, so *any* entry is flagged red even though the mod loads fine. An empty list shows `-` and raises no warning. If the game starts reporting a proper version, add `<GameVersions><Version>1.0.0</Version></GameVersions>` back.

## Compatibility

Tested against **Probably Stolen 100-Rev6**. Should keep working across revisions of 1.0.0, but a game update that renames the patched methods will make the mod fail to load — check the log and open an issue.

Not affiliated with Questing Goose Studio. No game files are included or redistributed; you need your own copy of the game to build.

## See also

Other console-style mods for the same game, if you just want the features rather than the source:

- [Debug Console Plus](https://www.nexusmods.com/probablystolen/mods/257) — ButterLab, `Ctrl+Shift+I`, the most complete one (UI panels, item attributes, NPCs, events)
- [PSConsole](https://www.nexusmods.com/probablystolen/mods/554) — muchengbai, `/`
- [Console Mod](https://www.nexusmods.com/probablystolen/mods/618) — BBQ886, `~`

## License

MIT — see [LICENSE](LICENSE).

---

中文说明见 [README.zh-CN.md](README.zh-CN.md)。
