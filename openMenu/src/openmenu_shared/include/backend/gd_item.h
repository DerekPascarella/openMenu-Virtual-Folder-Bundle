/*
 * File: item.h
 * Project: ini_parse
 * File Created: Wednesday, 19th May 2021 3:00:52 pm
 * Author: Hayden Kowalchuk
 * -----
 * Copyright (c) 2021 Hayden Kowalchuk, Hayden Kowalchuk
 * License: BSD 3-clause "New" or "Revised" License, http://www.opensource.org/licenses/BSD-3-Clause
 */

#pragma once

typedef struct gd_item {
    char name[128];
    char date[12];
    char product[12];
    char disc[8];
    char version[8];
    char region[4];
    unsigned int slot_num;
    char vga[1];
    char folder[512];
    char type[8];
} gd_item;

/* Identity hash for the recently played history. Built from serial, title,
 * and disc number so it survives SD card reordering. Zero is reserved to
 * mark empty history slots, so a real hash of zero is nudged to one. */
static inline unsigned int
gd_item_recent_hash(const struct gd_item* item) {
    unsigned int h = 2166136261u;
    const char* fields[3];
    fields[0] = item->product;
    fields[1] = item->name;
    fields[2] = item->disc;
    for (int f = 0; f < 3; f++) {
        const char* p = fields[f];
        while (*p) {
            h ^= (unsigned char)*p++;
            h *= 16777619u;
        }
        /* absorb a zero byte between fields so "ab" + "c" and "a" + "bc" differ */
        h *= 16777619u;
    }
    if (h == 0) {
        h = 1;
    }
    return h;
}

/* Helper functions to parse disc field "N/M" format (supports 1-10) */
static inline int
gd_item_disc_num(const char* disc) {
    /* Parse current disc number before '/' */
    int num = 0;
    const char* p = disc;
    while (*p && *p != '/') {
        if (*p >= '0' && *p <= '9') {
            num = num * 10 + (*p - '0');
        }
        p++;
    }
    return num;
}

static inline int
gd_item_disc_total(const char* disc) {
    /* Parse total disc count after '/' */
    const char* p = disc;
    while (*p && *p != '/') {
        p++;
    }
    if (*p == '/') {
        p++;
    }
    int num = 0;
    while (*p) {
        if (*p >= '0' && *p <= '9') {
            num = num * 10 + (*p - '0');
        }
        p++;
    }
    return num;
}
