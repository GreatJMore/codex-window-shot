---
name: window-shot
description: Capture a specific desktop application window without including overlapping windows. Use for screenshots of desktop apps when full-screen or region capture would pick up other windows. Do not use for web-page content or documents that can be captured or rendered directly.
metadata:
  short-description: Capture a specific desktop window
---

# Window Shot

Use the bundled Windows tool to capture a window's own surface. It uses the window print API, so the image contains only the selected window even when other windows cover it. Do not replace a failed window capture with a full-screen or region screenshot; that reintroduces the overlapping-window problem.

## Tool

The executable is at:

`bin/WindowShot.exe`

Resolve it relative to this skill directory. It requires the .NET 8 Desktop Runtime, which is already installed on this machine.

Window enumeration and printing require access to the interactive desktop. In the Codex sandbox, a normal shell cannot see the desktop and may return an empty window list. Run the helper or `--list` with escalated/desktop access and ask for the required approval; do not conclude that no windows are open from a sandboxed run.

Prefer the helper script for routine captures:

```powershell
& "<skill-dir>\scripts\capture-window.ps1" -Match "Notepad" -Out "<workspace>\shot.png"
```

The helper prints a JSON result with `ok`, the chosen `handle`, `title`, `process`, `width`, `height`, `minimized`, and `output`. It exits nonzero on failure.

## Workflow

1. Identify the exact window the user wants. If the target is ambiguous, list windows first:

   ```powershell
   & "<skill-dir>\bin\WindowShot.exe" --list "<workspace>\windows.json"
   ```

   The process is a GUI-subsystem app, so use `Start-Process -Wait -WindowStyle Hidden` or poll for the JSON file before reading it.

2. Choose a window with `minimized: false`. If the requested window is minimized, ask the user to restore it. Do not capture a minimized window or an unrelated window instead.

3. Capture by handle when you have a precise target:

   ```powershell
   & "<skill-dir>\bin\WindowShot.exe" --capture "0x12345" "<workspace>\shot.png"
   ```

   Or use the helper's `-Handle` parameter. `--capture-title "<text>" "<path>"` is available for simple, unambiguous title matches.

4. Verify the PNG exists and is non-empty. Open it with the image viewer and confirm it is the requested window, not a full-screen image and not all black.

5. Keep the screenshot in the workspace or the path the user requested. Do not save test images into the user's Pictures folder.

## Boundaries

- Capture only the window the user asked for. Never use the user's other open windows as test subjects or capture them for comparison.
- Test the tool with its own window or a synthetic window, never with the user's personal applications.
- If the window is minimized, protected, or renders only black, explain the limitation and ask before trying another method.
- For web-page content, prefer a browser or Playwright page screenshot when available. Use Window Shot when the user explicitly wants the browser/application window itself.
- The GUI is for interactive selection: run `bin/WindowShot.exe` with no arguments, let the user pick the window, then use the capture button. The GUI copies the capture to the clipboard and saves it under the user's Pictures folder.
- While the GUI is running, `Ctrl+Alt+Shift+S` captures the foreground window when pressed from another app; when the GUI is focused, it captures the selected window.
