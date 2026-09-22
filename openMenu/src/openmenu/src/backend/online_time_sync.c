/*
 * File: online_time_sync.c
 * Project: openmenu
 * Online Time Sync: sets the console clock from pool.ntp.org once the
 * console is online.
 */

#include <fcntl.h>
#include <poll.h>
#include <stdbool.h>
#include <stdint.h>
#include <string.h>
#include <time.h>
#include <unistd.h>

#include <arch/timer.h>
#include <kos/mutex.h>
#include <kos/thread.h>
#include <netinet/in.h>
#include <openmenu_savefile.h>
#include <openmenu_settings.h>
#include <sys/socket.h>

#include "backend/dcnow_net.h"
#include "backend/online_time_sync.h"

#define NTP_HOST        "pool.ntp.org"
#define NTP_PORT        123
#define NTP_TRIES       3
#define NTP_WAIT_MS     3000
#define SYNC_ATTEMPTS   5
#define SYNC_RETRY_MS   30000
#define NTP_UNIX_OFFSET 2208988800u /* Seconds from 1900 to 1970. */
#define NTP_UTC_FLOOR   1767225600  /* 2026-01-01. An answer before this means a broken server. */

typedef enum sync_state { SYNC_IDLE = 0, SYNC_RUNNING, SYNC_DONE, SYNC_FAILED } sync_state_t;

static mutex_t sync_mutex = MUTEX_INITIALIZER;
static sync_state_t sync_state = SYNC_IDLE; /* guarded by sync_mutex */
static time_t sync_utc = 0;                 /* guarded by sync_mutex */
static kthread_t* sync_thread = NULL;       /* guarded by sync_mutex */
static volatile int abort_requested = 0;

/* Up to SYNC_ATTEMPTS requests per connection, SYNC_RETRY_MS apart. A clock
 * that was set stays set for the session unless the setting changes. */
static int attempts = 0;      /* requests started on this connection */
static uint64_t retry_at = 0; /* earliest time for the next request */
static bool done = false;
static int seen_setting = -1; /* the setting value the counters belong to */

static void
set_state(sync_state_t state, time_t utc) {
    mutex_lock(&sync_mutex);
    sync_state = state;
    sync_utc = utc;
    mutex_unlock(&sync_mutex);
}

/* Sends one request and waits for the answer. Returns 1 with the server's
 * transmit time in *utc, 0 on a timeout or a bad packet. */
static int
exchange(int fd, const struct sockaddr_in* addr, time_t* utc) {
    uint8_t packet[48];
    struct sockaddr_in from;
    socklen_t from_len;
    struct pollfd pfd;
    uint64_t deadline;
    uint32_t secs;

    memset(packet, 0, sizeof(packet));
    packet[0] = 0x23; /* No leap warning, version 4, client mode. */
    if (sendto(fd, packet, sizeof(packet), 0, (const struct sockaddr*)addr, sizeof(*addr)) != (ssize_t)sizeof(packet)) {
        return 0;
    }
    deadline = timer_ms_gettime64() + NTP_WAIT_MS;
    while (timer_ms_gettime64() < deadline && !abort_requested) {
        pfd.fd = fd;
        pfd.events = POLLRDNORM;
        pfd.revents = 0;
        if (poll(&pfd, 1, 100) <= 0) {
            continue;
        }
        from_len = sizeof(from);
        if (recvfrom(fd, packet, sizeof(packet), 0, (struct sockaddr*)&from, &from_len) != (ssize_t)sizeof(packet)) {
            continue;
        }
        /* Only a server answer with a real stratum counts. Stratum 0 is the
         * "kiss of death" a busy server sends instead of a time. */
        if ((packet[0] & 0x07) != 4 || packet[1] == 0 || packet[1] > 15) {
            continue;
        }
        secs = ((uint32_t)packet[40] << 24) | ((uint32_t)packet[41] << 16) | ((uint32_t)packet[42] << 8) | packet[43];
        if (secs == 0) {
            continue;
        }
        /* NTP seconds wrap in 2036. A value below the 1970 mark is from the
         * era after the wrap. */
        if (secs >= NTP_UNIX_OFFSET) {
            *utc = (time_t)(secs - NTP_UNIX_OFFSET);
        } else {
            *utc = (time_t)secs + (time_t)4294967296LL - (time_t)NTP_UNIX_OFFSET;
        }
        if (*utc < NTP_UTC_FLOOR) {
            continue;
        }
        return 1;
    }
    return 0;
}

static void*
sync_main(void* param) {
    struct sockaddr_in addr;
    time_t utc = 0;
    int fd;
    int ok = 0;

    (void)param;
    if (!dcnow_resolve(NTP_HOST, NTP_PORT, &addr)) {
        /* Skipped on an abort so the join keeps its two-second bound. */
        if (!abort_requested) {
            dcnow_presence_lookup();
        }
        set_state(SYNC_FAILED, 0);
        return NULL;
    }
    dcnow_udp_lock();
    fd = socket(AF_INET, SOCK_DGRAM, 0);
    if (fd < 0) {
        dcnow_udp_unlock();
        set_state(SYNC_FAILED, 0);
        return NULL;
    }
    fcntl(fd, F_SETFL, O_NONBLOCK);
    for (int i = 0; i < NTP_TRIES && !ok && !abort_requested; i++) {
        ok = exchange(fd, &addr, &utc);
    }
    close(fd);
    dcnow_udp_unlock();
    if (!abort_requested) {
        dcnow_presence_lookup();
    }
    set_state(ok ? SYNC_DONE : SYNC_FAILED, utc);
    return NULL;
}

/* Whoever takes the thread pointer joins it, so the main loop and the
 * connection engine's worker can both call this. */
static void
join_thread(void) {
    kthread_t* thread;

    mutex_lock(&sync_mutex);
    thread = sync_thread;
    sync_thread = NULL;
    mutex_unlock(&sync_mutex);
    if (thread != NULL) {
        thd_join(thread, NULL);
    }
}

/* Nothing may look for the thread pointer before it is stored, neither the
 * new thread nor an abort from the worker, so the mutex stays held across
 * the create. */
static void
start_request(void) {
    abort_requested = 0;
    mutex_lock(&sync_mutex);
    sync_utc = 0;
    sync_thread = thd_create(false, sync_main, NULL);
    sync_state = sync_thread != NULL ? SYNC_RUNNING : SYNC_FAILED;
    mutex_unlock(&sync_mutex);
}

void
online_time_sync_tick(void) {
    dcnow_status_t status;
    sync_state_t state;
    time_t utc;
    int setting = sf_online_time_sync[0];

    dcnow_conn_poll(&status);
    if (status.state != DCNOW_CONN_ONLINE) {
        attempts = 0;
        retry_at = 0;
    }
    if (setting != seen_setting) {
        seen_setting = setting;
        done = false;
        attempts = 0;
        retry_at = 0;
    }

    mutex_lock(&sync_mutex);
    state = sync_state;
    utc = sync_utc;
    mutex_unlock(&sync_mutex);
    if (state == SYNC_DONE || state == SYNC_FAILED) {
        join_thread();
        set_state(SYNC_IDLE, 0);
        if (state == SYNC_DONE && setting != ONLINE_TIME_SYNC_OFF && sf_dcnow[0] != DCNOW_OFF) {
            time_t local = utc + (time_t)online_time_sync_minutes(setting) * 60;
            if (set_rtc_and_syscfg(local) == 0) {
                done = true;
                return;
            }
        }
        retry_at = timer_ms_gettime64() + SYNC_RETRY_MS;
        return;
    }
    if (state == SYNC_RUNNING || done || attempts >= SYNC_ATTEMPTS || timer_ms_gettime64() < retry_at) {
        return;
    }
    if (sf_dcnow[0] == DCNOW_OFF || setting == ONLINE_TIME_SYNC_OFF || status.state != DCNOW_CONN_ONLINE) {
        return;
    }
    attempts++;
    start_request();
}

void
online_time_sync_abort(void) {
    abort_requested = 1;
    join_thread();
    set_state(SYNC_IDLE, 0);
}
