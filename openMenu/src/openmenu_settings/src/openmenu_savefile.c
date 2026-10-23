#ifdef _arch_dreamcast
#include <arch/irq.h>
#include <arch/rtc.h>
#include <crayon_savefile/peripheral.h>
#include <dc/flashrom.h>
#include <dc/maple.h>
#include <dc/maple/vmu.h>
#include <dc/video.h>
#include <kos/genwait.h>
#include <kos/thread.h>
#include <stdlib.h>

#include <openmenu_debug.h>

#if DEBUG_MAPLE_FLASH
static void
debug_flash_sf(uint8_t r, uint8_t g, uint8_t b) {
    vid_clear(r, g, b);
    thd_sleep(300);
}

#define DFLASH_SF(r, g, b) debug_flash_sf(r, g, b)
#else
#define DFLASH_SF(r, g, b) ((void)0)
#endif

#endif

#include <crayon_savefile/savefile.h>
#include <stdbool.h>
#include <string.h>

#include "boot_trace.h"
#include "openmenu_savefile.h"
#include "openmenu_settings.h"
#include "vmu_sync_debug.h"

/* Images and such */
#if __has_include("openmenu_lcd.h")                                                                                    \
                  && __has_include(                                                                                    \
                      "openmenu_pal.h")                                                                                \
                      && __has_include(                                                                                \
                          "openmenu_vmu.h")                                                                            \
                          && __has_include(                                                                            \
                              "openmenu_lcd_dcnow_off.h")                                                              \
                              && __has_include("openmenu_lcd_dcnow_on.h")                                              \
                                               && __has_include("openmenu_lcd_access_dcnow_off.h")                     \
                                                                && __has_include("openmenu_lcd_access_dcnow_on.h")
#include "openmenu_lcd.h"
#include "openmenu_lcd_access.h"
#include "openmenu_lcd_access_dcnow_off.h"
#include "openmenu_lcd_access_dcnow_on.h"
#include "openmenu_lcd_dcnow_off.h"
#include "openmenu_lcd_dcnow_on.h"
#include "openmenu_pal.h"
#include "openmenu_vmu.h"

#define OPENMENU_ICON  (openmenu_icon)
#define OPENMENU_PAL   (openmenu_pal)
#define OPENMENU_ICONS (1)
#else
#define OPENMENU_ICON  (NULL)
#define OPENMENU_PAL   (NULL)
#define OPENMENU_ICONS (0)
#endif

static crayon_savefile_details_t savefile_details;
static bool savefile_was_migrated = false;
static int8_t startup_device_id = -1; /* Device we loaded settings from at startup */
static bool loaded_from_sd = false;   /* True if settings were loaded from SD at startup */
static bool vmu_time_sync_warning;
static bool clock_storage_is_full;
static bool clock_storage_warning;

bool
clock_sync_storage_full(void) {
    return clock_storage_is_full;
}

bool
clock_sync_warning_pending(void) {
    return clock_storage_warning;
}

void
clock_sync_warning_dismiss(void) {
    clock_storage_warning = false;
}

bool
vmu_time_sync_warning_pending(void) {
    return vmu_time_sync_warning;
}

void
vmu_time_sync_warning_dismiss(void) {
    vmu_time_sync_warning = false;
}

#ifdef _arch_dreamcast
static uint8_t vmu_screens_bitmap = 0;

/* check if any VMU is connected; maple_enum_type() can return non-NULL for
 * empty slots so we also check dev->valid */
static bool
has_any_vmu(void) {
    for (int i = 0; i < 8; i++) {
        maple_device_t* dev = maple_enum_type(i, MAPLE_FUNC_MEMCARD);
        if (dev != NULL && dev->valid) {
            return true;
        }
    }
    return false;
}
#endif

/* Which icon set the VMUs get: 0 plain, 1 Dreamcast Now! offline, 2 online. */
static int lcd_variant = 0;
static bool lcd_owned = false;         /* The app draws the LCD itself */
static volatile bool lcd_busy = false; /* The access icon is up for a write */

#if defined(_arch_dreamcast) && OPENMENU_ICONS
static void*
lcd_logo(void) {
    switch (lcd_variant) {
        case 1: return openmenu_lcd_dcnow_off;
        case 2: return openmenu_lcd_dcnow_on;
        default: return openmenu_lcd;
    }
}

static void*
lcd_access(void) {
    switch (lcd_variant) {
        case 1: return openmenu_lcd_access_dcnow_off;
        case 2: return openmenu_lcd_access_dcnow_on;
        default: return openmenu_lcd_access;
    }
}
#endif

void
savefile_defaults() {
    sf_region[0] = REGION_NTSC_U;
    sf_aspect[0] = ASPECT_NORMAL;
    sf_ui[0] = UI_FOLDERS;
    sf_sort[0] = SORT_DEFAULT;
    sf_filter[0] = FILTER_ALL;
    sf_beep[0] = BEEP_OFF;
    sf_multidisc[0] = MULTIDISC_SHOW;
    sf_multidisc_grouping[0] = MULTIDISC_GROUPING_ANYWHERE;
    sf_custom_theme[0] = THEME_OFF;
    sf_custom_theme_num[0] = THEME_0;
    sf_bios_3d[0] = BIOS_3D_STANDARD;
    sf_scroll_art[0] = SCROLL_ART_ON;
    sf_scroll_index[0] = SCROLL_INDEX_ON;
    sf_folders_art[0] = FOLDERS_ART_ON;
    sf_folder_art[0] = FOLDER_ART_ON;
    sf_marquee_speed[0] = MARQUEE_SPEED_MEDIUM;
    sf_mouse_cursor_speed[0] = MOUSE_SPEED_MEDIUM;
    sf_mouse_scroll_speed[0] = MOUSE_SPEED_MEDIUM;
    sf_disc_details[0] = DISC_DETAILS_SHOW;
    sf_folders_item_details[0] = FOLDERS_ITEM_DETAILS_ON;
    sf_clock[0] = CLOCK_12HOUR;
    sf_vm2_send_all[0] = VM2_SEND_ALL;
    sf_boot_mode[0] = BOOT_MODE_FULL;
    sf_vmu_time_sync[0] = VMU_TIME_SYNC_OFF;
    sf_serial_vmu[0] = SERIAL_VMU_OFF;
    sf_serial_vmu_multislot[0] = SERIAL_VMU_MULTISLOT_OFF;
    sf_music[0] = MUSIC_ON;
    sf_honor_defaults[0] = HONOR_DEFAULTS_ON;
    sf_recently_played[0] = RECENTLY_PLAYED_OFF;
    memset(sf_recent_games, 0, sf_recent_games_length);
    sf_remember_last_game[0] = REMEMBER_LAST_GAME_OFF;
    memset(sf_last_game, 0, sf_last_game_length);
    memset(sf_last_game_product, 0, sf_last_game_product_length);
    memset(sf_last_game_folder, 0, sf_last_game_folder_length);
    memset(sf_last_game_filter, 0, sf_last_game_filter_length);
    sf_dcnow[0] = DCNOW_OFF;
    sf_dcnow_refresh[0] = DCNOW_REFRESH_OFF;
    sf_dcnow_vmu[0] = DCNOW_VMU_OFF;
    sf_online_time_sync[0] = ONLINE_TIME_SYNC_OFF;
    sf_clock_extender[0] = CLOCK_EXTENDER_OFF;
}

/* Called by crayon_savefile_deserialise_savedata() when loading a save written
 * by an older version. Never call this directly. */
int8_t
update_savefile(void** loaded_variables, crayon_savefile_version_t loaded_version,
                crayon_savefile_version_t latest_version) {
    if (loaded_version < latest_version) {
        savefile_was_migrated = true;
    }

    if (loaded_version < SFV_BIOS_3D) {
        sf_bios_3d[0] = BIOS_3D_STANDARD;
    }
    if (loaded_version < SFV_SCROLL_ART) {
        sf_scroll_art[0] = SCROLL_ART_ON;
    }
    if (loaded_version < SFV_SCROLL_INDEX) {
        sf_scroll_index[0] = SCROLL_INDEX_ON;
    }
    if (loaded_version < SFV_FOLDERS_ART) {
        sf_folders_art[0] = FOLDERS_ART_ON;
    }
    if (loaded_version < SFV_MARQUEE_SPEED) {
        sf_marquee_speed[0] = MARQUEE_SPEED_MEDIUM;
    }
    if (loaded_version < SFV_DISC_DETAILS) {
        sf_disc_details[0] = DISC_DETAILS_SHOW;
    }
    if (loaded_version < SFV_FOLDERS_ITEM_DETAILS) {
        sf_folders_item_details[0] = FOLDERS_ITEM_DETAILS_ON;
    }
    if (loaded_version < SFV_CLOCK) {
        sf_clock[0] = CLOCK_12HOUR;
    }
    if (loaded_version < SFV_MULTIDISC_GROUPING) {
        sf_multidisc_grouping[0] = MULTIDISC_GROUPING_ANYWHERE;
    }
    if (loaded_version < SFV_VM2_SEND_ALL) {
        sf_vm2_send_all[0] = VM2_SEND_ALL;
    }
    if (loaded_version < SFV_BOOT_MODE) {
        sf_boot_mode[0] = BOOT_MODE_FULL;
    }
    if (loaded_version < SFV_VMU_TIME_SYNC) {
        sf_vmu_time_sync[0] = VMU_TIME_SYNC_OFF;
    }
    if (loaded_version < SFV_SERIAL_VMU) {
        sf_serial_vmu[0] = SERIAL_VMU_OFF;
    }
    if (loaded_version < SFV_SERIAL_VMU_MULTISLOT) {
        sf_serial_vmu_multislot[0] = SERIAL_VMU_MULTISLOT_OFF;
    }
    if (loaded_version < SFV_EXIT_BIOS) {
        sf_bios_3d[0] = BIOS_3D_STANDARD;
    }
    if (loaded_version < SFV_FOLDER_ART) {
        sf_folder_art[0] = FOLDER_ART_ON;
    }
    if (loaded_version < SFV_MUSIC) {
        sf_music[0] = MUSIC_ON;
    }
    if (loaded_version < SFV_HONOR_DEFAULTS) {
        sf_honor_defaults[0] = HONOR_DEFAULTS_ON;
    }
    if (loaded_version < SFV_RECENTLY_PLAYED) {
        sf_recently_played[0] = RECENTLY_PLAYED_OFF;
        memset(sf_recent_games, 0, sf_recent_games_length);
    }
    if (loaded_version < SFV_REMEMBER_LAST_GAME) {
        sf_remember_last_game[0] = REMEMBER_LAST_GAME_OFF;
        memset(sf_last_game, 0, sf_last_game_length);
        memset(sf_last_game_product, 0, sf_last_game_product_length);
        memset(sf_last_game_folder, 0, sf_last_game_folder_length);
        memset(sf_last_game_filter, 0, sf_last_game_filter_length);
    }
    if (loaded_version < SFV_DCNOW) {
        sf_dcnow[0] = DCNOW_OFF;
        sf_dcnow_refresh[0] = DCNOW_REFRESH_OFF;
    }
    if (loaded_version < SFV_DCNOW_VMU) {
        sf_dcnow_vmu[0] = DCNOW_VMU_OFF;
    }
    if (loaded_version < SFV_ONLINE_TIME_SYNC) {
        sf_online_time_sync[0] = ONLINE_TIME_SYNC_OFF;
    }
    if (loaded_version < SFV_MOUSE_SPEEDS) {
        sf_mouse_cursor_speed[0] = MOUSE_SPEED_MEDIUM;
        sf_mouse_scroll_speed[0] = MOUSE_SPEED_MEDIUM;
    }
    if (loaded_version < SFV_CLOCK_EXTENDER) {
        sf_clock_extender[0] = CLOCK_EXTENDER_OFF;
    }
    return 0;
}

/* setup_savefile with optional LCD skip (avoids maple hang if no VMU connected) */
static uint8_t
setup_savefile_internal(crayon_savefile_details_t* details, bool skip_vmu_lcd) {
    uint8_t error;

#if defined(_arch_pc)
    crayon_savefile_set_base_path("saves/");
#else
    crayon_savefile_set_base_path(NULL); /* Dreamcast ignores the parameter and assumes
                                          * "/vmu/", so this is fine on every platform. */
#endif
    error =
        crayon_savefile_init_savefile_details(details, "OPENMENU.SYS", SFV_CURRENT, savefile_defaults, update_savefile);

    error += crayon_savefile_set_app_id(details, "openMenu");
    error += crayon_savefile_set_short_desc(details, "openMenu Config");
    error += crayon_savefile_set_long_desc(details, "openMenu Preferences");

    if (error) {
        return 1;
    }

#if defined(_arch_dreamcast) && OPENMENU_ICONS
    if (!skip_vmu_lcd) {
        vmu_screens_bitmap = crayon_peripheral_dreamcast_get_screens();
        crayon_peripheral_vmu_display_icon(vmu_screens_bitmap, lcd_logo());
    }

    savefile_details.icon_anim_count = OPENMENU_ICONS;
    savefile_details.icon_anim_speed = 1;
    savefile_details.icon_data = OPENMENU_ICON;
    savefile_details.icon_palette = (unsigned short*)OPENMENU_PAL;
#else
    (void)skip_vmu_lcd;
#endif

    crayon_savefile_add_variable(details, &sf_region, sf_region_type, sf_region_length, SFV_INITIAL, VAR_STILL_PRESENT);
    crayon_savefile_add_variable(details, &sf_aspect, sf_aspect_type, sf_aspect_length, SFV_INITIAL, VAR_STILL_PRESENT);
    crayon_savefile_add_variable(details, &sf_ui, sf_ui_type, sf_ui_length, SFV_INITIAL, VAR_STILL_PRESENT);
    crayon_savefile_add_variable(details, &sf_sort, sf_sort_type, sf_sort_length, SFV_INITIAL, VAR_STILL_PRESENT);
    crayon_savefile_add_variable(details, &sf_filter, sf_filter_type, sf_filter_length, SFV_INITIAL, VAR_STILL_PRESENT);
    crayon_savefile_add_variable(details, &sf_beep, sf_beep_type, sf_beep_length, SFV_INITIAL, VAR_STILL_PRESENT);
    crayon_savefile_add_variable(details, &sf_multidisc, sf_multidisc_type, sf_multidisc_length, SFV_INITIAL,
                                 VAR_STILL_PRESENT);
    crayon_savefile_add_variable(details, &sf_custom_theme, sf_custom_theme_type, sf_custom_theme_length, SFV_INITIAL,
                                 VAR_STILL_PRESENT);
    crayon_savefile_add_variable(details, &sf_custom_theme_num, sf_custom_theme_num_type, sf_custom_theme_num_length,
                                 SFV_INITIAL, VAR_STILL_PRESENT);
    crayon_savefile_add_variable(details, &sf_bios_3d, sf_bios_3d_type, sf_bios_3d_length, SFV_BIOS_3D,
                                 VAR_STILL_PRESENT);
    crayon_savefile_add_variable(details, &sf_scroll_art, sf_scroll_art_type, sf_scroll_art_length, SFV_SCROLL_ART,
                                 VAR_STILL_PRESENT);
    crayon_savefile_add_variable(details, &sf_scroll_index, sf_scroll_index_type, sf_scroll_index_length,
                                 SFV_SCROLL_INDEX, VAR_STILL_PRESENT);
    crayon_savefile_add_variable(details, &sf_folders_art, sf_folders_art_type, sf_folders_art_length, SFV_FOLDERS_ART,
                                 VAR_STILL_PRESENT);
    crayon_savefile_add_variable(details, &sf_marquee_speed, sf_marquee_speed_type, sf_marquee_speed_length,
                                 SFV_MARQUEE_SPEED, VAR_STILL_PRESENT);
    crayon_savefile_add_variable(details, &sf_disc_details, sf_disc_details_type, sf_disc_details_length,
                                 SFV_DISC_DETAILS, VAR_STILL_PRESENT);
    crayon_savefile_add_variable(details, &sf_folders_item_details, sf_folders_item_details_type,
                                 sf_folders_item_details_length, SFV_FOLDERS_ITEM_DETAILS, VAR_STILL_PRESENT);
    crayon_savefile_add_variable(details, &sf_clock, sf_clock_type, sf_clock_length, SFV_CLOCK, VAR_STILL_PRESENT);
    crayon_savefile_add_variable(details, &sf_multidisc_grouping, sf_multidisc_grouping_type,
                                 sf_multidisc_grouping_length, SFV_MULTIDISC_GROUPING, VAR_STILL_PRESENT);
    crayon_savefile_add_variable(details, &sf_vm2_send_all, sf_vm2_send_all_type, sf_vm2_send_all_length,
                                 SFV_VM2_SEND_ALL, VAR_STILL_PRESENT);
    crayon_savefile_add_variable(details, &sf_boot_mode, sf_boot_mode_type, sf_boot_mode_length, SFV_BOOT_MODE,
                                 VAR_STILL_PRESENT);
    crayon_savefile_add_variable(details, &sf_vmu_time_sync, sf_vmu_time_sync_type, sf_vmu_time_sync_length,
                                 SFV_VMU_TIME_SYNC, VAR_STILL_PRESENT);
    crayon_savefile_add_variable(details, &sf_serial_vmu, sf_serial_vmu_type, sf_serial_vmu_length, SFV_SERIAL_VMU,
                                 VAR_STILL_PRESENT);
    crayon_savefile_add_variable(details, &sf_serial_vmu_multislot, sf_serial_vmu_multislot_type,
                                 sf_serial_vmu_multislot_length, SFV_SERIAL_VMU_MULTISLOT, VAR_STILL_PRESENT);
    crayon_savefile_add_variable(details, &sf_folder_art, sf_folder_art_type, sf_folder_art_length, SFV_FOLDER_ART,
                                 VAR_STILL_PRESENT);
    crayon_savefile_add_variable(details, &sf_music, sf_music_type, sf_music_length, SFV_MUSIC, VAR_STILL_PRESENT);
    crayon_savefile_add_variable(details, &sf_honor_defaults, sf_honor_defaults_type, sf_honor_defaults_length,
                                 SFV_HONOR_DEFAULTS, VAR_STILL_PRESENT);
    crayon_savefile_add_variable(details, &sf_recently_played, sf_recently_played_type, sf_recently_played_length,
                                 SFV_RECENTLY_PLAYED, VAR_STILL_PRESENT);
    crayon_savefile_add_variable(details, &sf_recent_games, sf_recent_games_type, sf_recent_games_length,
                                 SFV_RECENTLY_PLAYED, VAR_STILL_PRESENT);
    crayon_savefile_add_variable(details, &sf_remember_last_game, sf_remember_last_game_type,
                                 sf_remember_last_game_length, SFV_REMEMBER_LAST_GAME, VAR_STILL_PRESENT);
    crayon_savefile_add_variable(details, &sf_last_game, sf_last_game_type, sf_last_game_length, SFV_REMEMBER_LAST_GAME,
                                 VAR_STILL_PRESENT);
    crayon_savefile_add_variable(details, &sf_last_game_product, sf_last_game_product_type, sf_last_game_product_length,
                                 SFV_REMEMBER_LAST_GAME, VAR_STILL_PRESENT);
    crayon_savefile_add_variable(details, &sf_last_game_folder, sf_last_game_folder_type, sf_last_game_folder_length,
                                 SFV_REMEMBER_LAST_GAME, VAR_STILL_PRESENT);
    crayon_savefile_add_variable(details, &sf_last_game_filter, sf_last_game_filter_type, sf_last_game_filter_length,
                                 SFV_REMEMBER_LAST_GAME, VAR_STILL_PRESENT);
    crayon_savefile_add_variable(details, &sf_dcnow, sf_dcnow_type, sf_dcnow_length, SFV_DCNOW, VAR_STILL_PRESENT);
    crayon_savefile_add_variable(details, &sf_dcnow_refresh, sf_dcnow_refresh_type, sf_dcnow_refresh_length, SFV_DCNOW,
                                 VAR_STILL_PRESENT);
    crayon_savefile_add_variable(details, &sf_dcnow_vmu, sf_dcnow_vmu_type, sf_dcnow_vmu_length, SFV_DCNOW_VMU,
                                 VAR_STILL_PRESENT);
    crayon_savefile_add_variable(details, &sf_online_time_sync, sf_online_time_sync_type, sf_online_time_sync_length,
                                 SFV_ONLINE_TIME_SYNC, VAR_STILL_PRESENT);
    crayon_savefile_add_variable(details, &sf_mouse_cursor_speed, sf_mouse_cursor_speed_type,
                                 sf_mouse_cursor_speed_length, SFV_MOUSE_SPEEDS, VAR_STILL_PRESENT);
    crayon_savefile_add_variable(details, &sf_mouse_scroll_speed, sf_mouse_scroll_speed_type,
                                 sf_mouse_scroll_speed_length, SFV_MOUSE_SPEEDS, VAR_STILL_PRESENT);
    crayon_savefile_add_variable(details, &sf_clock_extender, sf_clock_extender_type, sf_clock_extender_length,
                                 SFV_CLOCK_EXTENDER, VAR_STILL_PRESENT);

    if (crayon_savefile_solidify(details)) {
        return 1;
    }

    return 0;
}

uint8_t
setup_savefile(crayon_savefile_details_t* details) {
    return setup_savefile_internal(details, false);
}

int8_t
find_first_valid_savefile_device(crayon_savefile_details_t* details) {
    int8_t err = -1;
    for (int8_t i = 0; i < CRAYON_SF_NUM_SAVE_DEVICES; ++i) {
        err = crayon_savefile_set_device(details, i);
        if (!err) {
            break;
        }
    }
    return err;
}

void
savefile_init() {
    loaded_from_sd = false;

#ifdef _arch_dreamcast
    /* DEBUG: Dark Blue (0,0,128) = before setup_savefile_internal */
    DFLASH_SF(0, 0, 128);

    /* init savefile (skip VMU LCD for now, updated after VMU check below) */
    BT_STEP("VMU save setup");
    uint8_t setup_res = setup_savefile_internal(&savefile_details, true);
    BT_DONE("%d", setup_res);

    /* DEBUG: Dark Yellow (128,128,0) = after setup_savefile_internal */
    DFLASH_SF(128, 128, 0);

    /* DEBUG: Dark Cyan (0,128,128) = before sd_savefile_init */
    DFLASH_SF(0, 128, 128);

    /* init SD first; successful SD load skips VMU detection (avoids maple hang if no VMU) */
    sd_savefile_init();

    /* DEBUG: Dark Magenta (128,0,128) = after sd_savefile_init */
    DFLASH_SF(128, 0, 128);

    /* SD wins over VMU when both hold a save. */
    if (sd_savefile_available()) {
        SD_STATUS status = sd_savefile_get_status();
        if (status == SD_STATUS_READY || status == SD_STATUS_OLD || status == SD_STATUS_FUTURE) {
            BT_STEP("SD load settings");
            int8_t sd_load_res = sd_savefile_load();
            BT_DONE("%d", sd_load_res);
            if (sd_load_res == 0) {
                loaded_from_sd = true;
                startup_device_id = -1; /* Not a VMU */

                /* SD load successful - still check for VMU for LCD icon and time sync */
                /* DEBUG: Dark Red (128,0,0) = before has_any_vmu (SD path) */
                DFLASH_SF(128, 0, 0);

                BT_STEP("VMU detect");
                bool sd_path_vmu = has_any_vmu();
                BT_DONE("%s", sd_path_vmu ? "found" : "none");
                if (sd_path_vmu) {
                    /* DEBUG: Dark Green (0,128,0) = after has_any_vmu, VMU found (SD path) */
                    DFLASH_SF(0, 128, 0);
#if OPENMENU_ICONS
                    BT_STEP("VMU LCD icon");
                    vmu_screens_bitmap = crayon_peripheral_dreamcast_get_screens();
                    crayon_peripheral_vmu_display_icon(vmu_screens_bitmap, lcd_logo());
                    BT_DONE("ok");
#endif
                    if (sf_vmu_time_sync[0] == VMU_TIME_SYNC_ON) {
                        BT_STEP("VMU clock sync");
                        int8_t sync_res = sync_rtc_from_vmu();
                        BT_DONE("%d", sync_res);
                    }
                } else {
                    /* DEBUG: Orange (255,128,0) = after has_any_vmu, no VMU (SD path) */
                    DFLASH_SF(255, 128, 0);
                }
                return; /* Done - loaded from SD */
            }
        }
    }

    /* No usable SD save, so fall back to the VMU. */
    /* DEBUG: Dark Red (128,0,0) = before has_any_vmu */
    DFLASH_SF(128, 0, 0);

    BT_STEP("VMU detect");
    bool vmu_present = has_any_vmu();
    BT_DONE("%s", vmu_present ? "found" : "none");

    /* DEBUG: Dark Green (0,128,0) = after has_any_vmu (VMU found)
     *        Orange (255,128,0) = after has_any_vmu (no VMU) */
    if (vmu_present) {
        DFLASH_SF(0, 128, 0);
    } else {
        DFLASH_SF(255, 128, 0);
    }

    if (vmu_present) {
        /* DEBUG: Bright Pink (255,128,128) = before find_first_valid_savefile_device */
        DFLASH_SF(255, 128, 128);

        BT_STEP("VMU find save");
        int8_t device_res = find_first_valid_savefile_device(&savefile_details);
        BT_DONE("%d", device_res);

        /* DEBUG: Light Green (128,255,128) = after find_first_valid_savefile_device */
        DFLASH_SF(128, 255, 128);

        if (!setup_res && !device_res) {
            savefile_was_migrated = false;
            BT_STEP("VMU load save");
            int8_t load_res = crayon_savefile_load_savedata(&savefile_details);
            BT_DONE("%d", load_res);

            if (load_res == 0) {
                settings_sanitize();

                startup_device_id = savefile_details.save_device_id;

                /* Only auto-save if migration from older version occurred */
                if (savefile_was_migrated) {
                    crayon_savefile_save_savedata(&savefile_details);
                    savefile_was_migrated = false;
                }

                /* Sync RTC from VMU if enabled */
                if (sf_vmu_time_sync[0] == VMU_TIME_SYNC_ON) {
                    BT_STEP("VMU clock sync");
                    int8_t sync_res = sync_rtc_from_vmu();
                    BT_DONE("%d", sync_res);
                }

                /* VMU is present and valid, show LCD icon */
#if OPENMENU_ICONS
                BT_STEP("VMU LCD icon");
                vmu_screens_bitmap = crayon_peripheral_dreamcast_get_screens();
                crayon_peripheral_vmu_display_icon(vmu_screens_bitmap, lcd_logo());
                BT_DONE("ok");
#endif

                return; /* Done - loaded from VMU */
            }
            /* VMU device exists but no save file on it - show LCD icon anyway,
             * then fall through to defaults */
#if OPENMENU_ICONS
            BT_STEP("VMU LCD icon");
            vmu_screens_bitmap = crayon_peripheral_dreamcast_get_screens();
            crayon_peripheral_vmu_display_icon(vmu_screens_bitmap, lcd_logo());
            BT_DONE("ok");
#endif
        } else {
            /* VMU present but find_first_valid_savefile_device failed -
             * still show LCD icon since we know VMU exists */
#if OPENMENU_ICONS
            BT_STEP("VMU LCD icon");
            vmu_screens_bitmap = crayon_peripheral_dreamcast_get_screens();
            crayon_peripheral_vmu_display_icon(vmu_screens_bitmap, lcd_logo());
            BT_DONE("ok");
#endif
        }
    }
#else
    /* Non-Dreamcast: just set up savefile and try to load */
    uint8_t setup_res = setup_savefile(&savefile_details);
    int8_t device_res = find_first_valid_savefile_device(&savefile_details);

    if (!setup_res && !device_res) {
        savefile_was_migrated = false;
        int8_t load_res = crayon_savefile_load_savedata(&savefile_details);

        if (load_res == 0) {
            settings_sanitize();
            startup_device_id = savefile_details.save_device_id;

            if (savefile_was_migrated) {
                crayon_savefile_save_savedata(&savefile_details);
                savefile_was_migrated = false;
            }
            return;
        }
    }
#endif

    savefile_defaults();
    settings_sanitize();
}

void
savefile_close() {
    crayon_savefile_free_details(&savefile_details);
    crayon_savefile_free_base_path();

#ifdef _arch_dreamcast
    sd_savefile_shutdown();
#endif
}

int8_t
vmu_beep(int8_t save_device_id, uint32_t beep) {
    if (sf_beep[0] != BEEP_ON) {
        return 0;
    }

#ifdef _arch_dreamcast
    maple_device_t* vmu;

    vec2_s8_t port_and_slot = crayon_peripheral_dreamcast_get_port_and_slot(save_device_id);

    /* Invalid controller or port. */
    if (port_and_slot.x < 0) {
        return -1;
    }

    /* Make sure a device is actually in that port and slot. */
    if (!((vmu = maple_enum_dev(port_and_slot.x, port_and_slot.y)))) {
        return -1;
    }

    /* The device has to be valid and expose the function being asked for. */
    if (!vmu->valid) {
        return -1;
    }

    vmu_beep_raw(vmu, beep);
#endif

    return 0;
}

#if defined(_arch_dreamcast) && OPENMENU_ICONS
/* Restores the VMU icon after the launch animation has had time to finish. */
static void*
vmu_icon_restore_thread(void* param) {
    (void)param;
    thd_sleep(1500); /* 1.5 seconds */
    if (!lcd_owned) {
        crayon_peripheral_vmu_display_icon(vmu_screens_bitmap, lcd_logo());
    }
    lcd_busy = false;
    return NULL;
}
#endif

#ifdef _arch_dreamcast
#define DC_EPOCH_DELTA   631152000LL
#define CLOCK_LAST_DATE  0xffd872ffULL
#define CLOCK_PART_START 0x1c000
#define CLOCK_PART_SIZE  0x4000
#define CLOCK_BITMAP     0x3fc0
#define CLOCK_SLOTS      254

static unsigned checkpoint_generation;

static void
clock_storage_full(void) {
    clock_storage_is_full = true;
    clock_storage_warning = true;
}

static bool
clock_date(time_t local_time, uint32_t* date) {
    int64_t seconds = (int64_t)local_time;
    if (seconds < 0 || (uint64_t)seconds > CLOCK_LAST_DATE - DC_EPOCH_DELTA) {
        return false;
    }
    *date = (uint32_t)(seconds + DC_EPOCH_DELTA);
    return true;
}

static uint16_t
calc_flashrom_crc(const uint8_t* buffer) {
    uint32_t crc = 0xffff;
    for (int i = 0; i < 62; i++) {
        crc ^= (uint32_t)buffer[i] << 8;
        for (int bit = 0; bit < 8; bit++) {
            crc = ((crc << 1) ^ ((crc & 0x8000) ? 0x1021u : 0u)) & 0xffff;
        }
    }
    return (uint16_t)~crc;
}

static bool
clock_read(int offset, uint8_t* data, int size) {
    uint8_t verify[64];
    if (size <= 0 || size > 64 || offset < CLOCK_PART_START || offset > CLOCK_PART_START + CLOCK_PART_SIZE - size) {
        return false;
    }
    /* Different fills expose partial reads and no-op success returns. */
    memset(data, 0xa5, size);
    memset(verify, 0x5a, size);
    int first = flashrom_read(offset, data, size);
    int second = flashrom_read(offset, verify, size);
    return (first == 0 || first == size) && (second == 0 || second == size) && memcmp(data, verify, size) == 0;
}

static bool
clock_scan(const uint8_t* part, int* used, int* latest) {
    const uint8_t* bitmap = part + CLOCK_BITMAP;
    bool seen_free = false;
    *used = 0;
    *latest = 0;
    if (memcmp(part, "KATANA_FLASH____", 16) != 0 || part[16] != 2 || part[17] != 0) {
        return false;
    }
    for (int bit = 0; bit < CLOCK_SLOTS; bit++) {
        const uint8_t* record = part + (bit + 1) * 64;
        if (bitmap[bit / 8] & (0x80 >> (bit % 8))) {
            seen_free = true;
            for (int i = 0; i < 64; i++) {
                if (record[i] != 0xff) {
                    return false;
                }
            }
        } else {
            if (seen_free) {
                return false;
            }
            (*used)++;
            /* Interrupted appends consume a slot but do not replace a valid record. */
            uint16_t crc = (uint16_t)record[62] | ((uint16_t)record[63] << 8);
            if (record[0] == FLASHROM_B1_SYSCFG && record[1] == 0 && crc == calc_flashrom_crc(record)) {
                *latest = bit + 1;
            }
        }
    }
    for (int bit = CLOCK_SLOTS; bit < 64 * 8; bit++) {
        if (!(bitmap[bit / 8] & (0x80 >> (bit % 8)))) {
            return false;
        }
    }
    return *latest != 0;
}

static int8_t
clock_prepare(uint8_t** part, int* used, int* latest) {
    int start = -1, size = -1;
    *part = NULL;
    if (clock_storage_is_full) {
        return CLOCK_SYNC_STORAGE_FULL;
    }
    if (flashrom_info(FLASHROM_PT_BLOCK_1, &start, &size) != 0 || start != CLOCK_PART_START
        || size != CLOCK_PART_SIZE) {
        return CLOCK_SYNC_STORAGE_ERROR;
    }
    *part = malloc(CLOCK_PART_SIZE);
    if (*part == NULL) {
        return CLOCK_SYNC_STORAGE_ERROR;
    }
    for (int offset = 0; offset < CLOCK_PART_SIZE; offset += 64) {
        if (!clock_read(start + offset, *part + offset, 64)) {
            free(*part);
            *part = NULL;
            return CLOCK_SYNC_STORAGE_ERROR;
        }
    }
    if (!clock_scan(*part, used, latest)) {
        free(*part);
        *part = NULL;
        return CLOCK_SYNC_STORAGE_ERROR;
    }
    if (*used == CLOCK_SLOTS) {
        free(*part);
        *part = NULL;
        clock_storage_full();
        return CLOCK_SYNC_STORAGE_FULL;
    }
    return CLOCK_SYNC_OK;
}

static bool
clock_verify_partition(const uint8_t* expected) {
    uint8_t actual[64];
    for (int offset = 0; offset < CLOCK_PART_SIZE; offset += 64) {
        if (!clock_read(CLOCK_PART_START + offset, actual, 64) || memcmp(actual, expected + offset, 64) != 0) {
            return false;
        }
    }
    return true;
}

static bool
clock_program(uint8_t* expected, int offset, uint8_t* data, int size) {
    bool bitmap = size == 1 && offset >= CLOCK_BITMAP && offset < CLOCK_BITMAP + (CLOCK_SLOTS + 7) / 8;
    bool body = size == 62 && offset >= 66 && offset <= CLOCK_BITMAP - 62 && offset % 64 == 2;
    bool id = size == 2 && offset >= 64 && offset <= CLOCK_BITMAP - 64 && offset % 64 == 0;
    if ((!bitmap && !body && !id) || flashrom_write(CLOCK_PART_START + offset, data, size) != size) {
        return false;
    }
    memcpy(expected + offset, data, size);
    return clock_verify_partition(expected);
}

static bool
clock_append(uint8_t* part, int used, int latest, uint32_t date) {
    uint8_t record[64];
    memcpy(record, part + latest * 64, sizeof(record));
    for (int i = 0; i < 4; i++) {
        record[2 + i] = date >> (i * 8);
    }
    uint16_t crc = calc_flashrom_crc(record);
    record[62] = crc;
    record[63] = crc >> 8;
    int offset = (used + 1) * 64;
    int bitmap_offset = CLOCK_BITMAP + used / 8;
    uint8_t bitmap = part[bitmap_offset] & ~(0x80 >> (used % 8));
    if (!clock_program(part, bitmap_offset, &bitmap, 1)) {
        return false;
    }
    if (used + 1 == CLOCK_SLOTS) {
        clock_storage_full();
    }
    if (!clock_program(part, offset + 2, record + 2, 62) || !clock_program(part, offset, record, 2)) {
        return false;
    }
    int final_used, final_latest;
    if (!clock_scan(part, &final_used, &final_latest) || final_used != used + 1 || final_latest != used + 1
        || memcmp(part + final_latest * 64, record, sizeof(record)) != 0) {
        return false;
    }
    checkpoint_generation++;
    return true;
}

int8_t
set_rtc_and_syscfg(time_t local_time) {
    uint32_t date;
    uint8_t* part;
    int used, latest;
    if (clock_storage_is_full) {
        return CLOCK_SYNC_STORAGE_FULL;
    }
    if (!clock_date(local_time, &date)) {
        return CLOCK_SYNC_INVALID_TIME;
    }
    int8_t result = clock_prepare(&part, &used, &latest);
    if (result != CLOCK_SYNC_OK) {
        return result;
    }
    if (rtc_set_unix_secs(local_time) != 0) {
        free(part);
        return CLOCK_SYNC_RTC_FAILED;
    }
    bool saved = clock_append(part, used, latest, date);
    free(part);
    return saved ? CLOCK_SYNC_OK : CLOCK_SYNC_CHECKPOINT_FAILED;
}

void
clock_extender_tick(void) {
    static int seen_setting = -1;
    static unsigned seen_generation;
    int setting = sf_clock_extender[0];
    bool extend = setting == CLOCK_EXTENDER_ON && setting != seen_setting
                  && (seen_setting == -1 ? checkpoint_generation == 0 : checkpoint_generation == seen_generation);
    seen_setting = setting;
    if (extend && !clock_storage_is_full) {
        uint32_t date;
        uint8_t* part;
        int used, latest;
        if (clock_date(rtc_unix_secs(), &date) && clock_prepare(&part, &used, &latest) == CLOCK_SYNC_OK) {
            clock_append(part, used, latest, date);
            free(part);
        }
    }
    seen_generation = checkpoint_generation;
}

static bool
vmu_decode_datetime(const uint8_t* reply, time_t* result) {
    uint32_t function;
    if (reply[0] != MAPLE_RESPONSE_DATATRF || reply[3] != 3) {
        return false;
    }
    memcpy(&function, reply + 4, sizeof(function));
    if (function != MAPLE_FUNC_CLOCK) {
        return false;
    }

    const uint8_t* dt = reply + 8;
    unsigned year = dt[0] | (dt[1] << 8);
    unsigned month = dt[2];
    if (year > 9999 || month < 1 || month > 12 || dt[4] > 23 || dt[5] > 59 || dt[6] > 59) {
        return false;
    }
    static const uint8_t month_days[] = {31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31};
    unsigned days = month_days[month - 1];
    if (month == 2 && year % 4 == 0 && (year % 100 != 0 || year % 400 == 0)) {
        days++;
    }
    if (dt[3] < 1 || dt[3] > days) {
        return false;
    }

    /* The weekday is calculated from the date, including for VM2 replies. */
    struct tm local = {0};
    local.tm_year = (int)year - 1900;
    local.tm_mon = month - 1;
    local.tm_mday = dt[3];
    local.tm_hour = dt[4];
    local.tm_min = dt[5];
    local.tm_sec = dt[6];
    *result = mktime(&local);
    return *result != (time_t)-1;
}

static void
vmu_clock_reply(maple_state_t* state, maple_frame_t* frame) {
    (void)state;
    genwait_wake_all(frame);
}

static int
vmu_get_datetime_checked(maple_device_t* dev, time_t* result) {
    *result = (time_t)-1;
    for (unsigned attempt = 0; attempt < 3; attempt++) {
        if (!dev->valid
            || (dev->info.functions & (MAPLE_FUNC_MEMCARD | MAPLE_FUNC_CLOCK))
                   != (MAPLE_FUNC_MEMCARD | MAPLE_FUNC_CLOCK)) {
            return MAPLE_EFAIL;
        }
        if (maple_frame_lock(&dev->frame) == 0) {
            break;
        }
        if (attempt == 2) {
            return MAPLE_EAGAIN;
        }
        thd_sleep(20);
    }

    maple_frame_t* frame = &dev->frame;
    maple_frame_init(frame);
    /* KOS resends this payload when a device replies with AGAIN. */
    uint32_t send[2] = {MAPLE_FUNC_CLOCK, 0};
    frame->cmd = MAPLE_COMMAND_BREAD;
    frame->dst_port = dev->port;
    frame->dst_unit = dev->unit;
    frame->length = 2;
    frame->callback = vmu_clock_reply;
    frame->send_buf = send;

    /* Queue and sleep atomically so an early reply cannot miss the waiter. */
    uint32_t irq = irq_disable();
    maple_queue_frame(frame);
    int waited = genwait_wait(frame, "vmu_clock_read", 10000, NULL);
    uint8_t reply[16];
    bool received = frame->state == MAPLE_FRAME_RESPONDED;
    if (received) {
        memcpy(reply, frame->recv_buf, sizeof(reply));
        maple_frame_unlock(frame);
    } else {
        maple_queue_remove(frame);
        frame->state = MAPLE_FRAME_VACANT;
    }
    frame->callback = NULL;
    frame->send_buf = NULL;
    irq_restore(irq);

    if (!received) {
        return waited < 0 ? MAPLE_ETIMEOUT : MAPLE_EFAIL;
    }
    return vmu_decode_datetime(reply, result) ? MAPLE_EOK : MAPLE_EFAIL;
}

/* Checkpoint failures are separate from an incompatible VMU warning. */
int8_t
sync_rtc_from_vmu(void) {
    vmu_time_sync_warning = false;
#if DEBUG_VMU_SYNC
    return vmu_sync_debug_query();
#else
    if (clock_sync_storage_full()) {
        return CLOCK_SYNC_STORAGE_FULL;
    }
    bool attached = false;
    bool received_time = false;
    for (int i = 0; i < 8; i++) {
        maple_device_t* dev = maple_enum_type(i, MAPLE_FUNC_MEMCARD);
        if (dev == NULL || !dev->valid) {
            continue;
        }
        attached = true;

        /* Check if device has clock function */
        if (!(dev->info.functions & MAPLE_FUNC_CLOCK)) {
            continue;
        }

        /* Try to get VMU time */
        time_t vmu_time;
        int result = vmu_get_datetime_checked(dev, &vmu_time);
        if (result != MAPLE_EOK || vmu_time == (time_t)-1) {
            continue;
        }
        received_time = true;

        /* Set Dreamcast RTC */
        int8_t sync_result = set_rtc_and_syscfg(vmu_time);
        if (sync_result != CLOCK_SYNC_RTC_FAILED && sync_result != CLOCK_SYNC_INVALID_TIME) {
            return sync_result;
        }
    }
    vmu_time_sync_warning = attached && !received_time;
    return -1; /* No suitable VMU found or sync failed */
#endif
}

#else
/* Non-Dreamcast stub */
int8_t
sync_rtc_from_vmu(void) {
    return -1;
}

int8_t
set_rtc_and_syscfg(time_t local_time) {
    (void)local_time;
    return CLOCK_SYNC_RTC_FAILED;
}

void
clock_extender_tick(void) {}
#endif

int8_t
savefile_save() {
    settings_sanitize();

#if defined(_arch_dreamcast) && OPENMENU_ICONS
    /* Show access icon on target device before operation */
    if (vmu_screens_bitmap != 0) {
        uint8_t single_device = (1 << savefile_details.save_device_id) & vmu_screens_bitmap;
        if (single_device) {
            lcd_busy = true;
            crayon_peripheral_vmu_display_icon(single_device, lcd_access());
        }
    }
#endif

    vmu_beep(savefile_details.save_device_id, 0x000065f0); // Turn on beep (if enabled)
    int8_t result = crayon_savefile_save_savedata(&savefile_details);
    vmu_beep(savefile_details.save_device_id, 0x00000000); // Turn off beep (if enabled)

#if defined(_arch_dreamcast) && OPENMENU_ICONS
    if (vmu_screens_bitmap != 0) {
        thd_create(0, vmu_icon_restore_thread, NULL);
    }
#endif

    return result;
}

int8_t
savefile_get_device_status(int8_t device_id) {
    return crayon_savefile_save_device_status(&savefile_details, device_id);
}

uint32_t
savefile_get_device_version(int8_t device_id) {
    if (device_id < 0 || device_id >= CRAYON_SF_NUM_SAVE_DEVICES) {
        return 0;
    }
    return savefile_details.savefile_versions[device_id];
}

void
savefile_refresh_device_info(void) {
    crayon_savefile_update_all_device_infos(&savefile_details);
}

void
savefile_refresh_single_device_info(int8_t device_id) {
    crayon_savefile_update_device_info(&savefile_details, device_id);
}

int8_t
savefile_save_to_device(int8_t device_id) {
    int8_t old_device = savefile_details.save_device_id;

    if (crayon_savefile_set_device(&savefile_details, device_id) != 0) {
        savefile_details.save_device_id = old_device;
        return -1;
    }

    settings_sanitize();

#if defined(_arch_dreamcast) && OPENMENU_ICONS
    /* Show access icon on target device before operation */
    if (vmu_screens_bitmap != 0) {
        uint8_t single_device = (1 << device_id) & vmu_screens_bitmap;
        if (single_device) {
            lcd_busy = true;
            crayon_peripheral_vmu_display_icon(single_device, lcd_access());
        }
    }
#endif

    vmu_beep(device_id, 0x000065f0); /* Turn on beep */
    int8_t result = crayon_savefile_save_savedata(&savefile_details);
    vmu_beep(device_id, 0x00000000); /* Turn off beep */

#if defined(_arch_dreamcast) && OPENMENU_ICONS
    if (vmu_screens_bitmap != 0) {
        thd_create(0, vmu_icon_restore_thread, NULL);
    }
#endif

    if (result == 0) {
        /* track where current settings live */
        startup_device_id = device_id;
        loaded_from_sd = false;
    }

    return result;
}

int8_t
savefile_load_from_device(int8_t device_id) {
    int8_t old_device = savefile_details.save_device_id;

    if (crayon_savefile_set_device(&savefile_details, device_id) != 0) {
        savefile_details.save_device_id = old_device;
        return -1;
    }

#if defined(_arch_dreamcast) && OPENMENU_ICONS
    /* Show access icon on target device before operation */
    if (vmu_screens_bitmap != 0) {
        uint8_t single_device = (1 << device_id) & vmu_screens_bitmap;
        if (single_device) {
            lcd_busy = true;
            crayon_peripheral_vmu_display_icon(single_device, lcd_access());
        }
    }
#endif

    savefile_was_migrated = false;
    int8_t result = crayon_savefile_load_savedata(&savefile_details);

#if defined(_arch_dreamcast) && OPENMENU_ICONS
    if (vmu_screens_bitmap != 0) {
        thd_create(0, vmu_icon_restore_thread, NULL);
    }
#endif

    if (result == 0) {
        settings_sanitize();
        /* track where current settings live */
        startup_device_id = device_id;
        loaded_from_sd = false;
    }

    return result;
}

int8_t
savefile_get_startup_device_id(void) {
    return startup_device_id;
}

uint32_t
savefile_get_save_size_blocks(void) {
    uint32_t size_bytes = crayon_savefile_get_savefile_size(&savefile_details);
    /* Convert bytes to 512-byte blocks, rounding up */
    return (size_bytes + 511) / 512;
}

uint32_t
savefile_get_device_free_blocks(int8_t device_id) {
    uint32_t free_bytes = crayon_savefile_devices_free_space(device_id);
    /* Convert bytes to 512-byte blocks */
    return free_bytes / 512;
}

bool
savefile_was_loaded_from_sd(void) {
    return loaded_from_sd;
}

bool
savefile_sd_available(void) {
#ifdef _arch_dreamcast
    return sd_savefile_available();
#else
    return false;
#endif
}

SD_STATUS
savefile_get_sd_status(void) {
#ifdef _arch_dreamcast
    return sd_savefile_get_status();
#else
    return SD_STATUS_NOT_PRESENT;
#endif
}

bool
savefile_sd_unreadable(void) {
#ifdef _arch_dreamcast
    return sd_savefile_get_status() == SD_STATUS_UNREADABLE;
#else
    return false;
#endif
}

uint32_t
savefile_get_sd_version(void) {
#ifdef _arch_dreamcast
    return sd_savefile_get_version();
#else
    return 0;
#endif
}

int8_t
savefile_save_to_sd(void) {
#ifdef _arch_dreamcast
    settings_sanitize();
    int8_t result = sd_savefile_save();
    if (result == 0) {
        /* track where current settings live */
        loaded_from_sd = true;
        startup_device_id = -1;
    }
    return result;
#else
    return -1;
#endif
}

int8_t
savefile_load_from_sd(void) {
#ifdef _arch_dreamcast
    int8_t result = sd_savefile_load();
    if (result == 0) {
        settings_sanitize();
        /* track where current settings live */
        loaded_from_sd = true;
        startup_device_id = -1;
    }
    return result;
#else
    return -1;
#endif
}

void
savefile_refresh_sd_status(void) {
#ifdef _arch_dreamcast
    /* Initialize SD subsystem if not already done */
    if (!sd_savefile_available()) {
        /* sd_savefile_init() calls sd_savefile_refresh_status() internally,
         * so we don't need to call it again after successful init */
        if (sd_savefile_init() == 0) {
            return; /* Status already refreshed by init */
        }
        /* Init failed, status is already set to NOT_PRESENT or UNREADABLE */
        return;
    }
    /* SD already initialized, refresh the status */
    sd_savefile_refresh_status();
#endif
}

/* Every call repaints. The app calls this when the icon changes and again
 * when it hands the LCD back with the same variant. */
void
savefile_set_lcd_variant(int variant) {
    lcd_variant = variant;
#if defined(_arch_dreamcast) && OPENMENU_ICONS
    if (!lcd_owned && !lcd_busy) {
        vmu_screens_bitmap = crayon_peripheral_dreamcast_get_screens();
        crayon_peripheral_vmu_display_icon(vmu_screens_bitmap, lcd_logo());
    }
#endif
}

const void*
savefile_lcd_logo(void) {
#if defined(_arch_dreamcast) && OPENMENU_ICONS
    return lcd_logo();
#else
    return NULL;
#endif
}

const void*
savefile_lcd_access(void) {
#if defined(_arch_dreamcast) && OPENMENU_ICONS
    return lcd_access();
#else
    return NULL;
#endif
}

bool
savefile_lcd_busy(void) {
    return lcd_busy;
}

void
savefile_set_lcd_busy(bool busy) {
    lcd_busy = busy;
}

void
savefile_set_lcd_owner(bool owned) {
    lcd_owned = owned;
}

bool
savefile_lcd_owned(void) {
    return lcd_owned;
}
