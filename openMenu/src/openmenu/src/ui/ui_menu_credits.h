/*
 * File: ui_menu_credits.h
 * Project: ui
 * File Created: Monday, 12th July 2021 11:40:41 pm
 * Author: Hayden Kowalchuk
 * -----
 * Copyright (c) 2021 Hayden Kowalchuk, Hayden Kowalchuk
 * License: BSD 3-clause "New" or "Revised" License, http://www.opensource.org/licenses/BSD-3-Clause
 */

#pragma once

#include <backend/gd_item.h>
#include <openmenu_settings.h>
#include "ui/common.h"

struct theme_color;

void menu_setup(enum draw_state* state, struct theme_color* _colors, int* timeout_ptr, uint32_t title_color);
void popup_setup(enum draw_state* state, struct theme_color* _colors, int* timeout_ptr, uint32_t title_color);
void exit_menu_setup(enum draw_state* state, struct theme_color* _colors, int* timeout_ptr, uint32_t title_color,
                     int is_folder);
void cb_menu_setup(enum draw_state* state, struct theme_color* _colors, int* timeout_ptr, uint32_t title_color);
void recent_manage_setup(enum draw_state* state, struct theme_color* _colors, int* timeout_ptr, uint32_t title_color);
void saveload_setup(enum draw_state* state, struct theme_color* _colors, int* timeout_ptr, uint32_t title_color);

/* COMPACTION_TEST_START */
void compaction_test_setup(enum draw_state* state, struct theme_color* _colors, int* timeout_ptr, uint32_t title_color);
/* COMPACTION_TEST_END */

void handle_input_menu(enum control input);
void handle_input_credits(enum control input);
void handle_input_multidisc(enum control input);
void handle_input_exit(enum control input);
void handle_input_codebreaker(enum control input);
void handle_input_psx_launcher(enum control input);
void handle_input_saveload(enum control input);
void handle_input_recent_manage(enum control input);

/* COMPACTION_TEST_START */
void handle_input_compaction_test(enum control input);
/* COMPACTION_TEST_END */

void draw_menu_op(void);
void draw_menu_tr(void);

void draw_credits_op(void);
void draw_credits_tr(void);

void draw_multidisc_op(void);
void draw_multidisc_tr(void);

void draw_exit_op(void);
void draw_exit_tr(void);

void draw_codebreaker_op(void);
void draw_codebreaker_tr(void);

void draw_psx_launcher_op(void);
void draw_psx_launcher_tr(void);

void draw_saveload_op(void);
void draw_saveload_tr(void);

void draw_recent_manage_op(void);
void draw_recent_manage_tr(void);

/* COMPACTION_TEST_START */
void draw_compaction_test_op(void);
void draw_compaction_test_tr(void);

/* COMPACTION_TEST_END */

typedef enum {
    SERIAL_VMU_LAUNCH_DC,
    SERIAL_VMU_LAUNCH_BLEEM,
    SERIAL_VMU_LAUNCH_BLOOM,
    SERIAL_VMU_LAUNCH_CB,
    SERIAL_VMU_LAUNCH_EXIT_BIOS,
    SERIAL_VMU_LAUNCH_NONE,
} serial_vmu_launch_action_t;

void serial_vmu_setup(enum draw_state* state, struct theme_color* _colors, int* timeout_ptr, uint32_t title_color);
void serial_vmu_start_restore(const gd_item* item, serial_vmu_launch_action_t action);
void handle_input_serial_vmu(enum control input);
void draw_serial_vmu_op(void);
void draw_serial_vmu_tr(void);

/* Check for pending backup on boot - call from UI mode after first frame rendered */
void serial_vmu_check_boot_backup(enum draw_state* draw_current_ptr, struct theme_color* _colors, int* timeout_ptr,
                                  uint32_t title_color);

void set_cur_game_item(const gd_item* id);
const gd_item* get_cur_game_item();

/* Where the folders view should land when the manage state closes */
typedef enum RECENT_MANAGE_RESULT { RM_RESULT_ACTIVE = 0, RM_RESULT_TO_RECENT, RM_RESULT_TO_ROOT } RECENT_MANAGE_RESULT;

RECENT_MANAGE_RESULT recent_manage_result(void);
