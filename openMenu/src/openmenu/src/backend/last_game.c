#include <ctype.h>
#include <stdint.h>
#include <string.h>

#include <backend/db_list.h>
#include <backend/gd_item.h>
#include <backend/gd_list.h>
#include <openmenu_settings.h>
#include "backend/last_game.h"

/* The stored view keeps the category type in the low byte and the number
 * of the category in the next one */
#define FILTER_PACK(type, num) (((uint32_t)(unsigned char)(type)) | (((uint32_t)(num) & 0xFF) << 8))
#define FILTER_TYPE(value)     ((char)((value) & 0xFF))
#define FILTER_NUM(value)      ((int)(((value) >> 8) & 0xFF))

/* The genre list ends with a row for games that carry no genre at all */
#define GENRE_ROW_NONE         16
#define GENRE_ROW_MAX          16

static int restore_armed = 0;

int
last_game_record(const gd_item* disc) {
    list_view view;
    uint32_t game, folder = 0, filter = 0;

    if (sf_remember_last_game[0] != REMEMBER_LAST_GAME_ON) {
        return 0;
    }

    list_view_get(&view);
    game = gd_item_recent_hash(disc);

    if (view.kind == LIST_VIEW_FOLDER && view.folder_path[0]) {
        folder = list_folder_path_hash(view.folder_path);
    } else if (view.kind == LIST_VIEW_DRILL) {
        filter = FILTER_PACK(view.drill_type, view.drill_num);
    }

    if (sf_u32_read(sf_last_game) == game && sf_u32_read(sf_last_game_folder) == folder
        && sf_u32_read(sf_last_game_filter) == filter
        && !strncmp((const char*)sf_last_game_product, disc->product, sf_last_game_product_length - 1)) {
        return 0;
    }

    sf_u32_write(sf_last_game, game);
    sf_u32_write(sf_last_game_folder, folder);
    sf_u32_write(sf_last_game_filter, filter);
    strncpy((char*)sf_last_game_product, disc->product, sf_last_game_product_length - 1);
    sf_last_game_product[sf_last_game_product_length - 1] = '\0';
    return 1;
}

void
last_game_arm(void) {
    restore_armed = 1;
}

/* Exact row if it is there, otherwise whichever disc of the set is. The
 * second case is Compact having hidden the one that was launched. */
static int
row_for(const gd_item* item) {
    int row = list_index_of(item);

    if (row < 0 && item->product[0]) {
        row = list_index_of_product(item->product);
    }
    return row;
}

/* Which letter, region or genre row holds this game. Runs the same tests
 * the category builder does, so the game is always inside what comes back.
 * Returns -1 for a region that has no row of its own. */
static int
category_for(char type, const gd_item* item) {
    switch (type) {
        case 'A': {
            int c = toupper((int)item->name[0]);
            return (c >= 'A' && c <= 'Z') ? (c - '@') : 0;
        }
        case 'R': {
            if (!strncmp(item->region, "JUE", 3)) {
                return 3;
            }
            if (!strcmp(item->region, "J")) {
                return 0;
            }
            if (!strcmp(item->region, "U")) {
                return 1;
            }
            if (!strcmp(item->region, "E")) {
                return 2;
            }
            return -1;
        }
        case 'G': {
            db_item* meta;

            if (db_get_meta(item->product, &meta) || !meta->genre) {
                return GENRE_ROW_NONE;
            }
            for (int i = 0; i < GENRE_ROW_MAX; i++) {
                if (meta->genre & (1 << i)) {
                    return i;
                }
            }
            return GENRE_ROW_NONE;
        }
        default: return 0;
    }
}

/* Guards against a stored category that no longer makes sense, which a
 * half written save could leave behind */
static int
category_in_range(char type, int num) {
    switch (type) {
        case 'A': return num >= 0 && num <= 26;
        case 'R': return num >= 0 && num <= 3;
        case 'G': return num >= 0 && num <= GENRE_ROW_NONE;
        default: return 0;
    }
}

/* Puts the category rows back after a drill turned up nothing, so the menu
 * opens where it normally would instead of stranding the cursor on Back */
static void
leave_drill(void) {
    switch (sf_sort[0]) {
        case SORT_NAME: list_set_sort_name(); break;
        case SORT_DATE: list_set_sort_region(); break;
        case SORT_PRODUCT: list_set_sort_genre(); break;
        default: break;
    }
}

static int
restore_flat(const gd_item* item) {
    const gd_item* aim;
    char type;
    int num, row;

    /* A genre filter leaves one flat list with no categories in it */
    if (sf_filter[0] != FILTER_ALL) {
        return row_for(item);
    }

    switch (sf_sort[0]) {
        case SORT_NAME: type = 'A'; break;
        case SORT_DATE: type = 'R'; break;
        case SORT_PRODUCT: type = 'G'; break;
        default: return row_for(item);
    }

    /* Work off the disc that will be on screen, since a hidden one can
     * carry a different title to the one taking its place */
    aim = list_visible_disc(item);

    uint32_t saved = sf_u32_read(sf_last_game_filter);

    if (saved && FILTER_TYPE(saved) == type && category_in_range(type, FILTER_NUM(saved))) {
        num = FILTER_NUM(saved);
    } else {
        num = category_for(type, aim);
        if (num < 0) {
            return -1;
        }
    }

    list_set_sort_filter(type, num);
    row = row_for(item);

    /* The stored category goes stale when metadata changes under it, so
     * work one out from the game before giving up on the jump */
    if (row < 0) {
        int derived = category_for(type, aim);

        if (derived >= 0 && derived != num) {
            list_set_sort_filter(type, derived);
            row = row_for(item);
        }
    }

    if (row < 0) {
        leave_drill();
    }
    return row;
}

static int
restore_folders(const gd_item* item) {
    char path[LIST_FOLDER_PATH_MAX];
    uint32_t saved = sf_u32_read(sf_last_game_folder);
    int found = 0;
    int row;

    if (saved && list_folder_path_by_hash(saved, path, sizeof(path))) {
        found = list_folder_contains(path, item);
    }

    /* The folder it was launched from is gone or never held it, so try
     * where the game says it lives */
    if (!found) {
        strncpy(path, item->folder, sizeof(path) - 1);
        path[sizeof(path) - 1] = '\0';
        found = list_folder_contains(path, item);
    }

    if (found && path[0]) {
        list_folder_enter_path(path);
    }

    row = row_for(item);
    if (row < 0) {
        list_set_folder_root();
    }
    return row;
}

int
last_game_take_row(void) {
    const gd_item* item;
    uint32_t hash;

    if (!restore_armed) {
        return -1;
    }
    restore_armed = 0;

    hash = sf_u32_read(sf_last_game);
    if (sf_remember_last_game[0] != REMEMBER_LAST_GAME_ON || !hash) {
        return -1;
    }

    item = list_find_by_hash(hash);
    if (!item) {
        item = list_find_by_product((const char*)sf_last_game_product);
    }
    if (!item) {
        return -1;
    }

    if (sf_ui[0] == UI_FOLDERS) {
        return restore_folders(item);
    }
    return restore_flat(item);
}
