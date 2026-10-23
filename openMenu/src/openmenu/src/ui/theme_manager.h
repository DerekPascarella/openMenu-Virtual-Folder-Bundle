/*
 * File: theme_manager.h
 * Project: ui
 * File Created: Tuesday, 27th July 2021 12:09:25 pm
 * Author: Hayden Kowalchuk
 * -----
 * Copyright (c) 2021 Hayden Kowalchuk, Hayden Kowalchuk
 * License: BSD 3-clause "New" or "Revised" License, http://www.opensource.org/licenses/BSD-3-Clause
 */

#pragma once

#include <stdint.h>
#include <string.h>

#include <openmenu_settings.h>

struct image;

/* Per family cap on CUST_n, SCROLL_n and FOLDERS_n folders, n is 0 to 19 */
#define THEME_SLOTS_MAX (THEME_NUM_END + 1)

typedef struct theme_color {
    uint32_t text_color;
    uint32_t highlight_color;
    uint32_t menu_text_color;
    uint32_t menu_highlight_color;
    uint32_t menu_bkg_color;
    uint32_t menu_bkg_border_color;
    uint32_t icon_color;
} theme_color;

typedef struct theme_region {
    const char* bg_left;
    const char* bg_right;
    theme_color colors;
} theme_region;

typedef struct theme_custom {
    char bg_left[32];
    char bg_right[32];
    char name[20];
    theme_color colors;
} theme_custom;

typedef struct theme_scroll {
    char bg_left[32];
    char bg_right[32];
    char name[20];
    theme_color colors;
    char font[32];
    uint32_t cursor_color;
    uint32_t multidisc_color;
    uint32_t menu_title_color;
    int cursor_width;
    int cursor_height;
    int items_per_page;
    int pos_gameslist_x;
    int pos_gameslist_y;
    int pos_gameinfo_x;
    int pos_gameinfo_region_y;
    int pos_gameinfo_vga_y;
    int pos_gameinfo_disc_y;
    int pos_gameinfo_date_y;
    int pos_gameinfo_version_y;
    int pos_gametxr_x;
    int pos_gametxr_y;
    int list_x;
    int list_y;
    int artwork_x;
    int artwork_y;
    int artwork_size;
    int list_marquee_threshold;
    int item_details_x;
    int item_details_y;
    uint32_t item_details_text_color;
    int clock_x;
    int clock_y;
    uint32_t clock_text_color;
} theme_scroll;

int theme_manager_load(void);
int theme_read(const char* filename, void* theme, int type);
theme_region* theme_get_default(CFG_ASPECT aspect, int* num_themes);
theme_custom* theme_get_custom(int* num_themes);
theme_scroll* theme_get_scroll(int* num_themes);
theme_scroll* theme_get_folder(int* num_themes);
