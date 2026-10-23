/*
 * File: boot_trace.c
 * Project: openmenu_settings
 * Description: On-screen boot step log for diagnosing startup hangs
 */

#include "boot_trace.h"

#if DEBUG_BOOT_TRACE
#include <stdarg.h>
#include <stdbool.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>

#include <arch/timer.h>
#include <dc/biosfont.h>
#include <dc/maple.h>
#include <dc/maple/controller.h>
#include <dc/pvr.h>
#include <dc/video.h>
#include <kos/mutex.h>
#include <kos/thread.h>

#define TRACE_COLS    45
#define TRACE_VISIBLE 14
#define TRACE_ROWS    (TRACE_VISIBLE + 4)
#define TRACE_MAX     64
#define GLYPH_FIRST   32
#define GLYPH_COUNT   95

#define COLOR_TITLE   0x07FF
#define COLOR_TEXT    0xFFFF
#define COLOR_STATUS  0xFFE0

/* ASCII 32 to 126 as 12-bit rows, copied once so drawing never needs the BIOS font lock. */
static uint16_t glyphs[GLYPH_COUNT][BFONT_HEIGHT];

static char lines[TRACE_MAX][TRACE_COLS + 1];
static int line_count;
static char step_name[TRACE_COLS + 1];
static bool step_running;
static bool active;
static uint64_t boot_start_ms;
static uint64_t step_start_ms;
static unsigned spin;

static volatile bool heartbeat_stop;
static kthread_t* heartbeat;
static mutex_t trace_lock = MUTEX_INITIALIZER;
static uint32_t cleared_base = ~0u;

static uint16_t*
framebuffer(void) {
    if (vid_mode->pm != PM_RGB565) {
        return NULL;
    }

    uint32_t base = PVR_GET(PVR_FB_ADDR) & (PVR_RAM_SIZE - 1);
    uint16_t* fb = (uint16_t*)(PVR_RAM_BASE | base);

    /* A buffer flip shows whatever was last rendered there, so start it black. */
    if (base != cleared_base) {
        uint32_t* p = (uint32_t*)fb;
        for (size_t i = 0; i < (size_t)vid_mode->width * vid_mode->height / 2; i++) {
            p[i] = 0;
        }
        cleared_base = base;
    }
    return fb;
}

/* Pads to the full width so a shorter line overwrites a longer one. */
static void
draw_row(uint16_t* fb, int row, const char* text, uint16_t color) {
    const int width = vid_mode->width;
    const int x0 = (width - TRACE_COLS * BFONT_THIN_WIDTH) / 2;
    const int y0 = (vid_mode->height - TRACE_ROWS * BFONT_HEIGHT) / 2 + row * BFONT_HEIGHT;
    bool ended = false;

    for (int col = 0; col < TRACE_COLS; col++) {
        unsigned char c = ended ? ' ' : (unsigned char)text[col];
        if (!c) {
            ended = true;
            c = ' ';
        }
        if (c < GLYPH_FIRST || c >= GLYPH_FIRST + GLYPH_COUNT) {
            c = '?';
        }

        const uint16_t* glyph = glyphs[c - GLYPH_FIRST];
        uint16_t* dst = fb + y0 * width + x0 + col * BFONT_THIN_WIDTH;
        for (int y = 0; y < BFONT_HEIGHT; y++, dst += width) {
            for (int x = 0; x < BFONT_THIN_WIDTH; x++) {
                dst[x] = (glyph[y] & (0x800 >> x)) ? color : 0;
            }
        }
    }
}

static void
render(int first, const char* status) {
    uint16_t* fb = framebuffer();
    if (!fb) {
        return;
    }

    draw_row(fb, 0, "openMenu boot trace", COLOR_TITLE);
    for (int i = 0; i < TRACE_VISIBLE; i++) {
        int n = first + i;
        draw_row(fb, 2 + i, n < line_count ? lines[n] : "", COLOR_TEXT);
    }
    draw_row(fb, TRACE_ROWS - 1, status, COLOR_STATUS);
}

/* Caller holds trace_lock. */
static void
render_live(void) {
    static const char spinner[] = "|/-\\";
    char status[TRACE_COLS + 1];
    uint64_t now = timer_ms_gettime64();
    unsigned up = (unsigned)((now - boot_start_ms) / 1000 % 10000);
    char mark = spinner[spin++ % 4];

    if (step_running) {
        uint64_t ms = now - step_start_ms;
        snprintf(status, sizeof(status), "%c %-22.22s %4u.%u s  up %4u s", mark, step_name,
                 (unsigned)(ms / 1000 % 10000), (unsigned)(ms % 1000 / 100), up);
    } else {
        snprintf(status, sizeof(status), "%c %-31s  up %4u s", mark, "idle", up);
    }

    int first = line_count > TRACE_VISIBLE ? line_count - TRACE_VISIBLE : 0;
    render(first, status);
}

static void
format_ms(char* out, size_t size, uint64_t ms) {
    if (ms < 100000) {
        snprintf(out, size, "%u ms", (unsigned)ms % 100000);
    } else {
        snprintf(out, size, "%u s", (unsigned)(ms / 1000 % 100000));
    }
}

static char*
next_line(void) {
    if (line_count == TRACE_MAX) {
        memmove(lines[0], lines[1], sizeof(lines[0]) * (TRACE_MAX - 1));
        line_count--;
    }
    return lines[line_count++];
}

static void*
heartbeat_thread(void* param) {
    (void)param;
    while (!heartbeat_stop) {
        mutex_lock(&trace_lock);
        render_live();
        mutex_unlock(&trace_lock);
        thd_sleep(250);
    }
    return NULL;
}

void
boot_trace_begin(void) {
    uint16_t cell[BFONT_THIN_WIDTH * BFONT_HEIGHT];

    /* Index 0 stays blank because the BIOS font maps a space to a wide glyph. */
    for (int c = 1; c < GLYPH_COUNT; c++) {
        memset(cell, 0, sizeof(cell));
        bfont_draw(cell, BFONT_THIN_WIDTH, 1, (uint32_t)(c + GLYPH_FIRST));
        for (int y = 0; y < BFONT_HEIGHT; y++) {
            uint16_t bits = 0;
            for (int x = 0; x < BFONT_THIN_WIDTH; x++) {
                if (cell[y * BFONT_THIN_WIDTH + x]) {
                    bits |= 0x800 >> x;
                }
            }
            glyphs[c][y] = bits;
        }
    }

    boot_start_ms = timer_ms_gettime64();
    active = true;
    snprintf(next_line(), TRACE_COLS + 1, "Program started %lu ms after KOS", (unsigned long)boot_start_ms);

    heartbeat_stop = false;
    heartbeat = thd_create(false, heartbeat_thread, NULL);
}

void
boot_trace_step(const char* name) {
    if (!active) {
        return;
    }

    mutex_lock(&trace_lock);
    snprintf(step_name, sizeof(step_name), "%s", name);
    snprintf(next_line(), TRACE_COLS + 1, "%-22.22s %-13.13s", name, "...");
    step_running = true;
    step_start_ms = timer_ms_gettime64();
    render_live();
    step_start_ms = timer_ms_gettime64();
    mutex_unlock(&trace_lock);
}

void
boot_trace_done(const char* fmt, ...) {
    if (!active || !step_running) {
        return;
    }

    char result[TRACE_COLS + 1];
    char elapsed[9];
    va_list args;

    va_start(args, fmt);
    vsnprintf(result, sizeof(result), fmt, args);
    va_end(args);

    mutex_lock(&trace_lock);
    format_ms(elapsed, sizeof(elapsed), timer_ms_gettime64() - step_start_ms);
    snprintf(lines[line_count - 1], TRACE_COLS + 1, "%-22.22s %-13.13s %8s", step_name, result, elapsed);
    step_running = false;
    render_live();
    mutex_unlock(&trace_lock);
}

void
boot_trace_end(void) {
    if (!active) {
        return;
    }

    heartbeat_stop = true;
    thd_join(heartbeat, NULL);

    char total[9];
    format_ms(total, sizeof(total), timer_ms_gettime64() - boot_start_ms);

    const int pages = (line_count + TRACE_VISIBLE - 1) / TRACE_VISIBLE;
    int page = 0;
    int shown = -1;

    /* Starts with every button counted as held so a held START needs a fresh press. */
    uint32_t prev = ~0u;

    for (;;) {
        if (page != shown) {
            char status[TRACE_COLS + 1];
            snprintf(status, sizeof(status), "Total %s  Page %d/%d  START: menu", total, (page + 1) % 10, pages % 10);
            render(page * TRACE_VISIBLE, status);
            shown = page;
        }

        uint32_t buttons = 0;
        maple_device_t* cont = maple_enum_type(0, MAPLE_FUNC_CONTROLLER);
        if (cont) {
            cont_state_t* state = (cont_state_t*)maple_dev_status(cont);
            if (state) {
                buttons = state->buttons;
            }
        }
        uint32_t pressed = buttons & ~prev;
        prev = buttons;

        if (pressed & CONT_START) {
            break;
        }
        if ((pressed & CONT_DPAD_RIGHT) && page < pages - 1) {
            page++;
        }
        if ((pressed & CONT_DPAD_LEFT) && page > 0) {
            page--;
        }
        thd_sleep(30);
    }

    active = false;
}
#endif
