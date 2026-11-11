using System;
using UnityEngine;

namespace PSConsole
{
    /// <summary>
    /// Mod entry point. The mod loader calls Init -> OnEnable, and applies every
    /// [HarmonyPatch] class in this assembly automatically (do not call PatchAll here).
    /// </summary>
    public class ModEntry : IMod
    {
        internal const string MOD_ID = "ps_console";
        internal static ModLog Log;

        public void Init(ModManifest manifest)
        {
            Log = new ModLog(manifest);
            try
            {
                ConsoleBehaviour.Ensure();
            }
            catch (Exception e)
            {
                Log.Error("could not create the console object: " + e);
            }
        }

        public void OnEnable()
        {
            Log.Log("ready — press ~ (or F10) to open the console. Type 'help'.");
        }

        public void OnDisable()
        {
            try
            {
                if (ConsoleBehaviour.Instance != null)
                {
                    UnityEngine.Object.Destroy(ConsoleBehaviour.Instance.gameObject);
                }
            }
            catch
            {
                // shutting down anyway
            }
            Log.Log("disabled.");
        }
    }
}
