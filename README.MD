# openMenu Virtual Folder Bundle

This is a new version of openMenu with virtual folder/subfolder support and other updates and fixes, including a modified version of GD MENU Card Manager to support it. It aims to be the ultimate menu software and SD card management solution for SEGA Dreamcast [GDEMU](https://gdemu.wordpress.com/about/) users.

openMenu was originally written by [mrneo240](https://github.com/mrneo240/openmenu/), later forked by [megavolt85](https://github.com/megavolt85/openmenu), and then finally [sbstnc](https://github.com/sbstnc/openmenu/). With each iteration, important updates have been made to this open source menu software for the GDEMU, a SEGA Dreamcast optical drive emulator.

However due to the fragmented nature of these forks and the lack of time for each of their respective owners to contribute to the them, the author of this new openMenu build has decided to create a brand-new fork, rather than contribute to any of the existing ones. As an active member of the Dreamcast community, this author will work hard to maintain, update, and fix this new version of openMenu.

For those unfamiliar with the branching history of openMenu, it's important to note that **openMenu Virtual Folder Bundle** was built on top of the latest public version of openMenu, meaning it has full support for per-game virtual VMUs with [VM2](https://www.dreammods.net/store/p6/VM2.html), [VMUPro](https://stoneagegamer.com/vmu-pro-handheld-visual-memory-card-for-sega-dreamcast.html), and [USB4MAPLE](http://usb4maple.wikidot.com/).

To accommodate the new openMenu, a modified version of [GD MENU Card Manager](https://github.com/sonik-br/GDMENUCardManager) has also been developed and included in this bundle.

## Table of Contents

- [Table of Contents](#table-of-contents)
- [Current Version](#current-version)
- [Changelog](#changelog)
- [Credits](#credits)
- [Changes to openMenu and GD MENU Card Manager](#changes-to-openmenu-and-gd-menu-card-manager)
  - [Changes Since openMenu v1.3.0](#changes-since-openmenu-v130)
  - [Changes Since GD MENU Card Manager v2.0.1](#changes-since-gd-menu-card-manager-v201)
- [Usage Tutorial](#usage-tutorial)
- [Restoring DAT Files](#restoring-dat-files)
- [Theme Format](#theme-format)

## Current Version

The latest version of **openMenu Virtual Folder Bundle** is [1.0](xxxx).

## Changelog

- **Version 1.0 (2025-01-07)**
  * Initial release.

## Credits

- **Programming**
  * Derek Pascarella (ateam)
- **Testing**
  * NO HXRO
  * SEGA RPG FAN
  * Kanji
- **Special Thanks**
  * mrneo240 for his original work on openMenu
  * megavolt85 for forking openMenu and adding new features
  * sbstnc for forking openMenu and adding a Docker development container
  * Sonik for his work on GD MENU Card Manager
  * All of the wonderful people in the Dreamcast scene

## Changes to openMenu and GD MENU Card Manager

Many components of both openMenu and GD MENU Card Manager have been drastically overhauled for this version. See below for a list of changes.

### Changes Since openMenu v1.3.0

- New "Folders" Style mode, plus custom themes:
  * Virtual folder support included eight levels of nested subfolders.
  * Complete subfolder path maximum length is 256 characters, including backslashes (e.g., `Games\JP\RPG`).
  * L Trigger + R Trigger is now a shortcut to go back one directory
- Long entry names now supported (256 characters), along with marquee scrolling over those exceeding 49 characters.
- Scroll mode improvements:
  * Jumping cursor fixed
  * New setting to toggle index numbers in the game list (On/Off)
  * New setting to toggle artwork display (On/Off)
- Settings menu now has wrap-around cursor scrolling.
- GDEMU firmware version and openMenu build version now shown in the Settings menu.
- VMU configuration save file is no longer written on every openMenu startup, now only when saving settings or during save file migration.
- Non-game discs are mounted by GDEMU instantly, returning user to BIOS for things like playing audio CDs.
- Game ID is not sent to VM2/VMUPro/USB4MAPLE when a non-game disc is launched (e.g., an audio CD).
- VMU beep setting disabled (feature is not implemented yet).
- Credits updated to include additions from this author.

### Changes Since GD MENU Card Manager v2.0.1

- Game list column widths and window width adjusted for better visibility.
- Version check in About window removed (with possibility of returning in the future).
- A new "Folder" column added when building menu in openMenu mode, allowing users to specify virtual folder path for a disc image (e.g., `Games\RPGs\NTSC-U`).
- Non-game disc images now supported, with a new column named "Type" added where users can select from two drop-down options: `Game` and `Other`. Note that non-game disc images are automatically assigned the `Other` value, as openMenu will check this property to decide how to initialize the GDEMU.
- Disc image names no longer limited to 39 characters, increased to 256 characters.
- Disc numbers can now be customized by the user (e.g., `1/3`, `2/4`).
- Maximum length for game IDs is now ten (instead of 9), but is customizable by changing the `ProductIdMaxLength` key inside `GDMENUCardManager.dll.config`.
- Latest version of [CodeBreaker cheats database](https://www.dreamcast-talk.com/forum/viewtopic.php?t=17078) included by default.

## Usage Tutorial

A full video tutorial has been provided on how to use **openMenu Virtual Folder Bundle**.

<a href="https://youtu.be/YZtgtykCnM4"><img width="400" src="https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/blob/main/images/thumbnail.png?raw=true"></a>

## Restoring DAT Files

New users of **openMenu Virtual Folder Bundle** may wish to use their existing DAT files containing artwork and metadata. To do so, navigate to the `tools\openMenu\menu_data` folder in the original GD MENU Card Manager application folder and copy `BOX.DAT`, `ICON.DAT`, and `META.DAT` to the `tools\openMenu\menu_data` folder inside the **openMenu Virtual Folder Bundle**'s GD MENU Card Manager application folder, overwriting the defaults.

## Theme Format

A separate openMenu theme format was designed for the new "Folders" Style mode.

To add a new theme, navigate to the `tools\openMenu\menu_data\theme` folder and create a folder named `FOLDERS_X`, where `X` is a number between zero and nine. Inside that folder, create a new file named `THEME.INI`.

See example and explanation of parameters below.

```
[THEME]
name=FolderDefault
text_color=255,255,255
highlight_color=207,62,17
cursor_color=75,75,75
multidisc_color=207,62,17
menu_title_color=0,0,0
menu_text_color=255,255,255
menu_highlight_color=207,62,17
menu_bkg_color=0,0,0
menu_bkg_border_color=255,255,255
list_x=11
list_y=72
artwork_x=418
artwork_y=223
artwork_size=210
```

- **name:** Theme display name shown in settings menu (e.g., `MyTheme`)
- **text_color**: Color for normal (non-selected) game names in the list  
  _Example_: `255,255,255` (white)
- **highlight_color**: Color for the currently selected game name  
  _Example_: `207,62,17` (orange-red)
- **cursor_color**: Background color for the selection cursor rectangle  
  _Example_: `75,75,75` (dark gray)
- **multidisc_color**: Color for the multidisc indicator icon/text  
  _Example_: `207,62,17` (orange-red)
- **menu_title_color**: Color for popup titles (`Settings`, `Credits`, `Multidisc`, etc.)  
  _Example_: `0,0,0` (black)
- **menu_text_color**: Color for text items in popup menus  
  _Example_: `255,255,255` (white)
- **menu_highlight_color**: Color for highlighted/selected menu items  
  _Example_: `207,62,17` (orange-red)
- **menu_bkg_color**: Background fill color for popup boxes  
  _Example_: `0,0,0` (black)
- **menu_bkg_border_color**: Border outline color for popup boxes  
  _Example_: `255,255,255` (white)
- **list_x**: Horizontal position of game list (left edge)  
  _Example_: `11` (11 pixels from left screen edge)
- **list_y**: Vertical position of game list (top of first item)  
  _Example_: `72` (72 pixels from top screen edge)
- **artwork_x**: Horizontal position of box art/artwork (left edge)  
  _Example_: `418` (418 pixels from left screen edge)
- **artwork_y**: Vertical position of box art/artwork (top edge)  
  _Example_: `223` (223 pixels from top screen edge)
- **artwork_size**: Width and height of artwork square in pixels  
  _Example_: `210` (210x210 pixel square)

**Note**: All position values are absolute pixel coordinates for 640x480 resolution. If a positioning key is set to `0` or omitted, Folders mode uses built-in fallback defaults.

After making desited changes to `THEME.INI`, use a preferred image editor to create two PNGs: one with a resolution of 512x512px named `BG_L.PNG`, the other with a resolution of 128x512 pixels named `BG_R.PNG`. Templates for these can be found in the `tools\openMenu\menu_data\theme\FOLDERS` folder.

Split a 640x480px background image across these two PNGs, noting that the bottom row of 32 pixels will be ignored.

![theme_1.png](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle/blob/main/images/theme_1.png?raw=true)

Next, open each PNG in [PVR Tool](https://www.romhacking.net/utilities/1463/).

For `BG_L.PNG`, navigate to **Tools → Set Global Index** and use the value `1025`. Next, Navigate to **File → Save** As, using `565` for **Format**, `no palette` for **Palette**, only `Twiddle` for **Options**, and `BG_L.PVR` as the destination file name.

For `BG_R.PNG`, navigate to **Tools → Set Global Index** and use the value `1026`. Next, Navigate to **File → Save As**, using `565` for **Format**, `no palette` for **Palette**, and `BG_R.PVR` as the destination file name.

To confirm newly generated PVRs are in the correct format, navigate to **Tools → Properties** and compare the texture settings for `BG_L.PVR` and `BG_R.PVR` match the default theme's textures in the `tools\openMenu\menu_data\theme\FOLDERS` folder.

After that, open GD MENU Card Manager with GDEMU SD card inserted, then click **Save Changes**. The new theme will be accessible from the Settings menu while **Style** is set to `Folders`.
