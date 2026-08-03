/*
 * File: font_smooth.h
 * Project: ui
 * Pure helpers for smooth marquee text. Atlas gutter repacking keeps
 * bilinear sampling inside each glyph cell and the clip helper trims
 * glyph quads to the scroll window.
 */

#pragma once

#include <stdint.h>

/* PVR header format bytes as stored in the file */
#define FONT_PVR_PIX_ARGB1555 0x00
#define FONT_PVR_PIX_RGB565   0x01
#define FONT_PVR_PIX_ARGB4444 0x02
#define FONT_PVR_DAT_TWIDDLED 0x01
#define FONT_PVR_DAT_RECT     0x09
#define FONT_PVR_DAT_RECT_TW  0x0D

int font_smooth_eligible(uint8_t pixfmt, uint8_t datafmt, uint32_t w, uint32_t h, int char_w, int char_h);

int font_smooth_repack(const uint16_t* src, uint32_t w, uint32_t h, uint8_t datafmt, int char_w, int char_h,
                       uint16_t* dst);

int font_smooth_clip(float glyph_x, int char_w, float win_x1, float win_x2, float* x1, float* x2, float* t1, float* t2);
