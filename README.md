[marketplace]: <https://marketplace.visualstudio.com/items?itemName=MadsKristensen.ScratchFiles>
[vsixgallery]: <http://vsixgallery.com/extension/ScratchFiles.5e5465aa-805e-4395-b20d-a439f7c92ca1/>
[repo]: <https://github.com/madskristensen/ScratchFiles>

# Scratch Files for Visual Studio

[![Build](https://github.com/madskristensen/ScratchFiles/actions/workflows/build.yaml/badge.svg)](https://github.com/madskristensen/ScratchFiles/actions/workflows/build.yaml)
[![GitHub Sponsors](https://img.shields.io/github/sponsors/madskristensen)](https://github.com/sponsors/madskristensen)

Download this extension from the [Visual Studio Marketplace][marketplace] or get the [CI build][vsixgallery].

Instantly create temporary scratch files for quick notes, code snippets, and throwaway experiments — without cluttering your project. Think of it as a notepad that lives inside Visual Studio with full language support.

## Features

### Instant file creation with Ctrl+N

Press **Ctrl+N** to instantly create a new scratch file — no template dialog, no prompts. Just a blank editor ready to go. Files are auto-numbered (`scratch1`, `scratch2`, ...) and stored outside your project.

![Scratch Files overview](art/infobar.png)

### Automatic language detection

Start typing and save — Scratch Files detects the language from your content and offers to apply syntax highlighting automatically via the InfoBar.

![Language detection InfoBar](art/language-detection.png)

### Language picker

Click **Change Language** in the InfoBar to choose from every file type registered in Visual Studio. The picker shows a curated list of popular languages at the top and includes all VS-registered content types. You can also type a custom file extension directly.

![Language picker dialog](art/language-selector.png)

### Global and solution-scoped files

- **Global scratch files** are stored in `%APPDATA%\ScratchFiles` and available across all sessions.
- **Solution scratch files** are stored in `.vs\ScratchFiles` within your solution directory and travel with the solution.

New files are created in the Global scope by default. Use **Move to Solution** or **Move to Global** in the InfoBar to change the scope of any file.

![Tool window showing global and solution files](art/toolwindow.png)

### Scratch Files tool window

Open via **View > Other Windows > Scratch Files**. The tool window shows all your scratch files organized by scope (Global vs. Solution) with file-type icons, a toolbar for creating new files, and a right-click context menu.

**Drag and drop** files and folders to reorganize them — just like Windows Explorer. Create subfolders via the context menu to keep your scratch files organized.

<!-- ![Tool window with context menu](art/tool-window-context-menu.png) -->

### Save As...

Promote any scratch file to a real file. Click **Save As...** in the InfoBar to save a copy to My Documents (or wherever you like). The original scratch file is automatically cleaned up.

## Keyboard shortcuts

| Action | Shortcut |
|---|---|
| New scratch file | `Ctrl+N` |

## Options

Go to **Tools > Options > Scratch Files** to configure:

| Setting | Default | Description |
|---|---|---|
| Override Ctrl+N | `true` | Replace the default File > New File template dialog with instant scratch file creation. Requires restart. |

## Contributing

This is a passion project, and contributions are welcome!

- 🐛 **Found a bug?** [Open an issue][repo]
- 💡 **Have an idea?** [Start a discussion][repo]
- 🔧 **Want to contribute?** Pull requests are always welcome

**If this extension saves you time**, consider:

- ⭐ [Rating it on the Marketplace][marketplace]
- 💖 [Sponsoring on GitHub](https://github.com/sponsors/madskristensen)

## License

[Apache 2.0](LICENSE.txt)
