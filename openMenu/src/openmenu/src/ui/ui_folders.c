/*
 * File: ui_folders.c
 * Project: openmenu
 * File Created: 2025-12-31
 * Author: Derek Pascarella (ateam)
 * -----
 * Copyright (c) 2025
 * License: BSD 3-clause "New" or "Revised" License, http://www.opensource.org/licenses/BSD-3-Clause
 */

#include <stdbool.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>

#ifdef _arch_dreamcast
#include <arch/rtc.h>
#endif

#include <backend/gd_item.h>
#include <backend/gd_list.h>
#include <openmenu_debug.h>
#include <openmenu_savefile.h>
#include <openmenu_settings.h>
#include "dc/input.h"
#include "texture/txr_manager.h"
#include "ui/draw_prototypes.h"
#include "ui/font_prototypes.h"
#include "ui/theme_manager.h"
#include "ui/ui_common.h"
#include "ui/ui_menu_credits.h"

#include "ui/ui_folders.h"

#define UNUSED         __attribute__((unused))

/* Keyboard scancodes for quick-jump (from KOS keyboard.h) */
#define KBD_KEY_A      0x04
#define KBD_KEY_Z      0x1d
#define KBD_KEY_1      0x1e
#define KBD_KEY_9      0x26
#define KBD_KEY_0      0x27
#define KBD_MOD_LSHIFT 0x02
#define KBD_MOD_RSHIFT 0x20

/* Static resources */
static image txr_bg_left, txr_bg_right;
static image txr_focus;
extern image img_empty_boxart;
extern image img_dir_boxart;

/* Theme management (from Scroll) */
static theme_scroll default_theme = {"THEME/FOLDERS/BG_L.PVR",
                                     "THEME/FOLDERS/BG_R.PVR",
                                     "FoldersDefault",
                                     {COLOR_WHITE,                     /* text_color: 255,255,255 */
                                      PVR_PACK_ARGB(255, 207, 62, 17), /* highlight_color: 207,62,17 */
                                      COLOR_WHITE,                     /* menu_text_color: 255,255,255 */
                                      PVR_PACK_ARGB(255, 207, 62, 17), /* menu_highlight_color: 207,62,17 */
                                      COLOR_BLACK,                     /* menu_bkg_color: 0,0,0 */
                                      COLOR_WHITE,                     /* menu_bkg_border_color: 255,255,255 */
                                      COLOR_WHITE},                    /* icon_color */
                                     "FONT/GDMNUFNT.PVR",
                                     PVR_PACK_ARGB(255, 75, 75, 75),  /* cursor_color: 75,75,75 */
                                     PVR_PACK_ARGB(255, 207, 62, 17), /* multidisc_color: 207,62,17 */
                                     COLOR_BLACK,                     /* menu_title_color: 0,0,0 */
                                     404,                             /* cursor_width (calculated dynamically) */
                                     20,                              /* cursor_height */
                                     18,                              /* items_per_page (list_count) */
                                     3,                               /* pos_gameslist_x */
                                     14,                              /* pos_gameslist_y */
                                     424,                             /* pos_gameinfo_x */
                                     85,                              /* pos_gameinfo_region_y */
                                     109,                             /* pos_gameinfo_vga_y */
                                     133,                             /* pos_gameinfo_disc_y */
                                     157,                             /* pos_gameinfo_date_y */
                                     181,                             /* pos_gameinfo_version_y */
                                     420,                             /* pos_gametxr_x */
                                     213,                             /* pos_gametxr_y */
                                     13,                              /* list_x */
                                     68,                              /* list_y */
                                     416,                             /* artwork_x */
                                     215,                             /* artwork_y */
                                     210,                             /* artwork_size */
                                     49,                              /* list_marquee_threshold */
                                     521,                             /* item_details_x */
                                     430,                             /* item_details_y */
                                     COLOR_BLACK,                     /* item_details_text_color: 0,0,0 */
                                     623,                             /* clock_x */
                                     36,                              /* clock_y */
                                     COLOR_WHITE};                    /* clock_text_color: 255,255,255 */

static theme_scroll* cur_theme = NULL;
static theme_scroll* custom = NULL;

/* List management */
static const gd_item** list_current;
static int list_len;

/* Input state */
#define INPUT_TIMEOUT_INITIAL (18)
#define INPUT_TIMEOUT_REPEAT  (5)

/* Navigation state */
static int current_selected_item = 0;
static int current_starting_index = 0;
static int navigate_timeout = INPUT_TIMEOUT_INITIAL;
static enum draw_state draw_current = DRAW_UI;
static bool serial_vmu_boot_checked = false;

static bool direction_last = false;
static bool direction_current = false;
#define direction_held (direction_last & direction_current)

/* Strobe cursor animation */
static uint8_t cusor_alpha = 255;
static char cusor_step = -5;

/* Marquee scrolling state */
#define MARQUEE_INITIAL_PAUSE_FRAMES 60
#define MARQUEE_END_PAUSE_FRAMES     90

typedef enum {
    MARQUEE_STATE_INITIAL_PAUSE,
    MARQUEE_STATE_SCROLL_LEFT,
    MARQUEE_STATE_END_PAUSE,
    MARQUEE_STATE_SCROLL_RIGHT
} marquee_state_t;

static marquee_state_t marquee_state = MARQUEE_STATE_INITIAL_PAUSE;
static int marquee_offset = 0;
static int marquee_timer = 0;
static int marquee_max_offset = 0;
static int marquee_last_selected = -1;

static inline int
get_marquee_speed_frames(void) {
    extern uint8_t* sf_marquee_speed;
    switch (sf_marquee_speed[0]) {
        case 0: return 8;  /* Slow */
        case 1: return 6;  /* Medium */
        case 2: return 4;  /* Fast */
        default: return 6; /* Default to Medium */
    }
}

/* Display constants */
#define ITEM_SPACING    21
#define CURSOR_HEIGHT   20
#define FONT_CHAR_WIDTH 8
#define X_ADJUST_TEXT   4
#define Y_ADJUST_TEXT   4
#define Y_ADJUST_CRSR   3

/* Helper functions */

static void
draw_bg_layers(void) {
    {
        const dimen_RECT left = {.x = 0, .y = 0, .w = 512, .h = 480};
        draw_draw_sub_image(0, 0, 512, 480, COLOR_WHITE, &txr_bg_left, &left);
    }
    {
        const dimen_RECT right = {.x = 0, .y = 0, .w = 128, .h = 480};
        draw_draw_sub_image(512, 0, 128, 480, COLOR_WHITE, &txr_bg_right, &right);
    }
}

static void
marquee_reset(void) {
    marquee_state = MARQUEE_STATE_INITIAL_PAUSE;
    marquee_offset = 0;
    marquee_timer = MARQUEE_INITIAL_PAUSE_FRAMES;
    marquee_max_offset = 0;
}

static void
marquee_update_animation(int name_length) {
    int max_offset = name_length - cur_theme->list_marquee_threshold;
    if (max_offset < 0) {
        max_offset = 0;
    }

    marquee_max_offset = max_offset;

    if (marquee_timer > 0) {
        marquee_timer--;
        return;
    }

    switch (marquee_state) {
        case MARQUEE_STATE_INITIAL_PAUSE:
            marquee_state = MARQUEE_STATE_SCROLL_LEFT;
            marquee_timer = get_marquee_speed_frames();
            break;

        case MARQUEE_STATE_SCROLL_LEFT:
            marquee_offset++;
            if (marquee_offset >= marquee_max_offset) {
                marquee_offset = marquee_max_offset;
                marquee_state = MARQUEE_STATE_END_PAUSE;
                marquee_timer = MARQUEE_END_PAUSE_FRAMES;
            } else {
                marquee_timer = get_marquee_speed_frames();
            }
            break;

        case MARQUEE_STATE_END_PAUSE:
            marquee_state = MARQUEE_STATE_SCROLL_RIGHT;
            marquee_timer = get_marquee_speed_frames();
            break;

        case MARQUEE_STATE_SCROLL_RIGHT:
            marquee_offset--;
            if (marquee_offset <= 0) {
                marquee_offset = 0;
                marquee_state = MARQUEE_STATE_INITIAL_PAUSE;
                marquee_timer = MARQUEE_INITIAL_PAUSE_FRAMES;
            } else {
                marquee_timer = get_marquee_speed_frames();
            }
            break;
    }
}

static void
draw_gamelist(void) {
    if (list_len <= 0) {
        return;
    }

    char buffer[192];
    int visible_items = (list_len - current_starting_index) < cur_theme->items_per_page
                            ? (list_len - current_starting_index)
                            : cur_theme->items_per_page;

#ifndef STANDALONE_BINARY
    int hide_multidisc = sf_multidisc[0];
#else
    int hide_multidisc = 1;
#endif

    font_bmp_begin_draw();

    for (int i = 0; i < visible_items; i++) {
        int list_idx = current_starting_index + i;
        const gd_item* item = list_current[list_idx];

        /* Check if this is the selected item */
        bool is_selected = (list_idx == current_selected_item);

        /* Check if selection changed */
        if (is_selected && (current_selected_item != marquee_last_selected)) {
            marquee_reset();
            marquee_last_selected = current_selected_item;
        }

        /* Get disc info for multidisc indicator */
        int disc_set = gd_item_disc_total(item->disc);

        /* Format item text - already has brackets for folders */
        snprintf(buffer, 191, "%s", item->name);

        /* Draw cursor for selected item */
        if (is_selected) {
            uint32_t cursor_color = (cur_theme->cursor_color & 0x00FFFFFF) | PVR_PACK_ARGB(cusor_alpha, 0, 0, 0);
            int list_x = cur_theme->list_x ? cur_theme->list_x : 12;
            int list_y = cur_theme->list_y ? cur_theme->list_y : 68;
            int marquee_threshold = cur_theme->list_marquee_threshold ? cur_theme->list_marquee_threshold : 49;
            int cursor_width = (X_ADJUST_TEXT * 2) + (marquee_threshold * FONT_CHAR_WIDTH);
            draw_draw_quad(list_x, list_y + Y_ADJUST_TEXT + (i * ITEM_SPACING) - Y_ADJUST_CRSR, cursor_width,
                           CURSOR_HEIGHT, cursor_color);

            /* Set highlight color for text (only show multidisc color if product code exists) */
            if (hide_multidisc && (disc_set > 1) && item->product[0] != '\0') {
                font_bmp_set_color(cur_theme->multidisc_color);
            } else {
                font_bmp_set_color(cur_theme->colors.highlight_color);
            }

            /* Handle marquee for long names */
            int name_len = strlen(buffer);

            /* Check if it's a folder (starts with '[') */
            if (buffer[0] == '[' && name_len > 2) {
                /* Extract inner text (between brackets) */
                char* inner_start = &buffer[1];
                char* bracket_end = strrchr(buffer, ']');
                if (bracket_end && bracket_end > inner_start) {
                    int inner_len = bracket_end - inner_start;

                    int inner_threshold = cur_theme->list_marquee_threshold - 2;
                    if (inner_len > inner_threshold) {
                        /* Folder name needs marquee - keep brackets fixed */
                        /* Add 2 to compensate for display width difference (inner vs full) */
                        marquee_update_animation(inner_len + 2);

                        /* Temporarily null-terminate to show inner_threshold-char window of inner text */
                        char saved_char = inner_start[marquee_offset + inner_threshold];
                        inner_start[marquee_offset + inner_threshold] = '\0';

                        /* Build display string: [ + scrolled_text + ] */
                        char display_buf[128];
                        snprintf(display_buf, sizeof(display_buf), "[%s]", &inner_start[marquee_offset]);

                        font_bmp_draw_main(list_x + X_ADJUST_TEXT, list_y + Y_ADJUST_TEXT + (i * ITEM_SPACING),
                                           display_buf);

                        inner_start[marquee_offset + inner_threshold] = saved_char;
                    } else {
                        /* Folder name fits within threshold */
                        font_bmp_draw_main(list_x + X_ADJUST_TEXT, list_y + Y_ADJUST_TEXT + (i * ITEM_SPACING), buffer);
                    }
                } else {
                    /* Malformed bracket - display as-is */
                    font_bmp_draw_main(list_x + X_ADJUST_TEXT, list_y + Y_ADJUST_TEXT + (i * ITEM_SPACING), buffer);
                }
            } else if (name_len > cur_theme->list_marquee_threshold) {
                /* Non-folder long name - normal marquee */
                marquee_update_animation(name_len);
                char saved_char = buffer[marquee_offset + cur_theme->list_marquee_threshold];
                buffer[marquee_offset + cur_theme->list_marquee_threshold] = '\0';
                font_bmp_draw_main(list_x + X_ADJUST_TEXT, list_y + Y_ADJUST_TEXT + (i * ITEM_SPACING),
                                   &buffer[marquee_offset]);
                buffer[marquee_offset + cur_theme->list_marquee_threshold] = saved_char;
            } else {
                /* Short name - display normally */
                font_bmp_draw_main(list_x + X_ADJUST_TEXT, list_y + Y_ADJUST_TEXT + (i * ITEM_SPACING), buffer);
            }
        } else {
            /* Normal text color */
            font_bmp_set_color(cur_theme->colors.text_color);

            /* Truncate long names for non-selected items */
            int name_len = strlen(buffer);
            if (name_len > cur_theme->list_marquee_threshold) {
                /* Check if it's a folder */
                if (buffer[0] == '[' && name_len > 2) {
                    /* Truncate inner text and keep closing bracket */
                    buffer[cur_theme->list_marquee_threshold - 1] = ']';
                    buffer[cur_theme->list_marquee_threshold] = '\0';
                } else {
                    /* Regular truncation */
                    buffer[cur_theme->list_marquee_threshold] = '\0';
                }
            }

            /* Draw item text */
            int list_x = cur_theme->list_x ? cur_theme->list_x : 12;
            int list_y = cur_theme->list_y ? cur_theme->list_y : 68;
            font_bmp_draw_main(list_x + X_ADJUST_TEXT, list_y + Y_ADJUST_TEXT + (i * ITEM_SPACING), buffer);
        }
    }

    /* Update strobe animation */
    if (cusor_alpha == 255) {
        cusor_step = -5;
    } else if (!cusor_alpha) {
        cusor_step = 5;
    }
    cusor_alpha += cusor_step;
}

static void
draw_gameart(void) {
    if (list_len <= 0) {
        return;
    }

    const gd_item* item = list_current[current_selected_item];

    if (!strncmp(item->disc, "DIR", 3)) {
#ifndef STANDALONE_BINARY
        if (sf_folder_art[0] == FOLDER_ART_OFF) {
            return;
        }
#endif
        /* Folder artwork keyed by hashed path. The parent row and folders
         * without an entry come back empty and draw nothing. */
        txr_get_folder(item->product, &txr_focus);
        if (txr_focus.texture == img_empty_boxart.texture) {
            return;
        }
    } else {
#ifndef STANDALONE_BINARY
        if (sf_folders_art[0] == FOLDERS_ART_OFF) {
            return;
        }
#endif

        /* Load artwork for games */
        {
            txr_get_large(item->product, &txr_focus);
            if (txr_focus.texture == img_empty_boxart.texture) {
                txr_get_small(item->product, &txr_focus);
            }
        }

        if (txr_focus.texture == img_empty_boxart.texture) {
            return;
        }
    }

    int artwork_x = cur_theme->artwork_x ? cur_theme->artwork_x : 415;
    int artwork_y = cur_theme->artwork_y ? cur_theme->artwork_y : 215;
    int artwork_size = cur_theme->artwork_size ? cur_theme->artwork_size : 210;

    draw_draw_image(artwork_x, artwork_y, artwork_size, artwork_size, COLOR_WHITE, &txr_focus);
}

static void
draw_item_details(void) {
#ifndef STANDALONE_BINARY
    if (sf_folders_item_details[0] == FOLDERS_ITEM_DETAILS_OFF) {
        return;
    }
#endif

    if (list_len <= 0) {
        return;
    }

    const gd_item* item = list_current[current_selected_item];
    char details_line[64];

    int details_x = cur_theme->item_details_x ? cur_theme->item_details_x : 521;
    int details_y = cur_theme->item_details_y ? cur_theme->item_details_y : 430;

    /* Check if it's a folder */
    if (!strncmp(item->disc, "DIR", 3)) {
        /* Get folder stats - need to extract folder name */
        if (!strcmp(item->name, "[..]")) {
            /* Parent folder */
            snprintf(details_line, sizeof(details_line), "PARENT FOLDER");
        } else {
            /* Extract folder name from "[FolderName]" format */
            char folder_name[256];
            const char* start = item->name;
            if (start[0] == '[') {
                start++;
            }
            strncpy(folder_name, start, 255);
            folder_name[255] = '\0';
            char* end = strrchr(folder_name, ']');
            if (end) {
                *end = '\0';
            }

            int num_subfolders = 0;
            int num_games = 0;
            if (list_folder_get_stats(folder_name, &num_subfolders, &num_games) == 0) {
                if (num_subfolders > 0 && num_games > 0) {
                    snprintf(details_line, sizeof(details_line), "%d %s, %d %s", num_subfolders,
                             num_subfolders == 1 ? "SUBFOLDER" : "SUBFOLDERS", num_games,
                             num_games == 1 ? "DISC" : "DISCS");
                } else if (num_subfolders > 0) {
                    snprintf(details_line, sizeof(details_line), "%d %s", num_subfolders,
                             num_subfolders == 1 ? "SUBFOLDER" : "SUBFOLDERS");
                } else if (num_games > 0) {
                    snprintf(details_line, sizeof(details_line), "%d %s", num_games, num_games == 1 ? "DISC" : "DISCS");
                } else {
                    snprintf(details_line, sizeof(details_line), "EMPTY");
                }
            } else {
                snprintf(details_line, sizeof(details_line), "UNKNOWN");
            }
        }
    } else {
        /* It's a disc - determine disc info from disc field (format: "X/Y") */
        int current_disc = gd_item_disc_num(item->disc);
        int total_discs = gd_item_disc_total(item->disc);

        /* Treat as single disc if no product code */
        if (item->product[0] == '\0') {
            current_disc = total_discs = 1;
        }

#ifndef STANDALONE_BINARY
        /* Calculate effective disc count based on grouping setting:
         * - "Anywhere" at root level: show total disc count (all folders)
         * - "Anywhere" in subfolders: show local disc count
         * - "Same Folder Only": always show local disc count */
        int effective_total = total_discs;
        if (total_discs > 1 && sf_multidisc[0] == MULTIDISC_HIDE) {
            if (sf_multidisc_grouping[0] == MULTIDISC_GROUPING_ANYWHERE && list_folder_is_root()) {
                effective_total = list_count_multidisc_filtered(item->product, NULL);
            } else {
                effective_total = list_count_multidisc_in_folder(item->product);
            }
        }

        if (effective_total <= 1) {
            snprintf(details_line, sizeof(details_line), "SINGLE DISC");
        } else {
            /* Check if multidisc is hidden (collapsed view) */
            if (sf_multidisc[0]) {
                snprintf(details_line, sizeof(details_line), "%d DISCS", effective_total);
            } else {
                snprintf(details_line, sizeof(details_line), "DISC %d OF %d", current_disc, effective_total);
            }
        }
#else
        if (total_discs <= 1) {
            snprintf(details_line, sizeof(details_line), "SINGLE DISC");
        } else {
            snprintf(details_line, sizeof(details_line), "%d DISCS", total_discs);
        }
#endif
    }

    /* Draw single line centered on details_x */
    int text_width = strlen(details_line) * FONT_CHAR_WIDTH;
    int centered_x = details_x - (text_width / 2);

    uint32_t text_color =
        cur_theme->item_details_text_color ? cur_theme->item_details_text_color : cur_theme->colors.text_color;
    font_bmp_begin_draw();
    font_bmp_set_color(text_color);
    font_bmp_draw_main(centered_x, details_y, details_line);
}

static void
draw_clock(void) {
    /* Check if clock is disabled */
    if (sf_clock[0] == CLOCK_OFF) {
        return;
    }

    /* Get clock position from theme (or use defaults) */
    int clock_x = cur_theme->clock_x ? cur_theme->clock_x : 521;
    int clock_y = cur_theme->clock_y ? cur_theme->clock_y : 24;

    /* Get current time */
    time_t now;
#ifdef _arch_dreamcast
    now = rtc_unix_secs();
#else
    now = time(NULL);
#endif
    struct tm* t = localtime(&now);
    if (!t) {
        return;
    }

    char clock_buf[32];
    if (sf_clock[0] == CLOCK_12HOUR) {
        /* 12-hour format with AM/PM */
        int hour12 = t->tm_hour % 12;
        if (hour12 == 0) {
            hour12 = 12;
        }
        const char* ampm = (t->tm_hour < 12) ? "AM" : "PM";
        snprintf(clock_buf, sizeof(clock_buf), "%04d-%02d-%02d %02d:%02d:%02d %s", t->tm_year + 1900, t->tm_mon + 1,
                 t->tm_mday, hour12, t->tm_min, t->tm_sec, ampm);
    } else {
        /* 24-hour format */
        snprintf(clock_buf, sizeof(clock_buf), "%04d-%02d-%02d %02d:%02d:%02d", t->tm_year + 1900, t->tm_mon + 1,
                 t->tm_mday, t->tm_hour, t->tm_min, t->tm_sec);
    }

    /* Draw clock right-justified (clock_x is right edge) */
    int text_width = strlen(clock_buf) * FONT_CHAR_WIDTH;
    int right_x = clock_x - text_width;

    font_bmp_begin_draw();
    font_bmp_set_color(cur_theme->clock_text_color);
    font_bmp_draw_main(right_x, clock_y, clock_buf);
}

/* VMU_SYNC_DEBUG_START */
#if DEBUG_VMU_SYNC
/* VMU Time Sync Debug Display - enable DEBUG_VMU_SYNC in openmenu_debug.h */
static void
draw_vmu_sync_debug(void) {
#ifdef _arch_dreamcast
    /* Only show if VMU Time Sync is enabled */
    if (sf_vmu_time_sync[0] != VMU_TIME_SYNC_ON) {
        return;
    }

    /* Draw debug info at bottom of screen */
    font_bmp_begin_draw();
    font_bmp_set_color(PVR_PACK_ARGB(255, 255, 255, 0)); /* Yellow text */

    /* Draw header and five lines of debug info */
    font_bmp_draw_main(10, 375, "--- VMU TIME SYNC DEBUG (0=OK, -999=not called) ---");
    font_bmp_draw_main(10, 390, get_vmu_sync_debug_line1());
    font_bmp_draw_main(10, 405, get_vmu_sync_debug_line2());
    font_bmp_draw_main(10, 420, get_vmu_sync_debug_line3());
    font_bmp_draw_main(10, 435, get_vmu_sync_debug_line4());
    font_bmp_draw_main(10, 450, get_vmu_sync_debug_line5());
#endif
}
#endif
/* VMU_SYNC_DEBUG_END */

/* Navigation functions */

static void
menu_decrement(int amount) {
    if (direction_held && navigate_timeout > 0) {
        return;
    }

    if (current_selected_item < amount) {
        /* Single-step (UP): wrap to bottom. Page jump (L/R): stop at top */
        if (amount == 1) {
            current_selected_item = list_len - 1;
            current_starting_index = list_len - cur_theme->items_per_page;
            if (current_starting_index < 0) {
                current_starting_index = 0;
            }
        } else {
            current_selected_item = 0;
            current_starting_index = 0;
        }
    } else {
        current_selected_item -= amount;
    }

    if (current_selected_item < current_starting_index) {
        current_starting_index -= amount;
        if (current_starting_index < 0) {
            current_starting_index = 0;
        }
    }

    navigate_timeout = direction_held ? INPUT_TIMEOUT_REPEAT : INPUT_TIMEOUT_INITIAL;
}

static void
menu_increment(int amount) {
    if (direction_held && navigate_timeout > 0) {
        return;
    }

    current_selected_item += amount;
    if (current_selected_item >= list_len) {
        /* Single-step (DOWN): wrap to top. Page jump (L/R): stop at bottom */
        if (amount == 1) {
            current_selected_item = 0;
            current_starting_index = 0;
        } else {
            current_selected_item = list_len - 1;
            current_starting_index = list_len - cur_theme->items_per_page;
            if (current_starting_index < 0) {
                current_starting_index = 0;
            }
        }
        navigate_timeout = direction_held ? INPUT_TIMEOUT_REPEAT : INPUT_TIMEOUT_INITIAL;
        return;
    }

    if (current_selected_item >= current_starting_index + cur_theme->items_per_page) {
        current_starting_index += amount;
    }

    navigate_timeout = direction_held ? INPUT_TIMEOUT_REPEAT : INPUT_TIMEOUT_INITIAL;
}

static void
run_cb(void) {
    /* printf("run_cb: Starting\n"); */
    const gd_item* item = list_current[current_selected_item];
    int disc_set = gd_item_disc_total(item->disc);
    /* printf("run_cb: disc_set=%d\n", disc_set); */

#ifndef STANDALONE_BINARY
    int hide_multidisc = sf_multidisc[0];
#else
    int hide_multidisc = 1;
#endif

    /* printf("run_cb: hide_multidisc=%d\n", hide_multidisc); */

    /* Only show multidisc chooser if product code exists */
    if (hide_multidisc && (disc_set > 1) && item->product[0] != '\0') {
        /* Grouping: "Anywhere" at root searches all, otherwise current folder */
#ifndef STANDALONE_BINARY
        if (sf_multidisc_grouping[0] == MULTIDISC_GROUPING_ANYWHERE && list_folder_is_root()) {
            list_set_multidisc(item->product);
        } else {
            list_set_multidisc_in_folder(item->product);
        }
#else
        list_set_multidisc(item->product);
#endif

        /* Check if multiple discs remain after filtering */
        if (list_multidisc_length() > 1) {
            /* printf("run_cb: Showing multidisc popup\n"); */
            draw_current = DRAW_MULTIDISC;
            cb_multidisc = 1;
            /* printf("run_cb: Calling popup_setup\n"); */
            popup_setup(&draw_current, &cur_theme->colors, &navigate_timeout, cur_theme->menu_title_color);
            /* printf("run_cb: Multidisc setup complete\n"); */
            return;
        }
        /* Only 1 disc in this folder, fall through to launch directly */
    }

    /* printf("run_cb: Launching CB\n"); */
    if (sf_serial_vmu[0] != SERIAL_VMU_OFF) {
        set_cur_game_item(item);
        draw_current = DRAW_SERIAL_VMU;
        serial_vmu_setup(&draw_current, &cur_theme->colors, &navigate_timeout, cur_theme->menu_title_color);
        serial_vmu_start_restore(item, SERIAL_VMU_LAUNCH_CB);
    } else {
        dreamcast_launch_cb(item);
    }
}

static void
menu_accept(void) {
    if (list_len <= 0) {
        return;
    }

    const gd_item* item = list_current[current_selected_item];

    /* Check if it's a directory */
    if (!strncmp(item->disc, "DIR", 3)) {
        if (!strcmp(item->name, "[..]")) {
            /* Go back and restore cursor position */
            int restored_pos = list_folder_go_back();

            /* Reload list */
            list_current = list_get();
            list_len = list_length();

            /* Restore cursor position */
            current_selected_item = restored_pos;

            /* Adjust viewport to show restored cursor */
            if (current_selected_item < cur_theme->items_per_page) {
                current_starting_index = 0;
            } else {
                current_starting_index = current_selected_item - (cur_theme->items_per_page / 2);
                if (current_starting_index + cur_theme->items_per_page > list_len) {
                    current_starting_index = list_len - cur_theme->items_per_page;
                }
                if (current_starting_index < 0) {
                    current_starting_index = 0;
                }
            }
        } else if (item->product[0] == 'F') {
            /* Enter folder, saving current cursor position */
            /* Extract folder name from "[FolderName]" format */
            char folder_name[256];
            const char* start = item->name;
            if (start[0] == '[') {
                start++; /* Skip opening bracket */
            }
            strncpy(folder_name, start, 255);
            folder_name[255] = '\0';
            /* Remove closing bracket if present */
            char* end = strrchr(folder_name, ']');
            if (end) {
                *end = '\0';
            }
            list_folder_enter(folder_name, current_selected_item);

            /* Reload list */
            list_current = list_get();
            list_len = list_length();

            /* Start at top of new folder */
            current_selected_item = 0;
            current_starting_index = 0;
        }
        navigate_timeout = 3;
        draw_current = DRAW_UI;
        return;
    }

    /* Check for multidisc */
    int disc_set = gd_item_disc_total(item->disc);

#ifndef STANDALONE_BINARY
    int hide_multidisc = sf_multidisc[0];
#else
    int hide_multidisc = 1;
#endif

    /* Show multidisc chooser menu if needed (only if product code exists) */
    if (hide_multidisc && (disc_set > 1) && item->product[0] != '\0') {
        /* Grouping: "Anywhere" at root searches all, otherwise current folder */
#ifndef STANDALONE_BINARY
        if (sf_multidisc_grouping[0] == MULTIDISC_GROUPING_ANYWHERE && list_folder_is_root()) {
            list_set_multidisc(item->product);
        } else {
            list_set_multidisc_in_folder(item->product);
        }
#else
        list_set_multidisc(item->product);
#endif

        /* Check if multiple discs remain after filtering */
        if (list_multidisc_length() > 1) {
            /* printf("menu_accept: Showing multidisc popup for disc_set=%d\n", disc_set); */
            cb_multidisc = 0;
            draw_current = DRAW_MULTIDISC;
            popup_setup(&draw_current, &cur_theme->colors, &navigate_timeout, cur_theme->menu_title_color);
            return;
        }
        /* Only 1 disc in this folder, fall through to launch directly */
    }

    /* Launch game */
    if (!strcmp(item->type, "psx")) {
        if (is_bloom_available()) {
            /* Show PSX launcher choice popup (Serial VMU intercept happens in PSX launcher accept) */
            set_cur_game_item(item);
            draw_current = DRAW_PSX_LAUNCHER;
            popup_setup(&draw_current, &cur_theme->colors, &navigate_timeout, cur_theme->menu_title_color);
        } else {
            /* No Bloom available, launch directly with Bleem */
            if (sf_serial_vmu[0] != SERIAL_VMU_OFF && strcmp(item->type, "other") != 0) {
                set_cur_game_item(item);
                draw_current = DRAW_SERIAL_VMU;
                serial_vmu_setup(&draw_current, &cur_theme->colors, &navigate_timeout, cur_theme->menu_title_color);
                serial_vmu_start_restore(item, SERIAL_VMU_LAUNCH_BLEEM);
            } else {
                bleem_launch(item);
            }
        }
    } else {
        if (sf_serial_vmu[0] != SERIAL_VMU_OFF && strcmp(item->type, "other") != 0) {
            set_cur_game_item(item);
            draw_current = DRAW_SERIAL_VMU;
            serial_vmu_setup(&draw_current, &cur_theme->colors, &navigate_timeout, cur_theme->menu_title_color);
            serial_vmu_start_restore(item, SERIAL_VMU_LAUNCH_DC);
        } else {
            dreamcast_launch_disc(item);
        }
    }
}

static void
menu_cb(void) {
    if (list_len <= 0) {
        return;
    }

    /* CodeBreaker only available for regular games */
    if (strcmp(list_current[current_selected_item]->type, "game") != 0) {
        return;
    }

    start_cb = 0;
    draw_current = DRAW_CODEBREAKER;
    cb_menu_setup(&draw_current, &cur_theme->colors, &navigate_timeout, cur_theme->menu_title_color);
}

static void
menu_settings(void) {
    draw_current = DRAW_MENU;
    menu_setup(&draw_current, &cur_theme->colors, &navigate_timeout, cur_theme->menu_title_color);
}

static void
menu_exit(void) {
    const gd_item* item = list_current[current_selected_item];
    set_cur_game_item(item);

    /* Check if current item is a folder (disc starts with "DIR") */
    int is_folder = (item != NULL && !strncmp(item->disc, "DIR", 3));

    draw_current = DRAW_EXIT;
    exit_menu_setup(&draw_current, &cur_theme->colors, &navigate_timeout, cur_theme->menu_title_color, is_folder);
}

static void
menu_go_back(void) {
    /* Go back one folder level if not at root */
    if (!list_folder_is_root()) {
        /* Go back and restore cursor position */
        int restored_pos = list_folder_go_back();

        /* Reload list */
        list_current = list_get();
        list_len = list_length();

        /* Restore cursor position */
        current_selected_item = restored_pos;

        /* Adjust viewport to show restored cursor */
        if (current_selected_item < cur_theme->items_per_page) {
            current_starting_index = 0;
        } else {
            current_starting_index = current_selected_item - (cur_theme->items_per_page / 2);
            if (current_starting_index + cur_theme->items_per_page > list_len) {
                current_starting_index = list_len - cur_theme->items_per_page;
            }
            if (current_starting_index < 0) {
                current_starting_index = 0;
            }
        }

        navigate_timeout = 3;
    }
}

/* Quick-jump: check for Shift+Key and jump to first matching item */
static void
handle_keyboard_quickjump(void) {
    uint8_t mods = INPT_KeyboardModifiers();
    bool shift_held = (mods & KBD_MOD_LSHIFT) || (mods & KBD_MOD_RSHIFT);
    if (!shift_held || list_len <= 0) {
        return;
    }

    char target_char = 0;

    /* Check A-Z keys */
    for (uint8_t key = KBD_KEY_A; key <= KBD_KEY_Z; key++) {
        if (INPT_KeyboardButtonPress(key)) {
            target_char = 'A' + (key - KBD_KEY_A);
            break;
        }
    }

    /* Check 1-9 keys */
    if (!target_char) {
        for (uint8_t key = KBD_KEY_1; key <= KBD_KEY_9; key++) {
            if (INPT_KeyboardButtonPress(key)) {
                target_char = '1' + (key - KBD_KEY_1);
                break;
            }
        }
    }

    /* Check 0 key */
    if (!target_char && INPT_KeyboardButtonPress(KBD_KEY_0)) {
        target_char = '0';
    }

    if (!target_char) {
        return;
    }

    /* Find next item starting with target_char (case-insensitive).
     * Start searching from current position + 1, wrap around if needed. */
    char target_lower = (target_char >= 'A' && target_char <= 'Z') ? target_char + 32 : target_char;
    char target_upper = (target_char >= 'a' && target_char <= 'z') ? target_char - 32 : target_char;

    for (int offset = 1; offset <= list_len; offset++) {
        int i = (current_selected_item + offset) % list_len;
        const char* name = list_current[i]->name;
        char first_char;

        /* For folders "[Name]", match against the character inside the bracket */
        if (name[0] == '[' && name[1] != '\0') {
            first_char = name[1];
        } else {
            first_char = name[0];
        }

        if (first_char == target_lower || first_char == target_upper) {
            /* Found a match - move cursor there */
            current_selected_item = i;

            /* Adjust viewport */
            if (current_selected_item < cur_theme->items_per_page) {
                current_starting_index = 0;
            } else {
                current_starting_index = current_selected_item - (cur_theme->items_per_page / 2);
                if (current_starting_index + cur_theme->items_per_page > list_len) {
                    current_starting_index = list_len - cur_theme->items_per_page;
                }
                if (current_starting_index < 0) {
                    current_starting_index = 0;
                }
            }

            navigate_timeout = INPUT_TIMEOUT_INITIAL;
            return;
        }
    }
    /* No match found - cursor stays where it is */
}

/* Input handlers */

static void
handle_input_ui(enum control input) {
    direction_last = direction_current;
    direction_current = false;

    switch (input) {
        case UP:
            direction_current = true;
            menu_decrement(1);
            break;
        case DOWN:
            direction_current = true;
            menu_increment(1);
            break;
        case LEFT:
        case TRIG_L:
            direction_current = true;
            menu_decrement(5);
            break;
        case RIGHT:
        case TRIG_R:
            direction_current = true;
            menu_increment(5);
            break;
        case A: menu_accept(); break;
        case B: menu_go_back(); break;
        case X: menu_cb(); break;
        case Y: menu_exit(); break;
        case START: menu_settings(); break;

        case NONE:
        default: break;
    }

    /* Keyboard quick-jump: Shift+Letter/Number */
    handle_keyboard_quickjump();
}

/* Main UI functions */

FUNCTION(UI_NAME, init) {
    texman_clear();
    txr_empty_small_pool();
    txr_empty_large_pool();

    /* Load default FOLDERS theme from THEME.INI */
    theme_read("/cd/THEME/FOLDERS/THEME.INI", &default_theme, 2);

    /* Load Folder-style themes */
    if (sf_custom_theme[0]) {
        int custom_theme_num = 0;
        custom = theme_get_folder(&custom_theme_num);
        if ((int)sf_custom_theme_num[0] >= custom_theme_num) {
            /* Fallback to default Folder theme */
            cur_theme = (theme_scroll*)&default_theme;
        } else {
            cur_theme = &custom[sf_custom_theme_num[0]];
        }
    } else {
        /* Use default Scroll theme */
        cur_theme = (theme_scroll*)&default_theme;
    }

    unsigned int temp = texman_create();
    draw_load_texture_buffer(cur_theme->bg_left, &txr_bg_left, texman_get_tex_data(temp));
    texman_reserve_memory(txr_bg_left.width, txr_bg_left.height, 2 /* 16Bit */);

    temp = texman_create();
    draw_load_texture_buffer(cur_theme->bg_right, &txr_bg_right, texman_get_tex_data(temp));
    texman_reserve_memory(txr_bg_right.width, txr_bg_right.height, 2 /* 16Bit */);

    /* Initialize font */
    font_bmp_init(cur_theme->font, 8, 16);
}

FUNCTION(UI_NAME, setup) {
    /* Set to root folder view */
    list_set_folder_root();

    /* Get list pointers */
    list_current = list_get();
    list_len = list_length();

    /* Reset navigation state */
    current_selected_item = 0;
    current_starting_index = 0;
    navigate_timeout = 3;
    draw_current = DRAW_UI;

    cusor_alpha = 255;
    cusor_step = -5;

    /* Initialize marquee state */
    marquee_reset();
    marquee_last_selected = -1;
}

FUNCTION(UI_NAME, drawOP) { draw_bg_layers(); }

FUNCTION(UI_NAME, drawTR) {
    /* Always draw the game list, artwork, and item details first */
    draw_gamelist();
    draw_gameart();
    draw_item_details();
    draw_clock();
#if DEBUG_VMU_SYNC
    draw_vmu_sync_debug(); /* Enable DEBUG_VMU_SYNC in openmenu_debug.h */
#endif

    /* Check for pending Serial VMU backup on first frame */
    if (!serial_vmu_boot_checked && draw_current == DRAW_UI) {
        serial_vmu_boot_checked = true;
        serial_vmu_check_boot_backup(&draw_current, &cur_theme->colors, &navigate_timeout, cur_theme->menu_title_color);
    }

    /* Then draw popups on top */
    switch (draw_current) {
        case DRAW_MENU: {
            draw_menu_tr();
        } break;
        case DRAW_CREDITS: {
            draw_credits_tr();
        } break;
        case DRAW_MULTIDISC: {
            draw_multidisc_tr();
        } break;
        case DRAW_EXIT: {
            draw_exit_tr();
        } break;
        case DRAW_CODEBREAKER: {
            draw_codebreaker_tr();
        } break;
        case DRAW_PSX_LAUNCHER: {
            draw_psx_launcher_tr();
        } break;
        case DRAW_SAVELOAD: {
            draw_saveload_tr();
        } break;
        /* COMPACTION_TEST_START */
        case DRAW_COMPACTION_TEST: {
            draw_compaction_test_op();
            draw_compaction_test_tr();
        } break;
        /* COMPACTION_TEST_END */
        case DRAW_SERIAL_VMU: {
            draw_serial_vmu_op();
            draw_serial_vmu_tr();
        } break;
        default:
        case DRAW_UI: {
            /* Game list and artwork already drawn above */
        } break;
    }
}

FUNCTION_INPUT(UI_NAME, handle_input) {
    enum control input_current = button;

    switch (draw_current) {
        case DRAW_MENU: {
            handle_input_menu(input_current);
        } break;
        case DRAW_CREDITS: {
            handle_input_credits(input_current);
        } break;
        case DRAW_MULTIDISC: {
            handle_input_multidisc(input_current);
        } break;
        case DRAW_EXIT: {
            handle_input_exit(input_current);
        } break;
        case DRAW_CODEBREAKER: {
            handle_input_codebreaker(input_current);
            if (start_cb) {
                run_cb();
            }
        } break;
        case DRAW_PSX_LAUNCHER: {
            handle_input_psx_launcher(input_current);
        } break;
        case DRAW_SAVELOAD: {
            handle_input_saveload(input_current);
        } break;
        /* COMPACTION_TEST_START */
        case DRAW_COMPACTION_TEST: {
            handle_input_compaction_test(input_current);
        } break;
        /* COMPACTION_TEST_END */
        case DRAW_SERIAL_VMU: {
            handle_input_serial_vmu(input_current);
        } break;
        default:
        case DRAW_UI: {
            handle_input_ui(input_current);
        } break;
    }

    navigate_timeout--;
}
