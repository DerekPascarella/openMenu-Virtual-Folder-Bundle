/*
 * File: dcnow_net.c
 * Project: openmenu
 * Dreamcast Now! networking: expansion device detection and ISP settings.
 */

#include <fcntl.h>
#include <stdbool.h>
#include <stdio.h>
#include <string.h>
#include <unistd.h>

#include <arch/timer.h>
#include <dc/asic.h>
#include <dc/flashrom.h>
#include <dc/modem/modem.h>
#include <dc/net/broadband_adapter.h>
#include <dc/net/lan_adapter.h>
#include <kos/mutex.h>
#include <kos/net.h>
#include <kos/thread.h>
#include <netinet/in.h>
#include <openmenu_settings.h>
#include <ppp/ppp.h>
#include <sys/socket.h>

#include "backend/dcnow_fetch.h"
#include "backend/dcnow_net.h"
#include "backend/dcnow_vmu.h"
#include "backend/online_time_sync.h"

static int device_probed = 0;
/* Set once modem_init() has run, which leaves an interrupt hook for the teardown. */
static int modem_was_used = 0;
/* KOS declares this in kernel/net/net_dhcp.h, which it does not install, yet
 * it is the only way to ask for a lease again once net_init() has run. */
extern int net_dhcp_request(uint32_t required_address);

static mutex_t probe_mutex = MUTEX_INITIALIZER;
static dcnow_device_t device_found = DCNOW_DEV_NONE;

dcnow_device_t
dcnow_detect_device(void) {
    mutex_lock(&probe_mutex);
    if (!device_probed) {
        device_probed = 1;

        /* The adapter probes also register the interface with KOS for the
         * later net_init(), so this must only ever run once. */
        if (bba_init() == 0) {
            device_found = DCNOW_DEV_BBA;
        } else if (la_init() == 0) {
            device_found = DCNOW_DEV_LAN;
        } else if (modem_init()) {
            /* Leave the modem powered down until a dial is requested. */
            modem_was_used = 1;
            modem_shutdown();
            device_found = DCNOW_DEV_MODEM;
        }
    }
    mutex_unlock(&probe_mutex);
    return device_found;
}

dcnow_device_t
dcnow_device_hint(void) {
    return device_found;
}

const char*
dcnow_device_name(dcnow_device_t dev) {
    switch (dev) {
        case DCNOW_DEV_MODEM: return "Dial-Up Modem";
        case DCNOW_DEV_BBA: return "Broadband Adapter (HIT-0400)";
        case DCNOW_DEV_LAN: return "LAN Adapter (HIT-0300)";
        default: return "None detected";
    }
}

static void
address_from_flashrom(dcnow_isp_t* out, const flashrom_ispcfg_t* cfg) {
    out->ethernet_static = (cfg->method == FLASHROM_ISP_STATIC);
    out->pppoe = (cfg->method == FLASHROM_ISP_PPPOE);
    if (cfg->valid_fields & FLASHROM_ISP_IP) {
        memcpy(out->ip, cfg->ip, 4);
    }
    if (cfg->valid_fields & FLASHROM_ISP_NETMASK) {
        memcpy(out->netmask, cfg->nm, 4);
    }
    if (cfg->valid_fields & FLASHROM_ISP_GATEWAY) {
        memcpy(out->gateway, cfg->gw, 4);
    }
    if (cfg->valid_fields & FLASHROM_ISP_DNS) {
        memcpy(out->dns, cfg->dns[0], 4);
    }
}

void
dcnow_read_isp(dcnow_isp_t* out) {
    flashrom_ispcfg_t sega;
    flashrom_ispcfg_t pw;

    memset(out, 0, sizeof(*out));
    /* Block 0xE0 on the Sega side is the only place a static address is stored. */
    if (flashrom_get_ispcfg(&sega) == 0) {
        address_from_flashrom(out, &sega);
    } else if (flashrom_get_pw_ispcfg(&pw) == 0) {
        address_from_flashrom(out, &pw);
    }
}

/* Dial speed. MODEM_SPEED_V8_AUTO lets the modems pick the best rate. Set to
 * MODEM_SPEED_V8_14400 to cap the handshake for a marginal DreamPi line. */
#define DCNOW_MODEM_SPEED     MODEM_SPEED_V8_AUTO

#define DCNOW_DIAL_TIMEOUT_MS 90000
#define DCNOW_COOLDOWN_MS     5000

static mutex_t status_mutex = MUTEX_INITIALIZER;
static dcnow_status_t status = {DCNOW_CONN_IDLE, 0, 0, -1, 0, {{0}}, 0, 0};
static volatile int cancel_requested = 0;
static kthread_t* worker = NULL;
static int net_started = 0;
static dcnow_device_t active_device = DCNOW_DEV_NONE;
static uint64_t hangup_time = 0; /* guarded by status_mutex */

typedef enum worker_job { JOB_CONNECT, JOB_HANGUP, JOB_AUTOSTART, JOB_LOST } worker_job_t;

static worker_job_t worker_job = JOB_CONNECT;

/* Copies of the settings for the worker, so the UI can drop its own. */
static int adapter_static = 0;
static uint8_t static_ip[4];
static uint8_t static_netmask[4];
static uint8_t static_gateway[4];
static uint8_t static_dns[4];

static void
status_reset(dcnow_conn_state_t state) {
    mutex_lock(&status_mutex);
    status.state = state;
    status.can_cancel = 0;
    status.cooldown_seconds = 0;
    status.counter_line = -1;
    status.count = 0;
    status.hanging_up = 0;
    mutex_unlock(&status_mutex);
}

/* Appends a line, dropping the oldest when the ring is full. A line with a
 * counter gets "(N seconds)" appended by the window every frame. */
static void
status_push(const char* text, int with_counter) {
    mutex_lock(&status_mutex);
    if (status.count == DCNOW_STATUS_LINES) {
        memmove(status.lines[0], status.lines[1], sizeof(status.lines[0]) * (DCNOW_STATUS_LINES - 1));
        status.count--;
        if (status.counter_line == 0) {
            status.counter_line = -1;
        } else if (status.counter_line > 0) {
            status.counter_line--;
        }
    }
    snprintf(status.lines[status.count], DCNOW_STATUS_WIDTH, "%s", text);
    if (with_counter) {
        status.counter_line = status.count;
        status.counter_since = timer_ms_gettime64();
    }
    status.count++;
    mutex_unlock(&status_mutex);
}

/* Rewrites one line in place and stops its counter, used for the OK suffix. */
static void
status_replace(int index, const char* text) {
    mutex_lock(&status_mutex);
    if (index >= 0 && index < status.count) {
        snprintf(status.lines[index], DCNOW_STATUS_WIDTH, "%s", text);
        if (status.counter_line == index) {
            status.counter_line = -1;
        }
    }
    mutex_unlock(&status_mutex);
}

static int
status_last_index(void) {
    int index;
    mutex_lock(&status_mutex);
    index = status.count - 1;
    mutex_unlock(&status_mutex);
    return index;
}

static void
status_set_state(dcnow_conn_state_t state, int can_cancel) {
    mutex_lock(&status_mutex);
    status.state = state;
    status.can_cancel = can_cancel;
    mutex_unlock(&status_mutex);
}

/* Marks the CONNECTING state as a teardown, so the VMU can tell a hang-up from
 * a dial. Every status_reset() and enter_cooldown() clears it. */
static void
mark_hanging_up(void) {
    mutex_lock(&status_mutex);
    status.hanging_up = 1;
    mutex_unlock(&status_mutex);
}

/* Starts the DreamPi cooldown clock without changing the state. */
static void
start_cooldown_clock(void) {
    mutex_lock(&status_mutex);
    hangup_time = timer_ms_gettime64();
    mutex_unlock(&status_mutex);
}

/* Keeps the lines on screen and starts the DreamPi cooldown clock. */
static void
enter_cooldown(void) {
    mutex_lock(&status_mutex);
    status.state = DCNOW_CONN_COOLDOWN;
    status.can_cancel = 0;
    status.counter_line = -1;
    status.hanging_up = 0;
    hangup_time = timer_ms_gettime64();
    mutex_unlock(&status_mutex);
}

/* Failure lines: the header, the reason, and advice when there is any. */
static void
fail(const char* reason, const char* advice) {
    status_reset(DCNOW_CONN_FAILED);
    status_push("Connection failed.", 0);
    status_push(reason, 0);
    if (advice != NULL) {
        status_push(advice, 0);
    }
}

/* Waits while the modem is still training. Returns 1 when it settled, 0 on
 * timeout, -1 when the user canceled. */
static int
wait_for_carrier(void) {
    uint64_t start = timer_ms_gettime64();

    while (modem_is_connecting()) {
        if (cancel_requested) {
            return -1;
        }
        if (timer_ms_gettime64() - start >= DCNOW_DIAL_TIMEOUT_MS) {
            return 0;
        }
        thd_sleep(100);
    }
    return 1;
}

/* The PPP device for the modem, the same shape libppp uses internally. */
static int
ppp_dev_noop(ppp_device_t* self) {
    (void)self;
    return 0;
}

static int
ppp_dev_shutdown(ppp_device_t* self) {
    (void)self;
    if (modem_is_connected()) {
        modem_disconnect();
    }
    modem_shutdown();
    return 0;
}

static int
ppp_dev_tx(ppp_device_t* self, const uint8_t* data, size_t len, uint32_t flags) {
    size_t done = 0;
    int tries = 0;
    (void)self;
    (void)flags;

    while (done < len && tries < 1000) {
        int sent = modem_write_data((unsigned char*)data + done, (int)(len - done));
        if (sent <= 0) {
            tries++;
            thd_pass();
        } else {
            done += (size_t)sent;
        }
    }
    return done == len ? 0 : -1;
}

static const uint8_t*
ppp_dev_rx(ppp_device_t* self, ssize_t* out_len) {
    static uint8_t rb[1024];
    int cnt;
    (void)self;

    cnt = modem_read_data((unsigned char*)rb, 1024);
    if (cnt > 0) {
        *out_len = (ssize_t)cnt;
        return rb;
    }
    *out_len = 0;
    return NULL;
}

static ppp_device_t modem_dev = {
    "modem",       "PPP over Dreamcast Modem", 0,           0,          NULL, &ppp_dev_noop,
    &ppp_dev_noop, &ppp_dev_shutdown,          &ppp_dev_tx, &ppp_dev_rx};

/* libppp's ppp_shutdown() leaves its reader thread running until the phase
 * becomes DEAD, and only ppp_init() writes that. This pair makes it exit. */
static void
ppp_release(void) {
    ppp_shutdown();
    thd_sleep(120);
    if (ppp_init() >= 0) {
        thd_sleep(40);
        ppp_shutdown();
    }
    thd_sleep(240);
}

#define DCNOW_PPP_LCP               0xC021
#define DCNOW_LCP_TERMINATE_REQUEST 5

/* Asks the peer to close the link the PPP way with an LCP Terminate-Request,
 * so pppd on DreamPi exits at once instead of waiting for a carrier drop its
 * modem may never report. libppp ignores an ack for a request it did not send. */
static void
ppp_terminate(void) {
    static uint8_t id = 0x40;
    uint8_t request[4];

    for (int i = 0; i < 2; i++) {
        request[0] = DCNOW_LCP_TERMINATE_REQUEST;
        request[1] = id++;
        request[2] = 0;
        request[3] = 4; /* length of the whole packet, high byte first */
        ppp_send(request, sizeof(request), DCNOW_PPP_LCP);
        thd_sleep(300);
    }
    thd_sleep(400);
}

static void
ensure_net_started(void) {
    if (!net_started) {
        net_init(0);
        net_started = 1;
    }
}

static void
modem_hangup(void) {
    modem_disconnect();
    modem_shutdown();
}

/* Ends a dial the user has given up on. Returns 1 when it did. A number that
 * already went out gets the cooldown, since the DreamPi may have answered. */
static int
cancel_now(int dialed) {
    if (!cancel_requested) {
        return 0;
    }
    modem_hangup();
    status_reset(DCNOW_CONN_CANCELED);
    status_push("Dial canceled.", 0);
    if (dialed) {
        start_cooldown_clock();
    }
    return 1;
}

static void
run_modem(void) {
    char line[DCNOW_STATUS_WIDTH];
    int rc;

    modem_was_used = 1;
    status_set_state(DCNOW_CONN_CONNECTING, 1);
    status_push("Initializing modem...", 0);
    if (!modem_init()) {
        fail("Modem did not respond (modem -1).", "Power off and check the modem is seated.");
        return;
    }
    status_replace(status_last_index(), "Initializing modem... OK");
    modem_set_mode(MODEM_MODE_REMOTE, DCNOW_MODEM_SPEED);
    if (cancel_now(0)) {
        return;
    }

    snprintf(line, sizeof(line), "Dialing %s...", DCNOW_DIAL_NUMBER);
    status_push(line, 0);
    if (!modem_dial(DCNOW_DIAL_NUMBER)) {
        modem_shutdown();
        fail("Dialing failed (modem -3).", NULL);
        return;
    }
    snprintf(line, sizeof(line), "Dialing %s... OK", DCNOW_DIAL_NUMBER);
    status_replace(status_last_index(), line);

    status_push("Modem: Negotiating", 1);
    rc = wait_for_carrier();
    if (rc < 0) {
        cancel_now(1);
        return;
    }
    if (rc == 0) {
        modem_hangup();
        fail("No carrier after 90 seconds (modem -4).", "Check DreamPi and the phone cable.");
        start_cooldown_clock();
        return;
    }
    if (!modem_is_connected()) {
        modem_hangup();
        fail("No carrier, handshake failed (modem -5).", "Check DreamPi and the phone cable.");
        start_cooldown_clock();
        return;
    }
    status_replace(status_last_index(), "Modem: Connected");

    /* From here on the dial cannot be interrupted. */
    status_set_state(DCNOW_CONN_CONNECTING, 0);
    ensure_net_started();
    if (ppp_init() < 0) {
        modem_hangup();
        fail("PPP negotiation failed (ppp -1).", "Check DreamPi login and password.");
        start_cooldown_clock();
        return;
    }
    ppp_set_device(&modem_dev);
    ppp_set_login(DCNOW_DIAL_LOGIN, DCNOW_DIAL_PASSWORD);
    status_push("PPP: Negotiating", 1);
    if (ppp_connect() != 0) {
        ppp_release();
        modem_hangup();
        fail("PPP negotiation failed (ppp -1).", "Check DreamPi login and password.");
        start_cooldown_clock();
        return;
    }
    status_replace(status_last_index(), "PPP: Connected");
    dcnow_presence_lookup();
    status_set_state(DCNOW_CONN_ONLINE, 0);
}

/* Fills in the fixed address before the stack starts, so net_init() skips its
 * DHCP request. */
static void
apply_static_address(void) {
    netif_t* cur;

    LIST_FOREACH(cur, &net_if_list, if_list) {
        memcpy(cur->ip_addr, static_ip, 4);
        memcpy(cur->netmask, static_netmask, 4);
        memcpy(cur->gateway, static_gateway, 4);
        memcpy(cur->dns, static_dns, 4);
        for (int i = 0; i < 4; i++) {
            cur->broadcast[i] = (uint8_t)(static_ip[i] | (uint8_t)~static_netmask[i]);
        }
    }
}

static void
run_adapter(void) {
    char line[DCNOW_STATUS_WIDTH];
    int start_line;
    int dhcp_line = -1;

    status_set_state(DCNOW_CONN_CONNECTING, 0);
    status_push("Starting network...", 0);
    start_line = status_last_index();
    if (!net_started) {
        if (adapter_static) {
            apply_static_address();
        } else {
            /* net_init() runs the DHCP request itself and blocks for up to 60 s,
             * so the counter for it is announced before the call. */
            status_push("DHCP: Requesting an address", 1);
            dhcp_line = status_last_index();
        }
    } else if (!adapter_static && net_default_dev != NULL && net_default_dev->ip_addr[0] == 0) {
        /* A Retry after a failed lease. The stack is up, only DHCP runs again. */
        status_push("DHCP: Requesting an address", 1);
        dhcp_line = status_last_index();
        net_dhcp_request(0);
    }
    ensure_net_started();

    if (net_default_dev == NULL) {
        fail("Network did not start (net -1).", NULL);
        return;
    }
    status_replace(start_line, "Starting network... OK");
    if (net_default_dev->ip_addr[0] == 0) {
        fail("DHCP: No answer after 60 seconds.", "Check the cable and the router.");
        return;
    }
    if (!adapter_static) {
        snprintf(line, sizeof(line), "DHCP: Address %d.%d.%d.%d", net_default_dev->ip_addr[0],
                 net_default_dev->ip_addr[1], net_default_dev->ip_addr[2], net_default_dev->ip_addr[3]);
        if (dhcp_line >= 0) {
            status_replace(dhcp_line, line);
        } else {
            status_push(line, 0);
        }
    }
    dcnow_presence_lookup();
    status_set_state(DCNOW_CONN_ONLINE, 0);
}

static void
run_hangup(void) {
    status_push("Hanging up...", 0);
    ppp_terminate();
    ppp_release();
    modem_hangup();
    net_default_dev = NULL;
    status_replace(status_last_index(), "Hanging up... OK");
    enter_cooldown();
}

/* The settings the engine last connected with, for a window opened while the
 * link is up or coming up. */
static dcnow_isp_t known_isp;
static int known_isp_valid = 0;

static void
take_isp(const dcnow_isp_t* isp) {
    mutex_lock(&status_mutex);
    known_isp = *isp;
    known_isp_valid = 1;
    mutex_unlock(&status_mutex);
    adapter_static = isp->ethernet_static;
    memcpy(static_ip, isp->ip, 4);
    memcpy(static_netmask, isp->netmask, 4);
    memcpy(static_gateway, isp->gateway, 4);
    memcpy(static_dns, isp->dns, 4);
}

static void
run_autostart(void) {
    dcnow_isp_t isp;

    active_device = dcnow_detect_device();
    if (active_device == DCNOW_DEV_NONE) {
        status_reset(DCNOW_CONN_IDLE);
        return;
    }
    dcnow_read_isp(&isp);
    take_isp(&isp);
    if (active_device == DCNOW_DEV_MODEM) {
        run_modem();
    } else {
        run_adapter();
    }
}

/* The carrier went away under a live link. Tears it down without the LCP
 * request, which has nobody to hear it. */
static void
run_lost(void) {
    online_time_sync_abort();
    dcnow_fetch_clear();
    ppp_release();
    modem_hangup();
    net_default_dev = NULL;
    fail("Connection lost.", NULL);
    start_cooldown_clock();
}

static void*
worker_main(void* param) {
    (void)param;
    switch (worker_job) {
        case JOB_HANGUP: run_hangup(); break;
        case JOB_AUTOSTART: run_autostart(); break;
        case JOB_LOST: run_lost(); break;
        default:
            if (active_device == DCNOW_DEV_MODEM) {
                run_modem();
            } else {
                run_adapter();
            }
            break;
    }
    return NULL;
}

static void
finish_worker(void) {
    if (worker != NULL) {
        thd_join(worker, NULL);
        worker = NULL;
    }
}

int
dcnow_conn_start(dcnow_device_t dev, const dcnow_isp_t* isp) {
    dcnow_status_t snap;

    dcnow_conn_poll(&snap);
    if (snap.state == DCNOW_CONN_CONNECTING || snap.state == DCNOW_CONN_ONLINE || snap.state == DCNOW_CONN_COOLDOWN
        || snap.cooldown_seconds > 0) {
        return -1;
    }
    if (dev == DCNOW_DEV_NONE) {
        return -1;
    }
    finish_worker();

    take_isp(isp);

    active_device = dev;
    worker_job = JOB_CONNECT;
    cancel_requested = 0;
    status_reset(DCNOW_CONN_CONNECTING);
    worker = thd_create(false, worker_main, NULL);
    if (worker == NULL) {
        fail("Network did not start (net -1).", NULL);
        return -1;
    }
    return 0;
}

void
dcnow_conn_autostart(void) {
    dcnow_status_t snap;

    dcnow_conn_poll(&snap);
    if (snap.state != DCNOW_CONN_IDLE) {
        return;
    }
    finish_worker();
    worker_job = JOB_AUTOSTART;
    cancel_requested = 0;
    status_reset(DCNOW_CONN_CONNECTING);
    worker = thd_create(false, worker_main, NULL);
    if (worker == NULL) {
        fail("Network did not start (net -1).", NULL);
    }
}

int
dcnow_conn_isp(dcnow_isp_t* out) {
    int valid;

    mutex_lock(&status_mutex);
    valid = known_isp_valid;
    if (valid) {
        *out = known_isp;
    }
    mutex_unlock(&status_mutex);
    return valid;
}

void
dcnow_conn_cancel(void) {
    cancel_requested = 1;
}

void
dcnow_conn_disconnect(void) {
    dcnow_status_t snap;

    /* The window offers Disconnect only when online, so the join below
     * returns at once. */
    cancel_requested = 1;
    online_time_sync_abort();
    dcnow_fetch_clear();
    finish_worker();
    dcnow_conn_poll(&snap);
    /* The poll itself may have started the lost-link job. */
    finish_worker();
    dcnow_conn_poll(&snap);
    if (active_device == DCNOW_DEV_MODEM && snap.state == DCNOW_CONN_ONLINE) {
        status_reset(DCNOW_CONN_CONNECTING);
        mark_hanging_up();
        worker_job = JOB_HANGUP;
        worker = thd_create(false, worker_main, NULL);
        if (worker == NULL) {
            run_hangup();
        }
        return;
    }
    /* An adapter keeps its link and only forgets the status. Any other state
     * is a dial that failed or was canceled, and keeps its lines. */
    if (snap.state == DCNOW_CONN_ONLINE) {
        status_reset(DCNOW_CONN_IDLE);
    }
}

/* A dial past the point where Cancel is honored goes online first and is
 * hung up on the next frame. */
void
dcnow_conn_tick(void) {
    dcnow_status_t snap;

    if (sf_dcnow[0] != DCNOW_OFF) {
        return;
    }
    dcnow_conn_poll(&snap);
    if (snap.state == DCNOW_CONN_ONLINE) {
        dcnow_conn_disconnect();
    } else if (snap.state == DCNOW_CONN_CONNECTING && snap.can_cancel) {
        dcnow_conn_cancel();
    }
}

void
dcnow_conn_poll(dcnow_status_t* out) {
    uint64_t since;

    mutex_lock(&status_mutex);
    *out = status;
    since = hangup_time;
    mutex_unlock(&status_mutex);

    /* A modem link that lost its carrier is torn down on the worker. The
     * worker wrote active_device before it published ONLINE under the mutex,
     * and the copy above took that mutex, so the read here is ordered. */
    if (out->state == DCNOW_CONN_ONLINE && active_device == DCNOW_DEV_MODEM && !modem_is_connected()
        && worker_job != JOB_LOST) {
        finish_worker();
        worker_job = JOB_LOST;
        status_reset(DCNOW_CONN_CONNECTING);
        mark_hanging_up();
        out->state = DCNOW_CONN_CONNECTING;
        out->count = 0;
        out->counter_line = -1;
        out->hanging_up = 1;
        worker = thd_create(false, worker_main, NULL);
        if (worker == NULL) {
            run_lost();
            dcnow_conn_poll(out);
        }
    }

    /* The cooldown counts down on the caller's clock. After a hang-up the window
     * empties when it ends. After a failure or a cancel the lines stay. */
    out->cooldown_seconds = 0;
    if (since != 0) {
        uint64_t elapsed = timer_ms_gettime64() - since;
        if (elapsed < DCNOW_COOLDOWN_MS) {
            out->cooldown_seconds = (int)((DCNOW_COOLDOWN_MS - elapsed + 999) / 1000);
        } else {
            mutex_lock(&status_mutex);
            hangup_time = 0;
            mutex_unlock(&status_mutex);
            if (out->state == DCNOW_CONN_COOLDOWN) {
                status_reset(DCNOW_CONN_IDLE);
                out->state = DCNOW_CONN_IDLE;
                out->count = 0;
            }
        }
    }
}

int
dcnow_conn_address(uint8_t ip[4]) {
    if (net_default_dev == NULL || net_default_dev->ip_addr[0] == 0) {
        return 0;
    }
    memcpy(ip, net_default_dev->ip_addr, 4);
    return 1;
}

/* KOS compares a candidate port in host order against the ports it stored in
 * network order, so every unbound UDP socket lands on port 1024 while another
 * one is open, and the replies go to whichever socket comes first. */
static mutex_t udp_mutex = MUTEX_INITIALIZER;

void
dcnow_udp_lock(void) {
    mutex_lock(&udp_mutex);
}

void
dcnow_udp_unlock(void) {
    mutex_unlock(&udp_mutex);
}

#define DNS_PORT       53
#define DNS_ATTEMPTS   2
#define DNS_WAIT_MS    1000
#define DNS_POLL_MS    10
#define DNS_HEADER_LEN 12
#define DNS_NAME_MAX   255
#define DNS_REPLY_MAX  512
#define DNS_TYPE_A     1
#define DNS_CLASS_IN   1

/* Writes a query for the A record of host into out. Returns its length, or 0
 * when the name is not usable. */
static int
dns_build_query(const char* host, uint16_t id, uint8_t* out, size_t out_len) {
    size_t o = DNS_HEADER_LEN;
    const char* label = host;

    memset(out, 0, DNS_HEADER_LEN);
    out[0] = (uint8_t)(id >> 8);
    out[1] = (uint8_t)id;
    out[2] = 0x01; /* Recursion desired. */
    out[5] = 1;    /* One question. */
    for (;;) {
        const char* dot = strchr(label, '.');
        size_t len = dot != NULL ? (size_t)(dot - label) : strlen(label);

        if (len == 0 || len > 63 || o + 1 + len + 1 + 4 > out_len) {
            return 0;
        }
        out[o++] = (uint8_t)len;
        memcpy(out + o, label, len);
        o += len;
        if (dot == NULL) {
            break;
        }
        label = dot + 1;
    }
    out[o++] = 0;
    out[o++] = 0;
    out[o++] = DNS_TYPE_A;
    out[o++] = 0;
    out[o++] = DNS_CLASS_IN;
    return (int)o;
}

/* Steps over a name that may end in a compression pointer. Returns the offset
 * after it, or -1 when the packet ends first. */
static int
dns_skip_name(const uint8_t* p, int len, int o) {
    while (o < len) {
        int l = p[o];

        if (l == 0) {
            return o + 1;
        }
        if ((l & 0xC0) == 0xC0) {
            return o + 2 <= len ? o + 2 : -1;
        }
        o += 1 + l;
    }
    return -1;
}

/* Checks that the packet answers the query with the given id and copies its
 * first A record into ip. Returns 1 when found, 0 when the packet is not ours,
 * -1 when it is ours but carries an error or no address. */
static int
dns_parse_reply(const uint8_t* p, int len, uint16_t id, uint8_t ip[4]) {
    int questions;
    int answers;
    int o = DNS_HEADER_LEN;

    if (len < DNS_HEADER_LEN || ((p[0] << 8) | p[1]) != id || !(p[2] & 0x80)) {
        return 0;
    }
    if ((p[3] & 0x0F) != 0) {
        return -1;
    }
    questions = (p[4] << 8) | p[5];
    answers = (p[6] << 8) | p[7];
    for (int i = 0; i < questions; i++) {
        o = dns_skip_name(p, len, o);
        if (o < 0 || o + 4 > len) {
            return -1;
        }
        o += 4;
    }
    for (int i = 0; i < answers; i++) {
        int rtype;
        int rclass;
        int rdlen;

        o = dns_skip_name(p, len, o);
        if (o < 0 || o + 10 > len) {
            return -1;
        }
        rtype = (p[o] << 8) | p[o + 1];
        rclass = (p[o + 2] << 8) | p[o + 3];
        rdlen = (p[o + 8] << 8) | p[o + 9];
        o += 10;
        if (o + rdlen > len) {
            return -1;
        }
        if (rtype == DNS_TYPE_A && rclass == DNS_CLASS_IN && rdlen == 4) {
            memcpy(ip, p + o, 4);
            return 1;
        }
        o += rdlen;
    }
    return -1;
}

/* The KOS resolver never checks the transaction id and leaves the reply to
 * its repeated query on the wire, where the next lookup takes it as its own
 * answer. */
int
dcnow_resolve(const char* host, int port, struct sockaddr_in* out) {
    static uint16_t next_id = 1; /* guarded by udp_mutex */
    uint8_t query[DNS_HEADER_LEN + DNS_NAME_MAX + 4];
    uint8_t reply[DNS_REPLY_MAX];
    struct sockaddr_in server;
    uint8_t ip[4] = {0};
    uint16_t id;
    int query_len;
    int fd;
    int found = 0;
    int stop = 0;

    if (net_default_dev == NULL
        || (net_default_dev->dns[0] == 0 && net_default_dev->dns[1] == 0 && net_default_dev->dns[2] == 0
            && net_default_dev->dns[3] == 0)) {
        return 0;
    }
    memset(&server, 0, sizeof(server));
    server.sin_family = AF_INET;
    server.sin_port = htons(DNS_PORT);
    memcpy(&server.sin_addr, net_default_dev->dns, 4);

    mutex_lock(&udp_mutex);
    id = next_id++;
    query_len = dns_build_query(host, id, query, sizeof(query));
    fd = query_len > 0 ? socket(AF_INET, SOCK_DGRAM, 0) : -1;
    if (fd >= 0) {
        fcntl(fd, F_SETFL, O_NONBLOCK);
        /* Connected, so the stack drops anything not from the server. */
        if (connect(fd, (struct sockaddr*)&server, sizeof(server)) != 0) {
            stop = 1;
        }
        for (int attempt = 0; attempt < DNS_ATTEMPTS && !found && !stop; attempt++) {
            uint64_t deadline;

            if (send(fd, query, (size_t)query_len, 0) != (ssize_t)query_len) {
                break;
            }
            deadline = timer_ms_gettime64() + DNS_WAIT_MS;
            while (!found && !stop && timer_ms_gettime64() < deadline) {
                ssize_t n = recv(fd, reply, sizeof(reply), 0);
                int rc;

                if (n <= 0) {
                    thd_sleep(DNS_POLL_MS);
                    continue;
                }
                rc = dns_parse_reply(reply, (int)n, id, ip);
                if (rc == 1) {
                    found = 1;
                } else if (rc < 0) {
                    stop = 1;
                }
            }
        }
        close(fd);
    }
    mutex_unlock(&udp_mutex);

    if (found) {
        memset(out, 0, sizeof(*out));
        out->sin_family = AF_INET;
        out->sin_port = htons((uint16_t)port);
        memcpy(&out->sin_addr, ip, 4);
    }
    return found;
}

/* The name the Dreamcast Now! service maps to openMenu. It only has to be
 * looked up, not reached. */
#define DCNOW_PRESENCE_HOST "openmenu.dreamcastforever.com"

void
dcnow_presence_lookup(void) {
    struct sockaddr_in addr;

    dcnow_resolve(DCNOW_PRESENCE_HOST, 0, &addr);
}

static void (*hangup_hook)(void) = NULL;

void
dcnow_net_set_hangup_hook(void (*hook)(void)) {
    hangup_hook = hook;
}

void
dcnow_net_shutdown(void) {
    dcnow_status_t snap;

    dcnow_conn_poll(&snap);
    if (active_device == DCNOW_DEV_MODEM && (snap.state == DCNOW_CONN_ONLINE || snap.state == DCNOW_CONN_CONNECTING)) {
        dcnow_vmu_hanging_up();
        if (hangup_hook != NULL) {
            hangup_hook();
        }
    }
    /* Only the modem dial can be interrupted. A launch during the PPP phase
     * waits for the negotiation and the presence lookup to end. */
    cancel_requested = 1;
    /* A lost-link job joins the fetch and the time sync itself. */
    finish_worker();
    online_time_sync_abort();
    dcnow_fetch_abort();
    dcnow_conn_poll(&snap);
    /* The poll itself may have started the lost-link job. */
    finish_worker();
    if (active_device == DCNOW_DEV_MODEM && snap.state == DCNOW_CONN_ONLINE) {
        ppp_terminate();
        ppp_release();
        modem_hangup();
        net_default_dev = NULL;
    }
    if (net_started) {
        net_shutdown();
        net_started = 0;
    }
    if (active_device == DCNOW_DEV_BBA) {
        bba_shutdown();
    } else if (active_device == DCNOW_DEV_LAN) {
        la_shutdown();
    }
    if (modem_was_used) {
        /* modem_init() hooks the expansion port interrupt and modem_shutdown()
         * never unhooks it. The BIOS menu path skips the KOS shutdown, so the
         * event has to be disabled here. */
        asic_evt_disable(ASIC_EVT_EXP_8BIT, ASIC_IRQB);
        modem_was_used = 0;
    }
    status_reset(DCNOW_CONN_IDLE);
    dcnow_vmu_release();
}
