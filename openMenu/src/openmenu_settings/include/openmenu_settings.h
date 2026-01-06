#ifndef OPENMENU_SETTINGS_H
#define OPENMENU_SETTINGS_H

#include <crayon_savefile/savefile.h>

extern uint8_t* sf_region;
#define sf_region_type   CRAYON_TYPE_UINT8
#define sf_region_length 1

extern uint8_t* sf_aspect;
#define sf_aspect_type   CRAYON_TYPE_UINT8
#define sf_aspect_length 1

extern uint8_t* sf_ui;
#define sf_ui_type   CRAYON_TYPE_UINT8
#define sf_ui_length 1

extern uint8_t* sf_sort;
#define sf_sort_type   CRAYON_TYPE_UINT8
#define sf_sort_length 1

extern uint8_t* sf_filter;
#define sf_filter_type   CRAYON_TYPE_UINT8
#define sf_filter_length 1

extern uint8_t* sf_beep;
#define sf_beep_type   CRAYON_TYPE_UINT8
#define sf_beep_length 1

extern uint8_t* sf_multidisc;
#define sf_multidisc_type   CRAYON_TYPE_UINT8
#define sf_multidisc_length 1

extern uint8_t* sf_custom_theme;
#define sf_custom_theme_type   CRAYON_TYPE_UINT8
#define sf_custom_theme_length 1

extern uint8_t* sf_custom_theme_num;
#define sf_custom_theme_num_type   CRAYON_TYPE_UINT8
#define sf_custom_theme_num_length 1

extern uint8_t* sf_bios_3d;
#define sf_bios_3d_type   CRAYON_TYPE_UINT8
#define sf_bios_3d_length 1

extern uint8_t* sf_scroll_art;
#define sf_scroll_art_type   CRAYON_TYPE_UINT8
#define sf_scroll_art_length 1

extern uint8_t* sf_scroll_index;
#define sf_scroll_index_type   CRAYON_TYPE_UINT8
#define sf_scroll_index_length 1

extern uint8_t* sf_folders_art;
#define sf_folders_art_type   CRAYON_TYPE_UINT8
#define sf_folders_art_length 1

extern uint8_t* sf_marquee_speed;
#define sf_marquee_speed_type   CRAYON_TYPE_UINT8
#define sf_marquee_speed_length 1

enum savefile_version {
    SFV_INITIAL = 1,
    SFV_BIOS_3D,
    SFV_SCROLL_ART,
    SFV_SCROLL_INDEX,
    SFV_FOLDERS_ART,
    SFV_MARQUEE_SPEED,
    SFV_LATEST_PLUS_ONE //DON'T REMOVE
};

#define VAR_STILL_PRESENT SFV_LATEST_PLUS_ONE

#define SFV_CURRENT       (SFV_LATEST_PLUS_ONE - 1)

typedef enum CFG_REGION {
    REGION_START = 0,
    REGION_NTSC_U = REGION_START,
    REGION_NTSC_J,
    REGION_PAL,
    REGION_END = REGION_PAL,
} CFG_REGION;

typedef enum CFG_ASPECT {
    ASPECT_START = 0,
    ASPECT_NORMAL = ASPECT_START,
    ASPECT_WIDE,
    ASPECT_END = ASPECT_WIDE
} CFG_ASPECT;

typedef enum CFG_UI { UI_START = 0, UI_LINE_DESC = UI_START, UI_GRID3, UI_SCROLL, UI_FOLDERS, UI_END = UI_FOLDERS } CFG_UI;

typedef enum CFG_SORT {
    SORT_START = 0,
    SORT_DEFAULT = SORT_START,
    SORT_NAME,
    SORT_DATE,
    SORT_PRODUCT,
    SORT_END = SORT_PRODUCT
} CFG_SORT;

typedef enum CFG_FILTER {
    FILTER_START = 0,
    FILTER_ALL = FILTER_START,
    FILTER_ACTION,
    FILTER_RACING,
    FILTER_SIMULATION,
    FILTER_SPORTS,
    FILTER_LIGHTGUN,
    FILTER_FIGHTING,
    FILTER_SHOOTER,
    FILTER_SURVIVAL,
    FILTER_ADVENTURE,
    FILTER_PLATFORMER,
    FILTER_RPG,
    FILTER_SHMUP,
    FILTER_STRATEGY,
    FILTER_PUZZLE,
    FILTER_ARCADE,
    FILTER_MUSIC,
    FILTER_END = FILTER_MUSIC
} CFG_FILTER;

typedef enum CFG_BEEP { BEEP_START = 0, BEEP_OFF = BEEP_START, BEEP_ON, BEEP_END = BEEP_ON } CFG_BEEP;

typedef enum CFG_MULTIDISC {
    MULTIDISC_START = 0,
    MULTIDISC_SHOW = MULTIDISC_START,
    MULTIDISC_HIDE,
    MULTIDISC_END = MULTIDISC_HIDE
} CFG_MULTIDISC;

typedef enum CFG_CUSTOM_THEME {
    THEME_START = 0,
    THEME_OFF = THEME_START,
    THEME_ON,
    THEME_END = THEME_ON
} CFG_CUSTOM_THEME;

typedef enum CFG_CUSTOM_THEME_NUM {
    THEME_NUM_START = 0,
    THEME_0 = THEME_NUM_START,
    THEME_1,
    THEME_2,
    THEME_3,
    THEME_4,
    THEME_5,
    THEME_6,
    THEME_7,
    THEME_8,
    THEME_9,
    THEME_NUM_END = THEME_9
} CFG_CUSTOM_THEME_NUM;

typedef enum CFG_BIOS_3D {
    BIOS_3D_START = 0,
    BIOS_3D_OFF = BIOS_3D_START,
    BIOS_3D_ON,
    BIOS_3D_END = BIOS_3D_ON
} CFG_BIOS_3D;

typedef enum CFG_SCROLL_ART {
    SCROLL_ART_START = 0,
    SCROLL_ART_OFF = SCROLL_ART_START,
    SCROLL_ART_ON,
    SCROLL_ART_END = SCROLL_ART_ON
} CFG_SCROLL_ART;

typedef enum CFG_SCROLL_INDEX {
    SCROLL_INDEX_START = 0,
    SCROLL_INDEX_OFF = SCROLL_INDEX_START,
    SCROLL_INDEX_ON,
    SCROLL_INDEX_END = SCROLL_INDEX_ON
} CFG_SCROLL_INDEX;

typedef enum CFG_FOLDERS_ART {
    FOLDERS_ART_START = 0,
    FOLDERS_ART_OFF = FOLDERS_ART_START,
    FOLDERS_ART_ON,
    FOLDERS_ART_END = FOLDERS_ART_ON
} CFG_FOLDERS_ART;

typedef enum CFG_MARQUEE_SPEED {
    MARQUEE_SPEED_START = 0,
    MARQUEE_SPEED_SLOW = MARQUEE_SPEED_START,
    MARQUEE_SPEED_MEDIUM,
    MARQUEE_SPEED_FAST,
    MARQUEE_SPEED_END = MARQUEE_SPEED_FAST
} CFG_MARQUEE_SPEED;

typedef CFG_REGION region;

enum draw_state { DRAW_UI = 0, DRAW_MULTIDISC, DRAW_EXIT, DRAW_MENU, DRAW_CREDITS, DRAW_CODEBREAKER };

void settings_sanitize();

#endif //OPENMENU_SETTINGS_H
