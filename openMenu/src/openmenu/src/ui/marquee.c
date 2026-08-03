/*
 * File: marquee.c
 * Project: ui
 * Shared marquee scrolling for long game names in the list UIs.
 */

#include <stdint.h>

#include "ui/marquee.h"

/* Cell width of the fixed 8x16 bitmap font both list UIs draw with */
#define MARQUEE_CELL_PX              8
#define MARQUEE_INITIAL_PAUSE_FRAMES 60
#define MARQUEE_END_PAUSE_FRAMES     90

typedef enum {
    MARQUEE_STATE_INITIAL_PAUSE,
    MARQUEE_STATE_SCROLL_LEFT,
    MARQUEE_STATE_END_PAUSE,
    MARQUEE_STATE_SCROLL_RIGHT
} marquee_state_t;

static marquee_state_t marquee_state = MARQUEE_STATE_INITIAL_PAUSE;
static int marquee_frame = 0;
static int marquee_period = 0;
static float marquee_offset = 0.0f;
static int marquee_pause_timer = MARQUEE_INITIAL_PAUSE_FRAMES;
static int marquee_last_selected = -1;

/* The old marquee jumped a whole cell every period frames. Spreading that
 * cell across the period keeps the average speed identical. Progress is
 * counted in whole frames with the pixel offset derived from it because
 * accumulating a float step every frame would drift off the endpoints. */
static int
marquee_speed_period(void) {
    extern uint8_t* sf_marquee_speed;
    switch (sf_marquee_speed[0]) {
        case 0: return 8 + 1;  /* Slow */
        case 1: return 6 + 1;  /* Medium */
        case 2: return 4 + 1;  /* Fast */
        default: return 6 + 1; /* Default to Medium */
    }
}

void
marquee_reset(void) {
    marquee_state = MARQUEE_STATE_INITIAL_PAUSE;
    marquee_frame = 0;
    marquee_period = 0;
    marquee_offset = 0.0f;
    marquee_pause_timer = MARQUEE_INITIAL_PAUSE_FRAMES;
    marquee_last_selected = -1;
}

void
marquee_notice_selection(int selected) {
    if (selected != marquee_last_selected) {
        marquee_reset();
        marquee_last_selected = selected;
    }
}

void
marquee_tick(int overflow_px) {
    if (overflow_px < 0) {
        overflow_px = 0;
    }

    int period = marquee_speed_period();

    /* Keep the position steady if the speed setting changed mid scroll */
    if (period != marquee_period) {
        if (marquee_period > 0) {
            marquee_frame = (int)(marquee_offset * (float)period / (float)MARQUEE_CELL_PX + 0.5f);
        }
        marquee_period = period;
    }

    int total = overflow_px * period / MARQUEE_CELL_PX;
    if (marquee_frame > total) {
        marquee_frame = total;
    }

    if (marquee_pause_timer > 0) {
        marquee_pause_timer--;
        return;
    }

    switch (marquee_state) {
        case MARQUEE_STATE_INITIAL_PAUSE: marquee_state = MARQUEE_STATE_SCROLL_LEFT; break;

        case MARQUEE_STATE_SCROLL_LEFT:
            marquee_frame++;
            if (marquee_frame >= total) {
                marquee_frame = total;
                marquee_state = MARQUEE_STATE_END_PAUSE;
                marquee_pause_timer = MARQUEE_END_PAUSE_FRAMES;
            }
            break;

        case MARQUEE_STATE_END_PAUSE: marquee_state = MARQUEE_STATE_SCROLL_RIGHT; break;

        case MARQUEE_STATE_SCROLL_RIGHT:
            marquee_frame--;
            if (marquee_frame <= 0) {
                marquee_frame = 0;
                marquee_state = MARQUEE_STATE_INITIAL_PAUSE;
                marquee_pause_timer = MARQUEE_INITIAL_PAUSE_FRAMES;
            }
            break;
    }

    marquee_offset = (float)(marquee_frame * MARQUEE_CELL_PX) / (float)period;
    if (marquee_offset > (float)overflow_px) {
        marquee_offset = (float)overflow_px;
    }
}

float
marquee_offset_px(void) {
    return marquee_offset;
}
