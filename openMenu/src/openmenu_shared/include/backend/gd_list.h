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

struct gd_item;
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
