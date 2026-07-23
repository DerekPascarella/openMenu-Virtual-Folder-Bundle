/*
 * File: font_prototypes.h
 * Project: ui
 * File Created: Thursday, 20th May 2021 12:35:54 am
 * Author: Hayden Kowalchuk
 * -----
 * Copyright (c) 2021 Hayden Kowalchuk, Hayden Kowalchuk
 * License: BSD 3-clause "New" or "Revised" License, http://www.opensource.org/licenses/BSD-3-Clause
 */

#pragma once

#include <stdint.h> //For the uintX_t types

/* BMF formatted nice text */
int font_bmf_init(const char* fnt, const char* texture, int is_wide);
void font_bmf_destroy(void);

void font_bmf_begin_draw(void);
void font_bmf_set_scale(float scale);
void font_bmf_set_height_default(void);
void font_bmf_set_height(float height);

void font_bmf_draw(int x, int y, uint32_t color, const char* str);
void font_bmf_draw_main(int x, int y, uint32_t color, const char* str);
void font_bmf_draw_sub(int x, int y, uint32_t color, const char* str);
void font_bmf_draw_sub_wrap(int x, int y, uint32_t color, const char* str, int width);
void font_bmf_draw_auto_size(int x, int y, uint32_t color, const char* str, int width);
void font_bmf_draw_centered(int x, int y, uint32_t color, const char* str);
void font_bmf_draw_centered_auto_size(int x, int y, uint32_t color, const char* str, int width);
void font_bmf_draw_right(int x, int y, uint32_t color, const char* str);

/* Basic fixed width bitmap font */
int font_bmp_init(const char* filename, int char_width, int char_height);
void font_bmp_destroy(void);

void font_bmp_begin_draw(void);
void font_bmp_set_color(uint32_t color);
void font_bmp_set_color_components(int r, int g, int b, int a);

void font_bmp_draw_main(int x, int y, const char* str);
void font_bmp_draw_sub(int x, int y, const char* str);
void font_bmp_draw_sub_wrap(int x, int y, const char* str, int width);
void font_bmp_draw_auto_size(int x, int y, const char* str, int width);
void font_bmp_draw_centered(int x, int y, const char* str);
