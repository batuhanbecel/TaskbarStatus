# Fullscreen integration checks

Run the rebuilt executable with `--dev` (separate preferences, updates disabled).
These checks require an interactive Windows desktop and a game; the layout tests do not
verify compositor behavior or actual game rendering.

1. Enable **Show over fullscreen apps**, enter a borderless fullscreen game on the
   taskbar monitor, and confirm the text stays on-screen without stealing game focus.
2. Alt-Tab to the desktop and back several times. The widget should return to the
   taskbar on the desktop and detach again in the game.
3. Repeat with taskbar auto-hide enabled, and with a lower game resolution.
4. Disable **Show over fullscreen apps**. It should disappear in fullscreen and
   return after Alt-Tab to the desktop.
5. Test both position anchors, a large saved drag offset, and a monitor with a
   negative desktop origin.
6. In a controlled update test, queue an installer while a fullscreen application
   is foreground. With the overlay enabled, installation must still wait until
   fullscreen ends. Dev mode does not download updates automatically.

Exclusive fullscreen can bypass desktop composition. Use borderless fullscreen
if the game does not display ordinary desktop overlays.

## Snipping Tool regression

With **Show over fullscreen apps** both off and on, start a new Snipping Tool
capture from the desktop, wait longer than the update interval, and cancel with
Escape. The widget must keep its pre-capture visibility and taskbar parent, then
resume updates after capture ends. Repeat while a borderless game is foreground:
an already hidden widget must stay hidden, and an overlay must not reattach or
raise itself over the selection UI. The normal Snipping Tool editor must not pause
readings. Verify pending installers do not run during selection.
