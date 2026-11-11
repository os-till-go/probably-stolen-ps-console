using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PSConsole
{
    /// <summary>
    /// The console window itself: a plain IMGUI overlay (no game UI assets needed)
    /// that survives scene changes because its GameObject is DontDestroyOnLoad.
    ///
    /// Keyboard hotkeys are suppressed while it is open (see Patches.cs), but the
    /// mouse is left alone: the game stays playable and a transparent uGUI blocker
    /// only eats clicks that land on the console panel itself.
    /// </summary>
    internal class ConsoleBehaviour : MonoBehaviour
    {
        private const KeyCode ToggleKey = KeyCode.BackQuote;   // the ~ key
        private const KeyCode ToggleKeyAlt = KeyCode.F10;      // fallback for keyboards without ~
        private const int MaxLines = 500;

        internal static ConsoleBehaviour Instance { get; private set; }
        internal static bool IsOpen { get { return Instance != null && Instance.open; } }

        private struct Line
        {
            internal string text;
            internal Color color;
        }

        private static readonly Color ColNeutral = new Color(0.86f, 0.89f, 0.92f);
        private static readonly Color ColOk = new Color(0.45f, 0.93f, 0.68f);
        private static readonly Color ColErr = new Color(1.00f, 0.45f, 0.45f);
        private static readonly Color ColEcho = new Color(0.52f, 0.62f, 0.70f);

        private readonly List<Line> lines = new List<Line>();
        private readonly List<string> history = new List<string>();

        private bool open;
        private string input = string.Empty;
        private int historyCursor = -1;
        private string historyDraft = string.Empty;
        private Vector2 scroll;
        private bool scrollToBottom = true;

        private Font font;
        private GUIStyle logStyle;
        private GUIStyle inputStyle;
        private GUIStyle titleStyle;
        private GUIStyle hintStyle;
        private bool stylesBuilt;
        private float uiScale = 1f;
        private int builtForHeight;

        private GameObject blocker;
        private RectTransform blockerRect;

        internal static ConsoleBehaviour Ensure()
        {
            if (Instance != null)
            {
                return Instance;
            }

            GameObject go = new GameObject("PS Console");
            UnityEngine.Object.DontDestroyOnLoad(go);
            Instance = go.AddComponent<ConsoleBehaviour>();
            return Instance;
        }

        private void Awake()
        {
            Instance = this;
            Commands.RegisterAll();
            BuildBlocker();
            Print("PS Console 1.1.0 — type 'help', 'stat', or 'items <filter>'.");
        }

        private void OnDestroy()
        {
            if (blocker != null)
            {
                UnityEngine.Object.Destroy(blocker);
            }
        }

        private void Update()
        {
            if (Input.GetKeyDown(ToggleKey) || Input.GetKeyDown(ToggleKeyAlt))
            {
                if (open)
                {
                    Close();
                }
                else
                {
                    Open();
                }
            }
        }

        private void Open()
        {
            open = true;
            scrollToBottom = true;
            historyCursor = -1;
            historyDraft = string.Empty;
            if (blocker != null)
            {
                blocker.SetActive(true);
            }
        }

        private void Close()
        {
            open = false;
            input = string.Empty;
            if (blocker != null)
            {
                blocker.SetActive(false);
            }
        }

        // ------------------------------------------------------------ click guard

        /// <summary>
        /// IMGUI cannot stop a click from reaching the game's uGUI underneath.
        /// A transparent Image at a very high sorting order absorbs pointer events
        /// exactly over the panel, and nothing anywhere else.
        /// </summary>
        private void BuildBlocker()
        {
            try
            {
                blocker = new GameObject("PS Console Blocker");
                UnityEngine.Object.DontDestroyOnLoad(blocker);

                Canvas canvas = blocker.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 30000;

                blocker.AddComponent<GraphicRaycaster>();

                GameObject hit = new GameObject("hit");
                hit.transform.SetParent(blocker.transform, false);
                Image image = hit.AddComponent<Image>();
                image.color = new Color(0f, 0f, 0f, 0f);
                image.raycastTarget = true;

                blockerRect = image.rectTransform;
                blockerRect.anchorMin = new Vector2(0f, 1f);
                blockerRect.anchorMax = new Vector2(1f, 1f);
                blockerRect.pivot = new Vector2(0.5f, 1f);
                blockerRect.anchoredPosition = Vector2.zero;
                blockerRect.sizeDelta = new Vector2(0f, 300f);

                blocker.SetActive(false);
            }
            catch (Exception e)
            {
                blocker = null;
                blockerRect = null;
                ModEntry.Log?.Warning("click blocker unavailable: " + e.Message);
            }
        }

        private void SyncBlocker(float panelHeight)
        {
            if (blockerRect == null)
            {
                return;
            }

            blockerRect.sizeDelta = new Vector2(0f, panelHeight);
        }

        // ---------------------------------------------------------------- output

        internal void AddLine(string text, Color color)
        {
            lines.Add(new Line { text = text, color = color });
            if (lines.Count > MaxLines)
            {
                lines.RemoveRange(0, lines.Count - MaxLines);
            }
            if (open)
            {
                scrollToBottom = true;
            }
        }

        internal static void Print(string text) { if (Instance != null) Instance.AddLine(text, ColNeutral); }
        internal static void Ok(string text) { if (Instance != null) Instance.AddLine(text, ColOk); }
        internal static void Err(string text) { if (Instance != null) Instance.AddLine(text, ColErr); }
        internal static void Echo(string text) { if (Instance != null) Instance.AddLine("> " + text, ColEcho); }

        internal static void ClearAll()
        {
            if (Instance != null)
            {
                Instance.lines.Clear();
            }
        }

        internal static void RequestClose()
        {
            if (Instance != null)
            {
                Instance.Close();
            }
        }

        // ------------------------------------------------------------------ gui

        private void OnGUI()
        {
            if (!open)
            {
                return;
            }

            BuildStyles();
            HandleKeys();
            Draw();
        }

        private void BuildStyles()
        {
            if (stylesBuilt && builtForHeight == Screen.height)
            {
                return;
            }

            uiScale = Mathf.Clamp(Screen.height / 1080f, 1f, 1.8f);
            int logSize = Mathf.RoundToInt(17f * uiScale);
            int gap = Mathf.RoundToInt(4f * uiScale);

            if (font == null)
            {
                try
                {
                    font = Font.CreateDynamicFontFromOSFont(
                        new[] { "Consolas", "Cascadia Mono", "Microsoft YaHei", "Segoe UI" }, logSize);
                }
                catch
                {
                    font = null;
                }
            }

            logStyle = new GUIStyle(GUI.skin.label);
            logStyle.font = font;
            logStyle.fontSize = logSize;
            logStyle.richText = false;
            logStyle.wordWrap = true;
            logStyle.padding = new RectOffset(0, 0, 0, 0);
            logStyle.margin = new RectOffset(0, 0, 0, gap);
            logStyle.normal.textColor = ColNeutral;

            inputStyle = new GUIStyle(GUI.skin.textField);
            inputStyle.font = font;
            inputStyle.fontSize = logSize;
            inputStyle.alignment = TextAnchor.MiddleLeft;
            inputStyle.padding = new RectOffset(Mathf.RoundToInt(10f * uiScale), 8, 6, 6);

            titleStyle = new GUIStyle(logStyle);
            titleStyle.fontSize = Mathf.Max(12, logSize - 2);
            titleStyle.fontStyle = FontStyle.Bold;
            titleStyle.normal.textColor = new Color(0.38f, 0.90f, 0.88f);

            hintStyle = new GUIStyle(logStyle);
            hintStyle.fontSize = Mathf.Max(11, logSize - 4);
            hintStyle.normal.textColor = new Color(0.50f, 0.56f, 0.62f);

            stylesBuilt = true;
            builtForHeight = Screen.height;
        }

        private void HandleKeys()
        {
            Event e = Event.current;
            if (e == null || e.type != EventType.KeyDown)
            {
                return;
            }

            if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
            {
                Submit();
                e.Use();
            }
            else if (e.keyCode == KeyCode.Tab)
            {
                Complete();
                e.Use();
            }
            else if (e.keyCode == KeyCode.UpArrow)
            {
                WalkHistory(-1);
                e.Use();
            }
            else if (e.keyCode == KeyCode.DownArrow)
            {
                WalkHistory(1);
                e.Use();
            }
            else if (e.keyCode == KeyCode.Escape)
            {
                Close();
                e.Use();
            }
        }

        private void Submit()
        {
            string text = (input ?? string.Empty).Trim();
            input = string.Empty;
            historyCursor = -1;
            if (text.Length == 0)
            {
                return;
            }

            history.Add(text);
            if (history.Count > 100)
            {
                history.RemoveAt(0);
            }

            CommandProcessor.Execute(text);
        }

        private void Complete()
        {
            string completed = CommandProcessor.Suggest(input);
            if (completed != null)
            {
                input = completed;
            }
        }

        private void WalkHistory(int direction)
        {
            if (history.Count == 0)
            {
                return;
            }

            if (historyCursor == -1)
            {
                historyDraft = input;
                historyCursor = history.Count;
            }

            historyCursor += direction;
            if (historyCursor < 0)
            {
                historyCursor = 0;
            }
            if (historyCursor >= history.Count)
            {
                historyCursor = history.Count;
                input = historyDraft;
                return;
            }

            input = history[historyCursor];
        }

        private void Draw()
        {
            float height = Mathf.Min(Screen.height * 0.62f, 780f);
            float width = Screen.width;
            int margin = Mathf.RoundToInt(18f * uiScale);
            float inputHeight = Mathf.RoundToInt(34f * uiScale);
            float titleHeight = Mathf.RoundToInt(26f * uiScale);
            float barHeight = inputHeight + Mathf.RoundToInt(14f * uiScale);

            DrawRect(new Rect(0f, 0f, width, height), new Color(0.035f, 0.045f, 0.060f, 0.96f));
            DrawRect(new Rect(0f, height - 2f, width, 2f), new Color(0.25f, 0.85f, 0.82f, 0.55f));

            GUI.Label(new Rect(margin, 10f, width - margin * 2f, titleHeight),
                "PS CONSOLE   ·   help   ·   stat   ·   items <filter>", titleStyle);

            Rect logRect = new Rect(margin, titleHeight + 8f, width - margin * 2f,
                height - titleHeight - 8f - barHeight);

            if (scrollToBottom)
            {
                scroll.y = float.MaxValue;
                scrollToBottom = false;
            }

            GUILayout.BeginArea(logRect);
            scroll = GUILayout.BeginScrollView(scroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
            for (int i = 0; i < lines.Count; i++)
            {
                GUI.contentColor = lines[i].color;
                GUILayout.Label(lines[i].text, logStyle);
            }
            GUI.contentColor = Color.white;
            GUILayout.EndScrollView();
            GUILayout.EndArea();

            DrawRect(new Rect(0f, height - barHeight, width, barHeight), new Color(0f, 0f, 0f, 0.40f));

            float fieldX = margin + Mathf.RoundToInt(16f * uiScale);
            float fieldW = width - fieldX - margin - 210f * uiScale;

            GUI.Label(new Rect(margin, height - barHeight + 6f, margin, inputHeight), ">", titleStyle);

            GUI.SetNextControlName("psc_input");
            input = GUI.TextField(
                new Rect(fieldX, height - barHeight + 5f, fieldW, inputHeight),
                input, 512, inputStyle);

            GUI.Label(
                new Rect(width - margin - 200f * uiScale, height - barHeight + 10f, 200f * uiScale, titleHeight),
                "tab complete · esc close", hintStyle);

            if (Event.current.type == EventType.Repaint && GUI.GetNameOfFocusedControl() != "psc_input")
            {
                GUI.FocusControl("psc_input");
            }

            SyncBlocker(height);
        }

        private static void DrawRect(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }
    }
}
