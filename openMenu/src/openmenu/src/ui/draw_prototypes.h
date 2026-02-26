/*
 * File: draw_prototypes.h
 * Project: ui
 * File Created: Wednesday, 19th May 2021 9:33:13 pm
 * Author: Hayden Kowalchuk
 * -----
 * Copyright (c) 2021 Hayden Kowalchuk, Hayden Kowalchuk
 * License: BSD 3-clause "New" or "Revised" License, http://www.opensource.org/licenses/BSD-3-Clause
 */

#pragma once

#include <stdint.h> //For the uintX_t types

#include "common.h"
#ifdef _arch_dreamcast
#include "draw_kos.h"
#else
#include "draw_kos.h" // #include "draw_console.h"
#endif

struct dat_file;

typedef struct dimen_RECT {
    int16_t x;
    int16_t y;
    int16_t w;
    int16_t h;
} dimen_RECT;

/* Called only once at start */
void draw_init(void);

/* called at the start of each frame */
void draw_setup(void);

/* Controls which list we are drawing into */
void draw_set_list(int list);
int draw_get_list(void);

/* returns default missing texture */
void* draw_load_missing_icon(void* user);
/* Throws pass whatever is relevant to your platform as a pointer and it will filled + returned if successfull,
 * otherwise NULL */
void* draw_load_texture(const char* filename, void* user);
void* draw_load_texture_buffer(const char* filename, void* user, void* buffer);
/* Loads from new DAT file using struct + ID of file requested */
void* draw_load_texture_from_DAT_to_buffer(const struct dat_file* bin, const char* ID, void* user, void* buffer);

/* draws an image at coords of a given size */
void draw_draw_image(int x, int y, float width, float height, uint32_t color, void* user);
/* draws an image centered at coords of a given size */
void draw_draw_image_centered(int x, int y, float width, float height, uint32_t color, void* user);
/* draws an image at coords as a square */
void draw_draw_square(int x, int y, float size, uint32_t color, void* user);
/* Draws part of an image specified in rect at the given coords of size */
void draw_draw_sub_image(int x, int y, float width, float height, uint32_t color, void* user, const dimen_RECT* rect);

/* Draws untextured quad at coords with size and color(rgba) */
void draw_draw_quad(int x, int y, float width, float height, uint32_t color);

/* exec proto */
struct gd_item;
void bloom_launch(const struct gd_item* disc);
void bleem_launch(const struct gd_item* disc);
void dreamcast_launch_disc(const struct gd_item* disc);
void dreamcast_launch_cb(const struct gd_item* disc);

/* PSX launcher support */
void check_bloom_available(void);
int is_bloom_available(void);

/* z depth */
float z_get(void);
float z_set_cond(float z);
float z_set(float z);
float z_inc(void);
void z_reset(void);
