# Stage 5A — native WPF design preview

Prepared on 18 November 2026 on branch `design/stage-5-preview`. Waiting for user approval before implementation.

Direction: charcoal/navy surfaces, one muted cyan accent, quiet window controls, flat clip rows and clear hierarchy. Primary action is Add Sounds. Selected sound is highlighted; unselected rows do not carry separate large background panels.

Layout: collection/package navigation on the left; library, search, tile/list switch and selection actions in the center; selected sound effects and master clip gain on the right; persistent audio routing and Stop All along the bottom. Voice gain remains separate. Existing functionality and all three languages must remain available in the implementation. Narrow-window layouts, selection states and the tile view require implementation QA after approval.

The preview is rendered by a separate WPF application with synthetic clip names. It does not play audio or alter the installed application. The PNG and preview source archive are in the parent outputs directory as `OpenPad-redesign-preview.png` and `OpenPad-redesign-preview-source.zip`.

Working OpenPad 0.5 is backed up in `../backups/stage-5-before-design/OpenPad-0.5-with-library.zip`; source baseline tag is `baseline/before-stage-5`.

The supplied desktop-wpf skill informs implementation structure: ResourceDictionaries, templates, bindings and commands. A broad audio or application architecture rewrite is outside this redesign stage. `AudioEngine.cs` and `AudioDecoder.cs` must remain unchanged unless a separate need is identified and agreed.

**Stop point: show preview and wait for approval. No production UI implementation or release yet.**
