/*
 * File: dcnow_vmu.c
 * Project: openmenu
 * Dreamcast Now! on the VMU LCD: the indicator icons and the player screen.
 */

#include <stdbool.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include <arch/timer.h>
#include <crayon_savefile/peripheral.h>
#include <openmenu_savefile.h>
#include <openmenu_settings.h>

#include "backend/dcnow_fetch.h"
#include "backend/dcnow_net.h"
#include "backend/dcnow_vmu.h"

#define LCD_W        48
#define LCD_H        32
#define STRIP_H      7
#define ROW_H        6
#define ROWS_VISIBLE 4
#define COLS         12
#define STEP_FRAMES  15
#define GAMES_MAX    32

/* 3 by 5 font, one byte per row, bit 2 is the left pixel. Order: A to Z, 0 to
 * 9, space, then . : - / ! ? ( ) */
static const uint8_t font3x5[][5] = {
    {0x7, 0x5, 0x7, 0x5, 0x5}, /* A */
    {0x6, 0x5, 0x6, 0x5, 0x6}, /* B */
    {0x7, 0x4, 0x4, 0x4, 0x7}, /* C */
    {0x6, 0x5, 0x5, 0x5, 0x6}, /* D */
    {0x7, 0x4, 0x6, 0x4, 0x7}, /* E */
    {0x7, 0x4, 0x6, 0x4, 0x4}, /* F */
    {0x7, 0x4, 0x5, 0x5, 0x7}, /* G */
    {0x5, 0x5, 0x7, 0x5, 0x5}, /* H */
    {0x7, 0x2, 0x2, 0x2, 0x7}, /* I */
    {0x1, 0x1, 0x1, 0x5, 0x7}, /* J */
    {0x5, 0x5, 0x6, 0x5, 0x5}, /* K */
    {0x4, 0x4, 0x4, 0x4, 0x7}, /* L */
    {0x5, 0x7, 0x7, 0x5, 0x5}, /* M */
    {0x6, 0x5, 0x5, 0x5, 0x5}, /* N */
    {0x7, 0x5, 0x5, 0x5, 0x7}, /* O */
    {0x7, 0x5, 0x7, 0x4, 0x4}, /* P */
    {0x7, 0x5, 0x5, 0x7, 0x1}, /* Q */
    {0x6, 0x5, 0x6, 0x5, 0x5}, /* R */
    {0x7, 0x4, 0x7, 0x1, 0x7}, /* S */
    {0x7, 0x2, 0x2, 0x2, 0x2}, /* T */
    {0x5, 0x5, 0x5, 0x5, 0x7}, /* U */
    {0x5, 0x5, 0x5, 0x5, 0x2}, /* V */
    {0x5, 0x5, 0x7, 0x7, 0x5}, /* W */
    {0x5, 0x5, 0x2, 0x5, 0x5}, /* X */
    {0x5, 0x5, 0x2, 0x2, 0x2}, /* Y */
    {0x7, 0x1, 0x2, 0x4, 0x7}, /* Z */
    {0x7, 0x5, 0x5, 0x5, 0x7}, /* 0 */
    {0x2, 0x6, 0x2, 0x2, 0x7}, /* 1 */
    {0x7, 0x1, 0x7, 0x4, 0x7}, /* 2 */
    {0x7, 0x1, 0x7, 0x1, 0x7}, /* 3 */
    {0x5, 0x5, 0x7, 0x1, 0x1}, /* 4 */
    {0x7, 0x4, 0x7, 0x1, 0x7}, /* 5 */
    {0x7, 0x4, 0x7, 0x5, 0x7}, /* 6 */
    {0x7, 0x1, 0x1, 0x1, 0x1}, /* 7 */
    {0x7, 0x5, 0x7, 0x5, 0x7}, /* 8 */
    {0x7, 0x5, 0x7, 0x1, 0x7}, /* 9 */
    {0x0, 0x0, 0x0, 0x0, 0x0}, /* space */
    {0x0, 0x0, 0x0, 0x0, 0x2}, /* . */
    {0x0, 0x2, 0x0, 0x2, 0x0}, /* : */
    {0x0, 0x0, 0x7, 0x0, 0x0}, /* - */
    {0x1, 0x1, 0x2, 0x4, 0x4}, /* / */
    {0x2, 0x2, 0x2, 0x0, 0x2}, /* ! */
    {0x7, 0x1, 0x3, 0x0, 0x2}, /* ? */
    {0x2, 0x4, 0x4, 0x4, 0x2}, /* ( */
    {0x2, 0x1, 0x1, 0x1, 0x2}, /* ) */
};

typedef struct game_row {
    char code[16];
    int count;
} game_row_t;

static int shown_variant = 0; /* The library paints the plain icon at boot. */
static bool screen_active = false;

/* The list the rows are drawn from, rebuilt on every new fetch generation. */
static game_row_t games[GAMES_MAX];
static int game_count = 0;
static int real_games = 0; /* game_count without the idle row */
static int online_total = 0;
static int list_generation = -1;
static bool list_valid = false; /* The fetch store holds a list to draw. */

typedef struct row {
    char code[COLS + 1];
    char count[8];
} row_t;

/* The rows on screen plus the one entering below, and where the next comes from. */
static row_t queue[ROWS_VISIBLE + 1];
static int queue_len = 0;
static int next_game = 0;
static int scroll_px = 0; /* 0 to ROW_H - 1, how far the queue has moved up */
static int frame_counter = 0;
static uint64_t last_fetch_started = 0;

static uint8_t pixels[LCD_H][LCD_W];
static uint8_t frame[192] __attribute__((aligned(16)));
static uint8_t last_frame[192];
static bool dirty = true;

static int
glyph_index(char c) {
    static const char extra[] = " .:-/!?()";
    const char* e;

    if (c >= 'a' && c <= 'z') {
        c = (char)(c - 'a' + 'A');
    }
    if (c >= 'A' && c <= 'Z') {
        return c - 'A';
    }
    if (c >= '0' && c <= '9') {
        return 26 + (c - '0');
    }
    e = strchr(extra, c);
    return e != NULL ? 36 + (int)(e - extra) : 36;
}

static void
put_text(int x, int y, const char* s, int on) {
    for (; *s != '\0'; s++, x += 4) {
        const uint8_t* g = font3x5[glyph_index(*s)];
        for (int gy = 0; gy < 5; gy++) {
            for (int gx = 0; gx < 3; gx++) {
                int px = x + gx;
                int py = y + gy;
                if ((g[gy] >> (2 - gx)) & 1 && px >= 0 && px < LCD_W && py >= 0 && py < LCD_H) {
                    pixels[py][px] = (uint8_t)on;
                }
            }
        }
    }
}

/* Where text has to start to end one pixel short of the right edge. */
static int
right_x(const char* s) {
    return LCD_W - (int)strlen(s) * 4;
}

/* Text in the viewport only, so a row half way out is clipped by the strip. */
static void
put_row_text(int x, int y, const char* s) {
    for (int i = 0; s[i] != '\0'; i++) {
        const uint8_t* g = font3x5[glyph_index(s[i])];
        for (int gy = 0; gy < 5; gy++) {
            int py = y + gy;
            if (py < STRIP_H || py >= LCD_H) {
                continue;
            }
            for (int gx = 0; gx < 3; gx++) {
                if ((g[gy] >> (2 - gx)) & 1) {
                    pixels[py][x + i * 4 + gx] = 1;
                }
            }
        }
    }
}

static void
put_row(int y, const row_t* r) {
    put_row_text(1, y, r->code);
    put_row_text(right_x(r->count), y, r->count);
}

static void
put_centered(int y, const char* s) {
    put_text((LCD_W - ((int)strlen(s) * 4 - 1)) / 2, y, s, 1);
}

static void
strip(const char* left, const char* right) {
    memset(pixels[0], 1, LCD_W * STRIP_H);
    put_text(1, 1, left, 0);
    if (right != NULL) {
        put_text(right_x(right), 1, right, 0);
    }
}

/* The VMU shows the header rotated 180 degrees, so the view is packed
 * backwards. Bit 1 is a lit pixel. */
static void
pack(void) {
    memset(frame, 0, sizeof(frame));
    for (int y = 0; y < LCD_H; y++) {
        for (int x = 0; x < LCD_W; x++) {
            if (pixels[y][x]) {
                int hx = LCD_W - 1 - x;
                int hy = LCD_H - 1 - y;
                frame[hy * 6 + hx / 8] |= (uint8_t)(0x80 >> (hx % 8));
            }
        }
    }
}

static void
push(void) {
    pack();
    if (memcmp(frame, last_frame, sizeof(frame)) == 0 && !dirty) {
        return;
    }
    /* A save holds the LCD. The frame stays pending until it lets go. */
    if (savefile_lcd_busy()) {
        dirty = true;
        return;
    }
    memcpy(last_frame, frame, sizeof(frame));
    dirty = false;
    crayon_peripheral_vmu_display_icon(crayon_peripheral_dreamcast_get_screens(), frame);
}

static int
by_count_then_code(const void* a, const void* b) {
    const game_row_t* l = a;
    const game_row_t* r = b;

    if (l->count != r->count) {
        return r->count - l->count;
    }
    return strcmp(l->code, r->code);
}

/* One row: the game id left, the count right-aligned, the id cut to leave a
 * blank cell before the count. */
static void
format_row(row_t* out, const game_row_t* g) {
    int keep;

    snprintf(out->count, sizeof(out->count), "%d", g->count);
    keep = COLS - 1 - (int)strlen(out->count);
    snprintf(out->code, sizeof(out->code), "%.*s", keep, g->code);
}

/* Appends the next row from the newest list. Past the end comes one blank
 * row, then the list again from the top. */
static void
enqueue_next(void) {
    if (queue_len > ROWS_VISIBLE) {
        return;
    }
    if (next_game == game_count) {
        queue[queue_len].code[0] = '\0';
        queue[queue_len].count[0] = '\0';
        next_game = 0;
    } else {
        format_row(&queue[queue_len], &games[next_game]);
        next_game++;
    }
    queue_len++;
}

static void
reset_queue(void) {
    queue_len = 0;
    next_game = 0;
    scroll_px = 0;
    while (queue_len <= ROWS_VISIBLE) {
        enqueue_next();
    }
}

/* Groups the newest player list by game. The queue keeps its rows, only the
 * source of the next row changes. */
static void
take_games(const dcnow_fetch_status_t* fetch) {
    static dcnow_player_t players[DCNOW_PLAYER_MAX];
    int count = dcnow_fetch_copy(players, DCNOW_PLAYER_MAX);
    bool was_short = game_count <= ROWS_VISIBLE;
    int idle = 0;

    game_count = 0;
    for (int i = 0; i < count; i++) {
        int g;
        if (players[i].code[0] == '\0') {
            idle++;
            continue;
        }
        for (g = 0; g < game_count; g++) {
            if (strcmp(games[g].code, players[i].code) == 0) {
                break;
            }
        }
        if (g == game_count) {
            if (game_count == GAMES_MAX) {
                continue;
            }
            snprintf(games[g].code, sizeof(games[g].code), "%s", players[i].code);
            games[g].count = 0;
            game_count++;
        }
        games[g].count++;
    }
    qsort(games, (size_t)game_count, sizeof(games[0]), by_count_then_code);
    real_games = game_count;
    /* Players without a game share one row at the end of the list. */
    if (idle > 0 && game_count > 0 && game_count < GAMES_MAX) {
        snprintf(games[game_count].code, sizeof(games[0].code), "(IDLE)");
        games[game_count].count = idle;
        game_count++;
    }
    online_total = fetch->online_total;
    list_generation = fetch->generation;
    list_valid = fetch->list_valid;
    if (next_game >= game_count) {
        next_game = 0;
    }
    /* Short lists are drawn whole, so the queue only matters from the moment
     * the list first overflows the screen. */
    if (was_short && game_count > ROWS_VISIBLE) {
        reset_queue();
    }
}

static void
draw_list(void) {
    char total[8];

    snprintf(total, sizeof(total), "%d", online_total);
    strip("ONLINE", total);
    if (game_count <= ROWS_VISIBLE) {
        for (int i = 0; i < game_count; i++) {
            row_t row;
            format_row(&row, &games[i]);
            put_row(STRIP_H + 1 + i * ROW_H, &row);
        }
        return;
    }
    for (int i = 0; i < queue_len; i++) {
        put_row(STRIP_H + 1 + i * ROW_H - scroll_px, &queue[i]);
    }
}

static void
draw_screen(const dcnow_status_t* status) {
    memset(pixels, 0, sizeof(pixels));
    if (status->state == DCNOW_CONN_ONLINE && list_valid) {
        if (online_total == 0) {
            strip("ONLINE", "0");
            put_centered(15, "NO PLAYERS");
            put_centered(21, "ONLINE");
        } else if (real_games == 0) {
            char total[8];
            snprintf(total, sizeof(total), "%d", online_total);
            strip("ONLINE", total);
            put_centered(15, "NOBODY");
            put_centered(21, "IN A GAME");
        } else {
            draw_list();
        }
    } else if (status->state == DCNOW_CONN_ONLINE) {
        /* The first list can take most of a minute over a modem. */
        strip("ONLINE", NULL);
        put_centered(15, "FETCHING");
        put_centered(21, "PLAYERS");
    } else if (status->state == DCNOW_CONN_CONNECTING && status->hanging_up) {
        strip("OFFLINE", NULL);
        put_centered(18, "HANGING UP");
    } else if (status->state == DCNOW_CONN_CONNECTING) {
        /* The hint stays NONE for the few hundred milliseconds of the probe,
         * which shows the strip alone. */
        dcnow_device_t dev = dcnow_device_hint();
        strip("OFFLINE", NULL);
        if (dev == DCNOW_DEV_MODEM) {
            put_centered(15, "DIALING");
            put_centered(21, "DREAMPI");
        } else if (dev != DCNOW_DEV_NONE) {
            put_centered(15, "GETTING");
            put_centered(21, "ADDRESS");
        }
    } else {
        strip("OFFLINE", NULL);
        put_centered(15, "NOT");
        put_centered(21, "CONNECTED");
    }
}

/* 0 plain, 1 offline, 2 online, from the setting and the engine state. */
static int
icon_variant(const dcnow_status_t* status) {
    if (sf_dcnow[0] == DCNOW_OFF) {
        return 0;
    }
    return status->state == DCNOW_CONN_ONLINE ? 2 : 1;
}

static bool
screen_wanted(void) {
    return sf_dcnow[0] != DCNOW_OFF && sf_dcnow_refresh[0] != DCNOW_REFRESH_OFF && sf_dcnow_vmu[0] == DCNOW_VMU_ON;
}

void
dcnow_vmu_tick(void) {
    dcnow_status_t status;
    dcnow_fetch_status_t fetch;
    int variant;
    bool wanted;

    dcnow_conn_poll(&status);
    variant = icon_variant(&status);
    if (variant != shown_variant) {
        shown_variant = variant;
        savefile_set_lcd_variant(variant);
    }

    wanted = screen_wanted();
    if (wanted != screen_active) {
        screen_active = wanted;
        savefile_set_lcd_owner(wanted);
        dirty = true;
        if (!wanted) {
            /* Back to the icons: force the library to paint the current one. */
            shown_variant = -1;
            return;
        }
        reset_queue();
    }
    if (!screen_active) {
        return;
    }

    dcnow_fetch_poll(&fetch);
    if (fetch.generation != list_generation) {
        take_games(&fetch);
        dirty = true;
    }

    /* Auto-Refresh belongs to the tick while the screen is on. A fetch started
     * elsewhere, such as the window's Refresh, restarts the interval too. */
    if (fetch.state == DCNOW_FETCH_RUNNING) {
        last_fetch_started = timer_ms_gettime64();
    }
    if (status.state == DCNOW_CONN_ONLINE && fetch.state != DCNOW_FETCH_RUNNING
        && timer_ms_gettime64() - last_fetch_started >= (uint64_t)dcnow_refresh_seconds(sf_dcnow_refresh[0]) * 1000) {
        last_fetch_started = timer_ms_gettime64();
        dcnow_fetch_start();
    }

    /* The library does not repaint the logo while the app owns the LCD, so
     * the screen has to be redrawn when a save ends. */
    {
        static bool was_busy = false;
        bool busy = savefile_lcd_busy();
        if (was_busy && !busy) {
            dirty = true;
        }
        was_busy = busy;
    }

    if (++frame_counter < STEP_FRAMES && !dirty) {
        return;
    }
    if (frame_counter >= STEP_FRAMES) {
        frame_counter = 0;
        if (status.state == DCNOW_CONN_ONLINE && game_count > ROWS_VISIBLE) {
            if (++scroll_px == ROW_H) {
                scroll_px = 0;
                memmove(&queue[0], &queue[1], sizeof(queue[0]) * ROWS_VISIBLE);
                queue_len--;
                enqueue_next();
            }
        }
    }
    draw_screen(&status);
    push();
}

void
dcnow_vmu_redraw(void) {
    dirty = true;
}

void
dcnow_vmu_hanging_up(void) {
    if (!screen_active) {
        return;
    }
    memset(pixels, 0, sizeof(pixels));
    strip("OFFLINE", NULL);
    put_centered(18, "HANGING UP");
    push();
}

void
dcnow_vmu_release(void) {
    /* The link is down by now and nothing repaints the VMU once the game
     * runs, so the icon left behind is the offline one. */
    int variant = sf_dcnow[0] == DCNOW_OFF ? 0 : 1;

    if (screen_active) {
        screen_active = false;
        savefile_set_lcd_owner(false);
    } else if (variant == shown_variant) {
        return;
    }
    shown_variant = variant;
    savefile_set_lcd_variant(variant);
}
