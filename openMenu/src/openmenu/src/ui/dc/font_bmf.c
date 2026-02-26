/*
 * File: font.c
 * Project: ui
 * File Created: Monday, 3rd June 2019 1:00:17 pm
 * Author: Hayden Kowalchuk (hayden@hkowsoftware.com)
 * -----
 * Copyright (c) 2019 Hayden Kowalchuk
 */

#include <dc/pvr.h>
#include <kos/fs.h>
#include <stdbool.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include <dbgprint.h>
#include "ui/draw_prototypes.h"

#define PRINT_MEMBER(struct, member)                                                                                   \
    do {                                                                                                               \
        DBG_PRINT("%s: %d\n", #member, (int)((struct).member));                                                        \
    } while (0)

#define round(x)  (x)

/* BMFont implementation */
#define BMF_MAGIC (54938946) /* BMF(0x3) */

typedef struct bm_header {
    char bmf[3];
    uint8_t version;
} bm_header;

/* Binary font info structures */

// Tag header for each block type
typedef enum BM_BLOCK_TYPE { INFO = 1, COMMON, PAGES, CHARS, KERNING } BM_BLOCK_TYPE;

typedef struct __attribute__((__packed__)) bm_block_tag {
    uint8_t type;
    uint32_t size;
} bm_block_tag;

// Block type 1: info
#define INFO_NAME_OFFSET (14)

typedef struct __attribute__((__packed__)) bm_info {
    int16_t fontSize;
    uint8_t bitField;
    uint8_t charSet;
    uint16_t stretchH;
    uint8_t aa;
    uint8_t paddingUp;
    uint8_t paddingRight;
    uint8_t paddingDown;
    uint8_t paddingLeft;
    uint8_t spacingHoriz;
    uint8_t spacingVert;
    uint8_t outline;
    char fontName[64];
    /* VLA, size of block length - 14, null terminated string with length n */ // 64 for fix allign in stack
} bm_info;

// Block type 2: common
typedef struct __attribute__((__packed__)) bm_common {
    uint16_t lineHeight;
    uint16_t base;
    uint16_t scaleW;
    uint16_t scaleH;
    uint16_t pages;
    uint8_t bitField; // bits 0-6: reserved, bit 7: packed
    uint8_t alphaChnl;
    uint8_t redChnl;
    uint8_t greenChnl;
    uint8_t blueChnl;
} bm_common;

// Block type 3: pages
typedef struct __attribute__((__packed__)) bm_pages {
    int name_len;
    char pageNames[]; /*	p*(n+1)	strings	0	p null terminated strings, each
                       with length n */
} bm_pages;

// Block type 4: chars
typedef struct __attribute__((__packed__)) bm_char {
    uint32_t id;
    uint16_t x;
    uint16_t y;
    uint16_t width;
    uint16_t height;
    int16_t xoffset;
    int16_t yoffset;
    int16_t xadvance;
    uint8_t page;
    uint8_t chnl;
} bm_char;

// Block type 5: kerning pairs
typedef struct __attribute__((__packed__)) bm_kern_pair {
    uint32_t first;
    uint32_t second;
    int16_t amount;
} bm_kern_pair;

typedef struct __attribute__((__packed__)) bm_char_ex {
    uint32_t id;
    uint16_t x;
    uint16_t y;
    uint16_t width;
    uint16_t height;
    int16_t xoffset;
    int16_t yoffset;
    int16_t xadvance;
    uint8_t page;
    uint8_t chnl;
    bm_kern_pair* kerns;
} bm_char_ex;

typedef struct __attribute__((__packed__)) bm_font {
    uint16_t height;
    uint16_t width;
    uint32_t lineHeight;
    uint32_t fontSize;
    uint32_t num_chars;
    uint32_t num_kerns;
    bm_char_ex chars[256];
    bm_kern_pair* kerns;
} bm_font;

static bm_font font_basilea;
static int font_loaded = 0;
static float current_scale = 1.0;
static unsigned int current_color;

static int
BMF_parse_info(file_t fd, size_t block_size, bm_font* font) {
    DBG_PRINT("BMF found info block!\n");
    /* Unsure why youd want to have this around or on heap */
    static bm_info temp_info;
    fs_read(fd, &temp_info, block_size);

    /* Fix fontSize, its negative ? */
    temp_info.fontSize *= -1;

    font->fontSize = temp_info.fontSize;

    PRINT_MEMBER(temp_info, fontSize);
    PRINT_MEMBER(temp_info, bitField);
    PRINT_MEMBER(temp_info, charSet);
    PRINT_MEMBER(temp_info, stretchH);
    PRINT_MEMBER(temp_info, aa);
    PRINT_MEMBER(temp_info, paddingUp);
    PRINT_MEMBER(temp_info, paddingRight);
    PRINT_MEMBER(temp_info, paddingDown);
    PRINT_MEMBER(temp_info, paddingLeft);
    PRINT_MEMBER(temp_info, spacingHoriz);
    PRINT_MEMBER(temp_info, spacingVert);
    PRINT_MEMBER(temp_info, outline);
    DBG_PRINT("name: %s\n", temp_info.fontName);
    DBG_PRINT("\n");

    return 0;
}

static int
BMF_parse_common(file_t fd, size_t block_size, bm_font* font) {
    DBG_PRINT("BMF found common block!\n");
    bm_common temp_common;
    fs_read(fd, &temp_common, block_size);

    font->width = temp_common.scaleW;
    font->height = temp_common.scaleH;
    font->lineHeight = temp_common.lineHeight;

    PRINT_MEMBER(temp_common, lineHeight);
    PRINT_MEMBER(temp_common, base);
    PRINT_MEMBER(temp_common, scaleW);
    PRINT_MEMBER(temp_common, scaleH);
    PRINT_MEMBER(temp_common, pages);
    PRINT_MEMBER(temp_common, bitField);
    PRINT_MEMBER(temp_common, alphaChnl);
    PRINT_MEMBER(temp_common, redChnl);
    PRINT_MEMBER(temp_common, greenChnl);
    PRINT_MEMBER(temp_common, blueChnl);

    DBG_PRINT("\n");
    return 0;
}

static int
BMF_parse_pages(file_t fd, size_t block_size, bm_font* font) {
    DBG_PRINT("BMF found pages block!\n");
    (void)font;

    /* Do nothing! */
    fs_seek(fd, block_size, SEEK_CUR);

    DBG_PRINT("\n");

    return 0;
}

static int
BMF_parse_chars(file_t fd, size_t block_size, bm_font* font) {
    DBG_PRINT("BMF found char block!\n");

    int num_chars = block_size / sizeof(bm_char);
    bm_char_ex temp_char;

    font->num_chars = num_chars;

    DBG_PRINT("BMF %d chars present\n", num_chars);

    /* Read one at a time to font charset */
    for (int i = 0; i < num_chars; i++) {
        fs_read(fd, &temp_char, sizeof(bm_char));
        if (temp_char.id < 256) {
            /* Optionally print out info for each char parsed */
#if defined(DBG_CHAR_INFO) && DBG_CHAR_INFO
            char temp = (char)temp_char.id;
            DBG_PRINT("Char: %c\n", temp);
            PRINT_MEMBER(temp_char, id);
            PRINT_MEMBER(temp_char, x);
            PRINT_MEMBER(temp_char, y);
            PRINT_MEMBER(temp_char, width);
            PRINT_MEMBER(temp_char, height);
            PRINT_MEMBER(temp_char, xoffset);
            PRINT_MEMBER(temp_char, yoffset);
            PRINT_MEMBER(temp_char, xadvance);
            DBG_PRINT("\n");
#endif

            font->chars[temp_char.id] = temp_char;
            font->chars[temp_char.id].kerns = NULL;
        }
    }

    DBG_PRINT("\n");
    return 0;
}

static int
_kern_pair_sort(const void* a, const void* b) {
    const bm_kern_pair* ia = (const bm_kern_pair*)a;
    const bm_kern_pair* ib = (const bm_kern_pair*)b;
    if (ia->first == ib->first) {
        return ia->second - ib->second;
    }
    return ia->first - ib->first;
}

static int
BMF_parse_kerning(file_t fd, size_t block_size, bm_font* font) {
    DBG_PRINT("BMF found kerning block!\n");

    /* Parse and save */
    int num_pairs = block_size / sizeof(bm_kern_pair);

    DBG_PRINT("BMF %d kerning pairs present\n", num_pairs);

    font->kerns = malloc(sizeof(bm_kern_pair) * num_pairs);
    if (!font->kerns) {
        /* printf("%s no free memory\n", __func__); */
        return 0;
    }
    font->num_kerns = num_pairs;
    fs_read(fd, font->kerns, num_pairs * sizeof(bm_kern_pair));

    /* Sort Kerning pairs */
    qsort(font->kerns, num_pairs, sizeof(bm_kern_pair), _kern_pair_sort);

    /* List the now sorted pairs*/
    for (int i = 0; i < num_pairs; i++) {
        bm_kern_pair* pair = &font->kerns[i];

        if (pair->first < 256) {
            font->chars[pair->first].kerns = pair;
        }

#if defined(DBG_KERN_INFO) && DBG_KERN_INFO
        char first = (char)pair->first;
        char second = (char)pair->second;
        DBG_PRINT("First: %c\n", first);
        DBG_PRINT("Second: %c\n", second);
        DBG_PRINT("amount: %d\n", pair->amount);
        DBG_PRINT("\n");
#endif
    }

    DBG_PRINT("\n");
    return 0;
}

/* BMFont specifics */
static int
BMF_load(const char* file, bm_font* font) {
    // Zero out
    memset(font, '\0', sizeof(bm_font));

    file_t fd;
    bool parsing = true;
    fd = fs_open(file, O_RDONLY);

    if (fd == -1) {
        /* printf("BMF:Error file %s not found!\n", file); */
        return 1;
    }

    bm_header file_header;
    fs_read(fd, &file_header, sizeof(bm_header));
    if (file_header.version != 3) {
        fs_close(fd);
        /* printf("BMF:Error font magic wrong %3s!\n", file_header.bmf); */
        return 1;
    }

    static bm_block_tag next_block = {0, 0};
    size_t ele_read;
    while (parsing) {
        ele_read = fs_read(fd, &next_block, sizeof(bm_block_tag));
        DBG_PRINT("Found block type %d of size %ld\n", next_block.type, next_block.size);

        if (ele_read != sizeof(bm_block_tag)) {
            break;
        }

        switch (next_block.type) {
            case INFO: BMF_parse_info(fd, next_block.size, font); break;
            case COMMON: BMF_parse_common(fd, next_block.size, font); break;
            case PAGES: BMF_parse_pages(fd, next_block.size, font); break;
            case CHARS: BMF_parse_chars(fd, next_block.size, font); break;
            case KERNING:
                BMF_parse_kerning(fd, next_block.size, font);
                parsing = false; /* should be end of file */
                break;
            default: /* printf("BMF:Error unknown block type %d\n", next_block.type); */ break;
        }
    }

    fs_close(fd);

    font_loaded = 1;

    return 0;
}

int
BMF_adjust_kerning(unsigned char first, unsigned char second, bm_font* font) {
    const bm_kern_pair* kern_pairs = font->chars[first].kerns;
    if (kern_pairs) {
        do {
            if (kern_pairs->second == second) {
                return kern_pairs->amount;
            }
        } while ((unsigned char)(kern_pairs++)->first == first);
    }
    return 0;
}

/* Drawing */

void
font_bmf_set_height_default(void) {
    current_scale = 1.0f;
}

void
font_bmf_set_height(float height) {
    current_scale = height / font_basilea.fontSize;
}

void
font_bmf_set_scale(float scale) {
    current_scale = scale;
}

/* Scaling */
#define X_SCALE_4_3  (1.0f)
#define X_SCALE_16_9 (0.74941452f)
static float X_SCALE;

#ifdef KOS_SPRITE
static pvr_sprite_hdr_t font_header;
#define VERT_PER_CHAR (1)
#else
static pvr_poly_hdr_t font_header;
#define VERT_PER_CHAR (4)
#endif
static image font_texture;

/* Font prototype generics */
int
font_bmf_init(const char* fnt, const char* texture, int is_wide) {
    if (is_wide) {
        X_SCALE = (X_SCALE_16_9);
    } else {
        X_SCALE = (X_SCALE_4_3);
    }
    int ret = 0;
    char temp_fnt[128];
    snprintf(temp_fnt, 127, "/cd/%s", fnt);

    /* If we arent loaded then load eveyrthing, otherwise just load texture */
    if (!font_loaded) {
        ret += BMF_load(temp_fnt, &font_basilea);
    }

    unsigned int temp = texman_create();
    draw_load_texture_buffer(texture, &font_texture, texman_get_tex_data(temp));
    texman_reserve_memory(font_texture.width, font_texture.height, 2 /* 16Bit */);

    return ret;
}

void
font_bmf_destroy(void) { /*@Todo: release safely */ }

void
font_bmf_begin_draw(void) {
    /* Make a polygon header */
#ifdef KOS_SPRITE
    pvr_sprite_cxt_t tmp;
    pvr_sprite_cxt_txr(&tmp, draw_get_list(), font_texture.format, font_texture.width, font_texture.height,
                       font_texture.texture, PVR_FILTER_BILINEAR);
    pvr_sprite_compile(&font_header, &tmp);
#else
    pvr_poly_cxt_t tmp;
    pvr_poly_cxt_txr(&tmp, draw_get_list(), font_texture.format, font_texture.width, font_texture.height,
                     font_texture.texture, PVR_FILTER_BILINEAR);
    if (tmp.txr.enable != PVR_TEXTURE_DISABLE) {
        switch (tmp.txr.width) {
            case 8:
            case 16:
            case 32:
            case 64:
            case 128:
            case 256:
            case 512:
            case 1024: break;
            default:
                /* printf("%s error tex size %d(%ld) %d(%ld)\n", __func__, tmp.txr.width, font_texture.width,
                   tmp.txr.height, font_texture.height); */
                return;
                break;
        }
    }
    pvr_poly_compile(&font_header, &tmp);
#endif
    font_bmf_set_height_default();
    pvr_prim(&font_header, sizeof(font_header));
    current_color = PVR_PACK_ARGB(0xff, 0xff, 0xff, 0xff);
}

#define BUFFER_MAX_CHARS (128)
#ifdef KOS_SPRITE
static pvr_sprite_txr_t charbuf[BUFFER_MAX_CHARS * VERT_PER_CHAR] __attribute__((aligned(32)));
#else
static pvr_vertex_t charbuf[BUFFER_MAX_CHARS * VERT_PER_CHAR] __attribute__((aligned(32)));
#endif
static int charbuffered;

/* Draws a font letter using two triangle strips */
static int
font_bmf_draw_char(int x, int y, unsigned char chr) {
    bm_font* font = &font_basilea;

    /* Upper left */
    const float x1 = round(x + (current_scale * (float)font->chars[chr].xoffset) * X_SCALE);
    const float y1 = round(y + current_scale * (float)font->chars[chr].yoffset);
    const float u1 = (float)font->chars[chr].x / (float)font->width;
    const float v1 = (float)font->chars[chr].y / (float)font->height;

    /* Lower right */
    const float x2 = round(x + (current_scale * ((float)font->chars[chr].width + font->chars[chr].xoffset)) * X_SCALE);
    const float y2 = round(y + current_scale * ((float)font->chars[chr].height + font->chars[chr].yoffset));
    const float u2 = (float)(font->chars[chr].x + font->chars[chr].width) / (float)font->width;
    const float v2 = (float)(font->chars[chr].y + font->chars[chr].height) / (float)font->height;

    const float z = z_get();

    if (charbuffered > BUFFER_MAX_CHARS) {
        pvr_prim(charbuf, charbuffered * sizeof(charbuf[0]));
        charbuffered = 0;
    }

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
        /* interpolatied */
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
    vert1->argb = current_color;
    vert1->oargb = 0;

    vert2->flags = PVR_CMD_VERTEX;
    vert2->x = x1;
    vert2->y = y1;
    vert2->z = z;
    vert2->u = u1;
    vert2->v = v1;
    vert2->argb = current_color;
    vert2->oargb = 0;

    vert3->flags = PVR_CMD_VERTEX;
    vert3->x = x2;
    vert3->y = y2;
    vert3->z = z;
    vert3->u = u2;
    vert3->v = v2;
    vert3->argb = current_color;
    vert3->oargb = 0;

    vert4->flags = PVR_CMD_VERTEX_EOL;
    vert4->x = x2;
    vert4->y = y1;
    vert4->z = z;
    vert4->u = u2;
    vert4->v = v1;
    vert4->argb = current_color;
    vert4->oargb = 0;
#endif
    charbuffered += VERT_PER_CHAR;

    return (current_scale * font->chars[chr].xadvance) * X_SCALE;
}

static void
_font_bmf_draw_string(int x1, int y1, uint32_t color, const char* str) {
    current_color = color;
    charbuffered = 0;
    z_inc();

    unsigned char prev = 0;
    do {
        unsigned char chr = *str;
        if (chr != ' ') {
            /* Add possible kerning adjustment */
            x1 += round(current_scale * BMF_adjust_kerning(prev, chr, &font_basilea));
            x1 += font_bmf_draw_char(x1, y1, chr);
        } else {
            x1 += round(current_scale * (float)font_basilea.chars[' '].width);
        }

        prev = chr;
    } while (*++str);

    pvr_prim(charbuf, charbuffered * sizeof(charbuf[0]));
}

static float
_font_bmf_calculate_length_full(const char* str, int length) {
    /* Not sure if its worth calculating kerning for this */
    float width = 0;
    char prev = 0;
    bm_font* font = &font_basilea;
    int cursor = 0;

    while (*str && cursor++ < length) {
        int chr = *str;
        /* Add possible kerning adjustment */
        width += BMF_adjust_kerning(prev, chr, &font_basilea); /* slow */
        width += font->chars[chr].xadvance;

        prev = chr;
        str++;
    }

    return round(current_scale * round(width)) * X_SCALE;
}

static float
_font_bmf_calculate_length(const char* str) {
    return _font_bmf_calculate_length_full(str, strlen(str));
}

void
font_bmf_draw_auto_size(int x1, int y1, uint32_t color, const char* str, int width) {
    float save_scale = current_scale;
    float temp = _font_bmf_calculate_length(str);
    if (temp > width) {
        const float scale = ((float)width / temp);
        font_bmf_set_scale(scale);
    }
    _font_bmf_draw_string(x1, y1, color, str);
    current_scale = save_scale;
}

void
font_bmf_draw_centered(int x1, int y1, uint32_t color, const char* str) {
    int temp = (int)_font_bmf_calculate_length(str);
    _font_bmf_draw_string(x1 - (temp / 2), y1, color, str);
}

void
font_bmf_draw_centered_auto_size(int x1, int y1, uint32_t color, const char* str, int width) {
    float save_scale = current_scale;
    float temp = _font_bmf_calculate_length(str);
    if (temp > (float)width) {
        const float scale = ((float)width / temp);
        font_bmf_set_scale(scale);
    }
    _font_bmf_draw_string(x1 - ((int)temp / 2), y1, color, str);
    current_scale = save_scale;
}

void
font_bmf_draw(int x1, int y1, uint32_t color, const char* str) {
    _font_bmf_draw_string(x1, y1, color, str);
}

void
font_bmf_draw_main(int x1, int y1, uint32_t color, const char* str) {
    font_bmf_set_height_default();
    _font_bmf_draw_string(x1, y1, color, str);
}

void
font_bmf_draw_sub(int x1, int y1, uint32_t color, const char* str) {
    font_bmf_set_height(16.0f);
    _font_bmf_draw_string(x1, y1, color, str);
}

void
font_bmf_draw_sub_wrap(int x1, int y1, uint32_t color, const char* str, int width) {
    z_inc();
    current_color = color;
    unsigned char prev = ' ';
    unsigned int current_char = 0;

    const int x_start = x1;
    const char* text_end = strrchr(str, '\0');
    const char* current_text_start = str;
    const char* current_text_temp = str;
    const char* last_known_space = str;
    int current_text_len = 0;

    do {
        /* Try to find longest string broken by spaces that will fit in width */
        float current_text_width = 0.0f;
        prev = ' ';
        do {
            current_char = *(current_text_start + current_text_len);
            if (current_char == ' ') {
                last_known_space = current_text_start + current_text_len;
            }
            if (current_char == '\n') {
                /* not space but safe breaking point */
                last_known_space = current_text_start + current_text_len;
                break;
            }
            if (current_text_start + current_text_len >= text_end) {
                last_known_space = text_end;
                break;
            }
            current_text_width += (int)((current_scale * font_basilea.chars[current_char].xadvance) * X_SCALE);
            current_text_width += round(current_scale * BMF_adjust_kerning(prev, current_char, &font_basilea));
            prev = current_char;
            current_text_len++;
        } while (current_text_width < width);

        current_text_temp = current_text_start;
        charbuffered = 0;
        prev = 0;

        do {
            unsigned char chr = (*current_text_temp++);
            if (chr != ' ') {
                /* Add possible kerning adjustment */
                x1 += round(current_scale * BMF_adjust_kerning(prev, chr, &font_basilea));
                x1 += font_bmf_draw_char(x1, y1, chr);
            } else {
                x1 += round(current_scale * (float)font_basilea.chars[' '].width);
            }
            prev = chr;
        } while (current_text_temp < last_known_space);

        pvr_prim(charbuf, charbuffered * sizeof(charbuf[0]));

        /* prepare for next row */
        y1 += (current_scale * font_basilea.lineHeight * 1.2f /* Makes Text more natural */);
        x1 = x_start;
        current_text_start = last_known_space + 1;
        current_text_len = 0;
        prev = 0;
    } while (current_text_start < text_end);
}
