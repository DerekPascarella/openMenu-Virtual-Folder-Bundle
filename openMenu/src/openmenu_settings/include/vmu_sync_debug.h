#ifndef VMU_SYNC_DEBUG_H
#define VMU_SYNC_DEBUG_H

#include "openmenu_debug.h"

#if DEBUG_VMU_SYNC
#include <stdbool.h>
#include <stdint.h>

typedef struct {
    int port;
    int unit;
    uint32_t functions;
    char product[31];
    int result;
    int frame_state;
    uint32_t elapsed_ms;
    unsigned raw_size;
    uint8_t raw[1024];
} vmu_sync_sample_t;

extern vmu_sync_sample_t vmu_sync_samples[8];
extern unsigned vmu_sync_sample_count;
extern bool vmu_sync_debug_active;

int8_t vmu_sync_debug_query(void);
#endif

#endif
