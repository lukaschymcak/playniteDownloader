# Design: Replace "Download to" Field with Steam Library Selector

**Date:** 2026-04-09

## Context

The DownloadView has a "Download to:" text field pre-filled from `_settings.DownloadPath`. After adding `SteamLibraryPickerDialog`, the text field became redundant — when Steam libraries are detected, the picker overrides it at download time anyway. The raw path text box is confusing and unnecessary. This change removes it and replaces it with a purposeful "Select Steam Library" button that makes the selection intent explicit.

## Goal

Replace the freeform "Download to:" text input with a Steam library selector button that shows the current selection. If no library is pre-selected at download time, show the picker then. If the user cancels or no Steam libraries exist, cancel the download with an error message.

## Files to Modify

- `BlankPlugin/source/UI/DownloadView.cs`

## Changes

### 1. New field

Add `private string _selectedLibraryPath = null;` alongside other UI fields at the top of the class.

### 2. Remove old path field init

Remove the line (around line 115):
```csharp
_downloadPathBox.Text = _settings.DownloadPath;
```
Remove the `_downloadPathBox` field declaration.

### 3. Replace UI row in `BuildOptionsPanel()` (lines 423–436)

Remove the `pathRow` DockPanel (label + `_downloadPathBox`).

Replace with:
```csharp
var libraryRow = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
var selectLibBtn = new Button
{
    Content = "Select Steam Library",
    Padding = new Thickness(10, 3, 10, 3),
    HorizontalAlignment = HorizontalAlignment.Left
};
DockPanel.SetDock(selectLibBtn, Dock.Left);
_selectedLibLabel = new TextBlock
{
    Text = "No library selected",
    VerticalAlignment = VerticalAlignment.Center,
    Margin = new Thickness(10, 0, 0, 0),
    Foreground = Brushes.Gray
};
selectLibBtn.Click += (s, e) =>
{
    var libs = SteamLibraryHelper.GetSteamLibraries();
    if (libs.Count == 0)
    {
        AppendLog("No Steam libraries detected.");
        return;
    }
    var ownerWindow = Window.GetWindow(this);
    var picked = SteamLibraryPickerDialog.ShowPicker(ownerWindow, libs, _api);
    if (picked != null)
    {
        _selectedLibraryPath = picked;
        _selectedLibLabel.Text = picked;
        _selectedLibLabel.Foreground = Brushes.White;
    }
};
libraryRow.Children.Add(selectLibBtn);
libraryRow.Children.Add(_selectedLibLabel);
panel.Children.Add(libraryRow);
```

Add `private TextBlock _selectedLibLabel;` to class fields.

### 4. Update `OnDownloadClicked()` (lines 869–893)

Replace the current library-detection block with:
```csharp
string destPath;
if (_selectedLibraryPath != null)
{
    destPath = _selectedLibraryPath;
}
else
{
    var libraries = SteamLibraryHelper.GetSteamLibraries();
    if (libraries.Count == 0)
    {
        AppendLog("ERROR: No Steam libraries detected and no library selected.");
        return;
    }
    var ownerWindow = Window.GetWindow(this);
    var picked = SteamLibraryPickerDialog.ShowPicker(ownerWindow, libraries, _api);
    if (picked == null)
    {
        AppendLog("Download cancelled — no library selected.");
        return;
    }
    destPath = picked;
    _selectedLibraryPath = picked;
    _selectedLibLabel.Text = picked;
    _selectedLibLabel.Foreground = Brushes.White;
}
AppendLog("Installing to Steam library: " + destPath);
```

## Verification

1. Build in Visual Studio (`Ctrl+Shift+B`)
2. Install `.pext` into Playnite
3. Open DownloadView — confirm "Select Steam Library" button appears, no text box
4. Click button before downloading → picker appears → selecting a library shows path next to button
5. Click Download without pre-selecting → picker appears at that point
6. Cancel picker → download cancels with log message
7. On a machine with no Steam installed → error message logged, download does not proceed
