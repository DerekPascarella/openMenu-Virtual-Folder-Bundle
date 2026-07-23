/*
 * File: boot_defaults.c
 * Project: backend
 * Description: Boot style/theme override from /cd/DEFAULTS.INI
 *
 * GDMENUCardManager can bake a DEFAULTS.INI into the menu disc naming a
 * style and theme that should win over the savefile at boot. The user can
 * opt back out on the console with the Honor Menu Defaults setting. Only
 * the style and theme variables are ever touched here, every other saved
 * setting is honored as loaded.
 */

#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <strings.h>

#include <ini.h>
#include <kos.h>

#include <openmenu_settings.h>
#include "backend/boot_defaults.h"
#include "ui/theme_manager.h"

#define DEFAULTS_FILE "/cd/DEFAULTS.INI"

static int defaults_present = 0;

/* The style and theme that were active before the override, so turning
 * Honor Menu Defaults off can bring them back without a reboot */
static int have_snapshot = 0;
static uint8_t saved_ui;
static uint8_t saved_region;
static uint8_t saved_custom;
static uint8_t saved_custom_num;

/* Longest valid value is "FOLDERS_0" plus room to see it is oversized */
static char ini_style[16];
static char ini_theme[16];

static int
read_defaults_ini(void* user, const char* section, const char* name, const char* value) {
    (void)user;
    if (strcasecmp(section, "DEFAULTS") == 0) {
        if (strcasecmp(name, "STYLE") == 0) {
            strncpy(ini_style, value, sizeof(ini_style) - 1);
        } else if (strcasecmp(name, "THEME") == 0) {
            strncpy(ini_theme, value, sizeof(ini_theme) - 1);
        }
    }
    /* the [BGM] section is card manager bookkeeping, skip it */
    return 1;
}

static int
resolve_style(const char* text) {
    if (strcasecmp(text, "LINEDESC") == 0) {
        return UI_LINE_DESC;
    }
    if (strcasecmp(text, "GRID3") == 0) {
        return UI_GRID3;
    }
    if (strcasecmp(text, "SCROLL") == 0) {
        return UI_SCROLL;
    }
    if (strcasecmp(text, "FOLDERS") == 0) {
        return UI_FOLDERS;
    }
    return -1;
}

/* Matches values like CUST_3, one digit, nothing after it */
static int
theme_family_matches(const char* text, const char* prefix) {
    size_t n = strlen(prefix);
    return strncasecmp(text, prefix, n) == 0 && text[n] >= '0' && text[n] <= '9' && text[n + 1] == '\0';
}

/* The scanned theme arrays fill in readdir order, so the digit in the
 * directory name is not guaranteed to equal the array index. Resolve by
 * looking for the directory name inside each entry's background path,
 * which the scanner builds as THEME/<dir>/BG_L.PVR. */
static int
find_theme_index(const char* bg_left, size_t stride, int count, const char* dirname) {
    char needle[32];
    snprintf(needle, sizeof(needle), "THEME/%s/", dirname);
    size_t len = strlen(needle);

    for (int i = 0; i < count; i++) {
        const char* entry = bg_left + (size_t)i * stride;
        if (strncasecmp(entry, needle, len) == 0) {
            return i;
        }
    }
    return -1;
}

static void
apply_theme(int style) {
    if (ini_theme[0] == '\0') {
        return;
    }

    if (strcasecmp(ini_theme, "DEFAULT") == 0) {
        sf_custom_theme[0] = THEME_OFF;
        return;
    }

    if (style == UI_LINE_DESC || style == UI_GRID3) {
        int region = -1;
        if (strcasecmp(ini_theme, "NTSC_U") == 0) {
            region = REGION_NTSC_U;
        } else if (strcasecmp(ini_theme, "NTSC_J") == 0) {
            region = REGION_NTSC_J;
        } else if (strcasecmp(ini_theme, "PAL") == 0) {
            region = REGION_PAL;
        }
        if (region >= 0) {
            sf_region[0] = (uint8_t)region;
            sf_custom_theme[0] = THEME_OFF;
            return;
        }

        if (theme_family_matches(ini_theme, "CUST_")) {
            int count = 0;
            theme_custom* themes = theme_get_custom(&count);
            int idx = find_theme_index(themes[0].bg_left, sizeof(theme_custom), count, ini_theme);
            if (idx >= 0) {
                sf_custom_theme[0] = THEME_ON;
                sf_custom_theme_num[0] = (uint8_t)idx;
            }
        }
        return;
    }

    /* Scroll and Folders share the theme_scroll layout */
    const char* prefix = (style == UI_FOLDERS) ? "FOLDERS_" : "SCROLL_";
    if (theme_family_matches(ini_theme, prefix)) {
        int count = 0;
        theme_scroll* themes = (style == UI_FOLDERS) ? theme_get_folder(&count) : theme_get_scroll(&count);
        int idx = find_theme_index(themes[0].bg_left, sizeof(theme_scroll), count, ini_theme);
        if (idx >= 0) {
            sf_custom_theme[0] = THEME_ON;
            sf_custom_theme_num[0] = (uint8_t)idx;
        }
    }
}

void
boot_defaults_apply(void) {
    file_t fd = fs_open(DEFAULTS_FILE, O_RDONLY);
    if (fd == -1) {
        return;
    }

    fs_seek(fd, 0, SEEK_END);
    long size = fs_tell(fd);
    fs_seek(fd, 0, SEEK_SET);
    if (size <= 0) {
        fs_close(fd);
        return;
    }

    char* buf = malloc(size + 1);
    if (!buf) {
        fs_close(fd);
        return;
    }
    ssize_t got = fs_read(fd, buf, size);
    fs_close(fd);
    buf[got > 0 ? got : 0] = '\0';

    /* stale values from any earlier call must not leak into this parse */
    ini_style[0] = '\0';
    ini_theme[0] = '\0';

    /* A UTF-8 BOM would make the parser choke on line one and give up,
     * and Windows text editors love sneaking one in */
    char* text = buf;
    if (got >= 3 && (uint8_t)text[0] == 0xEF && (uint8_t)text[1] == 0xBB && (uint8_t)text[2] == 0xBF) {
        text += 3;
    }

    ini_parse_string(text, read_defaults_ini, NULL);
    free(buf);

    int style = resolve_style(ini_style);
    if (style < 0) {
        /* no usable [DEFAULTS] section, the file might only carry [BGM] */
        return;
    }

    /* The settings row shows whenever forcing is configured on the disc,
     * even while the user has it turned off */
    defaults_present = 1;

    if (sf_honor_defaults[0] != HONOR_DEFAULTS_ON) {
        return;
    }

    saved_ui = sf_ui[0];
    saved_region = sf_region[0];
    saved_custom = sf_custom_theme[0];
    saved_custom_num = sf_custom_theme_num[0];
    have_snapshot = 1;

    sf_ui[0] = (uint8_t)style;
    apply_theme(style);

    /* A custom theme left over from another style family can point past
     * the themes this style actually has. Scroll and Folders fall back to
     * their default theme on their own, LineDesc and Grid3 do not, so an
     * out of range index there would draw a blank screen. */
    if ((style == UI_LINE_DESC || style == UI_GRID3) && sf_custom_theme[0] == THEME_ON) {
        int count = 0;
        theme_get_custom(&count);
        if ((int)sf_custom_theme_num[0] >= count) {
            sf_custom_theme[0] = THEME_OFF;
        }
    }

    /* clamps anything odd and derives the packed sf_region for customs */
    settings_sanitize();
}

int
boot_defaults_available(void) {
    return defaults_present;
}

void
boot_defaults_restore(void) {
    if (!have_snapshot) {
        return;
    }

    sf_ui[0] = saved_ui;
    sf_region[0] = saved_region;
    sf_custom_theme[0] = saved_custom;
    sf_custom_theme_num[0] = saved_custom_num;

    settings_sanitize();
}
