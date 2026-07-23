# openMenu Virtual Folder Bundle

<img align="right" width="300" src="https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/blob/main/images/preview.gif?raw=true">

This is a new version of openMenu with virtual folder/subfolder support and other updates and fixes, including a modified version of GD MENU Card Manager to support it. It aims to be the ultimate menu software and SD card management solution for SEGA Dreamcast [GDEMU](https://gdemu.wordpress.com/about/) users.

openMenu was originally written by [mrneo240](https://github.com/mrneo240/openmenu/), later forked by [megavolt85](https://github.com/megavolt85/openmenu), and then finally [sbstnc](https://github.com/sbstnc/openmenu/). With each iteration, important updates have been made to this open source menu software for the GDEMU, a SEGA Dreamcast optical drive emulator.

However, due to the fragmented nature of these forks and the lack of time for each of their respective owners to contribute to them, the author of this new openMenu build has decided to create a brand-new fork, rather than contribute to any of the existing ones. As an active member of the Dreamcast community, this author will work hard to maintain, update, and fix this new version of openMenu.

For those unfamiliar with the branching history of openMenu, it's important to note that **openMenu Virtual Folder Bundle** was built on top of the latest public version of openMenu, meaning it has full support for per-game virtual VMUs with [VM2](https://www.dreammods.net/store/p6/VM2.html), [VMUPro](https://stoneagegamer.com/vmu-pro-handheld-visual-memory-card-for-sega-dreamcast.html), [USB4MAPLE](http://usb4maple.wikidot.com/), and [Pico2Maple](https://github.com/cluoma/Pico2Maple-fw).

To accommodate the new openMenu, a modified version of [GD MENU Card Manager](https://github.com/sonik-br/GDMENUCardManager) has also been developed and included in this bundle.

---

## Table of Contents

- [Current Version](#current-version)
- [Roadmap Ideas](#roadmap-ideas)
- [Changelog](#changelog)
- [Credits](#credits)
- [Changes to openMenu and GD MENU Card Manager](#changes-to-openmenu-and-gd-menu-card-manager)
  - [Changes Since openMenu v1.3.0](#changes-since-openmenu-v130)
  - [Changes Since GD MENU Card Manager v2.0.1](#changes-since-gd-menu-card-manager-v201)
- [Upgrading](#upgrading)
- [GD MENU Card Manager Usage](#gd-menu-card-manager-usage)
  - [Managing Artwork](#managing-artwork)
    - [Artwork Assignment](#artwork-assignment)
    - [Folder Artwork Assignment](#folder-artwork-assignment)
    - [DAT Tools](#dat-tools)
    - [Restoring DAT Files From Backup](#restoring-dat-files-from-backup)
    - [Restoring a New User's Personal DAT Files](#restoring-a-new-users-personal-dat-files)
    - [Cover Art Resources](#cover-art-resources)
  - [Managing SD Card](#managing-sd-card)
    - [Initial Setup](#initial-setup)
    - [Understanding the Interface](#understanding-the-interface)
    - [Adding Games](#adding-games)
    - [Removing Games](#removing-games)
    - [Reordering Games](#reordering-games)
    - [Editing Game Information](#editing-game-information)
    - [Virtual Folders (openMenu Only)](#virtual-folders-openmenu-only)
      - [Batch Folder Move/Rename](#batch-folder-moverename)
      - [Batch Folder Assignment](#batch-folder-assignment)
      - [Assigning Additional Folder Paths](#assigning-additional-folder-paths)
    - [Multi-Disc Games](#multi-disc-games)
    - [Disc Types (openMenu Only)](#disc-types-openmenu-only)
    - [Searching and Filtering](#searching-and-filtering)
    - [Sorting](#sorting)
    - [Undo and Redo](#undo-and-redo)
    - [Saving Changes](#saving-changes)
    - [Keyboard Shortcuts](#keyboard-shortcuts)
  - [Disc Image Options](#disc-image-options)
  - [Menu Options](#menu-options)
    - [Default Style and Theme](#default-style-and-theme)
    - [Background Music](#background-music)
  - [Application Settings](#application-settings)
    - [SD Drive](#sd-drive)
    - [Temporary Folder](#temporary-folder)
    - [Menu Type](#menu-type)
- [openMenu Usage](#openmenu-usage)
  - [VGA and Region Patching](#vga-and-region-patching)
  - [Using Cheats](#using-cheats)
    - [Per-Game Cheat Files](#per-game-cheat-files)
  - [Virtual VMU Device Support](#virtual-vmu-device-support)
  - [Serial VMU Feature](#serial-vmu-feature)
    - [Migration Tutorial](#migration-tutorial)
  - [Save/Load Manager](#saveload-manager)
  - [PlayStation Emulator Feature](#playstation-emulator-feature)
  - [Multi-Disc Launcher](#multi-disc-launcher)
  - [Exiting to BIOS](#exiting-to-bios)
    - [Available Options by Context](#available-options-by-context)
    - [Options Explanation](#options-explanation)
  - [Settings Menu](#settings-menu)
    - [Grid/LineDesc Mode Settings](#gridlinedesc-mode-settings)
    - [Scroll Mode Settings](#scroll-mode-settings)
    - [Folders Mode Settings](#folders-mode-settings)
  - [Button Mapping](#button-mapping)
    - [Grid/LineDesc Mode](#gridlinedesc-mode)
    - [Scroll Mode](#scroll-mode)
    - [Folders Mode](#folders-mode)
    - [Popup Menus (All Modes)](#popup-menus-all-modes)
  - [Keyboard Support](#keyboard-support)
- [Theming](#theming)
  - [Theme Format](#theme-format)
    - [General](#general)
    - [Color Styling](#color-styling)
    - [Game List Positioning and Sizing](#game-list-positioning-and-sizing)
    - [Artwork Positioning and Sizing](#artwork-positioning-and-sizing)
    - [Item Details Positioning](#item-details-positioning)
    - [Optional Clock Positioning and Color Styling](#optional-clock-positioning-and-color-styling)
- [Legal and Licensing](#legal-and-licensing)
  - [openMenu](#openmenu)
  - [GD MENU Card Manager](#gd-menu-card-manager)
  - [Third-Party Components](#third-party-components)

---

## Current Version

The latest version of **openMenu Virtual Folder Bundle** is [1.6.0-ateam](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/releases/tag/1.6.0-ateam).

---

## Roadmap Ideas

- Extend GD MENU Card Manager's **Menu Options** feature to set more default settings beyond view style and theme.
- New metadata format with optional openMenu popup window when a disc image is selected, showing screenshots, description, and more.
- Keep track of a recently played list in openMenu.
- See [Dreamcast Now](https://dreamcast.online/now/) information directly from openMenu (DreamPi or BBA required).
- Implement a housekeeping function in GD MENU Card Manager to help users easily identify gaps or issues with their disc image collection (e.g., duplicate serials, missing artwork, missing discs in multi-disc sets).
- Arbitrary disc queueing in openMenu (i.e., ability to define a playlist of disc images that can be cycled through using the in-game reset button combination).

---

## Changelog
- **Version 1.6.0-ateam (2026-07-23)**
  - **openMenu**
    - The settings menu is now scrollable in all view modes (see [Issue 168](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/168)).
    - The "VMU Beep on Save" setting has been restored (see [Issue 169](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/169)).
    - Custom folder artwork can now be displayed (see [Issue 172](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/172)).
    - Custom background music can now be played and a default style and theme can be honored (see [Issue 174](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/174)).
  - **GD MENU Card Manager**
    - Custom folder artwork can now be assigned (see [Issue 172](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/172)).
    - Editable "Region" column added in Card Manager for per-disc region patching (see [Issue 171](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/171)).
    - Card Manager now rejects Redump's old, short-lived GDI format with a clear message (see [Issue 170](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/170)).
    - Batch Folder Move/Rename function added to Linux and macOS builds (see [Issue 173](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/173)).
    - Card Manager can set a default style and theme for openMenu to use on launch, as well as custom background music (see [Issue 174](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/174)).
    - Small cosmetic cleanups (see [Issue 175](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/175)).
    - Drag-and-drop added to Linux version of Card Manager, and macOS drag-and-drop functionality enhanced (see [Issue 176](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/176)).
    - Up/down buttons added to Windows version of Card Manager (see [Issue 177](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/177)).
- **Version 1.5.5-ateam (2026-05-27)**
  - **openMenu**
    - CodeBreaker cheat database upgraded to version 2.8 (see [Issue 167](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/167)).
  - **GD MENU Card Manager**
    - Extended GDI shrink blacklist to include additional games (see [Issue 159](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/159)).
    - In macOS, fixed System Bar's "About \<Application Name\>" string (see [Issue 160](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/160)).
    - Redump GD-ROM CUE/BIN to GDI conversion now produces 1:1 identical TOSEC dumps (see [Issue 161](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/161)).
    - Added missing tooltip text to several UI elements (see [Issue 162](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/162)).
    - Small UI clean-up and consistency changes (see [Issue 163](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/163)).
    - Single GDI or CUE/BIN inside a ZIP, 7z, or RAR archive now "peeked" for quick IP.BIN metadata parsing (see [Issue 164](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/164)).
    - SharpCompress upgraded from version 0.33.0 to 0.36.0 for enhanced archive support (see [Issue 165](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/165)).
    - Fixed UI lockup when automatically converting GD-ROM CUE/BIN to GDI (see [Issue 166](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/166)).
- **Version 1.5.4-ateam (2026-05-05)**
  - **openMenu**
    - The X button now zooms artwork in LineDesc mode (see [Issue 151](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/151)).
  - **GD MENU Card Manager**
    - Fixed System Bar title on macOS from reading "Avalonia Application" to "GD MENU Card Manager" (see [Issue 154](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/154)).
    - Fixed GDI shrinking limitations and enhanced metadata extraction support (see [Issue 155](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/155)).
    - GitHub URL in the macOS/Linux Help/About window now appears as standard hyperlink (see [Issue 156](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/156)).
- **Version 1.5.3-ateam (2026-04-05)**
  - **GD MENU Card Manager**
    - CUE to CCD conversion for Mode 2 discs was broken, now fixed (see [Issue 150](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/150)).
- **Version 1.5.2-ateam (2026-04-03)**
  - **GD MENU Card Manager**
    - Support extended to arm64 architecture for macOS build (see [Issue 141](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/141)).
    - Enhanced multi-game automatic rename logic (see [Issue 142](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/142)).
    - "Sort" button no longer mentions sorting by path if in gdMenu mode (see [Issue 143](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/143)).
    - Artwork assign/manage window left/right shortcut now supports hold (see [Issue 144](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/144)).
    - Width of "Size" column increased in Linux/macOS builds in order to not cut off contents (see [Issue 145](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/145)).
    - SD Drive dropdown now displays filesystem label (see [Issue 146](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/146)).
    - Non-Dreamcast disc images (e.g., PlayStation discs, audio CDs, video CDs) now automatically converted from CUE to CCD (see [Issue 147](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/147)).
    - Region/VGA patching no longer performed on any non-Dreamcast disc image (see [Issue 148](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/148)).
    - "IP.BIN Info" window disabled for non-Dreamcast disc images (see [Issue 149](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/149)).
- **Version 1.5.1-ateam (2026-03-09)**
  - **openMenu**
    - Fixed errant CMD2 sent in Save/Load window (see [Issue 130](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/130)).
    - Fixed incorrect labeling of a VMU as "loaded" in Save/Load window during hot-swap scenarios (see [Issue 129](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/129)).
  - **GD MENU Card Manager**
    - Artwork management window now includes previous/next arrows (see [Issue 131](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/131)).
    - Tab size corrected in "DAT Tools" window for Linux/macOS version (see [Issue 132](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/132)).
    - Regression due to "no Serial ID" check fixed (see [Issue 134](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/134)).
    - Enhanced restriction of ASCII-only for Title, Folder, and Serial values (see [Issue 137](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/137)).
    - "Is Writable" check now performed on DATs and saved settings file (see [Issue 138](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/138)).
- **Version 1.5-ateam (2026-02-26)**
  - **openMenu**
    - Serial VMU feature added, enabling per-game virtual VMUs using serial SD adapter (see [Issue 108](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/108)).
    - Retro Fighters wireless controller/dongle stability greatly improved (see [Issue 121](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/121)).
    - Disc images can now appear in more than just one folder (see [Issue 110](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/110)).
    - Save/Load manager now performs live VMU device detection (see [Issue 107](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/107)).
    - The "save" openMenu VMU LCD icon now sent to any VMU for 1.5s where read/write operations are occurring (see [Issue 118](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/118)).
    - openMenu VMU LCD icon now sent live whenever a VMU is inserted (see [Issue 117](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/117)).
    - New "Exit to BIOS" options to avoid issues with custom BIOS bootloader on some console revisions (see [Issue 122](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/122)).
    - Endless/infinite scrolling added to all windows (see [Issue 109](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/109)).
    - Save files labeled "future" can now be overwritten, as well as downgraded (see [Issue 105](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/105)).
    - openMenu VMU LCD icons now correctly oriented on light gun (see [Issue 127](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/127)).
  - **GD MENU Card Manager**
    - Automatic updater added for seamless upgrades in the future (see [Issue 116](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/116)).
    - Disc images can now be assigned to more than one folder (see [Issue 110](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/110)).
    - New DAT Tools function added to overwrite local DATs with those from SD card's openMenu GDI (see [Issue 111](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/111)).
    - Metadata file `region.txt` now generated and honored, correcting issues with PAL consoles using analog video cables and some Japanese games (see [Issue 112](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/112)).
    - All "Disc Image Options" settings now remembered on next launch (see [Issue 113](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/113)).
    - Disc image "Info" window fixes and enhancements (see [Issue 114](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/114)).
    - SD card can no longer be saved in Card Manager if one or more disc images are missing a Serial ID (see [Issue 120](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/120)).
    - macOS package fixed to allow editing of artwork/metadata DATs, as well as saving custom settings (see [Issue 123](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/123)).
    - Fixed regression that prevented adding non-game disc images (e.g., audio CDs, VCDs) (see [Issue 124](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/124)).
    - Enhanced automatic disc image type detection (see [Issue 125](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/125)).
    - Card Manager now properly handles an SD card removed mid-session (see [Issue 126](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/126)).
- **Version 1.4-ateam (2026-02-14)**
  - **openMenu**
    - Fixed crash when launching games or exiting to BIOS while using "DeepDream" themes (see [Issue 98](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/98)).
    - CodeBreaker cheat database updated to include easy-to-find deflicker codes (see [Issue 97](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/97)).
    - Save/Load window's width tweaked, was too wide (see [Issue 99](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/99)).
    - Keyboard Shift+\<character\> will not erroneously trigger menu action assigned to said character (see [Issue 102](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/102)).
  - **GD MENU Card Manager**
    - Games List table can now be filtered (see [Issue 101](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/101)).
    - Section headings in help/about window now automatically line break (see [Issue 100](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/100)).
    - Window size and position remembered on next launch (see [Issue 103](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/103)).
    - Margins and spacing in Settings section made uniform, gdMenu/openMenu radio buttons now labeled "Menu Type" (see [Issue 104](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/104)).
- **Version 1.3-ateam (2026-02-13)**
  - **openMenu**
    - Support added for saving/loading settings to/from serial SD (see [Issue 84](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/84)).
    - DreamConn S "Exit to BIOS" fixed (should only cater to VM2) (see [Issue 82](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/82)).
    - Changed settings are now automatically applied after saving settings (see [Issue 80](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/80)).
    - Errors on Save/Load window no longer render outside of window (see [Issue 79](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/79)).
    - Date/time from VMU can now be synchronized to console (see [Issue 70](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/70)).
    - Loading splash screen has been added (see [Issue 91](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/91)).
    - Keyboard shortcut added to jump to items in list starting with any given character (see [Issue 92](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/92)).
    - Very rare, difficult to reproduce bug fixed where openMenu fails to launch without a VMU present (see [Issue 87](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/87)).
    - Popup window tweaks (see [Issue 94](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/94)).
  - **GD MENU Card Manager**
    - Disc images in CHD format are now automatically decompressed (see [Issue 45](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/45)).
    - Custom paths (not just SD cards) can now be used (see [Issue 89](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/89)).
    - Margins and vertical centering of several UI elements fixed (see [Issue 90](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/90)).
    - Info window height fixed, was too tall (see [Issue 93](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/93)).
    - Ellipses after progress bar messages no longer have an extra space before it (see [Issue 81](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/81)).
    - New optimized auto-creation of `GDEMU.INI` file if none exists (see [Issue 95](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/95)).
- **Version 1.2-ateam (2026-02-05)**
  - **openMenu**
    - New VMU save/load functionality with live detection of VMU devices upon loading the save manager, plus ability to upgrade old save files (see [Issue 60](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/60)).
    - New themes added, with old themes cleaned up or purged (see [Issue 44](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/44)).
    - Updated cheat database to v2.7 (see [Issue 61](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/61)).
    - Fixed vertical alignment of title bar text in credits window (see [Issue 62](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/62)).
    - Game ID support was added for Pico2Maple (see [Issue 67](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/67)).
    - Rare double button press trigger bug likely fixed (see [Issue 73](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/73)).
    - Exit to BIOS without sending Game ID now handled properly with DreamConn S (see [Issue 75](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/75)).
    - Eliminated state-transition input delay and improved button-hold detection (see [Issue 76](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/76)).
  - **GD MENU Card Manager**
    - New streamlined artwork management: artwork management directly in GD MENU Card Manager (no need for external DAT Editor), auto-generates bare minimum DAT files when none exist, prompts to auto-fix `BOX.DAT`/`ICON.DAT` mismatches, creates automatic backups to `dat_backups` folder, and performs the same Serial ID translation as openMenu (see [Issue 49](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/49)).
    - New DAT Tools window featuring import/merge, PNG export, and clear functions (see [Issue 50](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/50)).
    - Redump CUE/BIN images of both GD-ROM and CD-ROM rips now automatically converted to GDI and CDI, respectively (see [Issue 47](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/47)).
    - Moved GDI Shrink function (Windows only) to new "Disc Image Options" window, along with new VGA flag force and region patching options to support launching out-of-region games and games requiring VGA flag adjustment when launched from BIOS after "Exit to BIOS" (see [Issue 42](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/42)).
    - Search now covers both disc image titles and their Serial IDs (see [Issue 51](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/51)).
    - Added check for SD card files/folders in use, giving users a chance to close locking processes to avoid SD card corruption (see [Issue 53](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/53)).
    - Moved "Sort List" button to the top and added confirmation dialog (see [Issue 56](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/56)).
    - Added required SD card space check with graceful exit when space is exceeded (see [Issue 57](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/57)).
    - Changes made to temporary folder path are remembered on next application launch (see [Issue 58](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/58)).
    - Added undo/redo buttons to the bottom left of the main window for quickly undoing the last 10 edits to the games list, covering all operations (see [Issue 59](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/59)).
    - Removed now unnecessary Preload button (see [Issue 54](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/54)).
    - Removed Batch Rename button and moved all functions into right-click context menu, where one or more rows can be selected for batch operations (see [Issue 55](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/55)).
    - Fixed partially broken "IP.BIN Info" display (see [Issue 52](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/52)).
    - Sentence Case rename changed to Title Case and improved, Uppercase and Lowercase added (see [Issue 64](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/64)).
    - New Help/About window to reflect current state of GD MENU Card Manager (see [Issue 65](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/65)).
    - Card Manager now automatically performs Serial translations of two varieties (see [Issue 68](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/68)).
    - In addition to `DISCLIST.TXT`, a `DISCLIST.XLSX` spreadsheet is now also generated with a full listing of all disc images on SD card (see [Issue 71](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/71)).
    - Text alignment fixes in SD Drive, Temporary Folder, and Search (see [Issue 72](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/72)).
    - Tooltips added to all buttons (see [Issue 74](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/74)).
- **Version 1.1-ateam (2026-01-16)**
  - **openMenu**
    - New "Exit to BIOS" options (see [Issue 32](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/32)).
    - Added keyboard support for navigation and input (see [Issue 27](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/27), thanks [Xerxes3rd](https://github.com/Xerxes3rd)).
    - New virtual VMU device Game ID options added (see [Issue 35](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/35)).
    - Changed button mapping in Folders view mode: A launches disc, B goes back a folder, X launches CodeBreaker, Y exits to BIOS, Start opens Settings menu (see [Issue 26](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/26)).
    - Added "Boot Mode" setting for various full/fast boot options (see [Issue 39](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/39)).
    - Renamed Multi-Disc setting options from "Show"/"Hide" to "Show All"/"Compact" (see [Issue 25](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/25)).
    - Instances of "Multidisc" changed to "Multi-Disc" (see [Issue 30](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/30)).
    - New "Multi-Disc Grouping" setting added to Folders mode (see [Issue 31](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/31)).
    - Added info read-out display for cursor's currently highlighted item, with new theme parameters `item_details_x`, `item_details_y`, and `item_details_text_color` (see [Issue 24](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/24)).
    - Changed default sort option label to "Alphabetical" and added new "SD Card Order" sorting option across all view modes (see [Issue 23](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/23)).
    - Added optional real-time clock to Folders view mode (see [Issue 29](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/29)).
    - Renamed "Index" setting to "Display Index Numbers" for a clearer label in Scroll mode (see [Issue 22](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/22)).
    - Added option to hide/show disc details in Scroll mode Settings menu (see [Issue 18](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/18)).
    - Increased maximum supported discs in Multi-Disc launcher from 4 to 10 (see [Issue 17](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/17)).
    - Added new theme parameters `list_count` and `list_marquee_threshold` for Folders mode (see [Issue 16](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/16)).
    - Added blue swirl default theme variant, as well as red/blue swirl versions of a text-only theme (see [Issue 15](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/15) and [Issue 33](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/33)).
    - Added confirmation icon after updating VMU save file (see [Issue 14](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/14)).
    - Long game names in Multi-Disc popup now truncated with ellipsis (see [Issue 12](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/12)).
    - Added "PSX" disc type with optional Bloom emulator launcher for PlayStation disc images (see [Issue 9](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/9)).
    - Disc image now mounts on GDEMU when user selects "Exit to BIOS", enabling direct game launch or playback of a game disc's CDDA from BIOS (see [Issue 6](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/6)).
    - Non-game disc images (e.g., audio CDs) now skip full boot cycle and return directly to BIOS (see [Issue 5](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/5)).
    - Removed non-functional "Aspect" setting from Scroll mode (see [Issue 20](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/20)).
    - Fixed cover art sometimes displaying oversized/zoomed in FolderDefault theme (see [Issue 19](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/19)).
    - Fixed inconsistent margins and positioning of popup windows (see [Issue 8](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/8)).
    - Fixed PlayStation disc launching, which was non-functional in v1.0 (see [Issue 9](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/9)).
    - CodeBreaker launcher turned into navigable menu like all other popup windows (see [Issue 34](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/34)).
    - Folders mode is now default (see [Issue 36](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/36)).
    - "Close" buttons added to Multi-Disc and PlayStation Emulator launchers (see [Issue 37](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/37)).
  - **GD MENU Card Manager**
    - Added blue swirl default theme variant (see [Issue 15](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/15)).
    - Now writes `DISCLIST.TXT` to SD card root (see [Issue 13](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/13)).
    - First entry in list is no longer editable (see [Issue 11](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/11)).
    - Added "PSX" option to Type column dropdown for PlayStation disc images (see [Issue 9](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/9)).
    - Added batch folder rename/move functionality in Windows build (see [Issue 7](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/7)).
    - Folder column cells now have dynamic dropdown for folder selection in Windows build (see [Issue 4](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/4)).
    - "Sort List" button now sorts by combination of Folder + Title instead of just Title (see [Issue 2](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/2)).
    - Fixed missing default Scroll (gdMenu) style theme (see [Issue 10](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/10)).
    - Fixed CD-ROM/XA format disc parsing issues (see [Issue 3](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/3)).
    - Fixed virtual folder path being ignored on disc images without an ID (see [Issue 1](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/1)).
    - Default release package now ships with an empty META.DAT (see [Issue 38](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/38)).
    - Changed automatic disc image renaming option behavior (see [Issue 40](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/40)).
    - Updated File/Directory Move operations to overwrite existing to prevent errors (see [Issue 41](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/issues/41)).
- **Version 1.0-ateam (2026-01-07)**
  - Initial release (see the [Changes to openMenu and GD MENU Card Manager](#changes-to-openmenu-and-gd-menu-card-manager) section).

---

## Credits

- **Programming**
  - Derek Pascarella (ateam)
- **Testing**
  - NO HXRO
  - SEGA RPG FAN
  - Kanji
  - agarpac
  - null
  - Ade
  - NeoGohan
  - pipporamone
  - Tongara
  - Chris Daioglou
  - Retro Repair Corner
  - Farkus
  - FMRad10
  - Colin L.
  - PlayerGameSK
  - Virtua Shenmue
  - Nachosonic
  - Patrick Graham
  - onlycodered
- **Special Thanks**
  - mrneo240 for his original work on openMenu
  - megavolt85 for forking openMenu and adding new features
  - sbstnc for forking openMenu and adding a Docker development container
  - Sonik for his work on GD MENU Card Manager
  - RazorX, Chris Daioglou, and the testers for good ideas for new features
  - darc for [Redump2CDI](https://dreamcast.wiki/Redump2CDI), used for Redump CD-ROM CUE/BIN to CDI conversion
  - All of the wonderful people in the Dreamcast scene

---

## Changes to openMenu and GD MENU Card Manager

Many components of both openMenu and GD MENU Card Manager have been drastically overhauled for this version. See below for a list of changes.

### Changes Since openMenu v1.3.0

Below represents all the changes brought with the initial release of **openMenu Virtual Folder Bundle**. Since then, many more changes have been made (see the [Changelog](#changelog) section).

- New "Folders" Style mode, plus custom themes:
  - Virtual folder support includes eight levels of nested subfolders.
  - Complete subfolder path maximum length is 256 characters, including backslashes (e.g., `Games\JP\RPG`).
  - Maximum number of virtual folders supported per SD card is 1024.
- Long entry names now supported (256 characters), along with marquee scrolling over those exceeding 49 characters.
- Scroll mode improvements:
  - Jumping cursor fixed
  - New setting to toggle index numbers in the game list (On/Off)
  - New setting to toggle artwork display (On/Off)
- Settings menu now has wrap-around cursor scrolling.
- GDEMU firmware version and openMenu build version now shown in the Settings menu.
- VMU configuration save file is no longer written on every openMenu startup, now only when saving settings or during save file migration.
- Non-game discs are mounted by GDEMU instantly, returning user to BIOS for things like playing audio CDs.
- Game ID is not sent to VM2/VMUPro/USB4MAPLE when a non-game disc is launched (e.g., an audio CD).
- VMU beep setting disabled (later restored as the "VMU Beep on Save" setting).
- Credits updated to include additions from this author.

### Changes Since GD MENU Card Manager v2.0.1

Below represents all the changes brought with the initial release of **openMenu Virtual Folder Bundle**. Since then, many more changes have been made (see the [Changelog](#changelog) section).

- Game list column widths and window width adjusted for better visibility.
- Version check in About window removed (with possibility of returning in the future).
- A new "Folder" column added when building menu in openMenu mode, allowing users to specify virtual folder path for a disc image (e.g., `Games\RPGs\NTSC-U`).
- Non-game disc images now supported, with a new column named "Type" added where users can select from two dropdown options: `Game` and `Other`. Note that non-game disc images are automatically assigned the `Other` value, as openMenu will check this property to decide how to initialize the GDEMU.
- Disc image names no longer limited to 39 characters, increased to 256 characters.
- Disc numbers can now be customized by the user (e.g., `1/3`, `2/4`).
- Maximum length for Game IDs is now ten (instead of 9), but is customizable by changing the `ProductIdMaxLength` key inside `GDMENUCardManager.dll.config`.
- Latest version of [CodeBreaker cheats database](https://www.dreamcast-talk.com/forum/viewtopic.php?t=17078) included by default.

---

## Upgrading

As of `v1.5-ateam`, the Windows and Linux versions of this software package include an automatic in-app updater. However, macOS users must undertake the process described below.

The upgrade/migration process is very simple for:

- Upgrading from a previous version of **openMenu Virtual Folder Bundle** to the latest version.
- Upgrading from an old version of the openMenu/GD MENU Card Manager combination to the latest version of **openMenu Virtual Folder Bundle**.
- Migrating from legacy gdMenu to the latest version of **openMenu Virtual Folder Bundle**.

Regardless of scenario, users follow these steps:

1. Download the [latest release package](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/releases) for the appropriate platform (Windows, Linux, macOS).
2. Restore personal DATs if applicable (see the [Restoring a New User's Personal DAT Files](#restoring-a-new-users-personal-dat-files) section).
3. Insert GDEMU SD card into computer.
4. Launch GD MENU Card Manager.
5. Ensure "openMenu" is selected in the top right radio button, then click **Save Changes**.

---

## GD MENU Card Manager Usage

### Managing Artwork

GD MENU Card Manager provides a streamlined artwork management system for adding cover art to games and other disc images, as well as to virtual folders themselves. Artwork is stored in DAT files (e.g., Folders mode uses `BOX.DAT` for full-size cover art and `FOLDRART.DAT` for folder artwork) located in the `tools\openMenu\menu_data` folder (`~/Library/Application-Support/GDMENUCardManager/menu_data` on macOS).

#### Artwork Assignment

To assign artwork to a game:

1. In the Games List table, click the **Assign** or **Manage** button under the **Art** column in the row of the game to edit.
   - The button is labeled **Assign** if no artwork is presently tied to the Serial ID, and **Manage** if there is.
2. The Artwork window displays the game's Serial ID and current artwork (if any).
3. Click **Browse for Image** to select an image file (supported formats: PNG, JPG, JPEG, GIF, WebP, BMP, TIFF, TGA).
   - Avoid images with transparency (e.g., PNGs with transparent backgrounds).
4. The application automatically generates two PVR textures:
   - 256x256 pixels for `BOX.DAT` (full-size artwork shown in openMenu)
   - 128x128 pixels for `ICON.DAT` (thumbnail preview used in Grid mode)
5. Preview the artwork in the window, then click **Save** to apply the changes.
6. To remove artwork, click **Delete Entry** (only available when the game has existing artwork and no pending changes).

**Important Notes:**
- Artwork changes are held in memory until the user clicks **Save Changes** in the main window to write them to the DAT files.
- The application automatically performs Serial ID translation to ensure artwork is filed under the correct serial that openMenu uses internally. This handles regional variants and problematic disc IDs automatically. Because of this, users may notice Serial IDs are automatically changed for certain games (e.g., NTSC-U "Nightmare Creatures 2" has ID `T9504M` but will be automatically changed to `T9504N`). If the user attempts to revert back to the original Serial ID, a window will appear with a message explaining why the automatic translation is recommended, as well as the ability to override that recommendation.
- If `BOX.DAT` and `ICON.DAT` don't exist in the `tools\openMenu\menu_data` folder (`~/Library/Application-Support/GDMENUCardManager/menu_data` on macOS), the application will auto-generate minimal starter files.
- If a mismatch is detected between `BOX.DAT` and `ICON.DAT` (mismatched entry counts), the user will be prompted to auto-fix the issue.
- All DAT modifications are backed up automatically to the `dat_backups` folder before changes are made.

#### Folder Artwork Assignment

Artwork can also be assigned to virtual folders, which openMenu displays when the cursor hovers over a folder in Folders view mode (see the [Folders Mode Settings](#folders-mode-settings) section for the "Folder Artwork" setting).

To assign artwork to a folder:

1. Click the **Folder Tools** button at the top right of the **Games List** table (available in openMenu mode) and open the **Folder Artwork** tab.
2. The folder list shows every folder and subfolder currently in use, with subfolders indented beneath their parent.
3. Click the **Assign** or **Manage** button in the row of the folder to edit.
   - The button is labeled **Assign** if no artwork is presently tied to the folder, and **Manage** if there is.
4. The editing window works identically to the game artwork window: click **Browse for Image**, preview, then click **Save**. Left/right arrows navigate between folders.
   - Avoid images with transparency (e.g., PNGs with transparent backgrounds).
5. To remove artwork, click **Delete Entry**.

Folder artwork is stored in `FOLDRART.DAT`, with a companion `FOLDRART.MAP` file that keeps track of which folder each entry belongs to. Both files live alongside the other DATs in the `tools\openMenu\menu_data` folder (`~/Library/Application-Support/GDMENUCardManager/menu_data` on macOS).

**Important Notes:**
- Like game artwork, folder artwork changes are held in memory until the user clicks **Save Changes** in the main window.
- Folder artwork automatically follows folders renamed or moved with the **Batch Folder Move/Rename** function.
- If a folder is emptied out of existence through individual game edits, its artwork is kept and appears in an "Unassigned artwork" section beneath the folder list, where it can be viewed, re-assigned to a current folder, or deleted. Re-creating a folder with the exact same name automatically re-attaches its artwork.

#### DAT Tools

The **DAT Tools** button opens a window providing four functions for managing artwork data:

**Import DAT Entries**
- Merge artwork entries from another installation's DAT files into the current setup.
- Click **Browse** to select a folder containing `BOX.DAT` and/or `META.DAT` files.
- Choose import mode:
  - **Import only missing entries**: Only adds artwork for serials not already in current DAT files.
  - **Import all entries (overwrite existing)**: Replaces all matching entries with the imported versions.
- `ICON.DAT` is automatically regenerated from the merged `BOX.DAT`.
- Original DAT files are backed up before the merge operation.

**Export Artwork to PNGs**
- Export all artwork from currently assigned artwork to PNG image files.
- Click **Browse** to select an output folder.
- Files are named using the format: `Title [Serial].png` for game artwork and `Folder-Path [Folder ID].png` for folder artwork.
- Useful for backing up artwork or sharing with others.

**Clear All DAT Entries**
- Removes all entries from `BOX.DAT`, `ICON.DAT`, and `META.DAT`, as well as all folder artwork (`FOLDRART.DAT` and `FOLDRART.MAP`).
- Creates fresh, empty DAT files.
- Original files are backed up before clearing.
- **Warning**: This action cannot be undone except by restoring from the backup.

**Overwrite DATs with SD Card**
- Overwrite application's local DAT files with `BOX.DAT`, `ICON.DAT`, and `META.DAT` from the SD card's current openMenu build in the `01` folder, along with `FOLDRART.DAT` and `FOLDRART.MAP` when present.
- Original files are backed up before being replaced.

#### Restoring DAT Files From Backup

Automatic backups are saved to the `dat_backups` folder with timestamps (located in `~/Library/Application-Support/GDMENUCardManager` on macOS). To restore:

1. Navigate to the `dat_backups` folder in the GD MENU Card Manager application directory (`~/Library/Application-Support/GDMENUCardManager/dat_backups` on macOS).
2. Find the backup folder with the desired timestamp.
3. Copy the `BOX.DAT`, `ICON.DAT`, and `META.DAT` files (plus `FOLDRART.DAT` and `FOLDRART.MAP`, if folder artwork was in use) from the backup, removing the timestamp from the file name.
4. Paste them into `tools\openMenu\menu_data` (`~/Library/Application-Support/GDMENUCardManager/menu_data` on macOS), overwriting the current files.

#### Restoring a New User's Personal DAT Files

New users of **openMenu Virtual Folder Bundle** on Windows and Linux may wish to use their existing DAT files containing artwork and metadata. There are two options for performing this operation.

**Restoring From Files**

Navigate to the `tools\openMenu\menu_data` folder in the original GD MENU Card Manager application folder and copy `BOX.DAT`, `ICON.DAT`, and `META.DAT` (plus `FOLDRART.DAT` and `FOLDRART.MAP`, if folder artwork was in use) to the `tools\openMenu\menu_data` folder inside the **openMenu Virtual Folder Bundle**'s GD MENU Card Manager application folder, overwriting the defaults.

macOS users may take their old DATs and copy and paste them into `~/Library/Application-Support/GDMENUCardManager/menu_data`.

**Automatically Restoring From SD Card**

With the **Overwrite DATs with SD Card** function in **DAT Tools** (see more in the [DAT Tools](#dat-tools) section), users can take the DATs directly from their openMenu SD card and use them in GD MENU Card Manager.

#### Cover Art Resources

A collection of Dreamcast cover art maintained by [RazorX](https://x.com/RazorX2017) is available for download:

[Dreamcast Cover Art Repository](https://mega.nz/folder/td8wVDDZ#2R7lKrrF52MVKNDobxW7LQ)

This collection contains high-quality cover images that can be used with the artwork assignment feature.

### Managing SD Card

#### Initial Setup

1. Insert GDEMU SD card into computer.
2. Launch GD MENU Card Manager.
3. Select SD card from the **SD Drive** dropdown. The application will auto-detect drives containing `GDEMU.INI` or a folder named `01`. Alternatively, click the folder browse icon to the left of the dropdown to select any custom path to be treated as if it were a GDEMU SD card.
4. Choose a **Menu Type**:
   - **gdMenu** - Traditional Scroll mode menu interface.
   - **openMenu** - Full-featured menu with virtual folder support.

If needed, click **Refresh Drive List** to rescan available drives after inserting an SD card.

#### Understanding the Interface

The Games List table displays all disc images currently on the SD card. Each column serves a specific purpose:

| Column | Description |
|--------|-------------|
| **Info** | Click the icon to view detailed file information, `IP.BIN` metadata, and `0GDTEX.PVR` artwork preview. |
| **Location** | Shows whether the game is on "SD Card" or "Other" (pending copy). |
| **SD #** | The numbered folder on the SD card (`01`, `02`, `03`, etc.). |
| **Size** | File size of the disc image. |
| **Title** | Game display name (editable, max 256 characters). |
| **Folder** | Virtual folder path for organization (openMenu only, e.g., `Games\RPGs`). |
| **Serial** | Product ID from `IP.BIN` (editable, max 12 characters). |
| **Art** | Click the **Assign** or **Manage** button to open the artwork editing window for that game. |
| **Disc** | Disc number for multi-disc games (editable in openMenu mode only). |
| **Region** | Region flag from `IP.BIN`: any combination of `J` (Japan), `U` (USA), and `E` (Europe) (e.g., `JUE` or `U`). Editing this value patches the disc image itself when saving changes to an SD card (see the [Editing Game Information](#editing-game-information) section). |
| **Type** | Disc type: `Game`, `Other`, or `PSX` (openMenu only). Note that type `Other` will exit directly to BIOS, useful for things like audio CDs. |

**Note**: The first entry in the list is reserved for the menu disc image itself and cannot be edited, moved, or deleted.

#### Adding Games

Games can be added in three ways:

- Click the **Add** button (plus icon) and select one or more files from the file browser.
- Drag and drop files directly onto the application window.
- Add compressed archives (7Z, RAR, ZIP) containing supported disc images.

Supported disc image formats: GDI, CDI, CCD, MDS, CHD, and CUE/BIN (Redump format).

Note that GDI disc images in Redump's old, short-lived GDI format are automatically detected and rejected with an informational message, as this format is not supported. TOSEC-style GDI and modern Redump CUE/BIN dumps of the same game are supported instead.

**Automatic Redump CUE/BIN Conversion**

GD MENU Card Manager automatically detects and converts Redump-style CUE/BIN disc images:

- **GD-ROM CUE/BIN**: Automatically converted to GDI format, and the built-in TOSEC DAT is leveraged to ensure the resulting GDI matches byte-for-byte perfectly to the original TOSEC dump. The converter detects GD-ROM images by the presence of a "HIGH-DENSITY AREA" comment in the CUE file and properly handles track indexing and LBA calculations.
- **CD-ROM CUE/BIN**: Automatically converted to CDI format using Redump2CDI.

This feature allows users to add Redump-format disc images directly without manual conversion.

Additionally, GD MENU Card Manager automatically detects and converts non-Dreamcast disc images in CUE format to CCD format. Such images include audio CDs, video CDs, and PlayStation discs.

#### Removing Games

Select one or more games and either:

- Click the **Remove** button (minus icon).
- Press the **Delete** key.

Removed games are deleted from the SD card when **Save Changes** is clicked.

#### Reordering Games

Use the **Move Up** and **Move Down** buttons (arrow icons) to change the order of selected games, or drag them from place to place. The order determines how games appear in openMenu's "SD Card Order" sort mode and affects folder numbering on the SD card.

#### Editing Game Information

**Renaming a Single Game**

- Double-click the **Title** cell, or
- Right-click and select **Rename**.

**Batch Renaming**

Select multiple games by holding the Shift or Control keys, then right-click and choose a rename option:

- **Using IP.BIN info** - Uses the game name embedded in the disc image's `IP.BIN` header.
- **Using its folder name on computer** - Uses the containing folder's name (disabled when disc image is already on SD card).
- **Using disc image's base file name** - Uses the disc image's filename (disabled when disc image is already on SD card).

**Title Case**

Right-click selected games and choose **Title Case** to convert titles to Title Case formatting.

**Editing Region**

Double-click the **Region** cell to change a game's region flag. Any combination of `J`, `U`, and `E` is accepted (e.g., `J`, `UE`, `JUE`), with input automatically normalized (invalid characters are stripped, and an entry with nothing usable silently reverts to the previous value).

Unlike other columns, the Region value lives inside the disc image itself. The edit is held in the application and applied when clicking **Save Changes**, at which point the `IP.BIN` region flag and region strings are rewritten so the change takes effect when launching from the BIOS, and the `region.txt` metadata file is updated to match. Region is editable for uncompressed `Game` type disc images, whether already on the SD card or newly added (new games are patched after being copied to the SD card, so the original source file is never modified). A manually edited region takes priority over the global **Region-Free Patch** option during the save that applies it. Note that on subsequent saves, the **Apply to existing games** option will re-patch the game to region-free like any other (see [Disc Image Options](#disc-image-options)). If a region change cannot be applied during a save, the value is reverted and a warning lists the affected games.

#### Virtual Folders (openMenu Only)

When using openMenu mode, the **Folder** column allows organizing games into a hierarchical folder structure:

- Use backslashes to create nested folders (e.g., `Games\Action\US`).
- Maximum folder path length is 256 characters.
- Up to eight levels of nested subfolders are supported.
- Maximum of 1024 virtual folders per SD card.

##### Batch Folder Move/Rename

By clicking the **Folder Tools** button at the top right of the **Games List** table and opening the **Batch Folder Move/Rename** tab, users can perform bulk folder rename and move operations, affecting all associated disc images. Any folder artwork assigned to renamed or moved folders automatically follows them (see the [Folder Artwork Assignment](#folder-artwork-assignment) section).

##### Batch Folder Assignment

By selecting two or more rows in the **Games List** table, users can right-click and execute **Assign Folder Path** to update the virtual folder path definition for multiple disc images at once.

To assign multiple games to the same folder, edit each game's Folder cell with the identical path.

##### Assigning Additional Folder Paths

openMenu is capable of displaying a single disc image in multiple folders. This is useful for those who may want alphabetized folders, as well as those organized by genre.

Select a disc image in the **Games List** table, then right-click and select **Assign Additional Folder Paths**. Up to five additional folder paths can be assigned per disc image.

#### Multi-Disc Games

Edit the **Disc** column to specify disc numbering (e.g., `1/4` for disc one of four). openMenu's "Compact" multi-disc setting groups games with matching titles and sequential disc numbers into a single entry with a disc selection popup.

It's important to note that in order for openMenu to correctly identify that multiple discs are part of a single set, **they must share the same unique Serial value** when the SD card was authored.

For arbitrary multi-disc collections, such as an MP3, Karaoke, or VCD player boot disc plus a collection of media discs, all associated discs must appear in sequential order so that the disc-swap button on the GDEMU PCB can cycle through them correctly (while also sharing the same Serial). Next, ensure they share a sequential disc number and count value, with 10 discs being the maximum. See the example below, where the first entry is the MP3 player boot disc itself and all subsequent entries are the discs containing MP3 files.

| Title | Folder | Serial | Disc | Type |
|-------|--------|--------|------|------|
| MP3 Discs | Multimedia\Audio | MP3 | 1/4 | Game |
| MP3 Discs (Rock) | Multimedia\Audio | MP3 | 2/4 | Other |
| MP3 Discs (Country) | Multimedia\Audio | MP3 | 3/4 | Other |
| MP3 Discs (Jazz) | Multimedia\Audio | MP3 | 4/4 | Other |

In the table above, notice how even when using openMenu's "Alphabetical" sort setting, the MP3 player boot disc will appear first on the list, followed by each disc containing MP3 files. See more in the [Multi-Disc Launcher](#multi-disc-launcher) and [Folders Mode Settings](#folders-mode-settings) sections.

#### Disc Types (openMenu Only)

The **Type** column categorizes disc images:

| Type | Description |
|------|-------------|
| **Game** | Standard Dreamcast game (default). |
| **Other** | Non-game discs (audio CDs, utilities). These skip the full boot sequence and return directly to the BIOS. |
| **PSX** | PlayStation disc images for use with either the Bleemcast! or Bloom emulators (see the [PlayStation Emulator Feature](#playstation-emulator-feature) section). |

#### Searching and Filtering

Enter a search term in the search box and click **Search** (or press Enter). The search checks:

- Game titles
- Serial IDs (product numbers)

The first matching game is highlighted and scrolled into view. Click **Search** again to find the next match. The search is case-insensitive.

#### Sorting

Click **Sort List** to alphabetically sort all disc images. When in openMenu mode, this is a combination of Folder path and Title. In gdMenu mode, sorting is done by Title alone. A confirmation dialog is shown before sorting is applied.

#### Undo and Redo

The **Undo** and **Redo** buttons at the bottom left of the main window allow users to quickly reverse or reapply changes to the Games List table. The system tracks up to 10 levels of history and supports all operations including:

- Adding and removing games
- Editing titles, serials, folders, disc numbers, regions, and types
- Reordering games (drag-and-drop or move up/down)
- Batch operations (bulk rename, folder assignment)
- Artwork changes (both game and folder artwork)

#### Saving Changes

Click **Save Changes** to commit all modifications to the SD card. Before saving, the application performs several checks:

**SD Card Space Check**

The application calculates the required space for all operations:
- Space needed for new games (including decompression of archives)
- Space freed by removed games
- Space for menu files and metadata

If there isn't enough space, a warning dialog shows the shortfall. Users can choose to proceed at their own risk or cancel to free up space first.

**File Lock Check**

Before modifying files, the application checks if any SD card files or folders are in use by other programs. If locked files are detected:
1. A dialog lists the affected paths and error messages.
2. Close any programs that may be using the files (file explorers, media players, etc.).
3. Click **Retry** to check again, or **Cancel** to abort the save.

This prevents SD card corruption from concurrent file access.

Users can choose to forgo this operation by unchecking the **File/Folder Lock Check** option, a setting that will be remembered the next time the application is launched.

**Save Operations**

When the save proceeds, it:
- Renumbers and renames SD card folders as needed.
- Copies pending disc images to the SD card.
- Updates `BOX.DAT`, `ICON.DAT`, and `FOLDRART.DAT` if the session contained any artwork assignment changes.
- Generates openMenu configuration, including a virtual folder structure where applicable.
- Copies menu disc image files to folder `01`.
- Generates `DISCLIST.TXT` and `DISCLIST.XLSX` containing a list of all disc titles.

A "Done!" message confirms successful completion.

#### Keyboard Shortcuts

| Key | Action |
|-----|--------|
| **Delete** | Remove selected item(s) |
| **Escape** | Close window |
| **F2** | Rename selected item |
| **Ctrl+Z/Y** | Undo/Redo |

### Disc Image Options

The **Disc Image Options** window provides settings that affect how disc images are processed when saved to the SD card.

**GDI Shrink (Windows Only)**
- Reduces the size of GDI disc images on the SD card by removing unused data.
- Options:
  - **Apply to newly added games**: Shrink games when they're first copied to the SD card.
  - **Apply to existing games**: Shrink games already on the SD card.
  - **Also shrink compressed**: Apply shrinking to games added from compressed archives.
  - **Use blacklist (recommended)**: Skip shrinking for games known to have issues with the process.

**VGA Patch**
- Forces the VGA flag on games launched via "Exit to BIOS" from openMenu.
- Useful for games that don't natively support VGA output but work with the flag forced.
- Options:
  - **Apply to newly added games**: Patch games when they're first copied.
  - **Apply to existing games**: Patch games already on the SD card.

**Region-Free Patch**
- Enables launching out-of-region games via "Exit to BIOS" from openMenu.
- Patches games to bypass region lockout when launched from the BIOS.
- Options:
  - **Apply to newly added games**: Patch games when they're first copied.
  - **Apply to existing games**: Patch games already on the SD card.
- Games whose region was manually edited via the **Region** column keep their chosen region during the save that applies the edit. Note that on subsequent saves, this option will re-patch them to region-free like any other game (see [Editing Game Information](#editing-game-information)).

Click **Save** to apply changes, or simply close the window to discard them.

### Menu Options

The **Menu Options** button at the top right of the **Games List** table (available in openMenu mode) opens a window with two tabs, each with its own **Apply** button that saves only that tab's settings. Changes take effect the next time **Save Changes** is clicked in the main window.

#### Default Style and Theme

This tab forces openMenu to boot with a chosen style and theme, overriding whatever is saved on the VMU or serial SD card.

1. Check **Force default style and theme on boot**.
2. Select a style: **Folders**, **Scroll**, **Grid3**, or **LineDesc**.
3. Select a theme from the dropdown. Only themes compatible with the selected style are listed, including custom themes.
4. Click **Apply**.

To stop forcing a default, uncheck **Force default style and theme on boot** and click **Apply** again.

Alternatively, setting "Honor Menu Defaults" to "Off" in openMenu's Settings menu makes the console use its saved style and theme again without changing anything in Card Manager (see the [Settings Menu](#settings-menu) section).

#### Background Music

This tab adds looping background music to openMenu.

1. Check **Enable background music**.
2. Click **Browse** and select a music file (supported formats: WAV, MP3, OGG, FLAC).
3. Click **Apply**. The track is automatically converted to a Dreamcast-native format (roughly 2.65 MB per minute of audio) and stored inside the menu disc image.

To remove background music, uncheck **Enable background music** and click **Apply**.

Once a music track is present, playback can be toggled on the console with the "Play BGM" setting in openMenu's Settings menu (see the [Settings Menu](#settings-menu) section).

### Application Settings

#### SD Drive

Select SD card from the **SD Drive** dropdown. The application will auto-detect drives containing `GDEMU.INI` or a folder named `01`. Alternatively, click the folder browse icon to the left of the dropdown to select any custom path to be treated as if it were a GDEMU SD card.

#### Temporary Folder

GD MENU Card Manager uses a temporary folder for operations like decompressing archives and converting disc images. By default, it uses the system's temp folder, but users can specify a custom location:

1. Click the **Settings** button (gear icon) in the main window.
2. Browse to select a new temporary folder location.
3. The setting is saved to `GDMENUCardManager.TempFolder.config` in the application folder and remembered for future sessions.

This is useful if a user's system drive has limited space or if a user prefers to use a faster drive for temporary operations.

#### Menu Type

Choose a **Menu Type**:
- **gdMenu** - Traditional Scroll mode menu interface.
- **openMenu** - Full-featured menu with virtual folder support.

---

## openMenu Usage

### VGA and Region Patching

openMenu's default behavior is to both VGA patch and region patch any and all launched disc images. As a result, there is no setting to toggle said behavior.

### Using Cheats

While hovered over a game in Folders view mode, press the X button to launch it with CodeBreaker for cheats. When in Grid, LineDesc, and Scroll view modes, press the B button.

On Windows and Linux, the cheat database is stored in GD MENU Card Manager's `tools\openMenu\menu_data\CHEATS\FCDCHEATS.BIN` folder, which is the [latest available version](https://www.dreamcast-talk.com/forum/viewtopic.php?t=17078). Cheat editing isn't supported on macOS.

#### Per-Game Cheat Files

openMenu supports per-game cheat files for customized cheat configurations. When launching a game with CodeBreaker:

1. openMenu first looks for a game-specific cheat file named after the game's Serial ID: `tools\openMenu\menu_data\CHEATS\[SERIAL].BIN` (e.g., `MK51049.BIN` for "ChuChu Rocket!").
2. If no per-game file exists, it falls back to the global `FCDCHEATS.BIN` database.

**Creating Per-Game Cheat Files**

To create a per-game cheat file, download the [XPDC Code Compiler](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/raw/refs/heads/main/tools/xpdc_code_compiler%20(Gent%202025).exe) from this repository.

1. Launch Code Compiler and click **Import Save**, select `Binary Files (*.bin)` for "Files of type", then navigate to the `tools\openMenu\menu_data\CHEATS` folder and select the example `MK51178.BIN` file.
2. In the tool's main window will be an example layout for an individual game's cheats. Use this format to compile a cheat code list for the desired game.
3. Once complete, click **Browse File**, select `Binary Files (*.bin)` for "Files of type", then navigate to the `tools\openMenu\menu_data\CHEATS` folder, enter the game's unique Serial ID plus the `.bin` file extension (e.g., `HDR0005.BIN`), then click **Save**. A game's Serial ID can be found under the "Serial" column in GD MENU Card Manager.
4. Click **Compile Save** to write the new per-game cheat file to the `tools\openMenu\menu_data\CHEATS` folder.
5. Insert the GDEMU SD card into computer, launch GD MENU Card Manager, and click **Save Changes** to rebuild openMenu with the new cheat file.

Note that this same method can be used to modify the global `FCDCHEATS.BIN` database file, and new codes are sometimes posted to the cheat collection's [Dreamcast-Talk.com Forum Thread](https://www.dreamcast-talk.com/forum/viewtopic.php?t=17078). Users should be warned that a cheat database file may have size limits, and after a certain length entries for one or more games may disappear from the CodeBreaker menu.

### Virtual VMU Device Support

openMenu supports sending Game IDs to virtual VMU devices like the VM2, VMUPro, USB4MAPLE, and Pico2Maple to achieve per-game VMUs. The presence of such a device affects some features, like "Exit to BIOS" (see more in the [Exiting to BIOS](#exiting-to-bios) section).

Note that unlike previous versions of openMenu, **openMenu Virtual Folder Bundle** has an option to transmit Game IDs to any and all virtual VMU devices connected to the console when launching disc images and when choosing "Send Game ID" when exiting to BIOS.

Use the "Game ID" setting (see the [Settings Menu](#settings-menu) section) to turn transmission on or off.

Note that the Serial VMU (see the [Serial VMU Feature](#serial-vmu-feature) section) and Game ID features are mutually exclusive, and enabling one will disable the other.

### Serial VMU Feature

**When the Serial VMU feature is enabled, users must save openMenu settings to serial SD card instead of VMU** (see the [Save/Load Manager](#saveload-manager) section).

openMenu supports backing up and restoring VMU save data to and from a serial SD adapter (like the one [sold here](https://www.amazon.com/ALOEU-Adapter-Reader-Dreamcast-Dreamshell/dp/B0CSDNQ1HF)) on a per-game basis. When enabled, launching a game will automatically restore VMU data from the serial SD card before booting, and the next time openMenu starts it will back up any changes made during gameplay. This allows users to maintain persistent per-game VMUs without relying on excessive physical VMU storage.

New users of this feature may find [this video overview](https://youtu.be/ycVAZJv6TYE) helpful in understanding how it works.

Over the years, it's been noted that the Dreamcast serial SD adapter is picky with SD card support. Users have reported success with cards like [this](https://www.amazon.com/dp/B00FQMWTGK), [this](https://www.amazon.com/dp/B004CZ0H1Q), and [this](https://www.amazon.com/dp/B094YW3MCL). Furthermore, users have reported more success when they select "Default allocation size" for the **Allocation unit size** option when formatting their SD card.

On a FAT32-formatted serial SD card, individual per-game VMUs can be found in the `OPENMENU\SAVES` folder, where each game's unique Serial ID is used to create a dedicated folder (e.g., `OPENMENU\SAVES\MK5115450`). Inside this folder are the individual `.VMU` files for each slot, along with a `TITLE.TXT` file containing the full title of the game to which that Serial VMU belongs. These `.VMU` files can be modified using tools like [VMU Explorer](https://segaretro.org/VMU_Explorer).

The "Serial VMU" setting (see the [Settings Menu](#settings-menu) section) specifies which controller port and socket to use for backup and restore operations (e.g., Port A Socket 1, Port B Socket 2, etc.). Set it to "Off" to disable the feature entirely.

- **Restoring (game launch)**: When a game is launched (either directly, with cheats, or when exiting to BIOS), openMenu checks the serial SD card for a matching Serial VMU. If found, it is copied to the VMU in the configured port/socket before booting the game. If no Serial VMU exists, a blank VMU or a copy of the currently inserted physical VMU is prepared instead.
- **Backing up (next boot)**: The next time openMenu starts, it detects that a game was previously launched with Serial VMU active and automatically backs up the physical VMU data from the configured port/socket to the serial SD card.
- **Exit to BIOS**: When using "Exit to BIOS", openMenu offers to create a Serial VMU for that game, or restore that game's existing Serial VMU.

When the "Serial VMU Multi-Slot" setting is turned on (see the [Settings Menu](#settings-menu) section), openMenu provides five save slots per game instead of one. A slot selector is displayed before any backup or restore operation, allowing the user to choose which slot to use. Each slot shows a timestamp indicating when it was last written to, or "EMPTY" if unused.

With the "Serial VMU Multi-Slot" setting turned on, a custom display label can be given to each slot. On the serial SD card, navigate to the `OPENMENU\SAVES` folder and locate the dedicated folder for that game's Serial VMU (e.g., `OPENMENU\SAVES\MK5115450`). Next, find the `.VMU` file for a specific slot (e.g., `SLOT1.VMU`) and then create a `.TXT` file alongside it using the same base file name (e.g., `SLOT1.TXT`). Up to 28 characters are supported for custom slot labels, and anything exceeding that length will be automatically truncated when displayed. Note that slot labels can be defined ahead of time even when no `.VMU` file exists for it yet.

When "Serial VMU Multi-Slot" is off, slot 1 is used silently with no prompt.

For backup operations (returning to openMenu after playing a game), the slot selector pre-positions the cursor on the slot that was used when the game was last launched.

Note that the Serial VMU and Game ID (see the [Virtual VMU Device Support](#virtual-vmu-device-support) section) features are mutually exclusive, and enabling one will disable the other.

#### Migration Tutorial

openMenu's Serial VMU feature makes the process of migrating from several physical VMUs simple.

This guide walks through how to copy save files from physical VMUs onto the serial SD card using the Serial VMU feature. The basic idea is to use the Dreamcast BIOS as a middleman to copy saves from physical VMUs onto a single VMU that a user dedicates as their Serial VMU device.

**What's Needed**

- openMenu settings must be saved to serial SD card (not VMU).
- A dedicated VMU to be used for everyday Serial VMU backups/restores
- One or more additional VMUs containing save data to migrate
- A serial SD adapter and FAT32 formatted SD card

**Steps**

1. Plug in both VMUs. The dedicated VMU should be in the port/socket configured in openMenu's "Serial VMU" setting (see the [Settings Menu](#settings-menu) section). The VMU with the save data to migrate should be in a different port or socket.

2. In openMenu, highlight the game whose save data is to be migrated.

3. Press the button mapped to "Exit to BIOS". When prompted, select the **Create/Restore Serial VMU + Exit to BIOS** option. This allows an easy return to openMenu afterward by choosing **Play** from the BIOS.

4. As this will be the first time the game has been launched with the Serial VMU feature activated, a prompt will appear asking how to initialize the dedicated VMU. Choose "Start fresh and format VMU" unless there's a specific reason to leave the contents of this dedicated VMU intact.

   If it's not the first time the game has been launched with the Serial VMU feature activated, allow openMenu to automatically load the serial SD card's Serial VMU, or choose the desired slot if the "Serial VMU Multi-Slot" setting is turned on.

5. The Dreamcast BIOS will load. From here, choose **File** (the VMU icon on the main screen). Both the VMUs should be listed in their respective ports/sockets.

6. Copy the save data from the secondary VMU to the dedicated VMU. To do so, open the secondary VMU and locate the desired save file. Once selected, press the A button, then choose "Copy" from the menu. For the target VMU, select the VMU dedicated to everyday Serial VMU backups/restores. Repeat this process for games that use more than one save file.

7. Once the save data copying process is complete, return to openMenu by either choosing **Play** from the BIOS menu or power-cycling the console.

8. In openMenu, the backup prompt will appear. openMenu remembers that the previous session exited to BIOS with this game's Serial VMU active. It will detect the save data now on the dedicated VMU and back it up to a Serial VMU on the serial SD card. One of two things will happen depending on whether the "Serial VMU Multi-Slot" setting is turned on or off:
   - **On:** The user will be prompted to select one of five Serial VMU slots in which to store the backed up VMU contents.
   - **Off:** The dedicated VMU will automatically be backed up to the default Serial VMU slot (Slot 1).

At this point, save data migration is complete. In the future whenever this game is launched, its Serial VMU will either be automatically restored, or a prompt will appear asking the user to choose the Serial VMU slot from which to restore (dictated by the "Serial VMU Multi-Slot" setting).

To perform a save file migration for another game, simply repeat the above steps.

### Save/Load Manager

openMenu includes a Save/Load Manager for managing VMU configuration save files, including support for saving/loading settings to/from the serial SD adapter.

Access it from the Settings menu by selecting "Save/Load".

Note that when openMenu launches, it first scans for a serial SD card with valid openMenu settings saved to it (i.e., an `OPENMENU\OPENMENU.CFG` file). If found, those settings are automatically loaded and no VMU save file scan is performed. If a serial SD card with valid settings is not found, openMenu will automatically load saved settings from the first VMU that it finds (starting with Port A, Socket 1). If no valid settings are found, openMenu's default settings are used instead.

**Features:**

- **Live VMU Detection**: The manager scans all four controller ports and both VMU sockets, displaying detected devices in real time. Device types (standard VMU, VM2, VMUPro, USB4MAPLE, and Pico2Maple) are identified and shown.

- **Save Status Display**: Each detected VMU shows its current save status:
  - **(no save)**: No openMenu save file exists on this VMU.
  - **(saved)**: A current-version save file exists.
  - **(loaded)**: This VMU's save was loaded at startup.
  - **(old vX)**: An older version save file exists (will be upgraded on load).
  - **(invalid)**: A corrupted or unrecognized save file exists.
  - **(full)**: The VMU doesn't have enough free space for a save file.
  - **(future)**: The save file is from a newer version of openMenu.

- **Save to Selected**: Writes the current openMenu settings to the selected VMU.

- **Load from Selected**: Loads settings from the selected VMU. If the save file is from an older version, it will be automatically upgraded to the current format.

- **Automatic Upgrade**: When loading an old save file, openMenu automatically upgrades it to the current format and offers to save the upgraded version back to the VMU.

**Navigation:**
- Use D-Pad Up/Down to navigate between VMU slots and actions.
- Press A to select a VMU or execute an action.
- Press B to close the manager.

### PlayStation Emulator Feature

openMenu features a built-in version of the Bleemcast! emulator for launching PlayStation discs. While this emulator's performance and compatibility aren't perfect, it will typically be capable of at least booting most retail PlayStation discs as well as specially formatted self-booting Bleemcast! discs.

The following custom button combinations have been added to Bleemcast!:
- **Restart emulator:** `A + B + X + Y + D-pad Down`
- **Exit to menu**: `A + B + X + Y + Start`

Support also exists for the work-in-progress [Bloom](https://github.com/pcercuei/bloom) emulator. Users can compile Bloom from source and then place `BLOOM.BIN` in the `tools\openMenu\menu_data` folder, after which launching a PlayStation disc will bring up a menu allowing for the selection between Bleemcast! and Bloom.

It's important to note that Bloom is early in its development, and while it impressively places heavy emphasis on accuracy and compatibility, most games are not currently in a playable state.

### Multi-Disc Launcher

openMenu detects multi-disc sets by finding two or more entries with the same Serial and matching disc number/count values (e.g., `1/2`, `2/2`). It's important to note that in order for openMenu to correctly identify that multiple discs are part of a single set, **they must share the same unique Serial value** when the SD card was authored. See more in the [Multi-Disc Games](#multi-disc-games) section.

If the Multi-Disc setting is on "Compact", multiple discs part of the same entry will only appear once in the menu and users will be provided with a launcher menu to decide which disc to boot. There's an additional degree of control over how multi-disc entries are treated, the details of which can be read about in the [Folders Mode Settings](#folders-mode-settings) section.

### Exiting to BIOS

This build of openMenu has enhanced "Exit to BIOS" options which are dynamically displayed based on the disc image type and the presence of a virtual VMU device like VM2, VMUPro, USB4MAPLE, and Pico2Maple.

Furthermore, there are additional "Exit to BIOS" settings users can explore (see more in the [Available Options by Context](#available-options-by-context) section).

Note that region and VGA patching is not automatically applied when launching games from the BIOS. Instead, use [Disc Image Options](#disc-image-options) in GD MENU Card Manager to apply these patches directly to disc images.

#### Available Options by Context

| Context | Available Options |
|---------|-------------------|
| **Folder selected** | Exit to BIOS, Close |
| **Dreamcast/PlayStation disc** *(no virtual VMU)* | Mount disc + Exit to BIOS, Exit to BIOS, Close |
| **Dreamcast/PlayStation** *(with virtual VMU)* | Send Game ID + Mount disc + Exit to BIOS, Send Game ID + Exit to BIOS, Mount disc + Exit to BIOS, Exit to BIOS, Close |
| **Other disc types** *(e.g., audio CDs)* | Mount disc + Exit to BIOS, Exit to BIOS, Close |

#### Options Explanation

| Option | Mounts Disc | Sends Game ID | Exits to BIOS |
|--------|:-----------:|:-------------:|:-------------:|
| Send Game ID + Mount disc + Exit to BIOS | ✓ | ✓ | ✓ |
| Send Game ID + Exit to BIOS | | ✓ | ✓ |
| Mount disc + Exit to BIOS | ✓ | | ✓ |
| Exit to BIOS | | | ✓ |
| Close | | | |

**Note:** "Send Game ID" options only appear when a virtual VMU device (VM2, VMUPro, USB4MAPLE, or Pico2Maple) is detected and the selected item is a Dreamcast or PlayStation disc.

### Settings Menu

The Settings menu is accessed differently depending on the current view mode:
- **Grid/LineDesc**: Press Start button
- **Scroll**: Press X button
- **Folders**: Press Start button

The Settings menu is scrollable in all view modes, so when the list of settings exceeds the visible window, scrolling the cursor past the edge reveals the rest.

Each view mode has its own set of available settings. Below is a breakdown of each.

#### Grid/LineDesc Mode Settings

| Setting | Options | Description |
|---------|---------|-------------|
| **Style** | LineDesc, Grid3, Scroll, Folders | Changes the visual layout/view mode of openMenu. |
| **Theme** | NTSC-U, NTSC-J, PAL, Custom... | Selects the color scheme and background artwork. Custom themes appear if present. |
| **Play BGM** | Off, On | Plays background music added with Card Manager's **Menu Options** window (see the [Menu Options](#menu-options) section). Only shown when a music track is present. |
| **Honor Menu Defaults** | Off, On | When a default style and theme has been set in Card Manager's **Menu Options** window, "Off" makes openMenu use the console's saved style and theme instead. Only shown when a default style and theme has been applied. |
| **Aspect** | 4:3, 16:9 | Adjusts background artwork for standard or widescreen displays. |
| **Exit to BIOS** | Standard, Alternate, Alternate + 3D | "Standard" uses a standard method when exiting to BIOS, whereas "Alternate" uses a custom method that removes save file copy protection in the BIOS save manager. "Alternate + 3D" does the same while also enabling the "hidden" 3D BIOS feature. If users experience a freeze when exiting to BIOS with either "Alternate" setting, it's advised they use the "Standard" setting instead. |
| **Sort** | Alphabetical, Name, Region, Genre, SD Card Order | Determines how the game list is sorted. "SD Card Order" matches physical folder order on SD card. |
| **Filter** | All, Action, Racing, Simulation, Sports, Lightgun, Fighting, Shooter, Survival, Adventure, Platformer, RPG, Shmup, Strategy, Puzzle, Arcade, Music | Filters the game list to show only titles matching the selected genre. |
| **Multi-Disc** | Show All, Compact | "Show All" displays every disc individually. "Compact" groups multi-disc games into a single entry. |
| **VMU Time Sync** | Off, On | Use VMU date and time to set console date and time when openMenu boots (VM2 and VMUPro must be using a supported firmware). |
| **VMU Game ID** | Send to All, Send to First, Off | If virtual VMU device like VM2, VMUPro, USB4MAPLE, or Pico2Maple detected, send Game ID to the first one detected (starting at the first controller port and first VMU socket), all detected, or turn off ID transmission altogether (see the [Virtual VMU Device Support](#virtual-vmu-device-support) section) |
| **Serial VMU** | Off, On (A1), On (A2), On (B1), On (B2), On (C1), On (C2), On (D1), On (D2) | If serial SD card detected, use it to store per-game virtual VMUs in the specified Port and Socket. |
| **Serial VMU Multi-Slot** | Off, On | If Serial VMU is enabled, also enable up to five per-game slots, or leave off to default to one virtual VMU per game. |
| **Boot Mode** | Full Boot, License Only, Animation Only, Fast Boot | Controls how the game is launched. "Full Boot" displays the complete boot sequence, "License Only" shows only the SEGA license screen, "Animation Only" plays only the Dreamcast boot animation, and "Fast Boot" skips all startup screens and launches the game immediately. Some titles may not function correctly in modes other than "Full Boot". DreamConn S users are generally advised to use "Full Boot" to ensure VMU synchronization completes before the game starts, which is required for certain games' automatic load features. |
| **VMU Beep on Save** | Off, On | Sounds the VMU's buzzer while openMenu writes its settings save file. |
| **Save/Load** | N/A | Opens the [Save/Load Manager](#saveload-manager). |
| **Apply** | N/A | Applies settings for current session only. |

#### Scroll Mode Settings

| Setting | Options | Description |
|---------|---------|-------------|
| **Style** | LineDesc, Grid3, Scroll, Folders | Changes the visual layout/view mode of openMenu. |
| **Theme** | GDMENU, Custom... | Selects the theme. Custom themes appear if `SCROLL_X` folders exist. |
| **Play BGM** | Off, On | Plays background music added with Card Manager's **Menu Options** window (see the [Menu Options](#menu-options) section). Only shown when a music track is present. |
| **Honor Menu Defaults** | Off, On | When a default style and theme has been set in Card Manager's **Menu Options** window, "Off" makes openMenu use the console's saved style and theme instead. Only shown when a default style and theme has been applied. |
| **Exit to BIOS** | Standard, Alternate, Alternate + 3D | "Standard" uses a standard method when exiting to BIOS, whereas "Alternate" uses a custom method that removes save file copy protection in the BIOS save manager. "Alternate + 3D" does the same while also enabling the "hidden" 3D BIOS feature. If users experience a freeze when exiting to BIOS with either "Alternate" setting, it's advised they use the "Standard" setting instead. |
| **Sort** | Alphabetical, Name, Region, Genre, SD Card Order | Determines how the game list is sorted. "SD Card Order" matches physical folder order on SD card. |
| **Filter** | All, Action, Racing, Simulation, Sports, Lightgun, Fighting, Shooter, Survival, Adventure, Platformer, RPG, Shmup, Strategy, Puzzle, Arcade, Music | Filters the game list to show only titles matching the selected genre. |
| **Multi-Disc** | Show All, Compact | "Show All" displays every disc individually. "Compact" groups multi-disc games into a single entry. |
| **Artwork** | Off, On | Toggles display of box art/cover artwork. |
| **Display Index Numbers** | Off, On | Shows or hides the index number next to each game entry. |
| **Disc Details** | Show, Hide | Toggles display of disc information (region, VGA support, etc.) alongside game entries. |
| **Marquee Speed** | Slow, Medium, Fast | Controls how quickly long game names scroll horizontally. |
| **VMU Time Sync** | Off, On | Use VMU date and time to set console date and time when openMenu boots (VM2 and VMUPro must be using a supported firmware). |
| **VMU Game ID** | Send to All, Send to First, Off | If virtual VMU device like VM2, VMUPro, USB4MAPLE, or Pico2Maple detected, send Game ID to the first one detected (starting at the first controller port and first VMU socket), all detected, or turn off ID transmission altogether (see the [Virtual VMU Device Support](#virtual-vmu-device-support) section) |
| **Serial VMU** | Off, On (A1), On (A2), On (B1), On (B2), On (C1), On (C2), On (D1), On (D2) | If serial SD card detected, use it to store per-game virtual VMUs in the specified Port and Socket. |
| **Serial VMU Multi-Slot** | Off, On | If Serial VMU is enabled, also enable up to five per-game slots, or leave off to default to one virtual VMU per game. |
| **Boot Mode** | Full Boot, License Only, Animation Only, Fast Boot | Controls how the game is launched. "Full Boot" displays the complete boot sequence, "License Only" shows only the SEGA license screen, "Animation Only" plays only the Dreamcast boot animation, and "Fast Boot" skips all startup screens and launches the game immediately. Some titles may not function correctly in modes other than "Full Boot". DreamConn S users are generally advised to use "Full Boot" to ensure VMU synchronization completes before the game starts, which is required for certain games' automatic load features. |
| **VMU Beep on Save** | Off, On | Sounds the VMU's buzzer while openMenu writes its settings save file. |
| **Save/Load** | N/A | Opens the [Save/Load Manager](#saveload-manager). |
| **Apply** | N/A | Applies settings for current session only. |

#### Folders Mode Settings

| Setting | Options | Description |
|---------|---------|-------------|
| **Style** | LineDesc, Grid3, Scroll, Folders | Changes the visual layout/view mode of openMenu. |
| **Theme** | FoldersDefault, Custom... | Selects the theme. Custom themes appear if `FOLDERS_X` folders exist. |
| **Play BGM** | Off, On | Plays background music added with Card Manager's **Menu Options** window (see the [Menu Options](#menu-options) section). Only shown when a music track is present. |
| **Honor Menu Defaults** | Off, On | When a default style and theme has been set in Card Manager's **Menu Options** window, "Off" makes openMenu use the console's saved style and theme instead. Only shown when a default style and theme has been applied. |
| **Exit to BIOS** | Standard, Alternate, Alternate + 3D | "Standard" uses a standard method when exiting to BIOS, whereas "Alternate" uses a custom method that removes save file copy protection in the BIOS save manager. "Alternate + 3D" does the same while also enabling the "hidden" 3D BIOS feature. If users experience a freeze when exiting to BIOS with either "Alternate" setting, it's advised they use the "Standard" setting instead. |
| **Sort** | Alphabetical, SD Card Order | Determines how the game list is sorted. "SD Card Order" uses a "seen first" routine that organizes disc images, virtual folders, and subfolders based on their SD card sequence. |
| **Multi-Disc** | Show All, Compact | "Show All" displays every disc individually. "Compact" groups multi-disc games into a single entry. |
| **Multi-Disc Grouping** | Anywhere, Same Folder Only | Only enabled if Multi-Disc option is set to "Compact". "Anywhere" will show all associated discs on SD card when opening multi-disc launcher. "Same Folder Only" will treat discs as part of their own multi-disc (or single disc) set when organized in different folders. |
| **Game Artwork** | Off, On | Toggles display of box art/cover artwork for games. |
| **Folder Artwork** | Off, On | Toggles display of artwork assigned to folders (see the [Folder Artwork Assignment](#folder-artwork-assignment) section) when the cursor hovers over a folder. |
| **Item Details** | Off, On | Toggles display of the item details read-out below the artwork. |
| **Clock** | On (12-Hour), On (24-Hour), Off | Toggles display of the BIOS clock. |
| **Marquee Speed** | Slow, Medium, Fast | Controls how quickly long game names scroll horizontally. |
| **VMU Time Sync** | Off, On | Use VMU date and time to set console date and time when openMenu boots (VM2 and VMUPro must be using a supported firmware). |
| **VMU Game ID** | Send to All, Send to First, Off | If virtual VMU device like VM2, VMUPro, USB4MAPLE, or Pico2Maple detected, send Game ID to the first one detected (starting at the first controller port and first VMU socket), all detected, or turn off ID transmission altogether (see the [Virtual VMU Device Support](#virtual-vmu-device-support) section) |
| **Serial VMU** | Off, On (A1), On (A2), On (B1), On (B2), On (C1), On (C2), On (D1), On (D2) | If serial SD card detected, use it to store per-game virtual VMUs in the specified Port and Socket. |
| **Serial VMU Multi-Slot** | Off, On | If Serial VMU is enabled, also enable up to five per-game slots, or leave off to default to one virtual VMU per game. |
| **Boot Mode** | Full Boot, License Only, Animation Only, Fast Boot | Controls how the game is launched. "Full Boot" displays the complete boot sequence, "License Only" shows only the SEGA license screen, "Animation Only" plays only the Dreamcast boot animation, and "Fast Boot" skips all startup screens and launches the game immediately. Some titles may not function correctly in modes other than "Full Boot". DreamConn S users are generally advised to use "Full Boot" to ensure VMU synchronization completes before the game starts, which is required for certain games' automatic load features. |
| **VMU Beep on Save** | Off, On | Sounds the VMU's buzzer while openMenu writes its settings save file. |
| **Save/Load** | N/A | Opens the [Save/Load Manager](#saveload-manager). |
| **Apply** | N/A | Applies settings for current session only. |

### Button Mapping

#### Grid/LineDesc Mode

| Button | Function |
|--------|----------|
| D-Pad | Navigate game list |
| A | Launch selected game |
| B | Run CodeBreaker (if present) |
| X | Show full-size boxart (hold) |
| Y | Exit to BIOS menu |
| Start | Open Settings |
| L/R Triggers | Jump to previous/next letter *(LineDesc only)* |

**Note:** In LineDesc mode, Up/Down navigate by half a page at a time, while Left/Right move one item at a time.

#### Scroll Mode

| Button | Function |
|--------|----------|
| D-Pad Up/Down | Navigate by 1 item |
| D-Pad Left/Right | Navigate by 5 items |
| L/R Triggers | Navigate by 5 items |
| A | Launch selected game |
| B | Run CodeBreaker |
| X | Open Settings |
| Y | Exit to BIOS menu |

#### Folders Mode

| Button | Function |
|--------|----------|
| D-Pad Up/Down | Navigate by 1 item |
| D-Pad Left/Right | Navigate by 5 items |
| L/R Triggers | Navigate by 5 items |
| A | Launch game / Enter folder |
| B | Go back to parent folder |
| X | Run CodeBreaker |
| Y | Exit to BIOS menu |
| Start | Open Settings |

#### Popup Menus (All Modes)

When a popup menu is displayed (Settings, Multi-Disc, Exit to BIOS, etc.):

| Button | Function |
|--------|----------|
| D-Pad Up/Down | Navigate options |
| D-Pad Left/Right | Change setting value *(Settings menu only)* |
| A | Confirm / Select |
| B | Cancel / Close |

### Keyboard Support

Thanks to [Xerxes3rd](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/compare/main...Xerxes3rd:openMenu-Virtual-Folder-Bundle:main), full keyboard support has been added to openMenu. See mapping below.

| Keyboard Key     | Dreamcast Button            |
|------------------|-----------------------------|
| Arrow Keys       | D-Pad (Up/Down/Left/Right) |
| Z or Space       | A button |
| X or Escape      | B button |
| A                | X button |
| S                | Y button |
| Enter            | Start |
| Q or Page Up     | Left Trigger |
| W or Page Down   | Right Trigger |
| Shift + A-Z/0-9  | Jump to next item starting with that character |

---

## Theming

Windows users interested in additional custom themes should see [GDMenu & OpenMenu HD Theme Manager](https://segaxtreme.net/resources/gdmenu-openmenu-hd-theme-manager-all-in-one-package.61/).

### Theme Format

A separate openMenu theme format was designed for the new "Folders" Style mode.

To add a new theme, navigate to the `tools\openMenu\menu_data\theme` folder (Windows and Linux only) and create a folder named `FOLDERS_X`, where `X` is a number between zero and nine. Inside that folder, create a new file named `THEME.INI`.

See example and explanation of parameters below.

```
[THEME]
name=FoldersDefault
text_color=255,255,255
highlight_color=207,62,17
cursor_color=75,75,75
multidisc_color=207,62,17
menu_title_color=0,0,0
menu_text_color=255,255,255
menu_highlight_color=207,62,17
menu_bkg_color=0,0,0
menu_bkg_border_color=255,255,255
list_x=13
list_y=68
list_count=18
list_marquee_threshold=49
artwork_x=416
artwork_y=215
artwork_size=210
item_details_x=521
item_details_y=430
item_details_text_color=0,0,0
clock_x=623
clock_y=36
clock_text_color=255,255,255
```

#### General

- **name**: Theme display name shown in settings menu (e.g., `FoldersDefault`)

#### Color Styling

- **text_color**: Color for normal (non-selected) game names in the list

  _Example_: `255,255,255` (white)

- **highlight_color**: Color for the currently selected game name

  _Example_: `207,62,17` (orange-red)

- **cursor_color**: Background color for the selection cursor rectangle

  _Example_: `75,75,75` (dark gray)

- **multidisc_color**: Color for the multi-disc indicator icon/text

  _Example_: `207,62,17` (orange-red)

- **menu_title_color**: Color for popup titles (`Settings`, `Credits`, `Multi-Disc`, etc.)

  _Example_: `0,0,0` (black)

- **menu_text_color**: Color for text items in popup menus

  _Example_: `255,255,255` (white)

- **menu_highlight_color**: Color for highlighted/selected menu items

  _Example_: `207,62,17` (orange-red)

- **menu_bkg_color**: Background fill color for popup boxes

  _Example_: `0,0,0` (black)

- **menu_bkg_border_color**: Border outline color for popup boxes

  _Example_: `255,255,255` (white)

- **item_details_text_color**: Color for the item details text (region, date, etc.) below artwork

  _Example_: `0,0,0` (black)

#### Game List Positioning and Sizing

- **list_x**: Horizontal position of game list (left edge)

  _Example_: `13` (12 pixels from left screen edge)

- **list_y**: Vertical position of game list (top of first item)

  _Example_: `68` (68 pixels from top screen edge)

- **list_count**: Number of game entries visible per page

  _Example_: `18` (18 items displayed at once)

- **list_marquee_threshold**: Maximum characters before name scrolls horizontally

  _Example_: `49` (names longer than 49 characters will marquee scroll)

#### Artwork Positioning and Sizing

- **artwork_x**: Horizontal position of box art/artwork (left edge)

  _Example_: `416` (416 pixels from left screen edge)

- **artwork_y**: Vertical position of box art/artwork (top edge)

  _Example_: `215` (215 pixels from top screen edge)

- **artwork_size**: Width and height of artwork square in pixels

  _Example_: `210` (210x210 pixel square)

#### Item Details Positioning

- **item_details_x**: Horizontal center point for item details text below artwork

  _Example_: `521` (text centered around 521 pixels from left)

- **item_details_y**: Vertical position of item details text

  _Example_: `430` (430 pixels from top screen edge)

#### Optional Clock Positioning and Color Styling

- **clock_x**: Horizontal position of the clock's right edge (right-justified)

  _Example_: `623` (clock text ends at 623 pixels from left)

- **clock_y**: Vertical position of clock text

  _Example_: `36` (36 pixels from top screen edge)

- **clock_text_color**: RGB color for clock text

  _Example_: `255,255,255` (white text)

**Note**: All position values are absolute pixel coordinates for 640x480 resolution. If a positioning key is set to `0` or omitted, Folders mode uses built-in fallback defaults. The cursor width is calculated automatically based on `list_marquee_threshold`.

After making desired changes to `THEME.INI`, use a preferred image editor to create two PNGs: one with a resolution of 512x512px named `BG_L.PNG`, the other with a resolution of 128x512 pixels named `BG_R.PNG`. Templates for these can be found in the `tools\openMenu\menu_data\theme\FOLDERS` folder, and the [included templates](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/raw/refs/heads/main/theme_template/) for use with [The GIMP](https://www.gimp.org/) may prove useful to some users.

Split a 640x480px background image across these two PNGs, noting that the bottom row of 32 pixels will be ignored.

![theme_1.png](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/blob/main/images/theme_1.png?raw=true)

Next, open each PNG in [PVR Tool](https://www.romhacking.net/utilities/1463/).

For `BG_L.PNG`, navigate to **Tools → Set Global Index** and use the value `1025`. Next, navigate to **File → Save As**, using `565` for **Format**, `no palette` for **Palette**, only `Twiddle` for **Options**, and `BG_L.PVR` as the destination file name.

For `BG_R.PNG`, navigate to **Tools → Set Global Index** and use the value `1026`. Next, Navigate to **File → Save As**, using `565` for **Format**, `no palette` for **Palette**, and `BG_R.PVR` as the destination file name.

To confirm newly generated PVRs are in the correct format, navigate to **Tools → Properties** and compare the texture settings for `BG_L.PVR` and `BG_R.PVR` match the default theme's textures in the `tools\openMenu\menu_data\theme\FOLDERS` folder.

After that, open GD MENU Card Manager with GDEMU SD card inserted, then click **Save Changes**. The new theme will be accessible from the Settings menu while **Style** is set to `Folders`.

---

## Legal and Licensing

### openMenu
This project includes a modified version of openMenu, which is:

**Copyright © 2021, Hayden Kowalchuk**

Licensed under the Modified BSD License (3-Clause BSD License)

The original openMenu project was created by Hayden Kowalchuk (mrneo240), with subsequent forks and contributions by megavolt85 and sbstnc.

**Modifications made by Derek Pascarella (ateam) in 2026:**
- See the [Changes Since openMenu v1.3.0](#changes-since-openmenu-v130) and [Changelog](#changelog) sections.

For the full openMenu license text, see `/openMenu/LICENSE.md`.

### GD MENU Card Manager
This project includes a modified version of GD MENU Card Manager by Sonik:

**Original work**: Copyright (C) 2021, Sonik

**Original repository**: https://github.com/sonik-br/GDMENUCardManager

Licensed under the GNU General Public License v3.0 (GPL-3.0)

**Modifications made by Derek Pascarella (ateam) in 2026:**
- See the [Changes Since GD MENU Card Manager v2.0.1](#changes-since-gd-menu-card-manager-v201) and [Changelog](#changelog) sections.

This modified version is also licensed under GPL-3.0. For the full license text, see `/GD MENU Card Manager/LICENSE`.

**Source Code Availability**: Per GPL-3.0 requirements, the complete source code for GD MENU Card Manager is included in this distribution at `/GD MENU Card Manager/src/`.

### Third-Party Components
Both projects include additional third-party components with their own licenses:
- **Aaru Data Preservation Suite (LGPL v2.1)** - Copyright © 2011–2021 Natalia Portillo, provides disc image parsing and CD sector decoding (see `/GD MENU Card Manager/src/GDMENUCardManager.Core/Aaru/`)
- **DiscUtils (MIT)** - Copyright © 2008–2011 Kenneth Bell (see `/GD MENU Card Manager/src/GDMENUCardManager.Core/DiscUtils/DiscUtils-LICENSE.txt`)
- **SevenZipSharp (LGPL v3)** - Copyright © 2009–2010 Markovtsev Vadim, managed .NET wrapper for 7-Zip, used by the Windows version (see `/GD MENU Card Manager/src/GDMENUCardManager/SevenZip/`)
- **7-Zip native library (LGPL v2.1+)** - Copyright © Igor Pavlov, archive extraction, used by the Windows version
- **SharpCompress (MIT)** - Copyright © Adam Hathcock, archive extraction, used by the Linux/macOS version
- **libchdr (BSD 3-Clause)** - native CHD disc image library from the MAME project (see `/GD MENU Card Manager/src/GDMENUCardManager.Core/runtimes/`)
- **gdidrop (BSD 2-Clause)** - Copyright © 2019 Feyris-Tan, code used for Redump GD-ROM CUE/BIN to GDI conversion (see `/GD MENU Card Manager/src/GDMENUCardManager.Core/gdidrop-LICENSE.txt`)
- **Avalonia (MIT)** - cross-platform .NET UI framework
- **MessageBox.Avalonia (MIT)**
- **SharpCompress (MIT)** - archive extraction used by the cross-platform version
- **SixLabors.ImageSharp (Six Labors Split License v1.0)** - free for use in OSI-approved open-source projects
- **ClosedXML (MIT)**
- **ByteSize (MIT)**
- **NLayer (MIT)** - Copyright © Mark Heath and Andrew Ward, MP3 decoding for the background music feature
- **NVorbis (MIT)** - Copyright © Andrew Ward, OGG Vorbis decoding for the background music feature
- **CUETools.Codecs / CUETools.Codecs.FLAKE (LGPL v3)** - FLAC decoding for the background music feature
- **gong-wpf-dragdrop (BSD 2-Clause)** - Windows version only
- Various libraries in `/openMenu/external/` with their respective licenses

All original copyright notices and licenses for these components have been preserved.