/*
 * File: db_list.c
 * Project: backend
 * File Created: Wednesday, 16th June 2021 10:32:39 pm
 * Author: Hayden Kowalchuk
 * -----
 * Copyright (c) 2021 Hayden Kowalchuk, Hayden Kowalchuk
 * License: BSD 3-clause "New" or "Revised" License,
 * http://www.opensource.org/licenses/BSD-3-Clause
 */

#include <stdio.h>

#include "backend/dat_format.h"
#include "backend/db_item.h"
#include "backend/db_list.h"
#include "texture/serial_sanitize.h"

static dat_file dat_meta;
static db_item* db;
static int dat_first_index;

int
db_load_DAT(void) {
    DAT_init(&dat_meta);
    DAT_load_parse(&dat_meta, "META.DAT");
    dat_first_index = dat_meta.items[0].offset;

    /* Read DAT to db, but use Hash table to quickly search */
    db = malloc(dat_meta.num_chunks * sizeof(db_item));
    if (!db) {
        printf("%s no free memory\n", __func__);
        return 0;
    }
    fs_read(dat_meta.handle, db, dat_meta.num_chunks * sizeof(db_item));
    fs_close(dat_meta.handle);

    DAT_info(&dat_meta);

    return 0;
}

/* Returns 0 on success and places a pointer in item, otherwise returns 1 and
 * item = NULL */
int
db_get_meta(const char* id, struct db_item** item) {
    const char* id_santized = serial_santize_meta(id);
    uint32_t index = DAT_get_index_by_ID(&dat_meta, id_santized);

    if (index == 0xFFFFFFFF) {
        *item = NULL;
        return 1;
    }

    *item = &db[index - dat_first_index];
    return 0;
}

const char*
db_format_nplayers_str(int nplayers) {
    static char str[10];
    sprintf(str, "%d Player%c", nplayers, (nplayers > 1 ? 's' : '\0'));
    return str;
}

const char*
db_format_vmu_blocks_str(int num_blocks) {
    static char str[12];
    sprintf(str, "%d Block%c", num_blocks, (num_blocks != 1 ? 's' : '\0'));
    return str;
}
