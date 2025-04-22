# FriendListMod

**Version:** 1.0.1
**Author:** Cyelis

A lightweight in‑game friends list for Erenshor, built on BepInEx & Harmony.  
Easily add, whisper, and remove friends without opening external menus.  

---

## Features

- Toggleable friends window (default hotkey: **K**)  
- Persistent friend list saved to `BepInEx/Config/FriendList.txt`  
- Whisper directly to any friend (`/whisper <name>`)  
- Remove friends with confirmation dialog  
- Draggable window via the top‑left diamond handle  
- Close the window with **Esc** or the top‑right “X” button (without invoking the game’s pause menu)  
- Scrollable list with dynamic opacity, custom styling, and tooltips  

---

## Installation

1. Install **[BepInEx 5.x](https://github.com/BepInEx/BepInEx/releases)** into your Erenshor game folder.  
2. Copy **Erenshor-FriendList.dll** (the compiled plugin) into your `BepInEx/plugins` directory.  
3. Ensure **ErenshorQOL** (Brumdail’s QoL mod) is also installed if required.  
4. Launch Erenshor – the console should log “Plugin `YourName.FriendListMod` is loaded!”  

---

## Usage

### Toggling the Window
- Press **K** to open or close the friends list UI.

### Adding Friends
Use the game’s chat input to run:

/addfriend <SimPlayerName>

On Enter, the plugin:
1. Appends `<PlayerName>` to your list (if not already present).  
2. Saves the updated list to `BepInEx/Config/FriendList.txt`.  
3. Displays a confirmation message in the social log.

### Interacting with the List
- **Whisper** button: Opens chat with `/whisper <name> ` pre‑filled.  
- **X** button: Prompts to remove the friend from your list.  
- **Diamond handle (top‑left)**: Click and drag to reposition the window.  
- **Close “X” (top‑right)** or **Esc**: Closes the friends window only.

---

## Configuration

The plugin uses a plain text file for storage—no JSON or XML needed.

- **Location:** `BepInEx/Config/FriendList.txt`  
- **Format:** One player name per line.  
- **Manual edits:** You can open and edit this file between game sessions to bulk‑add or remove names.

---

## License

This plugin is released under the **MIT License**. See [LICENSE](LICENSE) for full details.  
