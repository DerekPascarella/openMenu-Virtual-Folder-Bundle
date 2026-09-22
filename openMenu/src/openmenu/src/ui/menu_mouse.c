#include <string.h>
#include "ui/dc/mouse.h"
#include "ui/menu_mouse.h"

#define MENU_MOUSE_TARGETS 128

typedef struct {
    int x, y, width, height;
    int* cursor;
    int row;
    enum control action;
} menu_mouse_target_t;

static menu_mouse_target_t targets[MENU_MOUSE_TARGETS];
static int target_count;
static menu_mouse_target_t surface;
static enum draw_state owner;
static uint32_t signature;
static bool collecting;
static bool enabled;
static bool rendered_busy;
static bool frame_busy;
static bool ready;
static int* scroll_cursor;
static int* scroll_offset;
static int scroll_count;
static int scroll_window;
static int scroll_rows[MENU_MOUSE_TARGETS];
static bool scroll_mapped;
static mouse_scrollbar_t scrollbar;
static bool scrollbar_visible;
static int pan;
static int pan_height;
static bool pan_pending;
static bool pan_repaint;
static mouse_scroll_state_t wheel_scroll;
static uint32_t wheel_signature;

void
menu_mouse_invalidate(void) {
    mouse_scrollbar_cancel();
    ready = false;
    enabled = false;
    rendered_busy = false;
    collecting = false;
    target_count = 0;
    pan = 0;
    pan_height = 0;
    pan_pending = false;
    pan_repaint = false;
    wheel_scroll.remainder = 0;
}

void
menu_mouse_begin(enum draw_state next_owner) {
    if (owner != next_owner) {
        menu_mouse_invalidate();
    }
    frame_busy = rendered_busy || menu_mouse_busy(next_owner);
    owner = next_owner;
    enabled = owner != DRAW_UI;
    collecting = owner != DRAW_UI;
    ready = false;
    target_count = 0;
    surface.width = 0;
    scroll_cursor = NULL;
    scroll_offset = NULL;
    scrollbar_visible = false;
}

void
menu_mouse_end(void) {
    if (collecting) {
        if (!scrollbar_visible) {
            mouse_scrollbar_cancel();
        }
        pan_repaint = false;
        if (pan_pending) {
            int first = -1;
            int last = -1;
            bool visible = false;
            for (int i = 0; i < target_count; i++) {
                menu_mouse_target_t* target = &targets[i];
                if (!target->cursor || target->y < 0 || target->y + target->height > 480) {
                    continue;
                }
                if (first < 0) {
                    first = i;
                }
                last = i;
                if (*target->cursor == target->row) {
                    visible = true;
                }
            }
            if (!visible && first >= 0) {
                int* cursor = targets[first].cursor;
                *cursor = *cursor < targets[first].row ? targets[first].row : targets[last].row;
                pan_repaint = true;
            }
            pan_pending = false;
        }
        ready = surface.width > 0 && signature == menu_mouse_signature(owner);
        rendered_busy = menu_mouse_busy(owner);
        if (frame_busy && !rendered_busy) {
            mouse_reset();
        }
    }
    collecting = false;
}

bool
menu_mouse_active(void) {
    return enabled;
}

enum draw_state
menu_mouse_owner(void) {
    return owner;
}

bool
menu_mouse_collecting(void) {
    return collecting;
}

void
menu_mouse_surface(int x, int y, int width, int height) {
    if (!collecting) {
        return;
    }
    target_count = 0;
    scroll_cursor = NULL;
    scroll_offset = NULL;
    scrollbar_visible = false;
    signature = menu_mouse_signature(owner);
    if (signature != wheel_signature) {
        wheel_scroll.remainder = 0;
    }
    wheel_signature = signature;
    surface.x = x - 2;
    surface.y = y - 2;
    surface.width = width + 4;
    surface.height = height + 4;
}

void
menu_mouse_row(int x, int y, int width, int height, int* cursor, int row, enum control action) {
    if (!collecting || x >= 640 || y >= 480 || x + width <= 0 || y + height <= 0
        || target_count >= MENU_MOUSE_TARGETS) {
        return;
    }
    menu_mouse_target_t* target = &targets[target_count++];
    target->x = x;
    target->y = y;
    target->width = width;
    target->height = height;
    target->cursor = cursor;
    target->row = row;
    target->action = action;
}

void
menu_mouse_scroll(int* cursor, int* offset, int count, int window, const int* rows) {
    if (!collecting || count <= 0 || window <= 0 || (rows && count > MENU_MOUSE_TARGETS)) {
        return;
    }
    scroll_cursor = cursor;
    scroll_offset = offset;
    scroll_count = count;
    scroll_window = window;
    scroll_mapped = rows != NULL;
    if (rows) {
        memcpy(scroll_rows, rows, count * sizeof(int));
    }
}

void
menu_mouse_scrollbar(const mouse_scrollbar_t* bar) {
    if (collecting && scroll_offset && bar->maximum > 0) {
        scrollbar = *bar;
        scrollbar_visible = true;
    }
}

int
menu_mouse_window_y(int y, int height) {
    if (!collecting || !mouse_get_state()->present) {
        return y;
    }
    if (height != pan_height) {
        pan = 0;
        pan_height = height;
        wheel_scroll.remainder = 0;
    }
    return height > 444 ? 18 - pan : y;
}

uint32_t
menu_mouse_hash(uint32_t hash, const void* data, size_t size) {
    const unsigned char* bytes = data;
    for (size_t i = 0; i < size; i++) {
        hash = (hash ^ bytes[i]) * 16777619u;
    }
    return hash;
}

static bool
menu_mouse_contains(const menu_mouse_target_t* target, int x, int y) {
    return x >= 0 && x < 640 && y >= 0 && y < 480 && x >= target->x && y >= target->y && x < target->x + target->width
           && y < target->y + target->height;
}

static void
menu_mouse_wheel(int wheel) {
    if (pan_height > 444) {
        pan_pending = true;
        pan += wheel * 24;
        if (pan < 0) {
            pan = 0;
        }
        if (pan > pan_height - 444) {
            pan = pan_height - 444;
        }
    } else if (scroll_cursor && scroll_offset) {
        int offset = *scroll_offset + wheel;
        int maximum = scroll_count > scroll_window ? scroll_count - scroll_window : 0;
        if (offset < 0) {
            offset = 0;
        }
        if (offset > maximum) {
            offset = maximum;
        }
        int cursor = scroll_mapped ? -1 : *scroll_cursor;
        if (scroll_mapped) {
            for (int i = 0; i < scroll_count; i++) {
                if (scroll_rows[i] == *scroll_cursor) {
                    cursor = i;
                }
            }
        }
        if (cursor < offset) {
            cursor = offset;
        }
        if (cursor >= offset + scroll_window) {
            cursor = offset + scroll_window - 1;
        }
        *scroll_offset = offset;
        *scroll_cursor = scroll_mapped ? scroll_rows[cursor] : cursor;
    }
    ready = false;
}

bool
menu_mouse_read(enum control* input, bool busy, bool cancel, int** selected) {
    const mouse_frame_t* mouse = mouse_get_state();
    *selected = NULL;
    bool valid = ready && !busy && signature == menu_mouse_signature(owner);
    if (!mouse->present || mouse->changed || !valid) {
        wheel_scroll.remainder = 0;
    }
    int old_offset = scroll_offset ? *scroll_offset : 0;
    if (mouse_scrollbar_read(valid && scrollbar_visible ? &scrollbar : NULL, signature, scroll_offset)) {
        wheel_scroll.remainder = 0;
        *input = NONE;
        if (scroll_offset && *scroll_offset != old_offset) {
            menu_mouse_wheel(0);
            mouse_scrollbar_rebase(menu_mouse_signature(owner));
        }
        return true;
    }
    if (pan_repaint && *input == A) {
        *input = NONE;
        return true;
    }
    bool action = mouse->pressed != 0 || mouse->wheel != 0;
    if (!mouse->present) {
        return false;
    }
    if (action) {
        *input = NONE;
    }
    if (mouse->changed || !ready || busy || signature != menu_mouse_signature(owner)) {
        return action;
    }
    menu_mouse_target_t* target = NULL;
    for (int i = target_count - 1; i >= 0; i--) {
        if (menu_mouse_contains(&targets[i], mouse->x, mouse->y)) {
            target = &targets[i];
            break;
        }
    }
    if (mouse->pressed & MOUSE_RIGHT) {
        if (owner == DRAW_MENU) {
            if (target && target->action == RIGHT) {
                if (target->cursor) {
                    *target->cursor = target->row;
                    *selected = target->cursor;
                }
                *input = LEFT;
            } else if (!menu_mouse_contains(&surface, mouse->x, mouse->y)) {
                *input = B;
            }
        } else if (cancel) {
            *input = B;
        }
        ready = false;
        return true;
    }
    if (mouse->pressed & MOUSE_LEFT) {
        if (target) {
            if (target->cursor) {
                *target->cursor = target->row;
                *selected = target->cursor;
            }
            *input = target->action;
        } else if (owner == DRAW_MENU && !menu_mouse_contains(&surface, mouse->x, mouse->y)) {
            *input = B;
        }
        ready = false;
        return true;
    }
    if (mouse->pressed) {
        if (owner == DRAW_MENU && (mouse->pressed & MOUSE_THIRD)) {
            *input = START;
            ready = false;
        }
        return true;
    }
    if (mouse->wheel) {
        int steps = mouse_scroll_steps(mouse->wheel, &wheel_scroll);
        if (steps) {
            menu_mouse_wheel(steps);
            wheel_signature = menu_mouse_signature(owner);
        }
        int offset = pan_height > 444 ? pan : (scroll_offset ? *scroll_offset : 0);
        int maximum = pan_height > 444 ? pan_height - 444 : scroll_count - scroll_window;
        if ((!scroll_offset && pan_height <= 444) || maximum <= 0 || (offset == 0 && mouse->wheel < 0)
            || (offset >= maximum && mouse->wheel > 0)) {
            wheel_scroll.remainder = 0;
        }
        return true;
    }
    if (mouse->moved && target && target->cursor) {
        *target->cursor = target->row;
        *selected = target->cursor;
        *input = NONE;
        return true;
    }
    return false;
}
