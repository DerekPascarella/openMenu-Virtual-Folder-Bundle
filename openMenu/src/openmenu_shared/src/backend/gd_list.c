/*
 * File: example.c
 * Project: ini_parse
 * File Created: Wednesday, 19th May 2021 2:50:50 pm
 * Author: Hayden Kowalchuk
 * -----
 * Copyright (c) 2021 Hayden Kowalchuk, Hayden Kowalchuk
 * License: BSD 3-clause "New" or "Revised" License,
 * http://www.opensource.org/licenses/BSD-3-Clause
 */

#include <ctype.h>
#include <stdint.h>
#include <stdlib.h>
#include <string.h>
#include <strings.h>

#include <ini.h>

#include "backend/db_item.h"
#include "backend/db_list.h"
#include "backend/gd_item.h"
#include "backend/gd_list.h"

#ifdef _arch_dreamcast
#include <kos/fs.h>
#define PATH_PREFIX "/cd/"
#else
#define PATH_PREFIX ""
#endif

#ifndef STANDALONE_BINARY
#include <openmenu_settings.h>
#endif

/* Base internal original */
static int num_items_BASE = -1;
static int num_items_read = 0;
static gd_item* gd_slots_BASE = NULL;

/* Current client facing pointer copy, may be sorted/filtered */
static int num_items_temp = -1;
static gd_item** list_temp = NULL;

static int num_items_current = -1;
static gd_item** list_current = NULL;

static int num_items_alphabet = 27;
static const struct gd_item list_alphabet_tmp[27] = {
    {"#", "", "A0", "DIR", "", "", 0, {' '}, ""},  {"A", "", "AA", "DIR", "", "", 1, {' '}, ""},
    {"B", "", "AB", "DIR", "", "", 2, {' '}, ""},  {"C", "", "AC", "DIR", "", "", 3, {' '}, ""},
    {"D", "", "AD", "DIR", "", "", 4, {' '}, ""},  {"E", "", "AE", "DIR", "", "", 5, {' '}, ""},
    {"F", "", "AF", "DIR", "", "", 6, {' '}, ""},  {"G", "", "AG", "DIR", "", "", 7, {' '}, ""},
    {"H", "", "AH", "DIR", "", "", 8, {' '}, ""},  {"I", "", "AI", "DIR", "", "", 9, {' '}, ""},
    {"J", "", "AJ", "DIR", "", "", 10, {' '}, ""}, {"K", "", "AK", "DIR", "", "", 11, {' '}, ""},
    {"L", "", "AL", "DIR", "", "", 12, {' '}, ""}, {"M", "", "AM", "DIR", "", "", 13, {' '}, ""},
    {"N", "", "AN", "DIR", "", "", 14, {' '}, ""}, {"O", "", "AO", "DIR", "", "", 15, {' '}, ""},
    {"P", "", "AP", "DIR", "", "", 16, {' '}, ""}, {"Q", "", "AQ", "DIR", "", "", 17, {' '}, ""},
    {"R", "", "AR", "DIR", "", "", 18, {' '}, ""}, {"S", "", "AS", "DIR", "", "", 19, {' '}, ""},
    {"T", "", "AT", "DIR", "", "", 20, {' '}, ""}, {"U", "", "AU", "DIR", "", "", 21, {' '}, ""},
    {"V", "", "AV", "DIR", "", "", 22, {' '}, ""}, {"W", "", "AW", "DIR", "", "", 23, {' '}, ""},
    {"X", "", "AX", "DIR", "", "", 24, {' '}, ""}, {"Y", "", "AY", "DIR", "", "", 25, {' '}, ""},
    {"Z", "", "AZ", "DIR", "", "", 26, {' '}, ""}};

static const struct gd_item* list_alphabet[27] = {
    &list_alphabet_tmp[0],  &list_alphabet_tmp[1],  &list_alphabet_tmp[2],  &list_alphabet_tmp[3],
    &list_alphabet_tmp[4],  &list_alphabet_tmp[5],  &list_alphabet_tmp[6],  &list_alphabet_tmp[7],
    &list_alphabet_tmp[8],  &list_alphabet_tmp[9],  &list_alphabet_tmp[10], &list_alphabet_tmp[11],
    &list_alphabet_tmp[12], &list_alphabet_tmp[13], &list_alphabet_tmp[14], &list_alphabet_tmp[15],
    &list_alphabet_tmp[16], &list_alphabet_tmp[17], &list_alphabet_tmp[18], &list_alphabet_tmp[19],
    &list_alphabet_tmp[20], &list_alphabet_tmp[21], &list_alphabet_tmp[22], &list_alphabet_tmp[23],
    &list_alphabet_tmp[24], &list_alphabet_tmp[25], &list_alphabet_tmp[26]};

static int num_items_region = 4;
static const struct gd_item list_region_tmp[4] = {{"NTSC-J", "", "RJ", "DIR", "", "", 0, {' '}, ""},
                                                  {"NTSC-U", "", "RU", "DIR", "", "", 1, {' '}, ""},
                                                  {"PAL", "", "RP", "DIR", "", "", 2, {' '}, ""},
                                                  {"FREE", "", "RF", "DIR", "", "", 3, {' '}, ""}};

static const struct gd_item* list_region[4] = {&list_region_tmp[0], &list_region_tmp[1], &list_region_tmp[2],
                                               &list_region_tmp[3]};

static int num_items_genre = 17;
static const struct gd_item list_genre_tmp[17] = {
    {"Action", "", "GACT", "DIR", "", "", 0, {' '}, ""},     {"Racing", "", "GRAC", "DIR", "", "", 1, {' '}, ""},
    {"Simulation", "", "GSIM", "DIR", "", "", 2, {' '}, ""}, {"Sports", "", "GSPO", "DIR", "", "", 3, {' '}, ""},
    {"Lightgun", "", "GLIG", "DIR", "", "", 4, {' '}, ""},   {"Fighting", "", "GFIG", "DIR", "", "", 5, {' '}, ""},
    {"Shooter", "", "GSHO", "DIR", "", "", 6, {' '}, ""},    {"Survival", "", "GSUR", "DIR", "", "", 7, {' '}, ""},
    {"Adventure", "", "GADV", "DIR", "", "", 8, {' '}, ""},  {"Platformer", "", "GPLA", "DIR", "", "", 9, {' '}, ""},
    {"RPG", "", "GRPG", "DIR", "", "", 10, {' '}, ""},       {"Shmup", "", "GSHM", "DIR", "", "", 11, {' '}, ""},
    {"Strategy", "", "GSTR", "DIR", "", "", 12, {' '}, ""},  {"Puzzle", "", "GPUZ", "DIR", "", "", 13, {' '}, ""},
    {"Arcade", "", "GARC", "DIR", "", "", 14, {' '}, ""},    {"Music", "", "GMUS", "DIR", "", "", 15, {' '}, ""},
    {"No genre", "", "GNG", "DIR", "", "", 16, {' '}, ""}};

static const struct gd_item* list_genre[17] = {
    &list_genre_tmp[0],  &list_genre_tmp[1],  &list_genre_tmp[2],  &list_genre_tmp[3],  &list_genre_tmp[4],
    &list_genre_tmp[5],  &list_genre_tmp[6],  &list_genre_tmp[7],  &list_genre_tmp[8],  &list_genre_tmp[9],
    &list_genre_tmp[10], &list_genre_tmp[11], &list_genre_tmp[12], &list_genre_tmp[13], &list_genre_tmp[14],
    &list_genre_tmp[15], &list_genre_tmp[16]};

static struct gd_item back_button = {"Back", "", " ", "DIR", "", "", 0, {' '}, ""};

/* Folder tree system for hierarchical navigation */
#define MAX_FOLDER_DEPTH    8
#define MAX_FOLDER_PATH     512
#define MAX_FOLDER_NODES    1024
#define MAX_FOLDER_CHILDREN 1024

typedef struct folder_node {
    char name[256];
    struct folder_node* parent;
    struct folder_node* children[MAX_FOLDER_CHILDREN];
    int num_children;
    gd_item** games;     /* Dynamic array of game pointers */
    int num_games;       /* Current number of games */
    int games_capacity;  /* Allocated capacity */
    int first_seen_slot; /* Slot number of first game with this folder path */
} folder_node_t;

typedef struct {
    char path[MAX_FOLDER_PATH];
    int depth;
    char breadcrumbs[MAX_FOLDER_DEPTH][256];
    int cursor_positions[MAX_FOLDER_DEPTH];
} folder_state_t;

static folder_node_t* folder_tree_root = NULL;
static folder_state_t folder_state = {{0}, 0, {{0}}, {0}};
static struct gd_item parent_button = {"[..]", "", "F..", "DIR", "", "", 0, {' '}, ""};
static struct gd_item folder_items[MAX_FOLDER_NODES];
static int folder_items_count = 0;

/* Currently browsed folder node (set by list_set_folder_root/path) */
static folder_node_t* folder_current_node = NULL;

/* Temporary list for holding all multidisc games in a set */
#define MULTIDISC_MAX_GAMES_PER_SET (10)
static int num_items_multidisc = -1;
static gd_item* list_multidisc[MULTIDISC_MAX_GAMES_PER_SET] = {NULL};

/* Alt folder paths - temporary storage during INI parse + tree build, then freed */
#define MAX_FOLDER_ALTS 5

typedef struct folder_alt_entry {
    int slot;
    int count;
    char paths[MAX_FOLDER_ALTS][MAX_FOLDER_PATH];
} folder_alt_entry_t;

static folder_alt_entry_t* folder_alt_entries = NULL;
static int folder_alt_count = 0;
static int folder_alt_capacity = 0;

static folder_alt_entry_t*
folder_alt_find(int slot) {
    for (int i = 0; i < folder_alt_count; i++) {
        if (folder_alt_entries[i].slot == slot) {
            return &folder_alt_entries[i];
        }
    }
    return NULL;
}

static void
folder_alt_store(int slot, int alt_idx, const char* path) {
    folder_alt_entry_t* entry = folder_alt_find(slot);
    if (!entry) {
        if (folder_alt_count >= folder_alt_capacity) {
            int new_cap = folder_alt_capacity ? folder_alt_capacity * 2 : 16;
            folder_alt_entry_t* new_arr = realloc(folder_alt_entries, new_cap * sizeof(folder_alt_entry_t));
            if (!new_arr) {
                return;
            }
            folder_alt_entries = new_arr;
            folder_alt_capacity = new_cap;
        }
        entry = &folder_alt_entries[folder_alt_count++];
        memset(entry, 0, sizeof(*entry));
        entry->slot = slot;
    }
    if (alt_idx < MAX_FOLDER_ALTS) {
        strncpy(entry->paths[alt_idx], path, MAX_FOLDER_PATH - 1);
        entry->paths[alt_idx][MAX_FOLDER_PATH - 1] = '\0';
        if (alt_idx >= entry->count) {
            entry->count = alt_idx + 1;
        }
    }
}

#ifndef STANDALONE_BINARY
static inline long int
filelength(file_t f) {
    return fs_total(f);
}
#else
static inline long int
filelength(FILE* f) {
    long int end;
    fseek(f, 0, SEEK_END);
    end = ftell(f);
    fseek(f, 0, SEEK_SET);

    return end;
}
#endif

static int
read_openmenu_ini(void* user, const char* section, const char* name, const char* value) {
    /* unused */
    (void)user;

    if ((strcmp(section, "OPENMENU") == 0) && (strcmp(name, "num_items") == 0)) {
        num_items_BASE = atoi(value) /* It can occur that GDMenuCardManager under reports by 1 */;
        num_items_temp = num_items_BASE - 1;
        gd_slots_BASE = malloc((num_items_BASE + 1) * sizeof(struct gd_item));
        if (!gd_slots_BASE) {
            printf("%s no free memory\n", __func__);
            return 0;
        }
        list_temp = malloc((num_items_BASE + 1) * sizeof(struct gd_item*));
        if (!list_temp) {
            printf("%s no free memory\n", __func__);
            return 0;
        }

        memset(gd_slots_BASE, '\0', (num_items_BASE + 1) * sizeof(struct gd_item));
        memset(list_temp, '\0', (num_items_BASE + 1) * sizeof(struct gd_item*));
        memset(list_multidisc, '\0', MULTIDISC_MAX_GAMES_PER_SET * sizeof(struct gd_item*));
    } else {
        /* Parsing games */
        char slot_string[4] = {0, 0, 0, 0};
        uintptr_t seperator = (uintptr_t)strchr(name, '.');
        if (seperator) {
            size_t temp_len = (size_t)(seperator - (uintptr_t)name);
            memcpy(slot_string, name, temp_len);
            int slot = atoi(slot_string);
            num_items_read = slot;

            gd_item* item = &gd_slots_BASE[slot - 1];
            if (!item->slot_num) {
                item->slot_num = slot;
            }

            const char* plain_name = name + temp_len + 1;

            // printf("[%s] %s: %s\n", section, plain_name, value);

            if (0)
                ;
#define CFG(s, n, default)                                                                                             \
    else if (strcasecmp(section, #s) == 0 && strcasecmp(plain_name, #n) == 0) strcpy(item->n, value);
#include "backend/gd_item.def"
            /* Alt folder paths for Folders mode (folder_alt1..folder_alt5) */
            else if (strcasecmp(section, "ITEMS") == 0 && strncasecmp(plain_name, "folder_alt", 10) == 0) {
                int alt_num = atoi(plain_name + 10);
                if (alt_num >= 1 && alt_num <= MAX_FOLDER_ALTS) {
                    folder_alt_store(slot, alt_num - 1, value);
                }
            }

        } else {
            /* error */
            printf("INI:Error unknown [%s] %s: %s\n", section, name, value);
        }
    }
    return 1;
}

void
list_print_slots(void) {
    for (int i = 0; i < num_items_BASE; i++) {
        printf("slot %d\n", i);
        gd_item* item = &gd_slots_BASE[i];
#define CFG(s, n, default) printf("%s = %s\n", #n, item->n);
#include "backend/gd_item.def"

        printf("\n");
    }
}

void
list_print_temp(void) {
    for (int i = 0; i < num_items_temp; i++) {
        printf("slot %d\n", i);
        gd_item* item = list_temp[i];
#define CFG(s, n, default) printf("%s = %s\n", #n, item->n);
#include "backend/gd_item.def"

        printf("\n");
    }
}

/* Might need a rework */
void
list_print(const gd_item** list) {
    for (int i = 0; i < num_items_temp; i++) {
        // printf("slot %d\n", i);
        const gd_item* item = list[i];
#define CFG(s, n, default) printf("%s = %s\n", #n, item->n);
#include "backend/gd_item.def"

        printf("\n");
    }
    printf("\n");
}

static void
list_temp_reset(void) {
    int base_idx, temp_idx = 0;

#ifdef _arch_dreamcast
    int hide_multidisc = sf_multidisc[0];
#else
    int hide_multidisc = 0;
#endif

    /* Skip openMenu itself */
    for (base_idx = 1; base_idx < num_items_BASE; base_idx++) {
        int disc_num = gd_item_disc_num(gd_slots_BASE[base_idx].disc);
        int disc_set = gd_item_disc_total(gd_slots_BASE[base_idx].disc);
        /* Only hide multi-disc entries if they have a valid product code */
        if (hide_multidisc && disc_num > 1 && disc_set > 1 && gd_slots_BASE[base_idx].product[0] != '\0') {
            continue;
        }

        list_temp[temp_idx++] = &gd_slots_BASE[base_idx];
    }
    num_items_temp = temp_idx;
}

static int
struct_cmp_by_name(const void* a, const void* b) {
    const gd_item* ia = *(const gd_item**)a;
    const gd_item* ib = *(const gd_item**)b;
    return strcasecmp(ia->name, ib->name);
}

static int
struct_cmp_by_region(const void* a, const void* b) {
    const gd_item* ia = *(const gd_item**)a;
    const gd_item* ib = *(const gd_item**)b;
    return strcmp(ia->region, ib->region);
}

void
list_set_sort_name(void) {
    list_temp_reset();
    list_current = (gd_item**)list_alphabet;
    num_items_current = num_items_alphabet;
}

void
list_set_sort_region(void) {
    list_temp_reset();
    list_current = (gd_item**)list_region;
    num_items_current = num_items_region;
}

void
list_set_sort_genre(void) {
    list_temp_reset();
    list_current = (gd_item**)list_genre;
    num_items_current = num_items_genre;
}

void
list_set_sort_default(void) {
    list_temp_reset();
    list_current = list_temp;
    num_items_current = num_items_temp;
}

void
list_set_sort_alphabetical(void) {
    list_temp_reset();
    qsort(list_temp, num_items_temp, sizeof(gd_item*), struct_cmp_by_name);
    list_current = list_temp;
    num_items_current = num_items_temp;
}

void
list_set_sort_filter(const char type, int num) {
#ifdef _arch_dreamcast
    int base_idx, temp_idx = 1;
    int hide_multidisc = sf_multidisc[0];

    FLAGS_GENRE matching_genre = (1 << num);

    list_temp[0] = &back_button;
    back_button.product[0] = type;

    /* Skip openMenu itself */
    for (base_idx = 1; base_idx < num_items_BASE; base_idx++) {
        int disc_num = gd_item_disc_num(gd_slots_BASE[base_idx].disc);
        int disc_set = gd_item_disc_total(gd_slots_BASE[base_idx].disc);
        /* Only hide multi-disc entries if they have a valid product code */
        if (hide_multidisc && disc_num > 1 && disc_set > 1 && gd_slots_BASE[base_idx].product[0] != '\0') {
            continue;
        }

        gd_item* temp_item = &gd_slots_BASE[base_idx];
        db_item* temp_meta;

        switch (type) {
            case 'G':
                if (!db_get_meta(temp_item->product, &temp_meta)) {
                    if (num == 16 && !temp_meta->genre) {
                        list_temp[temp_idx++] = temp_item;
                    } else if (temp_meta->genre & matching_genre) {
                        list_temp[temp_idx++] = temp_item;
                    }
                } else if (num == 16) {
                    list_temp[temp_idx++] = temp_item;
                }
                break;
            case 'R':

                if (num == 0 && !strcmp(temp_item->region, "J")) {
                    list_temp[temp_idx++] = temp_item;
                } else if (num == 1 && !strcmp(temp_item->region, "U")) {
                    list_temp[temp_idx++] = temp_item;
                } else if (num == 2 && !strcmp(temp_item->region, "E")) {
                    list_temp[temp_idx++] = temp_item;
                } else if (num == 3 && !strncmp(temp_item->region, "JUE", 3)) {
                    list_temp[temp_idx++] = temp_item;
                }
                break;
            default:
                if (num != 0) {
                    if (toupper(temp_item->name[0]) == (num + '@')) {
                        list_temp[temp_idx++] = temp_item;
                    }
                } else if (!isalpha((int)temp_item->name[0])) {
                    list_temp[temp_idx++] = temp_item;
                }
        }
    }

    qsort(&list_temp[1], temp_idx - 1, sizeof(gd_item*), struct_cmp_by_name);
    list_current = list_temp;
    num_items_current = num_items_temp = temp_idx;
#endif
}

const struct gd_item**
list_get(void) {
    return (const gd_item**)list_current;
}

const struct gd_item**
list_get_multidisc(void) {
    return (const gd_item**)list_multidisc;
}

void
list_set_genre(int matching_genre) {
#if !defined(STANDALONE_BINARY)
    int base_idx, temp_idx = 0;

    int hide_multidisc = sf_multidisc[0];

    /* Skip openMenu itself */
    for (base_idx = 1; base_idx < num_items_BASE; base_idx++) {
        int disc_num = gd_item_disc_num(gd_slots_BASE[base_idx].disc);
        int disc_set = gd_item_disc_total(gd_slots_BASE[base_idx].disc);
        /* Only hide multi-disc entries if they have a valid product code */
        if (hide_multidisc && disc_num > 1 && disc_set > 1 && gd_slots_BASE[base_idx].product[0] != '\0') {
            continue;
        }

        gd_item* temp_item = &gd_slots_BASE[base_idx];
        db_item* temp_meta;
        if (!db_get_meta(temp_item->product, &temp_meta)) {
            if (temp_meta->genre & matching_genre) {
                list_temp[temp_idx++] = temp_item;
            }
        }
    }

    num_items_temp = temp_idx;
#endif
}

void
list_set_genre_sort(int genre, int sort) {
    FLAGS_GENRE matching_genre = (1 << genre);
    list_set_genre(matching_genre);

    switch (sort) {
        case 1: qsort(list_temp, num_items_temp, sizeof(gd_item*), struct_cmp_by_name); break;
        case 2: qsort(list_temp, num_items_temp, sizeof(gd_item*), struct_cmp_by_region); break;
        default:
            /* @Note: no sort, strange codeflow */
            break;
    }

    list_current = list_temp;
    num_items_current = num_items_temp;
}

void
list_set_multidisc(const char* product_id) {
    int base_idx, temp_idx = 0;

    /* Skip openMenu itself */
    for (base_idx = 1; base_idx < num_items_BASE; base_idx++) {
        if (strcmp(gd_slots_BASE[base_idx].product, product_id)) {
            continue;
        }

        list_multidisc[temp_idx++] = &gd_slots_BASE[base_idx];
    }
    num_items_multidisc = temp_idx;
}

void
list_set_multidisc_filtered(const char* product_id, const char* folder_path) {
    int base_idx, temp_idx = 0;

    /* Skip openMenu itself */
    for (base_idx = 1; base_idx < num_items_BASE; base_idx++) {
        /* Must match product ID */
        if (strcmp(gd_slots_BASE[base_idx].product, product_id)) {
            continue;
        }

        /* If folder filter provided, must also match folder */
        if (folder_path && strcmp(gd_slots_BASE[base_idx].folder, folder_path)) {
            continue;
        }

        list_multidisc[temp_idx++] = &gd_slots_BASE[base_idx];
    }
    num_items_multidisc = temp_idx;
}

int
list_count_multidisc_filtered(const char* product_id, const char* folder_path) {
    int count = 0;

    /* Skip openMenu itself */
    for (int base_idx = 1; base_idx < num_items_BASE; base_idx++) {
        /* Must match product ID */
        if (strcmp(gd_slots_BASE[base_idx].product, product_id)) {
            continue;
        }

        /* If folder filter provided, must also match folder */
        if (folder_path && strcmp(gd_slots_BASE[base_idx].folder, folder_path)) {
            continue;
        }

        count++;
    }
    return count;
}

/* Node-based multi-disc lookup (uses current folder node, handles alt folders) */
void
list_set_multidisc_in_folder(const char* product_id) {
    int temp_idx = 0;
    folder_node_t* node = folder_current_node ? folder_current_node : folder_tree_root;

    for (int i = 0; i < node->num_games; i++) {
        if (strcmp(node->games[i]->product, product_id) == 0) {
            if (temp_idx < MULTIDISC_MAX_GAMES_PER_SET) {
                list_multidisc[temp_idx++] = node->games[i];
            }
        }
    }
    num_items_multidisc = temp_idx;
}

int
list_count_multidisc_in_folder(const char* product_id) {
    int count = 0;
    folder_node_t* node = folder_current_node ? folder_current_node : folder_tree_root;

    for (int i = 0; i < node->num_games; i++) {
        if (strcmp(node->games[i]->product, product_id) == 0) {
            count++;
        }
    }
    return count;
}

int
list_length(void) {
    return num_items_current;
}

int
list_multidisc_length(void) {
    return num_items_multidisc;
}

static void
fix_sega_serials(void) {
    /* fixing Sega serial issues... */

    /* Skip openMenu itself */
    for (int base_idx = 1; base_idx < num_items_BASE; base_idx++) {
        gd_item* item = &gd_slots_BASE[base_idx];

        /* Fix Alone in the Dark (PAL) overlapping Alone in the Dark (USA) */
        if (!strcmp(item->product, "T15117N") && !strcmp(item->date, "20010423")) {
            strcpy(item->product, "T15112D05");
        }
        /* Fix Crazy Taxi (PAL) overlapping Crazy Taxi (USA) */
        if (!strcmp(item->product, "MK51035") && !strcmp(item->date, "20000120")) {
            strcpy(item->product, "MK5103550");
        }
        /* Fix Disney's Donald Duck: Goin' Quackers (USA) overlapping Disney's
         * Donald Duck: Quack Attack (PAL) */
        if (!strcmp(item->product, "T17714D50") && !strcmp(item->date, "20001116")) {
            strcpy(item->product, "T17719N");
        }
        /* Fix Floigan Bros (PAL) overlapping Floigan Bros (USA) */
        if (!strcmp(item->product, "MK51114") && !strcmp(item->date, "20010920")) {
            strcpy(item->product, "MK5111450");
        }
        /* Fix Legacy of Kain: Soul Reaver (PAL) overlapping Legacy of Kain: Soul
         * Reaver (USA) */
        if (!strcmp(item->product, "T36802N") && !strcmp(item->date, "19991220")) {
            strcpy(item->product, "T36803D05");
        }
        /* Fix NBA2K2 (PAL) overlapping NBA2K2 (USA) */
        if (!strcmp(item->product, "MK51178") && !strcmp(item->date, "20011129")) {
            strcpy(item->product, "MK5117850");
        }
        /* Fix NBA Showtime (PAL) overlapping 4 Wheel Thunder (PAL) */
        if (!strcmp(item->product, "T9706D50") && !strcmp(item->date, "19991201")) {
            strcpy(item->product, "T9705D50");
        }
        /* Fix Nightmare Creatures II (USA) overlapping Dancing Blade 2 (JAP) */
        if (!strcmp(item->product, "T9504M") && !strcmp(item->date, "20000407")) {
            strcpy(item->product, "T9504N");
        }
        /* Fix Plasma Sword (PAL) overlapping Street Fighter Alpha 3 (PAL) */
        if (!strcmp(item->product, "T7005D") && !strcmp(item->date, "20000711")) {
            strcpy(item->product, "T7003D");
        }
        /* Fix Skies of Arcadia (PAL) overlapping Skies of Arcadia (USA) */
        if (!strcmp(item->product, "MK51052") && !strcmp(item->date, "20010306")) {
            strcpy(item->product, "MK5105250");
        }
        /* Fix Spider-Man (PAL) overlapping Spider-Man (USA) */
        if (!strcmp(item->product, "T13008N") && !strcmp(item->date, "20010402")) {
            strcpy(item->product, "T13011D50");
        }
        /* Fix TNN Motorsports (USA) overlapping Metal Slug 6 (AW) */
        if (!strcmp(item->product, "T0000M") && !strcmp(item->date, "19990813")) {
            strcpy(item->product, "T13701N");
        }

        /* Fix Maximum Speed (AW) overlapping Dolphin Blue (AW) */
        if (!strcmp(item->product, "T0006M") && !strcmp(item->date, "20030609")) {
            strcpy(item->product, "T0010M");
        }

        /* Fix Fist of North Star (AW) overlapping Rumble Fish (AW) */
        if (!strcmp(item->product, "T0009M") && strstr(item->name, "orth")) {
            strcpy(item->product, "T0026M");
        }
    }
}

int
list_read(const char* filename) {
    /* Always LD/cdrom */
#ifndef STANDALONE_BINARY
    file_t ini = fs_open(filename, O_RDONLY);
    if (ini == -1)
#else
    FILE* ini = fopen(filename, "rb");
    if (!ini)
#endif
    {
        printf("INI:Error opening %s!\n", filename);
        fflush(stdout);
        /*exit or something */
        return -1;
    }

    printf("INI:Open %s\n", filename);

    size_t ini_size = filelength(ini);
    char* ini_buffer = malloc(ini_size + 2) /* adjust for adding newline at end always */;
    if (!ini_buffer) {
        printf("%s no free memory\n", __func__);
        return -1;
    }
#ifndef STANDALONE_BINARY
    fs_read(ini, ini_buffer, ini_size);
    fs_close(ini);
#else
    fread(ini_buffer, ini_size, 1, ini);
    fclose(ini);
#endif
    /* Add newline */
    ini_buffer[ini_size + 0] = '\n';
    ini_buffer[ini_size + 1] = '\0';

    if (ini_parse_string(ini_buffer, read_openmenu_ini, NULL) < 0) {
        printf("INI:Error Parsing %s!\n", filename);
        fflush(stdout);
        /*exit or something */
        return -1;
    }
    free(ini_buffer);

    printf("Info: Loaded %d items from %d\n", num_items_read, num_items_BASE);
    /* Trim list if over reported */
    if (num_items_read != num_items_BASE) {
        num_items_BASE = num_items_read;
        num_items_temp = num_items_read - 1;
    }

    fix_sega_serials();

    printf("INI:Parse success (%d items)!\n", num_items_BASE);
    list_temp_reset();
    fflush(stdout);

    return 0;
}

int
list_read_default(void) {
    return list_read(PATH_PREFIX "OPENMENU.INI");
}

void
list_destroy(void) {
    num_items_BASE = -1;
    num_items_temp = -1;
    free(gd_slots_BASE);
    free(list_temp);
    gd_slots_BASE = NULL;
    list_temp = NULL;
}

const gd_item*
list_item_get(int idx) {
    if ((idx >= 0) && (idx < num_items_temp)) {
        return (const gd_item*)list_current[idx];
    }

    return NULL;
}

/* Folder navigation system functions */

static int
folder_parse_path(const char* folder_path, char segments[][256], int max_segments) {
    if (!folder_path || folder_path[0] == '\0') {
        return 0;
    }

    int segment_count = 0;
    const char* start = folder_path;
    const char* end;

    while ((end = strchr(start, '\\')) != NULL && segment_count < max_segments) {
        size_t len = end - start;
        if (len > 0 && len < 256) {
            memcpy(segments[segment_count], start, len);
            segments[segment_count][len] = '\0';
            segment_count++;
        }
        start = end + 1;
    }

    if (*start && segment_count < max_segments) {
        strncpy(segments[segment_count], start, 255);
        segments[segment_count][255] = '\0';
        segment_count++;
    }

    return segment_count;
}

static folder_node_t*
folder_find_or_create_node(folder_node_t* parent, const char* name, int slot_num) {
    if (!parent || !name) {
        return NULL;
    }

    for (int i = 0; i < parent->num_children; i++) {
        if (strcmp(parent->children[i]->name, name) == 0) {
            return parent->children[i];
        }
    }

    if (parent->num_children >= MAX_FOLDER_CHILDREN) {
        return NULL;
    }

    folder_node_t* node = calloc(1, sizeof(folder_node_t));
    if (!node) {
        return NULL;
    }

    strncpy(node->name, name, 255);
    node->name[255] = '\0';
    node->parent = parent;
    node->first_seen_slot = slot_num; /* Track when this folder was first seen */

    /* Allocate initial capacity for games (start with 64, will grow as needed) */
    node->games_capacity = 64;
    node->games = malloc(node->games_capacity * sizeof(gd_item*));
    if (!node->games) {
        free(node);
        return NULL;
    }
    node->num_games = 0;

    parent->children[parent->num_children++] = node;

    return node;
}

static folder_node_t*
folder_find_by_path(folder_node_t* root, const char* path) {
    if (!root) {
        return NULL;
    }

    if (!path || path[0] == '\0') {
        return root;
    }

    char segments[MAX_FOLDER_DEPTH][256];
    int depth = folder_parse_path(path, segments, MAX_FOLDER_DEPTH);

    folder_node_t* current = root;
    for (int d = 0; d < depth; d++) {
        folder_node_t* found = NULL;
        for (int i = 0; i < current->num_children; i++) {
            if (strcmp(current->children[i]->name, segments[d]) == 0) {
                found = current->children[i];
                break;
            }
        }
        if (!found) {
            return NULL;
        }
        current = found;
    }

    return current;
}

static void
folder_tree_destroy_recursive(folder_node_t* node) {
    if (!node) {
        return;
    }

    for (int i = 0; i < node->num_children; i++) {
        folder_tree_destroy_recursive(node->children[i]);
    }

    /* Free the dynamic games array */
    if (node->games) {
        free(node->games);
    }

    free(node);
}

static int
folder_cmp(const void* a, const void* b) {
    const gd_item** item_a = (const gd_item**)a;
    const gd_item** item_b = (const gd_item**)b;

    int is_dir_a = !strncmp((*item_a)->disc, "DIR", 3);
    int is_dir_b = !strncmp((*item_b)->disc, "DIR", 3);

    /* Directories always come first */
    if (is_dir_a != is_dir_b) {
        return is_dir_b - is_dir_a;
    }

    /* In Folders mode, mapping is inverted for backward compatibility:
     * - SORT_DEFAULT (0) = Alphabetical (old default behavior)
     * - SORT_NAME (1) = SD Card Order
     * Sort by slot order when Sort = Name, otherwise alphabetically */
    if (sf_sort[0] == SORT_NAME) {
        return (*item_a)->slot_num - (*item_b)->slot_num;
    }

    return strcasecmp((*item_a)->name, (*item_b)->name);
}

/* Check if a game should be visible within a folder node.
 * When multidisc hiding is enabled, show only the lowest disc number for this product in this folder.
 * The grouping mode only affects launcher/details, not folder display - every folder shows its local games. */
static int
folder_game_visible(folder_node_t* node, gd_item* game, int hide_multidisc) {
    if (!hide_multidisc) {
        return 1; /* Show all discs */
    }

    /* Games without product codes are always visible (treat as single disc) */
    if (game->product[0] == '\0') {
        return 1;
    }

    int disc_num = gd_item_disc_num(game->disc);
    int disc_set = gd_item_disc_total(game->disc);

    if (disc_set <= 1) {
        return 1; /* Single disc game, always visible */
    }

    /* Show only the lowest disc number for this product in this folder */
    int lowest_disc = disc_num;
    for (int i = 0; i < node->num_games; i++) {
        gd_item* other = node->games[i];
        if (strcmp(other->product, game->product) == 0) {
            int other_disc = gd_item_disc_num(other->disc);
            if (other_disc < lowest_disc) {
                lowest_disc = other_disc;
            }
        }
    }

    return (disc_num == lowest_disc);
}

/* Count visible games in a folder node (non-recursive, just this folder's games) */
static int
folder_count_visible_games(folder_node_t* node, int hide_multidisc) {
    int count = 0;
    for (int i = 0; i < node->num_games; i++) {
        if (folder_game_visible(node, node->games[i], hide_multidisc)) {
            count++;
        }
    }
    return count;
}

/* Recursively check if a folder has any visible content (games or non-empty subfolders) */
static int
folder_has_visible_content(folder_node_t* node, int hide_multidisc) {
    /* Check if this folder has visible games */
    if (folder_count_visible_games(node, hide_multidisc) > 0) {
        return 1;
    }

    /* Check if any subfolder has visible content */
    for (int i = 0; i < node->num_children; i++) {
        if (folder_has_visible_content(node->children[i], hide_multidisc)) {
            return 1;
        }
    }

    return 0;
}

/* Register a game into a folder path in the tree. Skips duplicates. */
static void
folder_register_item(gd_item* item, const char* folder_path, int slot_idx) {
    char segments[MAX_FOLDER_DEPTH][256];
    int depth = folder_parse_path(folder_path, segments, MAX_FOLDER_DEPTH);

    folder_node_t* current = folder_tree_root;
    for (int d = 0; d < depth; d++) {
        current = folder_find_or_create_node(current, segments[d], slot_idx);
        if (!current) {
            return;
        }
    }

    /* Check for duplicate (same item pointer already in this node) */
    for (int i = 0; i < current->num_games; i++) {
        if (current->games[i] == item) {
            return;
        }
    }

    if (current->num_games >= current->games_capacity) {
        int new_capacity = current->games_capacity * 2;
        gd_item** new_games = realloc(current->games, new_capacity * sizeof(gd_item*));
        if (!new_games) {
            return;
        }
        current->games = new_games;
        current->games_capacity = new_capacity;
    }
    current->games[current->num_games++] = item;
}

void
list_folder_init(void) {
    folder_tree_root = calloc(1, sizeof(folder_node_t));
    if (!folder_tree_root) {
        printf("Error: Could not allocate folder tree root\n");
        return;
    }

    strcpy(folder_tree_root->name, "<ROOT>");

    /* Allocate initial capacity for root node's games */
    folder_tree_root->games_capacity = 64;
    folder_tree_root->games = malloc(folder_tree_root->games_capacity * sizeof(gd_item*));
    if (!folder_tree_root->games) {
        printf("Error: Could not allocate root games array\n");
        free(folder_tree_root);
        folder_tree_root = NULL;
        return;
    }
    folder_tree_root->num_games = 0;

    for (int i = 1; i < num_items_BASE; i++) {
        gd_item* item = &gd_slots_BASE[i];

        /* Register primary folder */
        folder_register_item(item, item->folder, i);

        /* Register alt folders (folder_alt1..folder_alt5 from INI) */
        folder_alt_entry_t* alts = folder_alt_find(i + 1); /* slot is 1-indexed */
        if (alts) {
            for (int a = 0; a < alts->count; a++) {
                if (alts->paths[a][0] == '\0') {
                    continue; /* skip unset/gap entries */
                }
                folder_register_item(item, alts->paths[a], i);
            }
        }
    }

    /* Free alt folder table, no longer needed after tree build */
    free(folder_alt_entries);
    folder_alt_entries = NULL;
    folder_alt_count = 0;
    folder_alt_capacity = 0;

    folder_state.depth = 0;
    folder_state.path[0] = '\0';

    printf("Info: Folder tree built successfully\n");
}

void
list_set_folder_root(void) {
    printf("list_set_folder_root: Starting\n");
    if (!folder_tree_root) {
        printf("list_set_folder_root: No folder tree, using default sort\n");
        list_set_sort_default();
        return;
    }

    printf("list_set_folder_root: Building folder view, root has %d children and %d games\n",
           folder_tree_root->num_children, folder_tree_root->num_games);

#ifndef STANDALONE_BINARY
    int hide_multidisc = sf_multidisc[0];
#else
    int hide_multidisc = 1;
#endif

    int temp_idx = 0;
    folder_items_count = 0;

    for (int i = 0; i < folder_tree_root->num_children; i++) {
        if (folder_items_count >= MAX_FOLDER_NODES) {
            break;
        }

        /* Skip empty subfolders (no visible games or nested content) */
        if (!folder_has_visible_content(folder_tree_root->children[i], hide_multidisc)) {
            continue;
        }

        gd_item* folder_entry = &folder_items[folder_items_count++];
        memset(folder_entry, 0, sizeof(gd_item));

        snprintf(folder_entry->name, 128, "[%s]", folder_tree_root->children[i]->name);
        strcpy(folder_entry->disc, "DIR");
        folder_entry->product[0] = 'F';
        folder_entry->slot_num = folder_tree_root->children[i]->first_seen_slot;

        list_temp[temp_idx++] = folder_entry;
    }

    for (int i = 0; i < folder_tree_root->num_games; i++) {
        gd_item* game = folder_tree_root->games[i];

        if (!folder_game_visible(folder_tree_root, game, hide_multidisc)) {
            continue;
        }

        list_temp[temp_idx++] = game;
    }

    printf("list_set_folder_root: Sorting %d items\n", temp_idx);
    qsort(list_temp, temp_idx, sizeof(gd_item*), folder_cmp);

    list_current = list_temp;
    num_items_current = num_items_temp = temp_idx;

    folder_current_node = folder_tree_root;
    folder_state.depth = 0;
    folder_state.path[0] = '\0';

    printf("list_set_folder_root: Complete, %d items in list\n", temp_idx);
}

void
list_set_folder_path(const char* path) {
    if (!folder_tree_root) {
        list_set_sort_default();
        return;
    }

    folder_node_t* node = folder_find_by_path(folder_tree_root, path);
    if (!node) {
        list_set_folder_root();
        return;
    }

    folder_current_node = node;

#ifndef STANDALONE_BINARY
    int hide_multidisc = sf_multidisc[0];
#else
    int hide_multidisc = 1;
#endif

    int temp_idx = 0;

    if (folder_state.depth > 0) {
        list_temp[temp_idx++] = &parent_button;
    }

    folder_items_count = 0;

    for (int i = 0; i < node->num_children; i++) {
        if (folder_items_count >= MAX_FOLDER_NODES) {
            break;
        }

        /* Skip empty subfolders (no visible games or nested content) */
        if (!folder_has_visible_content(node->children[i], hide_multidisc)) {
            continue;
        }

        gd_item* folder_entry = &folder_items[folder_items_count++];
        memset(folder_entry, 0, sizeof(gd_item));

        snprintf(folder_entry->name, 128, "[%s]", node->children[i]->name);
        strcpy(folder_entry->disc, "DIR");
        folder_entry->product[0] = 'F';
        folder_entry->slot_num = node->children[i]->first_seen_slot;

        list_temp[temp_idx++] = folder_entry;
    }

    for (int i = 0; i < node->num_games; i++) {
        gd_item* game = node->games[i];

        if (!folder_game_visible(node, game, hide_multidisc)) {
            continue;
        }

        list_temp[temp_idx++] = game;
    }

    qsort(list_temp, temp_idx, sizeof(gd_item*), folder_cmp);

    list_current = list_temp;
    num_items_current = num_items_temp = temp_idx;
}

void
list_folder_enter(const char* folder_name, int cursor_pos) {
    if (!folder_tree_root || folder_state.depth >= MAX_FOLDER_DEPTH || !folder_name) {
        return;
    }

    folder_node_t* current_node = folder_find_by_path(folder_tree_root, folder_state.path);
    if (!current_node) {
        return;
    }

    /* Find child folder by name */
    folder_node_t* target_folder = NULL;
    for (int i = 0; i < current_node->num_children; i++) {
        if (strcmp(current_node->children[i]->name, folder_name) == 0) {
            target_folder = current_node->children[i];
            break;
        }
    }

    if (!target_folder) {
        return; /* Folder not found */
    }

    /* Save cursor position before descending */
    folder_state.cursor_positions[folder_state.depth] = cursor_pos;

    strncpy(folder_state.breadcrumbs[folder_state.depth], target_folder->name, 255);
    folder_state.breadcrumbs[folder_state.depth][255] = '\0';
    folder_state.depth++;

    folder_state.path[0] = '\0';
    for (int i = 0; i < folder_state.depth; i++) {
        if (i > 0) {
            strcat(folder_state.path, "\\");
        }
        strcat(folder_state.path, folder_state.breadcrumbs[i]);
    }

    list_set_folder_path(folder_state.path);
}

int
list_folder_get_stats(const char* folder_name, int* num_subfolders, int* num_games) {
    if (!folder_tree_root || !folder_name || !num_subfolders || !num_games) {
        return -1;
    }

    folder_node_t* current_node = folder_find_by_path(folder_tree_root, folder_state.path);
    if (!current_node) {
        return -1;
    }

#ifndef STANDALONE_BINARY
    int hide_multidisc = sf_multidisc[0];
#else
    int hide_multidisc = 1;
#endif

    /* Find child folder by name */
    for (int i = 0; i < current_node->num_children; i++) {
        if (strcmp(current_node->children[i]->name, folder_name) == 0) {
            folder_node_t* child = current_node->children[i];

            /* Count visible subfolders */
            int visible_subfolders = 0;
            for (int j = 0; j < child->num_children; j++) {
                if (folder_has_visible_content(child->children[j], hide_multidisc)) {
                    visible_subfolders++;
                }
            }
            *num_subfolders = visible_subfolders;

            /* Count visible games */
            *num_games = folder_count_visible_games(child, hide_multidisc);
            return 0;
        }
    }

    return -1; /* Folder not found */
}

int
list_folder_go_back(void) {
    int saved_cursor_pos = 0;

    if (folder_state.depth > 0) {
        folder_state.depth--;

        folder_state.path[0] = '\0';
        for (int i = 0; i < folder_state.depth; i++) {
            if (i > 0) {
                strcat(folder_state.path, "\\");
            }
            strcat(folder_state.path, folder_state.breadcrumbs[i]);
        }

        list_set_folder_path(folder_state.path);

        /* Retrieve saved cursor position with bounds checking */
        saved_cursor_pos = folder_state.cursor_positions[folder_state.depth];
        if (saved_cursor_pos >= num_items_current) {
            saved_cursor_pos = num_items_current - 1;
        }
        if (saved_cursor_pos < 0) {
            saved_cursor_pos = 0;
        }
    }

    return saved_cursor_pos;
}

int
list_folder_get_depth(void) {
    return folder_state.depth;
}

int
list_folder_is_root(void) {
    return folder_state.depth == 0;
}

void
list_folder_destroy(void) {
    if (folder_tree_root) {
        folder_tree_destroy_recursive(folder_tree_root);
        folder_tree_root = NULL;
    }

    folder_state.depth = 0;
    folder_state.path[0] = '\0';
    folder_items_count = 0;
}
