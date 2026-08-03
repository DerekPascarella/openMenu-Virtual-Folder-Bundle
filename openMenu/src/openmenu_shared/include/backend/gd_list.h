/*
 * File: gd_list.h
 * Project: ini_parse
 * File Created: Wednesday, 19th May 2021 5:33:33 pm
 * Author: Hayden Kowalchuk
 * -----
 * Copyright (c) 2021 Hayden Kowalchuk, Hayden Kowalchuk
 * License: BSD 3-clause "New" or "Revised" License, http://www.opensource.org/licenses/BSD-3-Clause
 */

#pragma once

#define LIST_FOLDER_PATH_MAX 512

struct gd_item;

/* What the list is showing right now, so a launch can be traced back to
 * the view it came from */
typedef enum LIST_VIEW_KIND {
    LIST_VIEW_FLAT = 0, /* games straight up */
    LIST_VIEW_CATEGORY, /* the letter, region or genre rows */
    LIST_VIEW_DRILL,    /* games inside one of those categories */
    LIST_VIEW_FOLDER,
    LIST_VIEW_RECENT
} LIST_VIEW_KIND;

typedef struct list_view {
    int kind;
    char drill_type; /* 'A', 'R' or 'G', zero when not drilled */
    int drill_num;
    const char* folder_path;
} list_view;

void list_view_get(struct list_view* out);

int list_read(const char* filename);
int list_read_default(void);
void list_destroy(void);
void list_print_slots(void);
void list_print_temp(void);
void list_print(const struct gd_item** list);

/* simple sorting methods */
const struct gd_item** list_get(void);
void list_set_sort_name(void);
void list_set_sort_region(void);
void list_set_sort_genre(void);
void list_set_sort_default(void);
void list_set_sort_alphabetical(void);
/* complex filtering and sorting */
void list_set_genre(int genre);
void list_set_genre_sort(int genre, int sort);
void list_set_sort_filter(const char type, int num);
/* Grab multidisc games */
void list_set_multidisc(const char* product_id);
void list_set_multidisc_filtered(const char* product_id, const char* folder_path);
const struct gd_item** list_get_multidisc(void);
int list_count_multidisc_filtered(const char* product_id, const char* folder_path);
void list_set_multidisc_in_folder(const char* product_id);
int list_count_multidisc_in_folder(const char* product_id);

int list_length(void);
int list_multidisc_length(void);
const struct gd_item* list_item_get(int idx);

/* Recently played view */
void list_set_recent(void);
int list_recent_count(void);
struct gd_item** list_recent_entries(int* count);

/* Finding a game again after a reboot */
const struct gd_item* list_find_by_hash(unsigned int hash);
const struct gd_item* list_find_by_product(const char* product);
const struct gd_item* list_visible_disc(const struct gd_item* item);
int list_index_of(const struct gd_item* item);
int list_index_of_product(const char* product);

/* Folder navigation functions */
void list_folder_init(void);
void list_set_folder_root(void);
void list_set_folder_path(const char* path);
void list_folder_enter(const char* folder_name, int cursor_pos);
int list_folder_get_stats(const char* folder_name, int* num_subfolders, int* num_games);
int list_folder_go_back(void);
int list_folder_get_depth(void);
int list_folder_is_root(void);
void list_folder_destroy(void);
unsigned int list_folder_path_hash(const char* path);
int list_folder_path_by_hash(unsigned int hash, char* out, int out_size);
int list_folder_contains(const char* path, const struct gd_item* item);
int list_folder_enter_path(const char* path);
