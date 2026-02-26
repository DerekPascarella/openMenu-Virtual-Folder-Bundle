#pragma once
#include <dc/maple.h>
#include <dc/maple/vmu.h>

/* rotate 48x32 1bpp VMU LCD bitmap 180 degrees
   (reverse byte order of 192-byte buffer + reverse bits per byte) */
static inline void
rotate_lcd_180(const unsigned char* src, unsigned char* dst) {
    for (int i = 0; i < 192; i++) {
        unsigned char b = src[191 - i];
        b = (b & 0xF0) >> 4 | (b & 0x0F) << 4;
        b = (b & 0xCC) >> 2 | (b & 0x33) << 2;
        b = (b & 0xAA) >> 1 | (b & 0x55) << 1;
        dst[i] = b;
    }
}

/* vmu_draw_lcd() wrapper; rotates 180 when a light gun is at port unit 0
   (VMUs sit upside down inside light guns) */
static inline void
vmu_draw_lcd_auto(maple_device_t* dev, const void* bitmap) {
    maple_device_t* ctrl = maple_enum_dev(dev->port, 0);
    if (ctrl && (ctrl->info.functions & MAPLE_FUNC_LIGHTGUN)) {
        unsigned char rotated[192];
        rotate_lcd_180((const unsigned char*)bitmap, rotated);
        vmu_draw_lcd(dev, rotated);
    } else {
        vmu_draw_lcd(dev, bitmap);
    }
}
