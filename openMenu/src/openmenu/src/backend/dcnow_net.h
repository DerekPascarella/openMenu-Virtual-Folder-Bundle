/*
 * File: dcnow_net.h
 * Project: openmenu
 * Dreamcast Now! networking: expansion device detection and ISP settings.
 */

#pragma once

#include <stdint.h>

typedef enum dcnow_device { DCNOW_DEV_NONE = 0, DCNOW_DEV_MODEM, DCNOW_DEV_BBA, DCNOW_DEV_LAN } dcnow_device_t;

/* Probes the expansion slot once per boot and remembers the answer. */
dcnow_device_t dcnow_detect_device(void);

/* Display name for the window, such as "Broadband Adapter (HIT-0400)". */
const char* dcnow_device_name(dcnow_device_t dev);

/* The console's network settings from the flash ROM. Only an adapter uses
 * them. The modem dials DreamPi with the fixed values below. */
typedef struct dcnow_isp {
    int ethernet_static; /* the console is set up with a fixed address */
    int pppoe;           /* the console is set up for PPPoE, which KOS cannot do */
    uint8_t ip[4];
    uint8_t netmask[4];
    uint8_t gateway[4];
    uint8_t dns[4];
} dcnow_isp_t;

/* What the modem dials. DreamPi answers any number and accepts any login. */
#define DCNOW_DIAL_NUMBER   "1111111"
#define DCNOW_DIAL_LOGIN    "openMenu"
#define DCNOW_DIAL_PASSWORD "openMenu"

/* Reads the console's adapter settings from the flash ROM. */
void dcnow_read_isp(dcnow_isp_t* out);

/* Connection engine. The window starts a connection, polls the status every
 * frame, and can cancel or hang up. Everything that blocks runs on a worker
 * thread inside this module. */
typedef enum dcnow_conn_state {
    DCNOW_CONN_IDLE = 0,
    DCNOW_CONN_CONNECTING,
    DCNOW_CONN_ONLINE,
    DCNOW_CONN_FAILED,
    DCNOW_CONN_CANCELED,
    DCNOW_CONN_COOLDOWN
} dcnow_conn_state_t;

#define DCNOW_STATUS_LINES 4
#define DCNOW_STATUS_WIDTH 48

typedef struct dcnow_status {
    dcnow_conn_state_t state;
    int can_cancel;         /* Cancel is honored right now */
    int cooldown_seconds;   /* seconds left before Connect or Retry returns, 0 when none */
    int counter_line;       /* line that shows a live "(N seconds)" counter, or -1 */
    uint64_t counter_since; /* timer_ms_gettime64() when that line started */
    char lines[DCNOW_STATUS_LINES][DCNOW_STATUS_WIDTH];
    int count;
    int hanging_up; /* a Disconnect or a lost carrier is being torn down */
} dcnow_status_t;

/* Starts the modem dial or the adapter bring-up on the worker thread.
 * Returns 0 when started, -1 when busy, already online, or nothing is fitted. */
int dcnow_conn_start(dcnow_device_t dev, const dcnow_isp_t* isp);

/* Probes the device and reads the ISP settings on the worker, then connects.
 * For the boot-time Auto-Connect, where no frame may stall. */
void dcnow_conn_autostart(void);

/* The probed device without probing: DCNOW_DEV_NONE until the probe has run. */
dcnow_device_t dcnow_device_hint(void);

/* The ISP settings the engine last connected with. Returns 0 before the first connection. */
int dcnow_conn_isp(dcnow_isp_t* out);

/* Stops a modem dial before the carrier is up. Ignored at any other time. */
void dcnow_conn_cancel(void);

/* Hangs up the modem on the worker and starts the DreamPi cooldown. On an
 * adapter it only clears the status and leaves the link up. */
void dcnow_conn_disconnect(void);

/* Once per frame from the main loop. Hangs up, or cancels a dial, once
 * Dreamcast Now! has been turned off. */
void dcnow_conn_tick(void);

/* Copies the current state and status lines for drawing. Main thread only:
 * on a lost modem carrier it also starts the lost-link worker, which the
 * caller's next join collects. */
void dcnow_conn_poll(dcnow_status_t* out);

/* The default interface's address once online. Returns 1 when there is one. */
int dcnow_conn_address(uint8_t ip[4]);

/* KOS gives every unbound UDP socket the same local port while another one is
 * open, so only one UDP socket may exist at a time. dcnow_resolve() takes this
 * lock itself. Anything else that opens a UDP socket holds it from socket() to
 * close(). A reply can also arrive after its socket has closed and land in the
 * next one, so no packet is trusted for arriving alone. The resolver checks the
 * transaction id and the time sync checks the NTP header. */
void dcnow_udp_lock(void);
void dcnow_udp_unlock(void);

/* Resolves a host name to an address with the port filled in. Returns 1 when
 * resolved, 0 when not. Sends its own query, once more after a second without
 * an answer, and accepts only a reply that carries its transaction id. Worker
 * threads only. */
struct sockaddr_in;
int dcnow_resolve(const char* host, int port, struct sockaddr_in* out);

/* Looks up the name Dreamcast Now! knows openMenu by and drops the answer.
 * The DreamPi reports the newest name the console looked up, so this runs
 * after the last other lookup of the same job. Worker threads only. */
void dcnow_presence_lookup(void);

/* Tears everything down. Called before every launch and exit path. */
void dcnow_net_shutdown(void);

/* Registers the hook the teardown calls before it hangs up a modem link,
 * so the screen can show the wait. */
void dcnow_net_set_hangup_hook(void (*hook)(void));
