/*
 * File: ui_dcnow.c
 * Project: ui
 * The Dreamcast Now! window, opened from the settings footer.
 */

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <strings.h>
#include <time.h>

#include <arch/timer.h>

#include <openmenu_settings.h>
#include "backend/dcnow_fetch.h"
#include "backend/dcnow_net.h"
#include "ui/draw_prototypes.h"
#include "ui/font_prototypes.h"
#include "ui/menu_mouse.h"
#include "ui/theme_manager.h"
#include "ui/ui_dcnow.h"
#include "ui/ui_menu_credits.h"

static bool mouse_command = false;
static uint32_t mouse_command_signature;
static enum draw_state* state_ptr = NULL;
static int* input_timeout_ptr = NULL;
static uint32_t text_color;
static uint32_t highlight_color;
static uint32_t menu_bkg_border_color;
static uint32_t menu_title_color;

static dcnow_device_t device = DCNOW_DEV_NONE;
static dcnow_isp_t isp;

/* Device and ISP lines shown above the separator */
#define INFO_MAX_LINES   4
#define INFO_LABEL_WIDTH 10
static char info_label[INFO_MAX_LINES][12];
static char info_value[INFO_MAX_LINES][48];
static int info_count = 0;

static void
add_info(const char* label, const char* value) {
    if (info_count >= INFO_MAX_LINES) {
        return;
    }
    snprintf(info_label[info_count], sizeof(info_label[0]), "%s", label);
    snprintf(info_value[info_count], sizeof(info_value[0]), "%s", value);
    info_count++;
}

static void
format_ip(char* out, size_t out_len, const uint8_t ip[4]) {
    snprintf(out, out_len, "%d.%d.%d.%d", ip[0], ip[1], ip[2], ip[3]);
}

static void
build_info(void) {
    char value[48];

    info_count = 0;
    add_info("Device", dcnow_device_name(device));

    switch (device) {
        case DCNOW_DEV_MODEM:
            add_info("Phone", DCNOW_DIAL_NUMBER);
            add_info("Login", DCNOW_DIAL_LOGIN);
            memset(value, '*', strlen(DCNOW_DIAL_PASSWORD));
            value[strlen(DCNOW_DIAL_PASSWORD)] = '\0';
            add_info("Password", value);
            break;
        case DCNOW_DEV_BBA:
        case DCNOW_DEV_LAN: {
            if (isp.ethernet_static) {
                char address[20];
                format_ip(address, sizeof(address), isp.ip);
                snprintf(value, sizeof(value), "%s (Static)", address);
                add_info("Address", value);
                format_ip(value, sizeof(value), isp.gateway);
                add_info("Gateway", value);
                format_ip(value, sizeof(value), isp.dns);
                add_info("DNS", value);
            } else if (isp.pppoe) {
                add_info("Address", "PPPoE (not supported, using DHCP)");
            } else {
                add_info("Address", "DHCP");
            }
        } break;
        default: break;
    }
}

/* Options offered below the status lines, by connection state */
#define OPT_CONNECT    0
#define OPT_CANCEL     1
#define OPT_RETRY      2
#define OPT_DISCONNECT 3
#define OPT_CLOSE      4
#define OPT_REFRESH    5

static const char* option_text[] = {"Connect", "Cancel", "Retry", "Disconnect", "Close", "Refresh"};

static dcnow_status_t status;
static int options[6];
static int option_count = 0;
static int probe_frames = 0; /* frames left before the device probe runs */

/* The player list and the fetch state as the window saw them last frame */
static dcnow_fetch_status_t fetch;
static dcnow_player_t list[DCNOW_PLAYER_MAX];
static int list_count = 0;
static int list_generation = -1;
static int list_scroll = 0;
static int focus = 0;      /* cursor over the list rows followed by the options */
static int have_list = 0;  /* a player list has arrived and is still valid */
static int list_total = 0; /* online players in the feed, the list keeps at most DCNOW_PLAYER_MAX */
static char focused_name[DCNOW_NAME_LEN];
static uint64_t last_fetch_started = 0; /* for the Auto-Refresh interval */

/* Frames between two accepted presses, the same value as the settings rows. */
#define DCNOW_INPUT_TIMEOUT 10

/* Rows of the list that fit above the options. */
static int
list_rows(void) {
    return device == DCNOW_DEV_MODEM ? 5 : 6;
}

/* Rebuilds the option list for the current state. Close is always last. */
static void
build_options(void) {
    /* With only Close on screen there is no option worth restoring. */
    int previous = option_count > 1 && focus >= list_count ? options[focus - list_count] : -1;

    option_count = 0;
    switch (status.state) {
        case DCNOW_CONN_IDLE:
            if (device == DCNOW_DEV_MODEM) {
                options[option_count++] = OPT_CONNECT;
            }
            options[option_count++] = OPT_CLOSE;
            break;
        case DCNOW_CONN_CONNECTING:
            if (status.can_cancel) {
                options[option_count++] = OPT_CANCEL;
            }
            options[option_count++] = OPT_CLOSE;
            break;
        case DCNOW_CONN_ONLINE:
            if (fetch.state == DCNOW_FETCH_FAILED && !have_list) {
                options[option_count++] = OPT_RETRY;
            } else if (fetch.state != DCNOW_FETCH_RUNNING || have_list) {
                options[option_count++] = OPT_REFRESH;
            }
            if (device == DCNOW_DEV_MODEM && !(fetch.state == DCNOW_FETCH_RUNNING && !have_list)) {
                options[option_count++] = OPT_DISCONNECT;
            }
            options[option_count++] = OPT_CLOSE;
            break;
        case DCNOW_CONN_FAILED:
            if (status.cooldown_seconds == 0) {
                options[option_count++] = OPT_RETRY;
            }
            options[option_count++] = OPT_CLOSE;
            break;
        case DCNOW_CONN_CANCELED:
            if (status.cooldown_seconds == 0) {
                options[option_count++] = OPT_CONNECT;
            }
            options[option_count++] = OPT_CLOSE;
            break;
        case DCNOW_CONN_COOLDOWN: options[option_count++] = OPT_CLOSE; break;
    }

    if (focus < list_count && list_count > 0) {
        return;
    }
    focus = list_count;
    for (int i = 0; i < option_count; i++) {
        if (options[i] == previous) {
            focus = list_count + i;
        }
    }
}

/* Sort order of the list: by country so nearby players sit together, then by
 * game, then by name. Unknown countries and idle players go last. */
static int
player_order(const void* left, const void* right) {
    const dcnow_player_t* a = left;
    const dcnow_player_t* b = right;

    if ((a->country[0] == '\0') != (b->country[0] == '\0')) {
        return a->country[0] == '\0' ? 1 : -1;
    }
    if (strcmp(a->country, b->country) != 0) {
        return strcmp(a->country, b->country);
    }
    if ((a->title[0] == '\0') != (b->title[0] == '\0')) {
        return a->title[0] == '\0' ? 1 : -1;
    }
    if (strcasecmp(a->title, b->title) != 0) {
        return strcasecmp(a->title, b->title);
    }
    return strcasecmp(a->name, b->name);
}

/* Copies a newly arrived list, sorted. The first list puts the cursor on its
 * first player. A refresh keeps it on the same player or the same option. */
static void
take_list(void) {
    int previous_list_count = list_count;
    int option_index = focus - previous_list_count;
    int had_list = have_list;

    list_count = dcnow_fetch_copy(list, DCNOW_PLAYER_MAX);
    qsort(list, (size_t)list_count, sizeof(list[0]), player_order);
    list_generation = fetch.generation;
    have_list = fetch.list_valid;
    list_total = fetch.online_total;
    list_scroll = 0;
    if (!had_list && list_count > 0) {
        focus = 0;
    } else if (option_index < 0) {
        focus = 0;
        for (int i = 0; i < list_count; i++) {
            if (strcmp(list[i].name, focused_name) == 0) {
                focus = i;
            }
        }
    } else {
        focus = list_count + option_index;
    }
    if (option_count > 0 && focus > list_count + option_count - 1) {
        focus = list_count + option_count - 1;
    }
    if (focus < 0) {
        focus = 0;
    }
}

/* Keeps the focused list row inside the visible rows. */
static void
clamp_list_scroll(void) {
    int rows = list_rows();

    if (focus < list_count) {
        if (focus < list_scroll) {
            list_scroll = focus;
        } else if (focus >= list_scroll + rows) {
            list_scroll = focus - rows + 1;
        }
    }
    if (list_scroll > list_count - rows) {
        list_scroll = list_count - rows;
    }
    if (list_scroll < 0) {
        list_scroll = 0;
    }
}

static void
start_connection(void) {
    if (dcnow_conn_start(device, &isp) == 0) {
        dcnow_conn_poll(&status);
        build_options();
    }
}

static void
start_fetch(void) {
    last_fetch_started = timer_ms_gettime64();
    dcnow_fetch_start();
    dcnow_fetch_poll(&fetch);
    build_options();
}

/* Time of day per the Clock setting (e.g., "11:12:40 AM" or "23:12:40"). */
static void
format_clock(char* out, size_t out_len, time_t when) {
    struct tm* t = localtime(&when);

    if (t == NULL) {
        snprintf(out, out_len, "--:--:--");
        return;
    }
    if (sf_clock[0] == CLOCK_24HOUR) {
        snprintf(out, out_len, "%02d:%02d:%02d", t->tm_hour, t->tm_min, t->tm_sec);
    } else {
        /* 12-hour is the default, Clock Off included. */
        int hour12 = t->tm_hour % 12;
        if (hour12 == 0) {
            hour12 = 12;
        }
        snprintf(out, out_len, "%d:%02d:%02d %s", hour12, t->tm_min, t->tm_sec, t->tm_hour < 12 ? "AM" : "PM");
    }
}

static void
summary_line(char* out, size_t out_len) {
    char when[16];

    format_clock(when, sizeof(when), fetch.updated);
    if (fetch.state == DCNOW_FETCH_RUNNING) {
        snprintf(out, out_len, "Refreshing player list...");
    } else if (fetch.state == DCNOW_FETCH_FAILED) {
        snprintf(out, out_len, "Refresh failed, showing the %s list.", when);
    } else if (list_total == 0) {
        snprintf(out, out_len, "No players online (updated %s)", when);
    } else if (list_total == 1) {
        snprintf(out, out_len, "1 player online (updated %s)", when);
    } else {
        snprintf(out, out_len, "%d players online (updated %s)", list_total, when);
    }
}

/* One list row: country, name, title, cut with "..." past the column widths.
 * A name keeps 20 columns, a title 24, one less when the scrollbar takes the
 * last one. A player without a game shows "(Idle)". */
static void
list_row(const dcnow_player_t* p, char* name, size_t name_len, char* title, size_t title_len, int title_max) {
    if (strlen(p->name) > 20) {
        snprintf(name, name_len, "%.17s...", p->name);
    } else {
        snprintf(name, name_len, "%s", p->name);
    }
    const char* text = p->title[0] != '\0' ? p->title : "(Idle)";
    if ((int)strlen(text) > title_max) {
        snprintf(title, title_len, "%.*s...", title_max - 3, text);
    } else {
        snprintf(title, title_len, "%s", text);
    }
}

void
dcnow_setup(enum draw_state* state, theme_color* _colors, int* timeout_ptr, uint32_t title_color) {
    text_color = _colors->menu_text_color;
    highlight_color = _colors->menu_highlight_color;
    menu_bkg_border_color = _colors->menu_bkg_border_color;
    menu_title_color = title_color;

    state_ptr = state;
    input_timeout_ptr = timeout_ptr;
    *input_timeout_ptr = 3;

    /* The device probe spins for a few hundred milliseconds, so the window
     * draws one frame with a placeholder before it runs. The main loop draws
     * in the same pass that opens the window, so that first draw does not
     * count. */
    probe_frames = 2;
    info_count = 0;
    option_count = 0;
    focus = 0;
    list_scroll = 0;
    dcnow_fetch_poll(&fetch);
    if (fetch.generation != list_generation) {
        take_list();
    }
    dcnow_conn_poll(&status);
}

void
dcnow_boot_autostart(void) {
    static int done = 0; /* A UI style change re-runs the hook that calls this. */

    if (done) {
        return;
    }
    done = 1;
    if (sf_dcnow[0] == DCNOW_AUTO_CONNECT) {
        dcnow_conn_autostart();
    }
}

/* Once an adapter is online the Address line shows the lease instead of "DHCP". */
static void
show_lease(void) {
    uint8_t ip[4];
    char address[20];

    if (device == DCNOW_DEV_MODEM || isp.ethernet_static || info_count < 2 || !dcnow_conn_address(ip)) {
        return;
    }
    format_ip(address, sizeof(address), ip);
    snprintf(info_value[1], sizeof(info_value[0]), "%s (DHCP)", address);
}

/* Runs the probe after the first frame and connects when the settings ask for
 * it. Retry never comes back here, since the device is known by then. */
static void
finish_probe(void) {
    device = dcnow_detect_device();
    dcnow_conn_poll(&status);
    /* A flash ROM read runs with interrupts off and could drop PPP frames, so
     * the engine's own copy is used when it has one. A modem needs nothing from
     * the flash ROM. */
    if (device != DCNOW_DEV_MODEM && !dcnow_conn_isp(&isp)) {
        dcnow_read_isp(&isp);
    }
    build_info();

    dcnow_conn_poll(&status);
    dcnow_fetch_poll(&fetch);
    option_count = 0;
    build_options();
    if (status.state == DCNOW_CONN_ONLINE) {
        show_lease();
        if (fetch.state == DCNOW_FETCH_IDLE) {
            start_fetch();
        }
    }

    /* Adapters need no Connect step. The modem waits for Connect here, since
     * Auto-Connect only dials at boot. */
    if (status.state == DCNOW_CONN_IDLE && (device == DCNOW_DEV_BBA || device == DCNOW_DEV_LAN)) {
        start_connection();
    }
}

/* Reads both engines once and applies what a change implies. Input and draw
 * share it, so whichever runs first after a change does the same work. */
static void
sync_state(void) {
    dcnow_status_t fresh;
    dcnow_fetch_status_t fresh_fetch;

    dcnow_conn_poll(&fresh);
    dcnow_fetch_poll(&fresh_fetch);
    if (fresh.state != status.state || fresh.can_cancel != status.can_cancel || fresh_fetch.state != fetch.state
        || (fresh.cooldown_seconds > 0) != (status.cooldown_seconds > 0)) {
        status = fresh;
        fetch = fresh_fetch;
        build_options();
        if (status.state == DCNOW_CONN_ONLINE) {
            show_lease();
            if (fetch.state == DCNOW_FETCH_IDLE) {
                start_fetch();
            }
        }
    } else {
        status = fresh;
        fetch = fresh_fetch;
    }
}

static void
dcnow_leave(void) {
    *state_ptr = DRAW_MENU;
    *input_timeout_ptr = 3;
}

static void
option_accept(void) {
    if (option_count == 0 || focus < list_count) {
        return;
    }
    switch (options[focus - list_count]) {
        case OPT_CONNECT:
        case OPT_RETRY:
            if (status.state == DCNOW_CONN_ONLINE) {
                start_fetch();
            } else {
                start_connection();
            }
            break;
        case OPT_REFRESH:
            if (fetch.state != DCNOW_FETCH_RUNNING) {
                start_fetch();
            }
            break;
        case OPT_CANCEL: dcnow_conn_cancel(); break;
        case OPT_DISCONNECT:
            dcnow_conn_disconnect();
            list_count = 0;
            list_total = 0;
            list_generation = -1;
            have_list = 0;
            focus = 0;
            dcnow_conn_poll(&status);
            dcnow_fetch_poll(&fetch);
            build_options();
            break;
        case OPT_CLOSE: dcnow_leave(); break;
        default: break;
    }
}

void
handle_input_dcnow(enum control input) {
    if (probe_frames > 0) {
        return;
    }

    /* Keep the option list in step with the engines before acting on it */
    sync_state();
    if (mouse_command && mouse_command_signature != dcnow_mouse_signature()) {
        input = NONE;
    }
    mouse_command = false;

    switch (input) {
        case UP:
            if (*input_timeout_ptr > 0 || list_count + option_count == 0) {
                break;
            }
            focus--;
            if (focus < 0) {
                focus = list_count + option_count - 1;
            }
            *input_timeout_ptr = DCNOW_INPUT_TIMEOUT;
            break;
        case DOWN:
            if (*input_timeout_ptr > 0 || list_count + option_count == 0) {
                break;
            }
            focus++;
            if (focus >= list_count + option_count) {
                focus = 0;
            }
            *input_timeout_ptr = DCNOW_INPUT_TIMEOUT;
            break;
        case A:
            if (*input_timeout_ptr > 0) {
                break;
            }
            option_accept();
            *input_timeout_ptr = DCNOW_INPUT_TIMEOUT;
            break;
        case B:
        case START:
            for (int i = 0; i < option_count; i++) {
                if (options[i] == OPT_CLOSE) {
                    dcnow_leave();
                    break;
                }
            }
            break;
        default: break;
    }
    if (focus < list_count) {
        snprintf(focused_name, sizeof(focused_name), "%s", list[focus].name);
    }
    clamp_list_scroll();
}

void
draw_dcnow_op(void) { /* nothing in the opaque pass */ }

/* Status lines for the state area. The engine stores counter lines without
 * their suffix so the seconds keep ticking here even while the worker is
 * blocked inside a KOS call. */
static int
status_lines(char lines[DCNOW_STATUS_LINES][DCNOW_STATUS_WIDTH]) {
    int count = status.count;

    /* The placeholder frame shows no leftovers from an earlier connection. */
    if (probe_frames > 0) {
        return 0;
    }
    if (device == DCNOW_DEV_NONE) {
        snprintf(lines[0], DCNOW_STATUS_WIDTH, "No Dial-Up Modem, Broadband Adapter, or LAN");
        snprintf(lines[1], DCNOW_STATUS_WIDTH, "Adapter was found in the expansion slot.");
        snprintf(lines[2], DCNOW_STATUS_WIDTH, "Power off, connect one, then reopen.");
        return 3;
    }
    for (int i = 0; i < count; i++) {
        if (i == status.counter_line) {
            int seconds = (int)((timer_ms_gettime64() - status.counter_since) / 1000);
            snprintf(lines[i], DCNOW_STATUS_WIDTH, "%s (%d %s)", status.lines[i], seconds,
                     seconds == 1 ? "second" : "seconds");
        } else {
            snprintf(lines[i], DCNOW_STATUS_WIDTH, "%s", status.lines[i]);
        }
    }
    if (status.cooldown_seconds > 0) {
        /* The lines of the hang-up, cancel or failure stay. The countdown goes below them. */
        int at = count < DCNOW_STATUS_LINES ? count++ : DCNOW_STATUS_LINES - 1;
        snprintf(lines[at], DCNOW_STATUS_WIDTH, "Waiting for DreamPi to reset (%d %s)", status.cooldown_seconds,
                 status.cooldown_seconds == 1 ? "second" : "seconds");
    }
    return count;
}

/* The fetch's own lines, with its "(waiting N seconds)" counter. */
static int
fetch_lines(char lines[DCNOW_STATUS_LINES][DCNOW_STATUS_WIDTH]) {
    for (int i = 0; i < fetch.count; i++) {
        if (i == fetch.counter_line) {
            int seconds = (int)((timer_ms_gettime64() - fetch.counter_since) / 1000);
            snprintf(lines[i], DCNOW_STATUS_WIDTH, "%s (waiting %d %s)", fetch.lines[i], seconds,
                     seconds == 1 ? "second" : "seconds");
        } else {
            snprintf(lines[i], DCNOW_STATUS_WIDTH, "%s", fetch.lines[i]);
        }
    }
    return fetch.count;
}

static void
draw_list_scrollbar(int x, int width, int top, int rows, int line_height) {
    int track_h = rows * line_height;
    int thumb_h;
    int thumb_y;

    if (list_count <= rows) {
        return;
    }
    thumb_h = track_h * rows / list_count;
    if (thumb_h < 16) {
        thumb_h = 16;
    }
    thumb_y = top + (track_h - thumb_h) * list_scroll / (list_count - rows);
    mouse_scrollbar_t bar = {x + width - 12, top, 6, track_h, thumb_y, thumb_h, 0, list_count - rows, rows};
    menu_mouse_scrollbar(&bar);
    draw_draw_quad(x + width - 12, top, 6, track_h, menu_bkg_border_color);
    draw_draw_quad(x + width - 11, thumb_y, 4, thumb_h, highlight_color);
}

/* BMF text that must stay inside max_width. The font's own shrink routine
 * assumes the default height, so the window scales from its 24 px itself. */
static void
draw_fit(int x, int y, uint32_t color, const char* text, int max_width) {
    float w = font_bmf_text_width(text);

    if (w > (float)max_width) {
        font_bmf_set_height(24.0f * (float)max_width / w);
        font_bmf_draw(x, y, color, text);
        font_bmf_set_height(24.0f);
    } else {
        font_bmf_draw(x, y, color, text);
    }
}

/* The window while the device probe is still pending: the title and one line. */
static void
draw_detecting(void) {
    const char* text = "Detecting device...";

    if (sf_ui[0] == UI_SCROLL || sf_ui[0] == UI_FOLDERS) {
        const int width = 408;
        const int height = 20 + 8 + 24 + 8;
        const int x = (640 / 2) - (width / 2);
        const int y = (480 / 2) - (height / 2);

        draw_popup_menu_ex(x, y, width, height, sf_ui[0]);
        font_bmp_begin_draw();
        font_bmp_set_color(menu_title_color);
        font_bmp_draw_main(x + width / 2 - (14 * 8 / 2), y + 2, "Dreamcast Now!");
        font_bmp_set_color(text_color);
        font_bmp_draw_main(x + 8, y + 20 + 8 + 4, text);
    } else {
        const int width = 484;
        const int height = 28 + 8 + 26 + 8;
        const int x = (640 / 2) - (width / 2);
        const int y = (480 / 2) - (height / 2);

        draw_popup_menu_ex(x, y, width, height, sf_ui[0]);
        font_bmf_begin_draw();
        font_bmf_set_height(24.0f);
        font_bmf_draw(x + 4, y + 2, text_color, "Dreamcast Now!");
        font_bmf_draw(x + 4, y + 28 + 8, text_color, text);
        font_bmf_set_height_default();
    }
}

void
draw_dcnow_tr(void) {
    char lines[DCNOW_STATUS_LINES][DCNOW_STATUS_WIDTH];

    z_set_cond(205.0f);

    /* The placeholder frame has been shown, so the probe can run. */
    if (probe_frames > 0) {
        if (--probe_frames == 0) {
            finish_probe();
        }
    }

    /* The engines run on their own threads, so every frame reads fresh
     * snapshots. The option list stays empty until the probe has run. */
    if (probe_frames > 0) {
        dcnow_conn_poll(&status);
    } else {
        sync_state();
    }
    if (probe_frames > 0) {
        draw_detecting();
        return;
    }
    dcnow_fetch_poll(&fetch);
    if (fetch.generation != list_generation) {
        take_list();
        build_options();
    }
    if (status.state != DCNOW_CONN_ONLINE && (list_count > 0 || have_list)) {
        list_count = 0;
        list_total = 0;
        list_generation = -1;
        have_list = 0;
        focus = 0;
        build_options();
    }

    /* Auto-Refresh, counted from the last fetch start, skipped while one runs. */
    if (sf_dcnow_vmu[0] != DCNOW_VMU_ON && status.state == DCNOW_CONN_ONLINE && fetch.state != DCNOW_FETCH_RUNNING
        && dcnow_refresh_seconds(sf_dcnow_refresh[0]) > 0
        && timer_ms_gettime64() - last_fetch_started >= (uint64_t)dcnow_refresh_seconds(sf_dcnow_refresh[0]) * 1000) {
        start_fetch();
    }
    clamp_list_scroll();

    /* Online with a list: the summary and the rows. Otherwise the engine's
     * lines, followed by the fetch's lines while one runs or failed. */
    const int show_list = status.state == DCNOW_CONN_ONLINE && have_list;
    int status_count = 0;
    char summary[DCNOW_STATUS_WIDTH];
    char fetch_buf[DCNOW_STATUS_LINES][DCNOW_STATUS_WIDTH];
    int fetch_count = 0;
    const int rows = list_rows();
    int visible = list_count < rows ? list_count : rows;

    if (show_list) {
        summary_line(summary, sizeof(summary));
    } else {
        status_count = status_lines(lines);
        if (status.state == DCNOW_CONN_ONLINE) {
            fetch_count = fetch_lines(fetch_buf);
        }
        visible = 0;
    }

    /* Body rows: summary + blank + rows, or the status lines. One blank row
     * separates the body from the options. An empty list has no blank row under
     * the summary, since the gap before the options is enough. */
    const int body_rows = show_list ? (visible > 0 ? 2 + visible : 1) : status_count + fetch_count;
    const int gap_rows = (body_rows > 0 && option_count > 0) ? 1 : 0;

    if (sf_ui[0] == UI_SCROLL || sf_ui[0] == UI_FOLDERS) {
        const int line_height = 24;
        const int width = 408;
        const int pad = 8;
        int height = 20 + pad + info_count * line_height + pad + 2 + pad
                     + (body_rows + gap_rows + option_count) * line_height + pad;
        const int x = (640 / 2) - (width / 2);
        const int y = (480 / 2) - (height / 2);
        const int x_item = x + 8;
        const int sep_y = y + 20 + pad + info_count * line_height + pad;
        char line_buf[64];

        draw_popup_menu_ex(x, y, width, height, sf_ui[0]);
        if (show_list) {
            menu_mouse_scroll(&focus, &list_scroll, list_count, rows, NULL);
        }
        draw_draw_quad(x, sep_y, width, 2, menu_bkg_border_color);
        if (show_list) {
            draw_list_scrollbar(x, width, sep_y + 2 + pad + 2 * line_height, rows, line_height);
        }

        font_bmp_begin_draw();
        font_bmp_set_color(menu_title_color);
        font_bmp_draw_main(x + width / 2 - (14 * 8 / 2), y + 2, "Dreamcast Now!");

        font_bmp_set_color(text_color);
        int cur_y = y + 20 + pad + 4;
        for (int i = 0; i < info_count; i++) {
            snprintf(line_buf, sizeof(line_buf), "%-*s%s", INFO_LABEL_WIDTH, info_label[i], info_value[i]);
            font_bmp_draw_main(x_item, cur_y, line_buf);
            cur_y += line_height;
        }

        cur_y = sep_y + 2 + pad + 4;
        if (show_list) {
            font_bmp_draw_main(x_item, cur_y, summary);
            cur_y += (visible > 0 ? 2 : 1) * line_height;
            for (int i = 0; i < visible; i++) {
                char name[24];
                char title[32];
                const int row = list_scroll + i;

                list_row(&list[row], name, sizeof(name), title, sizeof(title), list_count > rows ? 23 : 24);
                snprintf(line_buf, sizeof(line_buf), "%-2s %-*s %s", list[row].country, list_count > rows ? 23 : 24,
                         title, name);
                font_bmp_set_color(row == focus ? highlight_color : text_color);
                menu_mouse_row(x_item, cur_y - 4, width - 24, line_height, &focus, row, NONE);
                font_bmp_draw_main(x_item, cur_y, line_buf);
                cur_y += line_height;
            }
            font_bmp_set_color(text_color);
        } else {
            for (int i = 0; i < status_count; i++) {
                font_bmp_draw_main(x_item, cur_y, lines[i]);
                cur_y += line_height;
            }
            for (int i = 0; i < fetch_count; i++) {
                font_bmp_draw_main(x_item, cur_y, fetch_buf[i]);
                cur_y += line_height;
            }
        }
        cur_y += gap_rows * line_height;
        for (int i = 0; i < option_count; i++) {
            font_bmp_set_color(list_count + i == focus ? highlight_color : text_color);
            menu_mouse_row(x_item, cur_y, width - 16, 20, &focus, list_count + i, A);
            font_bmp_draw_main(x_item, cur_y, option_text[options[i]]);
            cur_y += line_height;
        }
    } else {
        const int line_height = 26;
        const int width = 484;
        const int title_height = 28;
        const int pad = 8;
        int height = title_height + pad + info_count * line_height + pad + 2 + pad
                     + (body_rows + gap_rows + option_count) * line_height + pad;
        const int x = (640 / 2) - (width / 2);
        const int y = (480 / 2) - (height / 2);
        const int x_item = x + 4;
        const int x_value = x_item + 112; /* clears the widest label, Password, at 92 px */
        const int sep_y = y + title_height + pad + info_count * line_height + pad;

        draw_popup_menu_ex(x, y, width, height, sf_ui[0]);
        draw_draw_quad(x, sep_y, width, 2, menu_bkg_border_color);
        if (show_list) {
            draw_list_scrollbar(x, width, sep_y + 2 + pad + 2 * line_height, rows, line_height);
        }

        font_bmf_begin_draw();
        font_bmf_set_height(24.0f);
        font_bmf_draw(x_item, y + 2, text_color, "Dreamcast Now!");

        int cur_y = y + title_height + pad;
        for (int i = 0; i < info_count; i++) {
            font_bmf_draw(x_item, cur_y, text_color, info_label[i]);
            draw_fit(x_value, cur_y, text_color, info_value[i], width - 120);
            cur_y += line_height;
        }

        cur_y = sep_y + 2 + pad;
        if (show_list) {
            draw_fit(x_item, cur_y, text_color, summary, width - 8);
            cur_y += (visible > 0 ? 2 : 1) * line_height;
            for (int i = 0; i < visible; i++) {
                char name[24];
                char title[32];
                const int row = list_scroll + i;
                const uint32_t color = row == focus ? highlight_color : text_color;

                list_row(&list[row], name, sizeof(name), title, sizeof(title), list_count > rows ? 23 : 24);
                /* The BMF drawer reads past an empty string, so a missing
                 * country draws nothing in its column. */
                if (list[row].country[0] != '\0') {
                    font_bmf_draw(x_item, cur_y, color, list[row].country);
                }
                draw_fit(x_item + 48, cur_y, color, title, 190);
                /* The name column stops 8 px short of the scrollbar track. */
                draw_fit(x_item + 248, cur_y, color, name, width - 272);
                cur_y += line_height;
            }
        } else {
            for (int i = 0; i < status_count; i++) {
                draw_fit(x_item, cur_y, text_color, lines[i], width - 8);
                cur_y += line_height;
            }
            for (int i = 0; i < fetch_count; i++) {
                draw_fit(x_item, cur_y, text_color, fetch_buf[i], width - 8);
                cur_y += line_height;
            }
        }
        cur_y += gap_rows * line_height;
        for (int i = 0; i < option_count; i++) {
            font_bmf_draw(x_item, cur_y, list_count + i == focus ? highlight_color : text_color,
                          option_text[options[i]]);
            cur_y += line_height;
        }
        font_bmf_set_height_default();
    }
}

uint32_t
dcnow_mouse_signature(void) {
    dcnow_status_t fresh;
    dcnow_fetch_status_t fresh_fetch;
    dcnow_conn_poll(&fresh);
    dcnow_fetch_poll(&fresh_fetch);
    int state[] = {state_ptr ? *state_ptr : DRAW_UI,
                   probe_frames,
                   fresh.state,
                   fresh.can_cancel,
                   fresh.cooldown_seconds > 0,
                   fresh_fetch.state,
                   fresh_fetch.generation,
                   fresh.count,
                   fresh_fetch.count,
                   status.count,
                   fetch.count,
                   status.state,
                   fetch.state,
                   list_generation,
                   list_count,
                   list_scroll,
                   option_count,
                   info_count,
                   have_list};
    uint32_t hash = menu_mouse_hash(2166136261u, state, sizeof(state));
    return menu_mouse_hash(hash, options, option_count * sizeof(int));
}

bool
handle_mouse_dcnow(enum control* input) {
    int* selected = NULL;
    bool cancel = false;
    for (int i = 0; i < option_count; i++) {
        if (options[i] == OPT_CLOSE) {
            cancel = true;
        }
    }
    uint32_t signature = dcnow_mouse_signature();
    bool consumed = menu_mouse_read(input, probe_frames > 0, cancel, &selected);
    if (consumed && *input != NONE) {
        mouse_command = true;
        mouse_command_signature = signature;
        *input_timeout_ptr = 0;
    }
    return consumed;
}
