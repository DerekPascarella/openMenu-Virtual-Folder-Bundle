#include <stddef.h>
#include "ui/dc/mouse.h"
#include "ui/mouse_scrollbar.h"

static bool captured;
static bool blocked;
static bool dragging;
static mouse_scrollbar_t captured_bar;
static uint32_t captured_identity;
static int* captured_offset;
static int expected_offset;
static int grab_offset;
static int start_y;
static int start_offset;

void
mouse_scrollbar_cancel(void) {
    blocked = blocked || captured;
    captured = false;
}

void
mouse_scrollbar_rebase(uint32_t identity) {
    if (captured) {
        captured_identity = identity;
    }
}

bool
mouse_scrollbar_read(const mouse_scrollbar_t* bar, uint32_t identity, int* offset) {
    const mouse_frame_t* mouse = mouse_get_state();
    if (captured
        && (!mouse->present || mouse->changed || !bar || bar->maximum <= 0 || identity != captured_identity
            || offset != captured_offset || *offset != expected_offset || bar->x != captured_bar.x
            || bar->y != captured_bar.y || bar->width != captured_bar.width || bar->height != captured_bar.height
            || bar->thumb_height != captured_bar.thumb_height || bar->inset != captured_bar.inset
            || bar->maximum != captured_bar.maximum || bar->page != captured_bar.page)) {
        mouse_scrollbar_cancel();
    }
    if (blocked) {
        if (!mouse->buttons) {
            blocked = false;
        }
        return true;
    }
    if (captured) {
        if (!(mouse->buttons & MOUSE_LEFT)) {
            mouse_scrollbar_cancel();
            blocked = mouse->buttons != 0;
            return true;
        }
        if (dragging) {
            int travel = bar->height - 2 * bar->inset - bar->thumb_height;
            int thumb_y = mouse->y - grab_offset;
            int value = start_offset;
            if (mouse->y != start_y && travel > 0) {
                value += (int)((int64_t)(mouse->y - start_y) * bar->maximum / travel);
                if (thumb_y <= bar->y + bar->inset) {
                    value = 0;
                } else if (thumb_y >= bar->y + bar->inset + travel) {
                    value = bar->maximum;
                }
            }
            if (value < 0) {
                value = 0;
            } else if (value > bar->maximum) {
                value = bar->maximum;
            }
            *offset = value;
            expected_offset = value;
        }
        return true;
    }
    if (!mouse->present || mouse->changed || !bar || bar->maximum <= 0 || !(mouse->pressed & MOUSE_LEFT)
        || mouse->pressed & ~MOUSE_LEFT || mouse->x < 0 || mouse->x >= 640 || mouse->y < 0 || mouse->y >= 480
        || mouse->x < bar->x || mouse->x >= bar->x + bar->width || mouse->y < bar->y
        || mouse->y >= bar->y + bar->height) {
        return false;
    }
    captured = true;
    captured_bar = *bar;
    captured_identity = identity;
    captured_offset = offset;
    dragging = mouse->y >= bar->thumb_y && mouse->y < bar->thumb_y + bar->thumb_height;
    grab_offset = mouse->y - bar->thumb_y;
    start_y = mouse->y;
    start_offset = *offset;
    if (!dragging) {
        *offset += mouse->y < bar->thumb_y ? -bar->page : bar->page;
        if (*offset < 0) {
            *offset = 0;
        } else if (*offset > bar->maximum) {
            *offset = bar->maximum;
        }
    }
    expected_offset = *offset;
    return true;
}
