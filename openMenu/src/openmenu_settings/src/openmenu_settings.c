#include "openmenu_settings.h"

uint8_t* sf_region;
uint8_t* sf_aspect;
uint8_t* sf_ui;
uint8_t* sf_sort;
uint8_t* sf_filter;
uint8_t* sf_beep;
uint8_t* sf_multidisc;
uint8_t* sf_custom_theme;
uint8_t* sf_custom_theme_num;
uint8_t* sf_bios_3d;
uint8_t* sf_scroll_art;
uint8_t* sf_scroll_index;
uint8_t* sf_folders_art;
uint8_t* sf_marquee_speed;

void
settings_sanitize() {
    if ((sf_ui[0] < UI_START) || (sf_ui[0] > UI_END)) {
        sf_ui[0] = UI_LINE_DESC;
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
        sf_beep[0] = BEEP_ON;
    }

    if ((sf_multidisc[0] < MULTIDISC_START) || (sf_multidisc[0] > MULTIDISC_END)) {
        sf_multidisc[0] = MULTIDISC_SHOW;
    }

    if ((sf_custom_theme[0] < THEME_START) || (sf_custom_theme[0] > THEME_END)) {
        sf_custom_theme_num[0] = (CFG_CUSTOM_THEME_NUM)THEME_OFF;
    }

    if ((sf_custom_theme_num[0] < THEME_NUM_START) || (sf_custom_theme_num[0] > THEME_NUM_END)) {
        sf_custom_theme_num[0] = THEME_NUM_START;
    }

    if (sf_custom_theme[0]) {
        sf_region[0] = REGION_END + 1 + sf_custom_theme_num[0];
    }

    if ((sf_beep[0] < BIOS_3D_START) || (sf_beep[0] > BIOS_3D_END)) {
        sf_beep[0] = BIOS_3D_OFF;
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

    if ((sf_marquee_speed[0] < MARQUEE_SPEED_START) || (sf_marquee_speed[0] > MARQUEE_SPEED_END)) {
        sf_marquee_speed[0] = MARQUEE_SPEED_MEDIUM;
    }
}