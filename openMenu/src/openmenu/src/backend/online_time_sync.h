/*
 * File: online_time_sync.h
 * Project: openmenu
 * Online Time Sync: sets the console clock from pool.ntp.org once the
 * console is online.
 */

#pragma once

/* Once per frame from the main loop. Starts a request when the settings allow
 * one and the console is online, and applies the answer when it arrives. */
void online_time_sync_tick(void);

/* Stops a request in progress and waits for its thread. A resolver call
 * cannot be interrupted, so this can take up to two seconds. Call it before
 * the fetch's abort, since the fetch may be waiting on the UDP lock the time
 * sync holds. */
void online_time_sync_abort(void);
