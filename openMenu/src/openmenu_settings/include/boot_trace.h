/*
 * File: boot_trace.h
 * Project: openmenu_settings
 * Description: On-screen boot step log for diagnosing startup hangs
 */

#ifndef BOOT_TRACE_H
#define BOOT_TRACE_H

#include "openmenu_debug.h"

#if DEBUG_BOOT_TRACE
void boot_trace_begin(void);
void boot_trace_step(const char* name);
void boot_trace_done(const char* fmt, ...) __attribute__((format(printf, 1, 2)));
void boot_trace_end(void);

#define BT_STEP(name) boot_trace_step(name)
#define BT_DONE(...)  boot_trace_done(__VA_ARGS__)
#else
/* Takes the arguments so a result kept only for the trace is still used. */
static inline void
bt_done_off(const char* fmt, ...) {
    (void)fmt;
}

#define BT_STEP(name) ((void)0)
#define BT_DONE(...)  bt_done_off(__VA_ARGS__)
#endif

#endif /* BOOT_TRACE_H */
