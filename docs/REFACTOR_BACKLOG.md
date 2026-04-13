# Refactor backlog

## N1 — Split `DownloadView.cs`

`DownloadView.cs` is large and mixes several workflows (resolve manifest, depot selection, download, IGDB/metadata, post-install). **Deferred:** no structural split in the current ship window; track here for a dedicated refactor branch (extract helpers or partial classes by workflow, keep Playnite threading rules).
