/*
 * File: ui_dcnow.h
 * Project: ui
 * The Dreamcast Now! window, opened from the settings footer.
 */

#pragma once

#include <stdbool.h>
#include <stdint.h>

#include <openmenu_settings.h>
#include "ui/common.h"

struct theme_color;

void dcnow_setup(enum draw_state* state, struct theme_color* _colors, int* timeout_ptr, uint32_t title_color);

/* Boot-time Auto-Connect, called once from the UI draw pass after the list has
 * been drawn. Starts the connection in the background, opens nothing. */
void dcnow_boot_autostart(void);

void handle_input_dcnow(enum control input);
void draw_dcnow_op(void);
void draw_dcnow_tr(void);

uint32_t dcnow_mouse_signature(void);
bool handle_mouse_dcnow(enum control* input);
