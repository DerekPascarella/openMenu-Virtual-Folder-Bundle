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

#include <string.h>

#include <backend/db_item.h>
#include <backend/gd_item.h>
#include <backend/gd_list.h>
#include <backend/gdemu_sdk.h>
#include <crayon_savefile/savefile.h>
#include <openmenu_savefile.h>
#include <openmenu_settings.h>

#include "ui/draw_kos.h"
#include "ui/draw_prototypes.h"
#include "ui/font_prototypes.h"
#include "ui/ui_common.h"

#include "ui/ui_menu_credits.h"

#pragma region Settings_Menu

static const char* menu_choice_text[] = {"Style", "Theme", "Aspect", "Beep", "3D BIOS", "Sort", "Filter", "Multidisc", "Artwork", "Index", "Artwork", "Marquee Speed"};
static const char* theme_choice_text[] = {"LineDesc", "Grid3", "Scroll", "Folders"};
static const char* region_choice_text[] = {"NTSC-U", "NTSC-J", "PAL"};
static const char* region_choice_text_scroll[] = {"GDMENU"};
static const char* region_choice_text_folders[] = {"FoldersDefault"};
static const char* aspect_choice_text[] = {"4:3", "16:9"};
static const char* beep_choice_text[] = {"Off", "On"}; /* Hidden from UI but kept for array sizing */
static const char* bios_3d_choice_text[] = {"Off", "On"};
static const char* sort_choice_text[] = {"Default", "Name", "Region", "Genre"};
static const char* filter_choice_text[] = {"All",      "Action",   "Racing",   "Simulation", "Sports",     "Lightgun",
                                           "Fighting", "Shooter",  "Survival", "Adventure",  "Platformer", "RPG",
                                           "Shmup",    "Strategy", "Puzzle",   "Arcade",     "Music"};
static const char* multidisc_choice_text[] = {"Show", "Hide"};
static const char* scroll_art_choice_text[] = {"Off", "On"};
static const char* scroll_index_choice_text[] = {"Off", "On"};
static const char* folders_art_choice_text[] = {"Off", "On"};
static const char* marquee_speed_choice_text[] = {"Slow", "Medium", "Fast"};
static const char* save_choice_text[] = {"Save", "Apply"};
static const char* credits_text[] = {"Credits"};

const char* custom_theme_text[10] = {0};
static theme_custom* custom_themes;
static theme_scroll* custom_scroll;
static int num_custom_themes;
int cb_multidisc = 0;
int start_cb = 0;
static const gd_item* cur_game_item = NULL;

#define MENU_OPTIONS  ((int)(sizeof(menu_choice_text) / sizeof(menu_choice_text)[0]))
#define MENU_CHOICES  (MENU_OPTIONS)
#define THEME_CHOICES (sizeof(theme_choice_text) / sizeof(theme_choice_text)[0])
static int REGION_CHOICES = (sizeof(region_choice_text) / sizeof(region_choice_text)[0]);
#define ASPECT_CHOICES     (sizeof(aspect_choice_text) / sizeof(aspect_choice_text)[0])
#define BEEP_CHOICES       (sizeof(beep_choice_text) / sizeof(beep_choice_text)[0]) /* Hidden from UI */
#define BIOS_3D_CHOICES    (sizeof(bios_3d_choice_text) / sizeof(bios_3d_choice_text)[0])
#define SORT_CHOICES       (sizeof(sort_choice_text) / sizeof(sort_choice_text)[0])
#define FILTER_CHOICES     (sizeof(filter_choice_text) / sizeof(filter_choice_text)[0])
#define MULTIDISC_CHOICES  (sizeof(multidisc_choice_text) / sizeof(multidisc_choice_text)[0])
#define SCROLL_ART_CHOICES (sizeof(scroll_art_choice_text) / sizeof(scroll_art_choice_text)[0])
#define SCROLL_INDEX_CHOICES (sizeof(scroll_index_choice_text) / sizeof(scroll_index_choice_text)[0])
#define FOLDERS_ART_CHOICES (sizeof(folders_art_choice_text) / sizeof(folders_art_choice_text)[0])
#define MARQUEE_SPEED_CHOICES (sizeof(marquee_speed_choice_text) / sizeof(marquee_speed_choice_text)[0])

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
    CHOICE_SCROLL_ART,
    CHOICE_SCROLL_INDEX,
    CHOICE_FOLDERS_ART,
    CHOICE_MARQUEE_SPEED,
    CHOICE_SAVE,
    CHOICE_CREDITS,
    CHOICE_END = CHOICE_CREDITS
} MENU_CHOICE;

#define INPUT_TIMEOUT (10)

static int choices[MENU_CHOICES + 1];
static int choices_max[MENU_CHOICES + 1] = {
    THEME_CHOICES,     3, ASPECT_CHOICES, BEEP_CHOICES, BIOS_3D_CHOICES, SORT_CHOICES, FILTER_CHOICES,
    MULTIDISC_CHOICES, SCROLL_ART_CHOICES, SCROLL_INDEX_CHOICES, FOLDERS_ART_CHOICES, MARQUEE_SPEED_CHOICES, 2 /* Apply/Save */};
static const char** menu_choice_array[MENU_CHOICES] = {theme_choice_text,       region_choice_text,   aspect_choice_text,
                                                       beep_choice_text,        bios_3d_choice_text,  sort_choice_text,
                                                       filter_choice_text,      multidisc_choice_text,
                                                       scroll_art_choice_text,  scroll_index_choice_text, folders_art_choice_text,
                                                       marquee_speed_choice_text};
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
    /* Ensure color themeing is consistent */
    text_color = _colors->menu_text_color;
    highlight_color = _colors->menu_highlight_color;
    menu_bkg_color = _colors->menu_bkg_color;
    menu_bkg_border_color = _colors->menu_bkg_border_color;

    /* So we can modify the shared state and input timeout */
    state_ptr = state;
    input_timeout_ptr = timeout_ptr;
    *input_timeout_ptr = (30 * 1) /* half a second */;
}

void
menu_setup(enum draw_state* state, theme_color* _colors, int* timeout_ptr, uint32_t title_color) {
    common_setup(state, _colors, timeout_ptr);
    menu_title_color = title_color;

    choices[CHOICE_THEME] = sf_ui[0];
    choices[CHOICE_REGION] = sf_region[0];
    choices[CHOICE_ASPECT] = sf_aspect[0];
    choices[CHOICE_SORT] = sf_sort[0];
    choices[CHOICE_FILTER] = sf_filter[0];
    choices[CHOICE_BEEP] = sf_beep[0]; /* Hidden from UI */
    choices[CHOICE_BIOS_3D] = sf_bios_3d[0];
    choices[CHOICE_MULTIDISC] = sf_multidisc[0];
    choices[CHOICE_SCROLL_ART] = sf_scroll_art[0];
    choices[CHOICE_SCROLL_INDEX] = sf_scroll_index[0];
    choices[CHOICE_FOLDERS_ART] = sf_folders_art[0];
    choices[CHOICE_MARQUEE_SPEED] = sf_marquee_speed[0];

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
}

static void
menu_leave(void) {
    if (*input_timeout_ptr > 0) {
        return;
    }
    *state_ptr = DRAW_UI;
    *input_timeout_ptr = (30 * 1) /* half a second */;
}

static void
credits_leave(void) {
    if (*input_timeout_ptr > 0) {
        return;
    }
    *state_ptr = DRAW_MENU;
    *input_timeout_ptr = (20);
}

static void
menu_accept(void) {
    if (*input_timeout_ptr > 0) {
        return;
    }
    if (current_choice == CHOICE_SAVE) {
        /* update Global Settings */
        sf_ui[0] = choices[CHOICE_THEME];
        sf_region[0] = choices[CHOICE_REGION];
        sf_aspect[0] = choices[CHOICE_ASPECT];
        sf_sort[0] = choices[CHOICE_SORT];
        sf_filter[0] = choices[CHOICE_FILTER];
        sf_beep[0] = choices[CHOICE_BEEP]; /* Hidden from UI */
        sf_bios_3d[0] = choices[CHOICE_BIOS_3D];
        sf_multidisc[0] = choices[CHOICE_MULTIDISC];
        sf_scroll_art[0] = choices[CHOICE_SCROLL_ART];
        sf_scroll_index[0] = choices[CHOICE_SCROLL_INDEX];
        sf_folders_art[0] = choices[CHOICE_FOLDERS_ART];
        sf_marquee_speed[0] = choices[CHOICE_MARQUEE_SPEED];
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
                default:
                case SORT_DEFAULT: list_set_sort_default(); break;
            }
        } else {
            /* If filtering, filter down to only genre then sort */
            list_set_genre_sort((FLAGS_GENRE)choices[CHOICE_FILTER] - 1, choices[CHOICE_SORT]);
        }

        if (choices[CHOICE_SAVE] == 0 /* Save */) {
            savefile_save();
        }
        extern void reload_ui(void);
        reload_ui();
    }
    if (current_choice == CHOICE_CREDITS) {
        *state_ptr = DRAW_CREDITS;
        *input_timeout_ptr = (20 * 1) /* 1/3 second */;
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
        /* Skip FOLDERS_ART option in non-Folders modes */
        if (current_choice == CHOICE_FOLDERS_ART && sf_ui[0] != UI_FOLDERS) {
            skip = 1;
        }
        /* Skip MARQUEE_SPEED option in non-Scroll/Folders modes */
        if (current_choice == CHOICE_MARQUEE_SPEED && sf_ui[0] != UI_SCROLL && sf_ui[0] != UI_FOLDERS) {
            skip = 1;
        }
        /* Skip Aspect/Sort/Filter in Folders mode */
        if (sf_ui[0] == UI_FOLDERS && (current_choice == CHOICE_ASPECT || current_choice == CHOICE_SORT || current_choice == CHOICE_FILTER)) {
            skip = 1;
        }
        /* Skip BEEP option (disabled/commented out) */
        if (current_choice == CHOICE_BEEP) {
            skip = 1;
        }
        if (!skip) {
            break;  /* Found a valid option */
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
        /* Skip FOLDERS_ART option in non-Folders modes */
        if (current_choice == CHOICE_FOLDERS_ART && sf_ui[0] != UI_FOLDERS) {
            skip = 1;
        }
        /* Skip MARQUEE_SPEED option in non-Scroll/Folders modes */
        if (current_choice == CHOICE_MARQUEE_SPEED && sf_ui[0] != UI_SCROLL && sf_ui[0] != UI_FOLDERS) {
            skip = 1;
        }
        /* Skip Aspect/Sort/Filter in Folders mode */
        if (sf_ui[0] == UI_FOLDERS && (current_choice == CHOICE_ASPECT || current_choice == CHOICE_SORT || current_choice == CHOICE_FILTER)) {
            skip = 1;
        }
        /* Skip BEEP option (disabled/commented out) */
        if (current_choice == CHOICE_BEEP) {
            skip = 1;
        }
        if (!skip) {
            break;  /* Found a valid option */
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
    if ((*input_timeout_ptr > 0) || (current_choice >= CHOICE_CREDITS)) {
        return;
    }
    choices[current_choice]--;
    if (choices[current_choice] < 0) {
        choices[current_choice] = 0;
    }
    if (current_choice == CHOICE_THEME) {
        menu_region_adj();
    }
    *input_timeout_ptr = INPUT_TIMEOUT;
}

static void
menu_choice_right(void) {
    if ((*input_timeout_ptr > 0) || (current_choice >= CHOICE_CREDITS)) {
        return;
    }
    choices[current_choice]++;
    if (choices[current_choice] >= choices_max[current_choice]) {
        choices[current_choice]--;
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
    if (current_choice < 0) {
        current_choice = 0;
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
    if (current_choice >= multidisc_len) {
        current_choice--;
    }
    *input_timeout_ptr = INPUT_TIMEOUT;
}

static void
menu_accept_multidisc(void) {
    if (*input_timeout_ptr > 0) {
        return;
    }
    const gd_item** list_multidisc = list_get_multidisc();

    if (!cb_multidisc) {
        dreamcast_launch_disc(list_multidisc[current_choice]);
    } else {
        dreamcast_launch_cb(list_multidisc[current_choice]);
    }
}

static void
menu_exit(void) {
    if (*input_timeout_ptr > 0) {
        return;
    }

    /* Probably should change this */
    exit_to_bios();
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
    switch (input) {
        case B: menu_leave(); break;
        case A: menu_exit(); break;
        default: break;
    }
}

void
handle_input_codebreaker(enum control input) {
    switch (input) {
        case B: menu_leave(); break;
        case A:
            if (*input_timeout_ptr > 0) {
                return;
            }
            start_cb = 1;
            break;
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
draw_popup_menu(int x, int y, int width, int height) {
    const int border_width = 2;
    draw_draw_quad(x - border_width, y - border_width, width + (2 * border_width), height + (2 * border_width),
                   menu_bkg_border_color);
    draw_draw_quad(x, y, width, height, menu_bkg_color);

    if (sf_ui[0] == UI_SCROLL || sf_ui[0] == UI_FOLDERS) {
        /* Top header */
        draw_draw_quad(x, y, width, 20, menu_bkg_border_color);
    }
}

void
draw_menu_tr(void) {
    z_set_cond(205.0f);
    if (sf_ui[0] == UI_SCROLL || sf_ui[0] == UI_FOLDERS) {
        /* Menu size and placement */
        const int line_height = 24;
        const int width = 320;
        /* Calculate visible options for height - Folders hides more items */
        int visible_options = MENU_OPTIONS - 1;  /* Hide BEEP */
        if (sf_ui[0] == UI_FOLDERS) {
            visible_options -= 4;  /* Hide Aspect, Sort, Filter, SCROLL_ART (SCROLL_INDEX also hidden in rendering) */
        }
        const int height = (visible_options + 5) * line_height + (line_height * 11 / 12);
        const int x = (640 / 2) - (width / 2);
        const int y = (480 / 2) - (height / 2);
        const int x_item = x + 4;

        char line_buf[70];

        /* Draw a popup in the middle of the screen */
        draw_popup_menu(x, y, width, height);

        /* overlay our text on top with options */
        int cur_y = y + 2;
        font_bmp_begin_draw();
        font_bmp_set_color(menu_title_color);

        font_bmp_draw_main(width - (8 * 8 / 2), cur_y, "Settings");

        cur_y += line_height / 2;
        for (int i = 0; i < MENU_CHOICES; i++) {
            /* Skip SCROLL_ART option in non-Scroll modes */
            if (i == CHOICE_SCROLL_ART && sf_ui[0] != UI_SCROLL) {
                continue;
            }
            /* Skip SCROLL_INDEX option in non-Scroll modes */
            if (i == CHOICE_SCROLL_INDEX && sf_ui[0] != UI_SCROLL) {
                continue;
            }
            /* Skip FOLDERS_ART option in non-Folders modes */
            if (i == CHOICE_FOLDERS_ART && sf_ui[0] != UI_FOLDERS) {
                continue;
            }
            /* Skip MARQUEE_SPEED option in non-Scroll/Folders modes */
            if (i == CHOICE_MARQUEE_SPEED && sf_ui[0] != UI_SCROLL && sf_ui[0] != UI_FOLDERS) {
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
            if (i == current_choice) {
                font_bmp_set_color(highlight_color);
            } else {
                font_bmp_set_color(text_color);
            }
            if (i == CHOICE_REGION && (choices[i] >= REGION_CHOICES)) {
                string_outer_concat(line_buf, menu_choice_text[i], custom_theme_text[(int)choices[i] - REGION_CHOICES],
                                    39);
            } else {
                string_outer_concat(line_buf, menu_choice_text[i], menu_choice_array[i][(int)choices[i]], 39);
            }
            font_bmp_draw_main(x_item, cur_y, line_buf);
        }

        /* Draw save or apply choice, highlight the current one */
        uint32_t save_color =
            ((current_choice == CHOICE_SAVE) && (choices[CHOICE_SAVE] == 0) ? highlight_color : text_color);
        uint32_t apply_color =
            ((current_choice == CHOICE_SAVE) && (choices[CHOICE_SAVE] == 1) ? highlight_color : text_color);
        font_bmp_set_color(save_color);
        cur_y += line_height;
        /* Save at 256, Apply at 344 */
        font_bmp_draw_main(640 / 2 - (8 * 8), cur_y, save_choice_text[0]);
        font_bmp_set_color(apply_color);
        font_bmp_draw_main(640 / 2 + (8 * 3), cur_y, save_choice_text[1]);

        if (current_choice == CHOICE_CREDITS) {
            font_bmp_set_color(highlight_color);
        } else {
            font_bmp_set_color(text_color);
        }
        cur_y += line_height;
        font_bmp_draw_main(640 / 2 - (8 * 4), cur_y, credits_text[0]);

        /* Add empty line for spacing */
        cur_y += line_height;

        /* Draw GDEMU firmware version (non-selectable) */
        uint8_t version_buffer[8] = {0};
        uint32_t version_size = 8;
        char version_str[40];
        if (gdemu_get_version(version_buffer, &version_size) == 0) {
            snprintf(version_str, sizeof(version_str), "GDEMU Firmware: %d.%02x.%d",
                     version_buffer[7], version_buffer[6], version_buffer[5]);
        } else {
            snprintf(version_str, sizeof(version_str), "GDEMU Firmware: N/A");
        }
        font_bmp_set_color(text_color);
        cur_y += line_height;
        /* Center based on actual string length (8 pixels per character) */
        int str_pixel_width = strlen(version_str) * 8;
        font_bmp_draw_main(640 / 2 - (str_pixel_width / 2), cur_y, version_str);

        /* Draw openMenu build version (non-selectable) */
        char build_str[80];
        snprintf(build_str, sizeof(build_str), "openMenu Build: %s", OPENMENU_BUILD_VERSION);
        cur_y += line_height;
        str_pixel_width = strlen(build_str) * 8;
        font_bmp_draw_main(640 / 2 - (str_pixel_width / 2), cur_y, build_str);

    } else {
        /* Menu size and placement (Artwork/Marquee options not shown in LineDesc/Grid3) */
        const int line_height = 32;
        const int width = 400;
        const int visible_options = MENU_OPTIONS - 5; /* Exclude SCROLL_ART, SCROLL_INDEX, FOLDERS_ART, MARQUEE_SPEED, and BEEP */
        const int height = (visible_options + 4) * line_height - line_height / 4;
        const int x = (640 / 2) - (width / 2);
        const int y = (480 / 2) - (height / 2);
        const int x_item = x + 4;
        const int x_choice = 344 + 24 + 20 + 25; /* magic :( */

        /* Draw a popup in the middle of the screen */
        draw_popup_menu(x, y, width, height);

        /* overlay our text on top with options */
        int cur_y = y + 2;
        font_bmf_begin_draw();
        font_bmf_set_height_default();

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
            /* Skip FOLDERS_ART option in non-Folders modes */
            if (i == CHOICE_FOLDERS_ART && sf_ui[0] != UI_FOLDERS) {
                continue;
            }
            /* Skip MARQUEE_SPEED option in non-Scroll/Folders modes */
            if (i == CHOICE_MARQUEE_SPEED && sf_ui[0] != UI_SCROLL && sf_ui[0] != UI_FOLDERS) {
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
            font_bmf_draw(x_item, cur_y, temp_color, menu_choice_text[i]);

            if (i == CHOICE_REGION && (choices[i] >= REGION_CHOICES)) {
                font_bmf_draw_centered(x_choice, cur_y, temp_color,
                                       custom_theme_text[(int)choices[i] - REGION_CHOICES]);
            } else {
                font_bmf_draw_centered(x_choice, cur_y, temp_color, menu_choice_array[i][(int)choices[i]]);
            }
        }

        /* Draw save or apply choice, highlight the current one */
        uint32_t save_color =
            ((current_choice == CHOICE_SAVE) && (choices[CHOICE_SAVE] == 0) ? highlight_color : text_color);
        uint32_t apply_color =
            ((current_choice == CHOICE_SAVE) && (choices[CHOICE_SAVE] == 1) ? highlight_color : text_color);
        cur_y += line_height;
        font_bmf_draw_centered(640 / 2 - (width / 4), cur_y, save_color, save_choice_text[0]);
        font_bmf_draw_centered(640 / 2 + (width / 4), cur_y, apply_color, save_choice_text[1]);

        uint32_t temp_color = ((current_choice == CHOICE_CREDITS) ? highlight_color : text_color);
        cur_y += line_height;
        font_bmf_draw_centered(640 / 2, cur_y, temp_color, credits_text[0]);

        /* Add empty line for spacing */
        cur_y += line_height;

        /* Draw GDEMU firmware version (non-selectable, smaller font) */
        uint8_t version_buffer[8] = {0};
        uint32_t version_size = 8;
        char version_str[40];
        if (gdemu_get_version(version_buffer, &version_size) == 0) {
            snprintf(version_str, sizeof(version_str), "GDEMU  Firmware:  %d.%02x.%d",
                     version_buffer[7], version_buffer[6], version_buffer[5]);
        } else {
            snprintf(version_str, sizeof(version_str), "GDEMU  Firmware:  N/A");
        }
        cur_y += line_height / 2;
        font_bmf_set_height(20.0f);
        font_bmf_draw_centered(640 / 2, cur_y, text_color, version_str);

        /* Draw openMenu build version (non-selectable, smaller font) */
        char build_str[80];
        snprintf(build_str, sizeof(build_str), "openMenu  Build :  %s", OPENMENU_BUILD_VERSION);
        cur_y += line_height * 3 / 4;
        font_bmf_draw_centered(640 / 2, cur_y, text_color, build_str);

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
        const int height = (num_credits + 1) * line_height + (line_height * 13 / 12);
        const int x = (640 / 2) - (width / 2);
        const int y = (480 / 2) - (height / 2);
        const int x_item = x + 4;

        char line_buf[65];
        /* Draw a rectangle in the middle of the screen */

        /* Draw a popup in the middle of the screen */
        draw_popup_menu(x, y, width, height);

        /* overlay our text on top with options */
        int cur_y = y + 4;
        font_bmp_begin_draw();
        font_bmp_set_color(sf_ui[0] == UI_FOLDERS ? menu_title_color : text_color);

        font_bmp_draw_main(width - (8 * 8 / 2), cur_y, "Credits");
        font_bmp_set_color(sf_ui[0] == UI_FOLDERS ? text_color : highlight_color);

        cur_y += line_height / 2;
        for (int i = 0; i < num_credits; i++) {
            cur_y += line_height;
            string_outer_concat(line_buf, credits[i].contributor, credits[i].role, 39);
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
        /* Menu size and placement */
        const int line_height = 24;
        const int width = 320;
        const int height = (multidisc_len + 1) * line_height + (line_height / 2);
        const int x = (640 / 2) - (width / 2);
        const int y = (480 / 2) - (height / 2);
        const int x_item = x + 4;
        char line_buf[65];
        char temp_game_name[36];
        char temp_game_num[4];

        /* Draw a popup in the middle of the screen */
        draw_popup_menu(x, y, width, height);

        /* overlay our text on top with options */
        int cur_y = y + 2;
        font_bmp_begin_draw();
        font_bmp_set_color(sf_ui[0] == UI_FOLDERS ? menu_title_color : text_color);

        font_bmp_draw_main(width - (10 * 8 / 2), cur_y, "Multidisc");

        cur_y += line_height / 2;
        for (int i = 0; i < multidisc_len; i++) {
            cur_y += line_height;
            if (i == current_choice) {
                font_bmp_set_color(highlight_color);
            } else {
                font_bmp_set_color(text_color);
            }
            const int disc_num = list_multidisc[i]->disc[0] - '0';
            strncpy(temp_game_name, list_multidisc[i]->name, sizeof(temp_game_name) - 1);
            temp_game_name[sizeof(temp_game_name) - 1] = '\0';
            snprintf(temp_game_num, sizeof(temp_game_name), "#%d", disc_num);
            string_outer_concat(line_buf, temp_game_name, temp_game_num, 39);
            font_bmp_draw_main(x_item, cur_y, line_buf);
        }
    } else {
        /* Menu size and placement */
        const int line_height = 32;
        const int width = 300;
        const int height = (multidisc_len + 1) * line_height + (line_height / 2);
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

        font_bmf_draw_centered(x + width / 2, cur_y, text_color, "Multidisc");

        cur_y += line_height / 4;

        for (int i = 0; i < multidisc_len; i++) {
            cur_y += line_height;
            uint32_t temp_color = text_color;
            if (i == current_choice) {
                temp_color = highlight_color;
            }
            const int disc_num = list_multidisc[i]->disc[0] - '0';
            strncpy(temp_game_name, list_multidisc[i]->name, sizeof(temp_game_name) - 1);
            temp_game_name[sizeof(temp_game_name) - 1] = '\0';
            snprintf(line_buf, 69, "%s #%d", temp_game_name, disc_num);
            font_bmf_draw_auto_size(x_item, cur_y, temp_color, line_buf, width - 4);
        }
    }
}

void
draw_exit_op(void) { /* Again nothing...Still...Ugh... */ }

void
draw_exit_tr(void) {
    z_set_cond(205.0f);

    /* Draw a popup in the middle of the screen */
    const int popup_height = (sf_ui[0] == UI_SCROLL || sf_ui[0] == UI_FOLDERS) ? 70 : 80;
    draw_popup_menu(160, 120, 180, popup_height);

    if (sf_ui[0] == UI_SCROLL || sf_ui[0] == UI_FOLDERS) {
        font_bmp_begin_draw();
        font_bmp_set_color(menu_title_color);

        font_bmp_draw_main(200, 122, "Exit to BIOS");
        font_bmp_set_color(sf_ui[0] == UI_FOLDERS ? text_color : highlight_color);
        font_bmp_draw_main(168, 158, "A - exit, B - cancel");
    } else {
        font_bmf_begin_draw();
        font_bmf_set_height(24.0);

        font_bmf_draw(200, 122, text_color, "Exit to BIOS");
        font_bmf_draw(168, 158, highlight_color, "A - exit, B - cancel");
    }
}

void
draw_codebreaker_op(void) { /* Again nothing...Still...Ugh... */ }

void
draw_codebreaker_tr(void) {
    z_set_cond(205.0f);

    /* Draw a popup in the middle of the screen */
    const int popup_height = (sf_ui[0] == UI_SCROLL || sf_ui[0] == UI_FOLDERS) ? 70 : 80;
    draw_popup_menu(160, 120, 190, popup_height);

    if (sf_ui[0] == UI_SCROLL || sf_ui[0] == UI_FOLDERS) {
        font_bmp_begin_draw();
        font_bmp_set_color(menu_title_color);

        font_bmp_draw_main(200, 122, "Run CodeBreaker");
        font_bmp_set_color(sf_ui[0] == UI_FOLDERS ? text_color : highlight_color);
        font_bmp_draw_main(178, 158, "A - run, B - cancel");
    } else {
        font_bmf_begin_draw();
        font_bmf_set_height(24.0);

        font_bmf_draw(170, 122, text_color, "Run CodeBreaker");
        font_bmf_draw(178, 158, highlight_color, "A - run,  B - cancel");
    }
}
