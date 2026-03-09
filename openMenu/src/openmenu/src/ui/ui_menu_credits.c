/*
 * File: ui_menu_credits.c
 * Project: ui
 * File Created: Monday, 12th July 2021 11:34:23 pm
 * Author: Hayden Kowalchuk
 * -----
 * Copyright (c) 2021 Hayden Kowalchuk, Hayden Kowalchuk
 * License: BSD 3-clause "New" or "Revised" License,
 * http://www.opensource.org/licenses/BSD-3-Clause
 */

#include <fat/fs_fat.h>
#include <fcntl.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/stat.h>
#include <unistd.h>

#include <backend/db_item.h>
#include <backend/gd_item.h>
#include <backend/gd_list.h>
#include <backend/gdemu_sdk.h>
#include <crayon_savefile/savefile.h>
#include <openmenu_debug.h>
#include <openmenu_savefile.h>
#include <openmenu_settings.h>

#include "ui/draw_kos.h"
#include "ui/draw_prototypes.h"
#include "ui/font_prototypes.h"
#include "ui/ui_common.h"

#include "ui/ui_menu_credits.h"

/* External declaration for VM2/VMUPro/USB4Maple/Pico2Maple detection */
#include <crayon_savefile/peripheral.h>
#include <dc/maple.h>
#include <dc/maple/vmu.h>
#include <kos/fs.h>
#include <openmenu_lcd.h>
#include <openmenu_lcd_access.h>
#include "vm2/vm2_api.h"
#include "vmu_lcd_utils.h"
extern maple_device_t* vm2_devices[];
extern int vm2_device_count;
extern void vm2_rescan(void);
extern void vm2_send_id_to_all(const char* product, const char* name);
extern const char* vm2_get_type_name(maple_device_t* dev);

#pragma region Exit_Menu

/* Exit to BIOS menu option strings */
static const char* exit_option_text[] = {"Send game ID + Mount disc + Exit to BIOS",
                                         "Send game ID + Exit to BIOS",
                                         "Create/Restore Serial VMU + Mount disc + Exit to BIOS",
                                         "Create/Restore Serial VMU + Exit to BIOS",
                                         "Mount disc + Exit to BIOS",
                                         "Exit to BIOS",
                                         "Close"};

static const char* exit_info_text = "Region and VGA patching are not automatically applied when "
                                    "launching games from the BIOS. Select \"Disc Image Options\" "
                                    "in GD MENU Card Manager to patch instead.";

static int exit_menu_choice = 0;
static int exit_menu_num_options = 0;
static int exit_menu_is_folder = 0;

/* Exit menu option indices, set dynamically based on context */
typedef enum EXIT_OPTION {
    EXIT_OPT_SENDID_MOUNT = 0,
    EXIT_OPT_SENDID_ONLY,
    EXIT_OPT_RESTORE_MOUNT,
    EXIT_OPT_RESTORE_ONLY,
    EXIT_OPT_MOUNT_ONLY,
    EXIT_OPT_EXIT_ONLY,
    EXIT_OPT_CLOSE,
    EXIT_OPT_MAX
} EXIT_OPTION;

/* Dynamic option list for current context */
static EXIT_OPTION exit_options[EXIT_OPT_MAX];

/* Build exit options list based on context
 * - is_folder: true if a folder is selected (only Exit to BIOS + Close)
 * - has_vm2: true if VM2/VMUPro/USB4Maple/Pico2Maple is detected
 * - is_game: true if type != "other" (game, psx, etc.)
 * - Also checks if VMU Game ID transmission is enabled (not set to Off)
 */
static void
exit_menu_build_options(int is_folder, int has_vm2, int is_game) {
    exit_menu_num_options = 0;
    exit_menu_is_folder = is_folder;

    if (is_folder) {
        /* Folder selected: only Exit to BIOS and Close */
        exit_options[exit_menu_num_options++] = EXIT_OPT_EXIT_ONLY;
        exit_options[exit_menu_num_options++] = EXIT_OPT_CLOSE;
    } else if (sf_serial_vmu[0] != SERIAL_VMU_OFF && is_game) {
        /* Serial VMU enabled + type != "other": restore options */
        exit_options[exit_menu_num_options++] = EXIT_OPT_RESTORE_MOUNT;
        exit_options[exit_menu_num_options++] = EXIT_OPT_RESTORE_ONLY;
        exit_options[exit_menu_num_options++] = EXIT_OPT_MOUNT_ONLY;
        exit_options[exit_menu_num_options++] = EXIT_OPT_EXIT_ONLY;
        exit_options[exit_menu_num_options++] = EXIT_OPT_CLOSE;
    } else if (has_vm2 && is_game && sf_vm2_send_all[0] != VM2_SEND_OFF) {
        /* VM2 detected + type != "other" + transmission enabled: all options */
        exit_options[exit_menu_num_options++] = EXIT_OPT_SENDID_MOUNT;
        exit_options[exit_menu_num_options++] = EXIT_OPT_SENDID_ONLY;
        exit_options[exit_menu_num_options++] = EXIT_OPT_MOUNT_ONLY;
        exit_options[exit_menu_num_options++] = EXIT_OPT_EXIT_ONLY;
        exit_options[exit_menu_num_options++] = EXIT_OPT_CLOSE;
    } else {
        /* No VM2 or type == "other": mount, exit, close */
        exit_options[exit_menu_num_options++] = EXIT_OPT_MOUNT_ONLY;
        exit_options[exit_menu_num_options++] = EXIT_OPT_EXIT_ONLY;
        exit_options[exit_menu_num_options++] = EXIT_OPT_CLOSE;
    }
}

static int
count_wrap_lines(const char* text, int max_chars) {
    if (max_chars <= 0) {
        return 0;
    }
    int lines = 0;
    int line_len = 0;
    int last_space_offset = -1;
    int i = 0;

    while (text[i]) {
        if (text[i] == ' ') {
            last_space_offset = i;
        }
        line_len++;
        if (line_len > max_chars) {
            lines++;
            if (last_space_offset >= 0 && last_space_offset >= (i - line_len + 1)) {
                i = last_space_offset + 1;
            }
            line_len = 0;
            last_space_offset = -1;
            continue;
        }
        i++;
    }
    if (line_len > 0) {
        lines++;
    }
    return lines;
}

static void
draw_wrap_text_bmp(const char* text, int x, int y, int max_chars, int line_height) {
    char line_buf[128];
    int line_len = 0;
    int line_start = 0;
    int last_space = -1;
    int i = 0;

    while (text[i]) {
        if (text[i] == ' ') {
            last_space = i;
        }
        line_len++;
        if (line_len > max_chars) {
            int break_at = (last_space > line_start) ? last_space : i;
            int len = break_at - line_start;
            strncpy(line_buf, &text[line_start], len);
            line_buf[len] = '\0';
            font_bmp_draw_main(x, y, line_buf);
            y += line_height;
            line_start = break_at + ((text[break_at] == ' ') ? 1 : 0);
            i = line_start;
            line_len = 0;
            last_space = -1;
            continue;
        }
        i++;
    }
    if (line_len > 0) {
        strncpy(line_buf, &text[line_start], line_len);
        line_buf[line_len] = '\0';
        font_bmp_draw_main(x, y, line_buf);
    }
}

#pragma endregion Exit_Menu

#pragma region CodeBreaker_Menu

/* CodeBreaker menu option strings */
static const char* cb_option_text[] = {"Launch selected disc with CodeBreaker", "Close"};

static int cb_menu_choice = 0;
#define CB_MENU_NUM_OPTIONS 2

typedef enum CB_OPTION { CB_OPT_LAUNCH = 0, CB_OPT_CLOSE } CB_OPTION;

#pragma endregion CodeBreaker_Menu

#pragma region Settings_Menu

static const char* menu_choice_text[] = {"Style",
                                         "Theme",
                                         "Aspect",
                                         "Beep",
                                         "Exit to BIOS",
                                         "Sort",
                                         "Filter",
                                         "Multi-Disc",
                                         "Multi-Disc Grouping",
                                         "Artwork",
                                         "Display Index Numbers",
                                         "Disc Details",
                                         "Artwork",
                                         "Item Details",
                                         "Clock",
                                         "Marquee Speed",
                                         "VMU Time Sync",
                                         "Serial VMU",
                                         "Serial VMU Multi-Slot",
                                         "VMU Game ID",
                                         "Boot Mode"};
static const char* theme_choice_text[] = {"LineDesc", "Grid3", "Scroll", "Folders"};
static const char* region_choice_text[] = {"NTSC-U", "NTSC-J", "PAL"};
static const char* region_choice_text_scroll[] = {"GDMENU"};
static const char* region_choice_text_folders[] = {"FoldersDefault"};
static const char* aspect_choice_text[] = {"4:3", "16:9"};
static const char* beep_choice_text[] = {"Off", "On"}; /* Hidden from UI but kept for array sizing */
static const char* bios_3d_choice_text[] = {"Standard", "Alternate", "Alternate + 3D"};
static const char* sort_choice_text[] = {"Alphabetical", "Name", "Region", "Genre", "SD Card Order"};
static const char* sort_choice_text_folders[] = {"Alphabetical", "SD Card Order"};
#define SORT_CHOICES_FOLDERS 2
static const char* filter_choice_text[] = {"All",      "Action",   "Racing",   "Simulation", "Sports",     "Lightgun",
                                           "Fighting", "Shooter",  "Survival", "Adventure",  "Platformer", "RPG",
                                           "Shmup",    "Strategy", "Puzzle",   "Arcade",     "Music"};
static const char* multidisc_choice_text[] = {"Show All", "Compact"};
static const char* multidisc_grouping_choice_text[] = {"Anywhere", "Same Folder Only"};
static const char* scroll_art_choice_text[] = {"Off", "On"};
static const char* scroll_index_choice_text[] = {"Off", "On"};
static const char* disc_details_choice_text[] = {"Show", "Hide"};
static const char* folders_art_choice_text[] = {"Off", "On"};
static const char* folders_item_details_choice_text[] = {"Off", "On"};
static const char* marquee_speed_choice_text[] = {"Slow", "Medium", "Fast"};
static const char* clock_choice_text[] = {"On (12-Hour)", "On (24-Hour)", "Off"};
static const char* vmu_time_sync_choice_text[] = {"Off", "On"};
static const char* serial_vmu_choice_text[] = {"Off",     "On (A1)", "On (A2)", "On (B1)", "On (B2)",
                                               "On (C1)", "On (C2)", "On (D1)", "On (D2)"};
static const char* serial_vmu_multislot_choice_text[] = {"Off", "On"};
static const char* vm2_send_all_choice_text[] = {"Send to All", "Send to First", "Off"};
static const char* boot_mode_choice_text[] = {"Full Boot", "License Only", "Animation Only", "Fast Boot"};
static const char* save_choice_text[] = {"Save/Load", "Apply"};
static const char* credits_text[] = {"Credits"};

const char* custom_theme_text[10] = {0};
static theme_custom* custom_themes;
static theme_scroll* custom_scroll;
static int num_custom_themes;
int cb_multidisc = 0;
int start_cb = 0;
static int psx_launcher_choice = 0; /* 0 = Bleem!, 1 = Bloom */
static const gd_item* cur_game_item = NULL;

#define MENU_OPTIONS  ((int)(sizeof(menu_choice_text) / sizeof(menu_choice_text)[0]))
#define MENU_CHOICES  (MENU_OPTIONS)
#define THEME_CHOICES (sizeof(theme_choice_text) / sizeof(theme_choice_text)[0])
static int REGION_CHOICES = (sizeof(region_choice_text) / sizeof(region_choice_text)[0]);
#define ASPECT_CHOICES             (sizeof(aspect_choice_text) / sizeof(aspect_choice_text)[0])
#define BEEP_CHOICES               (sizeof(beep_choice_text) / sizeof(beep_choice_text)[0]) /* Hidden from UI */
#define BIOS_3D_CHOICES            (sizeof(bios_3d_choice_text) / sizeof(bios_3d_choice_text)[0])
#define SORT_CHOICES               (sizeof(sort_choice_text) / sizeof(sort_choice_text)[0])
#define FILTER_CHOICES             (sizeof(filter_choice_text) / sizeof(filter_choice_text)[0])
#define MULTIDISC_CHOICES          (sizeof(multidisc_choice_text) / sizeof(multidisc_choice_text)[0])
#define MULTIDISC_GROUPING_CHOICES (sizeof(multidisc_grouping_choice_text) / sizeof(multidisc_grouping_choice_text)[0])
#define SCROLL_ART_CHOICES         (sizeof(scroll_art_choice_text) / sizeof(scroll_art_choice_text)[0])
#define SCROLL_INDEX_CHOICES       (sizeof(scroll_index_choice_text) / sizeof(scroll_index_choice_text)[0])
#define DISC_DETAILS_CHOICES       (sizeof(disc_details_choice_text) / sizeof(disc_details_choice_text)[0])
#define FOLDERS_ART_CHOICES        (sizeof(folders_art_choice_text) / sizeof(folders_art_choice_text)[0])
#define FOLDERS_ITEM_DETAILS_CHOICES                                                                                   \
    (sizeof(folders_item_details_choice_text) / sizeof(folders_item_details_choice_text)[0])
#define MARQUEE_SPEED_CHOICES (sizeof(marquee_speed_choice_text) / sizeof(marquee_speed_choice_text)[0])
#define CLOCK_CHOICES         (sizeof(clock_choice_text) / sizeof(clock_choice_text)[0])
#define VMU_TIME_SYNC_CHOICES (sizeof(vmu_time_sync_choice_text) / sizeof(vmu_time_sync_choice_text)[0])
#define SERIAL_VMU_CHOICES    (sizeof(serial_vmu_choice_text) / sizeof(serial_vmu_choice_text)[0])
#define SERIAL_VMU_MULTISLOT_CHOICES                                                                                   \
    (sizeof(serial_vmu_multislot_choice_text) / sizeof(serial_vmu_multislot_choice_text)[0])
#define VM2_SEND_ALL_CHOICES (sizeof(vm2_send_all_choice_text) / sizeof(vm2_send_all_choice_text)[0])
#define BOOT_MODE_CHOICES    (sizeof(boot_mode_choice_text) / sizeof(boot_mode_choice_text)[0])

typedef enum MENU_CHOICE {
    CHOICE_START,
    CHOICE_THEME = CHOICE_START,
    CHOICE_REGION,
    CHOICE_ASPECT,
    CHOICE_BEEP,
    CHOICE_BIOS_3D,
    CHOICE_SORT,
    CHOICE_FILTER,
    CHOICE_MULTIDISC,
    CHOICE_MULTIDISC_GROUPING,
    CHOICE_SCROLL_ART,
    CHOICE_SCROLL_INDEX,
    CHOICE_DISC_DETAILS,
    CHOICE_FOLDERS_ART,
    CHOICE_FOLDERS_ITEM_DETAILS,
    CHOICE_CLOCK,
    CHOICE_MARQUEE_SPEED,
    CHOICE_VMU_TIME_SYNC,
    CHOICE_SERIAL_VMU,
    CHOICE_SERIAL_VMU_MULTISLOT,
    CHOICE_VM2_SEND_ALL,
    CHOICE_BOOT_MODE,
    CHOICE_SAVE,
    CHOICE_CREDITS,
    CHOICE_END = CHOICE_CREDITS
} MENU_CHOICE;

#define INPUT_TIMEOUT (10)

static int choices[MENU_CHOICES + 1];
static int choices_max[MENU_CHOICES + 1] = {THEME_CHOICES,
                                            3,
                                            ASPECT_CHOICES,
                                            BEEP_CHOICES,
                                            BIOS_3D_CHOICES,
                                            SORT_CHOICES,
                                            FILTER_CHOICES,
                                            MULTIDISC_CHOICES,
                                            MULTIDISC_GROUPING_CHOICES,
                                            SCROLL_ART_CHOICES,
                                            SCROLL_INDEX_CHOICES,
                                            DISC_DETAILS_CHOICES,
                                            FOLDERS_ART_CHOICES,
                                            FOLDERS_ITEM_DETAILS_CHOICES,
                                            CLOCK_CHOICES,
                                            MARQUEE_SPEED_CHOICES,
                                            VMU_TIME_SYNC_CHOICES,
                                            SERIAL_VMU_CHOICES,
                                            SERIAL_VMU_MULTISLOT_CHOICES,
                                            VM2_SEND_ALL_CHOICES,
                                            BOOT_MODE_CHOICES,
                                            2 /* Apply/Save */};
static const char** menu_choice_array[MENU_CHOICES] = {theme_choice_text,
                                                       region_choice_text,
                                                       aspect_choice_text,
                                                       beep_choice_text,
                                                       bios_3d_choice_text,
                                                       sort_choice_text,
                                                       filter_choice_text,
                                                       multidisc_choice_text,
                                                       multidisc_grouping_choice_text,
                                                       scroll_art_choice_text,
                                                       scroll_index_choice_text,
                                                       disc_details_choice_text,
                                                       folders_art_choice_text,
                                                       folders_item_details_choice_text,
                                                       clock_choice_text,
                                                       marquee_speed_choice_text,
                                                       vmu_time_sync_choice_text,
                                                       serial_vmu_choice_text,
                                                       serial_vmu_multislot_choice_text,
                                                       vm2_send_all_choice_text,
                                                       boot_mode_choice_text};
static int current_choice = CHOICE_START;
static int* input_timeout_ptr = NULL;

#pragma endregion Settings_Menu

#pragma region Credits_Menu

typedef struct credit_pair {
    const char* contributor;
    const char* role;
} credit_pair;

static const credit_pair credits[] = {
    (credit_pair){"ateam", "Folders, Updates/Fixes"},
    (credit_pair){"megavolt85", "gdemu sdk, coder"},
    (credit_pair){"u/westhinksdifferent/", "UI Mockups"},
    (credit_pair){"FlorreW", "Metadata DB"},
    (credit_pair){"hasnopants", "Metadata DB"},
    (credit_pair){"Roareye", "Metadata DB"},
    (credit_pair){"sonik-br", "GDMENUCardManager"},
    (credit_pair){"protofall", "Crayon_VMU"},
    (credit_pair){"TheLegendOfXela", "Boxart (Customs)"},
    (credit_pair){"marky-b-1986", "Theming Ideas"},
    (credit_pair){"Various Testers", "Breaking Things"},
    (credit_pair){"Kofi Supporters", "Coffee+Hardware"},
    (credit_pair){"mrneo240", "Author"},
};
static const int num_credits = sizeof(credits) / sizeof(credit_pair);

#pragma endregion Credits_Menu

static enum draw_state* state_ptr = NULL;
static uint32_t text_color;
static uint32_t highlight_color;
static uint32_t menu_bkg_color;
static uint32_t menu_bkg_border_color;
static uint32_t menu_title_color;

/* Forward declaration for Save/Load window initialization */
static void saveload_init_state(void);

/* COMPACTION_TEST_START */
/* Forward declaration for compaction test */
static void compaction_test_setup_internal(void);
/* COMPACTION_TEST_END */

/* Build version string (compiled in from VERSION.TXT at build time) */
#ifndef OPENMENU_BUILD_VERSION
#define OPENMENU_BUILD_VERSION "Unknown"
#endif

void
set_cur_game_item(const gd_item* id) {
    cur_game_item = id;
}

const gd_item*
get_cur_game_item() {
    return cur_game_item;
}

static void
common_setup(enum draw_state* state, theme_color* _colors, int* timeout_ptr) {
    /* sync color theme */
    text_color = _colors->menu_text_color;
    highlight_color = _colors->menu_highlight_color;
    menu_bkg_color = _colors->menu_bkg_color;
    menu_bkg_border_color = _colors->menu_bkg_border_color;

    /* So we can modify the shared state and input timeout */
    state_ptr = state;
    input_timeout_ptr = timeout_ptr;
    *input_timeout_ptr = 3;
}

void
menu_setup(enum draw_state* state, theme_color* _colors, int* timeout_ptr, uint32_t title_color) {
    common_setup(state, _colors, timeout_ptr);
    menu_title_color = title_color;

    choices[CHOICE_THEME] = sf_ui[0];
    choices[CHOICE_REGION] = sf_region[0];
    choices[CHOICE_ASPECT] = sf_aspect[0];
    choices[CHOICE_SORT] = sf_sort[0];
    /* In Folders mode, clamp Sort to valid range (0-1) */
    if (sf_ui[0] == UI_FOLDERS && choices[CHOICE_SORT] >= SORT_CHOICES_FOLDERS) {
        choices[CHOICE_SORT] = 0; /* Default to Alphabetical */
    }
    choices[CHOICE_FILTER] = sf_filter[0];
    choices[CHOICE_BEEP] = sf_beep[0]; /* Hidden from UI */
    choices[CHOICE_BIOS_3D] = sf_bios_3d[0];
    choices[CHOICE_MULTIDISC] = sf_multidisc[0];
    choices[CHOICE_MULTIDISC_GROUPING] = sf_multidisc_grouping[0];
    choices[CHOICE_SCROLL_ART] = sf_scroll_art[0];
    choices[CHOICE_SCROLL_INDEX] = sf_scroll_index[0];
    choices[CHOICE_DISC_DETAILS] = sf_disc_details[0];
    choices[CHOICE_FOLDERS_ART] = sf_folders_art[0];
    choices[CHOICE_FOLDERS_ITEM_DETAILS] = sf_folders_item_details[0];
    choices[CHOICE_MARQUEE_SPEED] = sf_marquee_speed[0];
    choices[CHOICE_CLOCK] = sf_clock[0];
    choices[CHOICE_VMU_TIME_SYNC] = sf_vmu_time_sync[0];
    choices[CHOICE_SERIAL_VMU] = sf_serial_vmu[0];
    choices[CHOICE_SERIAL_VMU_MULTISLOT] = sf_serial_vmu_multislot[0];
    choices[CHOICE_VM2_SEND_ALL] = sf_vm2_send_all[0];
    /* Enforce mutual exclusion on load (mirrors menu_choice_left/right logic) */
    if (choices[CHOICE_SERIAL_VMU] != SERIAL_VMU_OFF) {
        choices[CHOICE_VM2_SEND_ALL] = VM2_SEND_OFF;
    } else if (choices[CHOICE_VM2_SEND_ALL] != VM2_SEND_OFF) {
        choices[CHOICE_SERIAL_VMU] = SERIAL_VMU_OFF;
        choices[CHOICE_SERIAL_VMU_MULTISLOT] = SERIAL_VMU_MULTISLOT_OFF;
    }
    choices[CHOICE_BOOT_MODE] = sf_boot_mode[0];

    if (choices[CHOICE_THEME] != UI_SCROLL && choices[CHOICE_THEME] != UI_FOLDERS) {
        menu_choice_array[CHOICE_REGION] = region_choice_text;
        REGION_CHOICES = (sizeof(region_choice_text) / sizeof(region_choice_text)[0]);
        choices_max[CHOICE_REGION] = REGION_CHOICES;
        /* Grab custom themes if we have them */
        custom_themes = theme_get_custom(&num_custom_themes);
        if (num_custom_themes > 0) {
            for (int i = 0; i < num_custom_themes; i++) {
                choices_max[CHOICE_REGION]++;
                custom_theme_text[i] = custom_themes[i].name;
            }
        }
    } else {
        /* Assign appropriate default theme name based on UI mode */
        if (sf_ui[0] == UI_FOLDERS) {
            menu_choice_array[CHOICE_REGION] = region_choice_text_folders;
        } else {
            menu_choice_array[CHOICE_REGION] = region_choice_text_scroll;
        }
        REGION_CHOICES = 1;
        choices_max[CHOICE_REGION] = 1;
        /* Load appropriate themes based on UI mode */
        if (sf_ui[0] == UI_FOLDERS) {
            custom_scroll = theme_get_folder(&num_custom_themes);
        } else {
            custom_scroll = theme_get_scroll(&num_custom_themes);
        }
        if (num_custom_themes > 0) {
            for (int i = 0; i < num_custom_themes; i++) {
                choices_max[CHOICE_REGION]++;
                custom_theme_text[i] = custom_scroll[i].name;
            }
            if (sf_custom_theme[0] == THEME_ON) {
                choices[CHOICE_REGION] = sf_custom_theme_num[0] + 1;
            }
        }
    }

    if (choices[CHOICE_REGION] >= choices_max[CHOICE_REGION]) {
        choices[CHOICE_REGION] = choices_max[CHOICE_REGION] - 1;
    }
}

void
popup_setup(enum draw_state* state, theme_color* _colors, int* timeout_ptr, uint32_t title_color) {
    common_setup(state, _colors, timeout_ptr);
    menu_title_color = title_color;

    current_choice = CHOICE_START;
    psx_launcher_choice = 0; /* Reset to Bleem! as default */
}

void
exit_menu_setup(enum draw_state* state, theme_color* _colors, int* timeout_ptr, uint32_t title_color, int is_folder) {
    common_setup(state, _colors, timeout_ptr);
    menu_title_color = title_color;

    /* Rescan for VM2 devices (detect hot-swapped devices) */
    vm2_rescan();

    /* Reset selection to first option */
    exit_menu_choice = 0;

    /* Determine if VM2 is present */
    int has_vm2 = (vm2_device_count > 0);

    /* Determine if current item is a game (type != "other") */
    int is_game = 0;
    if (!is_folder && cur_game_item != NULL && cur_game_item->type[0] != '\0') {
        is_game = (strcmp(cur_game_item->type, "other") != 0);
    }

    /* Build the options list */
    exit_menu_build_options(is_folder, has_vm2, is_game);
}

static void
menu_leave(void) {
    *state_ptr = DRAW_UI;
    *input_timeout_ptr = 3;
}

static void
credits_leave(void) {
    *state_ptr = DRAW_MENU;
    *input_timeout_ptr = 3;
}

static void
menu_accept(void) {
    if (current_choice == CHOICE_SAVE) {
        if (choices[CHOICE_SAVE] == 0 /* Save/Load */) {
            /* Open Save/Load window instead of saving directly */
            saveload_init_state();
            *state_ptr = DRAW_SAVELOAD;
            *input_timeout_ptr = 3;
            return;
        }

        /* Apply only (no save). Apply settings and reload UI */
        /* update Global Settings */
        sf_ui[0] = choices[CHOICE_THEME];
        sf_region[0] = choices[CHOICE_REGION];
        sf_aspect[0] = choices[CHOICE_ASPECT];
        sf_sort[0] = choices[CHOICE_SORT];
        sf_filter[0] = choices[CHOICE_FILTER];
        sf_beep[0] = choices[CHOICE_BEEP]; /* Hidden from UI */
        sf_bios_3d[0] = choices[CHOICE_BIOS_3D];
        sf_multidisc[0] = choices[CHOICE_MULTIDISC];
        sf_multidisc_grouping[0] = choices[CHOICE_MULTIDISC_GROUPING];
        sf_scroll_art[0] = choices[CHOICE_SCROLL_ART];
        sf_scroll_index[0] = choices[CHOICE_SCROLL_INDEX];
        sf_disc_details[0] = choices[CHOICE_DISC_DETAILS];
        sf_folders_art[0] = choices[CHOICE_FOLDERS_ART];
        sf_folders_item_details[0] = choices[CHOICE_FOLDERS_ITEM_DETAILS];
        sf_marquee_speed[0] = choices[CHOICE_MARQUEE_SPEED];
        sf_clock[0] = choices[CHOICE_CLOCK];
        /* If VMU Time Sync was just enabled, sync the RTC now */
        if (choices[CHOICE_VMU_TIME_SYNC] == VMU_TIME_SYNC_ON && sf_vmu_time_sync[0] == VMU_TIME_SYNC_OFF) {
            sync_rtc_from_vmu();
        }
        /* COMPACTION_TEST_START. Enable DEBUG_COMPACTION_TEST in openmenu_debug.h */
#if DEBUG_COMPACTION_TEST
        /* Hijack VMU Time Sync enable to trigger compaction test */
        if (choices[CHOICE_VMU_TIME_SYNC] == VMU_TIME_SYNC_ON && sf_vmu_time_sync[0] == VMU_TIME_SYNC_OFF) {
            sf_vmu_time_sync[0] = choices[CHOICE_VMU_TIME_SYNC];
            compaction_test_setup_internal();
            *state_ptr = DRAW_COMPACTION_TEST;
            *input_timeout_ptr = 3;
            return;
        }
#endif
        /* COMPACTION_TEST_END */
        sf_vmu_time_sync[0] = choices[CHOICE_VMU_TIME_SYNC];
        sf_serial_vmu[0] = choices[CHOICE_SERIAL_VMU];
        sf_serial_vmu_multislot[0] = choices[CHOICE_SERIAL_VMU_MULTISLOT];
        sf_vm2_send_all[0] = choices[CHOICE_VM2_SEND_ALL];
        sf_boot_mode[0] = choices[CHOICE_BOOT_MODE];
        if (choices[CHOICE_THEME] != UI_SCROLL && choices[CHOICE_THEME] != UI_FOLDERS && sf_region[0] > REGION_END) {
            sf_custom_theme[0] = THEME_ON;
            int num_default_themes = 0;
            theme_get_default(sf_aspect[0], &num_default_themes);
            sf_custom_theme_num[0] = sf_region[0] - num_default_themes;
        } else if ((choices[CHOICE_THEME] == UI_SCROLL || choices[CHOICE_THEME] == UI_FOLDERS) && sf_region[0] > 0) {
            sf_custom_theme[0] = THEME_ON;
            sf_custom_theme_num[0] = sf_region[0] - 1;
        } else {
            sf_custom_theme[0] = THEME_OFF;
        }

        /* If not filtering, then plain sort */
        if (!choices[CHOICE_FILTER]) {
            switch ((CFG_SORT)choices[CHOICE_SORT]) {
                case SORT_NAME: list_set_sort_name(); break;
                case SORT_DATE: list_set_sort_region(); break;
                case SORT_PRODUCT: list_set_sort_genre(); break;
                case SORT_SD_CARD: list_set_sort_default(); break;
                default:
                case SORT_DEFAULT: list_set_sort_alphabetical(); break;
            }
        } else {
            /* If filtering, filter down to only genre then sort */
            list_set_genre_sort((FLAGS_GENRE)choices[CHOICE_FILTER] - 1, choices[CHOICE_SORT]);
        }

        extern void reload_ui(void);
        reload_ui();
    }
    if (current_choice == CHOICE_CREDITS) {
        *state_ptr = DRAW_CREDITS;
        *input_timeout_ptr = 3;
    }
}

static void
menu_choice_prev(void) {
    if (*input_timeout_ptr > 0) {
        return;
    }
    current_choice--;
    /* Wrap around if we go below start */
    if (current_choice < CHOICE_START) {
        current_choice = CHOICE_END;
    }
    /* Keep skipping until we land on a valid option */
    int attempts = 0;
    while (attempts < CHOICE_END - CHOICE_START + 1) {
        int skip = 0;
        /* Skip SCROLL_ART option in non-Scroll modes */
        if (current_choice == CHOICE_SCROLL_ART && sf_ui[0] != UI_SCROLL) {
            skip = 1;
        }
        /* Skip SCROLL_INDEX option in non-Scroll modes */
        if (current_choice == CHOICE_SCROLL_INDEX && sf_ui[0] != UI_SCROLL) {
            skip = 1;
        }
        /* Skip DISC_DETAILS option in non-Scroll modes */
        if (current_choice == CHOICE_DISC_DETAILS && sf_ui[0] != UI_SCROLL) {
            skip = 1;
        }
        /* Skip MULTIDISC_GROUPING option in non-Folders modes or when Multi-Disc is "Show All" */
        if (current_choice == CHOICE_MULTIDISC_GROUPING
            && (sf_ui[0] != UI_FOLDERS || choices[CHOICE_MULTIDISC] == MULTIDISC_SHOW)) {
            skip = 1;
        }
        /* Skip FOLDERS_ART option in non-Folders modes */
        if (current_choice == CHOICE_FOLDERS_ART && sf_ui[0] != UI_FOLDERS) {
            skip = 1;
        }
        /* Skip FOLDERS_ITEM_DETAILS option in non-Folders modes */
        if (current_choice == CHOICE_FOLDERS_ITEM_DETAILS && sf_ui[0] != UI_FOLDERS) {
            skip = 1;
        }
        /* Skip MARQUEE_SPEED option in non-Scroll/Folders modes */
        if (current_choice == CHOICE_MARQUEE_SPEED && sf_ui[0] != UI_SCROLL && sf_ui[0] != UI_FOLDERS) {
            skip = 1;
        }
        /* Skip CLOCK option in non-Folders modes */
        if (current_choice == CHOICE_CLOCK && sf_ui[0] != UI_FOLDERS) {
            skip = 1;
        }
        /* Skip SERIAL_VMU when no SD card, or VMU Game ID active with VM2 present */
        if (current_choice == CHOICE_SERIAL_VMU
            && (!savefile_sd_available() || (choices[CHOICE_VM2_SEND_ALL] != VM2_SEND_OFF && vm2_device_count > 0))) {
            skip = 1;
        }
        /* Skip SERIAL_VMU_MULTISLOT when Serial VMU off, no SD, or VMU Game ID active with VM2 present */
        if (current_choice == CHOICE_SERIAL_VMU_MULTISLOT
            && (choices[CHOICE_SERIAL_VMU] == SERIAL_VMU_OFF || !savefile_sd_available()
                || (choices[CHOICE_VM2_SEND_ALL] != VM2_SEND_OFF && vm2_device_count > 0))) {
            skip = 1;
        }
        /* Skip VM2_SEND_ALL option when no VM2 devices detected or Serial VMU is active */
        if (current_choice == CHOICE_VM2_SEND_ALL
            && (vm2_device_count == 0 || choices[CHOICE_SERIAL_VMU] != SERIAL_VMU_OFF)) {
            skip = 1;
        }
        /* Skip Aspect in Scroll mode (not used) */
        if (current_choice == CHOICE_ASPECT && sf_ui[0] == UI_SCROLL) {
            skip = 1;
        }
        /* Skip Aspect/Filter in Folders mode */
        if (sf_ui[0] == UI_FOLDERS && (current_choice == CHOICE_ASPECT || current_choice == CHOICE_FILTER)) {
            skip = 1;
        }
        /* Skip BEEP option (disabled/commented out) */
        if (current_choice == CHOICE_BEEP) {
            skip = 1;
        }
        /* Skip CREDITS in up/down navigation (reached via left/right from Save/Apply) */
        if (current_choice == CHOICE_CREDITS) {
            skip = 1;
        }
        if (!skip) {
            break; /* Found a valid option */
        }
        current_choice--;
        if (current_choice < CHOICE_START) {
            current_choice = CHOICE_END;
        }
        attempts++;
    }
    *input_timeout_ptr = INPUT_TIMEOUT;
}

static void
menu_choice_next(void) {
    if (*input_timeout_ptr > 0) {
        return;
    }
    current_choice++;
    /* Wrap around if we go past end */
    if (current_choice > CHOICE_END) {
        current_choice = CHOICE_START;
    }
    /* Keep skipping until we land on a valid option */
    int attempts = 0;
    while (attempts < CHOICE_END - CHOICE_START + 1) {
        int skip = 0;
        /* Skip SCROLL_ART option in non-Scroll modes */
        if (current_choice == CHOICE_SCROLL_ART && sf_ui[0] != UI_SCROLL) {
            skip = 1;
        }
        /* Skip SCROLL_INDEX option in non-Scroll modes */
        if (current_choice == CHOICE_SCROLL_INDEX && sf_ui[0] != UI_SCROLL) {
            skip = 1;
        }
        /* Skip DISC_DETAILS option in non-Scroll modes */
        if (current_choice == CHOICE_DISC_DETAILS && sf_ui[0] != UI_SCROLL) {
            skip = 1;
        }
        /* Skip MULTIDISC_GROUPING option in non-Folders modes or when Multi-Disc is "Show All" */
        if (current_choice == CHOICE_MULTIDISC_GROUPING
            && (sf_ui[0] != UI_FOLDERS || choices[CHOICE_MULTIDISC] == MULTIDISC_SHOW)) {
            skip = 1;
        }
        /* Skip FOLDERS_ART option in non-Folders modes */
        if (current_choice == CHOICE_FOLDERS_ART && sf_ui[0] != UI_FOLDERS) {
            skip = 1;
        }
        /* Skip FOLDERS_ITEM_DETAILS option in non-Folders modes */
        if (current_choice == CHOICE_FOLDERS_ITEM_DETAILS && sf_ui[0] != UI_FOLDERS) {
            skip = 1;
        }
        /* Skip MARQUEE_SPEED option in non-Scroll/Folders modes */
        if (current_choice == CHOICE_MARQUEE_SPEED && sf_ui[0] != UI_SCROLL && sf_ui[0] != UI_FOLDERS) {
            skip = 1;
        }
        /* Skip CLOCK option in non-Folders modes */
        if (current_choice == CHOICE_CLOCK && sf_ui[0] != UI_FOLDERS) {
            skip = 1;
        }
        /* Skip SERIAL_VMU when no SD card, or VMU Game ID active with VM2 present */
        if (current_choice == CHOICE_SERIAL_VMU
            && (!savefile_sd_available() || (choices[CHOICE_VM2_SEND_ALL] != VM2_SEND_OFF && vm2_device_count > 0))) {
            skip = 1;
        }
        /* Skip SERIAL_VMU_MULTISLOT when Serial VMU off, no SD, or VMU Game ID active with VM2 present */
        if (current_choice == CHOICE_SERIAL_VMU_MULTISLOT
            && (choices[CHOICE_SERIAL_VMU] == SERIAL_VMU_OFF || !savefile_sd_available()
                || (choices[CHOICE_VM2_SEND_ALL] != VM2_SEND_OFF && vm2_device_count > 0))) {
            skip = 1;
        }
        /* Skip VM2_SEND_ALL option when no VM2 devices detected or Serial VMU is active */
        if (current_choice == CHOICE_VM2_SEND_ALL
            && (vm2_device_count == 0 || choices[CHOICE_SERIAL_VMU] != SERIAL_VMU_OFF)) {
            skip = 1;
        }
        /* Skip Aspect in Scroll mode (not used) */
        if (current_choice == CHOICE_ASPECT && sf_ui[0] == UI_SCROLL) {
            skip = 1;
        }
        /* Skip Aspect/Filter in Folders mode */
        if (sf_ui[0] == UI_FOLDERS && (current_choice == CHOICE_ASPECT || current_choice == CHOICE_FILTER)) {
            skip = 1;
        }
        /* Skip BEEP option (disabled/commented out) */
        if (current_choice == CHOICE_BEEP) {
            skip = 1;
        }
        /* Skip CREDITS in up/down navigation (reached via left/right from Save/Apply) */
        if (current_choice == CHOICE_CREDITS) {
            skip = 1;
        }
        if (!skip) {
            break; /* Found a valid option */
        }
        current_choice++;
        if (current_choice > CHOICE_END) {
            current_choice = CHOICE_START;
        }
        attempts++;
    }
    *input_timeout_ptr = INPUT_TIMEOUT;
}

static void
menu_region_adj(void) {
    if (choices[CHOICE_THEME] != UI_SCROLL && choices[CHOICE_THEME] != UI_FOLDERS) {
        menu_choice_array[CHOICE_REGION] = region_choice_text;
        REGION_CHOICES = (sizeof(region_choice_text) / sizeof(region_choice_text)[0]);
        choices_max[CHOICE_REGION] = REGION_CHOICES;
        /* Grab custom themes if we have them */
        custom_themes = theme_get_custom(&num_custom_themes);
        if (num_custom_themes > 0) {
            for (int i = 0; i < num_custom_themes; i++) {
                choices_max[CHOICE_REGION]++;
                custom_theme_text[i] = custom_themes[i].name;
            }
        }
    } else {
        /* Assign appropriate default theme name based on current Style selection */
        if (choices[CHOICE_THEME] == UI_FOLDERS) {
            menu_choice_array[CHOICE_REGION] = region_choice_text_folders;
            REGION_CHOICES = (sizeof(region_choice_text_folders) / sizeof(region_choice_text_folders)[0]);
        } else {
            menu_choice_array[CHOICE_REGION] = region_choice_text_scroll;
            REGION_CHOICES = (sizeof(region_choice_text_scroll) / sizeof(region_choice_text_scroll)[0]);
        }
        choices_max[CHOICE_REGION] = REGION_CHOICES;
        /* Load appropriate themes based on UI mode */
        if (choices[CHOICE_THEME] == UI_FOLDERS) {
            custom_scroll = theme_get_folder(&num_custom_themes);
        } else {
            custom_scroll = theme_get_scroll(&num_custom_themes);
        }
        if (num_custom_themes > 0) {
            for (int i = 0; i < num_custom_themes; i++) {
                choices_max[CHOICE_REGION]++;
                custom_theme_text[i] = custom_scroll[i].name;
            }
        }
    }

    if (choices[CHOICE_REGION] >= choices_max[CHOICE_REGION]) {
        choices[CHOICE_REGION] = choices_max[CHOICE_REGION] - 1;
    }
}

static void
menu_choice_left(void) {
    if (*input_timeout_ptr > 0) {
        return;
    }
    /* Handle Save/Apply/Credits row navigation */
    if (current_choice == CHOICE_CREDITS) {
        current_choice = CHOICE_SAVE;
        choices[CHOICE_SAVE] = 1; /* Select Apply */
        *input_timeout_ptr = INPUT_TIMEOUT;
        return;
    }
    if (current_choice == CHOICE_SAVE && choices[CHOICE_SAVE] == 0) {
        /* Already on Save (leftmost), do nothing */
        return;
    }
    choices[current_choice]--;
    if (choices[current_choice] < 0) {
        choices[current_choice] = 0;
    }
    /* Mutual exclusion: Serial VMU and VMU Game ID */
    if (current_choice == CHOICE_SERIAL_VMU && choices[CHOICE_SERIAL_VMU] != SERIAL_VMU_OFF) {
        choices[CHOICE_VM2_SEND_ALL] = VM2_SEND_OFF;
    }
    if (current_choice == CHOICE_VM2_SEND_ALL && choices[CHOICE_VM2_SEND_ALL] != VM2_SEND_OFF) {
        choices[CHOICE_SERIAL_VMU] = SERIAL_VMU_OFF;
        choices[CHOICE_SERIAL_VMU_MULTISLOT] = SERIAL_VMU_MULTISLOT_OFF;
    }
    /* Reset multi-slot when Serial VMU is turned off */
    if (current_choice == CHOICE_SERIAL_VMU && choices[CHOICE_SERIAL_VMU] == SERIAL_VMU_OFF) {
        choices[CHOICE_SERIAL_VMU_MULTISLOT] = SERIAL_VMU_MULTISLOT_OFF;
    }
    if (current_choice == CHOICE_THEME) {
        menu_region_adj();
    }
    *input_timeout_ptr = INPUT_TIMEOUT;
}

static void
menu_choice_right(void) {
    if (*input_timeout_ptr > 0) {
        return;
    }
    /* Handle Save/Apply/Credits row navigation */
    if (current_choice == CHOICE_CREDITS) {
        /* Already on Credits (rightmost), do nothing */
        return;
    }
    if (current_choice == CHOICE_SAVE && choices[CHOICE_SAVE] == 1) {
        /* On Apply, move right to Credits */
        current_choice = CHOICE_CREDITS;
        *input_timeout_ptr = INPUT_TIMEOUT;
        return;
    }
    choices[current_choice]++;
    /* In Folders mode, limit Sort to 2 options */
    int max_choice = choices_max[current_choice];
    if (current_choice == CHOICE_SORT && sf_ui[0] == UI_FOLDERS) {
        max_choice = SORT_CHOICES_FOLDERS;
    }
    if (choices[current_choice] >= max_choice) {
        choices[current_choice]--;
    }
    /* Mutual exclusion: Serial VMU and VMU Game ID */
    if (current_choice == CHOICE_SERIAL_VMU && choices[CHOICE_SERIAL_VMU] != SERIAL_VMU_OFF) {
        choices[CHOICE_VM2_SEND_ALL] = VM2_SEND_OFF;
    }
    if (current_choice == CHOICE_VM2_SEND_ALL && choices[CHOICE_VM2_SEND_ALL] != VM2_SEND_OFF) {
        choices[CHOICE_SERIAL_VMU] = SERIAL_VMU_OFF;
        choices[CHOICE_SERIAL_VMU_MULTISLOT] = SERIAL_VMU_MULTISLOT_OFF;
    }
    /* Reset multi-slot when Serial VMU is turned off */
    if (current_choice == CHOICE_SERIAL_VMU && choices[CHOICE_SERIAL_VMU] == SERIAL_VMU_OFF) {
        choices[CHOICE_SERIAL_VMU_MULTISLOT] = SERIAL_VMU_MULTISLOT_OFF;
    }
    if (current_choice == CHOICE_THEME) {
        menu_region_adj();
    }
    *input_timeout_ptr = INPUT_TIMEOUT;
}

static void
menu_multidisc_prev(void) {
    if (*input_timeout_ptr > 0) {
        return;
    }
    current_choice--;
    int multidisc_len = list_multidisc_length();
    if (current_choice < 0) {
        current_choice = multidisc_len; /* Wrap to Close */
    }
    *input_timeout_ptr = INPUT_TIMEOUT;
}

static void
menu_multidisc_next(void) {
    if (*input_timeout_ptr > 0) {
        return;
    }
    current_choice++;
    int multidisc_len = list_multidisc_length();
    /* Allow one extra option for Close */
    if (current_choice > multidisc_len) {
        current_choice = 0; /* Wrap to first disc */
    }
    *input_timeout_ptr = INPUT_TIMEOUT;
}

static void
menu_accept_multidisc(void) {
    const gd_item** list_multidisc = list_get_multidisc();
    int multidisc_len = list_multidisc_length();

    /* Close option is at index multidisc_len */
    if (current_choice == multidisc_len) {
        menu_leave();
        return;
    }

    if (!cb_multidisc) {
        if (sf_serial_vmu[0] != SERIAL_VMU_OFF && strcmp(list_multidisc[current_choice]->type, "other") != 0) {
            set_cur_game_item(list_multidisc[current_choice]);
            *state_ptr = DRAW_SERIAL_VMU;
            serial_vmu_start_restore(list_multidisc[current_choice], SERIAL_VMU_LAUNCH_DC);
        } else {
            dreamcast_launch_disc(list_multidisc[current_choice]);
        }
    } else {
        if (sf_serial_vmu[0] != SERIAL_VMU_OFF) {
            set_cur_game_item(list_multidisc[current_choice]);
            *state_ptr = DRAW_SERIAL_VMU;
            serial_vmu_start_restore(list_multidisc[current_choice], SERIAL_VMU_LAUNCH_CB);
        } else {
            dreamcast_launch_cb(list_multidisc[current_choice]);
        }
    }
}

static void
menu_exit_prev(void) {
    if (*input_timeout_ptr > 0) {
        return;
    }
    exit_menu_choice--;
    if (exit_menu_choice < 0) {
        exit_menu_choice = exit_menu_num_options - 1; /* Wrap to last option */
    }
    *input_timeout_ptr = INPUT_TIMEOUT;
}

static void
menu_exit_next(void) {
    if (*input_timeout_ptr > 0) {
        return;
    }
    exit_menu_choice++;
    if (exit_menu_choice >= exit_menu_num_options) {
        exit_menu_choice = 0; /* Wrap to first option */
    }
    *input_timeout_ptr = INPUT_TIMEOUT;
}

/* Forward declarations */
static void serial_vmu_start_exit_restore(int mount_disc);
static int serial_vmu_detected_count(void);
static int serial_vmu_cursor_to_device(int cursor);

static void
menu_exit_accept(void) {
    EXIT_OPTION selected = exit_options[exit_menu_choice];

    switch (selected) {
        case EXIT_OPT_CLOSE:
            /* Just close the popup */
            menu_leave();
            break;

        case EXIT_OPT_EXIT_ONLY:
            /* Exit to BIOS without mounting disc */
            /* Send hardcoded "DCBIOS" ID to actual VM2 devices only (not VMUPro/USB4Maple/Pico2Maple) */
            vm2_rescan();
            for (int i = 0; i < vm2_device_count; i++) {
                const char* type = vm2_get_type_name(vm2_devices[i]);
                if (type && strcmp(type, "VM2") == 0) {
                    vm2_set_id(vm2_devices[i], "DCBIOS", NULL);
                }
            }
            exit_to_bios_ex(0, 0);
            break;

        case EXIT_OPT_MOUNT_ONLY:
            /* Mount disc and exit to BIOS (no ID sending) */
            exit_to_bios_ex(1, 0);
            break;

        case EXIT_OPT_SENDID_ONLY:
            /* Send game ID and exit to BIOS (no disc mounting) */
            exit_to_bios_ex(0, 1);
            break;

        case EXIT_OPT_SENDID_MOUNT:
            /* Send game ID + mount disc + exit to BIOS */
            exit_to_bios_ex(1, 1);
            break;

        case EXIT_OPT_RESTORE_MOUNT:
            /* Create/Restore Serial VMU + mount disc + exit to BIOS */
            *state_ptr = DRAW_SERIAL_VMU;
            serial_vmu_start_exit_restore(1);
            break;

        case EXIT_OPT_RESTORE_ONLY:
            /* Create/Restore Serial VMU + exit to BIOS (no disc mounting) */
            *state_ptr = DRAW_SERIAL_VMU;
            serial_vmu_start_exit_restore(0);
            break;

        default: break;
    }
}

void
cb_menu_setup(enum draw_state* state, theme_color* _colors, int* timeout_ptr, uint32_t title_color) {
    common_setup(state, _colors, timeout_ptr);
    menu_title_color = title_color;

    /* Reset selection to first option (Launch) */
    cb_menu_choice = 0;
}

static void
menu_cb_prev(void) {
    if (*input_timeout_ptr > 0) {
        return;
    }
    cb_menu_choice--;
    if (cb_menu_choice < 0) {
        cb_menu_choice = CB_MENU_NUM_OPTIONS - 1; /* Wrap to last option */
    }
    *input_timeout_ptr = INPUT_TIMEOUT;
}

static void
menu_cb_next(void) {
    if (*input_timeout_ptr > 0) {
        return;
    }
    cb_menu_choice++;
    if (cb_menu_choice >= CB_MENU_NUM_OPTIONS) {
        cb_menu_choice = 0; /* Wrap to first option */
    }
    *input_timeout_ptr = INPUT_TIMEOUT;
}

static void
menu_cb_accept(void) {
    CB_OPTION selected = (CB_OPTION)cb_menu_choice;

    switch (selected) {
        case CB_OPT_LAUNCH: start_cb = 1; break;

        case CB_OPT_CLOSE: menu_leave(); break;

        default: break;
    }
}

void
handle_input_menu(enum control input) {
    switch (input) {
        case LEFT: menu_choice_left(); break;
        case RIGHT: menu_choice_right(); break;
        case UP: menu_choice_prev(); break;
        case DOWN: menu_choice_next(); break;
        case START:
        case B: menu_leave(); break;
        case A: menu_accept(); break;
        default: break;
    }
}

void
handle_input_credits(enum control input) {
    switch (input) {
        case A:
        case B:
        case START: credits_leave(); break;
        default: break;
    }
}

void
handle_input_multidisc(enum control input) {
    switch (input) {
        case UP: menu_multidisc_prev(); break;
        case DOWN: menu_multidisc_next(); break;
        case B: menu_leave(); break;
        case A: menu_accept_multidisc(); break;
        default: break;
    }
}

void
handle_input_exit(enum control input) {
    /* All modes use navigable menu */
    switch (input) {
        case UP: menu_exit_prev(); break;
        case DOWN: menu_exit_next(); break;
        case B: menu_leave(); break;
        case A: menu_exit_accept(); break;
        default: break;
    }
}

void
handle_input_codebreaker(enum control input) {
    switch (input) {
        case UP: menu_cb_prev(); break;
        case DOWN: menu_cb_next(); break;
        case B: menu_leave(); break;
        case A: menu_cb_accept(); break;
        default: break;
    }
}

void
draw_menu_op(void) { /* might be useless */ }

static void
string_outer_concat(char* out, const char* left, const char* right, int len) {
    const int input_len = strlen(left) + strlen(right);
    strcpy(out, left);
    for (int i = 0; i < len - input_len; i++) {
        strcat(out, " ");
    }
    strcat(out, right);
}

static void
draw_popup_menu_ex(int x, int y, int width, int height, int ui_mode) {
    const int border_width = 2;
    draw_draw_quad(x - border_width, y - border_width, width + (2 * border_width), height + (2 * border_width),
                   menu_bkg_border_color);
    draw_draw_quad(x, y, width, height, menu_bkg_color);

    if (ui_mode == UI_SCROLL || ui_mode == UI_FOLDERS) {
        /* Top header */
        draw_draw_quad(x, y, width, 20, menu_bkg_border_color);
    }
}

static void
draw_popup_menu(int x, int y, int width, int height) {
    draw_popup_menu_ex(x, y, width, height, sf_ui[0]);
}

/* Poll for VM2 device changes each frame.
 * maple_enum_dev() is free (cached), only rescan if the count changes. */
static void
settings_live_update_vm2(void) {
    static int prev_count = -1;
    int cur_count = 0;
    for (int8_t i = 0; i < 8; i++) {
        int port = i / 2;
        int unit = (i % 2 == 0) ? 1 : 2;
        maple_device_t* dev = maple_enum_dev(port, unit);
        if (dev && (dev->info.functions & MAPLE_FUNC_MEMCARD)) {
            cur_count++;
        }
    }
    if (cur_count != prev_count) {
        prev_count = cur_count;
        vm2_rescan();
    }
}

void
draw_menu_tr(void) {
    z_set_cond(205.0f);
    /* Poll for VM2 device changes */
    settings_live_update_vm2();
    if (sf_ui[0] == UI_SCROLL || sf_ui[0] == UI_FOLDERS) {
        /* Menu size and placement */
        const int line_height = 24;
        const int width = 320;
        /* Calculate visible options for height.
         * Extra rows after options: Save/Apply/Credits, spacing, combined version = 3 rows
         * The +3 in the height formula accounts for these rows */
        int visible_options = MENU_OPTIONS - 1; /* Hide BEEP */
        if (sf_ui[0] == UI_SCROLL) {
            visible_options -= 4; /* Hide Aspect, FOLDERS_ART, FOLDERS_ITEM_DETAILS, CLOCK, MULTIDISC_GROUPING (5 items,
                                     -1 for padding) */
        } else if (sf_ui[0] == UI_FOLDERS) {
            visible_options -=
                4; /* Hide Aspect, Filter, SCROLL_ART, SCROLL_INDEX, DISC_DETAILS (5 items, -1 for padding) */
            /* Dynamically hide MULTIDISC_GROUPING when Multi-Disc is "Show All" */
            if (choices[CHOICE_MULTIDISC] == MULTIDISC_SHOW) {
                visible_options -= 1;
            }
        }
        /* Dynamically hide VM2_SEND_ALL when no VM2 devices detected or Serial VMU is active */
        if (vm2_device_count == 0 || choices[CHOICE_SERIAL_VMU] != SERIAL_VMU_OFF) {
            visible_options -= 1;
        }
        /* Dynamically hide SERIAL_VMU when no SD card, or VMU Game ID active with VM2 present */
        if (!savefile_sd_available() || (choices[CHOICE_VM2_SEND_ALL] != VM2_SEND_OFF && vm2_device_count > 0)) {
            visible_options -= 1;
        }
        /* Dynamically hide SERIAL_VMU_MULTISLOT when Serial VMU off, no SD, or VMU Game ID active with VM2 present */
        if (choices[CHOICE_SERIAL_VMU] == SERIAL_VMU_OFF || !savefile_sd_available()
            || (choices[CHOICE_VM2_SEND_ALL] != VM2_SEND_OFF && vm2_device_count > 0)) {
            visible_options -= 1;
        }
        const int height = (visible_options + 3) * line_height + 4;
        const int x = (640 / 2) - (width / 2);
        const int y = (480 / 2) - (height / 2);
        const int x_item = x + 8; /* 8px left margin */

        char line_buf[70];

        /* Draw a popup in the middle of the screen */
        draw_popup_menu(x, y, width, height);

        /* overlay our text on top with options */
        int cur_y = y + 2;
        font_bmp_begin_draw();
        font_bmp_set_color(menu_title_color);

        font_bmp_draw_main(width - (8 * 8 / 2), cur_y, "Settings");

        cur_y += 2;
        for (int i = 0; i < MENU_CHOICES; i++) {
            /* Skip SCROLL_ART option in non-Scroll modes */
            if (i == CHOICE_SCROLL_ART && sf_ui[0] != UI_SCROLL) {
                continue;
            }
            /* Skip SCROLL_INDEX option in non-Scroll modes */
            if (i == CHOICE_SCROLL_INDEX && sf_ui[0] != UI_SCROLL) {
                continue;
            }
            /* Skip DISC_DETAILS option in non-Scroll modes */
            if (i == CHOICE_DISC_DETAILS && sf_ui[0] != UI_SCROLL) {
                continue;
            }
            /* Skip MULTIDISC_GROUPING option in non-Folders modes or when Multi-Disc is "Show All" */
            if (i == CHOICE_MULTIDISC_GROUPING
                && (sf_ui[0] != UI_FOLDERS || choices[CHOICE_MULTIDISC] == MULTIDISC_SHOW)) {
                continue;
            }
            /* Skip FOLDERS_ART option in non-Folders modes */
            if (i == CHOICE_FOLDERS_ART && sf_ui[0] != UI_FOLDERS) {
                continue;
            }
            /* Skip FOLDERS_ITEM_DETAILS option in non-Folders modes */
            if (i == CHOICE_FOLDERS_ITEM_DETAILS && sf_ui[0] != UI_FOLDERS) {
                continue;
            }
            /* Skip MARQUEE_SPEED option in non-Scroll/Folders modes */
            if (i == CHOICE_MARQUEE_SPEED && sf_ui[0] != UI_SCROLL && sf_ui[0] != UI_FOLDERS) {
                continue;
            }
            /* Skip CLOCK option in non-Folders modes */
            if (i == CHOICE_CLOCK && sf_ui[0] != UI_FOLDERS) {
                continue;
            }
            /* Skip VM2_SEND_ALL option when no VM2 devices detected or Serial VMU is active */
            if (i == CHOICE_VM2_SEND_ALL && (vm2_device_count == 0 || choices[CHOICE_SERIAL_VMU] != SERIAL_VMU_OFF)) {
                continue;
            }
            /* Skip SERIAL_VMU when no SD card, or VMU Game ID active with VM2 present */
            if (i == CHOICE_SERIAL_VMU
                && (!savefile_sd_available()
                    || (choices[CHOICE_VM2_SEND_ALL] != VM2_SEND_OFF && vm2_device_count > 0))) {
                continue;
            }
            /* Skip SERIAL_VMU_MULTISLOT when Serial VMU off, no SD, or VMU Game ID active with VM2 present */
            if (i == CHOICE_SERIAL_VMU_MULTISLOT
                && (choices[CHOICE_SERIAL_VMU] == SERIAL_VMU_OFF || !savefile_sd_available()
                    || (choices[CHOICE_VM2_SEND_ALL] != VM2_SEND_OFF && vm2_device_count > 0))) {
                continue;
            }
            /* Skip Aspect in Scroll mode (not used) */
            if (i == CHOICE_ASPECT && sf_ui[0] == UI_SCROLL) {
                continue;
            }
            /* Skip Aspect/Filter in Folders mode */
            if (sf_ui[0] == UI_FOLDERS && (i == CHOICE_ASPECT || i == CHOICE_FILTER)) {
                continue;
            }
            /* Skip BEEP option (disabled/commented out) */
            if (i == CHOICE_BEEP) {
                continue;
            }
            cur_y += line_height;
            if (i == current_choice) {
                font_bmp_set_color(highlight_color);
            } else {
                font_bmp_set_color(text_color);
            }
            if (i == CHOICE_REGION && (choices[i] >= REGION_CHOICES)) {
                string_outer_concat(line_buf, menu_choice_text[i], custom_theme_text[(int)choices[i] - REGION_CHOICES],
                                    38);
            } else if (i == CHOICE_SORT && sf_ui[0] == UI_FOLDERS) {
                /* In Folders mode, use Folders-specific sort text and clamp value */
                int sort_idx = choices[i] < SORT_CHOICES_FOLDERS ? choices[i] : 0;
                string_outer_concat(line_buf, menu_choice_text[i], sort_choice_text_folders[sort_idx], 38);
            } else {
                string_outer_concat(line_buf, menu_choice_text[i], menu_choice_array[i][(int)choices[i]], 38);
            }
            font_bmp_draw_main(x_item, cur_y, line_buf);
        }

        /* Draw Save/Apply/Credits on one line */
        uint32_t save_color =
            ((current_choice == CHOICE_SAVE) && (choices[CHOICE_SAVE] == 0) ? highlight_color : text_color);
        uint32_t apply_color =
            ((current_choice == CHOICE_SAVE) && (choices[CHOICE_SAVE] == 1) ? highlight_color : text_color);
        uint32_t credits_color = (current_choice == CHOICE_CREDITS ? highlight_color : text_color);
        cur_y += line_height;
        /* Save at left, Apply in middle, Credits at right. Equal 24px spacing */
        /* Save/Load(72px) + gap(24px) + Apply(40px) + gap(24px) + Credits(56px) = 216px total */
        font_bmp_set_color(save_color);
        font_bmp_draw_main(640 / 2 - 108, cur_y, save_choice_text[0]);
        font_bmp_set_color(apply_color);
        font_bmp_draw_main(640 / 2 - 12, cur_y, save_choice_text[1]);
        font_bmp_set_color(credits_color);
        font_bmp_draw_main(640 / 2 + 52, cur_y, credits_text[0]);

        /* Add empty line for spacing */
        cur_y += line_height;

        /* Draw GDEMU + openMenu version on one line (non-selectable) */
        uint8_t version_buffer[8] = {0};
        uint32_t version_size = 8;
        char combined_str[80];
        if (gdemu_get_version(version_buffer, &version_size) == 0) {
            snprintf(combined_str, sizeof(combined_str), "GDEMU %d.%02x.%d - openMenu %s", version_buffer[7],
                     version_buffer[6], version_buffer[5], OPENMENU_BUILD_VERSION);
        } else {
            snprintf(combined_str, sizeof(combined_str), "GDEMU N/A - openMenu %s", OPENMENU_BUILD_VERSION);
        }
        font_bmp_set_color(text_color);
        cur_y += line_height;
        /* Center based on actual string length (8 pixels per character) */
        int str_pixel_width = strlen(combined_str) * 8;
        font_bmp_draw_main(640 / 2 - (str_pixel_width / 2), cur_y, combined_str);

    } else {
        /* Menu size and placement (many options not shown in LineDesc/Grid3) */
        const int line_height = 26;
        const int width = 400;
        /* Exclude: BEEP, SCROLL_ART, SCROLL_INDEX, DISC_DETAILS, FOLDERS_ART, FOLDERS_ITEM_DETAILS, MARQUEE_SPEED,
         * CLOCK, MULTIDISC_GROUPING (9 items, -1 for padding) */
        int visible_options = MENU_OPTIONS - 8;
        /* Dynamically hide VM2_SEND_ALL when no VM2 devices detected or Serial VMU is active */
        if (vm2_device_count == 0 || choices[CHOICE_SERIAL_VMU] != SERIAL_VMU_OFF) {
            visible_options -= 1;
        }
        /* Dynamically hide SERIAL_VMU when no SD card, or VMU Game ID active with VM2 present */
        if (!savefile_sd_available() || (choices[CHOICE_VM2_SEND_ALL] != VM2_SEND_OFF && vm2_device_count > 0)) {
            visible_options -= 1;
        }
        /* Dynamically hide SERIAL_VMU_MULTISLOT when Serial VMU off, no SD, or VMU Game ID active with VM2 present */
        if (choices[CHOICE_SERIAL_VMU] == SERIAL_VMU_OFF || !savefile_sd_available()
            || (choices[CHOICE_VM2_SEND_ALL] != VM2_SEND_OFF && vm2_device_count > 0)) {
            visible_options -= 1;
        }
        const int height =
            (visible_options + 3) * line_height - line_height / 4
            + line_height / 2; /* Add space for combined version string and extra spacing before buttons */
        const int x = (640 / 2) - (width / 2);
        const int y = (480 / 2) - (height / 2); /* Vertically centered */
        const int x_item = x + 4;
        const int x_choice = 344 + 24 + 20 + 25; /* magic :( */

        /* Draw a popup in the middle of the screen */
        draw_popup_menu(x, y, width, height);

        /* overlay our text on top with options */
        int cur_y = y + 2;
        font_bmf_begin_draw();
        font_bmf_set_height(24.0f);

        font_bmf_draw(x_item, cur_y, text_color, "Settings");

        cur_y += line_height / 4;
        for (int i = 0; i < MENU_CHOICES; i++) {
            /* Skip SCROLL_ART option in non-Scroll modes */
            if (i == CHOICE_SCROLL_ART && sf_ui[0] != UI_SCROLL) {
                continue;
            }
            /* Skip SCROLL_INDEX option in non-Scroll modes */
            if (i == CHOICE_SCROLL_INDEX && sf_ui[0] != UI_SCROLL) {
                continue;
            }
            /* Skip DISC_DETAILS option in non-Scroll modes */
            if (i == CHOICE_DISC_DETAILS && sf_ui[0] != UI_SCROLL) {
                continue;
            }
            /* Skip FOLDERS_ART option in non-Folders modes */
            if (i == CHOICE_FOLDERS_ART && sf_ui[0] != UI_FOLDERS) {
                continue;
            }
            /* Skip FOLDERS_ITEM_DETAILS option in non-Folders modes */
            if (i == CHOICE_FOLDERS_ITEM_DETAILS && sf_ui[0] != UI_FOLDERS) {
                continue;
            }
            /* Skip MARQUEE_SPEED option in non-Scroll/Folders modes */
            if (i == CHOICE_MARQUEE_SPEED && sf_ui[0] != UI_SCROLL && sf_ui[0] != UI_FOLDERS) {
                continue;
            }
            /* Skip CLOCK option in non-Folders modes */
            if (i == CHOICE_CLOCK && sf_ui[0] != UI_FOLDERS) {
                continue;
            }
            /* Skip VM2_SEND_ALL option when no VM2 devices detected or Serial VMU is active */
            if (i == CHOICE_VM2_SEND_ALL && (vm2_device_count == 0 || choices[CHOICE_SERIAL_VMU] != SERIAL_VMU_OFF)) {
                continue;
            }
            /* Skip SERIAL_VMU when no SD card, or VMU Game ID active with VM2 present */
            if (i == CHOICE_SERIAL_VMU
                && (!savefile_sd_available()
                    || (choices[CHOICE_VM2_SEND_ALL] != VM2_SEND_OFF && vm2_device_count > 0))) {
                continue;
            }
            /* Skip SERIAL_VMU_MULTISLOT when Serial VMU off, no SD, or VMU Game ID active with VM2 present */
            if (i == CHOICE_SERIAL_VMU_MULTISLOT
                && (choices[CHOICE_SERIAL_VMU] == SERIAL_VMU_OFF || !savefile_sd_available()
                    || (choices[CHOICE_VM2_SEND_ALL] != VM2_SEND_OFF && vm2_device_count > 0))) {
                continue;
            }
            /* Skip MULTIDISC_GROUPING option in non-Folders modes or when Multi-Disc is "Show All" */
            if (i == CHOICE_MULTIDISC_GROUPING
                && (sf_ui[0] != UI_FOLDERS || choices[CHOICE_MULTIDISC] == MULTIDISC_SHOW)) {
                continue;
            }
            /* Skip Aspect/Sort/Filter in Folders mode */
            if (sf_ui[0] == UI_FOLDERS && (i == CHOICE_ASPECT || i == CHOICE_SORT || i == CHOICE_FILTER)) {
                continue;
            }
            /* Skip BEEP option (disabled/commented out) */
            if (i == CHOICE_BEEP) {
                continue;
            }
            cur_y += line_height;
            uint32_t temp_color = text_color;
            if (i == current_choice) {
                temp_color = highlight_color;
            }
            font_bmf_draw(x_item, cur_y, temp_color,
                          i == CHOICE_SERIAL_VMU_MULTISLOT ? "Serial VMU Slots" : menu_choice_text[i]);

            if (i == CHOICE_REGION && (choices[i] >= REGION_CHOICES)) {
                font_bmf_draw_centered(x_choice, cur_y, temp_color,
                                       custom_theme_text[(int)choices[i] - REGION_CHOICES]);
            } else {
                font_bmf_draw_centered(x_choice, cur_y, temp_color, menu_choice_array[i][(int)choices[i]]);
            }
        }

        /* Extra spacing before buttons, same as spacing after buttons to version strings */
        cur_y += line_height + line_height / 2;

        /* Draw Save/Apply/Credits on one line using smaller font */
        /* Each button centered in its own 1/3 column of the window */
        uint32_t save_color =
            ((current_choice == CHOICE_SAVE) && (choices[CHOICE_SAVE] == 0) ? highlight_color : text_color);
        uint32_t apply_color =
            ((current_choice == CHOICE_SAVE) && (choices[CHOICE_SAVE] == 1) ? highlight_color : text_color);
        uint32_t credits_color = ((current_choice == CHOICE_CREDITS) ? highlight_color : text_color);
        font_bmf_set_height(20.0f);
        font_bmf_draw_centered(x + width / 6, cur_y, save_color, save_choice_text[0]);
        font_bmf_draw_centered(x + width / 2, cur_y, apply_color, save_choice_text[1]);
        font_bmf_draw_centered(x + width * 5 / 6, cur_y, credits_color, credits_text[0]);
        font_bmf_set_height_default();

        /* Add empty line for spacing */
        cur_y += line_height;

        /* Draw GDEMU + openMenu version on one line (non-selectable, smaller font) */
        uint8_t version_buffer[8] = {0};
        uint32_t version_size = 8;
        char combined_str[80];
        if (gdemu_get_version(version_buffer, &version_size) == 0) {
            snprintf(combined_str, sizeof(combined_str), "GDEMU  %d.%02x.%d  -  openMenu  %s", version_buffer[7],
                     version_buffer[6], version_buffer[5], OPENMENU_BUILD_VERSION);
        } else {
            snprintf(combined_str, sizeof(combined_str), "GDEMU  N/A  -  openMenu  %s", OPENMENU_BUILD_VERSION);
        }
        cur_y += line_height / 2;
        font_bmf_set_height(20.0f);
        font_bmf_draw_centered(640 / 2, cur_y, text_color, combined_str);

        font_bmf_set_height_default();
    }
}

void
draw_credits_op(void) { /* Again nothing... */ }

void
draw_credits_tr(void) {
    z_set_cond(205.0f);

    if (sf_ui[0] == UI_SCROLL || sf_ui[0] == UI_FOLDERS) {
        /* Menu size and placement */
        const int line_height = 24;
        const int width = 320;
        const int height = (num_credits + 1) * line_height + 4;
        const int x = (640 / 2) - (width / 2);
        const int y = (480 / 2) - (height / 2);
        const int x_item = x + 8; /* 8px left margin */

        char line_buf[65];

        /* Draw a popup in the middle of the screen */
        draw_popup_menu(x, y, width, height);

        /* overlay our text on top with options */
        int cur_y = y + 2;
        font_bmp_begin_draw();
        font_bmp_set_color(sf_ui[0] == UI_FOLDERS ? menu_title_color : text_color);

        font_bmp_draw_main(width - (8 * 8 / 2), cur_y, "Credits");
        font_bmp_set_color(sf_ui[0] == UI_FOLDERS ? text_color : highlight_color);

        cur_y += 2;
        for (int i = 0; i < num_credits; i++) {
            cur_y += line_height;
            string_outer_concat(line_buf, credits[i].contributor, credits[i].role, 38);
            font_bmp_draw_main(x_item, cur_y, line_buf);
        }

    } else {
        /* Menu size and placement */
        const int line_height = 26;
        const int width = 560;
        const int height = (num_credits + 2) * line_height;
        const int x = (640 / 2) - (width / 2);
        const int y = (480 / 2) - (height / 2);
        const int x_choice = 344 + 24 + 60; /* magic :( */
        const int x_item = x + 4;

        /* Draw a popup in the middle of the screen */
        draw_popup_menu(x, y, width, height);

        /* overlay our text on top with options */
        int cur_y = y + 2;
        font_bmf_begin_draw();
        font_bmf_set_height(24.0f);

        font_bmf_draw(x_item, cur_y, text_color, "Credits");

        cur_y += line_height / 4;
        for (int i = 0; i < num_credits; i++) {
            cur_y += line_height;
            font_bmf_draw(x_item, cur_y, highlight_color, credits[i].contributor);
            font_bmf_draw_centered(x_choice, cur_y, highlight_color, credits[i].role);
        }
    }
}

void
draw_multidisc_op(void) { /* Again nothing...Still... */ }

void
draw_multidisc_tr(void) {
    const gd_item** list_multidisc = list_get_multidisc();
    int multidisc_len = list_multidisc_length();

    z_set_cond(205.0f);
    if (sf_ui[0] == UI_SCROLL || sf_ui[0] == UI_FOLDERS) {
        /* Menu size and placement. Width auto-sized based on disc labels */
        const int line_height = 24;
        const int title_gap = line_height / 2;
        const int title_width = 10 * 8; /* "Multi-Disc" = 10 chars */
        const int padding = 16;         /* 8px margin on each side */
        const int max_name_chars = 35;  /* Maximum characters for game name */
        char line_buf[48];
        char temp_game_name[36];

        /* Find the longest disc label to determine popup width */
        int max_label_len = 0;
        for (int i = 0; i < multidisc_len; i++) {
            int name_len = strlen(list_multidisc[i]->name);
            if (name_len > max_name_chars) {
                name_len = max_name_chars;
            }
            /* Account for " #N" or " #NN" suffix (space + # + 1-2 digits) */
            int disc_num = gd_item_disc_num(list_multidisc[i]->disc);
            int suffix_len = (disc_num >= 10) ? 4 : 3;
            int label_len = name_len + suffix_len;
            if (label_len > max_label_len) {
                max_label_len = label_len;
            }
        }

        /* Width is the larger of title or max label, plus padding */
        const int content_width = max_label_len * 8;
        const int width = (content_width > title_width ? content_width : title_width) + padding;
        const int height = (multidisc_len + 2) * line_height + (line_height / 2) + title_gap;
        const int x = (640 / 2) - (width / 2);
        const int y = (480 / 2) - (height / 2);
        const int x_item = x + (padding / 2);

        /* Draw a popup in the middle of the screen */
        draw_popup_menu(x, y, width, height);

        /* overlay our text on top with options */
        int cur_y = y + 2;
        font_bmp_begin_draw();
        font_bmp_set_color(sf_ui[0] == UI_FOLDERS ? menu_title_color : text_color);

        font_bmp_draw_main(x + width / 2 - (10 * 8 / 2), cur_y, "Multi-Disc");

        cur_y += title_gap; /* Add spacing after title */
        for (int i = 0; i < multidisc_len; i++) {
            cur_y += line_height;
            if (i == current_choice) {
                font_bmp_set_color(highlight_color);
            } else {
                font_bmp_set_color(text_color);
            }
            const int disc_num = gd_item_disc_num(list_multidisc[i]->disc);
            strncpy(temp_game_name, list_multidisc[i]->name, sizeof(temp_game_name) - 1);
            temp_game_name[sizeof(temp_game_name) - 1] = '\0';
            /* Add ellipsis if name was truncated */
            if (strlen(list_multidisc[i]->name) >= sizeof(temp_game_name)) {
                strcpy(&temp_game_name[sizeof(temp_game_name) - 4], "...");
            }
            /* Format as "GameName #N" without fixed-width padding */
            snprintf(line_buf, sizeof(line_buf), "%s #%d", temp_game_name, disc_num);
            font_bmp_draw_main(x_item, cur_y, line_buf);
        }

        /* Close option */
        cur_y += line_height;
        font_bmp_set_color(current_choice == multidisc_len ? highlight_color : text_color);
        font_bmp_draw_main(x_item, cur_y, "Close");
    } else {
        /* Menu size and placement */
        const int line_height = 32;
        const int width = 300;
        const int height = (multidisc_len + 2) * line_height + (line_height / 2);
        const int x = (640 / 2) - (width / 2);
        const int y = (480 / 2) - (height / 2);
        const int x_item = x + 4;
        char line_buf[65];
        char temp_game_name[62];

        /* Draw a popup in the middle of the screen */
        draw_popup_menu(x, y, width, height);

        /* overlay our text on top with options */
        int cur_y = y + 2;
        font_bmf_begin_draw();
        font_bmf_set_height_default();

        font_bmf_draw_centered(x + width / 2, cur_y, text_color, "Multi-Disc");

        cur_y += line_height / 4;

        for (int i = 0; i < multidisc_len; i++) {
            cur_y += line_height;
            uint32_t temp_color = text_color;
            if (i == current_choice) {
                temp_color = highlight_color;
            }
            const int disc_num = gd_item_disc_num(list_multidisc[i]->disc);
            strncpy(temp_game_name, list_multidisc[i]->name, sizeof(temp_game_name) - 1);
            temp_game_name[sizeof(temp_game_name) - 1] = '\0';
            /* Add ellipsis if name was truncated */
            if (strlen(list_multidisc[i]->name) >= sizeof(temp_game_name)) {
                strcpy(&temp_game_name[sizeof(temp_game_name) - 4], "...");
            }
            snprintf(line_buf, 69, "%s #%d", temp_game_name, disc_num);
            font_bmf_draw_auto_size(x_item, cur_y, temp_color, line_buf, width - 4);
        }

        /* Close option */
        cur_y += line_height;
        font_bmf_draw(x_item, cur_y, current_choice == multidisc_len ? highlight_color : text_color, "Close");
    }
}

void
draw_exit_op(void) { /* Again nothing...Still...Ugh... */ }

void
draw_exit_tr(void) {
    z_set_cond(205.0f);

    if (sf_ui[0] == UI_SCROLL || sf_ui[0] == UI_FOLDERS) {
        /* Menu size and placement. Width calculated based on actual options */
        const int line_height = 24;
        const int title_gap = 2;
        const int padding = 16;         /* 8px margin on each side */
        const int title_width = 12 * 8; /* "Exit to BIOS" = 12 chars */

        /* Find the longest option text in the current menu */
        int max_option_len = 0;
        for (int i = 0; i < exit_menu_num_options; i++) {
            int len = strlen(exit_option_text[exit_options[i]]);
            if (len > max_option_len) {
                max_option_len = len;
            }
        }

        /* Width is the larger of title or max option, plus padding */
        const int content_width = max_option_len * 8;
        const int width = (content_width > title_width ? content_width : title_width) + padding;
        int height = (exit_menu_num_options + 1) * line_height + 4;
        const int is_game_type = (cur_game_item && strcmp(cur_game_item->type, "game") == 0);
        int num_info_lines = 0;
        if (is_game_type) {
            num_info_lines = count_wrap_lines(exit_info_text, max_option_len);
            height += line_height + num_info_lines * line_height;
        }
        const int x = (640 / 2) - (width / 2);
        const int y = (480 / 2) - (height / 2);
        const int x_item = x + (padding / 2);

        draw_popup_menu(x, y, width, height);

        int cur_y = y + 2;
        font_bmp_begin_draw();
        font_bmp_set_color(menu_title_color);

        font_bmp_draw_main(x + width / 2 - (12 * 8 / 2), cur_y, "Exit to BIOS");

        cur_y += title_gap;
        for (int i = 0; i < exit_menu_num_options; i++) {
            cur_y += line_height;
            if (i == exit_menu_choice) {
                font_bmp_set_color(highlight_color);
            } else {
                font_bmp_set_color(text_color);
            }
            font_bmp_draw_main(x_item, cur_y, exit_option_text[exit_options[i]]);
        }
        if (is_game_type) {
            cur_y += 2 * line_height; /* blank line */
            font_bmp_set_color(text_color);
            draw_wrap_text_bmp(exit_info_text, x_item, cur_y, max_option_len, line_height);
        }
    } else {
        /* LineDesc/Grid modes. Dynamic menu with larger font */
        const int line_height = 32;
        const int title_gap = line_height / 4;
        const int padding = 20;

        /* Find the longest option text in the current menu */
        int max_option_len = 0;
        for (int i = 0; i < exit_menu_num_options; i++) {
            int len = strlen(exit_option_text[exit_options[i]]);
            if (len > max_option_len) {
                max_option_len = len;
            }
        }

        /* Estimate width based on font (roughly 10-12px per char for bmf font) */
        const int content_width = max_option_len * 10;
        const int title_width = 12 * 10; /* "Exit to BIOS" */
        const int width = (content_width > title_width ? content_width : title_width) + padding;
        int height = (exit_menu_num_options + 1) * line_height + (line_height / 2);
        const int is_game_type = (cur_game_item && strcmp(cur_game_item->type, "game") == 0);
        if (is_game_type) {
            int info_chars_per_line = (content_width > title_width ? content_width : title_width) / 6;
            int num_info_lines = count_wrap_lines(exit_info_text, info_chars_per_line);
            height += line_height + line_height / 2 + num_info_lines * 20 + 10;
        }
        const int x = (640 / 2) - (width / 2);
        const int y = (480 / 2) - (height / 2);
        const int x_item = x + 10;

        draw_popup_menu(x, y, width, height);

        int cur_y = y + 2;
        font_bmf_begin_draw();
        font_bmf_set_height_default();

        font_bmf_draw_centered(x + width / 2, cur_y, text_color, "Exit to BIOS");

        cur_y += title_gap;
        for (int i = 0; i < exit_menu_num_options; i++) {
            cur_y += line_height;
            uint32_t temp_color = text_color;
            if (i == exit_menu_choice) {
                temp_color = highlight_color;
            }
            font_bmf_draw_auto_size(x_item, cur_y, temp_color, exit_option_text[exit_options[i]], width - 20);
        }
        if (is_game_type) {
            cur_y += 2 * line_height; /* blank line */
            font_bmf_set_height(16.0f);
            font_bmf_draw_sub_wrap(x_item, cur_y, text_color, exit_info_text, width - 20);
        }
    }
}

void
draw_codebreaker_op(void) { /* Again nothing...Still...Ugh... */ }

void
draw_codebreaker_tr(void) {
    z_set_cond(205.0f);

    if (sf_ui[0] == UI_SCROLL || sf_ui[0] == UI_FOLDERS) {
        /* Menu size and placement. Width calculated based on actual options */
        const int line_height = 24;
        const int title_gap = 2;
        const int padding = 16;         /* 8px margin on each side */
        const int title_width = 10 * 8; /* "Use Cheats" = 10 chars */

        /* Find the longest option text */
        int max_option_len = 0;
        for (int i = 0; i < CB_MENU_NUM_OPTIONS; i++) {
            int len = strlen(cb_option_text[i]);
            if (len > max_option_len) {
                max_option_len = len;
            }
        }

        /* Width is the larger of title or max option, plus padding */
        const int content_width = max_option_len * 8;
        const int width = (content_width > title_width ? content_width : title_width) + padding;
        const int height = (CB_MENU_NUM_OPTIONS + 1) * line_height + 4;
        const int x = (640 / 2) - (width / 2);
        const int y = (480 / 2) - (height / 2);
        const int x_item = x + (padding / 2);

        draw_popup_menu(x, y, width, height);

        int cur_y = y + 2;
        font_bmp_begin_draw();
        font_bmp_set_color(menu_title_color);

        font_bmp_draw_main(x + width / 2 - (10 * 8 / 2), cur_y, "Use Cheats");

        cur_y += title_gap;
        for (int i = 0; i < CB_MENU_NUM_OPTIONS; i++) {
            cur_y += line_height;
            if (i == cb_menu_choice) {
                font_bmp_set_color(highlight_color);
            } else {
                font_bmp_set_color(text_color);
            }
            font_bmp_draw_main(x_item, cur_y, cb_option_text[i]);
        }
    } else {
        /* LineDesc/Grid modes. Dynamic menu with larger font */
        const int line_height = 32;
        const int title_gap = line_height / 4;
        const int padding = 20;

        /* Find the longest option text */
        int max_option_len = 0;
        for (int i = 0; i < CB_MENU_NUM_OPTIONS; i++) {
            int len = strlen(cb_option_text[i]);
            if (len > max_option_len) {
                max_option_len = len;
            }
        }

        /* Estimate width based on font (roughly 10-12px per char for bmf font) */
        const int content_width = max_option_len * 10;
        const int title_width = 10 * 10; /* "Use Cheats" */
        const int width = (content_width > title_width ? content_width : title_width) + padding;
        const int height = (CB_MENU_NUM_OPTIONS + 1) * line_height + (line_height / 2);
        const int x = (640 / 2) - (width / 2);
        const int y = (480 / 2) - (height / 2);
        const int x_item = x + 10;

        draw_popup_menu(x, y, width, height);

        int cur_y = y + 2;
        font_bmf_begin_draw();
        font_bmf_set_height_default();

        font_bmf_draw_centered(x + width / 2, cur_y, text_color, "Use Cheats");

        cur_y += title_gap;
        for (int i = 0; i < CB_MENU_NUM_OPTIONS; i++) {
            cur_y += line_height;
            uint32_t temp_color = text_color;
            if (i == cb_menu_choice) {
                temp_color = highlight_color;
            }
            font_bmf_draw_auto_size(x_item, cur_y, temp_color, cb_option_text[i], width - 20);
        }
    }
}

/* PSX Launcher popup functions */
static void
menu_psx_launcher_prev(void) {
    if (*input_timeout_ptr > 0) {
        return;
    }
    psx_launcher_choice--;
    if (psx_launcher_choice < 0) {
        psx_launcher_choice = 2; /* Wrap to last option */
    }
    *input_timeout_ptr = INPUT_TIMEOUT;
}

static void
menu_psx_launcher_next(void) {
    if (*input_timeout_ptr > 0) {
        return;
    }
    psx_launcher_choice++;
    if (psx_launcher_choice > 2) {
        psx_launcher_choice = 0; /* Wrap to first option */
    }
    *input_timeout_ptr = INPUT_TIMEOUT;
}

static void
menu_accept_psx_launcher(void) {
    if (psx_launcher_choice == 0) {
        if (sf_serial_vmu[0] != SERIAL_VMU_OFF && strcmp(cur_game_item->type, "other") != 0) {
            *state_ptr = DRAW_SERIAL_VMU;
            serial_vmu_start_restore(cur_game_item, SERIAL_VMU_LAUNCH_BLEEM);
        } else {
            bleem_launch(cur_game_item);
        }
    } else if (psx_launcher_choice == 1) {
        if (sf_serial_vmu[0] != SERIAL_VMU_OFF && strcmp(cur_game_item->type, "other") != 0) {
            *state_ptr = DRAW_SERIAL_VMU;
            serial_vmu_start_restore(cur_game_item, SERIAL_VMU_LAUNCH_BLOOM);
        } else {
            bloom_launch(cur_game_item);
        }
    } else {
        /* Close */
        menu_leave();
    }
}

void
handle_input_psx_launcher(enum control input) {
    switch (input) {
        case UP: menu_psx_launcher_prev(); break;
        case DOWN: menu_psx_launcher_next(); break;
        case B: menu_leave(); break;
        case A: menu_accept_psx_launcher(); break;
        default: break;
    }
}

void
draw_psx_launcher_op(void) { /* Nothing needed */ }

void
draw_psx_launcher_tr(void) {
    z_set_cond(205.0f);

    if (sf_ui[0] == UI_SCROLL || sf_ui[0] == UI_FOLDERS) {
        /* Menu size and placement. Width based on title "PlayStation Launcher" (20 chars) */
        const int line_height = 24;
        const int title_gap = 2;
        const int padding = 16;             /* 8px margin on each side */
        const int width = 20 * 8 + padding; /* 176 */
        const int height = 4 * line_height + 4;
        const int x = (640 / 2) - (width / 2);
        const int y = (480 / 2) - (height / 2);
        const int x_item = x + (padding / 2);

        draw_popup_menu(x, y, width, height);

        int cur_y = y + 2;
        font_bmp_begin_draw();
        font_bmp_set_color(sf_ui[0] == UI_FOLDERS ? menu_title_color : text_color);

        font_bmp_draw_main(x + width / 2 - (20 * 8 / 2), cur_y, "PlayStation Launcher");

        cur_y += title_gap;
        cur_y += line_height;
        font_bmp_set_color(psx_launcher_choice == 0 ? highlight_color : text_color);
        font_bmp_draw_main(x_item, cur_y, "Bleemcast!");

        cur_y += line_height;
        font_bmp_set_color(psx_launcher_choice == 1 ? highlight_color : text_color);
        font_bmp_draw_main(x_item, cur_y, "Bloom");

        cur_y += line_height;
        font_bmp_set_color(psx_launcher_choice == 2 ? highlight_color : text_color);
        font_bmp_draw_main(x_item, cur_y, "Close");
    } else {
        /* LineDesc/Grid modes, keep original sizing */
        const int line_height = 32;
        const int width = 200;
        const int height = 4 * line_height + (line_height / 2);
        const int x = (640 / 2) - (width / 2);
        const int y = (480 / 2) - (height / 2);
        const int x_item = x + 10;

        draw_popup_menu(x, y, width, height);

        int cur_y = y + 2;
        font_bmf_begin_draw();
        font_bmf_set_height_default();

        font_bmf_draw_centered(x + width / 2, cur_y, text_color, "PlayStation Launcher");

        cur_y += line_height;
        font_bmf_draw(x_item, cur_y, psx_launcher_choice == 0 ? highlight_color : text_color, "Bleemcast!");

        cur_y += line_height;
        font_bmf_draw(x_item, cur_y, psx_launcher_choice == 1 ? highlight_color : text_color, "Bloom");

        cur_y += line_height;
        font_bmf_draw(x_item, cur_y, psx_launcher_choice == 2 ? highlight_color : text_color, "Close");
    }
}

#pragma region SaveLoad_Menu

/* Save/Load sub-states */
typedef enum SAVELOAD_STATE {
    SAVELOAD_BROWSE = 0, /* Browsing device list */
    SAVELOAD_CONFIRM,    /* Confirming overwrite */
    SAVELOAD_BUSY,       /* Operation in progress */
    SAVELOAD_RESULT      /* Showing result message */
} SAVELOAD_STATE;

/* Save status for each device */
typedef enum SAVE_STATUS {
    SAVE_NONE = 0, /* No save file, can create */
    SAVE_CURRENT,  /* Up-to-date save file */
    SAVE_OLD,      /* Older version, will upgrade */
    SAVE_INVALID,  /* Corrupt/invalid, must overwrite */
    SAVE_NO_SPACE, /* No save, not enough space */
    SAVE_FUTURE    /* Save from newer program version */
} SAVE_STATUS;

/* Information about a VMU slot */
typedef struct vmu_slot_info {
    int8_t device_id;        /* Crayon device ID (0-7) */
    int8_t crayon_status;    /* Raw CRAYON_SF_STATUS_* value */
    SAVE_STATUS save_status; /* Friendly status enum */
    int has_device;          /* 1 if device present */
    char type_name[12];      /* "VMU", "VM2", "VMUPro", "USB4MAPLE", "Pico2Maple", "None" */
    int is_startup_source;   /* 1 if this is where settings were loaded at boot */
} vmu_slot_info;

/* Save/Load window state */
static vmu_slot_info saveload_slots[8];   /* All 8 VMU slots */
static int saveload_cursor = 0;           /* Current cursor position in full list */
static int saveload_selected_device = -1; /* Index of selected device for actions (-1 = none) */
static SAVELOAD_STATE saveload_substate = SAVELOAD_BROWSE;
static const char* saveload_msg_line1 = NULL;
static int saveload_pending_action = 0;    /* 0 = save, 1 = load (for confirm dialog) */
static int saveload_confirm_choice = 0;    /* 0 = Yes, 1 = No */
static int saveload_pending_upgrade = 0;   /* 1 = upgrade old, 2 = downgrade future */
static int saveload_original_ui_mode = -1; /* UI mode when window opened (for consistent rendering) */

/* Serial SD card state */
static bool saveload_sd_available = false;
static SD_STATUS saveload_sd_status = SD_STATUS_NOT_PRESENT;
static uint32_t saveload_sd_version = 0;
static bool saveload_sd_is_startup_source = false;

static bool saveload_show_serial_error = false;

/* Cached width state, recomputed only on device or substate changes */
static bool saveload_width_dirty = true;
static SAVELOAD_STATE saveload_width_substate = SAVELOAD_BROWSE;
static const char* saveload_width_msg = NULL;
static int saveload_cached_max_chars = 22;

#define SAVELOAD_ACTION_SAVE  0
#define SAVELOAD_ACTION_LOAD  1
#define SAVELOAD_ACTION_CLOSE 2

/* Count number of selectable items (devices with VMU + SD + 3 action buttons) */
static int
saveload_get_selectable_count(void) {
    int count = 0;
    for (int i = 0; i < 8; i++) {
        if (saveload_slots[i].has_device) {
            count++;
        }
    }
    /* Add SD if available */
    if (saveload_sd_available) {
        count++;
    }
    return count + 3; /* +3 for Save/Load/Close buttons */
}

/* Get the device index for a cursor position, or -1 if cursor is on action buttons
 * Returns 0-7 for VMU slots, 8 for SD, -1 for action buttons */
static int
saveload_cursor_to_device_index(int cursor) {
    int device_count = 0;
    for (int i = 0; i < 8; i++) {
        if (saveload_slots[i].has_device) {
            if (device_count == cursor) {
                return i;
            }
            device_count++;
        }
    }
    /* Check if cursor is on SD */
    if (saveload_sd_available && device_count == cursor) {
        return 8; /* Special index for SD */
    }
    return -1; /* Cursor is on action buttons */
}

/* Get the action index for a cursor position (0=Save, 1=Load, 2=Close), or -1 if on device */
static int
saveload_cursor_to_action(int cursor) {
    int device_count = 0;
    for (int i = 0; i < 8; i++) {
        if (saveload_slots[i].has_device) {
            device_count++;
        }
    }
    /* Account for SD slot if available */
    if (saveload_sd_available) {
        device_count++;
    }
    if (cursor >= device_count) {
        return cursor - device_count;
    }
    return -1;
}

/* Check if cursor is on a device (vs action button) */
static int
saveload_cursor_on_device(void) {
    return saveload_cursor_to_device_index(saveload_cursor) >= 0;
}

/* Scan all VMU slots and update saveload_slots array */
static void
saveload_scan_devices(void) {
    savefile_refresh_device_info();
    int8_t startup_dev = savefile_get_startup_device_id();

    for (int8_t i = 0; i < 8; i++) {
        vmu_slot_info* slot = &saveload_slots[i];
        slot->device_id = i;
        slot->is_startup_source = (i == startup_dev);

        int8_t status = savefile_get_device_status(i);
        slot->crayon_status = status;

        if (status == CRAYON_SF_STATUS_NO_DEVICE) {
            slot->has_device = 0;
            slot->save_status = SAVE_NONE;
            strcpy(slot->type_name, "None");
        } else {
            slot->has_device = 1;

            /* Get device type name via maple */
            int port = i / 2;
            int unit = (i % 2 == 0) ? 1 : 2;
            maple_device_t* dev = maple_enum_dev(port, unit);
            if (dev) {
                const char* type = vm2_get_type_name(dev);
                strncpy(slot->type_name, type, sizeof(slot->type_name) - 1);
                slot->type_name[sizeof(slot->type_name) - 1] = '\0';
            } else {
                strcpy(slot->type_name, "VMU");
            }

            /* Map crayon status to friendly status */
            switch (status) {
                case CRAYON_SF_STATUS_NO_SF_ROOM: slot->save_status = SAVE_NONE; break;
                case CRAYON_SF_STATUS_NO_SF_FULL: slot->save_status = SAVE_NO_SPACE; break;
                case CRAYON_SF_STATUS_CURRENT_SF: slot->save_status = SAVE_CURRENT; break;
                case CRAYON_SF_STATUS_OLD_SF_ROOM:
                case CRAYON_SF_STATUS_OLD_SF_FULL: slot->save_status = SAVE_OLD; break;
                case CRAYON_SF_STATUS_FUTURE_SF: slot->save_status = SAVE_FUTURE; break;
                case CRAYON_SF_STATUS_INVALID_SF:
                default: slot->save_status = SAVE_INVALID; break;
            }
        }
    }

    /* SD card scanning */
    savefile_refresh_sd_status();
    saveload_sd_available = savefile_sd_available();
    saveload_sd_status = savefile_get_sd_status();
    saveload_sd_version = savefile_get_sd_version();
    saveload_sd_is_startup_source = savefile_was_loaded_from_sd();

    /* Adjust selected device if it's no longer valid */
    if (saveload_selected_device >= 0) {
        int idx = saveload_cursor_to_device_index(saveload_selected_device);
        if (idx < 0) {
            saveload_selected_device = -1;
        } else if (idx < 8 && !saveload_slots[idx].has_device) {
            saveload_selected_device = -1;
        } else if (idx == 8 && !saveload_sd_available) {
            saveload_selected_device = -1;
        }
    }
}

/* Map crayon status to SAVE_STATUS */
static SAVE_STATUS
saveload_map_status(int8_t status) {
    switch (status) {
        case CRAYON_SF_STATUS_NO_SF_ROOM: return SAVE_NONE;
        case CRAYON_SF_STATUS_NO_SF_FULL: return SAVE_NO_SPACE;
        case CRAYON_SF_STATUS_CURRENT_SF: return SAVE_CURRENT;
        case CRAYON_SF_STATUS_OLD_SF_ROOM:
        case CRAYON_SF_STATUS_OLD_SF_FULL: return SAVE_OLD;
        case CRAYON_SF_STATUS_FUTURE_SF: return SAVE_FUTURE;
        case CRAYON_SF_STATUS_INVALID_SF:
        default: return SAVE_INVALID;
    }
}

/* Check for VMU insertion/removal each frame.
 * maple_enum_dev() is free (cached), only scan save status on new device */
/* Map a raw device index (0-7 VMU, 8 SD) to filtered cursor position, or -1 */
static int
saveload_device_index_to_cursor(int dev_idx) {
    int count = 0;
    for (int i = 0; i < 8; i++) {
        if (saveload_slots[i].has_device) {
            if (i == dev_idx) {
                return count;
            }
            count++;
        }
    }
    if (saveload_sd_available && dev_idx == 8) {
        return count; /* SD is right after last VMU */
    }
    return -1;
}

/* Find next available storage device at or after dev_idx (0-7 VMU, 8 SD), wrapping.
 * Returns device index (0-7 or 8) or -1 if nothing available. */
static int
saveload_find_next_device(int dev_idx) {
    /* Search forward from dev_idx through VMUs */
    for (int i = dev_idx; i < 8; i++) {
        if (saveload_slots[i].has_device) {
            return i;
        }
    }
    /* Check SD card */
    if (saveload_sd_available && dev_idx <= 8) {
        return 8;
    }
    /* Wrap around from beginning */
    for (int i = 0; i < dev_idx && i < 8; i++) {
        if (saveload_slots[i].has_device) {
            return i;
        }
    }
    return -1;
}

static void
saveload_live_update_devices(void) {
    int changed = 0;

    /* Snapshot what the cursor and selection are pointing at before changes */
    int old_cursor_dev = saveload_cursor_to_device_index(saveload_cursor); /* 0-7, 8=SD, -1=button */
    int old_cursor_action = saveload_cursor_to_action(saveload_cursor);    /* 0=Save,1=Load,2=Close, -1=device */
    int old_selected_dev = -1;
    if (saveload_selected_device >= 0) {
        old_selected_dev = saveload_cursor_to_device_index(saveload_selected_device);
    }

    int inserted_id = -1;

    for (int8_t i = 0; i < 8; i++) {
        vmu_slot_info* slot = &saveload_slots[i];
        int port = i / 2;
        int unit = (i % 2 == 0) ? 1 : 2;
        maple_device_t* dev = maple_enum_dev(port, unit);
        int now_present = (dev != NULL) && (dev->info.functions & MAPLE_FUNC_MEMCARD);

        if (now_present && !slot->has_device) {
            /* New device, scan save status */
            savefile_refresh_single_device_info(i);
            int8_t status = savefile_get_device_status(i);
            slot->device_id = i;
            slot->is_startup_source = 0; /* hot-inserted device is never the startup source */
            slot->crayon_status = status;
            slot->has_device = 1;
            const char* type = get_vmu_type_name(dev);
            strncpy(slot->type_name, type, sizeof(slot->type_name) - 1);
            slot->type_name[sizeof(slot->type_name) - 1] = '\0';
            slot->save_status = saveload_map_status(status);
            inserted_id = i;
            changed = 1;
        } else if (!now_present && slot->has_device) {
            /* Device removed */
            slot->has_device = 0;
            slot->save_status = SAVE_NONE;
            strcpy(slot->type_name, "None");
            slot->crayon_status = CRAYON_SF_STATUS_NO_DEVICE;
            changed = 1;
        }
    }

    if (!changed) {
        return;
    }

    saveload_width_dirty = true;

    int device_count = saveload_get_selectable_count() - 3;
    int close_idx = device_count + 2;

    /* Handle selected device */
    if (old_selected_dev >= 0 && old_selected_dev < 8 && !saveload_slots[old_selected_dev].has_device) {
        /* Selected VMU was removed */
        saveload_selected_device = -1;
    } else if (saveload_selected_device >= 0) {
        /* Selected device still present, remap cursor index */
        int new_idx = saveload_device_index_to_cursor(old_selected_dev);
        if (new_idx >= 0) {
            saveload_selected_device = new_idx;
        } else {
            saveload_selected_device = -1;
        }
    }

    /* Handle cursor position */
    if (old_cursor_dev >= 0) {
        /* Cursor was on a device */
        if (old_cursor_dev < 8 && !saveload_slots[old_cursor_dev].has_device) {
            /* That VMU was removed, find next available device */
            int next = saveload_find_next_device(old_cursor_dev);
            if (next >= 0) {
                int idx = saveload_device_index_to_cursor(next);
                if (idx >= 0) {
                    saveload_cursor = idx;
                } else {
                    saveload_cursor = close_idx;
                }
            } else {
                /* No devices at all, jump to Close */
                saveload_cursor = close_idx;
            }
        } else {
            /* Device still present, recalculate filtered index */
            int new_idx = saveload_device_index_to_cursor(old_cursor_dev);
            if (new_idx >= 0) {
                saveload_cursor = new_idx;
            }
            /* If a new device was inserted, jump to it */
            if (inserted_id >= 0) {
                int idx = saveload_device_index_to_cursor(inserted_id);
                if (idx >= 0) {
                    saveload_cursor = idx;
                }
            }
        }
    } else if (old_cursor_action >= 0) {
        /* Cursor was on a button, keep it on the same button */
        saveload_cursor = device_count + old_cursor_action;

        /* If no device selected, don't leave cursor on Save/Load */
        if (saveload_selected_device < 0 && old_cursor_action < 2) {
            saveload_cursor = close_idx;
        }

        /* If a device was inserted and cursor was on a button, jump to it */
        if (inserted_id >= 0) {
            int idx = saveload_device_index_to_cursor(inserted_id);
            if (idx >= 0) {
                saveload_cursor = idx;
            }
        }
    }

    /* Final clamp */
    int total = saveload_get_selectable_count();
    if (saveload_cursor >= total) {
        saveload_cursor = total - 1;
        if (saveload_cursor < 0) {
            saveload_cursor = 0;
        }
    }

    if (inserted_id >= 0) {
        vmu_slot_info* ins = &saveload_slots[inserted_id];
        if (strcmp(ins->type_name, "VMU") != 0 && strcmp(ins->type_name, "None") != 0) {
            int port = inserted_id / 2;
            int unit = (inserted_id % 2 == 0) ? 1 : 2;
            maple_device_t* dev = maple_enum_dev(port, unit);
            if (dev) {
                /* CMD33 can cause a brief disconnect while switching profiles */
                vm2_set_id(dev, "openmenu", NULL);
                thd_sleep(200);
                while (!maple_enum_dev(port, unit)) {
                    thd_pass();
                }
                /* rescan slot with new profile */
                vm2_rescan();
                savefile_refresh_single_device_info(inserted_id);
                ins->crayon_status = savefile_get_device_status(inserted_id);
                ins->save_status = saveload_map_status(ins->crayon_status);
            }
        }
    }
}

/* Initialize saveload state. Called from menu_accept when colors are already set */
static void
saveload_init_state(void) {
    /* Save current UI mode for consistent rendering until window closes */
    saveload_original_ui_mode = sf_ui[0];

    /* Reset state */
    saveload_substate = SAVELOAD_BROWSE;
    saveload_cursor = 0;
    saveload_selected_device = -1;
    saveload_msg_line1 = NULL;
    saveload_confirm_choice = 0;
    saveload_pending_action = 0;
    saveload_pending_upgrade = 0;
    saveload_show_serial_error = false;

    /* rescan and re-identify as openmenu */
    vm2_rescan();
    for (int i = 0; i < vm2_device_count; i++) {
        maple_device_t* vmu = vm2_devices[i];
        int port = vmu->port;
        int unit = vmu->unit;
        vm2_set_id(vmu, "openmenu", NULL);
        thd_sleep(200);
        while (!maple_enum_dev(port, unit)) {
            thd_pass();
        }
    }

    saveload_scan_devices();
    saveload_width_dirty = true;

    /* Find first selectable device and set cursor there */
    for (int i = 0; i < 8; i++) {
        if (saveload_slots[i].has_device) {
            saveload_cursor = 0;
            break;
        }
    }
}

/* Apply current menu choices to sf_* settings variables */
static void
saveload_apply_choices_to_settings(void) {
    sf_ui[0] = choices[CHOICE_THEME];
    sf_region[0] = choices[CHOICE_REGION];
    sf_aspect[0] = choices[CHOICE_ASPECT];
    sf_sort[0] = choices[CHOICE_SORT];
    sf_filter[0] = choices[CHOICE_FILTER];
    sf_beep[0] = choices[CHOICE_BEEP];
    sf_bios_3d[0] = choices[CHOICE_BIOS_3D];
    sf_multidisc[0] = choices[CHOICE_MULTIDISC];
    sf_multidisc_grouping[0] = choices[CHOICE_MULTIDISC_GROUPING];
    sf_scroll_art[0] = choices[CHOICE_SCROLL_ART];
    sf_scroll_index[0] = choices[CHOICE_SCROLL_INDEX];
    sf_disc_details[0] = choices[CHOICE_DISC_DETAILS];
    sf_folders_art[0] = choices[CHOICE_FOLDERS_ART];
    sf_folders_item_details[0] = choices[CHOICE_FOLDERS_ITEM_DETAILS];
    sf_marquee_speed[0] = choices[CHOICE_MARQUEE_SPEED];
    sf_clock[0] = choices[CHOICE_CLOCK];
    sf_vmu_time_sync[0] = choices[CHOICE_VMU_TIME_SYNC];
    sf_serial_vmu[0] = choices[CHOICE_SERIAL_VMU];
    sf_serial_vmu_multislot[0] = choices[CHOICE_SERIAL_VMU_MULTISLOT];
    sf_vm2_send_all[0] = choices[CHOICE_VM2_SEND_ALL];
    sf_boot_mode[0] = choices[CHOICE_BOOT_MODE];

    /* Handle custom theme encoding */
    if (choices[CHOICE_THEME] != UI_SCROLL && choices[CHOICE_THEME] != UI_FOLDERS && sf_region[0] > REGION_END) {
        sf_custom_theme[0] = THEME_ON;
        int num_default_themes = 0;
        theme_get_default(sf_aspect[0], &num_default_themes);
        sf_custom_theme_num[0] = sf_region[0] - num_default_themes;
    } else if ((choices[CHOICE_THEME] == UI_SCROLL || choices[CHOICE_THEME] == UI_FOLDERS) && sf_region[0] > 0) {
        sf_custom_theme[0] = THEME_ON;
        sf_custom_theme_num[0] = sf_region[0] - 1;
    } else {
        sf_custom_theme[0] = THEME_OFF;
    }
}

static void
saveload_do_save(void) {
    if (saveload_selected_device < 0) {
        return;
    }
    int dev_idx = saveload_cursor_to_device_index(saveload_selected_device);
    if (dev_idx < 0) {
        return;
    }

    saveload_substate = SAVELOAD_BUSY;
    saveload_msg_line1 = "Saving...";

    /* Apply current menu choices to settings */
    saveload_apply_choices_to_settings();

    int8_t result;

    if (dev_idx == 8) {
        /* Save to SD */
        result = savefile_save_to_sd();
        if (result != 0) {
            /* Determine specific error */
            if (!savefile_sd_available()) {
                saveload_msg_line1 = "Error: SD card not detected.";
            } else {
                saveload_msg_line1 = "Error: Failed to write to SD.";
            }
        }
    } else {
        /* Save to VMU */
        vmu_slot_info* slot = &saveload_slots[dev_idx];
        result = savefile_save_to_device(slot->device_id);
        if (result != 0) {
            /* Check if it was a space issue */
            uint32_t needed = savefile_get_save_size_blocks();
            uint32_t available = savefile_get_device_free_blocks(slot->device_id);
            if (needed > available) {
                saveload_msg_line1 = "Error: Not enough space on VMU.";
            } else {
                saveload_msg_line1 = "Error: Failed to save settings.";
            }
        }
    }

    saveload_substate = SAVELOAD_RESULT;
    if (result == 0) {
        saveload_msg_line1 = "Settings saved successfully.";
    } else {
        vm2_rescan();
        for (int i = 0; i < vm2_device_count; i++) {
            maple_device_t* vmu = vm2_devices[i];
            int port = vmu->port;
            int unit = vmu->unit;
            vm2_set_id(vmu, "openmenu", NULL);
            thd_sleep(200);
            while (!maple_enum_dev(port, unit)) {
                thd_pass();
            }
        }
        saveload_scan_devices();
    }
}

static void
saveload_do_load(void) {
    if (saveload_selected_device < 0) {
        return;
    }
    int dev_idx = saveload_cursor_to_device_index(saveload_selected_device);
    if (dev_idx < 0) {
        return;
    }

    saveload_substate = SAVELOAD_BUSY;
    saveload_msg_line1 = "Loading...";

    int8_t result;

    if (dev_idx == 8) {
        /* Load from SD */
        int was_old = (saveload_sd_status == SD_STATUS_OLD);
        int was_future = (saveload_sd_status == SD_STATUS_FUTURE);
        result = savefile_load_from_sd();

        saveload_substate = SAVELOAD_RESULT;
        if (result == 0) {
            if (was_old) {
                /* Auto-upgrade: save back to SD */
                savefile_save_to_sd();
                saveload_msg_line1 = "Settings loaded and upgraded.";
            } else if (was_future) {
                /* Auto-downgrade: save back to SD as current version */
                savefile_save_to_sd();
                saveload_msg_line1 = "Settings loaded and downgraded.";
            } else {
                saveload_msg_line1 = "Settings loaded successfully.";
            }
        } else {
            SD_STATUS status = savefile_get_sd_status();
            switch (status) {
                case SD_STATUS_NOT_PRESENT: saveload_msg_line1 = "Error: SD card not detected."; break;
                case SD_STATUS_INVALID: saveload_msg_line1 = "Error: SD config file invalid."; break;
                case SD_STATUS_FUTURE: saveload_msg_line1 = "Error: Incompatible future save."; break;
                default: saveload_msg_line1 = "Error: Failed to read from SD."; break;
            }
        }
    } else {
        /* Load from VMU */
        vmu_slot_info* slot = &saveload_slots[dev_idx];
        int was_old = (slot->save_status == SAVE_OLD);
        int was_future = (slot->save_status == SAVE_FUTURE);

        result = savefile_load_from_device(slot->device_id);

        saveload_substate = SAVELOAD_RESULT;
        if (result == 0) {
            /* Success */
            if (was_old) {
                /* Auto-upgrade: save back to VMU */
                savefile_save_to_device(slot->device_id);
                saveload_msg_line1 = "Settings loaded and upgraded.";
            } else if (was_future) {
                /* Auto-downgrade: save back to VMU as current version */
                savefile_save_to_device(slot->device_id);
                saveload_msg_line1 = "Settings loaded and downgraded.";
            } else {
                saveload_msg_line1 = "Settings loaded successfully.";
            }
        } else {
            if (slot->save_status == SAVE_INVALID) {
                saveload_msg_line1 = "Error: Save file is corrupt.";
            } else if (slot->save_status == SAVE_FUTURE) {
                saveload_msg_line1 = "Error: Incompatible future save.";
            } else {
                saveload_msg_line1 = "Error: Failed to load settings.";
            }
        }
    }

    if (result != 0) {
        vm2_rescan();
        for (int i = 0; i < vm2_device_count; i++) {
            maple_device_t* vmu = vm2_devices[i];
            int port = vmu->port;
            int unit = vmu->unit;
            vm2_set_id(vmu, "openmenu", NULL);
            thd_sleep(200);
            while (!maple_enum_dev(port, unit)) {
                thd_pass();
            }
        }
        saveload_scan_devices();
    }
}

/* Close the Save/Load window and return to main UI */
static void
saveload_close_all(int do_reload) {
    if (do_reload) {
        /* Apply loaded settings to sort/filter */
        if (!sf_filter[0]) {
            switch ((CFG_SORT)sf_sort[0]) {
                case SORT_NAME: list_set_sort_name(); break;
                case SORT_DATE: list_set_sort_region(); break;
                case SORT_PRODUCT: list_set_sort_genre(); break;
                case SORT_SD_CARD: list_set_sort_default(); break;
                default:
                case SORT_DEFAULT: list_set_sort_alphabetical(); break;
            }
        } else {
            list_set_genre_sort((FLAGS_GENRE)sf_filter[0] - 1, sf_sort[0]);
        }

        extern void reload_ui(void);
        reload_ui();
    }
    *state_ptr = DRAW_UI;
    *input_timeout_ptr = 3;
}

void
saveload_setup(enum draw_state* state, theme_color* _colors, int* timeout_ptr, uint32_t title_color) {
    common_setup(state, _colors, timeout_ptr);
    menu_title_color = title_color;

    /* Save current UI mode for consistent rendering until window closes */
    saveload_original_ui_mode = sf_ui[0];

    /* Reset state */
    saveload_substate = SAVELOAD_BROWSE;
    saveload_cursor = 0;
    saveload_selected_device = -1;
    saveload_msg_line1 = NULL;
    saveload_confirm_choice = 0;
    saveload_pending_action = 0;
    saveload_pending_upgrade = 0;
    saveload_show_serial_error = false;

    saveload_scan_devices();
    saveload_width_dirty = true;

    /* Find first selectable device and set cursor there */
    for (int i = 0; i < 8; i++) {
        if (saveload_slots[i].has_device) {
            saveload_cursor = 0;
            break;
        }
    }
}

void
handle_input_saveload(enum control input) {
    /* Handle based on sub-state */
    switch (saveload_substate) {
        case SAVELOAD_BUSY:
            /* No input during operation */
            return;

        case SAVELOAD_RESULT:
            if (input == A || input == B || input == START) {
                if (saveload_msg_line1 != NULL
                    && (strstr(saveload_msg_line1, "loaded") != NULL || strstr(saveload_msg_line1, "saved") != NULL)) {
                    saveload_close_all(1);
                } else {
                    saveload_substate = SAVELOAD_BROWSE;
                    saveload_msg_line1 = NULL;
                }
                *input_timeout_ptr = INPUT_TIMEOUT;
            }
            return;

        case SAVELOAD_CONFIRM:
            /* Confirm dialog */
            switch (input) {
                case UP:
                case DOWN:
                    if (*input_timeout_ptr > 0) {
                        break;
                    }
                    saveload_confirm_choice = !saveload_confirm_choice;
                    *input_timeout_ptr = INPUT_TIMEOUT;
                    break;
                case A:
                    if (saveload_confirm_choice == 0) {
                        /* Yes, proceed with action */
                        if (saveload_pending_action == SAVELOAD_ACTION_SAVE) {
                            saveload_do_save();
                        } else {
                            saveload_do_load();
                        }
                    } else {
                        /* No, cancel and return to browse */
                        saveload_substate = SAVELOAD_BROWSE;
                    }
                    *input_timeout_ptr = INPUT_TIMEOUT;
                    break;
                case B:
                    /* Cancel */
                    saveload_substate = SAVELOAD_BROWSE;
                    *input_timeout_ptr = INPUT_TIMEOUT;
                    break;
                default: break;
            }
            return;

        case SAVELOAD_BROWSE:
            /* Normal browsing */
            break;
    }

    /* Serial VMU error popup. Swallow all input except A/B to dismiss */
    if (saveload_show_serial_error) {
        if (input == A || input == B) {
            saveload_show_serial_error = false;
            saveload_scan_devices();
            saveload_substate = SAVELOAD_BROWSE;
            saveload_cursor = 0;
        }
        return;
    }

    /* Browse state input handling */
    int total_selectable = saveload_get_selectable_count();
    int device_count = total_selectable - 3;
    int close_idx = device_count + 2; /* Close button index */

    switch (input) {
        case UP:
            if (*input_timeout_ptr > 0) {
                break;
            }
            if (saveload_cursor > 0) {
                int new_cursor = saveload_cursor - 1;
                /* Skip Save/Load buttons if no device selected */
                if (saveload_selected_device < 0 && new_cursor >= device_count && new_cursor < close_idx) {
                    new_cursor = device_count - 1; /* Jump to last device */
                    if (new_cursor < 0) {
                        new_cursor = 0;
                    }
                }
                saveload_cursor = new_cursor;
            } else {
                /* Wrap to bottom (Close button) */
                saveload_cursor = close_idx;
            }
            *input_timeout_ptr = INPUT_TIMEOUT;
            break;

        case DOWN:
            if (*input_timeout_ptr > 0) {
                break;
            }
            if (saveload_cursor < total_selectable - 1) {
                int new_cursor = saveload_cursor + 1;
                /* Skip Save/Load buttons if no device selected */
                if (saveload_selected_device < 0 && new_cursor >= device_count && new_cursor < close_idx) {
                    new_cursor = close_idx; /* Jump to Close */
                }
                saveload_cursor = new_cursor;
            } else {
                /* Wrap to top (first device) */
                saveload_cursor = 0;
            }
            *input_timeout_ptr = INPUT_TIMEOUT;
            break;

        case A: {
            int action = saveload_cursor_to_action(saveload_cursor);
            if (action == SAVELOAD_ACTION_CLOSE) {
                /* Close */
                *state_ptr = DRAW_MENU;
                *input_timeout_ptr = 3;
            } else if (action == SAVELOAD_ACTION_SAVE) {
                /* Save, check if we need confirmation */
                if (saveload_selected_device < 0) {
                    /* No device selected, do nothing */
                    break;
                }
                int dev_idx = saveload_cursor_to_device_index(saveload_selected_device);
                if (dev_idx == 8) {
                    /* SD card save */
                    if (saveload_sd_status == SD_STATUS_READY || saveload_sd_status == SD_STATUS_OLD
                        || saveload_sd_status == SD_STATUS_INVALID || saveload_sd_status == SD_STATUS_FUTURE) {
                        /* Need confirmation to overwrite */
                        saveload_substate = SAVELOAD_CONFIRM;
                        saveload_pending_action = SAVELOAD_ACTION_SAVE;
                        saveload_confirm_choice = 0;
                        saveload_pending_upgrade = 0;
                    } else {
                        /* No existing save, proceed directly */
                        saveload_do_save();
                    }
                } else if (dev_idx >= 0) {
                    if (choices[CHOICE_SERIAL_VMU] != SERIAL_VMU_OFF) {
                        saveload_show_serial_error = true;
                        break;
                    }
                    vmu_slot_info* slot = &saveload_slots[dev_idx];
                    if (slot->save_status == SAVE_CURRENT || slot->save_status == SAVE_OLD
                        || slot->save_status == SAVE_INVALID || slot->save_status == SAVE_FUTURE) {
                        /* Need confirmation to overwrite */
                        saveload_substate = SAVELOAD_CONFIRM;
                        saveload_pending_action = SAVELOAD_ACTION_SAVE;
                        saveload_confirm_choice = 0;
                        saveload_pending_upgrade = 0;
                    } else {
                        /* No existing save, proceed directly */
                        saveload_do_save();
                    }
                }
            } else if (action == SAVELOAD_ACTION_LOAD) {
                /* Load, check if we can load and need confirmation */
                if (saveload_selected_device < 0) {
                    /* No device selected, do nothing */
                    break;
                }
                int dev_idx = saveload_cursor_to_device_index(saveload_selected_device);
                if (dev_idx == 8) {
                    /* SD card load */
                    if (saveload_sd_status == SD_STATUS_NO_FILE) {
                        saveload_substate = SAVELOAD_RESULT;
                        saveload_msg_line1 = "Error: No save file on SD.";
                    } else if (saveload_sd_status == SD_STATUS_FUTURE) {
                        /* Future save, need confirmation for downgrade */
                        saveload_substate = SAVELOAD_CONFIRM;
                        saveload_pending_action = SAVELOAD_ACTION_LOAD;
                        saveload_confirm_choice = 0;
                        saveload_pending_upgrade = 2; /* 2 = downgrade */
                    } else if (saveload_sd_status == SD_STATUS_INVALID) {
                        saveload_substate = SAVELOAD_RESULT;
                        saveload_msg_line1 = "Error: SD config file invalid.";
                    } else if (saveload_sd_status == SD_STATUS_OLD) {
                        /* Old save, need confirmation for upgrade */
                        saveload_substate = SAVELOAD_CONFIRM;
                        saveload_pending_action = SAVELOAD_ACTION_LOAD;
                        saveload_confirm_choice = 0;
                        saveload_pending_upgrade = 1;
                    } else {
                        /* Current save, load directly */
                        saveload_do_load();
                    }
                } else if (dev_idx >= 0) {
                    vmu_slot_info* slot = &saveload_slots[dev_idx];
                    if (slot->save_status == SAVE_NONE || slot->save_status == SAVE_NO_SPACE) {
                        /* No save to load */
                        saveload_substate = SAVELOAD_RESULT;
                        saveload_msg_line1 = "Error: No save file on this VMU.";
                    } else if (slot->save_status == SAVE_FUTURE) {
                        /* Future save, need confirmation for downgrade */
                        saveload_substate = SAVELOAD_CONFIRM;
                        saveload_pending_action = SAVELOAD_ACTION_LOAD;
                        saveload_confirm_choice = 0;
                        saveload_pending_upgrade = 2; /* 2 = downgrade */
                    } else if (slot->save_status == SAVE_INVALID) {
                        saveload_substate = SAVELOAD_RESULT;
                        saveload_msg_line1 = "Error: Save file is corrupt.";
                    } else if (slot->save_status == SAVE_OLD) {
                        /* Old save, need confirmation for upgrade */
                        saveload_substate = SAVELOAD_CONFIRM;
                        saveload_pending_action = SAVELOAD_ACTION_LOAD;
                        saveload_confirm_choice = 0;
                        saveload_pending_upgrade = 1;
                    } else {
                        /* Current save, load directly */
                        saveload_do_load();
                    }
                }
            } else if (saveload_cursor_on_device()) {
                /* On a device, select it and move to Save button */
                saveload_selected_device = saveload_cursor;
                saveload_cursor = device_count; /* Move to Save button */
            }
            *input_timeout_ptr = INPUT_TIMEOUT;
            break;
        }

        case B:
        case START:
            /* Return to Settings menu */
            *state_ptr = DRAW_MENU;
            *input_timeout_ptr = 3;
            break;

        default: break;
    }
}

void
draw_saveload_op(void) {
    /* Nothing to draw in opaque pass */
}

/* Build VMU status string for display */
static void
saveload_build_vmu_status_str(char* out, size_t out_size, const vmu_slot_info* slot) {
    if (slot->is_startup_source && slot->save_status == SAVE_CURRENT) {
        strcpy(out, "(loaded)");
    } else {
        switch (slot->save_status) {
            case SAVE_NONE: strcpy(out, "(no save)"); break;
            case SAVE_CURRENT: strcpy(out, "(saved)"); break;
            case SAVE_OLD: {
                uint32_t ver = savefile_get_device_version(slot->device_id);
                snprintf(out, out_size, "(old v%lu)", (unsigned long)ver);
                break;
            }
            case SAVE_INVALID: strcpy(out, "(invalid)"); break;
            case SAVE_NO_SPACE: strcpy(out, "(full)"); break;
            case SAVE_FUTURE: strcpy(out, "(future)"); break;
            default: out[0] = '\0'; break;
        }
    }
}

/* Build SD card status string for display */
static void
saveload_build_sd_status_str(char* out, size_t out_size) {
    if (saveload_sd_is_startup_source
        && (saveload_sd_status == SD_STATUS_READY || saveload_sd_status == SD_STATUS_OLD)) {
        strcpy(out, "(loaded)");
    } else {
        switch (saveload_sd_status) {
            case SD_STATUS_NO_FILE: strcpy(out, "(no save)"); break;
            case SD_STATUS_READY: strcpy(out, "(saved)"); break;
            case SD_STATUS_OLD: snprintf(out, out_size, "(old v%lu)", (unsigned long)saveload_sd_version); break;
            case SD_STATUS_INVALID: strcpy(out, "(invalid)"); break;
            case SD_STATUS_NO_SPACE: strcpy(out, "(full)"); break;
            case SD_STATUS_FUTURE: strcpy(out, "(future)"); break;
            default: out[0] = '\0'; break;
        }
    }
}

/* Get confirm dialog version for width measurement */
static uint32_t
saveload_get_confirm_version(void) {
    uint32_t ver = 0;
    if (saveload_selected_device >= 0) {
        int dev_idx = saveload_cursor_to_device_index(saveload_selected_device);
        if (dev_idx == 8) {
            ver = saveload_sd_version;
        } else if (dev_idx >= 0) {
            ver = savefile_get_device_version(saveload_slots[dev_idx].device_id);
        }
    }
    return ver;
}

/* Recompute cached max character width for save/load window.
 * Called only when devices change or substate transitions. */
static void
saveload_recalc_width(void) {
    int max_chars = 22; /* "Save and Load Settings" */
    char size_buf[56];

    /* Measure device lines */
    for (int i = 0; i < 8; i++) {
        vmu_slot_info* slot = &saveload_slots[i];
        int len;
        if (slot->has_device) {
            char status_str[20];
            saveload_build_vmu_status_str(status_str, sizeof(status_str), slot);
            int port = i / 2;
            if (i % 2 == 0) {
                len = snprintf(size_buf, sizeof(size_buf), "Port %c - Socket 1: %s %s <", 'A' + port, slot->type_name,
                               status_str);
            } else {
                len = snprintf(size_buf, sizeof(size_buf), "         Socket 2: %s %s <", slot->type_name, status_str);
            }
        } else {
            len = 24; /* "Port X - Socket 1: None" */
        }
        if (len > max_chars) {
            max_chars = len;
        }
    }

    /* Measure SD line */
    if (saveload_sd_available) {
        char sd_status[20];
        saveload_build_sd_status_str(sd_status, sizeof(sd_status));
        int len = snprintf(size_buf, sizeof(size_buf), "Serial - SD card %s <", sd_status);
        if (len > max_chars) {
            max_chars = len;
        }
    }

    /* Measure action area by substate */
    switch (saveload_substate) {
        case SAVELOAD_CONFIRM:
            if (saveload_pending_upgrade) {
                uint32_t ver = saveload_get_confirm_version();
                int len;
                if (saveload_pending_upgrade == 2) {
                    len = snprintf(size_buf, sizeof(size_buf), "Downgrade future save (v%lu)?", (unsigned long)ver);
                } else {
                    len =
                        snprintf(size_buf, sizeof(size_buf), "Load will upgrade old save (v%lu).", (unsigned long)ver);
                }
                if (len > max_chars) {
                    max_chars = len;
                }
            } else {
                if (24 > max_chars) {
                    max_chars = 24; /* "Overwrite existing save?" */
                }
            }
            break;
        case SAVELOAD_RESULT:
            if (saveload_msg_line1) {
                int len = (int)strlen(saveload_msg_line1);
                if (len > max_chars) {
                    max_chars = len;
                }
            }
            if (7 > max_chars) {
                max_chars = 7; /* "Go back" */
            }
            break;
        default: /* BROWSE and BUSY */
            if (18 > max_chars) {
                max_chars = 18; /* "Load from selected" */
            }
            break;
    }

    saveload_cached_max_chars = max_chars;
    saveload_width_substate = saveload_substate;
    saveload_width_msg = saveload_msg_line1;
    saveload_width_dirty = false;
}

void
draw_saveload_tr(void) {
    z_set_cond(205.0f);

    /* Poll for device changes while browsing */
    if (saveload_substate == SAVELOAD_BROWSE) {
        saveload_live_update_devices();
    }

    /* Recalculate width only when something changed */
    if (saveload_width_dirty || saveload_substate != saveload_width_substate
        || saveload_msg_line1 != saveload_width_msg) {
        saveload_recalc_width();
    }

    /* use saved UI mode from window open, not live sf_ui[] */
    int ui_mode = (saveload_original_ui_mode >= 0) ? saveload_original_ui_mode : sf_ui[0];

    if (ui_mode == UI_SCROLL || ui_mode == UI_FOLDERS) {
        /* Scroll/Folders mode. Bitmap font */
        const int line_height = 24;
        const int padding = 16;

        /* Calculate height based on content:
         * 4 ports × 2 lines each = 8 lines
         * 1 Serial line
         * 4 action area lines
         * = 13 content lines + title */
        int content_lines = 8 + 1 + 4;

        int width = (saveload_cached_max_chars + 2) * 8;
        if (width > 600) {
            width = 600;
        }
        if (width < 280) {
            width = 280;
        }

        /* Match Credits formula: (content + 1) * line_height + extra padding */
        const int height = (content_lines + 1) * line_height + 4;
        const int x = (640 / 2) - (width / 2);
        const int y = (480 / 2) - (height / 2);
        const int x_item = x + (padding / 2);

        draw_popup_menu_ex(x, y, width, height, ui_mode);

        int cur_y = y + 2; /* Match Settings title position */
        font_bmp_begin_draw();
        font_bmp_set_color(menu_title_color);

        /* Title, centered */
        const char* title = "Save and Load Settings";
        font_bmp_draw_main(x + width / 2 - ((int)strlen(title) * 8 / 2), cur_y, title);

        cur_y += 2;

        /* Track cursor position for highlighting */
        int cursor_idx = 0;
        int device_count = 0;

        /* Count devices first */
        for (int i = 0; i < 8; i++) {
            if (saveload_slots[i].has_device) {
                device_count++;
            }
        }

        /* Draw ports and sockets. Compact layout: Port X - Socket 1 on same line */
        for (int p = 0; p < 4; p++) {
            /* Socket 1 row: "Port X - Socket 1: TYPE (status)" */
            cur_y += line_height;
            int slot_idx = p * 2;
            vmu_slot_info* slot = &saveload_slots[slot_idx];

            if (slot->has_device) {
                int is_cursor = (saveload_substate == SAVELOAD_BROWSE && cursor_idx == saveload_cursor);
                int is_selected = (!saveload_cursor_on_device() && cursor_idx == saveload_selected_device);

                if (is_cursor) {
                    font_bmp_set_color(highlight_color);
                } else {
                    font_bmp_set_color(text_color);
                }

                char status_str[20];
                saveload_build_vmu_status_str(status_str, sizeof(status_str), slot);

                char line[56];
                snprintf(line, sizeof(line), "Port %c - Socket 1: %s %s%s", 'A' + p, slot->type_name, status_str,
                         is_selected ? " <" : "");
                font_bmp_draw_main(x_item, cur_y, line);
                cursor_idx++;
            } else {
                font_bmp_set_color(text_color);
                char line[32];
                snprintf(line, sizeof(line), "Port %c - Socket 1: None", 'A' + p);
                font_bmp_draw_main(x_item, cur_y, line);
            }

            /* Socket 2 row: "         Socket 2: TYPE (status)", aligned under Socket 1 */
            cur_y += line_height;
            slot_idx = p * 2 + 1;
            slot = &saveload_slots[slot_idx];

            if (slot->has_device) {
                int is_cursor = (saveload_substate == SAVELOAD_BROWSE && cursor_idx == saveload_cursor);
                int is_selected = (!saveload_cursor_on_device() && cursor_idx == saveload_selected_device);

                if (is_cursor) {
                    font_bmp_set_color(highlight_color);
                } else {
                    font_bmp_set_color(text_color);
                }

                char status_str[20];
                saveload_build_vmu_status_str(status_str, sizeof(status_str), slot);

                char line[56];
                /* 9 spaces to align "Socket 2" under "Socket 1" */
                snprintf(line, sizeof(line), "         Socket 2: %s %s%s", slot->type_name, status_str,
                         is_selected ? " <" : "");
                font_bmp_draw_main(x_item, cur_y, line);
                cursor_idx++;
            } else {
                font_bmp_set_color(text_color);
                font_bmp_draw_main(x_item, cur_y, "         Socket 2: None");
            }
        }

        /* Serial row. SD card, separate from port entries */
        cur_y += line_height;
        if (saveload_sd_available) {
            int is_cursor = (saveload_substate == SAVELOAD_BROWSE && cursor_idx == saveload_cursor);
            int is_selected = (!saveload_cursor_on_device() && saveload_selected_device == cursor_idx);

            if (is_cursor) {
                font_bmp_set_color(highlight_color);
            } else {
                font_bmp_set_color(text_color);
            }

            char status_str[20];
            saveload_build_sd_status_str(status_str, sizeof(status_str));

            char line[48];
            snprintf(line, sizeof(line), "Serial - SD card %s%s", status_str, is_selected ? " <" : "");
            font_bmp_draw_main(x_item, cur_y, line);
            cursor_idx++;
            device_count++;
        } else {
            font_bmp_set_color(text_color);
            font_bmp_draw_main(x_item, cur_y, "Serial - SD card");
        }

        /* Spacing before action area */
        cur_y += line_height;

        /* Action area. All states use exactly 4 lines for consistent window height */
        if (saveload_substate == SAVELOAD_BUSY || saveload_substate == SAVELOAD_RESULT) {
            font_bmp_set_color(text_color);

            /* Line 1: Main message */
            cur_y += line_height;
            if (saveload_msg_line1) {
                font_bmp_draw_main(x_item, cur_y, saveload_msg_line1);
            }

            /* msg1 / empty / button / empty */
            cur_y += line_height; /* Empty separator */
            cur_y += line_height;
            if (saveload_substate == SAVELOAD_RESULT) {
                const char* btn = (saveload_msg_line1
                                   && (strstr(saveload_msg_line1, "loaded") || strstr(saveload_msg_line1, "saved")))
                                      ? "Close"
                                      : "Go back";
                font_bmp_set_color(highlight_color);
                font_bmp_draw_main(x_item, cur_y, btn);
            }
            cur_y += line_height; /* Empty for consistent height */
        } else if (saveload_substate == SAVELOAD_CONFIRM) {
            /* Layout: prompt / Yes / No / empty */
            font_bmp_set_color(text_color);

            /* Line 1: Prompt message */
            cur_y += line_height;
            if (saveload_pending_upgrade) {
                uint32_t ver = saveload_get_confirm_version();
                char upgrade_msg[48];
                if (saveload_pending_upgrade == 2) {
                    snprintf(upgrade_msg, sizeof(upgrade_msg), "Downgrade future save (v%lu)?", (unsigned long)ver);
                } else {
                    snprintf(upgrade_msg, sizeof(upgrade_msg), "Load will upgrade old save (v%lu).",
                             (unsigned long)ver);
                }
                font_bmp_draw_main(x_item, cur_y, upgrade_msg);
            } else {
                font_bmp_draw_main(x_item, cur_y, "Overwrite existing save?");
            }

            /* Line 2: Yes */
            cur_y += line_height;
            font_bmp_set_color(saveload_confirm_choice == 0 ? highlight_color : text_color);
            font_bmp_draw_main(x_item, cur_y, "Yes");

            /* Line 3: No */
            cur_y += line_height;
            font_bmp_set_color(saveload_confirm_choice == 1 ? highlight_color : text_color);
            font_bmp_draw_main(x_item, cur_y, "No");

            /* Line 4: Empty for consistent height */
            cur_y += line_height;
        } else {
            /* BROWSE state. Layout: Save / Load / Close / empty */
            int action_start_idx = device_count; /* device_count already includes SD if available */

            /* Line 1: Save to selected */
            cur_y += line_height;
            int is_save_cursor = (saveload_cursor == action_start_idx);
            int save_disabled = (saveload_selected_device < 0);
            if (is_save_cursor && !save_disabled) {
                font_bmp_set_color(highlight_color);
            } else {
                font_bmp_set_color(text_color);
            }
            font_bmp_draw_main(x_item, cur_y, "Save to selected");

            /* Line 2: Load from selected */
            cur_y += line_height;
            int is_load_cursor = (saveload_cursor == action_start_idx + 1);
            int load_disabled = (saveload_selected_device < 0);
            if (is_load_cursor && !load_disabled) {
                font_bmp_set_color(highlight_color);
            } else {
                font_bmp_set_color(text_color);
            }
            font_bmp_draw_main(x_item, cur_y, "Load from selected");

            /* Line 3: Close */
            cur_y += line_height;
            int is_close_cursor = (saveload_cursor == action_start_idx + 2);
            if (is_close_cursor) {
                font_bmp_set_color(highlight_color);
            } else {
                font_bmp_set_color(text_color);
            }
            font_bmp_draw_main(x_item, cur_y, "Close");

            /* Line 4: Empty for consistent height */
            cur_y += line_height;
        }

        /* Serial VMU error popup, drawn on top of save/load window */
        if (saveload_show_serial_error) {
            const int err_line_height = 24;
            const int err_width = (38 + 2) * 8;
            const int err_height = (7 + 1) * err_line_height + 4; /* title + 3 msg + empty + 2 msg + Close */
            const int err_x = (640 / 2) - (err_width / 2);
            const int err_y = (480 / 2) - (err_height / 2);
            const int err_x_item = err_x + 8;

            draw_popup_menu_ex(err_x, err_y, err_width, err_height, ui_mode);

            int ey = err_y + 2;
            font_bmp_set_color(menu_title_color);
            font_bmp_draw_main(err_x + err_width / 2 - (5 * 8 / 2), ey, "Error");
            ey += 2;

            font_bmp_set_color(text_color);
            ey += err_line_height;
            font_bmp_draw_main(err_x_item, ey, "When Serial VMU is enabled, openMenu");
            ey += err_line_height;
            font_bmp_draw_main(err_x_item, ey, "settings cannot be saved to a VMU.");

            ey += err_line_height; /* Empty separator */

            ey += err_line_height;
            font_bmp_draw_main(err_x_item, ey, "Save settings to serial SD card");
            ey += err_line_height;
            font_bmp_draw_main(err_x_item, ey, "instead.");

            ey += err_line_height; /* Empty separator */

            ey += err_line_height;
            font_bmp_set_color(highlight_color);
            font_bmp_draw_main(err_x_item, ey, "Close");
        }
    } else {
        /* LineDesc/Grid mode. Proportional font */
        const int line_height = 26;
        const int padding = 16;

        /* Calculate height based on content:
         * 4 ports × 2 lines each = 8 lines
         * 1 Serial line
         * 4 action area lines
         * = 13 content lines + title */
        int content_lines = 8 + 1 + 4;

        int width = saveload_cached_max_chars * 10 + padding;
        if (width > 520) {
            width = 520;
        }
        if (width < 280) {
            width = 280;
        }

        const int height = (content_lines + 2) * line_height;
        const int x = (640 / 2) - (width / 2);
        const int y = (480 / 2) - (height / 2);
        const int x_item = x + (padding / 2);

        draw_popup_menu_ex(x, y, width, height, ui_mode);

        int cur_y = y + 2; /* Match Settings title position */
        font_bmf_begin_draw();
        font_bmf_set_height(24.0f);

        /* Title */
        font_bmf_draw(x_item, cur_y, text_color, "Save and Load Settings");

        cur_y += line_height / 4;

        /* Track cursor position for highlighting */
        int cursor_idx = 0;
        int device_count = 0;

        /* Count devices first */
        for (int i = 0; i < 8; i++) {
            if (saveload_slots[i].has_device) {
                device_count++;
            }
        }

        /* Draw ports and sockets. Compact layout: Port X - Socket 1 on same line */
        for (int p = 0; p < 4; p++) {
            /* Socket 1 row: "Port X - Socket 1: TYPE (status)" */
            cur_y += line_height;
            int slot_idx = p * 2;
            vmu_slot_info* slot = &saveload_slots[slot_idx];

            if (slot->has_device) {
                int is_cursor = (saveload_substate == SAVELOAD_BROWSE && cursor_idx == saveload_cursor);
                int is_selected = (!saveload_cursor_on_device() && cursor_idx == saveload_selected_device);

                uint32_t slot_color = is_cursor ? highlight_color : text_color;

                char status_str[20];
                saveload_build_vmu_status_str(status_str, sizeof(status_str), slot);

                char line[56];
                snprintf(line, sizeof(line), "Port %c - Socket 1: %s %s%s", 'A' + p, slot->type_name, status_str,
                         is_selected ? " <" : "");
                font_bmf_draw(x_item, cur_y, slot_color, line);
                cursor_idx++;
            } else {
                char line[32];
                snprintf(line, sizeof(line), "Port %c - Socket 1: None", 'A' + p);
                font_bmf_draw(x_item, cur_y, text_color, line);
            }

            /* Socket 2 row: "         Socket 2: TYPE (status)", aligned under Socket 1 */
            cur_y += line_height;
            slot_idx = p * 2 + 1;
            slot = &saveload_slots[slot_idx];

            if (slot->has_device) {
                int is_cursor = (saveload_substate == SAVELOAD_BROWSE && cursor_idx == saveload_cursor);
                int is_selected = (!saveload_cursor_on_device() && cursor_idx == saveload_selected_device);

                uint32_t slot_color = is_cursor ? highlight_color : text_color;

                char status_str[20];
                saveload_build_vmu_status_str(status_str, sizeof(status_str), slot);

                char line[48];
                /* Fixed pixel offset to align "Socket 2" under "Socket 1" */
                snprintf(line, sizeof(line), "Socket 2: %s %s%s", slot->type_name, status_str, is_selected ? " <" : "");
                font_bmf_draw(x_item + 72, cur_y, slot_color, line);
                cursor_idx++;
            } else {
                /* Fixed pixel offset to align with Socket 1 */
                font_bmf_draw(x_item + 72, cur_y, text_color, "Socket 2: None");
            }
        }

        /* Serial row. SD card, separate from port entries */
        cur_y += line_height;
        if (saveload_sd_available) {
            int is_cursor = (saveload_substate == SAVELOAD_BROWSE && cursor_idx == saveload_cursor);
            int is_selected = (!saveload_cursor_on_device() && saveload_selected_device == cursor_idx);

            uint32_t sd_color = is_cursor ? highlight_color : text_color;

            char status_str[20];
            saveload_build_sd_status_str(status_str, sizeof(status_str));

            char line[48];
            snprintf(line, sizeof(line), "Serial - SD card %s%s", status_str, is_selected ? " <" : "");
            font_bmf_draw(x_item, cur_y, sd_color, line);
            cursor_idx++;
            device_count++;
        } else {
            font_bmf_draw(x_item, cur_y, text_color, "Serial - SD card");
        }

        /* Spacing before action area */
        cur_y += line_height;

        /* Action area. All states use exactly 4 lines for consistent window height */
        if (saveload_substate == SAVELOAD_BUSY || saveload_substate == SAVELOAD_RESULT) {
            /* Line 1: Main message */
            cur_y += line_height;
            if (saveload_msg_line1) {
                font_bmf_draw(x_item, cur_y, text_color, saveload_msg_line1);
            }

            /* msg1 / empty / button / empty */
            cur_y += line_height; /* Empty separator */
            cur_y += line_height;
            if (saveload_substate == SAVELOAD_RESULT) {
                const char* btn = (saveload_msg_line1
                                   && (strstr(saveload_msg_line1, "loaded") || strstr(saveload_msg_line1, "saved")))
                                      ? "Close"
                                      : "Go back";
                font_bmf_draw(x_item, cur_y, highlight_color, btn);
            }
            cur_y += line_height; /* Empty for consistent height */
        } else if (saveload_substate == SAVELOAD_CONFIRM) {
            /* Layout: prompt / Yes / No / empty */
            /* Line 1: Prompt message */
            cur_y += line_height;
            if (saveload_pending_upgrade) {
                uint32_t ver = saveload_get_confirm_version();
                char upgrade_msg[48];
                if (saveload_pending_upgrade == 2) {
                    snprintf(upgrade_msg, sizeof(upgrade_msg), "Downgrade future save (v%lu)?", (unsigned long)ver);
                } else {
                    snprintf(upgrade_msg, sizeof(upgrade_msg), "Load will upgrade old save (v%lu).",
                             (unsigned long)ver);
                }
                font_bmf_draw(x_item, cur_y, text_color, upgrade_msg);
            } else {
                font_bmf_draw(x_item, cur_y, text_color, "Overwrite existing save?");
            }

            /* Line 2: Yes */
            cur_y += line_height;
            font_bmf_draw(x_item, cur_y, saveload_confirm_choice == 0 ? highlight_color : text_color, "Yes");

            /* Line 3: No */
            cur_y += line_height;
            font_bmf_draw(x_item, cur_y, saveload_confirm_choice == 1 ? highlight_color : text_color, "No");

            /* Line 4: Empty for consistent height */
            cur_y += line_height;
        } else {
            /* BROWSE state. Layout: Save / Load / Close / empty */
            int action_start_idx = device_count; /* device_count already includes SD if available */

            /* Line 1: Save to selected */
            cur_y += line_height;
            int is_save_cursor = (saveload_cursor == action_start_idx);
            int save_disabled = (saveload_selected_device < 0);
            uint32_t save_color = (is_save_cursor && !save_disabled) ? highlight_color : text_color;
            font_bmf_draw(x_item, cur_y, save_color, "Save to selected");

            /* Line 2: Load from selected */
            cur_y += line_height;
            int is_load_cursor = (saveload_cursor == action_start_idx + 1);
            int load_disabled = (saveload_selected_device < 0);
            uint32_t load_color = (is_load_cursor && !load_disabled) ? highlight_color : text_color;
            font_bmf_draw(x_item, cur_y, load_color, "Load from selected");

            /* Line 3: Close */
            cur_y += line_height;
            int is_close_cursor = (saveload_cursor == action_start_idx + 2);
            uint32_t close_color = is_close_cursor ? highlight_color : text_color;
            font_bmf_draw(x_item, cur_y, close_color, "Close");

            /* Line 4: Empty for consistent height */
            cur_y += line_height;
        }

        /* Serial VMU error popup, drawn on top of save/load window */
        if (saveload_show_serial_error) {
            const int err_line_height = 26;
            const int err_max_chars = 38;
            int err_width = err_max_chars * 10 + 16;
            if (err_width > 520) {
                err_width = 520;
            }
            const int err_height = (7 + 3) * err_line_height; /* title + 3 msg + empty + 2 msg + Close + padding */
            const int err_x = (640 / 2) - (err_width / 2);
            const int err_y = (480 / 2) - (err_height / 2);
            const int err_x_item = err_x + 8;

            draw_popup_menu_ex(err_x, err_y, err_width, err_height, ui_mode);

            int ey = err_y + 2;
            font_bmf_draw(err_x_item, ey, menu_title_color, "Error");
            ey += err_line_height / 4;

            ey += err_line_height;
            font_bmf_draw(err_x_item, ey, text_color, "When Serial VMU is enabled, openMenu");
            ey += err_line_height;
            font_bmf_draw(err_x_item, ey, text_color, "settings cannot be saved to a VMU.");

            ey += err_line_height; /* Empty separator */

            ey += err_line_height;
            font_bmf_draw(err_x_item, ey, text_color, "Save settings to serial SD card");
            ey += err_line_height;
            font_bmf_draw(err_x_item, ey, text_color, "instead.");

            ey += err_line_height; /* Empty separator */

            ey += err_line_height;
            font_bmf_draw(err_x_item, ey, highlight_color, "Close");
        }
    }
}

#pragma endregion SaveLoad_Menu

/* COMPACTION_TEST_START */
#pragma region Compaction_Test_Menu

/* Compaction test states */
typedef enum {
    COMPACTION_INIT,
    COMPACTION_CONFIRM,
    COMPACTION_BACKUP,
    COMPACTION_FILLING,
    COMPACTION_RESULT,
    COMPACTION_RESTORING,
    COMPACTION_DONE,
    COMPACTION_ERROR
} compaction_test_state_t;

static compaction_test_state_t compaction_state = COMPACTION_INIT;
static const char* compaction_msg = NULL;

static void
compaction_test_setup_internal(void) {
    compaction_state = COMPACTION_CONFIRM;
    compaction_msg = "Test flashrom partition compaction?";
}

void
compaction_test_setup(enum draw_state* state, theme_color* _colors, int* timeout_ptr, uint32_t title_color) {
    common_setup(state, _colors, timeout_ptr);
    menu_title_color = title_color;
    compaction_test_setup_internal();
}

static void
compaction_test_close(void) {
    compaction_test_cleanup();
    *state_ptr = DRAW_MENU;
    *input_timeout_ptr = 3;
}

void
handle_input_compaction_test(enum control input) {
    switch (compaction_state) {
        case COMPACTION_CONFIRM:
            if (input == A) {
                /* Start the test */
                compaction_state = COMPACTION_BACKUP;
            } else if (input == B) {
                compaction_test_close();
            }
            break;

        case COMPACTION_BACKUP:
            /* handled in draw loop */
            break;

        case COMPACTION_FILLING:
            /* handled in draw loop */
            /* Allow B to cancel and restore */
            if (input == B) {
                compaction_state = COMPACTION_RESTORING;
            }
            break;

        case COMPACTION_RESULT:
            /* Test completed, need to restore partition */
            if (input == A || input == B) {
                compaction_state = COMPACTION_RESTORING;
            }
            break;

        case COMPACTION_ERROR:
            /* Error occurred (e.g., backup failed), just close, nothing to restore */
            if (input == A || input == B) {
                compaction_test_close();
            }
            break;

        case COMPACTION_RESTORING:
            /* handled in draw loop */
            break;

        case COMPACTION_DONE:
            if (input == A || input == B) {
                compaction_test_close();
            }
            break;

        default: break;
    }
}

static void
update_compaction_test(void) {
    int8_t result;

    switch (compaction_state) {
        case COMPACTION_BACKUP:
            result = compaction_test_init();
            if (result == 0) {
                compaction_state = COMPACTION_FILLING;
            } else {
                compaction_msg = compaction_test_get_status();
                compaction_state = COMPACTION_ERROR;
            }
            break;

        case COMPACTION_FILLING:
            result = compaction_test_step();
            if (result == 1) {
                /* Done filling */
                int test_result = compaction_test_get_result();
                if (test_result == 1) {
                    compaction_msg = "SUCCESS: Compaction occurred!";
                } else if (test_result == 0) {
                    compaction_msg = "FAILURE: No compaction detected.";
                } else {
                    compaction_msg = compaction_test_get_status();
                }
                compaction_state = COMPACTION_RESULT;
            } else if (result < 0) {
                compaction_msg = compaction_test_get_status();
                compaction_state = COMPACTION_ERROR;
            }
            /* result == 0 means continue */
            break;

        case COMPACTION_RESTORING:
            result = compaction_test_restore();
            if (result == 0) {
                compaction_msg = "Partition restored.";
                compaction_state = COMPACTION_DONE;
            } else {
                compaction_msg = "WARNING: Restore failed!";
                compaction_state = COMPACTION_DONE;
            }
            break;

        default: break;
    }
}

void
draw_compaction_test_op(void) {
    /* Update state machine each frame */
    update_compaction_test();
}

void
draw_compaction_test_tr(void) {
    z_set_cond(205.0f);

    int width = 280;
    int height = 120;
    int x = (640 - width) / 2;
    int y = (480 - height) / 2;

    draw_popup_menu(x, y, width, height);

    int x_text = x + 12;
    int y_text = y + 8;
    char line[64];

    if (sf_ui[0] == UI_SCROLL || sf_ui[0] == UI_FOLDERS) {
        /* Scroll/Folders mode. Bitmap font */
        int line_height = 20;

        font_bmp_begin_draw();

        /* Title */
        font_bmp_set_color(menu_title_color);
        font_bmp_draw_main(x_text, y_text, "Flashrom Compaction Test");
        y_text += line_height + 4;

        switch (compaction_state) {
            case COMPACTION_CONFIRM:
                font_bmp_set_color(text_color);
                font_bmp_draw_main(x_text, y_text, "Fill flashrom partition 2 to");
                y_text += line_height;
                font_bmp_draw_main(x_text, y_text, "test BIOS auto-compaction.");
                y_text += line_height + 4;
                font_bmp_set_color(highlight_color);
                font_bmp_draw_main(x_text, y_text, "A: Start   B: Cancel");
                break;

            case COMPACTION_BACKUP:
                font_bmp_set_color(text_color);
                font_bmp_draw_main(x_text, y_text, "Backing up partition...");
                break;

            case COMPACTION_FILLING:
                snprintf(line, sizeof(line), "Writing: %d / %d", compaction_test_get_write_count(),
                         compaction_test_get_total_blocks());
                font_bmp_set_color(text_color);
                font_bmp_draw_main(x_text, y_text, line);
                y_text += line_height;
                font_bmp_draw_main(x_text, y_text, "B: Cancel and restore");
                break;

            case COMPACTION_RESULT:
                font_bmp_set_color(text_color);
                if (compaction_msg) {
                    font_bmp_draw_main(x_text, y_text, compaction_msg);
                }
                y_text += line_height + 4;
                font_bmp_set_color(highlight_color);
                font_bmp_draw_main(x_text, y_text, "Press A/B to restore");
                break;

            case COMPACTION_ERROR:
                font_bmp_set_color(text_color);
                if (compaction_msg) {
                    font_bmp_draw_main(x_text, y_text, compaction_msg);
                }
                y_text += line_height + 4;
                font_bmp_set_color(highlight_color);
                font_bmp_draw_main(x_text, y_text, "Press A/B to close");
                break;

            case COMPACTION_RESTORING:
                font_bmp_set_color(text_color);
                font_bmp_draw_main(x_text, y_text, "Restoring partition...");
                break;

            case COMPACTION_DONE:
                font_bmp_set_color(text_color);
                if (compaction_msg) {
                    font_bmp_draw_main(x_text, y_text, compaction_msg);
                }
                y_text += line_height + 4;
                font_bmp_set_color(highlight_color);
                font_bmp_draw_main(x_text, y_text, "Press A/B to close");
                break;

            default: break;
        }
    } else {
        /* LineDesc/Grid mode. BMF font */
        int line_height = 24;

        /* Title */
        font_bmf_draw(x_text, y_text, menu_title_color, "Flashrom Compaction Test");
        y_text += line_height + 4;

        switch (compaction_state) {
            case COMPACTION_CONFIRM:
                font_bmf_draw(x_text, y_text, text_color, "Fill flashrom partition 2 to");
                y_text += line_height;
                font_bmf_draw(x_text, y_text, text_color, "test BIOS auto-compaction.");
                y_text += line_height + 4;
                font_bmf_draw(x_text, y_text, highlight_color, "A: Start   B: Cancel");
                break;

            case COMPACTION_BACKUP: font_bmf_draw(x_text, y_text, text_color, "Backing up partition..."); break;

            case COMPACTION_FILLING:
                snprintf(line, sizeof(line), "Writing: %d / %d", compaction_test_get_write_count(),
                         compaction_test_get_total_blocks());
                font_bmf_draw(x_text, y_text, text_color, line);
                y_text += line_height;
                font_bmf_draw(x_text, y_text, text_color, "B: Cancel and restore");
                break;

            case COMPACTION_RESULT:
                if (compaction_msg) {
                    font_bmf_draw(x_text, y_text, text_color, compaction_msg);
                }
                y_text += line_height + 4;
                font_bmf_draw(x_text, y_text, highlight_color, "Press A/B to restore");
                break;

            case COMPACTION_ERROR:
                if (compaction_msg) {
                    font_bmf_draw(x_text, y_text, text_color, compaction_msg);
                }
                y_text += line_height + 4;
                font_bmf_draw(x_text, y_text, highlight_color, "Press A/B to close");
                break;

            case COMPACTION_RESTORING: font_bmf_draw(x_text, y_text, text_color, "Restoring partition..."); break;

            case COMPACTION_DONE:
                if (compaction_msg) {
                    font_bmf_draw(x_text, y_text, text_color, compaction_msg);
                }
                y_text += line_height + 4;
                font_bmf_draw(x_text, y_text, highlight_color, "Press A/B to close");
                break;

            default: break;
        }
    }
}

#pragma endregion Compaction_Test_Menu
/* COMPACTION_TEST_END */

#pragma region Serial_VMU

#define SERIAL_VMU_BLOCKS     256
#define SERIAL_VMU_BLOCK_SIZE 512
#define SERIAL_VMU_TOTAL_SIZE (SERIAL_VMU_BLOCKS * SERIAL_VMU_BLOCK_SIZE) /* 131072 = 128KB */
#define SERIAL_VMU_SAVES_DIR  "/sd/OPENMENU/SAVES"
#define SERIAL_VMU_LASTDISC   "/sd/OPENMENU/LASTDISC.TXT"
#define SERIAL_VMU_NUM_SLOTS  5

typedef enum {
    SERIAL_VMU_IDLE,

    /* Restore flow (game launch / exit to BIOS) */
    SERIAL_VMU_RESTORE_BUSY,
    SERIAL_VMU_RESTORE_FAILED,

    /* Backup flow (openMenu boot) */
    SERIAL_VMU_BACKUP_BUSY,
    SERIAL_VMU_BACKUP_FAILED,

    /* Decision states */
    SERIAL_VMU_FIRST_TIME,
    SERIAL_VMU_WIPE_CONFIRM,
    SERIAL_VMU_WIPE_BUSY,
    SERIAL_VMU_CORRUPT_FILE,
    SERIAL_VMU_NO_SD,
    SERIAL_VMU_NO_SD_BACKUP,
    SERIAL_VMU_NO_VMU,
    SERIAL_VMU_SLOT_SELECT,
} serial_vmu_state_t;

typedef struct {
    serial_vmu_state_t state;
    serial_vmu_launch_action_t launch_action;

    /* Game info */
    char serial_id[16];
    char game_name[128];
    char game_line[56];
    const gd_item* launch_item;

    /* VMU slot */
    int vmu_device_id; /* 0-7, from setting or fallback selector */
    maple_device_t* vmu_dev;

    /* Progress */
    int current_block;
    int error_block;

    /* Buffer */
    uint8_t* buffer;

    /* Exit to BIOS params */
    int exit_mount_disc;

    /* Menu cursor for windows with selectable options */
    int menu_cursor;
    int menu_num_options;

    /* Fallback VMU selector */
    int selector_cursor;
    int selected_device; /* raw device_id (0-7) of user-selected VMU, or -1 */

    /* Is this a backup or restore operation? (for context-sensitive messages) */
    int is_backup;

    /* File validation */
    int actual_file_size;

    /* Multi-slot support */
    int slot_number;                                /* 1-5, selected slot */
    int slot_cursor;                                /* 0-4 cursor position */
    char slot_file_id[24];                          /* "<SERIAL>-<SLOT>" for LASTDISC.TXT */
    char slot_timestamps[SERIAL_VMU_NUM_SLOTS][24]; /* "YYYY-MM-DD HH:MM:SS" or "EMPTY" */
    char slot_labels[SERIAL_VMU_NUM_SLOTS][29];     /* custom label from .TXT, max 28 chars + null */
    int remembered_slot;                            /* slot parsed from LASTDISC.TXT */
    int all_slots_empty;                            /* 1 if all 5 slots are EMPTY */
} serial_vmu_ctx_t;

static serial_vmu_ctx_t svmu_ctx;

/* Cached layout state for serial VMU window */
static bool svmu_layout_dirty = true;
static int svmu_cached_bmp_width = 0;
static int svmu_cached_bmf_width = 0;
static int svmu_cached_content_lines = 0;
static int svmu_layout_state = -1;

static int
serial_vmu_setting_to_device_id(uint8_t setting) {
    if (setting == SERIAL_VMU_OFF) {
        return -1;
    }
    return (int)(setting - SERIAL_VMU_A1); /* A1=0, A2=1, B1=2, ... D2=7 */
}

static maple_device_t*
serial_vmu_get_dev(int device_id) {
    int port = device_id / 2;
    int unit = (device_id % 2 == 0) ? 1 : 2;
    return maple_enum_dev(port, unit);
}

static const char*
serial_vmu_port_name(int device_id) {
    static const char* ports[] = {"A", "A", "B", "B", "C", "C", "D", "D"};
    if (device_id < 0 || device_id > 7) {
        return "?";
    }
    return ports[device_id];
}

static int
serial_vmu_socket_num(int device_id) {
    return (device_id % 2 == 0) ? 1 : 2;
}

static void
serial_vmu_format_game_line(char* out, size_t out_size, const char* serial_id, const char* game_name, int max_chars) {
    int prefix_len = strlen(serial_id) + 3; /* serial + " - " */
    int name_max = max_chars - prefix_len;
    if (name_max < 4) {
        snprintf(out, out_size, "%s", serial_id);
        return;
    }
    if ((int)strlen(game_name) <= name_max) {
        snprintf(out, out_size, "%s - %s", serial_id, game_name);
    } else {
        snprintf(out, out_size, "%s - %.*s...", serial_id, name_max - 3, game_name);
    }
}

/* Build slot file identifier: "<SERIAL>-<SLOT>" */
static void
serial_vmu_build_slot_file_id(char* out, size_t out_size, const char* serial_id, int slot) {
    snprintf(out, out_size, "%s-%d", serial_id, slot);
}

/* Populate slot_timestamps[] by stat()'ing each slot's .VMU file */
static void
serial_vmu_populate_slot_timestamps(void) {
    int empty_count = 0;
    for (int i = 0; i < SERIAL_VMU_NUM_SLOTS; i++) {
        char path[64];
        snprintf(path, sizeof(path), "%s/%s/SLOT%d.VMU", SERIAL_VMU_SAVES_DIR, svmu_ctx.serial_id, i + 1);
        struct stat st;
        if (stat(path, &st) == 0 && st.st_size > 0) {
            if (st.st_mtime > 0) {
                struct tm* tm = localtime(&st.st_mtime);
                if (tm) {
                    snprintf(svmu_ctx.slot_timestamps[i], sizeof(svmu_ctx.slot_timestamps[i]),
                             "%04d-%02d-%02d %02d:%02d:%02d", tm->tm_year + 1900, tm->tm_mon + 1, tm->tm_mday,
                             tm->tm_hour, tm->tm_min, tm->tm_sec);
                } else {
                    strncpy(svmu_ctx.slot_timestamps[i], "UNKNOWN", sizeof(svmu_ctx.slot_timestamps[i]));
                }
            } else {
                strncpy(svmu_ctx.slot_timestamps[i], "UNKNOWN", sizeof(svmu_ctx.slot_timestamps[i]));
            }
        } else {
            strncpy(svmu_ctx.slot_timestamps[i], "EMPTY", sizeof(svmu_ctx.slot_timestamps[i]));
            empty_count++;
        }

        /* Check for custom slot label (.TXT file) */
        snprintf(path, sizeof(path), "%s/%s/SLOT%d.TXT", SERIAL_VMU_SAVES_DIR, svmu_ctx.serial_id, i + 1);
        int fd = open(path, O_RDONLY);
        if (fd >= 0) {
            char buf[64];
            ssize_t n = read(fd, buf, sizeof(buf) - 1);
            close(fd);
            if (n > 0) {
                buf[n] = '\0';
                /* Trim trailing whitespace/newlines */
                while (n > 0 && (buf[n - 1] == '\n' || buf[n - 1] == '\r' || buf[n - 1] == ' ')) {
                    buf[--n] = '\0';
                }
                /* Trim leading whitespace */
                char* start = buf;
                while (*start == ' ' || *start == '\t') {
                    start++;
                }
                /* Take only first line */
                char* nl = strchr(start, '\n');
                if (nl) {
                    *nl = '\0';
                }
                nl = strchr(start, '\r');
                if (nl) {
                    *nl = '\0';
                }
                /* Truncate to 28 chars with "..." if needed */
                if ((int)strlen(start) > 28) {
                    memcpy(svmu_ctx.slot_labels[i], start, 25);
                    svmu_ctx.slot_labels[i][25] = '.';
                    svmu_ctx.slot_labels[i][26] = '.';
                    svmu_ctx.slot_labels[i][27] = '.';
                    svmu_ctx.slot_labels[i][28] = '\0';
                } else if (strlen(start) > 0) {
                    strncpy(svmu_ctx.slot_labels[i], start, sizeof(svmu_ctx.slot_labels[i]) - 1);
                }
            }
        }
    }
    svmu_ctx.all_slots_empty = (empty_count == SERIAL_VMU_NUM_SLOTS);
}

/* Parse LASTDISC.TXT content: find last hyphen, extract slot number.
 * Returns slot 1-5, or 1 if no valid slot suffix. */
static int
serial_vmu_parse_lastdisc(const char* lastdisc_str, char* serial_out, size_t serial_max) {
    const char* last_hyphen = strrchr(lastdisc_str, '-');
    if (last_hyphen && last_hyphen[1] >= '1' && last_hyphen[1] <= '5' && last_hyphen[2] == '\0') {
        size_t serial_len = (size_t)(last_hyphen - lastdisc_str);
        if (serial_len >= serial_max) {
            serial_len = serial_max - 1;
        }
        memcpy(serial_out, lastdisc_str, serial_len);
        serial_out[serial_len] = '\0';
        return last_hyphen[1] - '0';
    }
    /* Fallback: no valid slot suffix, treat as slot 1 */
    strncpy(serial_out, lastdisc_str, serial_max - 1);
    serial_out[serial_max - 1] = '\0';
    return 1;
}

static int
serial_vmu_ensure_dirs(const char* serial) {
    struct stat st;
    if (stat("/sd/OPENMENU", &st) != 0) {
        if (mkdir("/sd/OPENMENU", 0755) != 0) {
            return -1;
        }
    }
    if (stat(SERIAL_VMU_SAVES_DIR, &st) != 0) {
        if (mkdir(SERIAL_VMU_SAVES_DIR, 0755) != 0) {
            return -1;
        }
    }
    if (serial) {
        char serial_dir[64];
        snprintf(serial_dir, sizeof(serial_dir), "%s/%s", SERIAL_VMU_SAVES_DIR, serial);
        if (stat(serial_dir, &st) != 0) {
            if (mkdir(serial_dir, 0755) != 0) {
                return -1;
            }
        }
    }
    return 0;
}

static bool
serial_vmu_read_lastdisc(char* serial_out, size_t max_len) {
    int fd = open(SERIAL_VMU_LASTDISC, O_RDONLY);
    if (fd < 0) {
        return false;
    }

    ssize_t n = read(fd, serial_out, max_len - 1);
    close(fd);
    if (n <= 0) {
        return false;
    }

    serial_out[n] = '\0';
    /* Trim trailing whitespace/newline */
    while (n > 0 && (serial_out[n - 1] == '\n' || serial_out[n - 1] == '\r' || serial_out[n - 1] == ' ')) {
        serial_out[--n] = '\0';
    }
    return n > 0;
}

static bool
serial_vmu_write_lastdisc(const char* serial) {
    if (serial_vmu_ensure_dirs(NULL) != 0) {
        return false;
    }
    int fd = open(SERIAL_VMU_LASTDISC, O_WRONLY | O_CREAT | O_TRUNC, 0644);
    if (fd < 0) {
        return false;
    }
    ssize_t len = strlen(serial);
    ssize_t written = write(fd, serial, len);
    close(fd);
    fs_fat_sync("/sd");
    return written == len;
}

static bool
serial_vmu_clear_lastdisc(void) {
    unlink(SERIAL_VMU_LASTDISC);
    fs_fat_sync("/sd");
    return true;
}

static bool
serial_vmu_write_title_file(const char* serial, const char* title) {
    char path[64];
    snprintf(path, sizeof(path), "%s/%s/TITLE.TXT", SERIAL_VMU_SAVES_DIR, serial);
    ssize_t title_len = strlen(title);

    /* Check if file already exists with the same content */
    int fd = open(path, O_RDONLY);
    if (fd >= 0) {
        char buf[64];
        ssize_t n = read(fd, buf, sizeof(buf));
        close(fd);
        if (n == title_len && memcmp(buf, title, title_len) == 0) {
            return true; /* Already up to date */
        }
    }

    if (serial_vmu_ensure_dirs(serial) != 0) {
        return false;
    }
    fd = open(path, O_WRONLY | O_CREAT | O_TRUNC, 0644);
    if (fd < 0) {
        return false;
    }
    write(fd, title, title_len);
    close(fd);
    fs_fat_sync("/sd");
    return true;
}

typedef enum {
    SAVE_FILE_OK,
    SAVE_FILE_NOT_FOUND,
    SAVE_FILE_WRONG_SIZE,
    SAVE_FILE_READ_ERROR,
} save_file_status_t;

static save_file_status_t
serial_vmu_validate_file(const char* serial, int slot, int* actual_size) {
    char path[64];
    snprintf(path, sizeof(path), "%s/%s/SLOT%d.VMU", SERIAL_VMU_SAVES_DIR, serial, slot);

    struct stat st;
    if (stat(path, &st) != 0) {
        if (actual_size) {
            *actual_size = 0;
        }
        return SAVE_FILE_NOT_FOUND;
    }
    if (actual_size) {
        *actual_size = (int)st.st_size;
    }
    if (st.st_size != SERIAL_VMU_TOTAL_SIZE) {
        return SAVE_FILE_WRONG_SIZE;
    }
    return SAVE_FILE_OK;
}

/* Read entire VMU save file from SD into buffer (for restore) */
static int
serial_vmu_read_save_file(const char* serial, int slot, uint8_t* buffer) {
    char path[64];
    snprintf(path, sizeof(path), "%s/%s/SLOT%d.VMU", SERIAL_VMU_SAVES_DIR, serial, slot);
    int fd = open(path, O_RDONLY);
    if (fd < 0) {
        return -1;
    }
    ssize_t n = read(fd, buffer, SERIAL_VMU_TOTAL_SIZE);
    close(fd);
    return (n == SERIAL_VMU_TOTAL_SIZE) ? 0 : -1;
}

/* Write entire VMU buffer to SD save file (for backup) */
static int
serial_vmu_write_save_file(const char* serial, int slot, const uint8_t* buffer) {
    if (serial_vmu_ensure_dirs(serial) != 0) {
        return -1;
    }
    char path[64];
    snprintf(path, sizeof(path), "%s/%s/SLOT%d.VMU", SERIAL_VMU_SAVES_DIR, serial, slot);
    int fd = open(path, O_WRONLY | O_CREAT | O_TRUNC, 0644);
    if (fd < 0) {
        return -1;
    }
    ssize_t written = write(fd, buffer, SERIAL_VMU_TOTAL_SIZE);
    close(fd);
    fs_fat_sync("/sd");
    return (written == SERIAL_VMU_TOTAL_SIZE) ? 0 : -1;
}

/* Look up a game name by serial ID from the parsed game list */
static const char*
serial_vmu_find_game_name(const char* serial_id) {
    const gd_item** list = list_get();
    int len = list_length();
    for (int i = 0; i < len; i++) {
        const gd_item* item = list[i];
        if (item && strcmp(item->product, serial_id) == 0) {
            return item->name;
        }
    }
    return NULL;
}

static void
serial_vmu_init_context(const char* serial_id, const char* game_name, int is_backup) {
    memset(&svmu_ctx, 0, sizeof(svmu_ctx));
    svmu_ctx.state = SERIAL_VMU_IDLE;
    svmu_ctx.error_block = -1;
    svmu_ctx.is_backup = is_backup;

    strncpy(svmu_ctx.serial_id, serial_id, sizeof(svmu_ctx.serial_id) - 1);
    if (game_name) {
        strncpy(svmu_ctx.game_name, game_name, sizeof(svmu_ctx.game_name) - 1);
    }
    int game_line_max = (sf_ui[0] == UI_SCROLL || sf_ui[0] == UI_FOLDERS) ? 45 : 36;
    serial_vmu_format_game_line(svmu_ctx.game_line, sizeof(svmu_ctx.game_line), svmu_ctx.serial_id, svmu_ctx.game_name,
                                game_line_max);

    svmu_ctx.vmu_device_id = serial_vmu_setting_to_device_id(sf_serial_vmu[0]);
    svmu_ctx.slot_number = 1;
    svmu_ctx.remembered_slot = 1;
}

/* Perform the action after a successful restore or "launch anyway" */
static void
serial_vmu_do_launch(void) {
    /* Write LASTDISC.TXT so backup happens on next boot (includes slot suffix) */
    serial_vmu_write_lastdisc(svmu_ctx.slot_file_id);

    /* Write companion title file */
    if (svmu_ctx.game_name[0]) {
        serial_vmu_write_title_file(svmu_ctx.serial_id, svmu_ctx.game_name);
    }

    switch (svmu_ctx.launch_action) {
        case SERIAL_VMU_LAUNCH_DC: dreamcast_launch_disc(svmu_ctx.launch_item); break;
        case SERIAL_VMU_LAUNCH_BLEEM: bleem_launch(svmu_ctx.launch_item); break;
        case SERIAL_VMU_LAUNCH_BLOOM: bloom_launch(svmu_ctx.launch_item); break;
        case SERIAL_VMU_LAUNCH_CB: dreamcast_launch_cb(svmu_ctx.launch_item); break;
        case SERIAL_VMU_LAUNCH_EXIT_BIOS: exit_to_bios_ex(svmu_ctx.exit_mount_disc, 0); break;
        case SERIAL_VMU_LAUNCH_NONE: break;
    }
}

/* Perform the action after a successful backup */
static void
serial_vmu_finish_backup(void) {
    serial_vmu_clear_lastdisc();
    if (svmu_ctx.buffer) {
        free(svmu_ctx.buffer);
        svmu_ctx.buffer = NULL;
    }
    *state_ptr = DRAW_UI;
    *input_timeout_ptr = 3;
}

/* Forward declarations for functions defined later */
static void serial_vmu_reset_selector_tracking(void);
static void serial_vmu_live_update_selector(void);

/* Begin the restore flow. Checks SD, VMU, file validity */
static void
serial_vmu_begin_restore_flow(void) {
    /* Free previous buffer if any */
    if (svmu_ctx.buffer) {
        free(svmu_ctx.buffer);
        svmu_ctx.buffer = NULL;
    }

    /* Check SD availability */
    if (!savefile_sd_available()) {
        svmu_ctx.state = SERIAL_VMU_NO_SD;
        svmu_ctx.menu_cursor = 0;
        svmu_ctx.menu_num_options = 3;
        return;
    }

    /* Check VMU in configured slot */
    svmu_ctx.vmu_dev = serial_vmu_get_dev(svmu_ctx.vmu_device_id);
    if (!svmu_ctx.vmu_dev) {
        svmu_ctx.state = SERIAL_VMU_NO_VMU;
        svmu_ctx.selected_device = -1;
        int detected = serial_vmu_detected_count();
        svmu_ctx.selector_cursor = (detected > 0) ? 0 : detected + 1; /* first device, or Cancel */
        serial_vmu_reset_selector_tracking();
        return;
    }

    /* Check save file */
    save_file_status_t status =
        serial_vmu_validate_file(svmu_ctx.serial_id, svmu_ctx.slot_number, &svmu_ctx.actual_file_size);
    switch (status) {
        case SAVE_FILE_OK:
            /* Allocate buffer and read file */
            svmu_ctx.buffer = malloc(SERIAL_VMU_TOTAL_SIZE);
            if (!svmu_ctx.buffer) {
                svmu_ctx.state = SERIAL_VMU_RESTORE_FAILED;
                svmu_ctx.error_block = -1;
                svmu_ctx.menu_cursor = 0;
                svmu_ctx.menu_num_options = 4;
                return;
            }
            if (serial_vmu_read_save_file(svmu_ctx.serial_id, svmu_ctx.slot_number, svmu_ctx.buffer) != 0) {
                free(svmu_ctx.buffer);
                svmu_ctx.buffer = NULL;
                svmu_ctx.state = SERIAL_VMU_RESTORE_FAILED;
                svmu_ctx.error_block = -1;
                svmu_ctx.menu_cursor = 0;
                svmu_ctx.menu_num_options = 4;
                return;
            }
            svmu_ctx.state = SERIAL_VMU_RESTORE_BUSY;
            svmu_ctx.current_block = 0;
            vmu_draw_lcd_auto(svmu_ctx.vmu_dev, openmenu_lcd_access);
            break;
        case SAVE_FILE_NOT_FOUND:
            svmu_ctx.state = SERIAL_VMU_FIRST_TIME;
            svmu_ctx.menu_cursor = 0;
            svmu_ctx.menu_num_options = 4;
            break;
        case SAVE_FILE_WRONG_SIZE:
            svmu_ctx.state = SERIAL_VMU_CORRUPT_FILE;
            svmu_ctx.menu_cursor = 0;
            svmu_ctx.menu_num_options = 3;
            break;
        default:
            svmu_ctx.state = SERIAL_VMU_RESTORE_FAILED;
            svmu_ctx.error_block = -1;
            svmu_ctx.menu_cursor = 0;
            svmu_ctx.menu_num_options = 4;
            break;
    }
}

/* Begin the backup flow. Checks SD, VMU */
static void
serial_vmu_begin_backup_flow(void) {
    /* Free previous buffer if any */
    if (svmu_ctx.buffer) {
        free(svmu_ctx.buffer);
        svmu_ctx.buffer = NULL;
    }

    /* Check SD availability */
    if (!savefile_sd_available()) {
        svmu_ctx.state = SERIAL_VMU_NO_SD_BACKUP;
        svmu_ctx.menu_cursor = 0;
        svmu_ctx.menu_num_options = 3;
        return;
    }

    /* Check VMU in configured slot */
    svmu_ctx.vmu_dev = serial_vmu_get_dev(svmu_ctx.vmu_device_id);
    if (!svmu_ctx.vmu_dev) {
        svmu_ctx.state = SERIAL_VMU_NO_VMU;
        svmu_ctx.selected_device = -1;
        int detected = serial_vmu_detected_count();
        svmu_ctx.selector_cursor = (detected > 0) ? 0 : detected + 1; /* first device, or Cancel */
        serial_vmu_reset_selector_tracking();
        return;
    }

    /* Allocate buffer for reading VMU */
    svmu_ctx.buffer = malloc(SERIAL_VMU_TOTAL_SIZE);
    if (!svmu_ctx.buffer) {
        svmu_ctx.state = SERIAL_VMU_BACKUP_FAILED;
        svmu_ctx.error_block = -1;
        svmu_ctx.menu_cursor = 0;
        svmu_ctx.menu_num_options = 3;
        return;
    }
    svmu_ctx.state = SERIAL_VMU_BACKUP_BUSY;
    svmu_ctx.current_block = 0;
    vmu_draw_lcd_auto(svmu_ctx.vmu_dev, openmenu_lcd_access);
}

void
serial_vmu_setup(enum draw_state* state, theme_color* _colors, int* timeout_ptr, uint32_t title_color) {
    common_setup(state, _colors, timeout_ptr);
    menu_title_color = title_color;
}

/* Start a restore for game launch (called from UI mode menu_accept) */
void
serial_vmu_start_restore(const gd_item* item, serial_vmu_launch_action_t action) {
    /* Guard: skip Serial VMU for items with no serial ID, launch directly */
    if (!item->product[0]) {
        switch (action) {
            case SERIAL_VMU_LAUNCH_DC: dreamcast_launch_disc(item); break;
            case SERIAL_VMU_LAUNCH_BLEEM: bleem_launch(item); break;
            case SERIAL_VMU_LAUNCH_BLOOM: bloom_launch(item); break;
            case SERIAL_VMU_LAUNCH_CB: dreamcast_launch_cb(item); break;
            default: menu_leave(); break;
        }
        return;
    }
    /* Gate: ensure SD is available before any file I/O */
    serial_vmu_init_context(item->product, item->name, 0);
    svmu_ctx.launch_item = item;
    svmu_ctx.launch_action = action;
    if (!savefile_sd_available()) {
        svmu_ctx.state = SERIAL_VMU_NO_SD;
        svmu_ctx.menu_cursor = 0;
        svmu_ctx.menu_num_options = 3;
        return;
    }
    if (sf_serial_vmu_multislot[0] == SERIAL_VMU_MULTISLOT_ON) {
        serial_vmu_populate_slot_timestamps();
        if (svmu_ctx.all_slots_empty) {
            /* No saves in any slot. Skip selector, default to slot 1 */
            svmu_ctx.slot_number = 1;
            serial_vmu_build_slot_file_id(svmu_ctx.slot_file_id, sizeof(svmu_ctx.slot_file_id), svmu_ctx.serial_id, 1);
            serial_vmu_begin_restore_flow();
        } else {
            svmu_ctx.state = SERIAL_VMU_SLOT_SELECT;
        }
    } else {
        svmu_ctx.slot_number = 1;
        serial_vmu_build_slot_file_id(svmu_ctx.slot_file_id, sizeof(svmu_ctx.slot_file_id), svmu_ctx.serial_id, 1);
        serial_vmu_begin_restore_flow();
    }
}

/* Start a restore for exit to BIOS */
static void
serial_vmu_start_exit_restore(int mount_disc) {
    const gd_item* item = get_cur_game_item();
    if (!item) {
        return;
    }
    /* Guard: skip Serial VMU for items with no serial ID, exit directly */
    if (!item->product[0]) {
        exit_to_bios_ex(mount_disc, 0);
        return;
    }
    /* Gate: ensure SD is available before any file I/O */
    serial_vmu_init_context(item->product, item->name, 0);
    svmu_ctx.launch_item = item;
    svmu_ctx.launch_action = SERIAL_VMU_LAUNCH_EXIT_BIOS;
    svmu_ctx.exit_mount_disc = mount_disc;
    if (!savefile_sd_available()) {
        svmu_ctx.state = SERIAL_VMU_NO_SD;
        svmu_ctx.menu_cursor = 0;
        svmu_ctx.menu_num_options = 3;
        return;
    }
    if (sf_serial_vmu_multislot[0] == SERIAL_VMU_MULTISLOT_ON) {
        serial_vmu_populate_slot_timestamps();
        if (svmu_ctx.all_slots_empty) {
            svmu_ctx.slot_number = 1;
            serial_vmu_build_slot_file_id(svmu_ctx.slot_file_id, sizeof(svmu_ctx.slot_file_id), svmu_ctx.serial_id, 1);
            serial_vmu_begin_restore_flow();
        } else {
            svmu_ctx.state = SERIAL_VMU_SLOT_SELECT;
        }
    } else {
        svmu_ctx.slot_number = 1;
        serial_vmu_build_slot_file_id(svmu_ctx.slot_file_id, sizeof(svmu_ctx.slot_file_id), svmu_ctx.serial_id, 1);
        serial_vmu_begin_restore_flow();
    }
}

/* Check for pending backup on boot */
void
serial_vmu_check_boot_backup(enum draw_state* draw_current_ptr, theme_color* _colors, int* timeout_ptr,
                             uint32_t title_color) {
    if (sf_serial_vmu[0] == SERIAL_VMU_OFF) {
        return;
    }

    /* Gate: ensure SD is available before any file I/O */
    if (!savefile_sd_available()) {
        return;
    }

    char lastdisc_str[32] = {0};
    if (!serial_vmu_read_lastdisc(lastdisc_str, sizeof(lastdisc_str))) {
        return;
    }

    /* Parse serial ID and slot number from LASTDISC.TXT (e.g., "HDR-0000-2") */
    char serial_id[16] = {0};
    int remembered_slot = serial_vmu_parse_lastdisc(lastdisc_str, serial_id, sizeof(serial_id));

    /* Look up game name from parsed list */
    const char* name = serial_vmu_find_game_name(serial_id);

    serial_vmu_init_context(serial_id, name ? name : "", 1);
    svmu_ctx.launch_action = SERIAL_VMU_LAUNCH_NONE;
    svmu_ctx.remembered_slot = remembered_slot;

    *draw_current_ptr = DRAW_SERIAL_VMU;
    serial_vmu_setup(draw_current_ptr, _colors, timeout_ptr, title_color);

    if (sf_serial_vmu_multislot[0] == SERIAL_VMU_MULTISLOT_ON) {
        serial_vmu_populate_slot_timestamps();
        svmu_ctx.slot_cursor = remembered_slot - 1;
        svmu_ctx.state = SERIAL_VMU_SLOT_SELECT;
    } else {
        svmu_ctx.slot_number = 1;
        serial_vmu_build_slot_file_id(svmu_ctx.slot_file_id, sizeof(svmu_ctx.slot_file_id), svmu_ctx.serial_id, 1);
        serial_vmu_begin_backup_flow();
    }
}

void
draw_serial_vmu_op(void) {
    switch (svmu_ctx.state) {
        case SERIAL_VMU_RESTORE_BUSY: {
            if (svmu_ctx.current_block >= SERIAL_VMU_BLOCKS) {
                /* Restore complete */
                if (svmu_ctx.buffer) {
                    free(svmu_ctx.buffer);
                    svmu_ctx.buffer = NULL;
                }
                vmu_draw_lcd_auto(svmu_ctx.vmu_dev, openmenu_lcd);
                serial_vmu_do_launch();
                return;
            }
            /* Write one block to VMU */
            int ret = vmu_block_write(svmu_ctx.vmu_dev, (uint16_t)svmu_ctx.current_block,
                                      &svmu_ctx.buffer[svmu_ctx.current_block * SERIAL_VMU_BLOCK_SIZE]);
            if (ret != MAPLE_EOK) {
                svmu_ctx.error_block = svmu_ctx.current_block;
                svmu_ctx.state = SERIAL_VMU_RESTORE_FAILED;
                svmu_ctx.menu_cursor = 0;
                svmu_ctx.menu_num_options = 4;
                if (svmu_ctx.buffer) {
                    free(svmu_ctx.buffer);
                    svmu_ctx.buffer = NULL;
                }
                vmu_draw_lcd_auto(svmu_ctx.vmu_dev, openmenu_lcd);
                return;
            }
            svmu_ctx.current_block++;
            break;
        }

        case SERIAL_VMU_BACKUP_BUSY: {
            if (svmu_ctx.current_block >= SERIAL_VMU_BLOCKS) {
                /* All blocks read from VMU, write to SD */
                if (serial_vmu_write_save_file(svmu_ctx.serial_id, svmu_ctx.slot_number, svmu_ctx.buffer) != 0) {
                    svmu_ctx.error_block = -1;
                    svmu_ctx.state = SERIAL_VMU_BACKUP_FAILED;
                    svmu_ctx.menu_cursor = 0;
                    svmu_ctx.menu_num_options = 3;
                    if (svmu_ctx.buffer) {
                        free(svmu_ctx.buffer);
                        svmu_ctx.buffer = NULL;
                    }
                    vmu_draw_lcd_auto(svmu_ctx.vmu_dev, openmenu_lcd);
                    return;
                }
                /* Write companion title file */
                if (svmu_ctx.game_name[0]) {
                    serial_vmu_write_title_file(svmu_ctx.serial_id, svmu_ctx.game_name);
                }
                vmu_draw_lcd_auto(svmu_ctx.vmu_dev, openmenu_lcd);
                serial_vmu_finish_backup();
                return;
            }
            /* Read one block from VMU */
            int ret = vmu_block_read(svmu_ctx.vmu_dev, (uint16_t)svmu_ctx.current_block,
                                     &svmu_ctx.buffer[svmu_ctx.current_block * SERIAL_VMU_BLOCK_SIZE]);
            if (ret != MAPLE_EOK) {
                svmu_ctx.error_block = svmu_ctx.current_block;
                svmu_ctx.state = SERIAL_VMU_BACKUP_FAILED;
                svmu_ctx.menu_cursor = 0;
                svmu_ctx.menu_num_options = 3;
                if (svmu_ctx.buffer) {
                    free(svmu_ctx.buffer);
                    svmu_ctx.buffer = NULL;
                }
                vmu_draw_lcd_auto(svmu_ctx.vmu_dev, openmenu_lcd);
                return;
            }
            svmu_ctx.current_block++;
            break;
        }

        case SERIAL_VMU_WIPE_BUSY: {
            if (svmu_ctx.current_block >= SERIAL_VMU_BLOCKS) {
                /* Wipe complete, free buffer and launch game */
                if (svmu_ctx.buffer) {
                    free(svmu_ctx.buffer);
                    svmu_ctx.buffer = NULL;
                }
                vmu_draw_lcd_auto(svmu_ctx.vmu_dev, openmenu_lcd);
                serial_vmu_do_launch();
                return;
            }
            /* Write one block from EMPTY.VMU image to VMU */
            int ret = vmu_block_write(svmu_ctx.vmu_dev, (uint16_t)svmu_ctx.current_block,
                                      &svmu_ctx.buffer[svmu_ctx.current_block * SERIAL_VMU_BLOCK_SIZE]);
            if (ret != MAPLE_EOK) {
                /* Wipe failed, free buffer and still launch */
                if (svmu_ctx.buffer) {
                    free(svmu_ctx.buffer);
                    svmu_ctx.buffer = NULL;
                }
                vmu_draw_lcd_auto(svmu_ctx.vmu_dev, openmenu_lcd);
                serial_vmu_do_launch();
                return;
            }
            svmu_ctx.current_block++;
            break;
        }

        case SERIAL_VMU_SLOT_SELECT: break;

        default: break;
    }
}

/* Recompute cached layout for serial VMU window.
 * Called only when state or device configuration changes. */
static void
svmu_recalc_layout(void) {
    int content_lines = 0;
    int max_text_width = 10; /* "Serial VMU" title */
    char line_buf[80];

    switch (svmu_ctx.state) {
        case SERIAL_VMU_NO_SD:
            content_lines = 7;
            max_text_width = 39; /* "Per-game Serial VMUs are not available." */
            break;
        case SERIAL_VMU_NO_SD_BACKUP: {
            content_lines = 7;
            int gl = (int)strlen(svmu_ctx.game_line);
            max_text_width = gl > 44 ? gl : 44; /* vs "Backup pending, but no serial SD card found." */
            break;
        }
        case SERIAL_VMU_NO_VMU: {
            /* game_line + blank + 4 header + 8 slots + 4 footer */
            content_lines = 18;
            int gl = (int)strlen(svmu_ctx.game_line);
            int min_w = svmu_ctx.is_backup ? 37 : /* "Skip for now (ask again on next boot)" */
                            40;                   /* "Launch without Serial VMU restore/backup" */
            max_text_width = gl > min_w ? gl : min_w;
            for (int i = 0; i < 8; i++) {
                maple_device_t* dev = serial_vmu_get_dev(i);
                if (!dev) {
                    continue;
                }
                int port = i / 2;
                int socket = (i % 2 == 0) ? 1 : 2;
                const char* type = get_vmu_type_name(dev);
                int len = snprintf(line_buf, sizeof(line_buf), "Port %c - Socket %d: %s <", 'A' + port, socket, type);
                if (len > max_text_width) {
                    max_text_width = len;
                }
            }
            break;
        }
        case SERIAL_VMU_FIRST_TIME: {
            content_lines = 10;
            int gl = (int)strlen(svmu_ctx.game_line);
            int min_w = 40; /* "Launch without Serial VMU restore/backup" */
            max_text_width = gl > min_w ? gl : min_w;
            break;
        }
        case SERIAL_VMU_RESTORE_BUSY: {
            content_lines = 5;
            int gl = (int)strlen(svmu_ctx.game_line);
            max_text_width = gl > 24 ? gl : 24; /* vs "Block 256 / 256 (128 KB)" */
            break;
        }
        case SERIAL_VMU_BACKUP_BUSY: {
            content_lines = 5;
            int gl = (int)strlen(svmu_ctx.game_line);
            max_text_width = gl > 24 ? gl : 24; /* vs "Backing up Serial VMU..." */
            break;
        }
        case SERIAL_VMU_WIPE_BUSY: {
            content_lines = 5;
            int gl = (int)strlen(svmu_ctx.game_line);
            max_text_width = gl > 17 ? gl : 17; /* vs "Formatting VMU..." */
            break;
        }
        case SERIAL_VMU_RESTORE_FAILED: {
            content_lines = 8;
            int gl = (int)strlen(svmu_ctx.game_line);
            int fl;
            if (svmu_ctx.error_block >= 0) {
                snprintf(line_buf, sizeof(line_buf), "Failed to restore save at block %d.", svmu_ctx.error_block);
                fl = (int)strlen(line_buf);
            } else {
                fl = 23; /* "Failed to restore save." */
            }
            int min_w = 41; /* "Launch with VMU as is (back up on return)" */
            max_text_width = gl > min_w ? gl : min_w;
            if (fl > max_text_width) {
                max_text_width = fl;
            }
            break;
        }
        case SERIAL_VMU_BACKUP_FAILED: {
            content_lines = 7;
            int gl = (int)strlen(svmu_ctx.game_line);
            max_text_width = gl > 37 ? gl : 37; /* vs "Skip for now (ask again on next boot)" */
            break;
        }
        case SERIAL_VMU_CORRUPT_FILE: {
            content_lines = 11;
            int gl = (int)strlen(svmu_ctx.game_line);
            snprintf(line_buf, sizeof(line_buf), "Expected %d bytes, found %d.", SERIAL_VMU_TOTAL_SIZE,
                     svmu_ctx.actual_file_size);
            int el = (int)strlen(line_buf);
            int min_w = 33; /* "A new Serial VMU will be created." */
            max_text_width = gl > min_w ? gl : min_w;
            if (el > max_text_width) {
                max_text_width = el;
            }
            break;
        }
        case SERIAL_VMU_WIPE_CONFIRM: {
            content_lines = 8;
            int gl = (int)strlen(svmu_ctx.game_line);
            int min_w = 34; /* "All data on this VMU will be lost." */
            max_text_width = gl > min_w ? gl : min_w;
            break;
        }
        case SERIAL_VMU_SLOT_SELECT: {
            /* game_line + blank + header + blank + 5 slots + label lines + blank + option1 + option2 */
            content_lines = 4 + SERIAL_VMU_NUM_SLOTS + 3;
            for (int i = 0; i < SERIAL_VMU_NUM_SLOTS; i++) {
                if (svmu_ctx.slot_labels[i][0]) {
                    content_lines++;
                }
            }
            int gl = (int)strlen(svmu_ctx.game_line);
            int min_w = 40; /* "Launch without Serial VMU restore/backup" */
            max_text_width = gl > min_w ? gl : min_w;
            break;
        }
        default:
            /* Unknown state, zero out cache and stop recalculating */
            svmu_cached_bmp_width = 0;
            svmu_cached_bmf_width = 0;
            svmu_cached_content_lines = 0;
            svmu_layout_state = svmu_ctx.state;
            svmu_layout_dirty = false;
            return;
    }

    int bmp_width = (max_text_width + 2) * 8;
    if (bmp_width > 600) {
        bmp_width = 600;
    }

    int bmf_width = max_text_width * 10 + 16; /* 16 = BMF padding */
    if (bmf_width > 520) {
        bmf_width = 520;
    }
    if (bmf_width < 280) {
        bmf_width = 280;
    }

    svmu_cached_bmp_width = bmp_width;
    svmu_cached_bmf_width = bmf_width;
    svmu_cached_content_lines = content_lines;
    svmu_layout_state = svmu_ctx.state;
    svmu_layout_dirty = false;
}

void
draw_serial_vmu_tr(void) {
    z_set_cond(205.0f);

    /* Poll for device changes while on VMU selector */
    if (svmu_ctx.state == SERIAL_VMU_NO_VMU) {
        serial_vmu_live_update_selector();
    }

    /* Recalculate layout only when state or devices change */
    if (svmu_layout_dirty || svmu_ctx.state != svmu_layout_state) {
        svmu_recalc_layout();
    }

    if (svmu_cached_bmp_width == 0) {
        return; /* Unknown state */
    }

    if (sf_ui[0] == UI_SCROLL || sf_ui[0] == UI_FOLDERS) {
        const int line_height = 24;
        const int margin = 8;
        char line_buf[80];

        int width = svmu_cached_bmp_width;
        int height = (svmu_cached_content_lines + 1) * line_height + 4;
        int x = (640 / 2) - (width / 2);
        int y = (480 / 2) - (height / 2);
        int x_item = x + margin;

        draw_popup_menu_ex(x, y, width, height, sf_ui[0]);

        int cur_y = y + 2;
        font_bmp_begin_draw();
        font_bmp_set_color(menu_title_color);
        font_bmp_draw_main(x + width / 2 - (10 * 8 / 2), cur_y, "Serial VMU");
        cur_y += 2; /* title gap, match exit/settings menus */

        switch (svmu_ctx.state) {
            case SERIAL_VMU_NO_SD:
                cur_y += line_height;
                font_bmp_set_color(text_color);
                font_bmp_draw_main(x_item, cur_y, "Serial SD adapter not detected.");
                cur_y += line_height; /* blank */
                cur_y += line_height;
                font_bmp_draw_main(x_item, cur_y, "Per-game Serial VMUs are not available.");
                cur_y += line_height; /* blank */
                /* Options */
                cur_y += line_height;
                font_bmp_set_color(svmu_ctx.menu_cursor == 0 ? highlight_color : text_color);
                font_bmp_draw_main(x_item, cur_y, "Retry detection");
                cur_y += line_height;
                font_bmp_set_color(svmu_ctx.menu_cursor == 1 ? highlight_color : text_color);
                font_bmp_draw_main(x_item, cur_y,
                                   svmu_ctx.launch_action == SERIAL_VMU_LAUNCH_EXIT_BIOS ? "Exit without Serial VMU"
                                                                                         : "Launch without Serial VMU");
                cur_y += line_height;
                font_bmp_set_color(svmu_ctx.menu_cursor == 2 ? highlight_color : text_color);
                font_bmp_draw_main(x_item, cur_y, "Cancel");
                break;

            case SERIAL_VMU_NO_SD_BACKUP:
                cur_y += line_height;
                font_bmp_set_color(text_color);
                font_bmp_draw_main(x_item, cur_y, svmu_ctx.game_line);
                cur_y += line_height; /* blank */
                cur_y += line_height;
                font_bmp_draw_main(x_item, cur_y, "Backup pending, but no serial SD card found.");
                cur_y += line_height; /* blank */
                cur_y += line_height;
                font_bmp_set_color(svmu_ctx.menu_cursor == 0 ? highlight_color : text_color);
                font_bmp_draw_main(x_item, cur_y, "Retry detection");
                cur_y += line_height;
                font_bmp_set_color(svmu_ctx.menu_cursor == 1 ? highlight_color : text_color);
                font_bmp_draw_main(x_item, cur_y, "Skip for now (ask again on next boot)");
                cur_y += line_height;
                font_bmp_set_color(svmu_ctx.menu_cursor == 2 ? highlight_color : text_color);
                font_bmp_draw_main(x_item, cur_y, "Skip entirely");
                break;

            case SERIAL_VMU_NO_VMU: {
                int detected = serial_vmu_detected_count();
                int use_sel_idx = detected;
                int cancel_idx = detected + 1;
                bool cursor_on_dev = (svmu_ctx.selector_cursor < detected);
                cur_y += line_height;
                font_bmp_set_color(text_color);
                font_bmp_draw_main(x_item, cur_y, svmu_ctx.game_line);
                cur_y += line_height; /* blank */
                cur_y += line_height;
                snprintf(line_buf, sizeof(line_buf), "No VMU in Port %s Socket %d.",
                         serial_vmu_port_name(svmu_ctx.vmu_device_id), serial_vmu_socket_num(svmu_ctx.vmu_device_id));
                font_bmp_draw_main(x_item, cur_y, line_buf);
                cur_y += line_height; /* blank */
                cur_y += line_height;
                font_bmp_draw_main(x_item, cur_y,
                                   svmu_ctx.is_backup ? "Select a VMU for this backup."
                                                      : "Select a VMU for this restore.");
                cur_y += line_height; /* blank */
                /* Port/socket lines */
                int dev_cursor = 0;
                for (int p = 0; p < 4; p++) {
                    /* Socket 1 */
                    cur_y += line_height;
                    int slot_idx = p * 2;
                    maple_device_t* dev = serial_vmu_get_dev(slot_idx);
                    if (dev) {
                        bool is_cursor = (svmu_ctx.selector_cursor == dev_cursor);
                        bool is_selected = (!cursor_on_dev && svmu_ctx.selected_device == slot_idx);
                        const char* type = get_vmu_type_name(dev);
                        font_bmp_set_color(is_cursor ? highlight_color : text_color);
                        snprintf(line_buf, sizeof(line_buf), "Port %c - Socket 1: %s%s", 'A' + p, type,
                                 is_selected ? " <" : "");
                        font_bmp_draw_main(x_item, cur_y, line_buf);
                        dev_cursor++;
                    } else {
                        font_bmp_set_color(text_color);
                        snprintf(line_buf, sizeof(line_buf), "Port %c - Socket 1: None", 'A' + p);
                        font_bmp_draw_main(x_item, cur_y, line_buf);
                    }
                    /* Socket 2 */
                    cur_y += line_height;
                    int slot_idx2 = p * 2 + 1;
                    maple_device_t* dev2 = serial_vmu_get_dev(slot_idx2);
                    if (dev2) {
                        bool is_cursor = (svmu_ctx.selector_cursor == dev_cursor);
                        bool is_selected = (!cursor_on_dev && svmu_ctx.selected_device == slot_idx2);
                        const char* type = get_vmu_type_name(dev2);
                        font_bmp_set_color(is_cursor ? highlight_color : text_color);
                        snprintf(line_buf, sizeof(line_buf), "         Socket 2: %s%s", type, is_selected ? " <" : "");
                        font_bmp_draw_main(x_item, cur_y, line_buf);
                        dev_cursor++;
                    } else {
                        font_bmp_set_color(text_color);
                        font_bmp_draw_main(x_item, cur_y, "         Socket 2: None");
                    }
                }
                cur_y += line_height; /* blank */
                cur_y += line_height;
                /* "Use selected", only highlighted when cursor is here AND a device is selected */
                bool use_sel_active = (svmu_ctx.selector_cursor == use_sel_idx && svmu_ctx.selected_device >= 0);
                font_bmp_set_color(use_sel_active ? highlight_color : text_color);
                font_bmp_draw_main(x_item, cur_y, "Use selected");
                cur_y += line_height;
                int skip1_idx = cancel_idx;
                int skip2_idx = cancel_idx + 1;
                font_bmp_set_color(svmu_ctx.selector_cursor == skip1_idx ? highlight_color : text_color);
                font_bmp_draw_main(
                    x_item, cur_y,
                    svmu_ctx.is_backup
                        ? "Skip for now (ask again on next boot)"
                        : (svmu_ctx.launch_action == SERIAL_VMU_LAUNCH_EXIT_BIOS ? "Cancel exit" : "Cancel launch"));
                cur_y += line_height;
                font_bmp_set_color(svmu_ctx.selector_cursor == skip2_idx ? highlight_color : text_color);
                font_bmp_draw_main(x_item, cur_y,
                                   svmu_ctx.is_backup ? "Skip entirely"
                                                      : (svmu_ctx.launch_action == SERIAL_VMU_LAUNCH_EXIT_BIOS
                                                             ? "Exit without Serial VMU restore/backup"
                                                             : "Launch without Serial VMU restore/backup"));
                break;
            }

            case SERIAL_VMU_FIRST_TIME:
                cur_y += line_height;
                font_bmp_set_color(text_color);
                font_bmp_draw_main(x_item, cur_y, svmu_ctx.game_line);
                cur_y += line_height; /* blank */
                cur_y += line_height;
                font_bmp_draw_main(x_item, cur_y, "No existing Serial VMU found.");
                cur_y += line_height; /* blank */
                cur_y += line_height;
                font_bmp_draw_main(x_item, cur_y, "Choose how to initialize connected VMU.");
                cur_y += line_height; /* blank */
                cur_y += line_height;
                font_bmp_set_color(svmu_ctx.menu_cursor == 0 ? highlight_color : text_color);
                font_bmp_draw_main(x_item, cur_y, "Start fresh and format VMU");
                cur_y += line_height;
                font_bmp_set_color(svmu_ctx.menu_cursor == 1 ? highlight_color : text_color);
                font_bmp_draw_main(x_item, cur_y,
                                   svmu_ctx.launch_action == SERIAL_VMU_LAUNCH_EXIT_BIOS ? "Exit to BIOS with VMU as is"
                                                                                         : "Launch with VMU as is");
                cur_y += line_height;
                font_bmp_set_color(svmu_ctx.menu_cursor == 2 ? highlight_color : text_color);
                font_bmp_draw_main(x_item, cur_y,
                                   svmu_ctx.launch_action == SERIAL_VMU_LAUNCH_EXIT_BIOS
                                       ? "Exit without Serial VMU restore/backup"
                                       : "Launch without Serial VMU restore/backup");
                cur_y += line_height;
                font_bmp_set_color(svmu_ctx.menu_cursor == 3 ? highlight_color : text_color);
                font_bmp_draw_main(x_item, cur_y, "Cancel");
                break;

            case SERIAL_VMU_RESTORE_BUSY:
                cur_y += line_height;
                font_bmp_set_color(text_color);
                font_bmp_draw_main(x_item, cur_y, svmu_ctx.game_line);
                cur_y += line_height; /* blank */
                cur_y += line_height;
                font_bmp_draw_main(x_item, cur_y, "Restoring Serial VMU...");
                cur_y += line_height; /* blank */
                cur_y += line_height;
                snprintf(line_buf, sizeof(line_buf), "Block %d / %d (%d KB)", svmu_ctx.current_block, SERIAL_VMU_BLOCKS,
                         svmu_ctx.current_block * SERIAL_VMU_BLOCK_SIZE / 1024);
                font_bmp_draw_main(x_item, cur_y, line_buf);
                break;

            case SERIAL_VMU_BACKUP_BUSY:
                cur_y += line_height;
                font_bmp_set_color(text_color);
                font_bmp_draw_main(x_item, cur_y, svmu_ctx.game_line);
                cur_y += line_height; /* blank */
                cur_y += line_height;
                font_bmp_draw_main(x_item, cur_y, "Backing up Serial VMU...");
                cur_y += line_height; /* blank */
                cur_y += line_height;
                snprintf(line_buf, sizeof(line_buf), "Block %d / %d (%d KB)", svmu_ctx.current_block, SERIAL_VMU_BLOCKS,
                         svmu_ctx.current_block * SERIAL_VMU_BLOCK_SIZE / 1024);
                font_bmp_draw_main(x_item, cur_y, line_buf);
                break;

            case SERIAL_VMU_WIPE_BUSY:
                cur_y += line_height;
                font_bmp_set_color(text_color);
                font_bmp_draw_main(x_item, cur_y, svmu_ctx.game_line);
                cur_y += line_height; /* blank */
                cur_y += line_height;
                font_bmp_draw_main(x_item, cur_y, "Formatting VMU...");
                cur_y += line_height; /* blank */
                cur_y += line_height;
                snprintf(line_buf, sizeof(line_buf), "Block %d / %d", svmu_ctx.current_block, SERIAL_VMU_BLOCKS);
                font_bmp_draw_main(x_item, cur_y, line_buf);
                break;

            case SERIAL_VMU_RESTORE_FAILED:
                cur_y += line_height;
                font_bmp_set_color(text_color);
                font_bmp_draw_main(x_item, cur_y, svmu_ctx.game_line);
                cur_y += line_height; /* blank */
                cur_y += line_height;
                if (svmu_ctx.error_block >= 0) {
                    snprintf(line_buf, sizeof(line_buf), "Failed to restore save at block %d.", svmu_ctx.error_block);
                } else {
                    snprintf(line_buf, sizeof(line_buf), "Failed to restore save.");
                }
                font_bmp_draw_main(x_item, cur_y, line_buf);
                cur_y += line_height; /* blank */
                cur_y += line_height;
                font_bmp_set_color(svmu_ctx.menu_cursor == 0 ? highlight_color : text_color);
                font_bmp_draw_main(x_item, cur_y, "Retry");
                cur_y += line_height;
                font_bmp_set_color(svmu_ctx.menu_cursor == 1 ? highlight_color : text_color);
                font_bmp_draw_main(x_item, cur_y,
                                   svmu_ctx.launch_action == SERIAL_VMU_LAUNCH_EXIT_BIOS
                                       ? "Exit with VMU as is (back up on return)"
                                       : "Launch with VMU as is (back up on return)");
                cur_y += line_height;
                font_bmp_set_color(svmu_ctx.menu_cursor == 2 ? highlight_color : text_color);
                font_bmp_draw_main(x_item, cur_y,
                                   svmu_ctx.launch_action == SERIAL_VMU_LAUNCH_EXIT_BIOS
                                       ? "Exit without Serial VMU restore/backup"
                                       : "Launch without Serial VMU restore/backup");
                cur_y += line_height;
                font_bmp_set_color(svmu_ctx.menu_cursor == 3 ? highlight_color : text_color);
                font_bmp_draw_main(x_item, cur_y, "Cancel");
                break;

            case SERIAL_VMU_BACKUP_FAILED:
                cur_y += line_height;
                font_bmp_set_color(text_color);
                font_bmp_draw_main(x_item, cur_y, svmu_ctx.game_line);
                cur_y += line_height; /* blank */
                cur_y += line_height;
                if (svmu_ctx.error_block >= 0) {
                    snprintf(line_buf, sizeof(line_buf), "Failed to backup save at block %d.", svmu_ctx.error_block);
                } else {
                    snprintf(line_buf, sizeof(line_buf), "Failed to backup save.");
                }
                font_bmp_draw_main(x_item, cur_y, line_buf);
                cur_y += line_height; /* blank */
                cur_y += line_height;
                font_bmp_set_color(svmu_ctx.menu_cursor == 0 ? highlight_color : text_color);
                font_bmp_draw_main(x_item, cur_y, "Retry");
                cur_y += line_height;
                font_bmp_set_color(svmu_ctx.menu_cursor == 1 ? highlight_color : text_color);
                font_bmp_draw_main(x_item, cur_y, "Skip for now (ask again on next boot)");
                cur_y += line_height;
                font_bmp_set_color(svmu_ctx.menu_cursor == 2 ? highlight_color : text_color);
                font_bmp_draw_main(x_item, cur_y, "Skip entirely");
                break;

            case SERIAL_VMU_CORRUPT_FILE:
                cur_y += line_height;
                font_bmp_set_color(text_color);
                font_bmp_draw_main(x_item, cur_y, svmu_ctx.game_line);
                cur_y += line_height; /* blank */
                cur_y += line_height;
                font_bmp_draw_main(x_item, cur_y, "Serial VMU is corrupted.");
                cur_y += line_height; /* blank */
                cur_y += line_height;
                snprintf(line_buf, sizeof(line_buf), "Expected %d bytes, found %d.", SERIAL_VMU_TOTAL_SIZE,
                         svmu_ctx.actual_file_size);
                font_bmp_draw_main(x_item, cur_y, line_buf);
                cur_y += line_height; /* blank */
                cur_y += line_height;
                font_bmp_draw_main(x_item, cur_y, "A new Serial VMU will be created.");
                cur_y += line_height; /* blank */
                cur_y += line_height;
                font_bmp_set_color(svmu_ctx.menu_cursor == 0 ? highlight_color : text_color);
                font_bmp_draw_main(x_item, cur_y, "Start fresh and format VMU");
                cur_y += line_height;
                font_bmp_set_color(svmu_ctx.menu_cursor == 1 ? highlight_color : text_color);
                font_bmp_draw_main(x_item, cur_y,
                                   svmu_ctx.launch_action == SERIAL_VMU_LAUNCH_EXIT_BIOS ? "Exit to BIOS with VMU as is"
                                                                                         : "Launch with VMU as is");
                cur_y += line_height;
                font_bmp_set_color(svmu_ctx.menu_cursor == 2 ? highlight_color : text_color);
                font_bmp_draw_main(x_item, cur_y, "Cancel");
                break;

            case SERIAL_VMU_WIPE_CONFIRM:
                cur_y += line_height;
                font_bmp_set_color(text_color);
                font_bmp_draw_main(x_item, cur_y, svmu_ctx.game_line);
                cur_y += line_height; /* blank */
                cur_y += line_height;
                snprintf(line_buf, sizeof(line_buf), "Format VMU in Port %s Socket %d?",
                         serial_vmu_port_name(svmu_ctx.vmu_device_id), serial_vmu_socket_num(svmu_ctx.vmu_device_id));
                font_bmp_draw_main(x_item, cur_y, line_buf);
                cur_y += line_height; /* blank */
                cur_y += line_height;
                font_bmp_draw_main(x_item, cur_y, "All data on this VMU will be lost.");
                cur_y += line_height; /* blank */
                cur_y += line_height;
                font_bmp_set_color(svmu_ctx.menu_cursor == 0 ? highlight_color : text_color);
                font_bmp_draw_main(x_item, cur_y, "Yes");
                cur_y += line_height;
                font_bmp_set_color(svmu_ctx.menu_cursor == 1 ? highlight_color : text_color);
                font_bmp_draw_main(x_item, cur_y, "No");
                break;

            case SERIAL_VMU_SLOT_SELECT:
                cur_y += line_height;
                font_bmp_set_color(text_color);
                font_bmp_draw_main(x_item, cur_y, svmu_ctx.game_line);
                cur_y += line_height; /* blank */
                cur_y += line_height;
                font_bmp_draw_main(x_item, cur_y,
                                   svmu_ctx.is_backup
                                       ? "Select a Serial VMU slot for backup."
                                       : (svmu_ctx.all_slots_empty ? "Select a Serial VMU slot to start with."
                                                                   : "Select a Serial VMU slot to restore."));
                cur_y += line_height; /* blank */
                for (int i = 0; i < SERIAL_VMU_NUM_SLOTS; i++) {
                    cur_y += line_height;
                    snprintf(line_buf, sizeof(line_buf), "Slot %d (%s)", i + 1, svmu_ctx.slot_timestamps[i]);
                    font_bmp_set_color(svmu_ctx.slot_cursor == i ? highlight_color : text_color);
                    font_bmp_draw_main(x_item, cur_y, line_buf);
                    if (svmu_ctx.slot_labels[i][0]) {
                        cur_y += line_height;
                        font_bmp_draw_main(x_item, cur_y, svmu_ctx.slot_labels[i]);
                    }
                }
                cur_y += line_height; /* blank */
                cur_y += line_height;
                font_bmp_set_color(svmu_ctx.slot_cursor == SERIAL_VMU_NUM_SLOTS ? highlight_color : text_color);
                font_bmp_draw_main(x_item, cur_y,
                                   svmu_ctx.is_backup ? "Skip for now (ask again on next boot)"
                                                      : (svmu_ctx.launch_action == SERIAL_VMU_LAUNCH_EXIT_BIOS
                                                             ? "Exit without Serial VMU restore/backup"
                                                             : "Launch without Serial VMU restore/backup"));
                cur_y += line_height;
                font_bmp_set_color(svmu_ctx.slot_cursor == SERIAL_VMU_NUM_SLOTS + 1 ? highlight_color : text_color);
                font_bmp_draw_main(
                    x_item, cur_y,
                    svmu_ctx.is_backup
                        ? "Skip entirely"
                        : (svmu_ctx.launch_action == SERIAL_VMU_LAUNCH_EXIT_BIOS ? "Cancel exit" : "Cancel launch"));
                break;

            default: break;
        }

    } else {
        /* BMF font path (Grid/LineDesc), match Save/Load window scaling */
        const int line_height = 26;
        const int padding = 16;
        char line_buf[80];

        int width = svmu_cached_bmf_width;
        int height = (svmu_cached_content_lines + 2) * line_height;
        int x = (640 / 2) - (width / 2);
        int y = (480 / 2) - (height / 2);
        int x_item = x + (padding / 2);

        draw_popup_menu_ex(x, y, width, height, sf_ui[0]);

        int cur_y = y + 2;
        font_bmf_begin_draw();
        font_bmf_set_height(24.0f);
        font_bmf_draw(x_item, cur_y, menu_title_color, "Serial VMU");
        cur_y += line_height / 4; /* title gap, match Save/Load */
        font_bmf_set_height_default();

        switch (svmu_ctx.state) {
            case SERIAL_VMU_NO_SD:
                cur_y += line_height;
                font_bmf_draw_auto_size(x_item, cur_y, text_color, "Serial SD adapter not detected.", width - padding);
                cur_y += line_height;
                cur_y += line_height;
                font_bmf_draw_auto_size(x_item, cur_y, text_color, "Per-game Serial VMUs are not available.",
                                        width - padding);
                cur_y += line_height;
                cur_y += line_height;
                font_bmf_draw_auto_size(x_item, cur_y, svmu_ctx.menu_cursor == 0 ? highlight_color : text_color,
                                        "Retry detection", width - padding);
                cur_y += line_height;
                font_bmf_draw_auto_size(x_item, cur_y, svmu_ctx.menu_cursor == 1 ? highlight_color : text_color,
                                        svmu_ctx.launch_action == SERIAL_VMU_LAUNCH_EXIT_BIOS
                                            ? "Exit without Serial VMU"
                                            : "Launch without Serial VMU",
                                        width - padding);
                cur_y += line_height;
                font_bmf_draw_auto_size(x_item, cur_y, svmu_ctx.menu_cursor == 2 ? highlight_color : text_color,
                                        "Cancel", width - padding);
                break;

            case SERIAL_VMU_NO_SD_BACKUP:
                cur_y += line_height;
                font_bmf_draw_auto_size(x_item, cur_y, text_color, svmu_ctx.game_line, width - padding);
                cur_y += line_height;
                cur_y += line_height;
                font_bmf_draw_auto_size(x_item, cur_y, text_color, "Backup pending, but no serial SD card found.",
                                        width - padding);
                cur_y += line_height;
                cur_y += line_height;
                font_bmf_draw_auto_size(x_item, cur_y, svmu_ctx.menu_cursor == 0 ? highlight_color : text_color,
                                        "Retry detection", width - padding);
                cur_y += line_height;
                font_bmf_draw_auto_size(x_item, cur_y, svmu_ctx.menu_cursor == 1 ? highlight_color : text_color,
                                        "Skip for now (ask again on next boot)", width - padding);
                cur_y += line_height;
                font_bmf_draw_auto_size(x_item, cur_y, svmu_ctx.menu_cursor == 2 ? highlight_color : text_color,
                                        "Skip entirely", width - padding);
                break;

            case SERIAL_VMU_NO_VMU: {
                int detected = serial_vmu_detected_count();
                int use_sel_idx = detected;
                int cancel_idx = detected + 1;
                bool cursor_on_dev = (svmu_ctx.selector_cursor < detected);
                cur_y += line_height;
                font_bmf_draw_auto_size(x_item, cur_y, text_color, svmu_ctx.game_line, width - padding);
                cur_y += line_height; /* blank */
                cur_y += line_height;
                snprintf(line_buf, sizeof(line_buf), "No VMU in Port %s Socket %d.",
                         serial_vmu_port_name(svmu_ctx.vmu_device_id), serial_vmu_socket_num(svmu_ctx.vmu_device_id));
                font_bmf_draw_auto_size(x_item, cur_y, text_color, line_buf, width - padding);
                cur_y += line_height; /* blank */
                cur_y += line_height;
                font_bmf_draw_auto_size(x_item, cur_y, text_color,
                                        svmu_ctx.is_backup ? "Select a VMU for this backup."
                                                           : "Select a VMU for this restore.",
                                        width - padding);
                cur_y += line_height; /* blank */
                /* Port/socket lines */
                int dev_cursor = 0;
                for (int p = 0; p < 4; p++) {
                    /* Socket 1 */
                    cur_y += line_height;
                    int slot_idx = p * 2;
                    maple_device_t* dev = serial_vmu_get_dev(slot_idx);
                    if (dev) {
                        bool is_cursor = (svmu_ctx.selector_cursor == dev_cursor);
                        bool is_selected = (!cursor_on_dev && svmu_ctx.selected_device == slot_idx);
                        const char* type = get_vmu_type_name(dev);
                        uint32_t c = is_cursor ? highlight_color : text_color;
                        snprintf(line_buf, sizeof(line_buf), "Port %c - Socket 1: %s%s", 'A' + p, type,
                                 is_selected ? " <" : "");
                        font_bmf_draw_auto_size(x_item, cur_y, c, line_buf, width - padding);
                        dev_cursor++;
                    } else {
                        snprintf(line_buf, sizeof(line_buf), "Port %c - Socket 1: None", 'A' + p);
                        font_bmf_draw_auto_size(x_item, cur_y, text_color, line_buf, width - padding);
                    }
                    /* Socket 2, pixel offset to align under Socket 1 */
                    cur_y += line_height;
                    int slot_idx2 = p * 2 + 1;
                    maple_device_t* dev2 = serial_vmu_get_dev(slot_idx2);
                    if (dev2) {
                        bool is_cursor = (svmu_ctx.selector_cursor == dev_cursor);
                        bool is_selected = (!cursor_on_dev && svmu_ctx.selected_device == slot_idx2);
                        const char* type = get_vmu_type_name(dev2);
                        uint32_t c = is_cursor ? highlight_color : text_color;
                        snprintf(line_buf, sizeof(line_buf), "Socket 2: %s%s", type, is_selected ? " <" : "");
                        font_bmf_draw_auto_size(x_item + 72, cur_y, c, line_buf, width - padding - 72);
                        dev_cursor++;
                    } else {
                        font_bmf_draw_auto_size(x_item + 72, cur_y, text_color, "Socket 2: None", width - padding - 72);
                    }
                }
                cur_y += line_height; /* blank */
                cur_y += line_height;
                /* "Use selected", only highlighted when cursor is here AND a device is selected */
                bool use_sel_active = (svmu_ctx.selector_cursor == use_sel_idx && svmu_ctx.selected_device >= 0);
                font_bmf_draw_auto_size(x_item, cur_y, use_sel_active ? highlight_color : text_color, "Use selected",
                                        width - padding);
                cur_y += line_height;
                int skip1_idx = cancel_idx;
                int skip2_idx = cancel_idx + 1;
                font_bmf_draw_auto_size(
                    x_item, cur_y, svmu_ctx.selector_cursor == skip1_idx ? highlight_color : text_color,
                    svmu_ctx.is_backup
                        ? "Skip for now (ask again on next boot)"
                        : (svmu_ctx.launch_action == SERIAL_VMU_LAUNCH_EXIT_BIOS ? "Cancel exit" : "Cancel launch"),
                    width - padding);
                cur_y += line_height;
                font_bmf_draw_auto_size(x_item, cur_y,
                                        svmu_ctx.selector_cursor == skip2_idx ? highlight_color : text_color,
                                        svmu_ctx.is_backup ? "Skip entirely"
                                                           : (svmu_ctx.launch_action == SERIAL_VMU_LAUNCH_EXIT_BIOS
                                                                  ? "Exit without Serial VMU restore/backup"
                                                                  : "Launch without Serial VMU restore/backup"),
                                        width - padding);
                break;
            }

            case SERIAL_VMU_FIRST_TIME:
                cur_y += line_height;
                font_bmf_draw_auto_size(x_item, cur_y, text_color, svmu_ctx.game_line, width - padding);
                cur_y += line_height;
                cur_y += line_height;
                font_bmf_draw_auto_size(x_item, cur_y, text_color, "No existing Serial VMU found.", width - padding);
                cur_y += line_height;
                cur_y += line_height;
                font_bmf_draw_auto_size(x_item, cur_y, text_color, "Choose how to initialize connected VMU.",
                                        width - padding);
                cur_y += line_height;
                cur_y += line_height;
                font_bmf_draw_auto_size(x_item, cur_y, svmu_ctx.menu_cursor == 0 ? highlight_color : text_color,
                                        "Start fresh and format VMU", width - padding);
                cur_y += line_height;
                font_bmf_draw_auto_size(x_item, cur_y, svmu_ctx.menu_cursor == 1 ? highlight_color : text_color,
                                        svmu_ctx.launch_action == SERIAL_VMU_LAUNCH_EXIT_BIOS
                                            ? "Exit to BIOS with VMU as is"
                                            : "Launch with VMU as is",
                                        width - padding);
                cur_y += line_height;
                font_bmf_draw_auto_size(x_item, cur_y, svmu_ctx.menu_cursor == 2 ? highlight_color : text_color,
                                        svmu_ctx.launch_action == SERIAL_VMU_LAUNCH_EXIT_BIOS
                                            ? "Exit without Serial VMU restore/backup"
                                            : "Launch without Serial VMU restore/backup",
                                        width - padding);
                cur_y += line_height;
                font_bmf_draw_auto_size(x_item, cur_y, svmu_ctx.menu_cursor == 3 ? highlight_color : text_color,
                                        "Cancel", width - padding);
                break;

            case SERIAL_VMU_RESTORE_BUSY:
                cur_y += line_height;
                font_bmf_draw_auto_size(x_item, cur_y, text_color, svmu_ctx.game_line, width - padding);
                cur_y += line_height;
                cur_y += line_height;
                font_bmf_draw_auto_size(x_item, cur_y, text_color, "Restoring Serial VMU...", width - padding);
                cur_y += line_height;
                cur_y += line_height;
                snprintf(line_buf, sizeof(line_buf), "Block %d / %d (%d KB)", svmu_ctx.current_block, SERIAL_VMU_BLOCKS,
                         svmu_ctx.current_block * SERIAL_VMU_BLOCK_SIZE / 1024);
                font_bmf_draw_auto_size(x_item, cur_y, text_color, line_buf, width - padding);
                break;

            case SERIAL_VMU_BACKUP_BUSY:
                cur_y += line_height;
                font_bmf_draw_auto_size(x_item, cur_y, text_color, svmu_ctx.game_line, width - padding);
                cur_y += line_height;
                cur_y += line_height;
                font_bmf_draw_auto_size(x_item, cur_y, text_color, "Backing up Serial VMU...", width - padding);
                cur_y += line_height;
                cur_y += line_height;
                snprintf(line_buf, sizeof(line_buf), "Block %d / %d (%d KB)", svmu_ctx.current_block, SERIAL_VMU_BLOCKS,
                         svmu_ctx.current_block * SERIAL_VMU_BLOCK_SIZE / 1024);
                font_bmf_draw_auto_size(x_item, cur_y, text_color, line_buf, width - padding);
                break;

            case SERIAL_VMU_WIPE_BUSY:
                cur_y += line_height;
                font_bmf_draw_auto_size(x_item, cur_y, text_color, svmu_ctx.game_line, width - padding);
                cur_y += line_height;
                cur_y += line_height;
                font_bmf_draw_auto_size(x_item, cur_y, text_color, "Formatting VMU...", width - padding);
                cur_y += line_height;
                cur_y += line_height;
                snprintf(line_buf, sizeof(line_buf), "Block %d / %d", svmu_ctx.current_block, SERIAL_VMU_BLOCKS);
                font_bmf_draw_auto_size(x_item, cur_y, text_color, line_buf, width - padding);
                break;

            case SERIAL_VMU_RESTORE_FAILED:
                cur_y += line_height;
                font_bmf_draw_auto_size(x_item, cur_y, text_color, svmu_ctx.game_line, width - padding);
                cur_y += line_height;
                cur_y += line_height;
                if (svmu_ctx.error_block >= 0) {
                    snprintf(line_buf, sizeof(line_buf), "Failed to restore save at block %d.", svmu_ctx.error_block);
                } else {
                    snprintf(line_buf, sizeof(line_buf), "Failed to restore save.");
                }
                font_bmf_draw_auto_size(x_item, cur_y, text_color, line_buf, width - padding);
                cur_y += line_height;
                cur_y += line_height;
                font_bmf_draw_auto_size(x_item, cur_y, svmu_ctx.menu_cursor == 0 ? highlight_color : text_color,
                                        "Retry", width - padding);
                cur_y += line_height;
                font_bmf_draw_auto_size(x_item, cur_y, svmu_ctx.menu_cursor == 1 ? highlight_color : text_color,
                                        svmu_ctx.launch_action == SERIAL_VMU_LAUNCH_EXIT_BIOS
                                            ? "Exit with VMU as is (back up on return)"
                                            : "Launch with VMU as is (back up on return)",
                                        width - padding);
                cur_y += line_height;
                font_bmf_draw_auto_size(x_item, cur_y, svmu_ctx.menu_cursor == 2 ? highlight_color : text_color,
                                        svmu_ctx.launch_action == SERIAL_VMU_LAUNCH_EXIT_BIOS
                                            ? "Exit without Serial VMU restore/backup"
                                            : "Launch without Serial VMU restore/backup",
                                        width - padding);
                cur_y += line_height;
                font_bmf_draw_auto_size(x_item, cur_y, svmu_ctx.menu_cursor == 3 ? highlight_color : text_color,
                                        "Cancel", width - padding);
                break;

            case SERIAL_VMU_BACKUP_FAILED:
                cur_y += line_height;
                font_bmf_draw_auto_size(x_item, cur_y, text_color, svmu_ctx.game_line, width - padding);
                cur_y += line_height;
                cur_y += line_height;
                if (svmu_ctx.error_block >= 0) {
                    snprintf(line_buf, sizeof(line_buf), "Failed to backup save at block %d.", svmu_ctx.error_block);
                } else {
                    snprintf(line_buf, sizeof(line_buf), "Failed to backup save.");
                }
                font_bmf_draw_auto_size(x_item, cur_y, text_color, line_buf, width - padding);
                cur_y += line_height;
                cur_y += line_height;
                font_bmf_draw_auto_size(x_item, cur_y, svmu_ctx.menu_cursor == 0 ? highlight_color : text_color,
                                        "Retry", width - padding);
                cur_y += line_height;
                font_bmf_draw_auto_size(x_item, cur_y, svmu_ctx.menu_cursor == 1 ? highlight_color : text_color,
                                        "Skip for now (ask again on next boot)", width - padding);
                cur_y += line_height;
                font_bmf_draw_auto_size(x_item, cur_y, svmu_ctx.menu_cursor == 2 ? highlight_color : text_color,
                                        "Skip entirely", width - padding);
                break;

            case SERIAL_VMU_CORRUPT_FILE:
                cur_y += line_height;
                font_bmf_draw_auto_size(x_item, cur_y, text_color, svmu_ctx.game_line, width - padding);
                cur_y += line_height;
                cur_y += line_height;
                font_bmf_draw_auto_size(x_item, cur_y, text_color, "Serial VMU is corrupted.", width - padding);
                cur_y += line_height;
                cur_y += line_height;
                snprintf(line_buf, sizeof(line_buf), "Expected %d bytes, found %d.", SERIAL_VMU_TOTAL_SIZE,
                         svmu_ctx.actual_file_size);
                font_bmf_draw_auto_size(x_item, cur_y, text_color, line_buf, width - padding);
                cur_y += line_height;
                cur_y += line_height;
                font_bmf_draw_auto_size(x_item, cur_y, text_color, "A new Serial VMU will be created.",
                                        width - padding);
                cur_y += line_height;
                cur_y += line_height;
                font_bmf_draw_auto_size(x_item, cur_y, svmu_ctx.menu_cursor == 0 ? highlight_color : text_color,
                                        "Start fresh and format VMU", width - padding);
                cur_y += line_height;
                font_bmf_draw_auto_size(x_item, cur_y, svmu_ctx.menu_cursor == 1 ? highlight_color : text_color,
                                        svmu_ctx.launch_action == SERIAL_VMU_LAUNCH_EXIT_BIOS
                                            ? "Exit to BIOS with VMU as is"
                                            : "Launch with VMU as is",
                                        width - padding);
                cur_y += line_height;
                font_bmf_draw_auto_size(x_item, cur_y, svmu_ctx.menu_cursor == 2 ? highlight_color : text_color,
                                        "Cancel", width - padding);
                break;

            case SERIAL_VMU_WIPE_CONFIRM:
                cur_y += line_height;
                font_bmf_draw_auto_size(x_item, cur_y, text_color, svmu_ctx.game_line, width - padding);
                cur_y += line_height; /* blank */
                cur_y += line_height;
                snprintf(line_buf, sizeof(line_buf), "Format VMU in Port %s Socket %d?",
                         serial_vmu_port_name(svmu_ctx.vmu_device_id), serial_vmu_socket_num(svmu_ctx.vmu_device_id));
                font_bmf_draw_auto_size(x_item, cur_y, text_color, line_buf, width - padding);
                cur_y += line_height;
                cur_y += line_height;
                font_bmf_draw_auto_size(x_item, cur_y, text_color, "All data on this VMU will be lost.",
                                        width - padding);
                cur_y += line_height;
                cur_y += line_height;
                font_bmf_draw_auto_size(x_item, cur_y, svmu_ctx.menu_cursor == 0 ? highlight_color : text_color, "Yes",
                                        width - padding);
                cur_y += line_height;
                font_bmf_draw_auto_size(x_item, cur_y, svmu_ctx.menu_cursor == 1 ? highlight_color : text_color, "No",
                                        width - padding);
                break;

            case SERIAL_VMU_SLOT_SELECT:
                cur_y += line_height;
                font_bmf_draw_auto_size(x_item, cur_y, text_color, svmu_ctx.game_line, width - padding);
                cur_y += line_height;
                cur_y += line_height;
                font_bmf_draw_auto_size(x_item, cur_y, text_color,
                                        svmu_ctx.is_backup
                                            ? "Select a Serial VMU slot for backup."
                                            : (svmu_ctx.all_slots_empty ? "Select a Serial VMU slot to start with."
                                                                        : "Select a Serial VMU slot to restore."),
                                        width - padding);
                cur_y += line_height;
                for (int i = 0; i < SERIAL_VMU_NUM_SLOTS; i++) {
                    cur_y += line_height;
                    snprintf(line_buf, sizeof(line_buf), "Slot %d (%s)", i + 1, svmu_ctx.slot_timestamps[i]);
                    font_bmf_draw_auto_size(x_item, cur_y, svmu_ctx.slot_cursor == i ? highlight_color : text_color,
                                            line_buf, width - padding);
                    if (svmu_ctx.slot_labels[i][0]) {
                        cur_y += line_height;
                        font_bmf_draw_auto_size(x_item, cur_y, svmu_ctx.slot_cursor == i ? highlight_color : text_color,
                                                svmu_ctx.slot_labels[i], width - padding);
                    }
                }
                cur_y += line_height; /* blank */
                cur_y += line_height;
                font_bmf_draw_auto_size(x_item, cur_y,
                                        svmu_ctx.slot_cursor == SERIAL_VMU_NUM_SLOTS ? highlight_color : text_color,
                                        svmu_ctx.is_backup ? "Skip for now (ask again on next boot)"
                                                           : (svmu_ctx.launch_action == SERIAL_VMU_LAUNCH_EXIT_BIOS
                                                                  ? "Exit without Serial VMU restore/backup"
                                                                  : "Launch without Serial VMU restore/backup"),
                                        width - padding);
                cur_y += line_height;
                font_bmf_draw_auto_size(
                    x_item, cur_y, svmu_ctx.slot_cursor == SERIAL_VMU_NUM_SLOTS + 1 ? highlight_color : text_color,
                    svmu_ctx.is_backup
                        ? "Skip entirely"
                        : (svmu_ctx.launch_action == SERIAL_VMU_LAUNCH_EXIT_BIOS ? "Cancel exit" : "Cancel launch"),
                    width - padding);
                break;

            default: break;
        }
    }
}

static void
serial_vmu_menu_prev(void) {
    if (*input_timeout_ptr > 0) {
        return;
    }
    svmu_ctx.menu_cursor--;
    if (svmu_ctx.menu_cursor < 0) {
        svmu_ctx.menu_cursor = svmu_ctx.menu_num_options - 1; /* Wrap to last option */
    }
    *input_timeout_ptr = INPUT_TIMEOUT;
}

static void
serial_vmu_menu_next(void) {
    if (*input_timeout_ptr > 0) {
        return;
    }
    svmu_ctx.menu_cursor++;
    if (svmu_ctx.menu_cursor >= svmu_ctx.menu_num_options) {
        svmu_ctx.menu_cursor = 0; /* Wrap to first option */
    }
    *input_timeout_ptr = INPUT_TIMEOUT;
}

/* Count how many VMU devices are currently detected */
static int
serial_vmu_detected_count(void) {
    int count = 0;
    for (int i = 0; i < 8; i++) {
        if (serial_vmu_get_dev(i)) {
            count++;
        }
    }
    return count;
}

/* Map filtered cursor position to raw device_id (0-7), or -1 if cursor is on buttons */
static int
serial_vmu_cursor_to_device(int cursor) {
    int count = 0;
    for (int i = 0; i < 8; i++) {
        if (serial_vmu_get_dev(i)) {
            if (count == cursor) {
                return i;
            }
            count++;
        }
    }
    return -1;
}

/* Map raw device_id (0-7) to filtered cursor position, or -1 if device not present */
static int
serial_vmu_device_to_cursor(int device_id) {
    int count = 0;
    for (int i = 0; i < 8; i++) {
        if (serial_vmu_get_dev(i)) {
            if (i == device_id) {
                return count;
            }
            count++;
        }
    }
    return -1;
}

/* Find next present device at or after device_id, wrapping around.
 * Returns device_id (0-7) or -1 if no devices present. */
static int
serial_vmu_find_next_device(int device_id) {
    /* Search forward from device_id */
    for (int i = device_id; i < 8; i++) {
        if (serial_vmu_get_dev(i)) {
            return i;
        }
    }
    /* Wrap around from beginning */
    for (int i = 0; i < device_id; i++) {
        if (serial_vmu_get_dev(i)) {
            return i;
        }
    }
    return -1;
}

/* Tracking state for live device updates (reset when entering NO_VMU) */
static int svmu_selector_prev_detected = -1;
static bool svmu_selector_prev_devices[8] = {0};

static void
serial_vmu_reset_selector_tracking(void) {
    svmu_selector_prev_detected = -1;
    memset(svmu_selector_prev_devices, 0, sizeof(svmu_selector_prev_devices));
}

/* Per-frame update for NO_VMU selector: handle device insertion/removal.
 * Tracks which device_id the cursor was on, and adjusts after changes. */
static void
serial_vmu_live_update_selector(void) {

    int new_detected = serial_vmu_detected_count();

    /* First call. Initialize tracking */
    if (svmu_selector_prev_detected < 0) {
        svmu_selector_prev_detected = new_detected;
        for (int i = 0; i < 8; i++) {
            svmu_selector_prev_devices[i] = (serial_vmu_get_dev(i) != NULL);
        }
        return;
    }

    /* Build current presence map */
    bool cur_devices[8];
    for (int i = 0; i < 8; i++) {
        cur_devices[i] = (serial_vmu_get_dev(i) != NULL);
    }

    /* Check for any change */
    bool changed = (new_detected != svmu_selector_prev_detected);
    if (!changed) {
        for (int i = 0; i < 8; i++) {
            if (cur_devices[i] != svmu_selector_prev_devices[i]) {
                changed = true;
                break;
            }
        }
    }

    if (!changed) {
        return;
    }

    svmu_layout_dirty = true;

    /* Determine what the cursor was pointing at before the change */
    int old_cursor = svmu_ctx.selector_cursor;
    int old_device_id = -1;     /* -1 = cursor was on a button, not a device */
    int old_button_offset = -1; /* 0 = Use selected, 1 = skip1, 2 = skip2 */

    if (old_cursor < svmu_selector_prev_detected) {
        /* Cursor was on a device. Find which device_id using old presence map */
        int count = 0;
        for (int i = 0; i < 8; i++) {
            if (svmu_selector_prev_devices[i]) {
                if (count == old_cursor) {
                    old_device_id = i;
                    break;
                }
                count++;
            }
        }
    } else {
        /* Cursor was on a button: offset from first button position */
        old_button_offset = old_cursor - svmu_selector_prev_detected;
    }

    /* Find inserted and removed devices */
    int inserted_id = -1;
    for (int i = 0; i < 8; i++) {
        if (cur_devices[i] && !svmu_selector_prev_devices[i]) {
            inserted_id = i;
        }
    }

    /* Handle cursor adjustment */
    if (old_device_id >= 0) {
        /* Cursor was on a device */
        if (!cur_devices[old_device_id]) {
            /* That device was removed. Find next available */
            int next = serial_vmu_find_next_device(old_device_id);
            if (next >= 0) {
                svmu_ctx.selector_cursor = serial_vmu_device_to_cursor(next);
            } else {
                /* No devices left. Jump to skip1 (Cancel equivalent) */
                svmu_ctx.selector_cursor = new_detected + 1;
            }
        } else {
            /* Device still present. Recalculate filtered index (may have shifted) */
            int new_idx = serial_vmu_device_to_cursor(old_device_id);
            if (new_idx >= 0) {
                svmu_ctx.selector_cursor = new_idx;
            }
            /* If a new device was inserted, jump to it */
            if (inserted_id >= 0) {
                int idx = serial_vmu_device_to_cursor(inserted_id);
                if (idx >= 0) {
                    svmu_ctx.selector_cursor = idx;
                }
            }
        }
    } else if (old_button_offset >= 0) {
        /* Cursor was on a button, keep it on the same button */
        int new_button_pos = new_detected + old_button_offset;
        svmu_ctx.selector_cursor = new_button_pos;

        /* If a device was inserted and cursor was on a button, jump to it */
        if (inserted_id >= 0) {
            int idx = serial_vmu_device_to_cursor(inserted_id);
            if (idx >= 0) {
                svmu_ctx.selector_cursor = idx;
            }
        }
    }

    /* If selected device was removed, clear selection */
    if (svmu_ctx.selected_device >= 0 && !cur_devices[svmu_ctx.selected_device]) {
        svmu_ctx.selected_device = -1;
    }

    /* Don't leave cursor on "Use selected" if no device is selected */
    if (svmu_ctx.selected_device < 0 && svmu_ctx.selector_cursor == new_detected) {
        svmu_ctx.selector_cursor = (new_detected > 0) ? new_detected - 1 : new_detected + 1;
    }

    /* Clamp cursor to valid range */
    int last_idx = new_detected + 2;
    if (svmu_ctx.selector_cursor > last_idx) {
        svmu_ctx.selector_cursor = last_idx;
    }
    if (svmu_ctx.selector_cursor < 0) {
        svmu_ctx.selector_cursor = (new_detected > 0) ? 0 : new_detected + 1;
    }

    /* Update tracking state */
    svmu_selector_prev_detected = new_detected;
    for (int i = 0; i < 8; i++) {
        svmu_selector_prev_devices[i] = cur_devices[i];
    }
}

/* Cursor: 0..detected-1 = devices, detected = Use selected, detected+1 = Cancel */
static void
serial_vmu_selector_prev(void) {
    if (*input_timeout_ptr > 0) {
        return;
    }
    int detected = serial_vmu_detected_count();
    int last_idx = detected + 2;

    /* Clamp if device count changed */
    if (svmu_ctx.selector_cursor > last_idx) {
        svmu_ctx.selector_cursor = last_idx;
    }

    if (svmu_ctx.selector_cursor > 0) {
        int new_cursor = svmu_ctx.selector_cursor - 1;
        /* Skip "Use selected" if no device selected */
        if (svmu_ctx.selected_device < 0 && new_cursor == detected) {
            new_cursor = detected - 1; /* jump to last device */
            if (new_cursor < 0) {
                new_cursor = last_idx; /* no devices: wrap to last option */
            }
        }
        svmu_ctx.selector_cursor = new_cursor;
    } else {
        /* Wrap to bottom */
        svmu_ctx.selector_cursor = last_idx;
    }
    *input_timeout_ptr = INPUT_TIMEOUT;
}

static void
serial_vmu_selector_next(void) {
    if (*input_timeout_ptr > 0) {
        return;
    }
    int detected = serial_vmu_detected_count();
    int last_idx = detected + 2;

    /* Clamp if device count changed */
    if (svmu_ctx.selector_cursor > last_idx) {
        svmu_ctx.selector_cursor = last_idx;
    }

    if (svmu_ctx.selector_cursor < last_idx) {
        int new_cursor = svmu_ctx.selector_cursor + 1;
        /* Skip "Use selected" if no device selected */
        if (svmu_ctx.selected_device < 0 && new_cursor == detected) {
            new_cursor = detected + 1; /* jump past "Use selected" */
        }
        svmu_ctx.selector_cursor = new_cursor;
    } else {
        /* Wrap to top */
        svmu_ctx.selector_cursor = (detected > 0) ? 0 : detected + 1; /* first device, or first option */
    }
    *input_timeout_ptr = INPUT_TIMEOUT;
}

void
handle_input_serial_vmu(enum control input) {
    switch (svmu_ctx.state) {
        case SERIAL_VMU_RESTORE_BUSY:
        case SERIAL_VMU_BACKUP_BUSY:
        case SERIAL_VMU_WIPE_BUSY:
            /* No input during operations */
            break;

        case SERIAL_VMU_NO_SD:
            switch (input) {
                case UP: serial_vmu_menu_prev(); break;
                case DOWN: serial_vmu_menu_next(); break;
                case B: menu_leave(); break;
                case A:
                    if (svmu_ctx.menu_cursor == 0) {
                        /* Retry detection */
                        serial_vmu_begin_restore_flow();
                    } else if (svmu_ctx.menu_cursor == 1) {
                        /* Launch without Serial VMU */
                        if (svmu_ctx.launch_action != SERIAL_VMU_LAUNCH_NONE) {
                            /* Launch directly, no LASTDISC.TXT */
                            switch (svmu_ctx.launch_action) {
                                case SERIAL_VMU_LAUNCH_DC: dreamcast_launch_disc(svmu_ctx.launch_item); break;
                                case SERIAL_VMU_LAUNCH_BLEEM: bleem_launch(svmu_ctx.launch_item); break;
                                case SERIAL_VMU_LAUNCH_BLOOM: bloom_launch(svmu_ctx.launch_item); break;
                                case SERIAL_VMU_LAUNCH_EXIT_BIOS: exit_to_bios_ex(svmu_ctx.exit_mount_disc, 0); break;
                                default: break;
                            }
                        }
                    } else {
                        /* Cancel */
                        menu_leave();
                    }
                    break;
                default: break;
            }
            break;

        case SERIAL_VMU_NO_SD_BACKUP:
            switch (input) {
                case UP: serial_vmu_menu_prev(); break;
                case DOWN: serial_vmu_menu_next(); break;
                case B: menu_leave(); break;
                case A:
                    if (svmu_ctx.menu_cursor == 0) {
                        /* Retry detection */
                        serial_vmu_begin_backup_flow();
                    } else if (svmu_ctx.menu_cursor == 1) {
                        /* Skip for now (ask again on next boot), keep LASTDISC.TXT */
                        menu_leave();
                    } else {
                        /* Skip entirely. Clear LASTDISC.TXT */
                        serial_vmu_clear_lastdisc();
                        menu_leave();
                    }
                    break;
                default: break;
            }
            break;

        case SERIAL_VMU_NO_VMU: {
            int detected = serial_vmu_detected_count();
            int use_sel_idx = detected;
            int skip1_idx = detected + 1;
            int skip2_idx = detected + 2;
            switch (input) {
                case UP: serial_vmu_selector_prev(); break;
                case DOWN: serial_vmu_selector_next(); break;
                case B: menu_leave(); break;
                case A:
                    if (svmu_ctx.selector_cursor < detected) {
                        /* Select this device */
                        int dev_id = serial_vmu_cursor_to_device(svmu_ctx.selector_cursor);
                        if (dev_id >= 0 && serial_vmu_get_dev(dev_id)) {
                            svmu_ctx.selected_device = dev_id;
                            svmu_ctx.selector_cursor = use_sel_idx;
                        }
                    } else if (svmu_ctx.selector_cursor == use_sel_idx) {
                        /* Use selected */
                        if (svmu_ctx.selected_device >= 0) {
                            svmu_ctx.vmu_dev = serial_vmu_get_dev(svmu_ctx.selected_device);
                            if (svmu_ctx.vmu_dev) {
                                svmu_ctx.vmu_device_id = svmu_ctx.selected_device;
                                if (svmu_ctx.is_backup) {
                                    serial_vmu_begin_backup_flow();
                                } else {
                                    serial_vmu_begin_restore_flow();
                                }
                            } else {
                                /* Device gone, reset */
                                svmu_ctx.selected_device = -1;
                                svmu_ctx.selector_cursor = (detected > 0) ? 0 : skip1_idx;
                            }
                        }
                    } else if (svmu_ctx.selector_cursor == skip1_idx) {
                        /* Backup: skip for now / Restore: cancel launch */
                        menu_leave();
                    } else if (svmu_ctx.selector_cursor == skip2_idx) {
                        if (svmu_ctx.is_backup) {
                            /* Skip entirely. Clear LASTDISC.TXT */
                            serial_vmu_clear_lastdisc();
                            menu_leave();
                        } else {
                            /* Launch without Serial VMU restore/backup */
                            switch (svmu_ctx.launch_action) {
                                case SERIAL_VMU_LAUNCH_DC: dreamcast_launch_disc(svmu_ctx.launch_item); break;
                                case SERIAL_VMU_LAUNCH_BLEEM: bleem_launch(svmu_ctx.launch_item); break;
                                case SERIAL_VMU_LAUNCH_BLOOM: bloom_launch(svmu_ctx.launch_item); break;
                                case SERIAL_VMU_LAUNCH_CB: dreamcast_launch_cb(svmu_ctx.launch_item); break;
                                case SERIAL_VMU_LAUNCH_EXIT_BIOS: exit_to_bios_ex(svmu_ctx.exit_mount_disc, 0); break;
                                default: menu_leave(); break;
                            }
                        }
                    }
                    *input_timeout_ptr = INPUT_TIMEOUT;
                    break;
                default: break;
            }
            break;
        }

        case SERIAL_VMU_FIRST_TIME:
            switch (input) {
                case UP: serial_vmu_menu_prev(); break;
                case DOWN: serial_vmu_menu_next(); break;
                case B: menu_leave(); break;
                case A:
                    if (svmu_ctx.menu_cursor == 0) {
                        /* Start fresh and format VMU. Show confirmation */
                        svmu_ctx.state = SERIAL_VMU_WIPE_CONFIRM;
                        svmu_ctx.menu_cursor = 0;
                        svmu_ctx.menu_num_options = 2;
                    } else if (svmu_ctx.menu_cursor == 1) {
                        /* Launch/Exit with VMU as is */
                        serial_vmu_do_launch();
                    } else if (svmu_ctx.menu_cursor == 2) {
                        /* Launch/Exit without Serial VMU restore/backup */
                        switch (svmu_ctx.launch_action) {
                            case SERIAL_VMU_LAUNCH_DC: dreamcast_launch_disc(svmu_ctx.launch_item); break;
                            case SERIAL_VMU_LAUNCH_BLEEM: bleem_launch(svmu_ctx.launch_item); break;
                            case SERIAL_VMU_LAUNCH_BLOOM: bloom_launch(svmu_ctx.launch_item); break;
                            case SERIAL_VMU_LAUNCH_CB: dreamcast_launch_cb(svmu_ctx.launch_item); break;
                            case SERIAL_VMU_LAUNCH_EXIT_BIOS: exit_to_bios_ex(svmu_ctx.exit_mount_disc, 0); break;
                            default: menu_leave(); break;
                        }
                    } else {
                        /* Cancel */
                        menu_leave();
                    }
                    break;
                default: break;
            }
            break;

        case SERIAL_VMU_WIPE_CONFIRM:
            switch (input) {
                case UP: serial_vmu_menu_prev(); break;
                case DOWN: serial_vmu_menu_next(); break;
                case B: {
                    /* Go back to first time or corrupt. Re-check to determine which */
                    save_file_status_t st =
                        serial_vmu_validate_file(svmu_ctx.serial_id, svmu_ctx.slot_number, &svmu_ctx.actual_file_size);
                    bool back_to_corrupt = (st == SAVE_FILE_WRONG_SIZE);
                    svmu_ctx.state = back_to_corrupt ? SERIAL_VMU_CORRUPT_FILE : SERIAL_VMU_FIRST_TIME;
                    svmu_ctx.menu_cursor = 0;
                    svmu_ctx.menu_num_options = back_to_corrupt ? 3 : 4;
                } break;
                case A:
                    if (svmu_ctx.menu_cursor == 0) {
                        /* Yes. Wipe VMU by flashing BIOS-formatted EMPTY.VMU from disc */
                        svmu_ctx.vmu_dev = serial_vmu_get_dev(svmu_ctx.vmu_device_id);
                        if (svmu_ctx.vmu_dev) {
                            /* Load EMPTY.VMU from CD */
                            if (svmu_ctx.buffer) {
                                free(svmu_ctx.buffer);
                                svmu_ctx.buffer = NULL;
                            }
                            file_t fd = fs_open("/cd/EMPTY.VMU", O_RDONLY);
                            if (fd == -1) {
                                /* Can't open file, fall back to previous state */
                                break;
                            }
                            svmu_ctx.buffer = malloc(SERIAL_VMU_TOTAL_SIZE);
                            if (!svmu_ctx.buffer) {
                                fs_close(fd);
                                break;
                            }
                            ssize_t bytes_read = fs_read(fd, svmu_ctx.buffer, SERIAL_VMU_TOTAL_SIZE);
                            fs_close(fd);
                            if (bytes_read != SERIAL_VMU_TOTAL_SIZE) {
                                free(svmu_ctx.buffer);
                                svmu_ctx.buffer = NULL;
                                break;
                            }
                            svmu_ctx.state = SERIAL_VMU_WIPE_BUSY;
                            svmu_ctx.current_block = 0;
                            vmu_draw_lcd_auto(svmu_ctx.vmu_dev, openmenu_lcd_access);
                        }
                    } else {
                        /* No, go back */
                        save_file_status_t st = serial_vmu_validate_file(svmu_ctx.serial_id, svmu_ctx.slot_number,
                                                                         &svmu_ctx.actual_file_size);
                        svmu_ctx.state = (st == SAVE_FILE_WRONG_SIZE) ? SERIAL_VMU_CORRUPT_FILE : SERIAL_VMU_FIRST_TIME;
                        svmu_ctx.menu_cursor = 0;
                        svmu_ctx.menu_num_options = 3;
                    }
                    break;
                default: break;
            }
            break;

        case SERIAL_VMU_RESTORE_FAILED:
            switch (input) {
                case UP: serial_vmu_menu_prev(); break;
                case DOWN: serial_vmu_menu_next(); break;
                case B: menu_leave(); break;
                case A:
                    if (svmu_ctx.menu_cursor == 0) {
                        /* Retry */
                        serial_vmu_begin_restore_flow();
                    } else if (svmu_ctx.menu_cursor == 1) {
                        /* Launch/Exit with VMU as is (back up on return) */
                        serial_vmu_do_launch();
                    } else if (svmu_ctx.menu_cursor == 2) {
                        /* Launch/Exit without Serial VMU restore/backup */
                        switch (svmu_ctx.launch_action) {
                            case SERIAL_VMU_LAUNCH_DC: dreamcast_launch_disc(svmu_ctx.launch_item); break;
                            case SERIAL_VMU_LAUNCH_BLEEM: bleem_launch(svmu_ctx.launch_item); break;
                            case SERIAL_VMU_LAUNCH_BLOOM: bloom_launch(svmu_ctx.launch_item); break;
                            case SERIAL_VMU_LAUNCH_CB: dreamcast_launch_cb(svmu_ctx.launch_item); break;
                            case SERIAL_VMU_LAUNCH_EXIT_BIOS: exit_to_bios_ex(svmu_ctx.exit_mount_disc, 0); break;
                            default: menu_leave(); break;
                        }
                    } else {
                        /* Cancel */
                        menu_leave();
                    }
                    break;
                default: break;
            }
            break;

        case SERIAL_VMU_BACKUP_FAILED:
            switch (input) {
                case UP: serial_vmu_menu_prev(); break;
                case DOWN: serial_vmu_menu_next(); break;
                case B: menu_leave(); break;
                case A:
                    if (svmu_ctx.menu_cursor == 0) {
                        /* Retry */
                        serial_vmu_begin_backup_flow();
                    } else if (svmu_ctx.menu_cursor == 1) {
                        /* Skip backup, keep LASTDISC.TXT */
                        menu_leave();
                    } else {
                        /* Discard backup */
                        serial_vmu_clear_lastdisc();
                        menu_leave();
                    }
                    break;
                default: break;
            }
            break;

        case SERIAL_VMU_CORRUPT_FILE:
            switch (input) {
                case UP: serial_vmu_menu_prev(); break;
                case DOWN: serial_vmu_menu_next(); break;
                case B: menu_leave(); break;
                case A:
                    if (svmu_ctx.menu_cursor == 0) {
                        /* Start fresh and format VMU */
                        svmu_ctx.state = SERIAL_VMU_WIPE_CONFIRM;
                        svmu_ctx.menu_cursor = 0;
                        svmu_ctx.menu_num_options = 2;
                    } else if (svmu_ctx.menu_cursor == 1) {
                        /* Launch with VMU as is */
                        serial_vmu_do_launch();
                    } else {
                        /* Cancel */
                        menu_leave();
                    }
                    break;
                default: break;
            }
            break;

        case SERIAL_VMU_SLOT_SELECT: {
            int slot_max = SERIAL_VMU_NUM_SLOTS + 2; /* 5 slots + 2 options */
            switch (input) {
                case UP:
                    if (*input_timeout_ptr > 0) {
                        break;
                    }
                    svmu_ctx.slot_cursor--;
                    if (svmu_ctx.slot_cursor < 0) {
                        svmu_ctx.slot_cursor = slot_max - 1;
                    }
                    *input_timeout_ptr = INPUT_TIMEOUT;
                    break;
                case DOWN:
                    if (*input_timeout_ptr > 0) {
                        break;
                    }
                    svmu_ctx.slot_cursor++;
                    if (svmu_ctx.slot_cursor >= slot_max) {
                        svmu_ctx.slot_cursor = 0;
                    }
                    *input_timeout_ptr = INPUT_TIMEOUT;
                    break;
                case B: menu_leave(); break;
                case A:
                    if (svmu_ctx.slot_cursor == SERIAL_VMU_NUM_SLOTS) {
                        if (svmu_ctx.is_backup) {
                            /* Skip for now (ask again on next boot) */
                            menu_leave();
                        } else {
                            /* Launch without Serial VMU restore/backup */
                            switch (svmu_ctx.launch_action) {
                                case SERIAL_VMU_LAUNCH_DC: dreamcast_launch_disc(svmu_ctx.launch_item); break;
                                case SERIAL_VMU_LAUNCH_BLEEM: bleem_launch(svmu_ctx.launch_item); break;
                                case SERIAL_VMU_LAUNCH_BLOOM: bloom_launch(svmu_ctx.launch_item); break;
                                case SERIAL_VMU_LAUNCH_CB: dreamcast_launch_cb(svmu_ctx.launch_item); break;
                                case SERIAL_VMU_LAUNCH_EXIT_BIOS: exit_to_bios_ex(svmu_ctx.exit_mount_disc, 0); break;
                                default: menu_leave(); break;
                            }
                        }
                    } else if (svmu_ctx.slot_cursor == SERIAL_VMU_NUM_SLOTS + 1) {
                        if (svmu_ctx.is_backup) {
                            /* Skip entirely. Clear LASTDISC.TXT */
                            serial_vmu_clear_lastdisc();
                            menu_leave();
                        } else {
                            /* Cancel launch */
                            menu_leave();
                        }
                    } else {
                        svmu_ctx.slot_number = svmu_ctx.slot_cursor + 1;
                        serial_vmu_build_slot_file_id(svmu_ctx.slot_file_id, sizeof(svmu_ctx.slot_file_id),
                                                      svmu_ctx.serial_id, svmu_ctx.slot_number);
                        if (svmu_ctx.is_backup) {
                            serial_vmu_begin_backup_flow();
                        } else {
                            serial_vmu_begin_restore_flow();
                        }
                    }
                    *input_timeout_ptr = INPUT_TIMEOUT;
                    break;
                default: break;
            }
            break;
        }

        default: break;
    }
}

#pragma endregion Serial_VMU
