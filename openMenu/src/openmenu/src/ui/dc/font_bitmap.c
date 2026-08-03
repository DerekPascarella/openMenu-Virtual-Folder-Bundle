/*
 * File: font.c
 * Project: ui
 * File Created: Monday, 3rd June 2019 1:00:17 pm
 * Author: Hayden Kowalchuk (hayden@hkowsoftware.com)
 * -----
 * Copyright (c) 2019 Hayden Kowalchuk
 */

#include <dc/pvr.h>

#include <stdlib.h>

#include <dbgprint.h>
#include "font_smooth.h"
#include "ui/draw_prototypes.h"

typedef struct bitmap_font {
    int char_width;
    int char_height;
    int cell_pitch;
    image texture;
} bitmap_font;

static bitmap_font font;
static uint32_t font_color;

#define FONT_PERROW(font) (font.texture.width / font.cell_pitch)
#define BUFFER_MAX_CHARS  (128)

#ifdef KOS_SPRITE
static pvr_sprite_hdr_t font_header;
#define VERT_PER_CHAR (1)
static pvr_sprite_txr_t charbuf[BUFFER_MAX_CHARS * VERT_PER_CHAR] __attribute__((aligned(32)));
#else
static pvr_poly_hdr_t font_header;
#define VERT_PER_CHAR (4)
static pvr_vertex_t charbuf[BUFFER_MAX_CHARS * VERT_PER_CHAR] __attribute__((aligned(32)));
#endif

static int charbuffered;

int
font_bmp_init(const char* filename, int char_width, int char_height) {
    unsigned int temp = texman_create();

    font.char_height = char_height;
    font.char_width = char_width;
    font.cell_pitch = char_width;

    uint32_t w, h;
    uint8_t pixfmt, datafmt;
    void* ram = load_pvr_to_ram(filename, &w, &h, &pixfmt, &datafmt);

    if (ram && font_smooth_eligible(pixfmt, datafmt, w, h, char_width, char_height)) {
        /* Respace the glyphs with transparent gutters so bilinear sampling
           during smooth scrolling never reads a neighboring glyph */
        uint16_t* packed = malloc(w * 2 * h * sizeof(uint16_t));
        if (packed && font_smooth_repack(ram, w, h, datafmt, char_width, char_height, packed) == 0) {
            pvr_txr_load(packed, texman_get_tex_data(temp), w * 2 * h * 2);
            free(packed);

            uint32_t color = PVR_TXRFMT_ARGB1555;
            if (pixfmt == FONT_PVR_PIX_RGB565) {
                color = PVR_TXRFMT_RGB565;
            } else if (pixfmt == FONT_PVR_PIX_ARGB4444) {
                color = PVR_TXRFMT_ARGB4444;
            }

            font.texture.width = w * 2;
            font.texture.height = h;
            font.texture.format = color | PVR_TXRFMT_NONTWIDDLED;
            font.texture.texture = texman_get_tex_data(temp);
            font.cell_pitch = char_width * 2;
            texman_reserve_memory(font.texture.width, font.texture.height, 2 /* 16Bit */);

            font_color = 0xFFFFFFFF; // White

            return 0;
        }
        free(packed);
    }

    /* Anything unusual loads exactly the way it always has */
    draw_load_texture_buffer(filename, &font.texture, texman_get_tex_data(temp));
    texman_reserve_memory(font.texture.width, font.texture.height, 2 /* 16Bit */);

    font_color = 0xFFFFFFFF; // White

    return 0;
}

void
font_bmp_begin_draw() {
    /* Make a polygon header */
#ifdef KOS_SPRITE
    pvr_sprite_cxt_t tmp;
    pvr_sprite_cxt_txr(&tmp, draw_get_list(), font.texture.format, font.texture.width, font.texture.height,
                       font.texture.texture, PVR_FILTER_NONE);
    pvr_sprite_compile(&font_header, &tmp);
#else
    pvr_poly_cxt_t tmp;
    pvr_poly_cxt_txr(&tmp, draw_get_list(), font.texture.format, font.texture.width, font.texture.height,
                     font.texture.texture, PVR_FILTER_BILINEAR);
    pvr_poly_compile(&font_header, &tmp);
#endif
}

void
font_bmp_set_color(uint32_t color) {
    /*@Note: Either lxdream-nitro weirdness or something is wrong in how we draw,
     * set both to 0xFFFFFFFF */
    font_color = color;
#ifdef KOS_SPRITE
    font_header.argb = color;
#endif
    /* Start a textured polygon set (with the font texture and color) */
    pvr_prim(&font_header, sizeof(font_header));
}

void
font_bmp_set_color_default(void) {
    font_bmp_set_color(0xFFFFFFFF);
}

void
font_bmp_set_color_components(int r, int g, int b, int a) {
    font_color = PVR_PACK_ARGB(a, r, g, b);
}

/* Emits one glyph quad with explicit screen and texel extents */
static void
font_bmp_emit(float x1, float y1, float x2, float y2, float u1, float v1, float u2, float v2) {
    const float z = z_get();

#ifdef KOS_SPRITE
    pvr_sprite_txr_t vert = {
        .flags = PVR_CMD_VERTEX_EOL, /* Always? */
        /*  upper left */
        .ax = x1,
        .ay = y1,
        .az = z,
        /* upper right */
        .bx = x2,
        .by = y1,
        .bz = z,
        /* lower left */
        .cx = x2,
        .cy = y2,
        .cz = z,
        /* interpolated */
        .dx = x1,
        .dy = y2,
        .auv = PVR_PACK_16BIT_UV(u1, v1), /* UVS */
        .buv = PVR_PACK_16BIT_UV(u2, v1), /* UVS */
        .cuv = PVR_PACK_16BIT_UV(u2, v2), /* UVS */
    };

    charbuf[charbuffered] = vert;
#else
    pvr_vertex_t *vert1, *vert2, *vert3, *vert4;
    vert1 = &charbuf[charbuffered + 0];
    vert2 = &charbuf[charbuffered + 1];
    vert3 = &charbuf[charbuffered + 2];
    vert4 = &charbuf[charbuffered + 3];

    vert1->flags = PVR_CMD_VERTEX;
    vert1->x = x1;
    vert1->y = y2;
    vert1->z = z;
    vert1->u = u1;
    vert1->v = v2;
    vert1->argb = font_color;
    vert1->oargb = 0;

    vert2->flags = PVR_CMD_VERTEX;
    vert2->x = x1;
    vert2->y = y1;
    vert2->z = z;
    vert2->u = u1;
    vert2->v = v1;
    vert2->argb = font_color;
    vert2->oargb = 0;

    vert3->flags = PVR_CMD_VERTEX;
    vert3->x = x2;
    vert3->y = y2;
    vert3->z = z;
    vert3->u = u2;
    vert3->v = v2;
    vert3->argb = font_color;
    vert3->oargb = 0;

    vert4->flags = PVR_CMD_VERTEX_EOL;
    vert4->x = x2;
    vert4->y = y1;
    vert4->z = z;
    vert4->u = u2;
    vert4->v = v1;
    vert4->argb = font_color;
    vert4->oargb = 0;
#endif
    charbuffered += VERT_PER_CHAR;
}

/* Draws a font letter using two triangle strips */
static void
font_bmp_draw_char(int x, int y, unsigned char ch) {
    const int index = ch - 32;

    if (index < 0) {
        return;
    }

    const int ix = (index % FONT_PERROW(font)) * font.cell_pitch;
    const int iy = (index / FONT_PERROW(font)) * font.char_height;

    font_bmp_emit((float)x, (float)y, (float)(x + font.char_width), (float)(y + font.char_height),
                  ix * 1.0f / font.texture.width, iy * 1.0f / font.texture.height,
                  (ix + font.char_width) * 1.0f / font.texture.width,
                  (iy + font.char_height) * 1.0f / font.texture.height);
}

static void
_font_bmp_draw_string(int x1, int y1, const char* str) {
    z_inc();
    charbuffered = 0;

    do {
        unsigned char chr = (*str);
        font_bmp_draw_char(x1, y1, chr);
        x1 += (int)(font.char_width);
    } while (*++str);
    pvr_prim(charbuf, charbuffered * sizeof(charbuf[0]));
}

/* @Note: revisit this */
void
font_bmp_draw_sub_wrap(int x1, int y1, int width, const char* str) {
    (void)width;
    _font_bmp_draw_string(x1, y1, str);
}

void
font_bmp_draw_main(int x1, int y1, const char* str) {
    _font_bmp_draw_string(x1, y1, str);
}

/* Draws a string clipped to a pixel window, shifted left by scroll_px.
   Fractional offsets land between pixels which is what makes the marquee
   glide instead of step. */
void
font_bmp_draw_window(int x, int y, int window_w, float scroll_px, const char* str) {
    z_inc();
    charbuffered = 0;

    const float win_x1 = (float)x;
    const float win_x2 = (float)(x + window_w);
    const float texw = (float)font.texture.width;
    const float texh = (float)font.texture.height;

    for (int i = 0; str[i] && charbuffered < BUFFER_MAX_CHARS * VERT_PER_CHAR; i++) {
        const int index = (unsigned char)str[i] - 32;
        if (index < 0) {
            continue;
        }

        float gx = (float)x + (float)(i * font.char_width) - scroll_px;
        float x1, x2, t1, t2;
        if (!font_smooth_clip(gx, font.char_width, win_x1, win_x2, &x1, &x2, &t1, &t2)) {
            continue;
        }

        const int ix = (index % FONT_PERROW(font)) * font.cell_pitch;
        const int iy = (index / FONT_PERROW(font)) * font.char_height;

        font_bmp_emit(x1, (float)y, x2, (float)(y + font.char_height), ((float)ix + t1) / texw, (float)iy / texh,
                      ((float)ix + t2) / texw, (float)(iy + font.char_height) / texh);
    }

    if (charbuffered) {
        pvr_prim(charbuf, charbuffered * sizeof(charbuf[0]));
    }
}

void
font_draw_sub(int x1, int y1, const char* str) {
    _font_bmp_draw_string(x1, y1, str);
}
