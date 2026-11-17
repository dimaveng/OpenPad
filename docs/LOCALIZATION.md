# Localization

Supported UI language codes: `uk`, `en`, `ru`. Ukrainian uses ISO code `uk`; `ua` is accepted as an alias. Native language names in the picker remain recognizable in every locale.

`Translations.json` is embedded as `OpenPad.Translations.json`. Each stable resource key has a complete `uk` / `en` / `ru` set. XAML uses `DynamicResource`, while C# uses `L10n.T` and `L10n.F`. Russian source phrases serve as lookup identifiers in C#; changing them requires updating the matching translation entry. Preserve formatting placeholders and file-dialog filter patterns when editing translations.

If `Preferences.Language` is absent or unsupported, the Windows UI culture is detected: Ukrainian → uk, Russian → ru, otherwise English. The resolved value is saved during normal settings persistence. A manual choice overrides system culture on subsequent starts. Changing language updates labels, tooltips, generated clip controls and the monitor-off label, without reconfiguring audio devices or modifying user clip names.

Known application errors from the unchanged audio engine are translated at the UI boundary. Third-party, OS and driver error details may retain their original language. Standard OS dialog chrome follows Windows. Installer controls follow the Windows UI language independently of OpenPad settings; technical installation logs remain English.

Tests: `--language-test` checks dictionary completeness, placeholders and language detection. `--language-ui-test <output folder>` checks first-run selection, legacy settings, live switching, persisted choice, library/gain preservation and renders all three languages at the minimum window size.
