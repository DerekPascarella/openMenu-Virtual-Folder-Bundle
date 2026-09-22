#include "vmu_sync_debug.h"

#if DEBUG_VMU_SYNC
#include <arch/irq.h>
#include <arch/timer.h>
#include <dc/maple.h>
#include <kos/genwait.h>
#include <string.h>

vmu_sync_sample_t vmu_sync_samples[8];
unsigned vmu_sync_sample_count;
bool vmu_sync_debug_active;

static vmu_sync_sample_t* pending_sample;

static void
clock_reply(maple_state_t* state, maple_frame_t* frame) {
    (void)state;
    vmu_sync_sample_t* sample = pending_sample;

    /* A hardware no-response marker has no valid length byte. */
    sample->raw_size = frame->recv_buf[0] == 0xff ? 4 : 4 + 4 * (unsigned)frame->recv_buf[3];
    memcpy(sample->raw, frame->recv_buf, sample->raw_size);
    sample->frame_state = frame->state;
    genwait_wake_all(frame);
}

int8_t
vmu_sync_debug_query(void) {
    vmu_sync_debug_active = false;
    vmu_sync_sample_count = 0;
    memset(vmu_sync_samples, 0, sizeof(vmu_sync_samples));

    for (unsigned i = 0; i < 8; i++) {
        maple_device_t* dev = maple_enum_type(i, MAPLE_FUNC_MEMCARD);
        if (!dev || !dev->valid) {
            continue;
        }
        vmu_sync_sample_t* sample = &vmu_sync_samples[vmu_sync_sample_count++];
        sample->port = dev->port;
        sample->unit = dev->unit;
        sample->functions = dev->info.functions;
        memcpy(sample->product, dev->info.product_name, 30);
        sample->product[30] = '\0';
        sample->frame_state = dev->frame.state;

        /* Keep the production driver's single lock attempt for comparison. */
        if (maple_frame_lock(&dev->frame) < 0) {
            sample->result = MAPLE_EAGAIN;
            continue;
        }
        maple_frame_init(&dev->frame);
        /* KOS reuses this payload if the device requests a retry. */
        uint32_t send[2] = {MAPLE_FUNC_CLOCK, 0};
        dev->frame.cmd = MAPLE_COMMAND_BREAD;
        dev->frame.dst_port = dev->port;
        dev->frame.dst_unit = dev->unit;
        dev->frame.length = 2;
        dev->frame.callback = clock_reply;
        dev->frame.send_buf = send;
        pending_sample = sample;
        uint64_t started = timer_ms_gettime64();
        maple_queue_frame(&dev->frame);
        int waited = genwait_wait(&dev->frame, "vmu_clock_probe", 10000, NULL);

        uint32_t irq = irq_disable();
        sample->elapsed_ms = (uint32_t)(timer_ms_gettime64() - started);
        sample->frame_state = dev->frame.state;
        if (sample->raw_size != 0) {
            sample->result = MAPLE_EOK;
            maple_frame_unlock(&dev->frame);
        } else {
            sample->result = waited < 0 ? MAPLE_ETIMEOUT : MAPLE_EFAIL;
            maple_queue_remove(&dev->frame);
            dev->frame.callback = NULL;
            dev->frame.state = MAPLE_FRAME_VACANT;
        }
        pending_sample = NULL;
        irq_restore(irq);
    }

    vmu_sync_debug_active = true;
    /* This probe only reads devices. It does not synchronize the RTC. */
    return -1;
}
#endif
