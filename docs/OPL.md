# OpenPad Library (.opl), version 1

`.opl` is a ZIP container with UTF-8 `manifest.json` and one audio entry per clip. It stores the original audio bytes, clip order, names, gain, bass boost, effect switch and favorite state. Device settings and local file paths are never exported.

```json
{
  "Format": "OpenPad Library",
  "Version": 1,
  "Clips": [
    {
      "Audio": "audio/000000.wav",
      "Sha256": "64 hexadecimal characters",
      "Name": "Example",
      "Gain": 100,
      "Bass": 0,
      "Effects": true,
      "Favorite": false
    }
  ]
}
```

Entry names must be exactly `audio/{zero-based index with six digits}.{extension}`. Names in the archive are validated, but extraction uses generated local filenames. Unsupported manifest versions, missing/extra entries, duplicate entry names, invalid values and checksum mismatches reject the complete import. Gain must be finite and 0–500; bass finite and 0–24. Names must contain 1–512 characters. Up to 5,000 clips, 256 MiB per audio file, 2 GiB combined audio and an 8 MiB manifest are allowed.

Imported audio is stored under `Data/Imports/{unique batch id}/`. Original source files are not required after export. On import, an exact duplicate means identical audio SHA-256 plus name, gain, bass, effect and favorite settings. Exact duplicates are skipped both against the library and within a package. Different names/settings are retained. The existing library order is preserved and new clips are appended in package order.

An import stages files and validates them before adding clips. Failed imports and failed library saves remove their new files. Export is written to a temporary sibling and replaces the destination only on success. Removing imported clips from the library intentionally keeps their audio, so undo remains possible. Deleting unused managed files is not implemented yet.

The current format supports ordinary mono/stereo audio accepted by OpenPad. It does not include folders, custom hotkeys or collection grouping, which are not currently part of the library model.

## Русский

Один `.opl` содержит звуки, их порядок и настройки. Используйте «Импорт .opl» или перетаскивание файла. «Экспорт всех .opl» экспортирует всю библиотеку; при выделении — только выделенные звуки в порядке библиотеки. Точные повторы пропускаются. Настройки аудиоустройств и локальные пути в пакет не попадают.

Для массового удаления нажмите «Выбрать звуки» и отметьте клипы. «Все видимые» добавляет все звуки, подходящие под текущий поиск/избранное, к выделению. Выделение сохраняется при смене вида и фильтра. «Убрать» убирает их из библиотеки, сохраняя аудиофайлы. «Отменить» возвращает последний удалённый набор в течение текущего запуска приложения. Ctrl+клик тоже включает выбор. В режиме выбора нажатие на клип не воспроизводит его.
