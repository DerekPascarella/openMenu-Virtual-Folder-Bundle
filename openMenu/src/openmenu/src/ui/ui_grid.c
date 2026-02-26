/*
 * File: ui_grid.c
 * Project: ui
 * File Created: Saturday, 5th June 2021 10:07:38 pm
 * Author: Hayden Kowalchuk
 * -----
 * Copyright (c) 2021 Hayden Kowalchuk, Hayden Kowalchuk
 * License: BSD 3-clause "New" or "Revised" License,
 * http://www.opensource.org/licenses/BSD-3-Clause
 */
#include <stdbool.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include <backend/gd_item.h>
#include <backend/gd_list.h>

#include "dc/input.h"
#include "texture/txr_manager.h"
#include "ui/animation.h"
#include "ui/draw_prototypes.h"
#include "ui/font_prototypes.h"
#include "ui/ui_common.h"
#include "ui/ui_menu_credits.h"

#include "ui/ui_grid.h"

/* Keyboard scancodes for quick-jump (from KOS keyboard.h) */
#define KBD_KEY_A            0x04
#define KBD_KEY_Z            0x1d
#define KBD_KEY_1            0x1e
#define KBD_KEY_9            0x26
#define KBD_KEY_0            0x27
#define KBD_MOD_LSHIFT       0x02
#define KBD_MOD_RSHIFT       0x20

/* Scaling */
#define X_SCALE_4_3          ((float)1.0f)
#define X_SCALE_16_9         ((float)0.74941452f)

/* List managment */
#define INPUT_TIMEOUT        (10)
#define FOCUSED_HIRES_FRAMES (60 * 1) /* 1 second load in */
#define ANIM_FRAMES          (15)

/* Tile parameters */
/* Basic Info */
static float X_SCALE;
static short SCR_WIDTH;
static short SCR_HEIGHT;
static short TILE_AREA_WIDTH;
static short TILE_AREA_HEIGHT;
static short COLUMNS;
static short ROWS;
/* Calculated params */
static short GUTTER_SIDE;
static short GUTTER_TOP;
static short HORIZONTAL_SPACING;
static short VERTICAL_SPACING;
static short HIGHLIGHT_OVERHANG;
static short TILE_SIZE_X;
static short TILE_SIZE_Y;

/* Helpers */
static inline int
HIGHLIGHT_X_POS(int col) {
    return (GUTTER_SIDE - HIGHLIGHT_OVERHANG + ((HORIZONTAL_SPACING + TILE_SIZE_X) * (col))) * X_SCALE;
}

static inline int
HIGHLIGHT_Y_POS(int row) {
    return (GUTTER_TOP - HIGHLIGHT_OVERHANG + ((VERTICAL_SPACING + TILE_SIZE_Y) * (row)));
}

static inline int
TILE_X_POS(int col) {
    return (GUTTER_SIDE + ((HORIZONTAL_SPACING + TILE_SIZE_X) * (col))) * X_SCALE;
}

static inline int
TILE_Y_POS(int row) {
    return (GUTTER_TOP + ((VERTICAL_SPACING + TILE_SIZE_Y) * (row)));
}

static int screen_row = 0;
static int screen_column = 0;
static int current_starting_index = 0;
static int navigate_timeout = INPUT_TIMEOUT;
static int frames_focused = 0;

static bool boxart_button_held = false;

static bool direction_last = false;
static bool direction_current = false;
#define direction_held (direction_last & direction_current)

static vec2d pos_highlight = {.x = 0.f, .y = 0.f};
static anim2d anim_highlight;

static anim2d anim_large_art_pos;
static anim2d anim_large_art_scale;

/* For drawing */
static image txr_icon_list[/*ROWS * COLUMNS*/ 4 * 3 /* Assume the worst */]; /* Lower list of 9 or 12 icons */
static image txr_focus;
static image txr_highlight; /* Highlight square */
static image txr_bg_left, txr_bg_right;

extern image img_empty_boxart;
extern image img_dir_boxart;

/* Our actual gdemu items */
static const gd_item** list_current;
static int list_len;

static theme_region* region_themes;
static theme_custom* custom_themes;
static int num_default_themes;
static int num_custom_themes;
static theme_color* current_theme_colors;

static region region_current = REGION_NTSC_U;
static enum draw_state draw_current = DRAW_UI;
static bool serial_vmu_boot_checked = false;

static void
recalculate_aspect(CFG_ASPECT aspect) {
    if (aspect == ASPECT_NORMAL) {
        SCR_WIDTH = (640);
        X_SCALE = (X_SCALE_4_3);
        TILE_AREA_WIDTH = (440);
        TILE_AREA_HEIGHT = (380);
        COLUMNS = (3);
        ROWS = (3);
    } else {
        X_SCALE = (X_SCALE_16_9);
        SCR_WIDTH = (854);
        TILE_AREA_WIDTH = (600);
        TILE_AREA_HEIGHT = (380);
        COLUMNS = (4);
        ROWS = (3);
    }
    SCR_HEIGHT = (480);
    GUTTER_SIDE = ((SCR_WIDTH - TILE_AREA_WIDTH) / 2);
    GUTTER_TOP = ((SCR_HEIGHT - TILE_AREA_HEIGHT) / 2);
    HORIZONTAL_SPACING = (40);
    VERTICAL_SPACING = (10);
    HIGHLIGHT_OVERHANG = (4);
    TILE_SIZE_X = (((TILE_AREA_WIDTH - ((COLUMNS - 1) * HORIZONTAL_SPACING)) / COLUMNS));
    TILE_SIZE_Y = ((TILE_AREA_HEIGHT - ((ROWS - 1) * VERTICAL_SPACING)) / ROWS);
}

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

static inline int
current_selected(void) {
    return current_starting_index + (screen_row * COLUMNS) + (screen_column);
}

static void
draw_large_art(void) {
    if (anim_active(&anim_large_art_scale.time)) {
        txr_get_large(list_current[current_selected()]->product, &txr_focus);
        if (txr_focus.texture == img_empty_boxart.texture
            || !strncmp(list_current[current_selected()]->disc, "DIR", 3)) {
            /* Only draw if large is present */
            return;
        }
        /* Always draw on top */
        float z = z_get();
        z_set(512.0f);
        draw_draw_image_centered(anim_large_art_pos.cur.x, anim_large_art_pos.cur.y, anim_large_art_scale.cur.x,
                                 anim_large_art_scale.cur.y, COLOR_WHITE, &txr_focus);
        z_set(z);
    }
}

static void
setup_highlight_animation(void) {
    float start_x = pos_highlight.x;
    float start_y = pos_highlight.y;
    if (anim_active(&anim_highlight.time)) {
        start_x = anim_highlight.cur.x;
        start_y = anim_highlight.cur.y;
    }
    anim_highlight.start.x = start_x;
    anim_highlight.start.y = start_y;
    anim_highlight.end.x = (float)HIGHLIGHT_X_POS(screen_column);
    anim_highlight.end.y = (float)HIGHLIGHT_Y_POS(screen_row);
    anim_highlight.time.frame_now = 0;
    anim_highlight.time.frame_len = ANIM_FRAMES;
    anim_highlight.time.active = true;
}

static void
draw_static_highlight(int width, int height) {
    draw_draw_image(pos_highlight.x, pos_highlight.y, width, height, current_theme_colors->highlight_color,
                    &txr_highlight);
}

static void
draw_animated_highlight(int width, int height) {
    /* Always draw on top */
    float z = z_get();
    z_set(200.0f);
    draw_draw_image(anim_highlight.cur.x, anim_highlight.cur.y, width, height, current_theme_colors->highlight_color,
                    &txr_highlight);
    z_set(z);
}

static void
draw_grid_boxes(void) {
    for (int row = 0; row < ROWS; row++) {
        for (int column = 0; column < COLUMNS; column++) {
            int idx = (row * COLUMNS) + column;

            if (current_starting_index + idx < 0) {
                continue;
            }
            if (current_starting_index + idx >= list_len) {
                break;
            }
            float x_pos = GUTTER_SIDE + ((HORIZONTAL_SPACING + TILE_SIZE_X) * column); /* 100 + ((40 + 120)*{0,1,2}) */
            float y_pos = GUTTER_TOP + ((VERTICAL_SPACING + TILE_SIZE_Y) * row);       /* 20 + ((10 + 120)*{0,1,2}) */

            x_pos *= X_SCALE;

            if (!strncmp(list_current[current_starting_index + idx]->disc, "DIR", 3)
                && !strncmp(list_current[current_starting_index + idx]->name, "Back", 4)) {
                txr_icon_list[idx].texture = img_dir_boxart.texture;
                txr_icon_list[idx].width = img_dir_boxart.width;
                txr_icon_list[idx].height = img_dir_boxart.height;
                txr_icon_list[idx].format = img_dir_boxart.format;
            } else {
                txr_get_small(list_current[current_starting_index + idx]->product, &txr_icon_list[idx]);
            }
            draw_draw_image((int)x_pos, (int)y_pos, TILE_SIZE_X * X_SCALE, TILE_SIZE_Y, COLOR_WHITE,
                            &txr_icon_list[idx]);

            /* Highlight */
            if ((current_starting_index + idx) == current_selected()) {
                if (anim_alive(&anim_highlight.time)) {
                    draw_animated_highlight((TILE_SIZE_X + (HIGHLIGHT_OVERHANG * 2)) * X_SCALE,
                                            TILE_SIZE_Y + (HIGHLIGHT_OVERHANG * 2));
                } else {
                    pos_highlight.x = x_pos - (HIGHLIGHT_OVERHANG * X_SCALE);
                    pos_highlight.y = y_pos - (HIGHLIGHT_OVERHANG);
                    draw_static_highlight((TILE_SIZE_X + (HIGHLIGHT_OVERHANG * 2)) * X_SCALE,
                                          TILE_SIZE_Y + (HIGHLIGHT_OVERHANG * 2));
                }
            }
        }
    }

    /* Get multidisc settings */
    int hide_multidisc = sf_multidisc[0];

    for (int row = 0; row < ROWS; row++) {
        for (int column = 0; column < COLUMNS; column++) {
            int idx = (row * COLUMNS) + column;

            if (current_starting_index + idx < 0) {
                continue;
            }
            if (current_starting_index + idx >= list_len) {
                break;
            }

            int disc_set = gd_item_disc_total(list_current[current_selected()]->disc);
            /* Treat as single disc if folder or no product code */
            if (!strncmp(list_current[current_selected()]->disc, "DIR", 3)
                || list_current[current_selected()]->product[0] == '\0') {
                disc_set = 1;
            }

            /* Disc # above name, position 316x33 */
            if (((current_starting_index + idx) == current_selected()) && (hide_multidisc) && (disc_set > 1)) {
                float x_pos = GUTTER_SIDE + ((HORIZONTAL_SPACING + TILE_SIZE_X) * column) + 4;
                float y_pos = GUTTER_TOP + ((VERTICAL_SPACING + TILE_SIZE_Y) * row) + TILE_SIZE_Y - 24;
                int disc_set = gd_item_disc_total(list_current[current_selected()]->disc);

                x_pos *= X_SCALE;

                /* Draw multiple discs and how many */
                draw_draw_quad(x_pos, y_pos, TILE_SIZE_X * X_SCALE * 0.5f, 28, current_theme_colors->menu_bkg_color);
                char disc_str[12];
                snprintf(disc_str, 11, "%d Discs", disc_set);
                font_bmf_begin_draw();
                font_bmf_set_height(24);
                font_bmf_draw_sub(x_pos + 8, y_pos + 2, current_theme_colors->text_color, disc_str);
            }
        }
    }

    /* If focused, draw large cover art */
    draw_large_art();
}

static void
draw_game_title(void) {
    font_bmf_begin_draw();
    if (list_len <= 0) {
        font_bmf_draw_centered_auto_size((SCR_WIDTH / 2) * X_SCALE, 434, current_theme_colors->text_color,
                                         "Empty Game List", (SCR_WIDTH - (10 * 2)) * X_SCALE);
        return;
    }
    font_bmf_draw_centered_auto_size((SCR_WIDTH / 2) * X_SCALE, 434, current_theme_colors->text_color,
                                     list_current[current_selected()]->name, (SCR_WIDTH - (10 * 2)) * X_SCALE);
}

static void
update_time(void) {
    if (anim_alive(&anim_highlight.time)) {
        anim_tick(&anim_highlight.time);
        anim_update_2d(&anim_highlight);
    }
    if (boxart_button_held && anim_alive(&anim_large_art_scale.time)) {
        /* Update scale and position */
        anim_tick(&anim_large_art_pos.time);
        anim_update_2d(&anim_large_art_pos);

        anim_tick(&anim_large_art_scale.time);
        anim_update_2d(&anim_large_art_scale);
    }
    if (!boxart_button_held && anim_alive(&anim_large_art_scale.time)) {
        /* Rewind Animation update scale and position */
        anim_tick_backward(&anim_large_art_pos.time);
        anim_update_2d(&anim_large_art_pos);

        anim_tick_backward(&anim_large_art_scale.time);
        anim_update_2d(&anim_large_art_scale);
    }
}

static void
menu_row_up(void) {
    screen_row--;
    if (screen_row < 0) {
        screen_row = 0;
        current_starting_index -= COLUMNS;
        if (current_starting_index < 0) {
            current_starting_index = 0;
        }
    }
}

static void
menu_row_down(void) {
    screen_row++;
    if (screen_row >= ROWS) {
        screen_row = ROWS - 1;
        current_starting_index += COLUMNS;
        if (current_selected() > list_len) {
            current_starting_index -= COLUMNS;
        }
    }
    while (current_selected() >= list_len) {
        screen_column--;
        if (screen_column < 0) {
            screen_column = COLUMNS - 1;
            menu_row_up();
        }
    }
}

static void
kill_large_art_animation(void) {
    anim_large_art_pos.time.active = false;
    anim_large_art_scale.time.active = false;
}

static void
menu_up(int amount) {
    if (direction_held && navigate_timeout > 0) {
        return;
    }

    while (amount--) {
        menu_row_up();
    }

    setup_highlight_animation();
    kill_large_art_animation();

    frames_focused = 0;
    navigate_timeout = INPUT_TIMEOUT;
}

static void
menu_down(int amount) {
    if (direction_held && navigate_timeout > 0) {
        return;
    }

    while (amount--) {
        menu_row_down();
    }

    setup_highlight_animation();
    kill_large_art_animation();

    frames_focused = 0;
    navigate_timeout = INPUT_TIMEOUT;
}

static void
menu_left(void) {
    if (direction_held && navigate_timeout > 0) {
        return;
    }

    screen_column--;
    if (current_selected() < 0) {
        // screen_column = 0;
        int temp = (list_len - (COLUMNS * ROWS)) / COLUMNS;
        if ((list_len - (COLUMNS * ROWS)) % COLUMNS) {
            temp++;
        }
        current_starting_index = temp * COLUMNS;

        if (current_starting_index < 0 || list_len <= (COLUMNS * ROWS)) {
            current_starting_index = 0;
        }

        screen_row = (list_len - 1 - current_starting_index) / COLUMNS;
        screen_column = (list_len - 1 - current_starting_index) % COLUMNS;
    }
    if (screen_column < 0) {
        screen_column = COLUMNS - 1;
        menu_row_up();
    }

    setup_highlight_animation();
    kill_large_art_animation();

    frames_focused = 0;
    navigate_timeout = INPUT_TIMEOUT;
}

static void
menu_right(void) {
    if (direction_held && navigate_timeout > 0) {
        return;
    }
    screen_column++;
    if (current_selected() >= list_len) {
        // screen_column--;
        screen_row = screen_column = current_starting_index = 0;
    }
    if (screen_column >= COLUMNS) {
        screen_column = 0;
        menu_row_down();
    }

    setup_highlight_animation();
    kill_large_art_animation();

    frames_focused = 0;
    navigate_timeout = INPUT_TIMEOUT;
}

static void
menu_cb(void) {
    if (list_len <= 0) {
        return;
    }

    /* CodeBreaker only available for regular games */
    if (strcmp(list_current[current_selected()]->type, "game") != 0) {
        return;
    }

    start_cb = 0;
    draw_current = DRAW_CODEBREAKER;
    cb_menu_setup(&draw_current, current_theme_colors, &navigate_timeout, current_theme_colors->menu_highlight_color);
}

static void
run_cb(void) {
    /* grab the disc number and if there is more than one */
    int disc_set = gd_item_disc_total(list_current[current_selected()]->disc);

    /* Get multidisc settings */
    int hide_multidisc = sf_multidisc[0];

    /* prepare to show multidisc chooser menu (only if product code exists) */
    if (hide_multidisc && (disc_set > 1) && list_current[current_selected()]->product[0] != '\0') {
        cb_multidisc = 1;
        draw_current = DRAW_MULTIDISC;
        popup_setup(&draw_current, current_theme_colors, &navigate_timeout, current_theme_colors->menu_highlight_color);
        list_set_multidisc(list_current[current_selected()]->product);
        return;
    }

    if (sf_serial_vmu[0] != SERIAL_VMU_OFF) {
        set_cur_game_item(list_current[current_selected()]);
        draw_current = DRAW_SERIAL_VMU;
        serial_vmu_setup(&draw_current, current_theme_colors, &navigate_timeout,
                         current_theme_colors->menu_highlight_color);
        serial_vmu_start_restore(list_current[current_selected()], SERIAL_VMU_LAUNCH_CB);
    } else {
        dreamcast_launch_cb(list_current[current_selected()]);
    }
}

static void
menu_accept(void) {
    if (list_len <= 0) {
        return;
    }

    int current_selected_item = current_selected();

    if (!strncmp(list_current[current_selected_item]->disc, "DIR", 3)) {
        if (!strcmp(list_current[current_selected_item]->name, "Back")) {
            switch (list_current[current_selected_item]->product[0]) {
                case 'A': list_set_sort_name(); break;
                case 'G': list_set_sort_genre(); break;
                case 'R': list_set_sort_region(); break;
                default: list_set_sort_default();
            }
        } else {
            list_set_sort_filter(list_current[current_selected_item]->product[0],
                                 list_current[current_selected_item]->slot_num);
        }

        list_current = list_get();
        list_len = list_length();

        screen_column = screen_row = 0;
        current_starting_index = 0;
        draw_current = DRAW_UI;

        navigate_timeout = 3;

        anim_clear(&anim_highlight);
        anim_clear(&anim_large_art_pos);
        anim_clear(&anim_large_art_scale);
        return;
    }

    /* grab the disc number and if there is more than one */
    int disc_set = gd_item_disc_total(list_current[current_selected()]->disc);

    /* Get multidisc settings */
    int hide_multidisc = sf_multidisc[0];

    /* prepare to show multidisc chooser menu (only if product code exists) */
    if (hide_multidisc && (disc_set > 1) && list_current[current_selected()]->product[0] != '\0') {
        cb_multidisc = 0;
        draw_current = DRAW_MULTIDISC;
        popup_setup(&draw_current, current_theme_colors, &navigate_timeout, current_theme_colors->menu_highlight_color);
        list_set_multidisc(list_current[current_selected()]->product);
        return;
    }

    if (!strcmp(list_current[current_selected()]->type, "psx")) {
        if (is_bloom_available()) {
            /* Show PSX launcher choice popup */
            set_cur_game_item(list_current[current_selected()]);
            draw_current = DRAW_PSX_LAUNCHER;
            popup_setup(&draw_current, current_theme_colors, &navigate_timeout,
                        current_theme_colors->menu_highlight_color);
        } else {
            /* No Bloom available, launch directly with Bleem */
            if (sf_serial_vmu[0] != SERIAL_VMU_OFF && strcmp(list_current[current_selected()]->type, "other") != 0) {
                set_cur_game_item(list_current[current_selected()]);
                draw_current = DRAW_SERIAL_VMU;
                serial_vmu_setup(&draw_current, current_theme_colors, &navigate_timeout,
                                 current_theme_colors->menu_highlight_color);
                serial_vmu_start_restore(list_current[current_selected()], SERIAL_VMU_LAUNCH_BLEEM);
            } else {
                bleem_launch(list_current[current_selected()]);
            }
        }
    } else {
        if (sf_serial_vmu[0] != SERIAL_VMU_OFF && strcmp(list_current[current_selected()]->type, "other") != 0) {
            set_cur_game_item(list_current[current_selected()]);
            draw_current = DRAW_SERIAL_VMU;
            serial_vmu_setup(&draw_current, current_theme_colors, &navigate_timeout,
                             current_theme_colors->menu_highlight_color);
            serial_vmu_start_restore(list_current[current_selected()], SERIAL_VMU_LAUNCH_DC);
        } else {
            dreamcast_launch_disc(list_current[current_selected()]);
        }
    }
}

static void
menu_settings(void) {

    draw_current = DRAW_MENU;
    menu_setup(&draw_current, current_theme_colors, &navigate_timeout, current_theme_colors->menu_highlight_color);
}

static void
menu_show_large_art(void) {
    if (list_len <= 0) {
        return;
    }
    if (!boxart_button_held && !anim_active(&anim_large_art_scale.time)) {
        /* Setup positioning */
        {
            anim_large_art_pos.start.x = TILE_X_POS(screen_column) + (TILE_SIZE_X / 2 * X_SCALE);
            anim_large_art_pos.start.y = TILE_Y_POS(screen_row) + (TILE_SIZE_Y / 2);
            anim_large_art_pos.end.x = (SCR_WIDTH / 2 * X_SCALE);
            anim_large_art_pos.end.y =
                (TILE_Y_POS(0) + ((TILE_Y_POS(ROWS - 1) - TILE_Y_POS(0)) / 2)) + (TILE_SIZE_Y / 2);
            anim_large_art_pos.time.frame_now = 0;
            anim_large_art_pos.time.frame_len = 30;
            anim_large_art_pos.time.active = true;
        }
        /* Setup Scaling */
        {
            anim_large_art_scale.start.x = TILE_SIZE_X * X_SCALE;
            anim_large_art_scale.start.y = TILE_SIZE_X;
            anim_large_art_scale.end.x = (TILE_AREA_HEIGHT + HIGHLIGHT_OVERHANG * 2) * X_SCALE;
            anim_large_art_scale.end.y = TILE_AREA_HEIGHT + HIGHLIGHT_OVERHANG * 2;
            anim_large_art_scale.time.frame_now = 0;
            anim_large_art_scale.time.frame_len = 30;
            anim_large_art_scale.time.active = true;
        }
    }
}

static void
menu_exit(void) {

    set_cur_game_item(list_current[current_selected()]);
    draw_current = DRAW_EXIT;
    exit_menu_setup(&draw_current, current_theme_colors, &navigate_timeout, current_theme_colors->menu_highlight_color,
                    0 /* not a folder */);
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

    int current_idx = current_selected();
    for (int offset = 1; offset <= list_len; offset++) {
        int i = (current_idx + offset) % list_len;
        const char* name = list_current[i]->name;
        char first_char = name[0];

        if (first_char == target_lower || first_char == target_upper) {
            /* Found a match - calculate grid position */
            int items_per_page = ROWS * COLUMNS;

            /* Calculate which page this item is on */
            int page = i / items_per_page;
            current_starting_index = page * items_per_page;

            /* clamp to last visible page */
            int max_start = list_len - items_per_page;
            if (max_start < 0) {
                max_start = 0;
            }
            if (current_starting_index > max_start) {
                current_starting_index = max_start;
            }

            /* Calculate row and column within the visible area */
            int idx_in_view = i - current_starting_index;
            screen_row = idx_in_view / COLUMNS;
            screen_column = idx_in_view % COLUMNS;

            /* Clear animations */
            anim_clear(&anim_highlight);
            anim_clear(&anim_large_art_pos);
            anim_clear(&anim_large_art_scale);

            navigate_timeout = INPUT_TIMEOUT;
            frames_focused = 0;
            return;
        }
    }
    /* No match found - cursor stays where it is */
}

/* Base UI Methods */

FUNCTION(UI_NAME, init) {
    texman_clear();
    txr_empty_small_pool();
    txr_empty_large_pool();
    /* Set region from preferences */
    region_current = sf_region[0];
    recalculate_aspect(sf_aspect[0]);

    /* Get the current themes, original + custom */
    region_themes = theme_get_default(sf_aspect[0], &num_default_themes);
    custom_themes = theme_get_custom(&num_custom_themes);

    /* Enable custom theme if needed */
    int use_custom_theme = sf_custom_theme[0];
    if (use_custom_theme) {
        int custom_theme_num = sf_custom_theme_num[0];
        region_current = REGION_END + 1 + custom_theme_num;
    }

    /* on user for now, may change */
    unsigned int temp = texman_create();
    draw_load_texture_buffer("EMPTY.PVR", &img_empty_boxart, texman_get_tex_data(temp));
    texman_reserve_memory(img_empty_boxart.width, img_empty_boxart.height, 2 /* 16Bit */);

    temp = texman_create();
    draw_load_texture_buffer("DIR.PVR", &img_dir_boxart, texman_get_tex_data(temp));
    texman_reserve_memory(img_dir_boxart.width, img_dir_boxart.height, 2 /* 16Bit */);

    temp = texman_create();
    draw_load_texture_buffer("THEME/SHARED/HIGHLIGHT.PVR", &txr_highlight, texman_get_tex_data(temp));
    texman_reserve_memory(txr_highlight.width, txr_highlight.height, 2 /* 16Bit */);

    if ((int)region_current >= num_default_themes) {
        region_current -= num_default_themes;
        current_theme_colors = &custom_themes[region_current].colors;

        temp = texman_create();
        draw_load_texture_buffer(custom_themes[region_current].bg_left, &txr_bg_left, texman_get_tex_data(temp));
        texman_reserve_memory(txr_bg_left.width, txr_bg_left.height, 2 /* 16Bit */);

        temp = texman_create();
        draw_load_texture_buffer(custom_themes[region_current].bg_right, &txr_bg_right, texman_get_tex_data(temp));
        texman_reserve_memory(txr_bg_right.width, txr_bg_right.height, 2 /* 16Bit */);
    } else {
        current_theme_colors = &region_themes[region_current].colors;

        temp = texman_create();
        draw_load_texture_buffer(region_themes[region_current].bg_left, &txr_bg_left, texman_get_tex_data(temp));
        texman_reserve_memory(txr_bg_left.width, txr_bg_left.height, 2 /* 16Bit */);

        temp = texman_create();
        draw_load_texture_buffer(region_themes[region_current].bg_right, &txr_bg_right, texman_get_tex_data(temp));
        texman_reserve_memory(txr_bg_right.width, txr_bg_right.height, 2 /* 16Bit */);
    }

    font_bmf_init("FONT/BASILEA.FNT", "FONT/BASILEA_W.PVR", sf_aspect[0]);

    /* printf("Texture scratch free: %d/%d KB (%d/%d bytes)\n", texman_get_space_available() / 1024,
           TEXMAN_BUFFER_SIZE / 1024, texman_get_space_available(), TEXMAN_BUFFER_SIZE); */
}

static void
handle_input_ui(enum control input) {
    direction_last = direction_current;
    direction_current = false;
    boxart_button_held = false;
    switch (input) {
        case LEFT:
            direction_current = true;
            menu_left();
            break;
        case RIGHT:
            direction_current = true;
            menu_right();
            break;
        case UP:
            direction_current = true;
            menu_up(1);
            break;
        case DOWN:
            direction_current = true;
            menu_down(1);
            break;
        case TRIG_L:
            direction_current = true;
            menu_up(ROWS);
            break;
        case TRIG_R:
            direction_current = true;
            menu_down(ROWS);
            break;
        case A: menu_accept(); break;
        case START: menu_settings(); break;
        case Y: menu_exit(); break;
        case X:
            menu_show_large_art();
            boxart_button_held = true;
            break;
        case B: menu_cb(); break;

        /* Always nothing */
        case NONE:
        default: break;
    }
    if (screen_row < 0) {
        screen_row = 0;
    }
    if (screen_column < 0) {
        screen_column = 0;
    }

    /* Keyboard quick-jump: Shift+Letter/Number */
    handle_keyboard_quickjump();
}

/* Reset variables sensibly */
FUNCTION(UI_NAME, setup) {
    list_current = list_get();
    list_len = list_length();

    screen_column = screen_row = 0;
    current_starting_index = 0;
    draw_current = DRAW_UI;

    navigate_timeout = 3;

    anim_clear(&anim_highlight);
    anim_clear(&anim_large_art_pos);
    anim_clear(&anim_large_art_scale);
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

FUNCTION(UI_NAME, drawOP) {
    draw_bg_layers();

    switch (draw_current) {
        case DRAW_MENU: {
            /* Menu on top */
            draw_menu_op();
        } break;
        case DRAW_CREDITS: {
            /* Credits on top */
            draw_credits_op();
        } break;
        case DRAW_MULTIDISC: {
            /* Multidisc choice on top */
            draw_multidisc_op();
        } break;
        case DRAW_EXIT: {
            /* Exit popup on top */
            draw_exit_op();
        } break;
        case DRAW_CODEBREAKER: {
            /* CodeBreaker popup on top */
            draw_codebreaker_op();
        } break;
        case DRAW_PSX_LAUNCHER: {
            /* PSX launcher popup on top */
            draw_psx_launcher_op();
        } break;
        case DRAW_SAVELOAD: {
            /* Save/Load popup on top */
            draw_saveload_op();
        } break;
        /* COMPACTION_TEST_START */
        case DRAW_COMPACTION_TEST: {
            draw_compaction_test_op();
        } break;
        /* COMPACTION_TEST_END */
        default:
        case DRAW_UI: {
            /* always drawn */
        } break;
    }
}

FUNCTION(UI_NAME, drawTR) {
    update_time();

    draw_grid_boxes();
    draw_game_title();

    /* Check for pending Serial VMU backup on first frame */
    if (!serial_vmu_boot_checked && draw_current == DRAW_UI) {
        serial_vmu_boot_checked = true;
        serial_vmu_check_boot_backup(&draw_current, current_theme_colors, &navigate_timeout,
                                     current_theme_colors->menu_highlight_color);
    }

    switch (draw_current) {
        case DRAW_MENU: {
            /* Menu on top */
            draw_menu_tr();
        } break;
        case DRAW_CREDITS: {
            /* Credits on top */
            draw_credits_tr();
        } break;
        case DRAW_MULTIDISC: {
            /* Multidisc choice on top */
            draw_multidisc_tr();
        } break;
        case DRAW_EXIT: {
            /* Exit popup on top */
            draw_exit_tr();
        } break;
        case DRAW_CODEBREAKER: {
            /* CodeBreaker popup on top */
            draw_codebreaker_tr();
        } break;
        case DRAW_PSX_LAUNCHER: {
            /* PSX launcher popup on top */
            draw_psx_launcher_tr();
        } break;
        case DRAW_SAVELOAD: {
            /* Save/Load popup on top */
            draw_saveload_tr();
        } break;
        /* COMPACTION_TEST_START */
        case DRAW_COMPACTION_TEST: {
            draw_compaction_test_tr();
        } break;
        /* COMPACTION_TEST_END */
        case DRAW_SERIAL_VMU: {
            draw_serial_vmu_op();
            draw_serial_vmu_tr();
        } break;
        default:
        case DRAW_UI: {
            /* always drawn */
        } break;
    }
}
