/*
 * File: bgm.c
 * Project: backend
 * Description: Background music streaming from /cd/BGM.ADP
 *
 * The track is Yamaha AICA ADPCM behind a small "OMBG" header written by
 * GDMENUCardManager. The AICA decodes it in hardware, we just keep its
 * ring in sound RAM topped up. Between the disc and the sound chip sits a
 * 512 KB buffer in main RAM so box art loads can hog the GD drive for many
 * seconds without the music running dry. Everything here runs on the main
 * thread (the stream callback fires inside snd_stream_poll), so no locking.
 */

#include <stdint.h>
#include <string.h>

#include <dc/sound/sound.h>
#include <dc/sound/stream.h>
#include <kos.h>

#include <openmenu_settings.h>
#include "backend/bgm.h"

#define BGM_FILE         "/cd/BGM.ADP"
#define BGM_HEADER_LEN   32

/* About 12 to 16 seconds of stereo ADPCM depending on sample rate */
#define BGM_RING_SIZE    (512 << 10)

/* One disc read per frame at most, so a burst of refills never
 * costs more than one chunk of GD bandwidth per frame */
#define BGM_READ_CHUNK   (64 << 10)

/* KOS clamps ADPCM stream buffers to 32 KB and never asks the callback
 * for more than that in one go */
#define BGM_STAGING_SIZE (32 << 10)

static int bgm_ok = 0; /* valid BGM.ADP found at boot */
static int playing = 0;
static int snd_ready = 0;

static file_t bgm_fd = -1;
static snd_stream_hnd_t stream_hnd = SND_STREAM_INVALID;
static uint32_t payload_start = 0;
static uint32_t sample_rate = 0;
static int stereo = 0;

static uint8_t ring[BGM_RING_SIZE] __attribute__((aligned(32)));
static uint32_t ring_head = 0;  /* next write offset */
static uint32_t ring_tail = 0;  /* next read offset */
static uint32_t ring_level = 0; /* bytes currently buffered */

/* The stream callback must hand KOS one contiguous block, and the ring wraps,
 * so requests are bounced through here */
static uint8_t staging[BGM_STAGING_SIZE] __attribute__((aligned(32)));

/* Pulls one chunk from the disc into the ring. At end of file the track
 * wraps back to the payload start, which is also how looping works. */
static void
bgm_ring_fill_chunk(void) {
    if (BGM_RING_SIZE - ring_level < BGM_READ_CHUNK) {
        return;
    }

    /* only read up to the end of the ring, the wrap happens next call */
    uint32_t run = BGM_RING_SIZE - ring_head;
    if (run > BGM_READ_CHUNK) {
        run = BGM_READ_CHUNK;
    }

    ssize_t got = fs_read(bgm_fd, ring + ring_head, run);
    if (got == 0) {
        /* end of track, loop back to the payload start */
        fs_seek(bgm_fd, payload_start, SEEK_SET);
        return;
    }
    if (got < 0) {
        /* transient read error, try again next frame from the same spot */
        return;
    }

    ring_head = (ring_head + (uint32_t)got) % BGM_RING_SIZE;
    ring_level += (uint32_t)got;
}

static void*
bgm_stream_callback(snd_stream_hnd_t hnd, int bytes_req, int* bytes_recv) {
    (void)hnd;

    if (bytes_req > (int)sizeof(staging)) {
        bytes_req = (int)sizeof(staging);
    }

    uint32_t have = ring_level;
    if (have > (uint32_t)bytes_req) {
        have = (uint32_t)bytes_req;
    }

    uint32_t first = BGM_RING_SIZE - ring_tail;
    if (first > have) {
        first = have;
    }
    memcpy(staging, ring + ring_tail, first);
    memcpy(staging + first, ring, have - first);

    ring_tail = (ring_tail + have) % BGM_RING_SIZE;
    ring_level -= have;

    /* Starving here should be impossible with a full ring behind us, but
     * hand back silence rather than a short read so KOS stays in step */
    if (have < (uint32_t)bytes_req) {
        memset(staging + have, 0, (uint32_t)bytes_req - have);
    }

    *bytes_recv = bytes_req;
    return staging;
}

void
bgm_init(void) {
    uint8_t header[BGM_HEADER_LEN];

    bgm_fd = fs_open(BGM_FILE, O_RDONLY);
    if (bgm_fd == -1) {
        return;
    }

    ssize_t got = fs_read(bgm_fd, header, sizeof(header));
    if (got != (ssize_t)sizeof(header) || memcmp(header, "OMBG", 4) != 0) {
        fs_close(bgm_fd);
        bgm_fd = -1;
        return;
    }

    uint32_t version = header[0x04] | (header[0x05] << 8) | (header[0x06] << 16) | ((uint32_t)header[0x07] << 24);
    sample_rate = header[0x08] | (header[0x09] << 8) | (header[0x0A] << 16) | ((uint32_t)header[0x0B] << 24);
    uint16_t channels = header[0x0C] | (header[0x0D] << 8);

    /* 44.1 kHz is the most the AICA can play */
    if (version != 1 || sample_rate < 8000 || sample_rate > 44100 || (channels != 1 && channels != 2)) {
        fs_close(bgm_fd);
        bgm_fd = -1;
        return;
    }

    /* a header with no audio behind it is not a track */
    fs_seek(bgm_fd, 0, SEEK_END);
    if (fs_tell(bgm_fd) <= BGM_HEADER_LEN) {
        fs_close(bgm_fd);
        bgm_fd = -1;
        return;
    }
    fs_seek(bgm_fd, BGM_HEADER_LEN, SEEK_SET);

    stereo = (channels == 2);
    payload_start = BGM_HEADER_LEN;
    bgm_ok = 1;
}

int
bgm_available(void) {
    return bgm_ok;
}

static void
bgm_start(void) {
    if (!snd_ready) {
        if (snd_stream_init() < 0) {
            /* no sound system means no music, give up for this session */
            bgm_ok = 0;
            fs_close(bgm_fd);
            bgm_fd = -1;
            return;
        }
        snd_ready = 1;
    }

    if (stream_hnd == SND_STREAM_INVALID) {
        stream_hnd = snd_stream_alloc(bgm_stream_callback, BGM_STAGING_SIZE);
        if (stream_hnd == SND_STREAM_INVALID) {
            bgm_ok = 0;
            fs_close(bgm_fd);
            bgm_fd = -1;
            return;
        }
    }

    /* Prime just enough that the prefill inside stream start (64 KB for
     * stereo) always finds data. The per frame refill fills the rest of
     * the ring within a few frames. Capped in case the disc errors out. */
    int attempts = 8;
    while (ring_level < 2 * BGM_READ_CHUNK && attempts--) {
        bgm_ring_fill_chunk();
    }

    snd_stream_start_adpcm(stream_hnd, sample_rate, stereo);
    snd_stream_volume(stream_hnd, 255);
    playing = 1;
}

static void
bgm_pause(void) {
    /* Turning the setting back on restarts the track from the top,
     * so drop the buffered audio and rewind the file now */
    snd_stream_stop(stream_hnd);
    ring_head = 0;
    ring_tail = 0;
    ring_level = 0;
    fs_seek(bgm_fd, payload_start, SEEK_SET);
    playing = 0;
}

void
bgm_poll(void) {
    if (!bgm_ok) {
        return;
    }

    int want = (sf_music[0] == MUSIC_ON);
    if (want && !playing) {
        bgm_start();
    } else if (!want && playing) {
        bgm_pause();
    }

    if (playing) {
        bgm_ring_fill_chunk();
        snd_stream_poll(stream_hnd);
    }
}

void
bgm_shutdown(void) {
    if (stream_hnd != SND_STREAM_INVALID) {
        snd_stream_stop(stream_hnd);
        snd_stream_destroy(stream_hnd);
        stream_hnd = SND_STREAM_INVALID;
    }

    if (snd_ready) {
        snd_stream_shutdown();
        snd_shutdown();
        snd_ready = 0;
    }

    if (bgm_fd != -1) {
        fs_close(bgm_fd);
        bgm_fd = -1;
    }

    playing = 0;
    bgm_ok = 0;
}
