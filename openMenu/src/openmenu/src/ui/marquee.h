/*
 * File: marquee.h
 * Project: ui
 * Shared marquee scrolling for long game names in the list UIs.
 */

#pragma once

/* Back to the initial pause with the offset at zero */
void marquee_reset(void);

/* Resets automatically when the selected row changes */
void marquee_notice_selection(int selected);

/* Advance one frame. overflow_px is how far the text extends past the window */
void marquee_tick(int overflow_px);

/* Current scroll offset in pixels */
float marquee_offset_px(void);
