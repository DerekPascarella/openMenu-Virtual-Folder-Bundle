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

extern uint8_t* sf_multidisc_grouping;
#define sf_multidisc_grouping_type   CRAYON_TYPE_UINT8
#define sf_multidisc_grouping_length 1

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

extern uint8_t* sf_folder_art;
#define sf_folder_art_type   CRAYON_TYPE_UINT8
#define sf_folder_art_length 1

extern uint8_t* sf_marquee_speed;
#define sf_marquee_speed_type   CRAYON_TYPE_UINT8
#define sf_marquee_speed_length 1

extern uint8_t* sf_disc_details;
#define sf_disc_details_type   CRAYON_TYPE_UINT8
#define sf_disc_details_length 1

extern uint8_t* sf_folders_item_details;
#define sf_folders_item_details_type   CRAYON_TYPE_UINT8
#define sf_folders_item_details_length 1

extern uint8_t* sf_clock;
#define sf_clock_type   CRAYON_TYPE_UINT8
#define sf_clock_length 1

extern uint8_t* sf_vm2_send_all;
#define sf_vm2_send_all_type   CRAYON_TYPE_UINT8
#define sf_vm2_send_all_length 1

extern uint8_t* sf_boot_mode;
#define sf_boot_mode_type   CRAYON_TYPE_UINT8
#define sf_boot_mode_length 1

extern uint8_t* sf_vmu_time_sync;
#define sf_vmu_time_sync_type   CRAYON_TYPE_UINT8
#define sf_vmu_time_sync_length 1

extern uint8_t* sf_serial_vmu;
#define sf_serial_vmu_type   CRAYON_TYPE_UINT8
#define sf_serial_vmu_length 1

extern uint8_t* sf_serial_vmu_multislot;
#define sf_serial_vmu_multislot_type   CRAYON_TYPE_UINT8
#define sf_serial_vmu_multislot_length 1

extern uint8_t* sf_music;
#define sf_music_type   CRAYON_TYPE_UINT8
#define sf_music_length 1

extern uint8_t* sf_honor_defaults;
#define sf_honor_defaults_type   CRAYON_TYPE_UINT8
#define sf_honor_defaults_length 1

extern uint8_t* sf_recently_played;
#define sf_recently_played_type   CRAYON_TYPE_UINT8
#define sf_recently_played_length 1

/* Read and write the four byte values that are stored as bytes. Keeping
 * them out of the uint32 block matters because a save holds every uint32
 * ahead of every byte, so a wider uint32 block shifts the settings out
 * from under an older build reading the same save. */
static inline unsigned int
sf_u32_read(const uint8_t* bytes) {
    return ((unsigned int)bytes[0]) | ((unsigned int)bytes[1] << 8) | ((unsigned int)bytes[2] << 16)
           | ((unsigned int)bytes[3] << 24);
}

static inline void
sf_u32_write(uint8_t* bytes, unsigned int value) {
    bytes[0] = (uint8_t)(value & 0xFF);
    bytes[1] = (uint8_t)((value >> 8) & 0xFF);
    bytes[2] = (uint8_t)((value >> 16) & 0xFF);
    bytes[3] = (uint8_t)((value >> 24) & 0xFF);
}

/* Launch history as game hashes, newest first, zero means empty slot.
 * More slots than the largest display option so entries for games that
 * are missing from the current card still leave enough visible ones. */
#define sf_recent_games_slots 100
extern uint8_t* sf_recent_games;
#define sf_recent_games_type   CRAYON_TYPE_UINT8
#define sf_recent_games_length (sf_recent_games_slots * 4)

static inline unsigned int
sf_recent_games_get(int slot) {
    return sf_u32_read(&sf_recent_games[slot * 4]);
}

static inline void
sf_recent_games_set(int slot, unsigned int hash) {
    sf_u32_write(&sf_recent_games[slot * 4], hash);
}

extern uint8_t* sf_remember_last_game;
#define sf_remember_last_game_type   CRAYON_TYPE_UINT8
#define sf_remember_last_game_length 1

/* Where the cursor goes on the next boot. The hash uses the same identity
 * as the launch history, the serial covers cards rebuilt with different
 * titles, and the last two record the view it came from. A zero hash
 * means nothing is remembered. The three numbers are kept as bytes for
 * the reason given above sf_u32_read. */
extern uint8_t* sf_last_game;
#define sf_last_game_type   CRAYON_TYPE_UINT8
#define sf_last_game_length 4

extern uint8_t* sf_last_game_product;
#define sf_last_game_product_type   CRAYON_TYPE_UINT8
#define sf_last_game_product_length 12

extern uint8_t* sf_last_game_folder;
#define sf_last_game_folder_type   CRAYON_TYPE_UINT8
#define sf_last_game_folder_length 4

/* Category type in the low byte, category number in the next one */
extern uint8_t* sf_last_game_filter;
#define sf_last_game_filter_type   CRAYON_TYPE_UINT8
#define sf_last_game_filter_length 4

enum savefile_version {
    SFV_INITIAL = 1,
    SFV_BIOS_3D,
    SFV_SCROLL_ART,
    SFV_SCROLL_INDEX,
    SFV_FOLDERS_ART,
    SFV_MARQUEE_SPEED,
    SFV_DISC_DETAILS,
    SFV_FOLDERS_ITEM_DETAILS,
    SFV_CLOCK,
    SFV_MULTIDISC_GROUPING,
    SFV_VM2_SEND_ALL,
    SFV_BOOT_MODE,
    SFV_VMU_TIME_SYNC,
    SFV_SERIAL_VMU,
    SFV_SERIAL_VMU_MULTISLOT,
    SFV_EXIT_BIOS,
    SFV_FOLDER_ART,
    SFV_MUSIC,
    SFV_HONOR_DEFAULTS,
    SFV_RECENTLY_PLAYED,
    SFV_REMEMBER_LAST_GAME,
    SFV_LATEST_PLUS_ONE // DON'T REMOVE
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

typedef enum CFG_UI {
    UI_START = 0,
    UI_LINE_DESC = UI_START,
    UI_GRID3,
    UI_SCROLL,
    UI_FOLDERS,
    UI_END = UI_FOLDERS
} CFG_UI;

typedef enum CFG_SORT {
    SORT_START = 0,
    SORT_DEFAULT = SORT_START, /* Now means Alphabetical */
    SORT_NAME,
    SORT_DATE,
    SORT_PRODUCT,
    SORT_SD_CARD, /* SD Card Order (slot order) */
    SORT_END = SORT_SD_CARD
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

typedef enum CFG_MULTIDISC_GROUPING {
    MULTIDISC_GROUPING_START = 0,
    MULTIDISC_GROUPING_ANYWHERE = MULTIDISC_GROUPING_START,
    MULTIDISC_GROUPING_SAME_FOLDER,
    MULTIDISC_GROUPING_END = MULTIDISC_GROUPING_SAME_FOLDER
} CFG_MULTIDISC_GROUPING;

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
    BIOS_3D_STANDARD = BIOS_3D_START,
    BIOS_3D_ALTERNATE,
    BIOS_3D_ALTERNATE_3D,
    BIOS_3D_END = BIOS_3D_ALTERNATE_3D
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

typedef enum CFG_FOLDER_ART {
    FOLDER_ART_START = 0,
    FOLDER_ART_OFF = FOLDER_ART_START,
    FOLDER_ART_ON,
    FOLDER_ART_END = FOLDER_ART_ON
} CFG_FOLDER_ART;

typedef enum CFG_MARQUEE_SPEED {
    MARQUEE_SPEED_START = 0,
    MARQUEE_SPEED_SLOW = MARQUEE_SPEED_START,
    MARQUEE_SPEED_MEDIUM,
    MARQUEE_SPEED_FAST,
    MARQUEE_SPEED_END = MARQUEE_SPEED_FAST
} CFG_MARQUEE_SPEED;

typedef enum CFG_DISC_DETAILS {
    DISC_DETAILS_START = 0,
    DISC_DETAILS_SHOW = DISC_DETAILS_START,
    DISC_DETAILS_HIDE,
    DISC_DETAILS_END = DISC_DETAILS_HIDE
} CFG_DISC_DETAILS;

typedef enum CFG_FOLDERS_ITEM_DETAILS {
    FOLDERS_ITEM_DETAILS_START = 0,
    FOLDERS_ITEM_DETAILS_OFF = FOLDERS_ITEM_DETAILS_START,
    FOLDERS_ITEM_DETAILS_ON,
    FOLDERS_ITEM_DETAILS_END = FOLDERS_ITEM_DETAILS_ON
} CFG_FOLDERS_ITEM_DETAILS;

typedef enum CFG_RECENTLY_PLAYED {
    RECENTLY_PLAYED_START = 0,
    RECENTLY_PLAYED_OFF = RECENTLY_PLAYED_START,
    RECENTLY_PLAYED_10,
    RECENTLY_PLAYED_20,
    RECENTLY_PLAYED_30,
    RECENTLY_PLAYED_40,
    RECENTLY_PLAYED_50,
    RECENTLY_PLAYED_END = RECENTLY_PLAYED_50
} CFG_RECENTLY_PLAYED;

/* Display cap for the current setting, 10 through 50 */
#define RECENTLY_PLAYED_DISPLAY_MAX(setting) ((int)(setting) * 10)

typedef enum CFG_REMEMBER_LAST_GAME {
    REMEMBER_LAST_GAME_START = 0,
    REMEMBER_LAST_GAME_OFF = REMEMBER_LAST_GAME_START,
    REMEMBER_LAST_GAME_ON,
    REMEMBER_LAST_GAME_END = REMEMBER_LAST_GAME_ON
} CFG_REMEMBER_LAST_GAME;

typedef enum CFG_CLOCK {
    CLOCK_START = 0,
    CLOCK_12HOUR = CLOCK_START,
    CLOCK_24HOUR,
    CLOCK_OFF,
    CLOCK_END = CLOCK_OFF
} CFG_CLOCK;

typedef enum CFG_VM2_SEND_ALL {
    VM2_SEND_START = 0,
    VM2_SEND_ALL = VM2_SEND_START,
    VM2_SEND_FIRST,
    VM2_SEND_OFF,
    VM2_SEND_END = VM2_SEND_OFF
} CFG_VM2_SEND_ALL;

typedef enum CFG_BOOT_MODE {
    BOOT_MODE_START = 0,
    BOOT_MODE_FULL = BOOT_MODE_START, // boot_intro=1, sega_license=1
    BOOT_MODE_LICENSE,                // boot_intro=0, sega_license=1
    BOOT_MODE_ANIMATION,              // boot_intro=1, sega_license=0
    BOOT_MODE_FAST,                   // boot_intro=0, sega_license=0
    BOOT_MODE_END = BOOT_MODE_FAST
} CFG_BOOT_MODE;

typedef enum CFG_VMU_TIME_SYNC {
    VMU_TIME_SYNC_START = 0,
    VMU_TIME_SYNC_OFF = VMU_TIME_SYNC_START,
    VMU_TIME_SYNC_ON,
    VMU_TIME_SYNC_END = VMU_TIME_SYNC_ON
} CFG_VMU_TIME_SYNC;

typedef enum CFG_SERIAL_VMU {
    SERIAL_VMU_START = 0,
    SERIAL_VMU_OFF = SERIAL_VMU_START,
    SERIAL_VMU_A1,
    SERIAL_VMU_A2,
    SERIAL_VMU_B1,
    SERIAL_VMU_B2,
    SERIAL_VMU_C1,
    SERIAL_VMU_C2,
    SERIAL_VMU_D1,
    SERIAL_VMU_D2,
    SERIAL_VMU_END = SERIAL_VMU_D2
} CFG_SERIAL_VMU;

typedef enum CFG_SERIAL_VMU_MULTISLOT {
    SERIAL_VMU_MULTISLOT_START = 0,
    SERIAL_VMU_MULTISLOT_OFF = SERIAL_VMU_MULTISLOT_START,
    SERIAL_VMU_MULTISLOT_ON,
    SERIAL_VMU_MULTISLOT_END = SERIAL_VMU_MULTISLOT_ON
} CFG_SERIAL_VMU_MULTISLOT;

typedef enum CFG_MUSIC { MUSIC_START = 0, MUSIC_OFF = MUSIC_START, MUSIC_ON, MUSIC_END = MUSIC_ON } CFG_MUSIC;

typedef enum CFG_HONOR_DEFAULTS {
    HONOR_DEFAULTS_START = 0,
    HONOR_DEFAULTS_OFF = HONOR_DEFAULTS_START,
    HONOR_DEFAULTS_ON,
    HONOR_DEFAULTS_END = HONOR_DEFAULTS_ON
} CFG_HONOR_DEFAULTS;

typedef CFG_REGION region;

/* COMPACTION_TEST_START */
enum draw_state {
    DRAW_UI = 0,
    DRAW_MULTIDISC,
    DRAW_EXIT,
    DRAW_MENU,
    DRAW_CREDITS,
    DRAW_CODEBREAKER,
    DRAW_PSX_LAUNCHER,
    DRAW_SAVELOAD,
    DRAW_COMPACTION_TEST,
    DRAW_SERIAL_VMU,
    DRAW_RECENT_MANAGE
};

/* COMPACTION_TEST_END */

void settings_sanitize();

#endif // OPENMENU_SETTINGS_H
