#include "openmenu_settings.h"

uint8_t* sf_region;
uint8_t* sf_aspect;
uint8_t* sf_ui;
uint8_t* sf_sort;
uint8_t* sf_filter;
uint8_t* sf_beep;
uint8_t* sf_multidisc;
uint8_t* sf_multidisc_grouping;
uint8_t* sf_custom_theme;
uint8_t* sf_custom_theme_num;
uint8_t* sf_bios_3d;
uint8_t* sf_scroll_art;
uint8_t* sf_scroll_index;
uint8_t* sf_folders_art;
uint8_t* sf_folder_art;
uint8_t* sf_folders_item_details;
uint8_t* sf_marquee_speed;
uint8_t* sf_disc_details;
uint8_t* sf_clock;
uint8_t* sf_vm2_send_all;
uint8_t* sf_boot_mode;
uint8_t* sf_vmu_time_sync;
uint8_t* sf_serial_vmu;
uint8_t* sf_serial_vmu_multislot;
uint8_t* sf_music;
uint8_t* sf_honor_defaults;

void
settings_sanitize() {
    if ((sf_ui[0] < UI_START) || (sf_ui[0] > UI_END)) {
        sf_ui[0] = UI_FOLDERS;
    }

    if ((sf_region[0] < REGION_START) || (sf_region[0] > REGION_END)) {
        sf_region[0] = REGION_NTSC_U;
    }

    if ((sf_aspect[0] < ASPECT_START) || (sf_aspect[0] > ASPECT_END)) {
        sf_aspect[0] = ASPECT_NORMAL;
    }

    if ((sf_sort[0] < SORT_START) || (sf_sort[0] > SORT_END)) {
        sf_sort[0] = SORT_DEFAULT;
    }

    if ((sf_filter[0] < FILTER_START) || (sf_filter[0] > FILTER_END)) {
        sf_filter[0] = FILTER_ALL;
    }

    if ((sf_beep[0] < BEEP_START) || (sf_beep[0] > BEEP_END)) {
        sf_beep[0] = BEEP_OFF;
    }

    if ((sf_multidisc[0] < MULTIDISC_START) || (sf_multidisc[0] > MULTIDISC_END)) {
        sf_multidisc[0] = MULTIDISC_SHOW;
    }

    if ((sf_multidisc_grouping[0] < MULTIDISC_GROUPING_START) || (sf_multidisc_grouping[0] > MULTIDISC_GROUPING_END)) {
        sf_multidisc_grouping[0] = MULTIDISC_GROUPING_ANYWHERE;
    }

    if ((sf_custom_theme[0] < THEME_START) || (sf_custom_theme[0] > THEME_END)) {
        sf_custom_theme[0] = THEME_OFF;
    }

    if ((sf_custom_theme_num[0] < THEME_NUM_START) || (sf_custom_theme_num[0] > THEME_NUM_END)) {
        sf_custom_theme_num[0] = THEME_NUM_START;
    }

    if (sf_custom_theme[0]) {
        sf_region[0] = REGION_END + 1 + sf_custom_theme_num[0];
    }

    if ((sf_bios_3d[0] < BIOS_3D_START) || (sf_bios_3d[0] > BIOS_3D_END)) {
        sf_bios_3d[0] = BIOS_3D_STANDARD;
    }

    if ((sf_scroll_art[0] < SCROLL_ART_START) || (sf_scroll_art[0] > SCROLL_ART_END)) {
        sf_scroll_art[0] = SCROLL_ART_ON;
    }

    if ((sf_scroll_index[0] < SCROLL_INDEX_START) || (sf_scroll_index[0] > SCROLL_INDEX_END)) {
        sf_scroll_index[0] = SCROLL_INDEX_ON;
    }

    if ((sf_folders_art[0] < FOLDERS_ART_START) || (sf_folders_art[0] > FOLDERS_ART_END)) {
        sf_folders_art[0] = FOLDERS_ART_ON;
    }

    if ((sf_folder_art[0] < FOLDER_ART_START) || (sf_folder_art[0] > FOLDER_ART_END)) {
        sf_folder_art[0] = FOLDER_ART_ON;
    }

    if ((sf_folders_item_details[0] < FOLDERS_ITEM_DETAILS_START)
        || (sf_folders_item_details[0] > FOLDERS_ITEM_DETAILS_END)) {
        sf_folders_item_details[0] = FOLDERS_ITEM_DETAILS_ON;
    }

    if ((sf_marquee_speed[0] < MARQUEE_SPEED_START) || (sf_marquee_speed[0] > MARQUEE_SPEED_END)) {
        sf_marquee_speed[0] = MARQUEE_SPEED_MEDIUM;
    }

    if ((sf_disc_details[0] < DISC_DETAILS_START) || (sf_disc_details[0] > DISC_DETAILS_END)) {
        sf_disc_details[0] = DISC_DETAILS_SHOW;
    }

    if ((sf_clock[0] < CLOCK_START) || (sf_clock[0] > CLOCK_END)) {
        sf_clock[0] = CLOCK_12HOUR;
    }

    if ((sf_vm2_send_all[0] < VM2_SEND_START) || (sf_vm2_send_all[0] > VM2_SEND_END)) {
        sf_vm2_send_all[0] = VM2_SEND_ALL;
    }

    if ((sf_boot_mode[0] < BOOT_MODE_START) || (sf_boot_mode[0] > BOOT_MODE_END)) {
        sf_boot_mode[0] = BOOT_MODE_FULL;
    }

    if ((sf_vmu_time_sync[0] < VMU_TIME_SYNC_START) || (sf_vmu_time_sync[0] > VMU_TIME_SYNC_END)) {
        sf_vmu_time_sync[0] = VMU_TIME_SYNC_OFF;
    }

    if ((sf_serial_vmu[0] < SERIAL_VMU_START) || (sf_serial_vmu[0] > SERIAL_VMU_END)) {
        sf_serial_vmu[0] = SERIAL_VMU_OFF;
    }

    if ((sf_serial_vmu_multislot[0] < SERIAL_VMU_MULTISLOT_START)
        || (sf_serial_vmu_multislot[0] > SERIAL_VMU_MULTISLOT_END)) {
        sf_serial_vmu_multislot[0] = SERIAL_VMU_MULTISLOT_OFF;
    }

    if ((sf_music[0] < MUSIC_START) || (sf_music[0] > MUSIC_END)) {
        sf_music[0] = MUSIC_ON;
    }

    if ((sf_honor_defaults[0] < HONOR_DEFAULTS_START) || (sf_honor_defaults[0] > HONOR_DEFAULTS_END)) {
        sf_honor_defaults[0] = HONOR_DEFAULTS_ON;
    }
}