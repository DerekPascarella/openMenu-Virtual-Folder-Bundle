# openMenu Virtual Folder Bundle

A new version of openMenu with virtual folder/subfolder support and other updates and fixes, including a modified version of GD MENU Card Manager to support it. 

openMenu was originally written by [mrneo240](https://github.com/mrneo240/openmenu/), and then later forked by [megavolt85](https://github.com/megavolt85/openmenu) and finally [sbstnc](https://github.com/sbstnc/openmenu/). With each iteration, important updates have been made to this open source menu software for the SEGA Dreamcast GDEMU ODE. However due to the fragmented nature of these forks and the lack of time for each of their respective owners to contribute to the project, the author of this new openMenu build has decided to create a brand-new fork, rather than contribute to any of the existing ones. As an active member of the Dreamcast community, this author will work hard to maintain, update, and fix this new version of openMenu.

To accommodate the new openMenu, a modified version of [GD MENU Card Manager](https://github.com/sonik-br/GDMENUCardManager) has been developed and included in this bundle.

## Table of Contents

table of contents go here

## Current Version

openMenu Virtual Folder Bundle is currently at at version [1.0](xxxx).

## Changelog

- **Version 1.0 (2025-01-XX)**
  * Initial release.

## Changes to openMenu and GD MENU Card Manager

Many components of both openMenu and GD MENU Card Manager have been drastically overhauled for this version. See below for a list of changes.

### Changes Since openMenu v1.3.0

- New folder view mode, plus custom themes:
  * 8 subfolders max
  * Complete subfolder path maximum length is 256 characters
  * L + R is now a shortcut to go back one directory
- Long entry names now supported, along with marquee scrolling over those exceeding 49 characters.
- Settings menu now has wrap-around cursor scrolling.
- Scroll mode improvements:
  * Toggle index numbers in the game list (On/Off)
  * Toggle artwork display (On/Off)
- GDEMU version is now shown in the settings menu.
- openMenu build now shown in settings menu.
- Credits updated.
- VMU beep setting disabled (feature is not implemented yet).
- Save file is no longer written on every openMenu startup:
  * Now only written when saving settings or during save file migration.
- Non-game discs are mounted by GDEMU instantly, returning user to BIOS for things like playing audio CDs.
- Game ID is not sent to VM2/VMUPro/USB4MAPLE when a non-game disc is launched (e.g., an audio CD).

### Changes Since GD MENU Card Manager v2.0.1

- Game list column widths adjusted for better visibility.
- Version check in About window removed (with possibility of returning in the future).
- A new "Folder" column added when building menu in "openMenu" mode, allowing users to specify virtual folder path for a disc image (e.g., `Games\RPGs\NTSC-U`).
- Non-game disc images now supported, with a new column named "Type" added where users can select from two drop-down options: `Game` and `Other`. Note that non-game disc images are automatically assigned the `Other` value, as openMenu will check this property to decide how to initialize the GDEMU.
- Disc image names no longer limited to 39 characters, increased to 256 characters.
- Disc numbers can now be customized by the user (e.g., `1/3`, `2/4`).

## Usage Tutorial

Usage documentation and tuturial video link goes here.
