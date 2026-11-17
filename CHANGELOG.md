# Changelog

## 0.4.0

- Portable .opl packages: embedded audio, ordered clip list, settings, SHA-256 validation and exact duplicate detection.
- Asynchronous import/export with rollback, bounded package sizes and strict archive path validation.
- Multiple selection in both views, select visible clips, bulk library removal and undo for the last removal.
- Audio source files are preserved when removing library entries.
- Package round-trip/malformed-input tests and selection/undo UI checks; audio engine unchanged.

## 0.3.0

- Dark window chrome, scrollbars, sliders and level meters.
- Tile and list library views with a persisted preference.
- Correct alignment of tile numbers and full-width list rows.
- UI smoke tests for mode switching, filtering, settings persistence and small-window rendering.
- Idempotent audio reader disposal; atomic preparation of main/monitor playback readers.
- Installer payload validation, file rollback on copy failure and inclusion of license notices.
- Russian and English documentation; GPL-3.0-only project license.

## 0.2.0

- OpenPad name, installer and Start Menu shortcut.
- Ogg Vorbis and Opus support.
- Microphone mixing, independent voice gain and level meter.
- Gain up to 500%, Bass Boost and optional distortion.
