/*
 * File: dcnow_fetch.h
 * Project: openmenu
 * Dreamcast Now! player list: HTTP fetch and JSON scan on a worker thread.
 */

#pragma once

#include <stdint.h>
#include <time.h>

#include "backend/dcnow_net.h"

#define DCNOW_PLAYER_MAX 128
#define DCNOW_NAME_LEN   32
#define DCNOW_TITLE_LEN  64

typedef struct dcnow_player {
    char name[DCNOW_NAME_LEN];
    char country[3];
    char title[DCNOW_TITLE_LEN];
    char code[16];
} dcnow_player_t;

typedef enum dcnow_fetch_state {
    DCNOW_FETCH_IDLE = 0,
    DCNOW_FETCH_RUNNING,
    DCNOW_FETCH_DONE,
    DCNOW_FETCH_FAILED
} dcnow_fetch_state_t;

typedef struct dcnow_fetch_status {
    dcnow_fetch_state_t state;
    int counter_line;       /* line with a live "(waiting N seconds)" counter, or -1 */
    uint64_t counter_since; /* when that counter started */
    char lines[DCNOW_STATUS_LINES][DCNOW_STATUS_WIDTH];
    int count;
    int generation;   /* grows by one for every list that arrived */
    int player_count; /* online players in the newest list */
    int online_total; /* online players in the feed, the list keeps at most DCNOW_PLAYER_MAX */
    time_t updated;   /* console clock when the newest list arrived */
    int list_valid;   /* a list is in the store, kept until the store is cleared */
} dcnow_fetch_status_t;

/* Starts a fetch on the worker. Does nothing while one is running. */
void dcnow_fetch_start(void);

/* Stops a running fetch and waits for the worker. The last list is kept. */
void dcnow_fetch_abort(void);

/* Forgets the list and the status, used when the connection goes away. */
void dcnow_fetch_clear(void);

/* Snapshot for the window, once per frame. */
void dcnow_fetch_poll(dcnow_fetch_status_t* out);

/* Copies the newest list. Returns how many players were copied. */
int dcnow_fetch_copy(dcnow_player_t* out, int max);
