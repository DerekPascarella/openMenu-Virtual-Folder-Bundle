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

#include <backend/gd_item.h>
#include <backend/gd_list.h>
#include <openmenu_settings.h>
#include "dc/input.h"
#include "texture/txr_manager.h"
#include "ui/draw_prototypes.h"
#include "ui/font_prototypes.h"
#include "ui/ui_common.h"
#include "ui/ui_menu_credits.h"
#include "ui/theme_manager.h"

#include "ui/ui_folders.h"

#define UNUSED __attribute__((unused))

/* Static resources */
static image txr_bg_left, txr_bg_right;
static image txr_focus;
extern image img_empty_boxart;
extern image img_dir_boxart;

/* Theme management (from Scroll) */
static theme_scroll default_theme = {"THEME/FOLDERS/BG_L.PVR",
                                     "THEME/FOLDERS/BG_R.PVR",
                                     "FOLDERS",
                                           {PVR_PACK_ARGB(255, 152, 158, 164), PVR_PACK_ARGB(255, 103, 193, 245),
                                            PVR_PACK_ARGB(255, 164, 158, 152), PVR_PACK_ARGB(255, 103, 193, 245),
                                            COLOR_BLACK, PVR_PACK_ARGB(255, 13, 44, 70), COLOR_WHITE},
                                           "FONT/GDMNUFNT.PVR",
                                           PVR_PACK_ARGB(255, 33, 56, 82),
                                           PVR_PACK_ARGB(255, 100, 255, 225),
                                           COLOR_WHITE,
                                           404,
                                           20,
                                           20,
                                           3,
                                           14,
                                           424,
                                           85,
                                           109,
                                           133,
                                           157,
                                           181,
                                           420,
                                           213,
                                           11,
                                           77,
                                           420,
                                           213,
                                           210};

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

static bool direction_last = false;
static bool direction_current = false;
#define direction_held (direction_last & direction_current)

/* Strobe cursor animation */
static uint8_t cusor_alpha = 255;
static char cusor_step = -5;

/* Marquee scrolling state */
#define MARQUEE_DISPLAY_WIDTH 49
#define MARQUEE_INITIAL_PAUSE_FRAMES 60
#define MARQUEE_END_PAUSE_FRAMES 90

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

/* L+R trigger state for back navigation */
static bool trig_l_held = false;
static bool trig_r_held = false;

/* Display constants */
#define ITEMS_PER_PAGE 17
#define ITEM_SPACING 21
#define CURSOR_WIDTH 404
#define CURSOR_HEIGHT 20
#define X_ADJUST_TEXT 7
#define Y_ADJUST_TEXT 4
#define Y_ADJUST_CRSR 3

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
    int max_offset = name_length - MARQUEE_DISPLAY_WIDTH;
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
    int visible_items = (list_len - current_starting_index) < ITEMS_PER_PAGE
                            ? (list_len - current_starting_index)
                            : ITEMS_PER_PAGE;

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
        int disc_set = item->disc[2] - '0';

        /* Format item text - already has brackets for folders */
        snprintf(buffer, 191, "%s", item->name);

        /* Draw cursor for selected item */
        if (is_selected) {
            uint32_t cursor_color = (cur_theme->cursor_color & 0x00FFFFFF) |
                                    PVR_PACK_ARGB(cusor_alpha, 0, 0, 0);
            int list_x = cur_theme->list_x ? cur_theme->list_x : 11;
            int list_y = cur_theme->list_y ? cur_theme->list_y : 77;
            draw_draw_quad(list_x, list_y + Y_ADJUST_TEXT + (i * ITEM_SPACING) - Y_ADJUST_CRSR,
                          CURSOR_WIDTH, CURSOR_HEIGHT, cursor_color);

            /* Set highlight color for text */
            if (hide_multidisc && (disc_set > 1)) {
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

                    if (inner_len > 47) {
                        /* Folder name needs marquee - keep brackets fixed */
                        /* Add 2 to compensate for display width difference (47 vs 49) */
                        marquee_update_animation(inner_len + 2);

                        /* Temporarily null-terminate to show 47-char window of inner text */
                        char saved_char = inner_start[marquee_offset + 47];
                        inner_start[marquee_offset + 47] = '\0';

                        /* Build display string: [ + scrolled_text + ] */
                        char display_buf[50];
                        snprintf(display_buf, 50, "[%s]", &inner_start[marquee_offset]);

                        font_bmp_draw_main(list_x + X_ADJUST_TEXT,
                                           list_y + Y_ADJUST_TEXT + (i * ITEM_SPACING),
                                           display_buf);

                        inner_start[marquee_offset + 47] = saved_char;
                    } else {
                        /* Folder name fits in 49 chars */
                        font_bmp_draw_main(list_x + X_ADJUST_TEXT,
                                           list_y + Y_ADJUST_TEXT + (i * ITEM_SPACING),
                                           buffer);
                    }
                } else {
                    /* Malformed bracket - display as-is */
                    font_bmp_draw_main(list_x + X_ADJUST_TEXT,
                                       list_y + Y_ADJUST_TEXT + (i * ITEM_SPACING),
                                       buffer);
                }
            } else if (name_len > MARQUEE_DISPLAY_WIDTH) {
                /* Non-folder long name - normal marquee */
                marquee_update_animation(name_len);
                char saved_char = buffer[marquee_offset + MARQUEE_DISPLAY_WIDTH];
                buffer[marquee_offset + MARQUEE_DISPLAY_WIDTH] = '\0';
                font_bmp_draw_main(list_x + X_ADJUST_TEXT,
                                   list_y + Y_ADJUST_TEXT + (i * ITEM_SPACING),
                                   &buffer[marquee_offset]);
                buffer[marquee_offset + MARQUEE_DISPLAY_WIDTH] = saved_char;
            } else {
                /* Short name - display normally */
                font_bmp_draw_main(list_x + X_ADJUST_TEXT,
                                   list_y + Y_ADJUST_TEXT + (i * ITEM_SPACING),
                                   buffer);
            }
        } else {
            /* Normal text color */
            font_bmp_set_color(cur_theme->colors.text_color);

            /* Truncate long names for non-selected items */
            int name_len = strlen(buffer);
            if (name_len > MARQUEE_DISPLAY_WIDTH) {
                /* Check if it's a folder */
                if (buffer[0] == '[' && name_len > 2) {
                    /* Truncate inner text at 47 chars and keep closing bracket */
                    buffer[48] = ']';
                    buffer[49] = '\0';
                } else {
                    /* Regular truncation */
                    buffer[MARQUEE_DISPLAY_WIDTH] = '\0';
                }
            }

            /* Draw item text */
            int list_x = cur_theme->list_x ? cur_theme->list_x : 11;
            int list_y = cur_theme->list_y ? cur_theme->list_y : 77;
            font_bmp_draw_main(list_x + X_ADJUST_TEXT,
                               list_y + Y_ADJUST_TEXT + (i * ITEM_SPACING),
                               buffer);
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
#ifndef STANDALONE_BINARY
    if (sf_folders_art[0] == FOLDERS_ART_OFF) {
        return;
    }
#endif

    if (list_len <= 0) {
        return;
    }

    const gd_item* item = list_current[current_selected_item];

    /* Don't show artwork for folders */
    if (!strncmp(item->disc, "DIR", 3)) {
        return;
    }

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

    int artwork_x = cur_theme->artwork_x ? cur_theme->artwork_x : 420;
    int artwork_y = cur_theme->artwork_y ? cur_theme->artwork_y : 213;
    int artwork_size = cur_theme->artwork_size ? cur_theme->artwork_size : 210;
    draw_draw_image(artwork_x, artwork_y, artwork_size, artwork_size, COLOR_WHITE, &txr_focus);
}

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
            current_starting_index = list_len - ITEMS_PER_PAGE;
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
            current_starting_index = list_len - ITEMS_PER_PAGE;
            if (current_starting_index < 0) {
                current_starting_index = 0;
            }
        }
        navigate_timeout = direction_held ? INPUT_TIMEOUT_REPEAT : INPUT_TIMEOUT_INITIAL;
        return;
    }

    if (current_selected_item >= current_starting_index + ITEMS_PER_PAGE) {
        current_starting_index += amount;
    }

    navigate_timeout = direction_held ? INPUT_TIMEOUT_REPEAT : INPUT_TIMEOUT_INITIAL;
}

static void
run_cb(void) {
    printf("run_cb: Starting\n");
    int disc_set = list_current[current_selected_item]->disc[2] - '0';
    printf("run_cb: disc_set=%d\n", disc_set);

#ifndef STANDALONE_BINARY
    int hide_multidisc = sf_multidisc[0];
#else
    int hide_multidisc = 1;
#endif

    printf("run_cb: hide_multidisc=%d\n", hide_multidisc);

    if (hide_multidisc && (disc_set > 1)) {
        printf("run_cb: Showing multidisc popup\n");
        draw_current = DRAW_MULTIDISC;
        cb_multidisc = 1;
        printf("run_cb: Calling popup_setup\n");
        popup_setup(&draw_current, &cur_theme->colors, &navigate_timeout, cur_theme->menu_title_color);
        printf("run_cb: Calling list_set_multidisc\n");
        list_set_multidisc(list_current[current_selected_item]->product);
        printf("run_cb: Multidisc setup complete\n");
        return;
    }

    printf("run_cb: Launching CB\n");
    dreamcast_launch_cb(list_current[current_selected_item]);
}

static void
menu_accept(void) {
    if ((navigate_timeout > 0) || (list_len <= 0)) {
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
            if (current_selected_item < ITEMS_PER_PAGE) {
                current_starting_index = 0;
            } else {
                current_starting_index = current_selected_item - (ITEMS_PER_PAGE / 2);
                if (current_starting_index + ITEMS_PER_PAGE > list_len) {
                    current_starting_index = list_len - ITEMS_PER_PAGE;
                }
                if (current_starting_index < 0) {
                    current_starting_index = 0;
                }
            }
        } else if (item->product[0] == 'F') {
            /* Enter folder, saving current cursor position */
            list_folder_enter(item->slot_num, current_selected_item);

            /* Reload list */
            list_current = list_get();
            list_len = list_length();

            /* Start at top of new folder */
            current_selected_item = 0;
            current_starting_index = 0;
        }
        navigate_timeout = INPUT_TIMEOUT_INITIAL * 2;
        draw_current = DRAW_UI;
        return;
    }

    /* Check for multidisc */
    int disc_set = item->disc[2] - '0';

#ifndef STANDALONE_BINARY
    int hide_multidisc = sf_multidisc[0];
#else
    int hide_multidisc = 1;
#endif

    /* Show multidisc chooser menu if needed */
    if (hide_multidisc && (disc_set > 1)) {
        printf("menu_accept: Showing multidisc popup for disc_set=%d\n", disc_set);
        cb_multidisc = 0;
        draw_current = DRAW_MULTIDISC;
        popup_setup(&draw_current, &cur_theme->colors, &navigate_timeout, cur_theme->menu_title_color);
        list_set_multidisc(item->product);
        return;
    }

    /* Launch game */
    if (!strncmp(item->disc, "PS1", 3)) {
        bloom_launch(item);
    } else {
        dreamcast_launch_disc(item);
    }
}

static void
menu_cb(void) {
    if ((navigate_timeout > 0) || (list_len <= 0)) {
        return;
    }

    if (!strncmp(list_current[current_selected_item]->disc, "DIR", 3)) {
        return;
    } else if (!strncmp(list_current[current_selected_item]->disc, "PS1", 3)) {
        bloom_launch(list_current[current_selected_item]);
        return;
    }

    start_cb = 0;
    draw_current = DRAW_CODEBREAKER;
    menu_setup(&draw_current, &cur_theme->colors, &navigate_timeout, cur_theme->menu_title_color);
}

static void
menu_settings(void) {
    if (navigate_timeout > 0) {
        return;
    }

    draw_current = DRAW_MENU;
    menu_setup(&draw_current, &cur_theme->colors, &navigate_timeout, cur_theme->menu_title_color);
}

static void
menu_exit(void) {
    if (navigate_timeout > 0) {
        return;
    }

    set_cur_game_item(list_current[current_selected_item]);

    draw_current = DRAW_EXIT;
    popup_setup(&draw_current, &cur_theme->colors, &navigate_timeout, cur_theme->menu_title_color);
}

/* Input handlers */

static void
handle_input_ui(enum control input) {
    direction_last = direction_current;
    direction_current = false;

    /* Check for L+R simultaneous press using state-based detection */
    if (INPT_TriggerPressed(TRIGGER_L) && INPT_TriggerPressed(TRIGGER_R)) {
        /* Only process if not already handled this frame */
        if (!trig_l_held || !trig_r_held) {
            trig_l_held = true;
            trig_r_held = true;

            /* Go back one folder level */
            if (!list_folder_is_root()) {
                /* Go back and restore cursor position */
                int restored_pos = list_folder_go_back();

                /* Reload list */
                list_current = list_get();
                list_len = list_length();

                /* Restore cursor position */
                current_selected_item = restored_pos;

                /* Adjust viewport to show restored cursor */
                if (current_selected_item < ITEMS_PER_PAGE) {
                    current_starting_index = 0;
                } else {
                    current_starting_index = current_selected_item - (ITEMS_PER_PAGE / 2);
                    if (current_starting_index + ITEMS_PER_PAGE > list_len) {
                        current_starting_index = list_len - ITEMS_PER_PAGE;
                    }
                    if (current_starting_index < 0) {
                        current_starting_index = 0;
                    }
                }

                navigate_timeout = INPUT_TIMEOUT_INITIAL * 2;
            }
        }
        return;
    } else {
        /* Reset flags when triggers are released */
        trig_l_held = false;
        trig_r_held = false;
    }

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
        case A:
            menu_accept();
            break;
        case X:
            menu_settings();
            break;
        case Y:
            menu_exit();
            break;
        case B:
            menu_cb();
            break;

        case NONE:
        default:
            break;
    }
}

/* Main UI functions */

FUNCTION(UI_NAME, init) {
    printf("FOLDERS_init: Starting\n");
    texman_clear();
    txr_empty_small_pool();
    txr_empty_large_pool();
    printf("FOLDERS_init: Texture pools cleared\n");

    /* Load default FOLDERS theme from THEME.INI */
    theme_read("/cd/THEME/FOLDERS/THEME.INI", &default_theme, 1);

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

    printf("FOLDERS_init: Loading backgrounds\n");
    unsigned int temp = texman_create();
    draw_load_texture_buffer(cur_theme->bg_left, &txr_bg_left, texman_get_tex_data(temp));
    texman_reserve_memory(txr_bg_left.width, txr_bg_left.height, 2 /* 16Bit */);

    temp = texman_create();
    draw_load_texture_buffer(cur_theme->bg_right, &txr_bg_right, texman_get_tex_data(temp));
    texman_reserve_memory(txr_bg_right.width, txr_bg_right.height, 2 /* 16Bit */);

    /* Initialize font */
    printf("FOLDERS_init: Initializing font\n");
    font_bmp_init(cur_theme->font, 8, 16);
    printf("FOLDERS_init: Font initialized\n");

    printf("FOLDERS: Init complete\n");
}

FUNCTION(UI_NAME, setup) {
    printf("FOLDERS_setup: Starting\n");

    /* Set to root folder view */
    list_set_folder_root();

    printf("FOLDERS_setup: Getting list pointers\n");
    /* Get list pointers */
    list_current = list_get();
    list_len = list_length();

    printf("FOLDERS_setup: Got %d items\n", list_len);

    /* Reset navigation state */
    current_selected_item = 0;
    current_starting_index = 0;
    navigate_timeout = INPUT_TIMEOUT_INITIAL;
    draw_current = DRAW_UI;

    cusor_alpha = 255;
    cusor_step = -5;

    trig_l_held = false;
    trig_r_held = false;

    /* Initialize marquee state */
    marquee_reset();
    marquee_last_selected = -1;

    printf("FOLDERS: Setup complete, %d items\n", list_len);
}

FUNCTION(UI_NAME, drawOP) {
    draw_bg_layers();
}

FUNCTION(UI_NAME, drawTR) {
    /* Always draw the game list and artwork first */
    draw_gamelist();
    draw_gameart();

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
        default:
        case DRAW_UI: {
            handle_input_ui(input_current);
        } break;
    }

    navigate_timeout--;
}
