/*
 * File: sd_savefile.c
 * Project: openmenu_settings
 * Description: Serial SD card save/load support for openMenu settings
 */

#ifdef _arch_dreamcast

#include <errno.h>
#include <fcntl.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/stat.h>
#include <unistd.h>

#include <dc/sd.h>
#include <fat/fs_fat.h>
#include <kos/blockdev.h>

#include "openmenu_settings.h"
#include "sd_savefile.h"

/* State tracking */
static bool sd_initialized = false;
static bool sd_mounted = false;
static kos_blockdev_t sd_dev;
static uint8_t sd_partition_type;

/* Cached status */
static SD_STATUS sd_cached_status = SD_STATUS_NOT_PRESENT;
static uint32_t sd_cached_version = 0;

/* One entry per setting, in the same order as the crayon registrations in
 * setup_savefile(). A mismatch here silently corrupts every saved setting. */
typedef struct sd_var_entry {
    uint8_t* var_ptr;       /* Pointer to sf_* variable */
    size_t size;            /* Size in bytes */
    uint32_t introduced_in; /* SFV_* version when added */
} sd_var_entry_t;

static sd_var_entry_t sd_variables[] = {
    {NULL, 1, SFV_INITIAL},              /* sf_region */
    {NULL, 1, SFV_INITIAL},              /* sf_aspect */
    {NULL, 1, SFV_INITIAL},              /* sf_ui */
    {NULL, 1, SFV_INITIAL},              /* sf_sort */
    {NULL, 1, SFV_INITIAL},              /* sf_filter */
    {NULL, 1, SFV_INITIAL},              /* sf_beep */
    {NULL, 1, SFV_INITIAL},              /* sf_multidisc */
    {NULL, 1, SFV_INITIAL},              /* sf_custom_theme */
    {NULL, 1, SFV_INITIAL},              /* sf_custom_theme_num */
    {NULL, 1, SFV_BIOS_3D},              /* sf_bios_3d */
    {NULL, 1, SFV_SCROLL_ART},           /* sf_scroll_art */
    {NULL, 1, SFV_SCROLL_INDEX},         /* sf_scroll_index */
    {NULL, 1, SFV_FOLDERS_ART},          /* sf_folders_art */
    {NULL, 1, SFV_MARQUEE_SPEED},        /* sf_marquee_speed */
    {NULL, 1, SFV_DISC_DETAILS},         /* sf_disc_details */
    {NULL, 1, SFV_FOLDERS_ITEM_DETAILS}, /* sf_folders_item_details */
    {NULL, 1, SFV_CLOCK},                /* sf_clock */
    {NULL, 1, SFV_MULTIDISC_GROUPING},   /* sf_multidisc_grouping */
    {NULL, 1, SFV_VM2_SEND_ALL},         /* sf_vm2_send_all */
    {NULL, 1, SFV_BOOT_MODE},            /* sf_boot_mode */
    {NULL, 1, SFV_VMU_TIME_SYNC},        /* sf_vmu_time_sync */
    {NULL, 1, SFV_SERIAL_VMU},           /* sf_serial_vmu */
    {NULL, 1, SFV_SERIAL_VMU_MULTISLOT}, /* sf_serial_vmu_multislot */
    {NULL, 1, SFV_FOLDER_ART},           /* sf_folder_art */
    {NULL, 1, SFV_MUSIC},                /* sf_music */
    {NULL, 1, SFV_HONOR_DEFAULTS},       /* sf_honor_defaults */
    {NULL, 1, SFV_RECENTLY_PLAYED},      /* sf_recently_played */
    {NULL, 400, SFV_RECENTLY_PLAYED},    /* sf_recent_games (100 slots x 4 bytes) */
    {NULL, 1, SFV_REMEMBER_LAST_GAME},   /* sf_remember_last_game */
    {NULL, 4, SFV_REMEMBER_LAST_GAME},   /* sf_last_game (4 x uint8) */
    {NULL, 12, SFV_REMEMBER_LAST_GAME},  /* sf_last_game_product */
    {NULL, 4, SFV_REMEMBER_LAST_GAME},   /* sf_last_game_folder (4 x uint8) */
    {NULL, 4, SFV_REMEMBER_LAST_GAME},   /* sf_last_game_filter (4 x uint8) */
};
#define SD_VAR_COUNT (sizeof(sd_variables) / sizeof(sd_variables[0]))

/* Called once from sd_savefile_init. */
static void
sd_init_var_pointers(void) {
    sd_variables[0].var_ptr = sf_region;
    sd_variables[1].var_ptr = sf_aspect;
    sd_variables[2].var_ptr = sf_ui;
    sd_variables[3].var_ptr = sf_sort;
    sd_variables[4].var_ptr = sf_filter;
    sd_variables[5].var_ptr = sf_beep;
    sd_variables[6].var_ptr = sf_multidisc;
    sd_variables[7].var_ptr = sf_custom_theme;
    sd_variables[8].var_ptr = sf_custom_theme_num;
    sd_variables[9].var_ptr = sf_bios_3d;
    sd_variables[10].var_ptr = sf_scroll_art;
    sd_variables[11].var_ptr = sf_scroll_index;
    sd_variables[12].var_ptr = sf_folders_art;
    sd_variables[13].var_ptr = sf_marquee_speed;
    sd_variables[14].var_ptr = sf_disc_details;
    sd_variables[15].var_ptr = sf_folders_item_details;
    sd_variables[16].var_ptr = sf_clock;
    sd_variables[17].var_ptr = sf_multidisc_grouping;
    sd_variables[18].var_ptr = sf_vm2_send_all;
    sd_variables[19].var_ptr = sf_boot_mode;
    sd_variables[20].var_ptr = sf_vmu_time_sync;
    sd_variables[21].var_ptr = sf_serial_vmu;
    sd_variables[22].var_ptr = sf_serial_vmu_multislot;
    sd_variables[23].var_ptr = sf_folder_art;
    sd_variables[24].var_ptr = sf_music;
    sd_variables[25].var_ptr = sf_honor_defaults;
    sd_variables[26].var_ptr = sf_recently_played;
    sd_variables[27].var_ptr = sf_recent_games;
    sd_variables[28].var_ptr = sf_remember_last_game;
    sd_variables[29].var_ptr = sf_last_game;
    sd_variables[30].var_ptr = sf_last_game_product;
    sd_variables[31].var_ptr = sf_last_game_folder;
    sd_variables[32].var_ptr = sf_last_game_filter;
}

/* Calculate total data size for current version */
static size_t
sd_calculate_data_size(void) {
    size_t total = 0;
    for (size_t i = 0; i < SD_VAR_COUNT; i++) {
        total += sd_variables[i].size;
    }
    return total;
}

/* Calculate data size for a specific version */
static size_t
sd_calculate_data_size_for_version(uint32_t version) {
    size_t total = 0;
    for (size_t i = 0; i < SD_VAR_COUNT; i++) {
        if (sd_variables[i].introduced_in <= version) {
            total += sd_variables[i].size;
        }
    }
    return total;
}

/* Simple checksum calculation with rotation */
static uint32_t
calculate_checksum(const uint8_t* data, size_t len) {
    uint32_t sum = 0;
    for (size_t i = 0; i < len; i++) {
        sum += data[i];
        sum = (sum << 1) | (sum >> 31); /* Rotate left */
    }
    return sum;
}

/* Returns 0 when the directory already exists. */
static int
ensure_directory_exists(const char* path) {
    struct stat st;
    if (stat(path, &st) == 0) {
        return 0; /* Already exists */
    }
    return mkdir(path, 0755);
}

int8_t
sd_savefile_init(void) {
    int ret;

    if (sd_initialized) {
        return sd_mounted ? 0 : -1;
    }

    sd_init_var_pointers();

    if (fs_fat_init() != 0) {
        sd_cached_status = SD_STATUS_NOT_PRESENT;
        return -1;
    }

    if (sd_init() != 0) {
        fs_fat_shutdown();
        sd_cached_status = SD_STATUS_NOT_PRESENT;
        return -1;
    }

    sd_initialized = true;

    if (sd_blockdev_for_partition(0, &sd_dev, &sd_partition_type) != 0) {
        sd_shutdown();
        fs_fat_shutdown();
        sd_initialized = false;
        sd_cached_status = SD_STATUS_NOT_PRESENT;
        return -1;
    }

    ret = fs_fat_mount(SD_MOUNT_PATH, &sd_dev, FS_FAT_MOUNT_READWRITE);
    if (ret != 0) {
        sd_shutdown();
        fs_fat_shutdown();
        sd_initialized = false;
        sd_cached_status = SD_STATUS_NOT_PRESENT;
        return -1;
    }

    sd_mounted = true;

    sd_savefile_refresh_status();

    return 0;
}

void
sd_savefile_shutdown(void) {
    if (sd_mounted) {
        fs_fat_sync(SD_MOUNT_PATH);
        fs_fat_unmount(SD_MOUNT_PATH);
        sd_mounted = false;
    }

    if (sd_initialized) {
        sd_shutdown();
        fs_fat_shutdown();
        sd_initialized = false;
    }

    sd_cached_status = SD_STATUS_NOT_PRESENT;
    sd_cached_version = 0;
}

bool
sd_savefile_available(void) {
    return sd_initialized && sd_mounted;
}

SD_STATUS
sd_savefile_get_status(void) { return sd_cached_status; }

uint32_t
sd_savefile_get_version(void) {
    return sd_cached_version;
}

void
sd_savefile_refresh_status(void) {
    if (!sd_mounted) {
        sd_cached_status = SD_STATUS_NOT_PRESENT;
        sd_cached_version = 0;
        return;
    }

    /* Remember previous status - if it was known-good (READY/OLD),
     * we'll retry on transient read errors before updating status */
    SD_STATUS prev_status = sd_cached_status;
    bool was_known_good = (prev_status == SD_STATUS_READY || prev_status == SD_STATUS_OLD);

    /* Retry up to 3 times if we had a known-good status (transient serial errors) */
    int max_attempts = was_known_good ? 3 : 1;

    for (int attempt = 0; attempt < max_attempts; attempt++) {
        int fd = open(SD_CONFIG_FILE, O_RDONLY);
        if (fd < 0) {
            if (attempt < max_attempts - 1) {
                continue; /* Retry */
            }
            /* Final attempt failed - can't open file */
            sd_cached_status = SD_STATUS_NO_FILE;
            sd_cached_version = 0;
            return;
        }

        /* Read header to get version and validate */
        sd_config_header_t header;
        ssize_t bytes_read = read(fd, &header, sizeof(header));
        close(fd);

        if (bytes_read < 0 || bytes_read != (ssize_t)sizeof(header)) {
            if (attempt < max_attempts - 1) {
                continue; /* Retry */
            }
            /* Final attempt failed - if previous status was known-good,
             * keep old status. Otherwise mark as invalid. */
            if (!was_known_good) {
                sd_cached_status = SD_STATUS_INVALID;
                sd_cached_version = 0;
            }
            return;
        }

        if (header.magic[0] != 'O' || header.magic[1] != 'M' || header.magic[2] != 'C' || header.magic[3] != 'F') {
            if (attempt < max_attempts - 1) {
                continue; /* Retry - might be transient serial read error */
            }
            /* Final attempt failed - if previous status was known-good,
             * keep old status (transient error). Otherwise mark as invalid. */
            if (!was_known_good) {
                sd_cached_status = SD_STATUS_INVALID;
                sd_cached_version = 0;
            }
            return;
        }

        /* Successfully read and validated header */
        sd_cached_version = header.version;

        if (header.version == SFV_CURRENT) {
            sd_cached_status = SD_STATUS_READY;
        } else if (header.version < SFV_CURRENT) {
            sd_cached_status = SD_STATUS_OLD;
        } else {
            sd_cached_status = SD_STATUS_FUTURE;
        }
        return; /* Success - exit loop */
    }
}

int8_t
sd_savefile_load(void) {
    if (!sd_mounted) {
        return -1;
    }

    /* Retry up to 3 times to handle transient serial read errors */
    int max_attempts = 3;

    for (int attempt = 0; attempt < max_attempts; attempt++) {
        int fd = open(SD_CONFIG_FILE, O_RDONLY);
        if (fd < 0) {
            if (attempt < max_attempts - 1) {
                continue; /* Retry */
            }
            return -1;
        }

        /* Read header */
        sd_config_header_t header;
        ssize_t hdr_bytes = read(fd, &header, sizeof(header));
        if (hdr_bytes < 0 || hdr_bytes != (ssize_t)sizeof(header)) {
            close(fd);
            if (attempt < max_attempts - 1) {
                continue; /* Retry */
            }
            return -1;
        }

        /* Validate magic */
        if (header.magic[0] != 'O' || header.magic[1] != 'M' || header.magic[2] != 'C' || header.magic[3] != 'F') {
            close(fd);
            if (attempt < max_attempts - 1) {
                continue; /* Retry - might be transient read error */
            }
            return -1;
        }

        /* Validate data size against expected size for that version */
        size_t expected_size = sd_calculate_data_size_for_version(header.version);
        if (header.version > SFV_CURRENT) {
            /* Future version may have extra data - just need at least our known vars */
            if (header.data_size < expected_size) {
                close(fd);
                if (attempt < max_attempts - 1) {
                    continue;
                }
                return -1;
            }
        } else {
            if (header.data_size != expected_size) {
                close(fd);
                if (attempt < max_attempts - 1) {
                    continue; /* Retry - might be transient read error */
                }
                return -1;
            }
        }

        /* Allocate buffer for settings data */
        uint8_t* data = malloc(header.data_size);
        if (!data) {
            close(fd);
            return -1; /* Memory allocation failure - don't retry */
        }

        /* Read settings data */
        if (read(fd, data, header.data_size) != (ssize_t)header.data_size) {
            free(data);
            close(fd);
            if (attempt < max_attempts - 1) {
                continue; /* Retry */
            }
            return -1;
        }
        close(fd);

        /* Validate checksum */
        uint32_t calc_checksum = calculate_checksum(data, header.data_size);
        if (calc_checksum != header.checksum) {
            free(data);
            if (attempt < max_attempts - 1) {
                continue; /* Retry - might be transient read error */
            }
            return -1;
        }

        /* Apply settings from data buffer using variable table */
        size_t offset = 0;
        for (size_t i = 0; i < SD_VAR_COUNT; i++) {
            /* Only load variables that existed in the saved version */
            if (sd_variables[i].introduced_in <= header.version) {
                if (offset + sd_variables[i].size <= header.data_size) {
                    memcpy(sd_variables[i].var_ptr, &data[offset], sd_variables[i].size);
                    offset += sd_variables[i].size;
                }
            }
        }

        free(data);

        /* Reset obsoleted sf_bios_3d for pre-SFV_EXIT_BIOS saves */
        if (header.version < SFV_EXIT_BIOS) {
            sf_bios_3d[0] = BIOS_3D_STANDARD;
        }

        /* A config written before the setting existed says nothing about
         * it, so drop whatever this session left in memory instead of
         * carrying it into the loaded config */
        if (header.version < SFV_REMEMBER_LAST_GAME) {
            sf_remember_last_game[0] = REMEMBER_LAST_GAME_OFF;
            memset(sf_last_game, 0, sf_last_game_length);
            memset(sf_last_game_product, 0, sf_last_game_product_length);
            memset(sf_last_game_folder, 0, sf_last_game_folder_length);
            memset(sf_last_game_filter, 0, sf_last_game_filter_length);
        }

        /* Let settings_sanitize() handle defaults for any new variables */
        settings_sanitize();

        sd_cached_version = header.version;
        if (header.version > SFV_CURRENT) {
            sd_cached_status = SD_STATUS_FUTURE;
        } else {
            sd_cached_status = (header.version == SFV_CURRENT) ? SD_STATUS_READY : SD_STATUS_OLD;
        }

        return 0; /* Success */
    }

    return -1; /* All attempts failed */
}

int8_t
sd_savefile_save(void) {
    if (!sd_mounted) {
        return -1;
    }

    settings_sanitize();

    /* Ensure OPENMENU directory exists */
    if (ensure_directory_exists(SD_OPENMENU_DIR) != 0) {
        /* Directory might already exist, try to continue */
    }

    /* Calculate data size and allocate buffer */
    size_t data_size = sd_calculate_data_size();
    uint8_t* data = malloc(data_size);
    if (!data) {
        return -1;
    }

    /* Build settings data buffer using variable table */
    size_t offset = 0;
    for (size_t i = 0; i < SD_VAR_COUNT; i++) {
        memcpy(&data[offset], sd_variables[i].var_ptr, sd_variables[i].size);
        offset += sd_variables[i].size;
    }

    /* Build header */
    sd_config_header_t header;
    memcpy(header.magic, SD_CONFIG_MAGIC, SD_CONFIG_MAGIC_LEN);
    header.version = SFV_CURRENT;
    header.data_size = data_size;
    header.checksum = calculate_checksum(data, data_size);

    /* Write to file */
    int fd = open(SD_CONFIG_FILE, O_WRONLY | O_CREAT | O_TRUNC, 0644);
    if (fd < 0) {
        free(data);
        return -1;
    }

    ssize_t written = write(fd, &header, sizeof(header));
    if (written < 0 || written != (ssize_t)sizeof(header)) {
        close(fd);
        free(data);
        return -1;
    }

    written = write(fd, data, data_size);
    if (written < 0 || written != (ssize_t)data_size) {
        close(fd);
        free(data);
        return -1;
    }

    /* Sync before closing to ensure data is written */
    fs_fat_sync(SD_MOUNT_PATH);
    close(fd);
    free(data);

    sd_cached_version = SFV_CURRENT;
    sd_cached_status = SD_STATUS_READY;

    return 0;
}

#endif /* _arch_dreamcast */
