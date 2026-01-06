#ifdef _arch_dreamcast
#include <crayon_savefile/peripheral.h>
#include <dc/maple/vmu.h>
#endif

#include <stdbool.h>
#include <crayon_savefile/savefile.h>

#include "openmenu_savefile.h"
#include "openmenu_settings.h"

/* Images and such */
#if __has_include("openmenu_lcd.h") && __has_include("openmenu_pal.h") && __has_include("openmenu_vmu.h")
#include "openmenu_lcd.h"
#include "openmenu_pal.h"
#include "openmenu_vmu.h"

#define OPENMENU_ICON  (openmenu_icon)
#define OPENMENU_LCD   (openmenu_lcd)
#define OPENMENU_PAL   (openmenu_pal)
#define OPENMENU_ICONS (1)
#else
#define OPENMENU_ICON  (NULL)
#define OPENMENU_LCD   (NULL)
#define OPENMENU_PAL   (NULL)
#define OPENMENU_ICONS (0)
#endif

static crayon_savefile_details_t savefile_details;
static bool savefile_was_migrated = false;

void
savefile_defaults() {
    sf_region[0] = REGION_NTSC_U;
    sf_aspect[0] = ASPECT_NORMAL;
    sf_ui[0] = UI_LINE_DESC;
    sf_sort[0] = SORT_DEFAULT;
    sf_filter[0] = FILTER_ALL;
    sf_beep[0] = BEEP_ON;
    sf_multidisc[0] = MULTIDISC_SHOW;
    sf_custom_theme[0] = THEME_OFF;
    sf_custom_theme_num[0] = THEME_0;
    sf_bios_3d[0] = BIOS_3D_OFF;
    sf_scroll_art[0] = SCROLL_ART_ON;
    sf_scroll_index[0] = SCROLL_INDEX_ON;
    sf_folders_art[0] = FOLDERS_ART_ON;
    sf_marquee_speed[0] = MARQUEE_SPEED_MEDIUM;
}

//THIS IS USED BY THE CRAYON SAVEFILE DESERIALISER WHEN LOADING A SAVE FROM AN OLDER VERSION
//THERE IS NO NEED TO CALL THIS MANUALLY
int8_t
update_savefile(void** loaded_variables, crayon_savefile_version_t loaded_version,
                crayon_savefile_version_t latest_version) {
    /* Track if any migration occurred */
    if (loaded_version < latest_version) {
        savefile_was_migrated = true;
    }

    if (loaded_version < SFV_BIOS_3D) {
        sf_bios_3d[0] = BIOS_3D_OFF;
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
    return 0;
}

uint8_t
setup_savefile(crayon_savefile_details_t* details) {
    uint8_t error;

#if defined(_arch_pc)
    crayon_savefile_set_base_path("saves/");
#else
    crayon_savefile_set_base_path(NULL); //Dreamcast ignores the parameter anyways
    // (Assumes "/vmu/") so it's still fine to do the method above for all platforms
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
    uint8_t vmu_screens_bitmap = crayon_peripheral_dreamcast_get_screens();

    crayon_peripheral_vmu_display_icon(vmu_screens_bitmap, OPENMENU_LCD);

    savefile_details.icon_anim_count = OPENMENU_ICONS;
    savefile_details.icon_anim_speed = 1;
    savefile_details.icon_data = OPENMENU_ICON;
    savefile_details.icon_palette = (unsigned short*)OPENMENU_PAL;
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
    crayon_savefile_add_variable(details, &sf_scroll_index, sf_scroll_index_type, sf_scroll_index_length, SFV_SCROLL_INDEX,
                                 VAR_STILL_PRESENT);
    crayon_savefile_add_variable(details, &sf_folders_art, sf_folders_art_type, sf_folders_art_length, SFV_FOLDERS_ART,
                                 VAR_STILL_PRESENT);
    crayon_savefile_add_variable(details, &sf_marquee_speed, sf_marquee_speed_type, sf_marquee_speed_length, SFV_MARQUEE_SPEED,
                                 VAR_STILL_PRESENT);

    if (crayon_savefile_solidify(details)) {
        return 1;
    }

    return 0;
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
    uint8_t setup_res = setup_savefile(&savefile_details);
    int8_t device_res = find_first_valid_savefile_device(&savefile_details);

    if (!setup_res && !device_res) {
        savefile_was_migrated = false;
        crayon_savefile_load_savedata(&savefile_details);
        settings_sanitize();

        /* Only auto-save if migration from older version occurred */
        if (savefile_was_migrated) {
            crayon_savefile_save_savedata(&savefile_details);
            savefile_was_migrated = false;
        }
    }
}

void
savefile_close() {
    crayon_savefile_free_details(&savefile_details);
    crayon_savefile_free_base_path();
}

int8_t
vmu_beep(int8_t save_device_id, uint32_t beep) {
    if (sf_beep[0] != BEEP_ON) {
        return 0;
    }

#ifdef _arch_dreamcast
    maple_device_t* vmu;

    vec2_s8_t port_and_slot = crayon_peripheral_dreamcast_get_port_and_slot(save_device_id);

    // Invalid controller/port
    if (port_and_slot.x < 0) {
        return -1;
    }

    // Make sure there's a device in the port/slot
    if (!((vmu = maple_enum_dev(port_and_slot.x, port_and_slot.y)))) {
        return -1;
    }

    // Check the device is valid and it has a certain function
    if (!vmu->valid) {
        return -1;
    }

    vmu_beep_raw(vmu, beep);
#endif

    return 0;
}

int8_t
savefile_save() {
    settings_sanitize();
    vmu_beep(savefile_details.save_device_id, 0x000065f0); // Turn on beep (if enabled)
    crayon_savefile_save_savedata(&savefile_details);
    vmu_beep(savefile_details.save_device_id, 0x00000000); // Turn off beep (if enabled)
    return 0;
}