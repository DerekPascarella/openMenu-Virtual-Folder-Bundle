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
    uint8_t setup_res = setup_savefile_internal(&savefile_details, true);

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
            if (sd_savefile_load() == 0) {
                loaded_from_sd = true;
                startup_device_id = -1; /* Not a VMU */

                /* SD load successful - still check for VMU for LCD icon and time sync */
                /* DEBUG: Dark Red (128,0,0) = before has_any_vmu (SD path) */
                DFLASH_SF(128, 0, 0);

                if (has_any_vmu()) {
                    /* DEBUG: Dark Green (0,128,0) = after has_any_vmu, VMU found (SD path) */
                    DFLASH_SF(0, 128, 0);
#if OPENMENU_ICONS
                    vmu_screens_bitmap = crayon_peripheral_dreamcast_get_screens();
                    crayon_peripheral_vmu_display_icon(vmu_screens_bitmap, lcd_logo());
#endif
                    if (sf_vmu_time_sync[0] == VMU_TIME_SYNC_ON) {
                        sync_rtc_from_vmu();
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

    bool vmu_present = has_any_vmu();

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

        int8_t device_res = find_first_valid_savefile_device(&savefile_details);

        /* DEBUG: Light Green (128,255,128) = after find_first_valid_savefile_device */
        DFLASH_SF(128, 255, 128);

        if (!setup_res && !device_res) {
            savefile_was_migrated = false;
            int8_t load_res = crayon_savefile_load_savedata(&savefile_details);

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
                    sync_rtc_from_vmu();
                }

                /* VMU is present and valid, show LCD icon */
#if OPENMENU_ICONS
                vmu_screens_bitmap = crayon_peripheral_dreamcast_get_screens();
                crayon_peripheral_vmu_display_icon(vmu_screens_bitmap, lcd_logo());
#endif

                return; /* Done - loaded from VMU */
            }
            /* VMU device exists but no save file on it - show LCD icon anyway,
             * then fall through to defaults */
#if OPENMENU_ICONS
            vmu_screens_bitmap = crayon_peripheral_dreamcast_get_screens();
            crayon_peripheral_vmu_display_icon(vmu_screens_bitmap, lcd_logo());
#endif
        } else {
            /* VMU present but find_first_valid_savefile_device failed -
             * still show LCD icon since we know VMU exists */
#if OPENMENU_ICONS
            vmu_screens_bitmap = crayon_peripheral_dreamcast_get_screens();
            crayon_peripheral_vmu_display_icon(vmu_screens_bitmap, lcd_logo());
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
/* Epoch delta: seconds between Jan 1, 1950 and Jan 1, 1970 */
#define DC_EPOCH_DELTA 631152000

/**
 * CRC calculation for flashrom blocks (matches KOS flashrom_calc_crc).
 * CRC is calculated over the first 62 bytes of the 64-byte block.
 */
static uint16_t
calc_flashrom_crc(const uint8_t* buffer) {
    int i, c, n = 0xffff;

    for (i = 0; i < 62; i++) {
        n ^= buffer[i] << 8;
        for (c = 0; c < 8; c++) {
            if (n & 0x8000) {
                n = (n << 1) ^ 4129;
            } else {
                n = (n << 1);
            }
        }
    }
    return (uint16_t)((~n) & 0xffff);
}

/* update flashrom BLOCK_1/SYSCFG date so BIOS doesn't prompt for time on next boot.
 * writes bitmap before block data; skips physical block 1 (KOS never reads it).
 * returns 0 on success, -1 on failure */
static int8_t
update_flashrom_syscfg_date(time_t unix_time) {
    uint8_t buffer[64];
    int start, size;
    int bmcnt, i;
    uint8_t* bitmap = NULL;
    int first_unused = -1;
    uint32_t dc_time;
    uint16_t crc, verify_crc;
    int8_t rv = -1;
    uint8_t new_bitmap_byte;

    /* Read current syscfg block to preserve other settings */
    if (flashrom_get_block(FLASHROM_PT_BLOCK_1, FLASHROM_B1_SYSCFG, buffer) < 0) {
        return -1;
    }

    /* Verify block_id is correct (should be 5 = FLASHROM_B1_SYSCFG) */
    if (buffer[0] != 0x05 || buffer[1] != 0x00) {
        return -1; /* Unexpected block structure */
    }

    /* Convert Unix time to DC epoch (seconds since Jan 1, 1950) */
    dc_time = (uint32_t)(unix_time + DC_EPOCH_DELTA);

    /* Update the date field at offset 2 (little-endian, 4 bytes) */
    buffer[2] = (dc_time) & 0xFF;
    buffer[3] = (dc_time >> 8) & 0xFF;
    buffer[4] = (dc_time >> 16) & 0xFF;
    buffer[5] = (dc_time >> 24) & 0xFF;

    /* Recalculate CRC and store at offset 62 (little-endian, 2 bytes) */
    crc = calc_flashrom_crc(buffer);
    buffer[62] = crc & 0xFF;
    buffer[63] = (crc >> 8) & 0xFF;

    /* Verify our CRC calculation by reading it back */
    verify_crc = (uint16_t)buffer[62] | ((uint16_t)buffer[63] << 8);
    if (verify_crc != crc) {
        return -1; /* CRC storage failed somehow */
    }

    /* Get partition info */
    if (flashrom_info(FLASHROM_PT_BLOCK_1, &start, &size) != 0) {
        return -1;
    }

    /* Calculate bitmap size (one bit per 64-byte block, rounded to 64 bytes) */
    bmcnt = size / 64;
    bmcnt = (bmcnt + (64 * 8) - 1) & ~(64 * 8 - 1);
    bmcnt = bmcnt / 8;

    if (bmcnt > 65536 || bmcnt <= 0) {
        return -1;
    }

    /* Allocate and read bitmap from end of partition */
    bitmap = (uint8_t*)malloc(bmcnt);
    if (!bitmap) {
        return -1;
    }

    if (flashrom_read(start + size - bmcnt, bitmap, bmcnt) < 0) {
        goto cleanup;
    }

    /* Find first unused physical block (first set bit in bitmap).
     * Bit = 1 means unused (erased flash is all 1s).
     * IMPORTANT: Skip bit 0 - KOS's flashrom_get_block() uses "i > 0" in its
     * read loop, meaning it never checks bitmap bit 0 / physical block 1.
     * If we wrote there, it would never be found! */
    for (i = 1; i < bmcnt * 8; i++) {
        if (bitmap[i / 8] & (0x80 >> (i % 8))) {
            first_unused = i;
            break;
        }
    }

    if (first_unused < 0) {
        /* No free blocks - partition is full. This is extremely rare
         * (partition is 16KB = 256 blocks). Fail gracefully. */
        goto cleanup;
    }

    /* SAFETY: Write bitmap FIRST, then block data.
     * If block write fails after bitmap update, we lose one 64-byte slot
     * but cause no data corruption - the old syscfg remains valid.
     * The alternative order (block then bitmap) risks having an orphaned
     * block that could be overwritten by the next partition write. */

    /* Prepare new bitmap byte with the bit cleared (1->0 = mark as used) */
    new_bitmap_byte = bitmap[first_unused / 8] & ~(0x80 >> (first_unused % 8));

    /* Write updated bitmap byte to flash - syscall returns 0 on success */
    if (flashrom_write(start + size - bmcnt + (first_unused / 8), &new_bitmap_byte, 1) < 0) {
        /* Bitmap update failed - abort without writing block */
        goto cleanup;
    }

    /* Now write the block data to the slot we just reserved.
     * Physical block offset: start + (first_unused + 1) * 64
     * (bit 0 = physical block 1, bit N = physical block N+1) */
    if (flashrom_write(start + (first_unused + 1) * 64, buffer, 64) < 0) {
        /* Block write failed. We've "lost" one slot (it's marked used but
         * has invalid/partial data). This is unfortunate but not corruption.
         * The old syscfg block remains valid and will be found. */
        goto cleanup;
    }

    rv = 0; /* Success */

cleanup:
    free(bitmap);
    return rv;
}

/* Sets the console clock, then the SYSCFG date the BIOS checks at boot so it
 * does not ask for the time. Returns 0 when the clock was set. */
int8_t
set_rtc_and_syscfg(time_t local_time) {
    if (rtc_set_unix_secs(local_time) != 0) {
        return -1;
    }
    /* If the flash ROM write fails the time is still set. The user may just
     * see the BIOS date screen on the next boot. */
    update_flashrom_syscfg_date(local_time);
    return 0;
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

/* Returns 0 when a VMU sets the console clock, or -1 if synchronization fails. */
int8_t
sync_rtc_from_vmu(void) {
    vmu_time_sync_warning = false;
#if DEBUG_VMU_SYNC
    return vmu_sync_debug_query();
#else
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
        if (set_rtc_and_syscfg(vmu_time) == 0) {
            return 0; /* Success */
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
    return -1;
}
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
        /* Init failed, status is already set to NOT_PRESENT */
        return;
    }
    /* SD already initialized, refresh the status */
    sd_savefile_refresh_status();
#endif
}

/* COMPACTION_TEST_START */
#ifdef _arch_dreamcast

#include <stdio.h>

/* Compaction test state */
static int ct_write_count = 0;
static int ct_total_blocks = 0;
static int ct_result = 0; /* 0 = not done, 1 = no compaction, 2 = compaction detected */
static const char* ct_status = "Not started";
static char ct_debug_buf[64] = {0}; /* Debug info buffer */
static uint8_t* ct_backup_data = NULL;
static int ct_backup_start = 0;
static int ct_backup_size = 0;
static bool ct_initialized = false;

/* Count free blocks in partition 2 */
static int
ct_count_free_blocks(int start, int size) {
    int bmcnt = size / 64;
    bmcnt = (bmcnt + (64 * 8) - 1) & ~(64 * 8 - 1);
    bmcnt = bmcnt / 8;

    uint8_t* bitmap = (uint8_t*)malloc(bmcnt);
    if (!bitmap) {
        return -1;
    }

    if (flashrom_read(start + size - bmcnt, bitmap, bmcnt) < 0) {
        free(bitmap);
        return -1;
    }

    int free_count = 0;
    for (int i = 1; i < bmcnt * 8; i++) {
        if (bitmap[i / 8] & (0x80 >> (i % 8))) {
            free_count++;
        }
    }

    free(bitmap);
    return free_count;
}

/* Initialize the compaction test - backup partition to RAM */
int8_t
compaction_test_init(void) {
    int info_ret, read_ret;

    if (ct_initialized) {
        ct_status = "Already running";
        return -1;
    }

    /* Get partition info */
    info_ret = flashrom_info(FLASHROM_PT_BLOCK_1, &ct_backup_start, &ct_backup_size);
    if (info_ret != 0) {
        snprintf(ct_debug_buf, sizeof(ct_debug_buf), "info ret=%d", info_ret);
        ct_status = ct_debug_buf;
        return -1;
    }

    /* Allocate backup buffer */
    ct_backup_data = (uint8_t*)malloc(ct_backup_size);
    if (!ct_backup_data) {
        snprintf(ct_debug_buf, sizeof(ct_debug_buf), "alloc fail sz=%d", ct_backup_size);
        ct_status = ct_debug_buf;
        return -1;
    }

    /* Read entire partition - syscall returns 0 on success, -1 on failure */
    ct_status = "Backing up...";
    read_ret = flashrom_read(ct_backup_start, ct_backup_data, ct_backup_size);
    if (read_ret < 0) {
        snprintf(ct_debug_buf, sizeof(ct_debug_buf), "read ret=%d start=%X sz=%d", read_ret, ct_backup_start,
                 ct_backup_size);
        ct_status = ct_debug_buf;
        free(ct_backup_data);
        ct_backup_data = NULL;
        return -1;
    }

    /* Count initial free blocks */
    ct_total_blocks = ct_count_free_blocks(ct_backup_start, ct_backup_size);
    if (ct_total_blocks <= 0) {
        free(ct_backup_data);
        ct_backup_data = NULL;
        ct_status = "No free blocks";
        return -1;
    }

    ct_write_count = 0;
    ct_result = 0;
    ct_initialized = true;
    ct_status = "Ready";

    return 0;
}

/* Perform one write step - call each frame */
int8_t
compaction_test_step(void) {
    if (!ct_initialized || !ct_backup_data) {
        return -1;
    }

    uint8_t buffer[64];
    int bmcnt, i, first_unused = -1;
    uint8_t* bitmap;
    uint16_t crc;
    uint32_t test_date;

    /* Read current syscfg */
    if (flashrom_get_block(FLASHROM_PT_BLOCK_1, FLASHROM_B1_SYSCFG, buffer) < 0) {
        ct_status = "Read syscfg failed";
        ct_result = 1;
        return 1; /* Done with error */
    }

    /* Update date field with unique test value */
    test_date = 0x50000000 + ct_write_count;
    buffer[2] = (test_date) & 0xFF;
    buffer[3] = (test_date >> 8) & 0xFF;
    buffer[4] = (test_date >> 16) & 0xFF;
    buffer[5] = (test_date >> 24) & 0xFF;

    /* Recalculate CRC */
    crc = calc_flashrom_crc(buffer);
    buffer[62] = crc & 0xFF;
    buffer[63] = (crc >> 8) & 0xFF;

    /* Calculate bitmap size */
    bmcnt = ct_backup_size / 64;
    bmcnt = (bmcnt + (64 * 8) - 1) & ~(64 * 8 - 1);
    bmcnt = bmcnt / 8;

    /* Read bitmap */
    bitmap = (uint8_t*)malloc(bmcnt);
    if (!bitmap) {
        ct_status = "Bitmap alloc failed";
        ct_result = 1;
        return 1;
    }

    if (flashrom_read(ct_backup_start + ct_backup_size - bmcnt, bitmap, bmcnt) < 0) {
        free(bitmap);
        ct_status = "Bitmap read failed";
        ct_result = 1;
        return 1;
    }

    /* Find first unused block (skip bit 0) */
    for (i = 1; i < bmcnt * 8; i++) {
        if (bitmap[i / 8] & (0x80 >> (i % 8))) {
            first_unused = i;
            break;
        }
    }

    if (first_unused < 0) {
        /* Partition full! Check if compaction happened */
        int new_free = ct_count_free_blocks(ct_backup_start, ct_backup_size);
        free(bitmap);

        if (new_free > 5) {
            /* Significant free space appeared - compaction detected! */
            ct_status = "COMPACTION DETECTED!";
            ct_result = 2;
        } else {
            ct_status = "NO compaction";
            ct_result = 1;
        }
        return 1; /* Done */
    }

    /* Update status */
    ct_status = "Writing...";

    /* Write bitmap byte first (mark slot as used) */
    uint8_t new_bm_byte = bitmap[first_unused / 8] & ~(0x80 >> (first_unused % 8));
    free(bitmap);

    /* Syscalls return 0 on success, -1 on failure */
    if (flashrom_write(ct_backup_start + ct_backup_size - bmcnt + (first_unused / 8), &new_bm_byte, 1) < 0) {
        ct_status = "Bitmap write failed";
        ct_result = 1;
        return 1;
    }

    /* Write block data */
    if (flashrom_write(ct_backup_start + (first_unused + 1) * 64, buffer, 64) < 0) {
        ct_status = "Block write failed";
        ct_result = 1;
        return 1;
    }

    ct_write_count++;
    return 0; /* Continue */
}

/* Restore partition from backup */
int8_t
compaction_test_restore(void) {
    if (!ct_backup_data) {
        ct_status = "No backup data";
        return -1;
    }

    ct_status = "Erasing...";

    /* Erase partition (takes partition start address) */
    if (flashrom_delete(ct_backup_start) != 0) {
        ct_status = "Erase failed!";
        return -1;
    }

    ct_status = "Restoring...";

    /* Write backup data back - syscall returns 0 on success, -1 on failure */
    int write_ret = flashrom_write(ct_backup_start, ct_backup_data, ct_backup_size);
    if (write_ret < 0) {
        snprintf(ct_debug_buf, sizeof(ct_debug_buf), "write ret=%d", write_ret);
        ct_status = ct_debug_buf;
        return -1;
    }

    ct_status = "Restored OK";
    return 0;
}

/* Cleanup - free resources */
void
compaction_test_cleanup(void) {
    if (ct_backup_data) {
        free(ct_backup_data);
        ct_backup_data = NULL;
    }
    ct_initialized = false;
    ct_write_count = 0;
    ct_total_blocks = 0;
    ct_result = 0;
    ct_status = "Not started";
}

/* Getters for UI */
int
compaction_test_get_write_count(void) {
    return ct_write_count;
}

int
compaction_test_get_total_blocks(void) {
    return ct_total_blocks;
}

int
compaction_test_get_result(void) {
    return ct_result;
}

const char*
compaction_test_get_status(void) {
    return ct_status;
}

#else
/* Non-Dreamcast stubs */
int8_t
compaction_test_init(void) {
    return -1;
}

int8_t
compaction_test_step(void) {
    return -1;
}

int8_t
compaction_test_restore(void) {
    return -1;
}

void
compaction_test_cleanup(void) {}

int
compaction_test_get_write_count(void) {
    return 0;
}

int
compaction_test_get_total_blocks(void) {
    return 0;
}

int
compaction_test_get_result(void) {
    return 0;
}

const char*
compaction_test_get_status(void) {
    return "N/A";
}
#endif

/* COMPACTION_TEST_END */

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
