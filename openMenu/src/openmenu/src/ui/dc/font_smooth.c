/*
 * File: font_smooth.c
 * Project: ui
 * Pure helpers for smooth marquee text.
 */

#include <string.h>

#include "font_smooth.h"

static int
is_pow2(uint32_t v) {
    return v != 0 && (v & (v - 1)) == 0;
}

int
font_smooth_eligible(uint8_t pixfmt, uint8_t datafmt, uint32_t w, uint32_t h, int char_w, int char_h) {
    if (pixfmt != FONT_PVR_PIX_ARGB1555 && pixfmt != FONT_PVR_PIX_RGB565 && pixfmt != FONT_PVR_PIX_ARGB4444) {
        return 0;
    }
    if (datafmt != FONT_PVR_DAT_TWIDDLED && datafmt != FONT_PVR_DAT_RECT && datafmt != FONT_PVR_DAT_RECT_TW) {
        return 0;
    }
    if (!is_pow2(w) || !is_pow2(h)) {
        return 0;
    }
    if (w * 2 > 1024) {
        return 0;
    }
    if (char_w <= 0 || char_h <= 0 || (w % (uint32_t)char_w) || (h % (uint32_t)char_h)) {
        return 0;
    }
    return 1;
}

/* Splits an interleaved twiddle index back into x and y */
static void
untwiddle(uint32_t i, uint32_t* x, uint32_t* y) {
    uint32_t xv = 0, yv = 0;
    int bit = 0;
    while (i) {
        yv |= (i & 1) << bit;
        i >>= 1;
        xv |= (i & 1) << bit;
        i >>= 1;
        bit++;
    }
    *x = xv;
    *y = yv;
}

/* Doubles the cell pitch so every glyph gets a transparent gutter beside it */
static void
place(uint16_t* dst, uint32_t dst_w, uint32_t x, uint32_t y, int char_w, uint16_t px) {
    uint32_t cell = x / (uint32_t)char_w;
    uint32_t dx = cell * (uint32_t)(char_w * 2) + (x % (uint32_t)char_w);
    dst[y * dst_w + dx] = px;
}

int
font_smooth_repack(const uint16_t* src, uint32_t w, uint32_t h, uint8_t datafmt, int char_w, int char_h,
                   uint16_t* dst) {
    (void)char_h;
    const uint32_t dst_w = w * 2;
    memset(dst, 0, dst_w * h * sizeof(uint16_t));

    if (datafmt == FONT_PVR_DAT_RECT) {
        for (uint32_t y = 0; y < h; y++) {
            for (uint32_t x = 0; x < w; x++) {
                place(dst, dst_w, x, y, char_w, src[y * w + x]);
            }
        }
        return 0;
    }

    if (datafmt == FONT_PVR_DAT_TWIDDLED || datafmt == FONT_PVR_DAT_RECT_TW) {
        /* Rectangles store a row or column of square twiddled tiles */
        uint32_t tile = w < h ? w : h;
        uint32_t ntiles = (w * h) / (tile * tile);
        const uint16_t* p = src;
        for (uint32_t t = 0; t < ntiles; t++) {
            uint32_t ox = (w > h) ? t * tile : 0;
            uint32_t oy = (h > w) ? t * tile : 0;
            for (uint32_t i = 0; i < tile * tile; i++) {
                uint32_t x, y;
                untwiddle(i, &x, &y);
                place(dst, dst_w, ox + x, oy + y, char_w, *p++);
            }
        }
        return 0;
    }

    return -1;
}

int
font_smooth_clip(float glyph_x, int char_w, float win_x1, float win_x2, float* x1, float* x2, float* t1, float* t2) {
    float gx2 = glyph_x + (float)char_w;
    if (gx2 <= win_x1 || glyph_x >= win_x2) {
        return 0;
    }
    *x1 = glyph_x < win_x1 ? win_x1 : glyph_x;
    *x2 = gx2 > win_x2 ? win_x2 : gx2;
    *t1 = *x1 - glyph_x;
    *t2 = *x2 - glyph_x;
    return 1;
}
