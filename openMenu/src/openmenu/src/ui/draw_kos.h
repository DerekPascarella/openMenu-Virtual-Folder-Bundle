/*
 * File: draw_kos.h
 * Project: ui
 * File Created: Wednesday, 19th May 2021 11:54:58 pm
 * Author: Hayden Kowalchuk
 * -----
 * Copyright (c) 2021 Hayden Kowalchuk, Hayden Kowalchuk
 * License: BSD 3-clause "New" or "Revised" License, http://www.opensource.org/licenses/BSD-3-Clause
 */

#pragma once

#include <stdint.h>

#include <dc/fmath.h>
#include <dc/pvr.h>

#include "dc/pvr_texture.h"
#include "texture/simple_texture_allocator.h"
#include "theme_manager.h"

#ifndef PVR_PACK_ARGB
#define PVR_PACK_ARGB(a, r, g, b)                                                                                      \
    ((((uint8_t)((a))) << 24) | (((uint8_t)((r))) << 16) | (((uint8_t)((g))) << 8) | (((uint8_t)((b))) << 0))
#endif

#define COLOR_WHITE    (0xFFFFFFFF) /*(PVR_PACK_ARGB(0xFF,0xFF,0xFF,0xFF))*/
#define COLOR_BLACK    (0xFF000000) /*(PVR_PACK_ARGB(0xFF,0x00,0x00,0x00))*/
#define COLOR_ORANGE_J (0xFFFAAA8F) /*(PVR_PACK_ARGB(0xFF,250,170,143))*/
#define COLOR_ORANGE_U (0xFFF6A61B) /*(PVR_PACK_ARGB(0xFF,246,166,27))*/
#define COLOR_BLUE     (0xFF9FBCE0) /*(PVR_PACK_ARGB(0xFF,159,188,224))*/
