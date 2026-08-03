/*
 * File: ui_line_desc.c
 * Project: ui
 * File Created: Wednesday, 19th May 2021 9:08:07 pm
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

#include <backend/db_list.h>
#include <backend/gd_item.h>
#include <backend/gd_list.h>

#include "backend/last_game.h"
#include "dc/input.h"
#include "texture/txr_manager.h"
#include "ui/animation.h"
#include "ui/draw_prototypes.h"
#include "ui/font_prototypes.h"
#include "ui/ui_common.h"
#include "ui/ui_menu_credits.h"

#include "ui/ui_line_desc.h"

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
#define ICON_BASE_SIZE       ((int)68)

/* List managment */
#define INPUT_TIMEOUT        (10)
#define FOCUSED_HIRES_FRAMES (60 * 1) /* 1 second load in */

/* Tile parameters */
/* Basic Info */
static float X_SCALE;
static float FONT_SYNOP_SIZE;
static short SCR_WIDTH;
static short SCR_HEIGHT;
static short ICON_SPACING;
static short ICON_AREA_WIDTH;
static short ICON_AREA_UNDERHANG;
static short NUM_ICONS;
static short HIGHLIGHT_OVERHANG;
/* Calculated params */
static short ICON_SIZE_X;
static short ICON_SIZE_Y;

static int current_selected_item;
static int navigate_timeout;
static int frames_focused;

static bool boxart_button_held = false;

static anim2d anim_large_art_pos;
static anim2d anim_large_art_scale;

db_item* current_meta;

/* For drawing */
static image txr_icon_list[16]; /* Lower list of 9 icons */
static image txr_focus;         /* current selected item, either lowres or hires */
static image txr_highlight;     /* Highlight square*/
static image txr_bg_left, txr_bg_right;
static image txr_icons_white /*, txr_icons_black*/;
static image* txr_icons_current;

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
        ICON_SPACING = (8);
        ICON_AREA_WIDTH = (684); // 640+22pixel over hang on edges
        ICON_AREA_UNDERHANG = (-24);
        NUM_ICONS = (9);
        FONT_SYNOP_SIZE = (16.0f);
    } else {
        X_SCALE = (X_SCALE_16_9);
        SCR_WIDTH = (854);
        ICON_SPACING = (8);
        ICON_AREA_WIDTH = (SCR_WIDTH); // 854 no over hang on edges
        ICON_AREA_UNDERHANG = (10);
        NUM_ICONS = (11);
        FONT_SYNOP_SIZE = (18.0f);
    }
    SCR_HEIGHT = (480);
    HIGHLIGHT_OVERHANG = (4);
    ICON_SIZE_X = (ICON_BASE_SIZE);
    ICON_SIZE_Y = (ICON_BASE_SIZE);
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

static void
draw_big_box(void) {
    const int y_pos = 72;
    const int width = (232) * X_SCALE;
    const int height = 232;
    const int right_edge = ((SCR_WIDTH / 2) - (SCR_WIDTH * 0.03125)) * X_SCALE;
    // centers (640x480) = 320, (854x480) = 427, 50%
    // 4:3  (640x480) right edge at 300, 20 pixels left of center or 3.125%
    // 16:9 (854x480) right edge at 400, 26 pixels left of center or 3.125%
    draw_draw_image(right_edge - width, y_pos, width, height, COLOR_WHITE, &txr_focus);
}

static void
draw_large_art(void) {
    if (anim_active(&anim_large_art_scale.time)) {
        txr_get_large(list_current[current_selected_item]->product, &txr_focus);
        if (txr_focus.texture == img_empty_boxart.texture
            || !strncmp(list_current[current_selected_item]->disc, "DIR", 3)) {
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
update_time(void) {
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
kill_large_art_animation(void) {
    anim_large_art_pos.time.active = false;
    anim_large_art_scale.time.active = false;
}

static void
menu_show_large_art(void) {
    if (list_len <= 0) {
        return;
    }
    if (!boxart_button_held && !anim_active(&anim_large_art_scale.time)) {
        const int big_box_y = 72;
        const int big_box_width = (232) * X_SCALE;
        const int big_box_height = 232;
        const int big_box_right_edge = ((SCR_WIDTH / 2) - (SCR_WIDTH * 0.03125)) * X_SCALE;
        /* Setup positioning */
        {
            anim_large_art_pos.start.x = big_box_right_edge - (big_box_width / 2);
            anim_large_art_pos.start.y = big_box_y + (big_box_height / 2);
            anim_large_art_pos.end.x = (SCR_WIDTH / 2) * X_SCALE;
            anim_large_art_pos.end.y = 200;
            anim_large_art_pos.time.frame_now = 0;
            anim_large_art_pos.time.frame_len = 30;
            anim_large_art_pos.time.active = true;
        }
        /* Setup Scaling */
        {
            anim_large_art_scale.start.x = big_box_width;
            anim_large_art_scale.start.y = big_box_height;
            anim_large_art_scale.end.x = (300) * X_SCALE;
            anim_large_art_scale.end.y = 300;
            anim_large_art_scale.time.frame_now = 0;
            anim_large_art_scale.time.frame_len = 30;
            anim_large_art_scale.time.active = true;
        }
    }
}

static void
draw_small_boxes(void) {
    int i;
    int num_icons = NUM_ICONS;
    int starting_icon_idx = current_selected_item - (NUM_ICONS / 2);
    float x_start = ICON_AREA_UNDERHANG;
    float y_pos = 350.0f;

    /* possible change how many we draw based on if we are not quite at the 5th
     * item in the list */
    if (current_selected_item < (NUM_ICONS / 2)) {
        num_icons = (NUM_ICONS / 2) + current_selected_item;
        x_start += (((NUM_ICONS / 2)) - current_selected_item) * (ICON_SIZE_X + ICON_SPACING);
        starting_icon_idx = 0;
        num_icons++;
    }

    for (i = 0; (i < num_icons) && (i + starting_icon_idx < list_len); i++) {
        if (!strncmp(list_current[starting_icon_idx + i]->disc, "DIR", 3)
            && !strncmp(list_current[starting_icon_idx + i]->name, "Back", 4)) {
            txr_icon_list[i].texture = img_dir_boxart.texture;
            txr_icon_list[i].width = img_dir_boxart.width;
            txr_icon_list[i].height = img_dir_boxart.height;
            txr_icon_list[i].format = img_dir_boxart.format;
        } else {
            txr_get_small(list_current[starting_icon_idx + i]->product, &txr_icon_list[i]);
        }
        draw_draw_image((x_start + (ICON_SIZE_X + ICON_SPACING) * i) * X_SCALE, y_pos, ICON_SIZE_X * X_SCALE,
                        ICON_SIZE_Y, COLOR_WHITE, &txr_icon_list[i]);
    }
}

static void
draw_small_box_highlight(void) {
    float x_start = ICON_AREA_UNDERHANG;
    float y_pos = 350.0f;
    int highlighted_icon = (NUM_ICONS) / 2; // Middle

    draw_draw_image((x_start + (ICON_SIZE_X + ICON_SPACING) * highlighted_icon - 4.0f) * X_SCALE, y_pos - 4.0f,
                    (ICON_SIZE_X + 8) * X_SCALE, ICON_SIZE_Y + 8, current_theme_colors->highlight_color,
                    &txr_highlight);
}

static void
draw_game_meta(void) {
    /* grab the disc number and if there is more than one */
    int disc_num = gd_item_disc_num(list_current[current_selected_item]->disc);
    int disc_set = gd_item_disc_total(list_current[current_selected_item]->disc);

    /* Treat as single disc if PSX, folder, or no product code */
    if (!strcmp(list_current[current_selected_item]->type, "psx")
        || !strncmp(list_current[current_selected_item]->disc, "DIR", 3)
        || list_current[current_selected_item]->product[0] == '\0') {
        disc_num = disc_set = 1;
    }

    /* Get multidisc settings */
    int hide_multidisc = sf_multidisc[0];

    /* Game Title */
    font_bmf_begin_draw();
    font_bmf_set_height(16.0f);
    if (list_len <= 0) {
        font_bmf_draw_auto_size((SCR_WIDTH / 2 - 4) * X_SCALE, 92 - 20, current_theme_colors->text_color,
                                "Empty Game List", (SCR_WIDTH / 2 - 10) * X_SCALE);
        return;
    }
    font_bmf_draw_auto_size((SCR_WIDTH / 2 - 4) * X_SCALE, 92 - 20, current_theme_colors->text_color,
                            list_current[current_selected_item]->name, (SCR_WIDTH / 2 - 10) * X_SCALE);

    /* Disc # above name, position 316x33 */
    {
        if (disc_set > 1) {
            if (!hide_multidisc) {
                char disc_str[12];
                snprintf(disc_str, 11, "Disc %d", disc_num);
                font_bmf_draw_sub(316 + 22 + 4, 36, current_theme_colors->text_color, disc_str);
            } else {
                /* Draw multiple discs and how many */
                char disc_str[12];
                snprintf(disc_str, 11, "%d Discs", disc_set);
                font_bmf_draw_sub(316 + 22 + 4, 36, current_theme_colors->text_color, disc_str);
            }
        }
    }

    const char* synopsis;
    if (current_meta) {
        /* success! */
        int slots[] = {396, 458, 534};
        int curr_slot = 0;

        synopsis = current_meta->description;
        font_bmf_set_height(FONT_SYNOP_SIZE);
        font_bmf_draw_sub_wrap(316, 136 - 20 - 8, current_theme_colors->text_color, synopsis, 640 - 316 - 10);

        font_bmf_set_height(14.0f);
        font_bmf_draw_centered(326 + (20 / 2), 282 + 12, current_theme_colors->text_color,
                               db_format_nplayers_str(current_meta->num_players));

        if (current_meta->vmu_blocks) {
            font_bmf_draw_centered(slots[curr_slot++] + (16 / 2), 282 + 12, current_theme_colors->text_color,
                                   db_format_vmu_blocks_str(current_meta->vmu_blocks));
        }

        if (current_meta->accessories & ACCESORIES_JUMP_PACK) {
            font_bmf_draw_centered(slots[curr_slot++] + 2 + (34 / 2), 282 + 12, current_theme_colors->text_color,
                                   "Jump Pack");
        }

        if (current_meta->accessories & (ACCESORIES_BBA | ACCESORIES_MODEM)) {
            font_bmf_draw_centered(slots[curr_slot++] + (22 / 2), 282 + 12, current_theme_colors->text_color, "Modem");
        }
        // reset slot
        curr_slot = 0;

        /* Draw Icons */
        {
            // 318x282
            const dimen_RECT uv_controller = {.x = 0, .y = 0, .w = 42, .h = 42};
            draw_draw_sub_image(326, 254 + 12, 20 * X_SCALE, 20, current_theme_colors->icon_color, txr_icons_current,
                                &uv_controller); // 20x20
        }

        if (current_meta->vmu_blocks) {
            // 388x282
            const dimen_RECT uv_vmu = {.x = 42, .y = 0, .w = 26, .h = 42};
            draw_draw_sub_image(slots[curr_slot++], 254 + 12, 16 * X_SCALE, 22, current_theme_colors->icon_color,
                                txr_icons_current, &uv_vmu); // 16x22
        }

        if (current_meta->accessories & ACCESORIES_JUMP_PACK) {
            // 448x282
            const dimen_RECT uv_rumble = {.x = 0, .y = 42, .w = 64, .h = 44};
            draw_draw_sub_image(slots[curr_slot++], 254 + 12, 34 * X_SCALE, 24, current_theme_colors->icon_color,
                                txr_icons_current, &uv_rumble); // 34x24
        }

        if (current_meta->accessories & (ACCESORIES_BBA | ACCESORIES_MODEM)) {
            // 524x282
            const dimen_RECT uv_modem = {.x = 0, .y = 86, .w = 42, .h = 42};
            draw_draw_sub_image(slots[curr_slot++], 254 + 12, 22 * X_SCALE, 22, current_theme_colors->icon_color,
                                txr_icons_current, &uv_modem); // 22x22
        }
    }

    if (disc_set > 1) {
        const dimen_RECT uv_disc = {.x = 42, .y = 86, .w = 42, .h = 42};
        draw_draw_sub_image(314, 33, 22, 22, current_theme_colors->text_color, txr_icons_current,
                            &uv_disc); // 22x22
    }
}

static void
menu_changed_item(void) {
    frames_focused = 0;
    db_get_meta(list_current[current_selected_item]->product, &current_meta);
}

static bool
chars_match_for_nav(char c1, char c2) {
    // Check if chars are digits ('0' through '9')
    bool c1_is_digit = (c1 >= '0' && c1 <= '9');
    bool c2_is_digit = (c2 >= '0' && c2 <= '9');

    if (c1_is_digit && c2_is_digit) {
        // Both are digits, they belong to the same numeric block
        return true;
    }

    return (c1 == c2);
}

static int
distance_to_next_letter(void) {
    if (list_current == NULL || list_len <= 0) {
        return 0; // Cannot search an empty or non-existent list
    }

    if (current_selected_item < 0 || current_selected_item >= list_len) {
        return 0; // Invalid starting point
    }

    if (current_selected_item >= list_len - 1) {
        return 1; // Last item is selected, moving forward will wrap around
    }

    char start_char = list_current[current_selected_item]->name[0];
    int distance = 0;

    // Loop forward to find the first item in a different block
    for (int i = current_selected_item + 1; i < list_len; ++i) {
        distance++;
        char current_char = list_current[i]->name[0];
        if (!chars_match_for_nav(start_char, current_char)) {
            return distance;
        }
    }

    return distance;
}

static int
distance_to_previous_letter(void) {
    if (list_current == NULL || list_len <= 0) {
        return 0; // Cannot search an empty or non-existent list
    }

    if (current_selected_item < 0 || current_selected_item >= list_len) {
        return 0; // Invalid starting point
    }

    int anchor = current_selected_item;
    if (current_selected_item == 0) {
        anchor = list_len - 1; // Anchor calculations at the end of the list
    }

    char start_char = list_current[anchor]->name[0];
    int first_diff_block_index = -1; // Index of first item in a different block

    // Find the first item backward that's in a *different* block
    for (int i = anchor - 1; i >= 0; --i) {
        char current_char = list_current[i]->name[0];
        if (!chars_match_for_nav(start_char, current_char)) {
            first_diff_block_index = i;
            break;
        }
    }

    int target_index;
    if (first_diff_block_index == -1) {
        // Case A: No different block found backward. Target the first item.
        target_index = 0;
    } else {
        // Case B: Found item in different block at first_diff_block_index.
        // Find the beginning of the block this item belongs to.
        char prev_block_char = list_current[first_diff_block_index]->name[0];
        target_index = first_diff_block_index; // Start assuming this index is the target

        // Walk backward while items are in the *same* block as prev_block_char
        int j = first_diff_block_index - 1;
        while (j >= 0 && chars_match_for_nav(prev_block_char, list_current[j]->name[0])) {
            target_index = j; // Update target to this earlier index in the same block
            j--;
        }
        // 'target_index' now holds the index of the first item in the previous block
    }

    // Calculate distance from the 'anchor' index to the target index.
    return anchor - target_index;
}

static void
menu_decrement(int amount) {
    if (navigate_timeout > 0) {
        return;
    }
    current_selected_item -= amount;
    if (current_selected_item < 0) {
        current_selected_item = list_len - 1;
    }

    kill_large_art_animation();

    navigate_timeout = INPUT_TIMEOUT;
    menu_changed_item();
}

static void
menu_increment(int amount) {
    if (navigate_timeout > 0) {
        return;
    }
    current_selected_item += amount;
    if (current_selected_item >= list_len) {
        current_selected_item = 0;
    }

    kill_large_art_animation();

    navigate_timeout = INPUT_TIMEOUT;
    menu_changed_item();
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
    cb_menu_setup(&draw_current, &region_themes[region_current].colors, &navigate_timeout,
                  region_themes[region_current].colors.menu_highlight_color);
}

static void
run_cb(void) {
    /* grab the disc number and if there is more than one */
    int disc_set = gd_item_disc_total(list_current[current_selected_item]->disc);

    /* Get multidisc settings */
    int hide_multidisc = sf_multidisc[0];

    /* prepare to show multidisc chooser menu (only if product code exists) */
    if (hide_multidisc && (disc_set > 1) && list_current[current_selected_item]->product[0] != '\0') {
        cb_multidisc = 1;
        draw_current = DRAW_MULTIDISC;
        popup_setup(&draw_current, &region_themes[region_current].colors, &navigate_timeout,
                    region_themes[region_current].colors.menu_highlight_color);
        list_set_multidisc(list_current[current_selected_item]->product);
        return;
    }

    if (sf_serial_vmu[0] != SERIAL_VMU_OFF) {
        set_cur_game_item(list_current[current_selected_item]);
        draw_current = DRAW_SERIAL_VMU;
        serial_vmu_setup(&draw_current, &region_themes[region_current].colors, &navigate_timeout,
                         region_themes[region_current].colors.menu_highlight_color);
        serial_vmu_start_restore(list_current[current_selected_item], SERIAL_VMU_LAUNCH_CB);
    } else {
        dreamcast_launch_cb(list_current[current_selected_item]);
    }
}

static void
menu_accept(void) {
    if (list_len <= 0) {
        return;
    }

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

        current_selected_item = 0;
        frames_focused = 0;
        draw_current = DRAW_UI;

        navigate_timeout = 3;
        menu_changed_item();
        return;
    }

    /* grab the disc number and if there is more than one */
    int disc_set = gd_item_disc_total(list_current[current_selected_item]->disc);

    /* Get multidisc settings */
    int hide_multidisc = sf_multidisc[0];

    /* prepare to show multidisc chooser menu (only if product code exists) */
    if (hide_multidisc && (disc_set > 1) && list_current[current_selected_item]->product[0] != '\0') {
        cb_multidisc = 0;
        draw_current = DRAW_MULTIDISC;
        popup_setup(&draw_current, &region_themes[region_current].colors, &navigate_timeout,
                    region_themes[region_current].colors.menu_highlight_color);
        list_set_multidisc(list_current[current_selected_item]->product);
        return;
    }

    if (!strcmp(list_current[current_selected_item]->type, "psx")) {
        if (is_bloom_available()) {
            /* Show PSX launcher choice popup (Serial VMU intercept happens in menu_accept_psx_launcher) */
            set_cur_game_item(list_current[current_selected_item]);
            draw_current = DRAW_PSX_LAUNCHER;
            popup_setup(&draw_current, &region_themes[region_current].colors, &navigate_timeout,
                        region_themes[region_current].colors.menu_highlight_color);
        } else {
            /* No Bloom available, launch directly with Bleem */
            if (sf_serial_vmu[0] != SERIAL_VMU_OFF && strcmp(list_current[current_selected_item]->type, "other") != 0) {
                set_cur_game_item(list_current[current_selected_item]);
                draw_current = DRAW_SERIAL_VMU;
                serial_vmu_setup(&draw_current, &region_themes[region_current].colors, &navigate_timeout,
                                 region_themes[region_current].colors.menu_highlight_color);
                serial_vmu_start_restore(list_current[current_selected_item], SERIAL_VMU_LAUNCH_BLEEM);
            } else {
                bleem_launch(list_current[current_selected_item]);
            }
        }
    } else {
        if (sf_serial_vmu[0] != SERIAL_VMU_OFF && strcmp(list_current[current_selected_item]->type, "other") != 0) {
            set_cur_game_item(list_current[current_selected_item]);
            draw_current = DRAW_SERIAL_VMU;
            serial_vmu_setup(&draw_current, &region_themes[region_current].colors, &navigate_timeout,
                             region_themes[region_current].colors.menu_highlight_color);
            serial_vmu_start_restore(list_current[current_selected_item], SERIAL_VMU_LAUNCH_DC);
        } else {
            dreamcast_launch_disc(list_current[current_selected_item]);
        }
    }
}

static void
menu_settings(void) {

    draw_current = DRAW_MENU;
    menu_setup(&draw_current, &region_themes[region_current].colors, &navigate_timeout,
               region_themes[region_current].colors.menu_highlight_color);
}

static void
update_data(void) {
    if (!strncmp(list_current[current_selected_item]->disc, "DIR", 3)
        && !strncmp(list_current[current_selected_item]->name, "Back", 4)) {
        txr_focus.texture = img_dir_boxart.texture;
        txr_focus.width = img_dir_boxart.width;
        txr_focus.height = img_dir_boxart.height;
        txr_focus.format = img_dir_boxart.format;
    } else {
        if (frames_focused > FOCUSED_HIRES_FRAMES) {
            txr_get_large(list_current[current_selected_item]->product, &txr_focus);
            if (txr_focus.texture == img_empty_boxart.texture) {
                txr_get_small(list_current[current_selected_item]->product, &txr_focus);
            }
        } else {
            txr_get_small(list_current[current_selected_item]->product, &txr_focus);
        }
    }

    frames_focused++;
}

static void
menu_exit(void) {

    set_cur_game_item(list_current[current_selected_item]);
    draw_current = DRAW_EXIT;
    exit_menu_setup(&draw_current, &region_themes[region_current].colors, &navigate_timeout,
                    region_themes[region_current].colors.menu_highlight_color, 0 /* not a folder */);
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
        char first_char = name[0];

        if (first_char == target_lower || first_char == target_upper) {
            /* Found a match - move cursor there */
            current_selected_item = i;

            /* Clear animations */
            anim_clear(&anim_large_art_pos);
            anim_clear(&anim_large_art_scale);

            navigate_timeout = INPUT_TIMEOUT;
            menu_changed_item();
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
    uint32_t temp = texman_create();
    draw_load_texture_buffer("EMPTY.PVR", &img_empty_boxart, texman_get_tex_data(temp));
    texman_reserve_memory(img_empty_boxart.width, img_empty_boxart.height, 2 /* 16Bit */);

    temp = texman_create();
    draw_load_texture_buffer("DIR.PVR", &img_dir_boxart, texman_get_tex_data(temp));
    texman_reserve_memory(img_dir_boxart.width, img_dir_boxart.height, 2 /* 16Bit */);

    temp = texman_create();
    draw_load_texture_buffer("THEME/SHARED/HIGHLIGHT.PVR", &txr_highlight, texman_get_tex_data(temp));
    texman_reserve_memory(txr_highlight.width, txr_highlight.height, 2 /* 16Bit */);

    temp = texman_create();
    draw_load_texture_buffer("THEME/SHARED/ICON_WHITE.PVR", &txr_icons_white, texman_get_tex_data(temp));
    texman_reserve_memory(txr_icons_white.width, txr_icons_white.height, 2 /* 16Bit */);

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

    txr_icons_current = &txr_icons_white;

#if 0
  temp = texman_create();
  draw_load_texture_buffer("THEME/SHARED/ICON_BLACK.PVR", &txr_icons_black, texman_get_tex_data(temp));
  texman_reserve_memory(txr_icons_black.width, txr_icons_black.height, 2 /* 16Bit */);
#endif

    font_bmf_init("FONT/BASILEA.FNT", "FONT/BASILEA_W.PVR", sf_aspect[0]);

    /* printf("Texture scratch free: %d/%d KB (%d/%d bytes)\n", texman_get_space_available() / 1024,
           TEXMAN_BUFFER_SIZE / 1024, texman_get_space_available(), TEXMAN_BUFFER_SIZE); */
}

static void
handle_input_ui(enum control input) {
    boxart_button_held = false;
    switch (input) {
        case LEFT: menu_decrement(1); break;
        case RIGHT: menu_increment(1); break;
        case UP: menu_decrement(NUM_ICONS / 2); break;
        case DOWN: menu_increment(NUM_ICONS / 2); break;
        case TRIG_L: menu_decrement(distance_to_previous_letter()); break;
        case TRIG_R: menu_increment(distance_to_next_letter()); break;
        case A: menu_accept(); break;
        case START: menu_settings(); break;
        case Y: menu_exit(); break;
        case B: menu_cb(); break;
        case X:
            menu_show_large_art();
            boxart_button_held = true;
            break;
        /* Always nothing */
        case NONE:
        default: break;
    }

    /* Keyboard quick-jump: Shift+Letter/Number */
    handle_keyboard_quickjump();
}

FUNCTION(UI_NAME, setup) {
    /* On the first boot setup this can drill into the category holding the
     * game that was played last */
    int restore_row = last_game_take_row();

    list_current = list_get();
    list_len = list_length();

    /* The carousel works this out for itself, so the row is all it needs */
    current_selected_item = (restore_row > 0 && restore_row < list_len) ? restore_row : 0;
    frames_focused = 0;
    draw_current = DRAW_UI;

    navigate_timeout = 3;
    menu_changed_item();

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
    update_data();
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

    draw_game_meta();
    if (list_len > 0) {
        draw_small_box_highlight();
        draw_small_boxes();
        draw_big_box();

        /* If focused, draw large cover art */
        draw_large_art();
    }

    /* Check for pending Serial VMU backup on first frame */
    if (!serial_vmu_boot_checked && draw_current == DRAW_UI) {
        serial_vmu_boot_checked = true;
        serial_vmu_check_boot_backup(&draw_current, &region_themes[region_current].colors, &navigate_timeout,
                                     region_themes[region_current].colors.menu_highlight_color);
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
