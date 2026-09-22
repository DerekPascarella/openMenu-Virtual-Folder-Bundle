#include "mouse.h"

#include <arch/irq.h>
#include <dc/maple.h>
#include <dc/maple/mouse.h>
#include <openmenu_settings.h>

#include "ui/draw_prototypes.h"

#ifndef MOUSE_MIDDLEBUTTON
#error "Apply docker/kos_patch/mouse_middle_button.patch and rebuild KOS before building OpenMenu."
#endif

static mouse_frame_t frame = {.x = 320, .y = 240};
static maple_device_t* active_mouse;
static uint8_t last_buttons;
static bool sample_ready;
static bool current_enabled;
static bool reset_pending = true;
static bool callbacks_registered;
static int cursor_speed = MOUSE_SPEED_MEDIUM;
static int cursor_remainder_x;
static int cursor_remainder_y;
static pvr_ptr_t cursor_texture;

static const char cursor_pixels[19][13] = {
    "O...........", "OO..........", "OFO.........", "OFFO........", "OFFFO.......", "OFFFFO......", "OFFFFFO.....",
    "OFFFFFFO....", "OFFFFFFFO...", "OFFFFFFFFO..", "OFFFFFFFFFO.", "OFFFFFFFFFFO", "OFFFFFFOOOOO", "OFFFOFFO....",
    "OFFO.OFFO...", "OFO..OFFO...", "OO....OFFO..", "......OFFO..", ".......OO...",
};

static void
mouse_detached(maple_device_t* dev) {
    if (dev == active_mouse) {
        active_mouse = NULL;
        sample_ready = false;
        reset_pending = true;
        frame.buttons = 0;
    }
}

const mouse_frame_t*
mouse_get_state(void) {
    return &frame;
}

static int
mouse_scale_delta(int delta, int speed, int* remainder) {
    if (speed == MOUSE_SPEED_SLOW) {
        int total = delta + *remainder;
        *remainder = total % 2;
        return total / 2;
    }
    *remainder = 0;
    return speed == MOUSE_SPEED_FAST ? delta * 2 : delta;
}

int
mouse_scroll_steps(int wheel, mouse_scroll_state_t* state) {
    if (state->speed != sf_mouse_scroll_speed[0]) {
        state->remainder = 0;
        state->speed = sf_mouse_scroll_speed[0];
    }
    return mouse_scale_delta(wheel, state->speed, &state->remainder);
}

void
mouse_reset(void) {
    int irq = irq_disable();
    reset_pending = true;
    sample_ready = false;
    frame.changed = true;
    frame.moved = false;
    frame.pressed = 0;
    frame.buttons = 0;
    frame.wheel = 0;
    cursor_remainder_x = 0;
    cursor_remainder_y = 0;
    irq_restore(irq);
}

void
mouse_poll(bool enabled) {
    int irq = irq_disable();
    if (!callbacks_registered) {
        /* KOS has one application detach callback slot. */
        maple_detach_callback(MAPLE_FUNC_MOUSE, mouse_detached);
        callbacks_registered = true;
    }

    maple_device_t* dev = maple_enum_type(0, MAPLE_FUNC_MOUSE);
    frame.changed = reset_pending || dev != active_mouse || enabled != current_enabled;
    frame.present = enabled && dev != NULL;
    frame.moved = false;
    frame.pressed = 0;
    frame.buttons = 0;
    frame.wheel = 0;
    if (frame.changed) {
        sample_ready = false;
    }
    if (frame.changed || cursor_speed != sf_mouse_cursor_speed[0]) {
        cursor_speed = sf_mouse_cursor_speed[0];
        cursor_remainder_x = 0;
        cursor_remainder_y = 0;
    }
    reset_pending = false;
    current_enabled = enabled;
    active_mouse = dev;

    if (!dev || !dev->status_valid) {
        frame.changed = true;
        sample_ready = false;
        cursor_remainder_x = 0;
        cursor_remainder_y = 0;
        irq_restore(irq);
        return;
    }

    /* KOS overwrites these deltas in IRQ context and status reads do not consume them. */
    mouse_state_t* state = (mouse_state_t*)dev->status;
    mouse_state_t sample = *state;
    state->dx = 0;
    state->dy = 0;
    state->dz = 0;

    uint8_t buttons = sample.buttons & (MOUSE_LEFT | MOUSE_RIGHT);
    if (sample.buttons & (MOUSE_MIDDLEBUTTON | MOUSE_SIDEBUTTON)) {
        buttons |= MOUSE_THIRD;
    }
    frame.buttons = enabled ? buttons : 0;
    if (enabled && sample_ready) {
        int old_x = frame.x;
        int old_y = frame.y;
        frame.x += mouse_scale_delta(sample.dx, cursor_speed, &cursor_remainder_x);
        frame.y += mouse_scale_delta(sample.dy, cursor_speed, &cursor_remainder_y);
        if (frame.x < 0) {
            frame.x = 0;
        }
        if (frame.x > 639) {
            frame.x = 639;
        }
        if (frame.y < 0) {
            frame.y = 0;
        }
        if (frame.y > 479) {
            frame.y = 479;
        }
        if ((frame.x == 0 && sample.dx < 0) || (frame.x == 639 && sample.dx > 0)) {
            cursor_remainder_x = 0;
        }
        if ((frame.y == 0 && sample.dy < 0) || (frame.y == 479 && sample.dy > 0)) {
            cursor_remainder_y = 0;
        }
        frame.moved = frame.x != old_x || frame.y != old_y;
        frame.wheel = sample.dz;
        frame.pressed = buttons & ~last_buttons;
    }
    last_buttons = buttons;
    sample_ready = true;
    irq_restore(irq);
}

void
mouse_draw_cursor(uint32_t fill, uint32_t outline) {
    if (!frame.present) {
        return;
    }
    if (!cursor_texture) {
        uint16_t pixels[32 * 32] = {0};
        cursor_texture = pvr_mem_malloc(sizeof(pixels));
        if (!cursor_texture) {
            return;
        }
        for (int y = 0; y < 19; y++) {
            for (int x = 0; x < 12; x++) {
                if (cursor_pixels[y][x] == 'F') {
                    pixels[y * 32 + x] = 0xFFFF;
                } else if (cursor_pixels[y][x] == 'O') {
                    pixels[y * 32 + 16 + x] = 0xFFFF;
                }
            }
        }
        pvr_txr_load_ex(pixels, cursor_texture, 32, 32, PVR_TXRLOAD_16BPP);
    }
    fill |= 0xFF000000;
    outline |= 0xFF000000;

    int width = 640 - frame.x;
    int height = 480 - frame.y;
    if (width > 12) {
        width = 12;
    }
    if (height > 19) {
        height = 19;
    }

    pvr_poly_cxt_t context;
    pvr_poly_hdr_t header;
    pvr_poly_cxt_txr(&context, draw_get_list(), PVR_TXRFMT_ARGB4444, 32, 32, cursor_texture, PVR_FILTER_NONE);
    /* Tall menus can reach the renderer's 512 depth limit. */
    context.depth.comparison = PVR_DEPTHCMP_GEQUAL;
    pvr_poly_compile(&header, &context);
    pvr_prim(&header, sizeof(header));

    for (int mask = 0; mask < 2; mask++) {
        float u = mask * 0.5f;
        pvr_vertex_t vert = {.argb = mask == 0 ? fill : outline,
                             .oargb = 0,
                             .flags = PVR_CMD_VERTEX,
                             .z = 512.0f,
                             .u = u,
                             .v = height / 32.0f};
        vert.x = frame.x;
        vert.y = frame.y + height;
        pvr_prim(&vert, sizeof(vert));

        vert.y = frame.y;
        vert.v = 0;
        pvr_prim(&vert, sizeof(vert));

        vert.x = frame.x + width;
        vert.y = frame.y + height;
        vert.u = u + width / 32.0f;
        vert.v = height / 32.0f;
        pvr_prim(&vert, sizeof(vert));

        vert.flags = PVR_CMD_VERTEX_EOL;
        vert.y = frame.y;
        vert.v = 0;
        pvr_prim(&vert, sizeof(vert));
    }
}
