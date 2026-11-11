using HarmonyLib;

namespace PSConsole
{
    /// <summary>
    /// The game's own "is the player typing in a text field" check gates every
    /// keyboard action handler, so typing "q" no longer rotates the held item.
    /// The mouse is deliberately NOT touched: the player can keep playing.
    /// </summary>
    [HarmonyPatch(typeof(StoreUIManager), "IsAnyTyping")]
    internal static class Patch_StoreUIManager_IsAnyTyping
    {
        private static void Postfix(ref bool __result)
        {
            if (ConsoleBehaviour.IsOpen)
            {
                __result = true;
            }
        }
    }

    /// <summary>
    /// A few shortcuts read KeyBindHelper.GetKeyDown directly instead of going
    /// through InputActionManager, so the typing check above misses them.
    /// </summary>
    [HarmonyPatch(typeof(KeyBindHelper), "get_SuppressHotkeys")]
    internal static class Patch_KeyBindHelper_SuppressHotkeys
    {
        private static void Postfix(ref bool __result)
        {
            if (ConsoleBehaviour.IsOpen)
            {
                __result = true;
            }
        }
    }
}
