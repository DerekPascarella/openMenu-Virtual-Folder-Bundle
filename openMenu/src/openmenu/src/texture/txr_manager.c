/*
 * File: txr_manager.c
 * Project: texture
 * File Created: Friday, 21st May 2021 2:30:13 pm
 * Author: Hayden Kowalchuk
 * -----
 * Copyright (c) 2021 Hayden Kowalchuk, Hayden Kowalchuk
 * License: BSD 3-clause "New" or "Revised" License,
 * http://www.opensource.org/licenses/BSD-3-Clause
 */

#include <stdio.h>
#include <string.h>

#include <dc/pvr.h>

#include <backend/dat_format.h>
#include <texture/serial_sanitize.h>
#include "block_pool.h"
#include "lru.h"
#include "ui/draw_kos.h"
#include "ui/draw_prototypes.h"

#include "txr_manager.h"

/* CFG for small pvr pool (128x128 16bit, 16 spaces) */
#define SM_SLOT_NUM  (16)
#define SM_SLOT_SIZE (128 * 128 * 2)
#define SM_POOL_SIZE (SM_SLOT_NUM * SM_SLOT_SIZE * sizeof(char))

/* CFG for large pvr pool (256x256 16bit, 4 spaces) */
#define LG_SLOT_NUM  (4)
#define LG_SLOT_SIZE (256 * 256 * 2)
#define LG_POOL_SIZE (LG_SLOT_NUM * LG_SLOT_SIZE * sizeof(char))

typedef struct dat_system {
    cache_instance cache;
    block_pool pool;
    struct dat_file addon;
    struct dat_file primary;
} dat_system;

static dat_system icon_system;
static dat_system box_system;

/* Folder artwork entries, box format, shares the large pool and cache */
static struct dat_file folder_dat;

unsigned int
block_pool_add_cb(const char* key, void* user) {
    /* unused here but could be good info to know */
    (void)key;

    block_pool* pool = (block_pool*)user;
    unsigned int ret;
    char* ptr;
    pool_get_next_free(pool, &ret, (void**)&ptr);
    return ret;
}

unsigned int
block_pool_del_cb(const char* key, void* value, void* user) {
    /* unused here but could be good info to know */
    (void)key;

    block_pool* pool = (block_pool*)user;
    unsigned int slot_num = *(unsigned int*)value;
    pool_dealloc_slot(pool, slot_num);
    return 0;
}

int
txr_load_DATs(void) {
    serial_sanitizer_init(); /*@Todo: Move this */

    DAT_init(&icon_system.addon);
    DAT_init(&icon_system.primary);
    DAT_init(&box_system.addon);
    DAT_init(&box_system.primary);
    DAT_init(&folder_dat);

    DAT_load_parse(&icon_system.primary, "ICON.DAT");
    DAT_load_parse(&box_system.primary, "BOX.DAT");
    DAT_load_parse(&icon_system.addon, "ICON_EX.DAT");
    DAT_load_parse(&box_system.addon, "BOX_EX.DAT");
    DAT_load_parse(&folder_dat, "FOLDRART.DAT");

    return 0;
}

int
txr_create_small_pool(void) {
    void* buffer = pvr_mem_malloc(SM_POOL_SIZE);
    pool_create(&icon_system.pool, buffer, SM_POOL_SIZE, SM_SLOT_NUM);
    icon_system.cache.cache = NULL;
    cache_set_size(&icon_system.cache, SM_SLOT_NUM);
    cache_callback_userdata(&icon_system.cache, &icon_system.pool);
    cache_callback_add(&icon_system.cache, block_pool_add_cb);
    cache_callback_del(&icon_system.cache, block_pool_del_cb);

    return 0;
}

/* pool_destroy_user(&pvr_small, pvr_mem_free); */

int
txr_create_large_pool(void) {
    void* buffer = pvr_mem_malloc(LG_POOL_SIZE);
    pool_create(&box_system.pool, buffer, LG_POOL_SIZE, LG_SLOT_NUM);
    box_system.cache.cache = NULL;
    cache_set_size(&box_system.cache, LG_SLOT_NUM);
    cache_callback_userdata(&box_system.cache, &box_system.pool);
    cache_callback_add(&box_system.cache, block_pool_add_cb);
    cache_callback_del(&box_system.cache, block_pool_del_cb);
    return 0;
}

void
txr_empty_small_pool(void) {
    empty_cache(&icon_system.cache);
    pool_dealloc_all(&icon_system.pool);
}

void
txr_empty_large_pool(void) {
    empty_cache(&box_system.cache);
    pool_dealloc_all(&box_system.pool);
}

static int
txr_get_from_dat_set(const char* id, struct image* img, dat_system* system) {
    void* txr_ptr;
    int slot_num;
    const char* id_santized = serial_santize_art(id);

    /* Initially check addon then fall back to regular */
    uint32_t temp_offset = DAT_get_offset_by_ID(&system->addon, id_santized);
    const dat_file* dat_source = &system->addon;
    if (!temp_offset) {
        temp_offset = DAT_get_offset_by_ID(&system->primary, id_santized);
        dat_source = &system->primary;
        if (!temp_offset) {
            dat_source = NULL;
        }
    }

    /* check if exists in DAT and if not, return missing image */
    if (!dat_source) {
        draw_load_missing_icon(img);
        return 0;
    }
    slot_num = find_in_cache(&system->cache, id_santized);
    if (slot_num == -1) {
        add_to_cache(&system->cache, id_santized, 0);
        slot_num = find_in_cache(&system->cache, id_santized);
        txr_ptr = pool_get_slot_addr(&system->pool, slot_num);

        draw_load_texture_from_DAT_to_buffer(dat_source, id_santized, img, txr_ptr);
        pool_set_slot_format(&system->pool, slot_num, img->width, img->height, img->format);
    } else {
        const slot_format* fmt = pool_get_slot_format(&system->pool, slot_num);
        img->width = fmt->width;
        img->height = fmt->height;
        img->format = fmt->format;
        img->texture = pool_get_slot_addr(&system->pool, slot_num);
    }
    return 0;
}

/*
called with "T1121.pvr" and a pointer to pointer to vram
returns pointer to use for texture upload/reference
 */
int
txr_get_small(const char* id, struct image* img) {
    return txr_get_from_dat_set(id, img, &icon_system);
}

int
txr_get_large(const char* id, struct image* img) {
    return txr_get_from_dat_set(id, img, &box_system);
}

/* Folder artwork lookup. Searches only FOLDRART.DAT but shares the large
 * pool and cache. Keys are hashed folder paths so no serial sanitizing. */
int
txr_get_folder(const char* id, struct image* img) {
    void* txr_ptr;
    int slot_num;

    uint32_t temp_offset = DAT_get_offset_by_ID(&folder_dat, id);
    if (!temp_offset) {
        draw_load_missing_icon(img);
        return 0;
    }
    slot_num = find_in_cache(&box_system.cache, id);
    if (slot_num == -1) {
        add_to_cache(&box_system.cache, id, 0);
        slot_num = find_in_cache(&box_system.cache, id);
        txr_ptr = pool_get_slot_addr(&box_system.pool, slot_num);

        draw_load_texture_from_DAT_to_buffer(&folder_dat, id, img, txr_ptr);
        pool_set_slot_format(&box_system.pool, slot_num, img->width, img->height, img->format);
    } else {
        const slot_format* fmt = pool_get_slot_format(&box_system.pool, slot_num);
        img->width = fmt->width;
        img->height = fmt->height;
        img->format = fmt->format;
        img->texture = pool_get_slot_addr(&box_system.pool, slot_num);
    }
    return 0;
}
