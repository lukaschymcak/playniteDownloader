# Design: Update Button in Library Card

**Date:** 2026-04-08  
**Status:** Approved

## Problem

Games with an available update show an orange "Update available" badge and left-edge accent stripe in the library panel, but there is no direct action button. The user must right-click the game in Playnite's main library and use the context menu to trigger an update.

## Goal

Add an "Update" button directly on the library card so the user can start an update in one click from the sidebar panel.

## Design

### Trigger condition

The button appears **only** when `_updateChecker.GetStatus(game.AppId) == "update_available"`. Games that are up-to-date, checking, or cannot-determine show no Update button (no change to their card).

### Button placement and appearance

Button order: `[Update] [Open] [Uninstall]`

- Width: 64px, Height: 26px, FontSize: 11 (matching existing buttons)
- Content: `"Update"`
- Background: `#CC6600` (muted orange, consistent with the existing `update_available` accent color `#FFA500` but slightly darker to distinguish it as an action)
- Foreground: White
- Left margin: 0, right margin: 6px (same gap pattern as `openBtn`)

### Click behavior

1. Opens `UpdateGameDialog` in a `PlayniteApi.Dialogs.CreateWindow` window (480×300, CenterOwner) — same setup as the context menu in `BlankPlugin.cs:196–209`.
2. After `ShowDialog()` returns (regardless of outcome), calls `RefreshLibraryList()` on the UI thread.
   - If the update succeeded: `UpdateChecker.MarkUpToDate` was already called inside `UpdateWindow`, so the status cache is `up_to_date`. The refreshed card shows the green stripe and no Update button.
   - If the user cancelled: status is still `update_available`. The card re-renders unchanged.

### Change scope

**Single method:** `LibraryView.CreateLibraryGameEntry` — insert the conditional button before `btnStack.Children.Add(openBtn)`.

No new classes, no new files, no schema changes.

## Files to modify

- `BlankPlugin/source/UI/LibraryView.cs` — `CreateLibraryGameEntry` method (~line 352)

## Verification

1. Build succeeds (no errors).
2. In Playnite: open the sidebar library panel.
3. For a game with `update_available` status: Update button is visible to the left of Open.
4. For a game with any other status: no Update button.
5. Clicking Update opens the `UpdateGameDialog` window.
6. After a successful update: library refreshes, card shows green stripe, no Update button.
7. After cancelling the dialog: card is unchanged.
