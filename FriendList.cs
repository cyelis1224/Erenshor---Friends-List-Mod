using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using BepInEx.Logging;
using System.Reflection;

namespace Erenshor_FriendList
{
    [BepInPlugin(ModGUID, ModName, ModVersion)]
    [BepInProcess("Erenshor.exe")]
    public class FriendListMod : BaseUnityPlugin
    {
        internal const string ModName = "FriendListMod";
        internal const string ModVersion = "1.0.1";
        internal const string ModDescription = "Friend List for Erenshor";
        internal const string Author = "Cyelis";
        private const string ModGUID = Author + "." + ModName;
        
        internal static FriendListMod context;
        internal static readonly ManualLogSource FriendListLogger = BepInEx.Logging.Logger.CreateLogSource(ModName);
        
        private readonly Harmony harmony = new Harmony(ModGUID);
        
        // List to store friends
        private List<string> friendList = new List<string>();
        
        // UI variables
        private bool showFriendList = false;
        private bool showConfirmationDialog = false;
        private string friendToRemove = "";
        private Vector2 scrollPosition;
        private Rect windowRect;
        private int windowId = 9876; // Unique window ID
        private GUIStyle headerStyle;
        private GUIStyle friendNameStyle;
        private GUIStyle classInfoStyle;
        private GUIStyle iconButtonStyle;
        private GUIStyle buttonStyle; // Add this back for text buttons
        private bool stylesInitialized = false;
        
        // UI Button for main HUD
        private GameObject friendListButton;
        
        // Add these variables for tooltip handling
        private bool showTooltip = false;
        private string tooltipText = "";
        private Rect tooltipRect;
        private GUIStyle tooltipStyle;
        
        // Add these variables for dragging
        private bool isDraggingWindow = false;
        private Vector2 dragOffset = Vector2.zero;
        
        // GroupBuilder-style textures
        private Texture2D windowBG, borderGlow, entryBG, entryHoverBG;
        private Texture2D diamondTex;        
        private UIAnchors uiAnchors;
        private Transform groupAnchor;
        
        // ────── CONFIGURATION CONSTANTS ───────────────────────────
        private const float kWindowBorderThickness = 2f;
        private const float kIconSize               = 34f;
        private const float kIconBorderThickness    = 2f;
        private const float kIconSpacing            = 8f;
        private const float kRowLeftPadding         = 8f;   // indent names off left edge
        private const float kRowRightPadding        = 8f;   // retreat icons off right edge
        private const float kScrollTopPadding       = 12f;  // extra spacing inside scroll‐view
        private const float kBottomPadding           = 16f;  // extra space below the Close button
        private const float kWhisperButtonWidth     = 80f; // width for the "Whisper" button
        private const float kCloseXButtonSize        = kIconSize;  // match icon size (34px) so the "X" isn't cut off
        
        // ── New: Config entry for the friends‐list toggle hotkey ─────────
        private ConfigEntry<KeyboardShortcut> _toggleHotkey;
        
        #region Fields & Configuration Constants
        #endregion

        #region Lifecycle (Awake / Start / Update)
        void Awake()
        {
            context = this;
            harmony.PatchAll();
            FriendListLogger.LogInfo($"Plugin {ModGUID} is loaded!");
            
            // Set initial window position to center of screen with a wider window
            float windowWidth = 424;
            float windowHeight = 520;
            windowRect = new Rect(
                (Screen.width - windowWidth) / 2,  // Center horizontally
                (Screen.height - windowHeight) / 2, // Center vertically
                windowWidth,
                windowHeight
            );
            
            // Load friend list from file if it exists
            LoadFriendList();
            
            // Bind the toggle‑window hotkey in the “General” section of config
            _toggleHotkey = Config.Bind(
                "General",                              // Section
                "Toggle Friends List Hotkey",           // Key
                new KeyboardShortcut(KeyCode.K),        // Default
                "Key to open/close the Friends List window. Will be ignored while typing in chat."
            );
            
            // ── find the game's UIAnchors so we can re‐use its GroupAnchor transform
            uiAnchors = FindObjectOfType<UIAnchors>();
            if (uiAnchors != null)
                groupAnchor = uiAnchors.GroupAnchor;
        }
        
        void Start()
        {
            // Create a button to open the friend list
            CreateFriendListButton();
        }
        
        void Update()
        {
            // Toggle via user‑configurable hotkey, but only when not typing in chat
            if (!GameData.PlayerTyping && _toggleHotkey.Value.IsDown())
            {
                showFriendList = !showFriendList;
            }
        }
        #endregion

        #region GUI (OnGUI & Window Drawers)
        void OnGUI()
        {
            // initialize once
            if (!stylesInitialized)
            {
                InitializeGUIStyles();
                stylesInitialized = true;
            }

            // ── Snap to the GroupAnchor when NOT dragging ────────────────
            if (showFriendList && groupAnchor != null && !isDraggingWindow)
            {
                // world -> screen
                Vector3 sp = Camera.main.WorldToScreenPoint(groupAnchor.position);
                // invert y for IMGUI
                windowRect.x = sp.x - (windowRect.width * 0.5f);
                windowRect.y = Screen.height - sp.y - (windowRect.height * 0.5f);
            }

            // grab event up front
            Event e = Event.current;

            // ── DRAG HANDLE LOGIC ────────────────────────────────────────
            const float dsize = 12f;

            // Mouse Down -> start drag if over the diamond in top‑left
            if (e.type == EventType.MouseDown && e.button == 0 && showFriendList)
            {
                // convert to local coords (relative to window upper‑left)
                Vector2 local = e.mousePosition - windowRect.position;
                Rect handleRect = new Rect(4, 4, dsize, dsize);
                if (handleRect.Contains(local))
                {
                    isDraggingWindow = true;
                    dragOffset = e.mousePosition - windowRect.position;
                    GameData.DraggingUIElement = true;
                    e.Use();
                }
            }

            // Mouse Drag -> move window
            if (isDraggingWindow && e.type == EventType.MouseDrag)
            {
                windowRect.position = e.mousePosition - dragOffset;
                e.Use();
            }

            // Mouse Up -> end drag
            if (isDraggingWindow && e.type == EventType.MouseUp)
            {
                isDraggingWindow = false;
                GameData.DraggingUIElement = false;
                e.Use();

                // ── Write our new window center back into the GroupAnchor ──
                if (groupAnchor != null)
                {
                    // compute screen‐center of our window
                    Vector3 screenCenter = new Vector3(
                        windowRect.x + windowRect.width  * 0.5f,
                        Screen.height - (windowRect.y + windowRect.height * 0.5f),
                        Camera.main.WorldToScreenPoint(groupAnchor.position).z
                    );
                    // back to world‐space
                    Vector3 worldPos = Camera.main.ScreenToWorldPoint(screenCenter);
                    groupAnchor.position = worldPos;
                }
            }

            // ── ESC KEY -> only close our window ──
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape && showFriendList)
            {
                showFriendList = false;
                e.Use();
            }

            //
            // Only swallow the scroll‐wheel so the camera doesn't zoom.
            // Leave MouseDown/MouseDrag free for scrollbars and other controls.
            //
            if (showFriendList)
            {
                if (e.type == EventType.ScrollWheel)
                    e.Use();
            }
            
            if (showFriendList)
            {
                GUI.FocusWindow(windowId);
                
                // Draw the window (no built‑in title)
                windowRect = GUI.Window(windowId, windowRect, DrawFriendListWindow, String.Empty);
                
                // Keep the window on screen
                windowRect.x = Mathf.Clamp(windowRect.x, 0, Screen.width - windowRect.width);
                windowRect.y = Mathf.Clamp(windowRect.y, 0, Screen.height - windowRect.height);

                // ── Outer border so the window edges are always visible ───────
                {
                    float t = kWindowBorderThickness;
                    // top
                    SafeDraw(
                        new Rect(windowRect.x - t, windowRect.y - t, windowRect.width + 2*t, t),
                        borderGlow);
                    // bottom
                    SafeDraw(
                        new Rect(windowRect.x - t, windowRect.y + windowRect.height, windowRect.width + 2*t, t),
                        borderGlow);
                    // left
                    SafeDraw(
                        new Rect(windowRect.x - t, windowRect.y, t, windowRect.height),
                        borderGlow);
                    // right
                    SafeDraw(
                        new Rect(windowRect.x + windowRect.width, windowRect.y, t, windowRect.height),
                        borderGlow);
                }
            }
            
            // Draw confirmation dialog
            if (showConfirmationDialog)
            {
                GUI.FocusWindow(9999);
                Rect confirmRect = new Rect((Screen.width - 300) / 2, (Screen.height - 150) / 2, 300, 150);
                GUI.Window(9999, confirmRect, DrawConfirmationDialog, "Confirm Removal");
            }
            
            // Draw tooltip if needed
            if (showTooltip)
            {
                GUI.Box(tooltipRect, tooltipText, tooltipStyle);
            }
        }
        
        private void InitializeGUIStyles()
        {
            // ── Make window have transparent background
            Color topCol    = new Color(0x18/255f, 0x23/255f, 0x26/255f, 0.55f);
            Color bottomCol = new Color(0f,       0f,       0f,       0.55f);
            windowBG        = MakeGradientTexture(topCol, bottomCol);

            // Outline colour for window border
            Color borderCol = new Color(0x4e/255f, 0x7f/255f, 0x89/255f, 1.00f);
            borderGlow      = MakeButtonTexture(borderCol);

            // Row backgrounds
            entryBG         = MakeButtonTexture(new Color(0x0f/255f, 0x12/255f, 0x10/255f, 0.35f) * 1.1f);
            entryHoverBG    = MakeButtonTexture(new Color(0x0f/255f, 0x12/255f, 0x10/255f, 0.45f) * 1.3f);

            // Instead, pick the exact RGBA UI‑anchor diamond uses:
            Color diamondCol = new Color(0.00f, 0.60f, 0.85f, 0.90f);
            diamondTex       = MakeButtonTexture(diamondCol);

            // Hook window skin (so borders/close box, etc. can still use it)
            GUI.skin.window.normal.background   = windowBG;
            GUI.skin.window.onNormal.background = windowBG;
            GUI.skin.window.border              = new RectOffset(4,4,4,4);
            GUI.skin.window.normal.textColor    = Color.white;
            
            // Header style – white text
            headerStyle = new GUIStyle(GUI.skin.label);
            headerStyle.fontSize = 20;
            headerStyle.normal.textColor = Color.white;
            headerStyle.alignment = TextAnchor.MiddleCenter;
            headerStyle.fontStyle = FontStyle.Bold;
            
            // Friend name style – larger white text
            friendNameStyle = new GUIStyle(GUI.skin.label);
            friendNameStyle.fontSize = 16;
            friendNameStyle.normal.textColor = Color.white;
            friendNameStyle.fontStyle = FontStyle.Bold;
            friendNameStyle.margin = new RectOffset(5, 5, 5, 0);
            
            // Class info style – smaller cyan text
            classInfoStyle = new GUIStyle(GUI.skin.label);
            classInfoStyle.fontSize = 12;
            classInfoStyle.normal.textColor = new Color(0.0f, 0.8f, 0.8f);
            classInfoStyle.margin = new RectOffset(5, 5, 0, 5);
            
            // Tooltip style
            tooltipStyle = new GUIStyle(GUI.skin.box);
            tooltipStyle.normal.background = windowBG;
            tooltipStyle.normal.textColor = Color.white;
            tooltipStyle.fontSize = 14;
            tooltipStyle.alignment = TextAnchor.MiddleCenter;
            tooltipStyle.padding = new RectOffset(10, 10, 5, 5);
            tooltipStyle.border = new RectOffset(1, 1, 1, 1);
            
            // Button style
            buttonStyle = new GUIStyle(GUI.skin.button);
            buttonStyle.normal.background = MakeButtonTexture(new Color(0.10f, 0.15f, 0.20f, 0.90f));
            buttonStyle.hover.background  = MakeButtonTexture(new Color(0.15f, 0.20f, 0.25f, 0.90f));
            buttonStyle.active.background = MakeButtonTexture(new Color(0.20f, 0.25f, 0.30f, 0.90f));
            buttonStyle.normal.textColor  = Color.white;
            buttonStyle.hover.textColor   = new Color(0.0f, 0.9f, 1.0f); // cyan on hover
            buttonStyle.alignment         = TextAnchor.MiddleCenter;
            buttonStyle.fontSize          = 14;
            buttonStyle.fontStyle         = FontStyle.Bold;
            buttonStyle.border            = new RectOffset(2, 2, 2, 2);
            buttonStyle.padding           = new RectOffset(8, 8, 4, 4);
            
            // Icon-button style
            iconButtonStyle = new GUIStyle(buttonStyle);
            iconButtonStyle.fontSize = 16;
            
            // (old skin.box override removed – we'll style each entry manually)
        }
        
        // Helper method to create button textures
        private Texture2D MakeButtonTexture(Color color)
        {
            Texture2D texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }
        
        void DrawFriendListWindow(int id)
        {
            //────── single definitions to avoid shadowing errors ─────────────────
            const float windowBorderThickness = 2f;    // panel border width
            const float dsize                 = 12f;   // drag‑handle & close‑X size

            Event e = Event.current;

            // Handle scroll events specifically for our scroll view
            if (e.type == EventType.ScrollWheel)
            {
                float scrollDelta = e.delta.y * 20f;
                scrollPosition.y += scrollDelta;
                scrollPosition.y = Mathf.Max(0, scrollPosition.y);
                e.Use();
            }
            
            // ── fill panel background with our semi‑transparent gradient ─────────
            SafeDraw(new Rect(0, 0, windowRect.width, windowRect.height),
                     windowBG, ScaleMode.StretchToFill);

            // ── draw a solid border on top of the gradient ───────────────────────
            SafeDraw(new Rect(0, -windowBorderThickness, windowRect.width, windowBorderThickness),
                     borderGlow);
            SafeDraw(new Rect(0, windowRect.height, windowRect.width, windowBorderThickness),
                     borderGlow);
            SafeDraw(new Rect(-windowBorderThickness, 0, windowBorderThickness, windowRect.height),
                     borderGlow);
            SafeDraw(new Rect(windowRect.width, 0, windowBorderThickness, windowRect.height),
                     borderGlow);

            // ── draw small rotated diamond in top‑left as our drag handle ───────
            Rect handleRect = new Rect(4, 4, dsize, dsize);
            var oldMat = GUI.matrix;
            GUIUtility.RotateAroundPivot(45f,
                new Vector2(handleRect.x + dsize/2f, handleRect.y + dsize/2f));
            SafeDraw(handleRect, diamondTex);
            GUI.matrix = oldMat;

            // ── draw a full‑size "X" button in the top‑right to close the list ────
            Rect closeXRect = new Rect(
                windowRect.width - kCloseXButtonSize - kIconSpacing, // inset by icon‑spacing
                kIconSpacing,                                        // down by same spacing
                kCloseXButtonSize,                                   // width (34px)
                kCloseXButtonSize                                    // height (34px)
            );
            DrawBorder(closeXRect, kWindowBorderThickness);
            // ensure the "X" is drawn in white
            Color before = GUI.color;
            GUI.color = Color.white;
            if (GUI.Button(closeXRect, "X", iconButtonStyle))
                showFriendList = false;
            GUI.color = before;

            // ── pull down content under the diamond ─────────────────────────────
            GUILayout.BeginArea(new Rect(0, dsize + 12, windowRect.width, windowRect.height - (dsize + 12)));
            GUILayout.BeginVertical(GUILayout.ExpandHeight(true));
            // extra top padding so the first row's icon borders aren't cut off
            GUILayout.Space(8f);

            // Header
            GUILayout.Label("Your Friends", headerStyle);
            GUILayout.Space(10);
            
            // Friend-row style = "Select a class" look
            GUIStyle friendItemStyle = new GUIStyle(GUI.skin.button);
            friendItemStyle.normal.background = entryBG;
            friendItemStyle.hover.background  = entryHoverBG;
            friendItemStyle.active.background = entryHoverBG;
            friendItemStyle.border            = new RectOffset(4, 4, 4, 4);
            friendItemStyle.margin            = new RectOffset(6, 6, 4, 4);
            friendItemStyle.padding           = new RectOffset(10, 8, 6, 6);
            friendItemStyle.normal.textColor  = Color.white;
            
            // Add current target button
            if (GameData.PlayerControl != null && GameData.PlayerControl.CurrentTarget != null)
            {
                Character targetChar = GameData.PlayerControl.CurrentTarget;
                if (targetChar.GetComponent<SimPlayer>() != null)
                {
                    string targetName = targetChar.GetComponent<NPC>().NPCName;
                    if (!friendList.Contains(targetName))
                    {
                        GUILayout.BeginHorizontal();
                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button($"Add {targetName} to Friends", buttonStyle, GUILayout.Height(40), GUILayout.Width(350)))
                        {
                            friendList.Add(targetName);
                            SaveFriendList();
                            SafeLogToSocialLog($"Added {targetName} to your friend list!", "lightblue");
                        }
                        GUILayout.FlexibleSpace();
                        GUILayout.EndHorizontal();
                        GUILayout.Space(10);
                    }
                }
            }
            
            // Scrollable list with custom style - ensure the style's background isn't null
            GUIStyle scrollViewStyle = new GUIStyle(GUI.skin.scrollView);
            if (scrollViewStyle.normal.background == null)
                scrollViewStyle.normal.background = entryBG;
            GUIStyle vs = new GUIStyle(GUI.skin.verticalScrollbar);
            if (vs.normal.background == null) vs.normal.background = entryBG;
            scrollPosition = GUILayout.BeginScrollView(
                scrollPosition,
                scrollViewStyle,
                vs,
                GUILayout.Width(windowRect.width),
                GUILayout.Height(windowRect.height - (dsize + 80))
            );
            
            // push rows down so their top borders clear the scrollview edge
            GUILayout.Space(kScrollTopPadding);

            if (friendList.Count == 0)
            {
                GUILayout.BeginHorizontal(friendItemStyle, GUILayout.Height(50));
                GUILayout.Label("No friends added yet.\nTarget a SimPlayer and use /friend to add them.", friendNameStyle);
                GUILayout.EndHorizontal();
            }
            else
            {
                foreach (string friend in friendList)
                {
                    GUILayout.BeginHorizontal();
                        // small indent so names aren't flush to the very left
                        GUILayout.Space(kRowLeftPadding);

                        // Left side: name & class info
                        GUILayout.BeginVertical();
                            GUILayout.Label(friend, friendNameStyle);
                            GUILayout.Label(GetSimPlayerInfo(friend), classInfoStyle);
                        GUILayout.EndVertical();

                        // push icons to the far right
                        GUILayout.FlexibleSpace();
                        DrawIconButton("Whisper", () => WhisperToFriend(friend), kWhisperButtonWidth);
                        GUILayout.Space(kIconSpacing);
                        DrawIconButton("X", () => ShowRemoveDialog(friend));

                        // retreat a bit from the scrollbar on the right
                        GUILayout.Space(kRowRightPadding);
                    GUILayout.EndHorizontal();
                    GUILayout.Space(5);
                }
            }
            
            GUILayout.EndScrollView();
            
            GUILayout.Space(kBottomPadding);
            
            GUILayout.EndVertical();
            GUILayout.EndArea();
        }
        
        void DrawConfirmationDialog(int id)
        {
            // Draw the same dark background with cyan border
            Texture2D confirmBg = MakeButtonTexture(new Color(0.05f, 0.07f, 0.12f, 0.95f));
            SafeDraw(new Rect(0, 0, 300, 150), confirmBg);
            
            // Draw a cyan border
            Color borderColor = new Color(0.0f, 0.6f, 0.8f, 0.7f);
            GUI.color = borderColor;
            SafeDraw(new Rect(0, 0, 300, 150), null);
            GUI.color = Color.white;
            
            GUILayout.BeginVertical();
            GUILayout.Space(15);
            GUILayout.Label($"Remove {friendToRemove} from your friends list?", headerStyle);
            GUILayout.Space(25);
            
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            
            if (GUILayout.Button("Yes", buttonStyle, GUILayout.Width(100), GUILayout.Height(40)))
            {
                friendList.Remove(friendToRemove);
                SaveFriendList();
                showConfirmationDialog = false;
                SafeLogToSocialLog($"{friendToRemove} has been removed from your friend list.", "lightblue");
            }
            
            GUILayout.Space(20);
            
            if (GUILayout.Button("No", buttonStyle, GUILayout.Width(100), GUILayout.Height(40)))
            {
                showConfirmationDialog = false;
            }
            
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            
            GUILayout.EndVertical();
        }
        
        // Create a button in the HUD to open the friend list
        private void CreateFriendListButton()
        {
            try
            {
                // We'll need to find a good place in the UI to add our button
                // This is a placeholder implementation
                FriendListLogger.LogInfo("Attempting to create friend list button");
                
                // For now, we'll just log that we're planning to add a button
                // The actual implementation will depend on the game's UI structure
            }
            catch (Exception ex)
            {
                FriendListLogger.LogError($"Error creating friend list button: {ex.Message}");
            }
        }
        
        // Method to whisper to a friend
        private void WhisperToFriend(string friendName)
        {
            // Set up the whisper command in the chat input
            if (GameData.TextInput != null)
            {
                GameData.TextInput.typed.text = $"/whisper {friendName} ";
                GameData.TextInput.InputBox.SetActive(true);
                GameData.PlayerTyping = true;
            }
        }

        // Load friend list from file
        private void LoadFriendList()
        {
            string filePath = Path.Combine(Paths.ConfigPath, "FriendList.txt");
            if (File.Exists(filePath))
            {
                try
                {
                    friendList = File.ReadAllLines(filePath).ToList();
                    FriendListLogger.LogInfo($"Loaded {friendList.Count} friends from file");
                }
                catch (Exception ex)
                {
                    FriendListLogger.LogError($"Error loading friend list: {ex.Message}");
                }
            }
        }
        
        // Save friend list to file
        private void SaveFriendList()
        {
            string filePath = Path.Combine(Paths.ConfigPath, "FriendList.txt");
            try
            {
                File.WriteAllLines(filePath, friendList);
                FriendListLogger.LogInfo("Friend list saved to file");
            }
            catch (Exception ex)
            {
                FriendListLogger.LogError($"Error saving friend list: {ex.Message}");
            }
        }
        
        // Helper method to safely log to the social log
        private static void SafeLogToSocialLog(string message, string color = "white")
        {
            try
            {
                // Call the LogAdd method directly
                UpdateSocialLog.LogAdd(message, color);
            }
            catch (Exception ex)
            {
                FriendListLogger.LogError($"Error logging to social log: {ex.Message}");
            }
        }
        
        // Patch to intercept the /friend command and add to our list
        [HarmonyPatch(typeof(TypeText), "CheckCommands")]
        public class FriendCommandPatch
        {
            static void Postfix(TypeText __instance)
            {
                try
                {
                    if (GameData.TextInput != null && 
                        GameData.TextInput.typed != null && 
                        GameData.TextInput.typed.text != null)
                    {
                        string inputText = GameData.TextInput.typed.text;
                        FriendListLogger.LogInfo($"Command detected: {inputText}");
                        
                        if (inputText.StartsWith("/friend "))
                        {
                            // Extract the friend name from the command
                            string[] parts = inputText.Split(new[] { ' ' }, 2);
                            if (parts.Length > 1 && !string.IsNullOrEmpty(parts[1]))
                            {
                                string friendName = parts[1].Trim();
                                FriendListLogger.LogInfo($"Friend command detected for: {friendName}");
                                
                                // Check if there's a targeted SimPlayer that matches this name
                                if (GameData.PlayerControl != null && GameData.PlayerControl.CurrentTarget != null)
                                {
                                    FriendListLogger.LogInfo($"Current target: {GameData.PlayerControl.CurrentTarget.name}");
                                }
                                
                                // Add to our friend list if not already there
                                if (!context.friendList.Contains(friendName))
                                {
                                    context.friendList.Add(friendName);
                                    context.SaveFriendList();
                                    FriendListLogger.LogInfo($"Added {friendName} to friend list");
                                    
                                    // Notify the player
                                    SafeLogToSocialLog($"Added {friendName} to your friend list!", "lightblue");
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    FriendListLogger.LogError($"Error in FriendCommandPatch: {ex.Message}\n{ex.StackTrace}");
                }
            }
        }
        
        [HarmonyPatch(typeof(TypeText), "CheckCommands")]
        public class DebugFriendCommandPatch
        {
            static bool Prefix()
            {
                try
                {
                    if (GameData.TextInput != null && 
                        GameData.TextInput.typed != null && 
                        GameData.TextInput.typed.text != null)
                    {
                        string inputText = GameData.TextInput.typed.text;
                        
                        if (inputText.StartsWith("/addfriend "))
                        {
                            // Extract the friend name from the command
                            string[] parts = inputText.Split(new[] { ' ' }, 2);
                            if (parts.Length > 1 && !string.IsNullOrEmpty(parts[1]))
                            {
                                string friendName = parts[1].Trim();
                                
                                // Add to our friend list if not already there
                                if (!context.friendList.Contains(friendName))
                                {
                                    context.friendList.Add(friendName);
                                    context.SaveFriendList();
                                    FriendListLogger.LogInfo($"Manually added {friendName} to friend list");
                                    
                                    // Notify the player
                                    SafeLogToSocialLog($"Added {friendName} to your friend list!", "yellow");
                                }
                            }
                            
                            // Clear the input and return false to prevent further processing
                            GameData.TextInput.typed.text = "";
                            GameData.TextInput.CDFrames = 10f;
                            GameData.TextInput.InputBox.SetActive(false);
                            GameData.PlayerTyping = false;
                            return false;
                        }
                    }
                }
                catch (Exception ex)
                {
                    FriendListLogger.LogError($"Error in DebugFriendCommandPatch: {ex.Message}");
                }
                
                return true;
            }
        }
        
        // Method to get SimPlayer class and level info
        private string GetSimPlayerInfo(string simPlayerName)
        {
            try
            {
                // Try to find the SimPlayer in active instances
                if (GameData.SimMngr != null && GameData.SimMngr.ActiveSimInstances != null)
                {
                    foreach (SimPlayer simPlayer in GameData.SimMngr.ActiveSimInstances)
                    {
                        if (simPlayer != null && simPlayer.GetComponent<NPC>() != null && 
                            simPlayer.GetComponent<NPC>().NPCName == simPlayerName)
                        {
                            Stats stats = simPlayer.GetComponent<Stats>();
                            if (stats != null)
                            {
                                string className = stats.CharacterClass != null ? stats.CharacterClass.ClassName : "Unknown";
                                int level = stats.Level;
                                return $"Level {level} {className}";
                            }
                        }
                    }
                }
                
                // Try to find in SimPlayerTracking
                if (GameData.SimMngr != null && GameData.SimMngr.Sims != null)
                {
                    foreach (SimPlayerTracking tracking in GameData.SimMngr.Sims)
                    {
                        if (tracking.SimName == simPlayerName)
                        {
                            return $"Level {tracking.Level} (Offline)";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                FriendListLogger.LogError($"Error getting SimPlayer info: {ex.Message}");
            }
            
            return "Status unknown";
        }
        
        // Helper method to handle tooltips
        private void HandleTooltip(Rect buttonRect, string tooltip)
        {
            if (buttonRect.Contains(Event.current.mousePosition))
            {
                showTooltip = true;
                tooltipText = tooltip;
                tooltipRect = new Rect(
                    Event.current.mousePosition.x + 15, 
                    Event.current.mousePosition.y - 35, 
                    120, 30);
                    
                if (tooltipRect.xMax > Screen.width)
                    tooltipRect.x = Screen.width - tooltipRect.width;
            }
            else if (tooltipText == tooltip)
            {
                showTooltip = false;
            }
        }

        // ─── H A R M O N Y   P A T C H E S ────────────────────────────

        // Prefix for Input.GetAxis: if our window is open and the axis is "Mouse ScrollWheel",
        // override result to 0 and skip the original method.
        [HarmonyPatch(typeof(Input), nameof(Input.GetAxis), new[] { typeof(string) })]
        private static class Patch_Input_GetAxis
        {
            static bool Prefix(string axisName, ref float __result)
            {
                if (FriendListMod.context != null
                    && FriendListMod.context.showFriendList
                    && axisName == "Mouse ScrollWheel")
                {
                    __result = 0f;
                    return false;   // skip the original
                }
                return true;        // call the original
            }
        }

        // Also patch Input.GetAxisRaw just in case any code uses that directly.
        [HarmonyPatch(typeof(Input), nameof(Input.GetAxisRaw), new[] { typeof(string) })]
        private static class Patch_Input_GetAxisRaw
        {
            static bool Prefix(string axisName, ref float __result)
            {
                if (FriendListMod.context != null
                    && FriendListMod.context.showFriendList
                    && axisName == "Mouse ScrollWheel")
                {
                    __result = 0f;
                    return false;
                }
                return true;
            }
        }

        // ─── HARMONY PATCH TO SWALLOW ESC FOR OTHER CODE ────────────────────────
        [HarmonyPatch(typeof(Input), nameof(Input.GetKeyDown), new[] { typeof(KeyCode) })]
        private static class Patch_Input_GetKeyDown
        {
            static bool Prefix(KeyCode key, ref bool __result)
            {
                if (key == KeyCode.Escape && context != null && context.showFriendList)
                {
                    __result = false;
                    return false;
                }
                return true;
            }
        }

        // ─── NEW PATCHES TO SILENCE GetKey & GetKeyUp FOR ESCAPE ───────────

        [HarmonyPatch(typeof(Input), nameof(Input.GetKey), new[] { typeof(KeyCode) })]
        private static class Patch_Input_GetKey
        {
            static bool Prefix(KeyCode key, ref bool __result)
            {
                if (key == KeyCode.Escape && context != null && context.showFriendList)
                {
                    __result = false;
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(Input), nameof(Input.GetKeyUp), new[] { typeof(KeyCode) })]
        private static class Patch_Input_GetKeyUp
        {
            static bool Prefix(KeyCode key, ref bool __result)
            {
                if (key == KeyCode.Escape && context != null && context.showFriendList)
                {
                    __result = false;
                    return false;
                }
                return true;
            }
        }

        // ─── HARMONY PATCH TO SWALLOW "Cancel" (Escape) VIA GetButtonDown ────────────
        [HarmonyPatch(typeof(Input), nameof(Input.GetButtonDown), new[] { typeof(string) })]
        private static class Patch_Input_GetButtonDown_Cancel
        {
            static bool Prefix(string buttonName, ref bool __result)
            {
                // when our list is up, swallow all Cancel presses
                if (buttonName == "Cancel" && context != null && context.showFriendList)
                {
                    __result = false;
                    return false;
                }
                return true;
            }
        }

        // ─── ALSO SWALLOW GetButton & GetButtonUp FOR "Cancel" ─────────────────────────
        [HarmonyPatch(typeof(Input), nameof(Input.GetButton), new[] { typeof(string) })]
        private static class Patch_Input_GetButton_Cancel
        {
            static bool Prefix(string buttonName, ref bool __result)
            {
                if (buttonName == "Cancel" && context != null && context.showFriendList)
                {
                    __result = false;
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(Input), nameof(Input.GetButtonUp), new[] { typeof(string) })]
        private static class Patch_Input_GetButtonUp_Cancel
        {
            static bool Prefix(string buttonName, ref bool __result)
            {
                if (buttonName == "Cancel" && context != null && context.showFriendList)
                {
                    __result = false;
                    return false;
                }
                return true;
            }
        }

        // ────── UTILITY DRAW HELPERS ───────────────────────────────
        /// <summary>
        /// Draws a solid border of <paramref name="thickness"/>px around <paramref name="r"/>.
        /// </summary>
        private void DrawBorder(Rect r, float thickness)
        {
            SafeDraw(new Rect(r.xMin,           r.yMin - thickness, r.width,       thickness), borderGlow);
            SafeDraw(new Rect(r.xMin,           r.yMax,             r.width,       thickness), borderGlow);
            SafeDraw(new Rect(r.xMin - thickness, r.yMin,           thickness,     r.height),   borderGlow);
            SafeDraw(new Rect(r.xMax,           r.yMin,             thickness,     r.height),   borderGlow);
        }

        /// <summary>
        /// Convenience for GUILayout + outlined button.
        /// You can now pass a custom width; height stays at kIconSize.
        /// </summary>
        private Rect DrawIconButton(string label, Action onClick, float width = kIconSize)
        {
            Rect rect = GUILayoutUtility.GetRect(
                width, kIconSize,
                GUILayout.Width(width),
                GUILayout.Height(kIconSize));

            DrawBorder(rect, kIconBorderThickness);

            if (GUI.Button(rect, label, iconButtonStyle))
                onClick();

            return rect;
        }

        private void ShowRemoveDialog(string friendName)
        {
            showConfirmationDialog = true;
            friendToRemove         = friendName;
        }
        #endregion

        #region Utils (Texture & Drawing Helpers)
        private Texture2D MakeGradientTexture(Color topColor, Color bottomColor)
        {
            var tex = new Texture2D(1, 2, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.SetPixel(0, 1, topColor);
            tex.SetPixel(0, 0, bottomColor);
            tex.Apply();
            return tex;
        }
        
        // ────── NEW: helpers to avoid passing null into GUI.DrawTexture ──────
        private void SafeDraw(Rect rect, Texture2D tex, ScaleMode mode = ScaleMode.StretchToFill)
        {
            if (tex != null)
                GUI.DrawTexture(rect, tex, mode);
        }
        private void SafeDraw(Rect rect, Texture2D tex)
        {
            if (tex != null)
                GUI.DrawTexture(rect, tex);
        }
        #endregion
    }
}
